using System.Text.Json;
using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.AzureDevOps.Dtos;

internal static class Mapping
{
    public static PipelineDefinition ToModel(this DefinitionDto d, string project) => new(
        d.Id,
        d.Project?.Name ?? project,
        d.Name,
        d.Path ?? "\\",
        d.Process?.Type switch { 1 => PipelineProcess.Designer, 2 => PipelineProcess.Yaml, _ => PipelineProcess.Unknown },
        d.Repository?.Name,
        string.IsNullOrEmpty(d.Repository?.DefaultBranch) ? null : d.Repository.DefaultBranch,
        d.QueueStatus is { } q && !q.Equals("enabled", StringComparison.OrdinalIgnoreCase));

    public static PipelineRun ToModel(this BuildDto b, string project) => new(
        b.Id,
        b.Project?.Name ?? project,
        b.Definition?.Id ?? 0,
        b.Definition?.Name ?? "",
        b.BuildNumber ?? b.Id.ToString(),
        ParseEnum(b.Status, RunStatus.Unknown),
        ParseEnum(b.Result, RunResult.None),
        b.SourceBranch ?? "",
        b.SourceVersion,
        b.Reason ?? "",
        b.RequestedFor?.DisplayName,
        b.Repository?.Name,
        b.Repository?.Type,
        Real(b.QueueTime) ?? Real(b.StartTime) ?? DateTimeOffset.MinValue,
        Real(b.StartTime),
        Real(b.FinishTime));

    public static TimelineRecord ToModel(this TimelineRecordDto r) => new(
        r.Id,
        r.ParentId,
        r.Type ?? "",
        r.Name ?? "",
        r.Result is null ? null : ParseEnum(r.Result, RunResult.None),
        r.State,
        Real(r.StartTime),
        Real(r.FinishTime),
        r.ErrorCount ?? 0,
        r.WarningCount ?? 0,
        r.Attempt ?? 1,
        r.Order ?? 0,
        r.WorkerName,
        r.Log?.Id,
        (r.Issues ?? [])
            .Where(i => !string.IsNullOrEmpty(i.Message))
            .Select(i => new TimelineIssue(
                string.Equals(i.Type, "error", StringComparison.OrdinalIgnoreCase) ? TimelineIssueType.Error : TimelineIssueType.Warning,
                i.Message!))
            .ToList());

    public static RunChange ToModel(this ChangeDto c) =>
        new(c.Id ?? "", (c.Message ?? "").Trim(), c.Author?.DisplayName, Real(c.Timestamp), c.DisplayUri);

    public static Repository ToModel(this RepositoryDto r, string project) => new(
        r.Id,
        r.Name,
        r.Project?.Id ?? Guid.Empty,
        r.Project?.Name ?? project,
        string.IsNullOrEmpty(r.DefaultBranch) ? null : r.DefaultBranch,
        r.IsDisabled ?? false,
        r.Size ?? 0);

    public static PolicyConfiguration ToModel(this PolicyConfigurationDto p, string project)
    {
        var settings = p.Settings.ValueKind == JsonValueKind.Object ? p.Settings.Clone() : EmptyObject;
        var typeId = p.Type?.Id ?? Guid.Empty;
        var typeName = p.Type?.DisplayName ?? "Unknown policy";
        return new PolicyConfiguration(
            p.Id,
            project,
            typeId,
            typeName,
            PolicyClassifier.Classify(typeId, typeName),
            p.IsEnabled,
            p.IsBlocking,
            p.IsDeleted ?? false,
            ParseScopes(settings),
            settings);
    }

    private static readonly JsonElement EmptyObject = JsonDocument.Parse("{}").RootElement.Clone();

    internal static List<PolicyScope> ParseScopes(JsonElement settings)
    {
        var scopes = new List<PolicyScope>();
        if (!settings.TryGetProperty("scope", out var scopeArray) || scopeArray.ValueKind != JsonValueKind.Array) return scopes;
        foreach (var s in scopeArray.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            Guid? repositoryId = s.TryGetProperty("repositoryId", out var r) && r.ValueKind == JsonValueKind.String
                && Guid.TryParse(r.GetString(), out var id) ? id : null;
            var refName = s.TryGetProperty("refName", out var rn) && rn.ValueKind == JsonValueKind.String ? rn.GetString() : null;
            var matchKind = s.TryGetProperty("matchKind", out var mk) && mk.ValueKind == JsonValueKind.String
                ? ParseEnum(mk.GetString(), PolicyMatchKind.Exact)
                : PolicyMatchKind.Exact;
            scopes.Add(new PolicyScope(repositoryId, string.IsNullOrEmpty(refName) ? null : refName, matchKind));
        }
        return scopes;
    }

    private static T ParseEnum<T>(string? value, T fallback) where T : struct, Enum =>
        value is not null && Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;

    /// <summary>ADO sometimes sends 0001-01-01 for "not set".</summary>
    private static DateTimeOffset? Real(DateTimeOffset? value) => value is { Year: > 1 } v ? v : null;
}
