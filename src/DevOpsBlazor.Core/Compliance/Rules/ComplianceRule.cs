using System.Text.Json;
using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Core.Compliance.Rules;

public enum RuleStatus { Pass, Warn, Fail, NotApplicable, Exempt }

public enum RuleScope
{
    /// <summary>Evaluated once per branch listed in the baseline.</summary>
    Branch,

    /// <summary>Evaluated once per repository.</summary>
    Repository,
}

public sealed record RuleResult(RuleStatus Status, string Reason)
{
    public static RuleResult Pass(string reason) => new(RuleStatus.Pass, reason);
    public static RuleResult Warn(string reason) => new(RuleStatus.Warn, reason);
    public static RuleResult Fail(string reason) => new(RuleStatus.Fail, reason);
    public static RuleResult NotApplicable(string reason) => new(RuleStatus.NotApplicable, reason);
}

public sealed record RuleContext(
    Repository Repository,
    BranchTarget? Branch,
    IReadOnlyList<EffectivePolicy> BranchPolicies,
    IReadOnlyList<EffectivePolicy> RepositoryPolicies,
    JsonElement Settings)
{
    public IReadOnlyList<EffectivePolicy> OfKind(PolicyKind kind) => BranchPolicies.Where(p => p.Kind == kind).ToList();
    public IReadOnlyList<EffectivePolicy> Enforced(PolicyKind kind) => BranchPolicies.Where(p => p.Kind == kind && p.IsEnforced).ToList();
}

public interface IComplianceRule
{
    string Id { get; }
    string Title { get; }
    string Description { get; }
    RuleScope Scope { get; }
    RuleResult Evaluate(RuleContext context);
}
