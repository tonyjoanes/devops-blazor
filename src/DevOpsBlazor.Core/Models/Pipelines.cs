namespace DevOpsBlazor.Core.Models;

public enum RunStatus { Unknown, NotStarted, InProgress, Cancelling, Postponed, Completed }

public enum RunResult { None, Succeeded, PartiallySucceeded, Failed, Canceled, Skipped, Abandoned }

public enum PipelineProcess { Unknown, Designer, Yaml }

public sealed record Project(Guid Id, string Name);

public sealed record PipelineDefinition(
    int Id,
    string Project,
    string Name,
    string Path,
    PipelineProcess Process,
    string? RepositoryName,
    string? DefaultBranch,
    bool IsDisabled)
{
    /// <summary>Folder path without the leading backslash, or empty for the root folder.</summary>
    public string Folder => Path.Trim('\\');
}

public sealed record PipelineRun(
    int Id,
    string Project,
    int DefinitionId,
    string DefinitionName,
    string BuildNumber,
    RunStatus Status,
    RunResult Result,
    string SourceBranch,
    string? SourceVersion,
    string Reason,
    string? RequestedFor,
    string? RepositoryName,
    string? RepositoryType,
    DateTimeOffset QueueTime,
    DateTimeOffset? StartTime,
    DateTimeOffset? FinishTime)
{
    public bool IsCompleted => Status == RunStatus.Completed;

    /// <summary>Time between being queued and an agent starting the run.</summary>
    public TimeSpan? QueueWait => StartTime is { } start ? Max(start - QueueTime) : null;

    /// <summary>Time from start to finish; null while the run is still going.</summary>
    public TimeSpan? Duration => StartTime is { } start && FinishTime is { } finish ? Max(finish - start) : null;

    public string BranchName => BranchNames.Short(SourceBranch);

    public string? ShortCommit => SourceVersion is { Length: >= 7 } v ? v[..7] : SourceVersion;

    /// <summary>The most useful single point in time for the run: when it finished, otherwise when it was queued.</summary>
    public DateTimeOffset When => FinishTime ?? QueueTime;

    private static TimeSpan Max(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;
}

public sealed record RunChange(string Id, string Message, string? Author, DateTimeOffset? Timestamp, string? DisplayUri);

public static class BranchNames
{
    public static string Short(string? refName)
    {
        if (string.IsNullOrEmpty(refName)) return "";
        if (refName.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase)) return refName["refs/heads/".Length..];
        if (refName.StartsWith("refs/pull/", StringComparison.OrdinalIgnoreCase))
        {
            var parts = refName.Split('/');
            return parts.Length > 2 ? $"PR {parts[2]}" : refName;
        }
        if (refName.StartsWith("refs/tags/", StringComparison.OrdinalIgnoreCase)) return "tag " + refName["refs/tags/".Length..];
        return refName;
    }

    public static string Full(string branch) =>
        branch.StartsWith("refs/", StringComparison.OrdinalIgnoreCase) ? branch : "refs/heads/" + branch;
}
