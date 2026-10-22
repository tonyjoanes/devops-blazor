using DevOpsBlazor.Core.Models;
using DevOpsBlazor.Core.Text;

namespace DevOpsBlazor.Core.RunAnalysis;

/// <summary>
/// Works out pass rate, median duration and problem labels from runs that have already been fetched.
/// Labels are hints, not verdicts: each carries the evidence that produced it.
/// </summary>
public static class RunAnalyzer
{
    /// <param name="runs">Runs for one pipeline, in any order.</param>
    public static PipelineHealth Analyze(
        PipelineDefinition definition, IEnumerable<PipelineRun> runs, RunLabelOptions options, DateTimeOffset now)
    {
        var newestFirst = runs.OrderByDescending(r => r.QueueTime).ToList();
        var completed = newestFirst.Where(r => r.IsCompleted).ToList();
        var judged = completed.Where(r => r.Result != RunResult.Canceled).ToList();
        double? passRate = judged.Count == 0 ? null : judged.Count(r => r.Result == RunResult.Succeeded) / (double)judged.Count;

        var successDurations = completed
            .Where(r => r.Result == RunResult.Succeeded && r.Duration is not null)
            .Select(r => r.Duration!.Value)
            .ToList();
        var median = Stats.Median(successDurations);

        var labels = new List<ProblemLabel>();
        if (definition.IsDisabled)
        {
            labels.Add(new(LabelKind.Disabled, LabelSeverity.Info, "Disabled", "The pipeline's triggers are disabled or paused in Azure DevOps."));
        }
        AddIfNotNull(labels, Broken(definition, completed));
        AddIfNotNull(labels, Flaky(completed, options));
        AddIfNotNull(labels, GettingSlower(completed, options));
        AddIfNotNull(labels, Stale(newestFirst, options, now));

        var runLabels = new Dictionary<int, IReadOnlyList<ProblemLabel>>();
        var usableMedian = successDurations.Count >= options.MinSamples ? median : null;
        foreach (var run in newestFirst)
        {
            var forRun = LabelRun(run, usableMedian, options);
            if (forRun.Count > 0) runLabels[run.Id] = forRun;
        }

        return new PipelineHealth(definition, newestFirst, passRate, median, Sort(labels), runLabels);
    }

    /// <summary>Labels that can be worked out from a single run plus the pipeline's typical duration.</summary>
    public static IReadOnlyList<ProblemLabel> LabelRun(PipelineRun run, TimeSpan? medianSuccess, RunLabelOptions options)
    {
        var labels = new List<ProblemLabel>();

        if (run.Result == RunResult.PartiallySucceeded)
        {
            labels.Add(new(LabelKind.PartiallySucceeded, LabelSeverity.Medium, "Partially succeeded",
                "A step failed but the run carried on, usually because the step has continueOnError set. The failure is easy to miss."));
        }
        if (run.Result == RunResult.Canceled)
        {
            labels.Add(new(LabelKind.Canceled, LabelSeverity.Info, "Cancelled", "The run was cancelled before it finished."));
        }
        if (run.IsCompleted && run.Duration is { } duration && medianSuccess is { } median && median > TimeSpan.Zero
            && duration.TotalSeconds > median.TotalSeconds * options.SlowFactor)
        {
            var factor = duration.TotalSeconds / median.TotalSeconds;
            labels.Add(new(LabelKind.Slow, LabelSeverity.Low, "Slow",
                $"Took {DisplayFormat.Duration(duration)} against a typical {DisplayFormat.Duration(median)} for successful runs ({factor:0.0}×)."));
        }
        if (run.QueueWait is { } wait && wait.TotalMinutes > options.LongQueueMinutes)
        {
            labels.Add(new(LabelKind.LongQueue, LabelSeverity.Low, "Long queue",
                $"Waited {DisplayFormat.Duration(wait)} for an agent before starting. This can point to agent capacity limits."));
        }
        return Sort(labels);
    }

    private static ProblemLabel? Broken(PipelineDefinition definition, List<PipelineRun> completedNewestFirst)
    {
        if (definition.DefaultBranch is not { } defaultBranch) return null;
        var onDefault = completedNewestFirst
            .Where(r => r.Result != RunResult.Canceled && string.Equals(r.SourceBranch, defaultBranch, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (onDefault.Count == 0 || onDefault[0].Result != RunResult.Failed) return null;

        var streak = onDefault.TakeWhile(r => r.Result == RunResult.Failed).ToList();
        var first = streak[^1];
        var branch = BranchNames.Short(defaultBranch);
        var count = streak.Count == onDefault.Count ? $"at least the last {streak.Count}" : $"the last {streak.Count}";
        var runWord = streak.Count == 1 ? "run" : "runs";
        var commit = first.ShortCommit is { } c ? $", starting at commit {c}" : "";
        return new(LabelKind.Broken, LabelSeverity.High, "Broken",
            $"Failed on {count} {runWord} of {branch}, since {DisplayFormat.ShortDate(first.When)}{commit}.");
    }

    private static ProblemLabel? Flaky(List<PipelineRun> completedNewestFirst, RunLabelOptions options)
    {
        static bool? Passed(PipelineRun r) => r.Result switch
        {
            RunResult.Succeeded => true,
            RunResult.Failed or RunResult.PartiallySucceeded => false,
            _ => null,
        };

        // The strongest signal: the same commit both failed and passed.
        var sameCommit = completedNewestFirst
            .Where(r => r.SourceVersion is not null && Passed(r) is not null)
            .GroupBy(r => r.SourceVersion!)
            .FirstOrDefault(g => g.Any(r => Passed(r) == true) && g.Any(r => Passed(r) == false));
        if (sameCommit is not null)
        {
            var run = sameCommit.First();
            return new(LabelKind.Flaky, LabelSeverity.Medium, "Flaky",
                $"Commit {run.ShortCommit} on {run.BranchName} both failed and passed, so at least one result didn't come from a code change.");
        }

        var worst = completedNewestFirst
            .GroupBy(r => r.SourceBranch, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var outcomes = g.OrderBy(r => r.QueueTime).Select(Passed).Where(p => p is not null).ToList();
                var flips = outcomes.Zip(outcomes.Skip(1)).Count(pair => pair.First != pair.Second);
                return (Branch: g.Key, Flips: flips, Runs: outcomes.Count);
            })
            .OrderByDescending(x => x.Flips)
            .FirstOrDefault();

        if (worst.Branch is null || worst.Flips < options.FlakyFlipsThreshold) return null;
        return new(LabelKind.Flaky, LabelSeverity.Medium, "Flaky",
            $"Results flipped between pass and fail {worst.Flips} times in the last {worst.Runs} runs of {BranchNames.Short(worst.Branch)}.");
    }

    private static ProblemLabel? GettingSlower(List<PipelineRun> completedNewestFirst, RunLabelOptions options)
    {
        var durations = completedNewestFirst
            .Where(r => r.Result == RunResult.Succeeded && r.Duration is not null)
            .OrderBy(r => r.QueueTime)
            .Select(r => r.Duration!.Value)
            .ToList();
        if (durations.Count < Math.Max(9, options.MinSamples * 3)) return null;

        var third = durations.Count / 3;
        var earlier = Stats.Median(durations.Take(third))!.Value;
        var recent = Stats.Median(durations.Skip(durations.Count - third))!.Value;
        if (recent.TotalSeconds <= earlier.TotalSeconds * options.SlowerTrendFactor || recent - earlier < TimeSpan.FromMinutes(1)) return null;

        var increase = (recent.TotalSeconds / earlier.TotalSeconds) - 1;
        return new(LabelKind.GettingSlower, LabelSeverity.Low, "Getting slower",
            $"Recent successful runs take a median {DisplayFormat.Duration(recent)}, up from {DisplayFormat.Duration(earlier)} earlier in the window (+{increase:P0}).".Replace(" %", "%"));
    }

    private static ProblemLabel? Stale(List<PipelineRun> newestFirst, RunLabelOptions options, DateTimeOffset now)
    {
        if (newestFirst.Count == 0)
        {
            return new(LabelKind.Stale, LabelSeverity.Info, "Stale", "No runs found. It may never have run, or old runs were removed by retention.");
        }
        var latest = newestFirst[0].QueueTime;
        var age = now - latest;
        if (age.TotalDays < options.StaleDays) return null;
        return new(LabelKind.Stale, LabelSeverity.Info, "Stale",
            $"No runs since {DisplayFormat.ShortDate(latest)} ({(int)age.TotalDays} days). It may be unused, or its trigger may be off.");
    }

    internal static IReadOnlyList<ProblemLabel> Sort(List<ProblemLabel> labels) =>
        labels.OrderBy(l => l.Severity).ThenBy(l => l.Kind).ToList();

    private static void AddIfNotNull(List<ProblemLabel> labels, ProblemLabel? label)
    {
        if (label is not null) labels.Add(label);
    }
}
