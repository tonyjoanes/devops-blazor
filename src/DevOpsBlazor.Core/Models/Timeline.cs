namespace DevOpsBlazor.Core.Models;

public enum TimelineIssueType { Warning, Error }

public sealed record TimelineIssue(TimelineIssueType Type, string Message);

public sealed record TimelineRecord(
    Guid Id,
    Guid? ParentId,
    string Type,
    string Name,
    RunResult? Result,
    string? State,
    DateTimeOffset? StartTime,
    DateTimeOffset? FinishTime,
    int ErrorCount,
    int WarningCount,
    int Attempt,
    int Order,
    string? WorkerName,
    int? LogId,
    IReadOnlyList<TimelineIssue> Issues)
{
    public TimeSpan? Duration => StartTime is { } s && FinishTime is { } f && f >= s ? f - s : null;

    public bool IsStage => Type.Equals("Stage", StringComparison.OrdinalIgnoreCase);
    public bool IsJob => Type.Equals("Job", StringComparison.OrdinalIgnoreCase);
    public bool IsTask => Type.Equals("Task", StringComparison.OrdinalIgnoreCase);
    public bool IsFailed => Result is RunResult.Failed;
}
