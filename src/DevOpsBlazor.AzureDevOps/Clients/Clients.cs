using DevOpsBlazor.AzureDevOps.Dtos;
using DevOpsBlazor.AzureDevOps.Http;
using DevOpsBlazor.Core.Models;
using Microsoft.Extensions.Options;

namespace DevOpsBlazor.AzureDevOps.Clients;

// All clients are read-only: they only ever issue GET requests (AdoHttpHandler enforces it).

public interface IConnectionClient
{
    /// <summary>The display name of the identity behind the PAT.</summary>
    Task<string> GetAuthenticatedUserAsync(CancellationToken cancellationToken = default);
}

public interface IProjectClient
{
    Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default);
}

public interface IBuildClient
{
    Task<IReadOnlyList<PipelineDefinition>> GetDefinitionsAsync(string project, CancellationToken cancellationToken = default);

    /// <summary>The newest <paramref name="perDefinition"/> runs of each listed pipeline, in as few calls as possible.</summary>
    Task<IReadOnlyList<PipelineRun>> GetRecentRunsAsync(
        string project, IReadOnlyCollection<int> definitionIds, int perDefinition, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PipelineRun>> GetRunsAsync(
        string project, int definitionId, int top, string? branch = null, CancellationToken cancellationToken = default);

    Task<PipelineRun> GetRunAsync(string project, int buildId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TimelineRecord>> GetTimelineAsync(string project, PipelineRun run, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RunChange>> GetChangesAsync(string project, int buildId, CancellationToken cancellationToken = default);
}

public interface IGitClient
{
    Task<IReadOnlyList<Repository>> GetRepositoriesAsync(string project, CancellationToken cancellationToken = default);
}

public interface IPolicyClient
{
    /// <summary>Every policy configuration in the project, in one paged call.</summary>
    Task<IReadOnlyList<PolicyConfiguration>> GetPolicyConfigurationsAsync(string project, CancellationToken cancellationToken = default);
}

internal static class Paths
{
    public static string Project(string project) => Uri.EscapeDataString(project);
}

public sealed class ConnectionClient(AdoHttp http) : IConnectionClient
{
    public async Task<string> GetAuthenticatedUserAsync(CancellationToken cancellationToken = default)
    {
        var data = await http.GetAsync<ConnectionDataDto>("_apis/connectionData", cancellationToken, skipApiVersion: true);
        return data.AuthenticatedUser?.DisplayName ?? data.AuthorizedUser?.ProviderDisplayName ?? "(unknown user)";
    }
}

public sealed class ProjectClient(AdoHttp http, AdoCache cache, IOptionsMonitor<AzureDevOpsOptions> options) : IProjectClient
{
    public Task<IReadOnlyList<Project>> GetProjectsAsync(CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync<IReadOnlyList<Project>>("projects", TimeSpan.FromMinutes(options.CurrentValue.CacheMinutes.Pipelines), async ct =>
        {
            var projects = await http.GetListAsync<ProjectDto>("_apis/projects?stateFilter=wellFormed&$top=500", ct);
            return projects.Select(p => new Project(p.Id, p.Name)).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, cancellationToken);
}

public sealed class BuildClient(AdoHttp http, AdoCache cache, IOptionsMonitor<AzureDevOpsOptions> options) : IBuildClient
{
    // Keeps URLs well under length limits when asking for many pipelines at once.
    private const int DefinitionsPerCall = 50;

    private CacheMinutesOptions Minutes => options.CurrentValue.CacheMinutes;

    public Task<IReadOnlyList<PipelineDefinition>> GetDefinitionsAsync(string project, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync<IReadOnlyList<PipelineDefinition>>($"definitions:{project}", TimeSpan.FromMinutes(Minutes.Pipelines), async ct =>
        {
            // includeAllProperties brings back the repository (for the default branch) and the process type.
            var url = $"{Paths.Project(project)}/_apis/build/definitions?includeAllProperties=true&queryOrder=definitionNameAscending";
            var definitions = await http.GetListAsync<DefinitionDto>(url, ct);
            return definitions.Select(d => d.ToModel(project)).ToList();
        }, cancellationToken);

    public Task<IReadOnlyList<PipelineRun>> GetRecentRunsAsync(
        string project, IReadOnlyCollection<int> definitionIds, int perDefinition, CancellationToken cancellationToken = default)
    {
        var ids = definitionIds.Distinct().Order().ToList();
        var key = $"recent:{project}:{perDefinition}:{string.Join(',', ids)}";
        return cache.GetOrCreateAsync<IReadOnlyList<PipelineRun>>(key, TimeSpan.FromMinutes(Minutes.Runs), async ct =>
        {
            var runs = new List<PipelineRun>();
            foreach (var chunk in ids.Chunk(DefinitionsPerCall))
            {
                var url = $"{Paths.Project(project)}/_apis/build/builds?definitions={string.Join(',', chunk)}" +
                          $"&maxBuildsPerDefinition={perDefinition}&queryOrder=queueTimeDescending";
                var builds = await http.GetListAsync<BuildDto>(url, ct);
                runs.AddRange(builds.Select(b => b.ToModel(project)));
            }
            return runs;
        }, cancellationToken);
    }

    public Task<IReadOnlyList<PipelineRun>> GetRunsAsync(
        string project, int definitionId, int top, string? branch = null, CancellationToken cancellationToken = default)
    {
        var key = $"runs:{project}:{definitionId}:{top}:{branch}";
        return cache.GetOrCreateAsync<IReadOnlyList<PipelineRun>>(key, TimeSpan.FromMinutes(Minutes.Runs), async ct =>
        {
            var url = $"{Paths.Project(project)}/_apis/build/builds?definitions={definitionId}&$top={top}&queryOrder=queueTimeDescending";
            if (!string.IsNullOrEmpty(branch)) url += "&branchName=" + Uri.EscapeDataString(BranchNames.Full(branch));
            var builds = await http.GetListAsync<BuildDto>(url, ct, maxItems: top);
            return builds.Select(b => b.ToModel(project)).ToList();
        }, cancellationToken);
    }

    public Task<PipelineRun> GetRunAsync(string project, int buildId, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync($"run:{project}:{buildId}", TimeSpan.FromMinutes(Minutes.Runs), async ct =>
        {
            var build = await http.GetAsync<BuildDto>($"{Paths.Project(project)}/_apis/build/builds/{buildId}", ct);
            return build.ToModel(project);
        }, cancellationToken);

    public Task<IReadOnlyList<TimelineRecord>> GetTimelineAsync(string project, PipelineRun run, CancellationToken cancellationToken = default)
    {
        // A finished run's timeline doesn't change, so it can be cached for much longer.
        var ttl = run.IsCompleted ? TimeSpan.FromHours(12) : TimeSpan.FromSeconds(15);
        return cache.GetOrCreateAsync<IReadOnlyList<TimelineRecord>>($"timeline:{project}:{run.Id}:{run.IsCompleted}", ttl, async ct =>
        {
            var timeline = await http.GetAsync<TimelineDto>($"{Paths.Project(project)}/_apis/build/builds/{run.Id}/timeline", ct);
            return (timeline.Records ?? []).Select(r => r.ToModel()).ToList();
        }, cancellationToken);
    }

    public Task<IReadOnlyList<RunChange>> GetChangesAsync(string project, int buildId, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync<IReadOnlyList<RunChange>>($"changes:{project}:{buildId}", TimeSpan.FromMinutes(Minutes.Pipelines), async ct =>
        {
            var changes = await http.GetListAsync<ChangeDto>($"{Paths.Project(project)}/_apis/build/builds/{buildId}/changes?$top=50", ct, maxItems: 50);
            return changes.Select(c => c.ToModel()).ToList();
        }, cancellationToken);
}

public sealed class GitClient(AdoHttp http, AdoCache cache, IOptionsMonitor<AzureDevOpsOptions> options) : IGitClient
{
    public Task<IReadOnlyList<Repository>> GetRepositoriesAsync(string project, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync<IReadOnlyList<Repository>>($"repos:{project}", TimeSpan.FromMinutes(options.CurrentValue.CacheMinutes.Policies), async ct =>
        {
            var repos = await http.GetListAsync<RepositoryDto>($"{Paths.Project(project)}/_apis/git/repositories", ct);
            return repos.Select(r => r.ToModel(project)).OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }, cancellationToken);
}

public sealed class PolicyClient(AdoHttp http, AdoCache cache, IOptionsMonitor<AzureDevOpsOptions> options) : IPolicyClient
{
    public Task<IReadOnlyList<PolicyConfiguration>> GetPolicyConfigurationsAsync(string project, CancellationToken cancellationToken = default) =>
        cache.GetOrCreateAsync<IReadOnlyList<PolicyConfiguration>>($"policies:{project}", TimeSpan.FromMinutes(options.CurrentValue.CacheMinutes.Policies), async ct =>
        {
            var policies = await http.GetListAsync<PolicyConfigurationDto>($"{Paths.Project(project)}/_apis/policy/configurations", ct);
            return policies.Select(p => p.ToModel(project)).ToList();
        }, cancellationToken);
}
