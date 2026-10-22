using DevOpsBlazor.Core.Models;
using DevOpsBlazor.Core.RunAnalysis;
using static DevOpsBlazor.Core.Tests.TestData;

namespace DevOpsBlazor.Core.Tests.RunAnalysis;

public class TimelineAnalyzerTests
{
    [Fact]
    public void First_failure_is_the_first_failed_task_in_execution_order_with_its_job()
    {
        var stage1 = Record("Stage", "Build", order: 1, result: RunResult.Failed);
        var stage2 = Record("Stage", "Test", order: 2, result: RunResult.Failed);
        var job1 = Record("Job", "Compile", stage1.Id, RunResult.Failed);
        var job2 = Record("Job", "Unit tests", stage2.Id, RunResult.Failed);
        var laterTask = Record("Task", "dotnet test", job2.Id, RunResult.Failed);
        var earlierTask = Record("Task", "dotnet build", job1.Id, RunResult.Failed, order: 2);
        var okTask = Record("Task", "Checkout", job1.Id, order: 1);

        // Deliberately out of order: the tree is rebuilt from parent ids and order.
        var (job, task) = TimelineAnalyzer.FirstFailure([laterTask, job2, stage2, okTask, earlierTask, job1, stage1]);

        Assert.Equal(earlierTask.Id, task!.Id);
        Assert.Equal(job1.Id, job!.Id);
    }

    [Fact]
    public void First_failure_falls_back_to_a_failed_job_when_no_task_failed()
    {
        var job = Record("Job", "Deploy", result: RunResult.Failed);
        var (failedJob, task) = TimelineAnalyzer.FirstFailure([job, Record("Task", "Step", job.Id)]);
        Assert.Equal(job.Id, failedJob!.Id);
        Assert.Null(task);
    }

    [Fact]
    public void Retried_and_timed_out_labels_come_from_attempts_and_issues()
    {
        var stage = Record("Stage", "Deploy", attempt: 2);
        var job = Record("Job", "Deploy job", stage.Id, RunResult.Canceled,
            issues: [new TimelineIssue(TimelineIssueType.Error, "The job running on agent X ran longer than the maximum time of 60 minutes.")]);

        var kinds = TimelineAnalyzer.Label([stage, job]).Select(l => l.Kind).ToList();

        Assert.Contains(LabelKind.Retried, kinds);
        Assert.Contains(LabelKind.TimedOut, kinds);
    }

    [Fact]
    public void Healthy_timeline_has_no_labels()
    {
        var job = Record("Job", "Build");
        Assert.Empty(TimelineAnalyzer.Label([job, Record("Task", "Compile", job.Id)]));
    }

    [Fact]
    public void Warning_count_uses_tasks_so_rolled_up_counts_are_not_doubled()
    {
        var job = Record("Job", "Build", warnings: 5);
        Assert.Equal(5, TimelineAnalyzer.WarningCount([job, Record("Task", "A", job.Id, warnings: 2), Record("Task", "B", job.Id, warnings: 3)]));
    }

    [Fact]
    public void Warnings_rising_needs_a_clear_increase()
    {
        Assert.NotNull(TimelineAnalyzer.WarningsRising([1, 2, 1, 8, 9, 10]));
        Assert.Null(TimelineAnalyzer.WarningsRising([5, 5, 6, 5, 6, 6]));
        Assert.Null(TimelineAnalyzer.WarningsRising([0, 10]));
    }

    [Fact]
    public void Errors_are_listed_in_execution_order()
    {
        var job = Record("Job", "Build", result: RunResult.Failed);
        var second = Record("Task", "Test", job.Id, RunResult.Failed, order: 2, issues: [new TimelineIssue(TimelineIssueType.Error, "second")]);
        var first = Record("Task", "Compile", job.Id, RunResult.Failed, order: 1,
            issues: [new TimelineIssue(TimelineIssueType.Warning, "ignored"), new TimelineIssue(TimelineIssueType.Error, "first")]);

        Assert.Equal(["first", "second"], TimelineAnalyzer.Errors([second, job, first]).Select(e => e.Issue.Message));
    }
}
