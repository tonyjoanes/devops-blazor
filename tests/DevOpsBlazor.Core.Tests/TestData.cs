using System.Text.Json;
using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Core.Tests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    public const string Main = "refs/heads/main";

    public static PipelineDefinition Definition(string? defaultBranch = Main, bool disabled = false) =>
        new(1, "Platform", "api-ci", "\\Services", PipelineProcess.Yaml, "api", defaultBranch, disabled);

    private static int _nextId = 1;

    /// <param name="hoursAgo">When the run was queued, relative to <see cref="Now"/>.</param>
    public static PipelineRun Run(
        RunResult result,
        double hoursAgo,
        TimeSpan? duration = null,
        string branch = Main,
        string? commit = null,
        TimeSpan? wait = null,
        RunStatus status = RunStatus.Completed)
    {
        var queued = Now - TimeSpan.FromHours(hoursAgo);
        var start = queued + (wait ?? TimeSpan.FromSeconds(30));
        var finish = status == RunStatus.Completed ? start + (duration ?? TimeSpan.FromMinutes(5)) : (DateTimeOffset?)null;
        var id = Interlocked.Increment(ref _nextId);
        return new PipelineRun(id, "Platform", 1, "api-ci", $"#{id}", status, status == RunStatus.Completed ? result : RunResult.None,
            branch, commit ?? Guid.NewGuid().ToString("N"), "individualCI", "Sam", "api", "TfsGit", queued, start, finish);
    }

    public static TimelineRecord Record(
        string type, string name, Guid? parent = null, RunResult? result = RunResult.Succeeded, int order = 1,
        int attempt = 1, int warnings = 0, params TimelineIssue[] issues) =>
        new(Guid.NewGuid(), parent, type, name, result, "completed", Now, Now.AddMinutes(1), issues.Count(i => i.Type == TimelineIssueType.Error),
            warnings, attempt, order, type == "Job" ? "agent-1" : null, 1, issues);

    // ---- Policies ----

    public static readonly Guid RepoId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid OtherRepoId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static Repository Repo(string name = "api", string? defaultBranch = Main, bool disabled = false, Guid? id = null) =>
        new(id ?? RepoId, name, Guid.Empty, "Platform", defaultBranch, disabled, 100);

    private static int _policyId = 1;

    public static PolicyConfiguration Policy(
        PolicyKind kind, object settings, bool enabled = true, bool blocking = true, bool deleted = false, params PolicyScope[] scopes) =>
        new(Interlocked.Increment(ref _policyId), "Platform", Guid.NewGuid(), kind.ToString(), kind, enabled, blocking, deleted,
            scopes.Length == 0 ? [new PolicyScope(RepoId, Main, PolicyMatchKind.Exact)] : scopes,
            JsonSerializer.SerializeToElement(settings));

    public static PolicyConfiguration MinReviewers(int count, bool creatorVoteCounts = false, bool resetOnPush = true, bool blocking = true) =>
        Policy(PolicyKind.MinimumReviewers, new { minimumApproverCount = count, creatorVoteCounts, resetOnSourcePush = resetOnPush }, blocking: blocking);

    public static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    public static readonly JsonElement NoSettings = Json(new { });
}
