using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Core.RunAnalysis;

/// <summary>Labels and summaries that need a run's timeline (stages, jobs and tasks).</summary>
public static class TimelineAnalyzer
{
    private static readonly string[] TimeoutPhrases =
    [
        "timed out",
        "timeout",
        "ran longer than the maximum time",
        "exceeded the maximum execution time",
    ];

    public static IReadOnlyList<ProblemLabel> Label(IReadOnlyList<TimelineRecord> records)
    {
        var labels = new List<ProblemLabel>();

        var retried = records
            .Where(r => r.Attempt > 1 && (r.IsStage || r.IsJob || r.Type.Equals("Phase", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.Attempt)
            .FirstOrDefault();
        if (retried is not null)
        {
            labels.Add(new(LabelKind.Retried, LabelSeverity.Low, "Retried",
                $"{retried.Type} '{retried.Name}' was rerun (attempt {retried.Attempt}). A rerun that passes can hide a flaky step."));
        }

        var timeout = records
            .SelectMany(r => r.Issues.Select(i => (Record: r, Issue: i)))
            .FirstOrDefault(x => TimeoutPhrases.Any(p => x.Issue.Message.Contains(p, StringComparison.OrdinalIgnoreCase)));
        if (timeout.Record is not null)
        {
            labels.Add(new(LabelKind.TimedOut, LabelSeverity.Medium, "Timed out",
                $"{timeout.Record.Type} '{timeout.Record.Name}': {Truncate(timeout.Issue.Message, 200)}"));
        }

        return RunAnalyzer.Sort(labels);
    }

    /// <summary>Total warnings in the run. Job and stage counts roll up task counts, so only tasks are counted.</summary>
    public static int WarningCount(IReadOnlyList<TimelineRecord> records)
    {
        var tasks = records.Where(r => r.IsTask).ToList();
        return tasks.Count > 0 ? tasks.Sum(r => r.WarningCount) : records.Sum(r => r.WarningCount);
    }

    /// <param name="warningsOldestFirst">Warning counts for completed runs, oldest first.</param>
    public static ProblemLabel? WarningsRising(IReadOnlyList<int> warningsOldestFirst)
    {
        if (warningsOldestFirst.Count < 6) return null;
        var half = warningsOldestFirst.Count / 2;
        var earlier = warningsOldestFirst.Take(half).Average();
        var recent = warningsOldestFirst.Skip(warningsOldestFirst.Count - half).Average();
        if (recent < earlier * 1.5 || recent - earlier < 3) return null;
        return new(LabelKind.WarningsRising, LabelSeverity.Low, "Warnings rising",
            $"Average warnings per run went from {earlier:0.#} to {recent:0.#} over the last {warningsOldestFirst.Count} runs.");
    }

    /// <summary>The first failed task in execution order, falling back to the first failed job.</summary>
    public static (TimelineRecord? Job, TimelineRecord? Task) FirstFailure(IReadOnlyList<TimelineRecord> records)
    {
        var tree = TimelineTree.Build(records);
        TimelineRecord? failedJob = null;
        foreach (var node in tree.Flatten())
        {
            if (node.Record.IsJob && node.Record.IsFailed) failedJob ??= node.Record;
            if (node.Record.IsTask && node.Record.IsFailed)
            {
                return (FindJob(records, node.Record), node.Record);
            }
        }
        return (failedJob, null);
    }

    public static IReadOnlyList<(TimelineRecord Record, TimelineIssue Issue)> Errors(IReadOnlyList<TimelineRecord> records) =>
        TimelineTree.Build(records).Flatten()
            .SelectMany(n => n.Record.Issues.Where(i => i.Type == TimelineIssueType.Error).Select(i => (n.Record, i)))
            .ToList();

    public static TimelineRecord? FindJob(IReadOnlyList<TimelineRecord> records, TimelineRecord record)
    {
        var byId = records.DistinctBy(r => r.Id).ToDictionary(r => r.Id);
        var current = record;
        while (current is not null)
        {
            if (current.IsJob) return current;
            current = current.ParentId is { } p && byId.TryGetValue(p, out var parent) ? parent : null;
        }
        return null;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}

public sealed class TimelineNode(TimelineRecord record)
{
    public TimelineRecord Record { get; } = record;
    public List<TimelineNode> Children { get; } = [];

    public IEnumerable<TimelineNode> Flatten()
    {
        yield return this;
        foreach (var child in Children)
        foreach (var node in child.Flatten())
            yield return node;
    }
}

public static class TimelineTree
{
    /// <summary>A synthetic root whose children are the top-level records (usually stages), ordered as they ran.</summary>
    public static TimelineNode Build(IReadOnlyList<TimelineRecord> records)
    {
        var root = new TimelineNode(new TimelineRecord(Guid.Empty, null, "Root", "", null, null, null, null, 0, 0, 1, 0, null, null, []));
        var nodes = records.DistinctBy(r => r.Id).ToDictionary(r => r.Id, r => new TimelineNode(r));
        foreach (var node in nodes.Values)
        {
            var parent = node.Record.ParentId is { } p && nodes.TryGetValue(p, out var found) ? found : root;
            parent.Children.Add(node);
        }
        foreach (var node in nodes.Values.Append(root))
        {
            node.Children.Sort((a, b) =>
            {
                var byOrder = a.Record.Order.CompareTo(b.Record.Order);
                return byOrder != 0 ? byOrder : Nullable.Compare(a.Record.StartTime, b.Record.StartTime);
            });
        }
        return root;
    }
}
