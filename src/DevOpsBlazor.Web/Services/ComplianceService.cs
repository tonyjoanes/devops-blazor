using DevOpsBlazor.AzureDevOps.Clients;
using DevOpsBlazor.Core.Compliance;
using DevOpsBlazor.Core.Models;
using Microsoft.Extensions.Options;

namespace DevOpsBlazor.Web.Services;

public sealed class ComplianceOptions
{
    /// <summary>Path to the baseline JSON, relative to the app's content root unless absolute.</summary>
    public string BaselineFile { get; set; } = "compliance-baseline.json";
}

public sealed record BaselineLoad(ComplianceBaseline? Baseline, string Path, string? Error);

public sealed record BranchPolicies(BranchTarget Target, IReadOnlyList<EffectivePolicy> Policies);

public sealed record RepoPolicyDetail(
    Repository Repository,
    ComplianceReport Report,
    IReadOnlyList<BranchPolicies> Branches,
    IReadOnlyList<EffectivePolicy> RepositoryPolicies,
    IReadOnlyDictionary<long, string> PipelineNames);

public sealed class ComplianceService(
    IGitClient git,
    IPolicyClient policyClient,
    IBuildClient builds,
    ProjectScope scope,
    IOptionsMonitor<ComplianceOptions> options,
    IWebHostEnvironment environment,
    ILogger<ComplianceService> logger)
{
    /// <summary>Reads the baseline from disk every time, so edits apply on the next refresh without a restart.</summary>
    public BaselineLoad LoadBaseline()
    {
        var path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.CurrentValue.BaselineFile));
        try
        {
            if (!File.Exists(path)) return new BaselineLoad(null, path, $"The baseline file wasn't found at {path}.");
            return new BaselineLoad(BaselineParser.Parse(File.ReadAllText(path)), path, null);
        }
        catch (FormatException ex)
        {
            return new BaselineLoad(null, path, ex.Message);
        }
        catch (IOException ex)
        {
            return new BaselineLoad(null, path, $"Couldn't read {path}: {ex.Message}");
        }
    }

    public async Task<ComplianceReport> EvaluateAsync(ComplianceBaseline baseline, string? project, CancellationToken cancellationToken = default)
    {
        var projects = await scope.ResolveAsync(project, cancellationToken);
        var repositories = new List<Repository>();
        var policies = new List<PolicyConfiguration>();
        var gate = new SemaphoreSlim(1);

        await Parallel.ForEachAsync(projects, new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = cancellationToken },
            async (name, ct) =>
            {
                var repos = git.GetRepositoriesAsync(name, ct);
                var configs = policyClient.GetPolicyConfigurationsAsync(name, ct);
                await Task.WhenAll(repos, configs);
                await gate.WaitAsync(ct);
                try
                {
                    repositories.AddRange(repos.Result);
                    policies.AddRange(configs.Result);
                }
                finally { gate.Release(); }
            });

        return ComplianceEvaluator.Evaluate(baseline, repositories, policies, DateTimeOffset.UtcNow);
    }

    public async Task<RepoPolicyDetail?> GetRepoDetailAsync(
        ComplianceBaseline baseline, string project, Guid repositoryId, CancellationToken cancellationToken = default)
    {
        var repos = await git.GetRepositoriesAsync(project, cancellationToken);
        var repository = repos.FirstOrDefault(r => r.Id == repositoryId);
        if (repository is null) return null;

        var policies = await policyClient.GetPolicyConfigurationsAsync(project, cancellationToken);
        var report = ComplianceEvaluator.Evaluate(baseline, [repository], policies, DateTimeOffset.UtcNow);

        var branchLabels = baseline.Branches.Prepend(BranchTarget.DefaultLabel).Distinct(StringComparer.OrdinalIgnoreCase);
        var branches = branchLabels
            .Select(label => BranchTarget.For(label, repository))
            .Select(target => new BranchPolicies(target, EffectivePolicyResolver.ForBranch(repository, target, policies)))
            .ToList();

        return new RepoPolicyDetail(
            repository,
            report,
            branches,
            EffectivePolicyResolver.ForRepository(repository, policies),
            await PipelineNamesAsync(project, cancellationToken));
    }

    /// <summary>Names for build validation policies. Optional: the page still works if pipelines can't be read.</summary>
    private async Task<IReadOnlyDictionary<long, string>> PipelineNamesAsync(string project, CancellationToken cancellationToken)
    {
        try
        {
            var definitions = await builds.GetDefinitionsAsync(project, cancellationToken);
            return definitions.ToDictionary(d => (long)d.Id, d => d.Name);
        }
        catch (DevOpsBlazor.AzureDevOps.AdoApiException ex)
        {
            logger.LogInformation("Pipeline names unavailable for {Project}: {Message}", project, ex.Message);
            return new Dictionary<long, string>();
        }
    }
}
