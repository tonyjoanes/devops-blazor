using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Core.Compliance;

/// <summary>A branch the baseline wants protected, resolved for one repository.</summary>
/// <param name="Label">As written in the baseline: "default", "main", "release/*".</param>
/// <param name="RefName">Full ref (or ref prefix for patterns), or null when it doesn't apply to this repo.</param>
/// <param name="IsPattern">True for "release/*": only prefix-scoped policies cover every matching branch.</param>
public sealed record BranchTarget(string Label, string? RefName, bool IsPattern, string? NotApplicableReason = null)
{
    public const string DefaultLabel = "default";

    public static BranchTarget For(string label, Repository repository)
    {
        if (label.Equals(DefaultLabel, StringComparison.OrdinalIgnoreCase))
        {
            return repository.DefaultBranch is { Length: > 0 } branch
                ? new BranchTarget(label, branch, false)
                : new BranchTarget(label, null, false, "The repository has no default branch. It's probably empty.");
        }
        if (label.EndsWith('*'))
        {
            var prefix = BranchNames.Full(label.TrimEnd('*'));
            return new BranchTarget(label, prefix, true);
        }
        return new BranchTarget(label, BranchNames.Full(label), false);
    }

    public string DisplayName(Repository repository) =>
        Label.Equals(DefaultLabel, StringComparison.OrdinalIgnoreCase) && repository.DefaultBranch is { } b
            ? $"{BranchNames.Short(b)} (default)"
            : Label;
}

public enum PolicySource
{
    /// <summary>Scoped to this repository.</summary>
    Repository,

    /// <summary>Scoped to every repository in the project (repositoryId is null).</summary>
    Project,
}

public sealed record EffectivePolicy(PolicyConfiguration Policy, PolicyScope Scope, PolicySource Source)
{
    public bool IsEnabled => Policy.IsEnabled;
    public bool IsEnforced => Policy.IsEnabled && Policy.IsBlocking;
    public PolicyKind Kind => Policy.Kind;
}

/// <summary>
/// Works out which policy configurations actually apply to a repository's branch or to the repository itself.
/// Policies can be scoped to a repo and branch, a branch prefix, every repo in the project, or every repo's default branch.
/// </summary>
public static class EffectivePolicyResolver
{
    public static IReadOnlyList<EffectivePolicy> ForBranch(
        Repository repository, BranchTarget target, IEnumerable<PolicyConfiguration> policies)
    {
        if (target.RefName is null) return [];
        var result = new List<EffectivePolicy>();
        foreach (var policy in policies.Where(p => !p.IsDeleted))
        {
            var scope = policy.Scopes.FirstOrDefault(s => AppliesToRepository(s, repository) && AppliesToBranch(s, repository, target));
            if (scope is not null) result.Add(new EffectivePolicy(policy, scope, SourceOf(scope)));
        }
        return result;
    }

    /// <summary>Policies that apply to the repository as a whole rather than a branch (file size, path length, reserved names and so on).</summary>
    public static IReadOnlyList<EffectivePolicy> ForRepository(Repository repository, IEnumerable<PolicyConfiguration> policies)
    {
        var result = new List<EffectivePolicy>();
        foreach (var policy in policies.Where(p => !p.IsDeleted))
        {
            if (policy.Scopes.Count == 0)
            {
                result.Add(new EffectivePolicy(policy, new PolicyScope(null, null, PolicyMatchKind.Exact), PolicySource.Project));
                continue;
            }
            var scope = policy.Scopes.FirstOrDefault(s => s.IsRepositoryLevel && AppliesToRepository(s, repository));
            if (scope is not null) result.Add(new EffectivePolicy(policy, scope, SourceOf(scope)));
        }
        return result;
    }

    private static PolicySource SourceOf(PolicyScope scope) =>
        scope.RepositoryId is null ? PolicySource.Project : PolicySource.Repository;

    private static bool AppliesToRepository(PolicyScope scope, Repository repository) =>
        scope.RepositoryId is null || scope.RepositoryId == repository.Id;

    private static bool AppliesToBranch(PolicyScope scope, Repository repository, BranchTarget target) =>
        scope.MatchKind switch
        {
            PolicyMatchKind.DefaultBranch =>
                !target.IsPattern && repository.DefaultBranch is { } d && Same(d, target.RefName),
            PolicyMatchKind.Exact =>
                scope.RefName is not null && !target.IsPattern && Same(scope.RefName, target.RefName),
            PolicyMatchKind.Prefix =>
                scope.RefName is not null && target.RefName!.StartsWith(scope.RefName, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };

    private static bool Same(string a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
