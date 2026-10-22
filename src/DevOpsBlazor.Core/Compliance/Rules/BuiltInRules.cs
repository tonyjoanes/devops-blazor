using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Core.Compliance.Rules;

public static class BuiltInRules
{
    public static IReadOnlyDictionary<string, IComplianceRule> All { get; } = new IComplianceRule[]
    {
        new MinReviewersRule(),
        new NoSelfApprovalRule(),
        new ResetVotesOnPushRule(),
        new BuildValidationBlockingRule(),
        new CommentResolutionRule(),
        new LinkedWorkItemsRule(),
        new AllowedMergeTypesRule(),
        new PoliciesAreBlockingRule(),
        new DefaultBranchIsMainRule(),
        new MaxFileSizeRule(),
    }.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

    /// <summary>Shared wording for "an enforced policy of this kind is required".</summary>
    /// <param name="name">Lower-case policy name for messages, e.g. "build validation".</param>
    internal static RuleResult RequireEnforced(RuleContext context, PolicyKind kind, string name, string passReason, bool optionalIsWarning)
    {
        if (context.Enforced(kind).Count > 0) return RuleResult.Pass(passReason);
        var all = context.OfKind(kind);
        if (all.Any(p => p.IsEnabled))
        {
            var reason = $"The {name} policy is configured but optional, so it doesn't block completion.";
            return optionalIsWarning ? RuleResult.Warn(reason) : RuleResult.Fail(reason);
        }
        if (all.Count > 0) return RuleResult.Fail($"The {name} policy exists but is disabled.");
        return RuleResult.Fail($"No {name} policy.");
    }
}

public sealed class MinReviewersRule : IComplianceRule
{
    public string Id => "MinReviewers";
    public string Title => "Min reviewers";
    public string Description => "An enforced minimum reviewer policy that requires at least \"min\" approvers (default 2).";
    public RuleScope Scope => RuleScope.Branch;

    public RuleResult Evaluate(RuleContext context)
    {
        var min = context.Settings.GetLong("min") ?? 2;
        var enforced = context.Enforced(PolicyKind.MinimumReviewers);
        if (enforced.Count == 0)
        {
            return BuiltInRules.RequireEnforced(context, PolicyKind.MinimumReviewers, "minimum reviewer", "", optionalIsWarning: false);
        }
        var best = enforced.Max(p => p.Policy.Settings.GetLong("minimumApproverCount") ?? 1);
        return best >= min
            ? RuleResult.Pass($"Requires {best} {Approvers(best)}.")
            : RuleResult.Fail($"Requires {best} {Approvers(best)}; the baseline requires {min}.");
    }

    private static string Approvers(long n) => n == 1 ? "approver" : "approvers";
}

public sealed class NoSelfApprovalRule : IComplianceRule
{
    public string Id => "NoSelfApproval";
    public string Title => "No self-approval";
    public string Description => "Authors can't approve their own pull requests (\"Allow requestors to approve their own changes\" is off).";
    public RuleScope Scope => RuleScope.Branch;

    public RuleResult Evaluate(RuleContext context)
    {
        var enforced = context.Enforced(PolicyKind.MinimumReviewers);
        if (enforced.Count == 0) return RuleResult.Fail("No enforced minimum reviewer policy, so nothing stops self-approval.");
        return enforced.Any(p => p.Policy.Settings.GetBool("creatorVoteCounts") != true)
            ? RuleResult.Pass("Authors' own approvals don't count.")
            : RuleResult.Fail("Authors can approve their own pull requests (\"Allow requestors to approve their own changes\" is on).");
    }
}

public sealed class ResetVotesOnPushRule : IComplianceRule
{
    public string Id => "ResetVotesOnPush";
    public string Title => "Reset votes on push";
    public string Description => "Approvals are reset (or must be on the latest iteration) when new changes are pushed.";
    public RuleScope Scope => RuleScope.Branch;

    public RuleResult Evaluate(RuleContext context)
    {
        var enforced = context.Enforced(PolicyKind.MinimumReviewers);
        if (enforced.Count == 0) return RuleResult.Fail("No enforced minimum reviewer policy.");
        var resets = enforced.Any(p =>
            p.Policy.Settings.GetBool("resetOnSourcePush") == true || p.Policy.Settings.GetBool("requireVoteOnLastIteration") == true);
        return resets
            ? RuleResult.Pass("Approvals must be given on the latest changes.")
            : RuleResult.Fail("Approvals stay in place when new changes are pushed after review.");
    }
}

public sealed class BuildValidationBlockingRule : IComplianceRule
{
    public string Id => "BuildValidationBlocking";
    public string Title => "Build validation";
    public string Description => "At least one enabled, blocking build validation policy that covers every path.";
    public RuleScope Scope => RuleScope.Branch;

    public RuleResult Evaluate(RuleContext context)
    {
        var enforced = context.Enforced(PolicyKind.Build);
        var unfiltered = enforced.Where(p => p.Policy.Settings.GetStrings("filenamePatterns").Count == 0).ToList();
        if (unfiltered.Count > 0)
        {
            return RuleResult.Pass($"{unfiltered.Count} blocking build validation {(unfiltered.Count == 1 ? "policy" : "policies")}.");
        }
        if (enforced.Count > 0)
        {
            var paths = enforced.SelectMany(p => p.Policy.Settings.GetStrings("filenamePatterns")).Distinct();
            return RuleResult.Warn($"Build validation only runs for some paths: {string.Join(", ", paths)}.");
        }
        return BuiltInRules.RequireEnforced(context, PolicyKind.Build, "build validation", "", optionalIsWarning: false);
    }
}

public sealed class CommentResolutionRule : IComplianceRule
{
    public string Id => "CommentResolution";
    public string Title => "Comment resolution";
    public string Description => "All pull request comments must be resolved before completion.";
    public RuleScope Scope => RuleScope.Branch;

    public RuleResult Evaluate(RuleContext context) =>
        BuiltInRules.RequireEnforced(context, PolicyKind.CommentRequirements, "comment resolution",
            "Comments must be resolved.", optionalIsWarning: true);
}

public sealed class LinkedWorkItemsRule : IComplianceRule
{
    public string Id => "LinkedWorkItems";
    public string Title => "Linked work items";
    public string Description => "Pull requests must link a work item.";
    public RuleScope Scope => RuleScope.Branch;

    public RuleResult Evaluate(RuleContext context) =>
        BuiltInRules.RequireEnforced(context, PolicyKind.WorkItemLinking, "work item linking",
            "Pull requests must link a work item.", optionalIsWarning: true);
}

public sealed class AllowedMergeTypesRule : IComplianceRule
{
    public string Id => "AllowedMergeTypes";
    public string Title => "Merge types";
    public string Description => "A merge strategy policy that allows only the types in \"allowed\" (squash, noFastForward, rebase, rebaseMerge). Default: squash.";
    public RuleScope Scope => RuleScope.Branch;

    public RuleResult Evaluate(RuleContext context)
    {
        var wanted = context.Settings.GetStrings("allowed")
            .Select(s => MergeTypes.TryParse(s, out var t) ? t : (MergeType?)null)
            .Where(t => t is not null)
            .Select(t => t!.Value)
            .ToHashSet();
        if (wanted.Count == 0) wanted.Add(MergeType.Squash);

        var enforced = context.Enforced(PolicyKind.MergeStrategy);
        if (enforced.Count == 0) return RuleResult.Fail("No enforced merge strategy policy, so every merge type is allowed.");

        // Every blocking policy must be satisfied, so only types allowed by all of them can be used.
        var allowed = enforced
            .Select(p => MergeTypes.Allowed(p.Policy.Settings))
            .Aggregate((IEnumerable<MergeType>)Enum.GetValues<MergeType>(), (acc, set) => acc.Intersect(set))
            .ToHashSet();
        var extra = allowed.Except(wanted).Order().ToList();
        var allowedText = allowed.Count == 0 ? "none" : string.Join(", ", allowed.Order().Select(MergeTypes.DisplayName));
        return extra.Count == 0
            ? RuleResult.Pass($"Allows: {allowedText}.")
            : RuleResult.Fail($"Also allows {string.Join(", ", extra.Select(MergeTypes.DisplayName))} (allows: {allowedText}).");
    }
}

public sealed class PoliciesAreBlockingRule : IComplianceRule
{
    public string Id => "PoliciesAreBlocking";
    public string Title => "All blocking";
    public string Description => "Every enabled branch policy is blocking (required), not optional.";
    public RuleScope Scope => RuleScope.Branch;

    public RuleResult Evaluate(RuleContext context)
    {
        var enabled = context.BranchPolicies.Where(p => p.IsEnabled).ToList();
        if (enabled.Count == 0) return RuleResult.Fail("No enabled branch policies at all.");
        var optional = enabled.Where(p => !p.Policy.IsBlocking).Select(p => p.Policy.TypeName).Distinct().ToList();
        if (optional.Count > 0) return RuleResult.Fail($"Optional (not blocking): {string.Join(", ", optional)}.");
        var disabled = context.BranchPolicies.Count(p => !p.IsEnabled);
        var note = disabled > 0 ? $" {disabled} disabled {(disabled == 1 ? "policy is" : "policies are")} ignored." : "";
        return RuleResult.Pass($"All {enabled.Count} enabled policies are blocking.{note}");
    }
}

public sealed class DefaultBranchIsMainRule : IComplianceRule
{
    public string Id => "DefaultBranchIsMain";
    public string Title => "Default branch";
    public string Description => "The default branch is named \"name\" (default: main).";
    public RuleScope Scope => RuleScope.Repository;

    public RuleResult Evaluate(RuleContext context)
    {
        var expected = context.Settings.GetString("name") ?? "main";
        if (context.Repository.DefaultBranch is not { } branch) return RuleResult.NotApplicable("The repository has no default branch.");
        var actual = BranchNames.Short(branch);
        return actual.Equals(expected, StringComparison.OrdinalIgnoreCase)
            ? RuleResult.Pass($"Default branch is {actual}.")
            : RuleResult.Fail($"Default branch is {actual}; the baseline expects {expected}.");
    }
}

public sealed class MaxFileSizeRule : IComplianceRule
{
    public string Id => "MaxFileSize";
    public string Title => "File size limit";
    public string Description => "An enforced file size restriction, optionally no larger than \"maxMB\".";
    public RuleScope Scope => RuleScope.Repository;

    public RuleResult Evaluate(RuleContext context)
    {
        var policies = context.RepositoryPolicies.Where(p => p.Kind == PolicyKind.FileSize && p.IsEnforced).ToList();
        if (policies.Count == 0) return RuleResult.Fail("No enforced file size restriction.");
        var limitBytes = policies.Min(p => p.Policy.Settings.GetLong("maximumGitBlobSizeInBytes") ?? long.MaxValue);
        var limitMb = limitBytes / 1024d / 1024d;
        var maxMb = context.Settings.GetLong("maxMB");
        if (maxMb is { } max && limitMb > max) return RuleResult.Fail($"Limit is {limitMb:0.#} MB; the baseline allows at most {max} MB.");
        return RuleResult.Pass($"Files over {limitMb:0.#} MB are blocked.");
    }
}
