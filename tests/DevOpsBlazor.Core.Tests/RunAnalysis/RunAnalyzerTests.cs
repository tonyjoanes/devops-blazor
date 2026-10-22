using DevOpsBlazor.Core.Models;
using DevOpsBlazor.Core.RunAnalysis;
using static DevOpsBlazor.Core.Tests.TestData;

namespace DevOpsBlazor.Core.Tests.RunAnalysis;

public class RunAnalyzerTests
{
    private static readonly RunLabelOptions Options = new();

    private static PipelineHealth Analyze(params PipelineRun[] runs) => RunAnalyzer.Analyze(Definition(), runs, Options, Now);

    private static ProblemLabel? Find(PipelineHealth health, LabelKind kind) => health.Labels.SingleOrDefault(l => l.Kind == kind);

    [Fact]
    public void Broken_when_latest_default_branch_run_failed_and_counts_the_streak()
    {
        var health = Analyze(
            Run(RunResult.Succeeded, 10),
            Run(RunResult.Failed, 8, commit: "abcdef1234567"),
            Run(RunResult.Failed, 6),
            Run(RunResult.Succeeded, 5, branch: "refs/heads/feature/x"),
            Run(RunResult.Failed, 4));

        var broken = Find(health, LabelKind.Broken);
        Assert.NotNull(broken);
        Assert.Equal(LabelSeverity.High, broken.Severity);
        Assert.Contains("the last 3 runs of main", broken.Evidence);
        Assert.Contains("commit abcdef1", broken.Evidence);
    }

    [Fact]
    public void Not_broken_when_latest_default_branch_run_passed_even_if_a_feature_branch_failed_later()
    {
        var health = Analyze(
            Run(RunResult.Failed, 10),
            Run(RunResult.Succeeded, 8),
            Run(RunResult.Failed, 2, branch: "refs/heads/feature/x"));

        Assert.Null(Find(health, LabelKind.Broken));
    }

    [Fact]
    public void Cancelled_and_in_progress_runs_do_not_hide_a_broken_default_branch()
    {
        var health = Analyze(
            Run(RunResult.Failed, 10),
            Run(RunResult.Canceled, 5),
            Run(RunResult.None, 1, status: RunStatus.InProgress));

        Assert.NotNull(Find(health, LabelKind.Broken));
    }

    [Fact]
    public void Broken_needs_a_known_default_branch()
    {
        var health = RunAnalyzer.Analyze(Definition(defaultBranch: null), [Run(RunResult.Failed, 1)], Options, Now);
        Assert.Null(Find(health, LabelKind.Broken));
    }

    [Fact]
    public void Flaky_when_the_same_commit_both_failed_and_passed()
    {
        var health = Analyze(
            Run(RunResult.Failed, 3, commit: "1234567890abc"),
            Run(RunResult.Succeeded, 2, commit: "1234567890abc"));

        var flaky = Find(health, LabelKind.Flaky);
        Assert.NotNull(flaky);
        Assert.Contains("1234567", flaky.Evidence);
    }

    [Fact]
    public void Flaky_when_results_flip_often_on_one_branch()
    {
        var health = Analyze(
            Run(RunResult.Succeeded, 6),
            Run(RunResult.Failed, 5),
            Run(RunResult.Succeeded, 4),
            Run(RunResult.Failed, 3),
            Run(RunResult.Succeeded, 2));

        Assert.Contains("flipped between pass and fail 4 times", Find(health, LabelKind.Flaky)!.Evidence);
    }

    [Fact]
    public void Not_flaky_for_a_single_break_and_fix()
    {
        var health = Analyze(
            Run(RunResult.Succeeded, 4),
            Run(RunResult.Failed, 3),
            Run(RunResult.Succeeded, 2));

        Assert.Null(Find(health, LabelKind.Flaky));
    }

    [Fact]
    public void Slow_run_is_labelled_against_the_median_successful_duration()
    {
        var slow = Run(RunResult.Succeeded, 1, duration: TimeSpan.FromMinutes(20));
        var health = Analyze(
            Run(RunResult.Succeeded, 5, duration: TimeSpan.FromMinutes(5)),
            Run(RunResult.Succeeded, 4, duration: TimeSpan.FromMinutes(6)),
            Run(RunResult.Succeeded, 3, duration: TimeSpan.FromMinutes(5)),
            slow);

        var label = Assert.Single(health.LabelsFor(slow));
        Assert.Equal(LabelKind.Slow, label.Kind);
        Assert.Contains("20m 00s", label.Evidence);
    }

    [Fact]
    public void Slow_needs_enough_successful_runs_to_compare_against()
    {
        var slow = Run(RunResult.Succeeded, 1, duration: TimeSpan.FromMinutes(20));
        var health = Analyze(Run(RunResult.Succeeded, 3, duration: TimeSpan.FromMinutes(5)), slow);
        Assert.Empty(health.LabelsFor(slow));
    }

    [Fact]
    public void Long_queue_and_partially_succeeded_runs_are_labelled()
    {
        var waited = Run(RunResult.PartiallySucceeded, 1, wait: TimeSpan.FromMinutes(15));
        var labels = Analyze(waited).LabelsFor(waited).Select(l => l.Kind).ToList();
        Assert.Contains(LabelKind.LongQueue, labels);
        Assert.Contains(LabelKind.PartiallySucceeded, labels);
    }

    [Fact]
    public void Getting_slower_compares_the_newest_third_with_the_oldest()
    {
        var runs = Enumerable.Range(0, 9)
            .Select(i => Run(RunResult.Succeeded, 20 - i, duration: TimeSpan.FromMinutes(i < 3 ? 5 : i < 6 ? 7 : 10)))
            .ToArray();

        Assert.NotNull(Find(Analyze(runs), LabelKind.GettingSlower));
    }

    [Fact]
    public void Stable_durations_are_not_getting_slower()
    {
        var runs = Enumerable.Range(0, 9).Select(i => Run(RunResult.Succeeded, 20 - i, duration: TimeSpan.FromMinutes(5))).ToArray();
        Assert.Null(Find(Analyze(runs), LabelKind.GettingSlower));
    }

    [Fact]
    public void Stale_when_no_recent_runs_or_no_runs_at_all()
    {
        Assert.NotNull(Find(Analyze(Run(RunResult.Succeeded, 24 * 45)), LabelKind.Stale));
        Assert.NotNull(Find(Analyze(), LabelKind.Stale));
        Assert.Null(Find(Analyze(Run(RunResult.Succeeded, 24)), LabelKind.Stale));
    }

    [Fact]
    public void Pass_rate_ignores_cancelled_and_unfinished_runs()
    {
        var health = Analyze(
            Run(RunResult.Succeeded, 5),
            Run(RunResult.Failed, 4),
            Run(RunResult.Canceled, 3),
            Run(RunResult.None, 1, status: RunStatus.InProgress));

        Assert.Equal(0.5, health.PassRate);
    }

    [Fact]
    public void Runs_are_ordered_newest_first_and_labels_most_severe_first()
    {
        var health = Analyze(
            Run(RunResult.Failed, 30 * 24 + 5, commit: "same"),
            Run(RunResult.Succeeded, 30 * 24 + 4, commit: "same"),
            Run(RunResult.Failed, 30 * 24 + 1));

        Assert.True(health.Runs[0].QueueTime > health.Runs[^1].QueueTime);
        Assert.Equal([LabelKind.Broken, LabelKind.Flaky, LabelKind.Stale], health.Labels.Select(l => l.Kind));
    }

    [Fact]
    public void Disabled_pipelines_get_an_info_label()
    {
        var health = RunAnalyzer.Analyze(Definition(disabled: true), [Run(RunResult.Succeeded, 1)], Options, Now);
        Assert.Equal(LabelSeverity.Info, Find(health, LabelKind.Disabled)!.Severity);
    }
}
