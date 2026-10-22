using DevOpsBlazor.Core.Models;
using Microsoft.Extensions.Options;

namespace DevOpsBlazor.AzureDevOps.Links;

/// <summary>
/// Builds every Azure DevOps portal URL the app links to. Portal URLs aren't a formal API, so they all live here:
/// if Microsoft changes one, this is the only place to fix.
/// </summary>
public sealed class AdoLinks(IOptionsMonitor<AzureDevOpsOptions> options)
{
    private string Org => options.CurrentValue.Organization?.Trim().TrimEnd('/') ?? "";

    private string ProjectRoot(string project) => $"{Org}/{Uri.EscapeDataString(project)}";

    public string Pipelines(string project) => $"{ProjectRoot(project)}/_build";

    public string Pipeline(string project, int definitionId) => $"{ProjectRoot(project)}/_build?definitionId={definitionId}";

    public string Run(string project, int buildId) => $"{ProjectRoot(project)}/_build/results?buildId={buildId}&view=results";

    /// <summary>The logs view for a run, optionally focused on one job and task (ids are timeline record ids).</summary>
    public string Logs(string project, int buildId, Guid? jobId = null, Guid? taskId = null)
    {
        var url = $"{ProjectRoot(project)}/_build/results?buildId={buildId}&view=logs";
        if (jobId is { } j) url += $"&j={j}";
        if (taskId is { } t) url += $"&t={t}";
        return url;
    }

    /// <summary>Direct download of every log as a zip. Opened by the browser, so it uses the user's own ADO sign-in.</summary>
    public string LogsZip(string project, int buildId) => $"{ProjectRoot(project)}/_apis/build/builds/{buildId}/logs?$format=zip";

    public string Commit(string project, string repositoryName, string commitId) =>
        $"{ProjectRoot(project)}/_git/{Uri.EscapeDataString(repositoryName)}/commit/{commitId}";

    public string? CommitFor(PipelineRun run) =>
        run.SourceVersion is { } sha && run.RepositoryName is { } repo && string.Equals(run.RepositoryType, "TfsGit", StringComparison.OrdinalIgnoreCase)
            ? Commit(run.Project, repo, sha)
            : null;

    public string Repository(string project, string repositoryName) => $"{ProjectRoot(project)}/_git/{Uri.EscapeDataString(repositoryName)}";

    /// <summary>Branch policy settings for one branch of a repository.</summary>
    public string BranchPolicies(string project, Guid repositoryId, string refName) =>
        $"{ProjectRoot(project)}/_settings/repositories?repo={repositoryId}&_a=policiesMid&refs={Uri.EscapeDataString(refName)}";

    /// <summary>Repository-level policy settings (file size, path length and so on).</summary>
    public string RepositoryPolicies(string project, Guid repositoryId) =>
        $"{ProjectRoot(project)}/_settings/repositories?repo={repositoryId}&_a=policies";

    /// <summary>Project-wide repository settings, where cross-repo policies are managed.</summary>
    public string ProjectRepositorySettings(string project) => $"{ProjectRoot(project)}/_settings/repositories?_a=policies";
}
