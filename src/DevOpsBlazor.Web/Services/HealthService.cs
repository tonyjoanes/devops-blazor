using DevOpsBlazor.AzureDevOps;
using DevOpsBlazor.AzureDevOps.Clients;
using Microsoft.Extensions.Options;

namespace DevOpsBlazor.Web.Services;

public sealed record ScopeCheck(string Scope, string What, bool Ok, string Detail);

public sealed record HealthReport(string? User, string? ConnectionError, IReadOnlyList<string> Projects, IReadOnlyList<ScopeCheck> Checks);

/// <summary>Checks the PAT works and has each scope the app needs, by making one small read per scope.</summary>
public sealed class HealthService(
    IConnectionClient connection,
    IProjectClient projectClient,
    IBuildClient builds,
    IGitClient git,
    IPolicyClient policies,
    ProjectScope scope)
{
    public async Task<HealthReport> CheckAsync(CancellationToken cancellationToken = default)
    {
        string user;
        try
        {
            user = await connection.GetAuthenticatedUserAsync(cancellationToken);
        }
        catch (AdoApiException ex)
        {
            return new HealthReport(null, ex.Message, [], []);
        }

        var checks = new List<ScopeCheck>();
        IReadOnlyList<string> projects = [];
        var projectCheck = await RunAsync("Project and Team (Read)", "List projects", async () =>
        {
            await projectClient.GetProjectsAsync(cancellationToken);
            projects = await scope.GetProjectNamesAsync(cancellationToken);
            return $"{projects.Count} project(s) in scope";
        });
        checks.Add(projectCheck);

        if (projects.Count > 0)
        {
            var first = projects[0];
            checks.Add(await RunAsync("Build (Read)", $"List pipelines in {first}", async () =>
                $"{(await builds.GetDefinitionsAsync(first, cancellationToken)).Count} pipeline(s)"));
            checks.Add(await RunAsync("Code (Read)", $"List repositories in {first}", async () =>
                $"{(await git.GetRepositoriesAsync(first, cancellationToken)).Count} repositories"));
            checks.Add(await RunAsync("Code (Read)", $"Read branch policies in {first}", async () =>
                $"{(await policies.GetPolicyConfigurationsAsync(first, cancellationToken)).Count} policy configuration(s)"));
        }

        return new HealthReport(user, null, projects, checks);
    }

    private static async Task<ScopeCheck> RunAsync(string scopeName, string what, Func<Task<string>> check)
    {
        try
        {
            return new ScopeCheck(scopeName, what, true, await check());
        }
        catch (AdoApiException ex)
        {
            return new ScopeCheck(scopeName, what, false, ex.Message);
        }
    }
}
