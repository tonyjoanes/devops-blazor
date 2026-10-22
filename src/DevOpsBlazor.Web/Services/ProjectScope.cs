using DevOpsBlazor.AzureDevOps;
using DevOpsBlazor.AzureDevOps.Clients;
using Microsoft.Extensions.Options;

namespace DevOpsBlazor.Web.Services;

/// <summary>The projects the app works across: those listed in config, or every project the PAT can see.</summary>
public sealed class ProjectScope(IProjectClient projects, IOptionsMonitor<AzureDevOpsOptions> options)
{
    public async Task<IReadOnlyList<string>> GetProjectNamesAsync(CancellationToken cancellationToken = default)
    {
        var configured = options.CurrentValue.Projects.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var all = await projects.GetProjectsAsync(cancellationToken);
        if (configured.Count == 0) return all.Select(p => p.Name).ToList();

        // Use Azure DevOps' spelling where it matches, but keep configured names it doesn't know so errors show up clearly.
        return configured
            .Select(c => all.FirstOrDefault(p => p.Name.Equals(c, StringComparison.OrdinalIgnoreCase))?.Name ?? c)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>One named project, or every project in scope when <paramref name="project"/> is null or "*".</summary>
    public async Task<IReadOnlyList<string>> ResolveAsync(string? project, CancellationToken cancellationToken = default) =>
        string.IsNullOrEmpty(project) || project == AllProjects
            ? await GetProjectNamesAsync(cancellationToken)
            : [project];

    public const string AllProjects = "*";
}
