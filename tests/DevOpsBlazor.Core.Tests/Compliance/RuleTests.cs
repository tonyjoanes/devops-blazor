using DevOpsBlazor.Core.Compliance;
using DevOpsBlazor.Core.Compliance.Rules;
using DevOpsBlazor.Core.Models;
using static DevOpsBlazor.Core.Tests.TestData;

namespace DevOpsBlazor.Core.Tests.Compliance;

public class RuleTests
{
    private static RuleResult Evaluate(IComplianceRule rule, object? settings = null, params PolicyConfiguration[] policies)
    {
        var repo = Repo();
        var target = BranchTarget.For("default", repo);
        var context = new RuleContext(
            repo,
            target,
            EffectivePolicyResolver.ForBranch(repo, target, policies),
            EffectivePolicyResolver.ForRepository(repo, policies),
            settings is null ? NoSettings : Json(settings));
        return rule.Evaluate(context);
    }

    [Fact]
    public void Min_reviewers_checks_the_count_and_that_the_policy_is_blocking()
    {
        var rule = new MinReviewersRule();
        Assert.Equal(RuleStatus.Pass, Evaluate(rule, new { min = 2 }, MinReviewers(2)).Status);

        var tooFew = Evaluate(rule, new { min = 2 }, MinReviewers(1));
        Assert.Equal(RuleStatus.Fail, tooFew.Status);
        Assert.Contains("baseline requires 2", tooFew.Reason);

        var optional = Evaluate(rule, new { min = 2 }, MinReviewers(3, blocking: false));
        Assert.Equal(RuleStatus.Fail, optional.Status);
        Assert.Contains("optional", optional.Reason);

        Assert.Equal("No minimum reviewer policy.", Evaluate(rule).Reason);
    }

    [Fact]
    public void Min_reviewers_defaults_to_two()
    {
        Assert.Equal(RuleStatus.Fail, Evaluate(new MinReviewersRule(), null, MinReviewers(1)).Status);
    }

    [Fact]
    public void No_self_approval_passes_when_any_enforced_policy_excludes_the_author()
    {
        var rule = new NoSelfApprovalRule();
        Assert.Equal(RuleStatus.Fail, Evaluate(rule, null, MinReviewers(2, creatorVoteCounts: true)).Status);
        Assert.Equal(RuleStatus.Pass, Evaluate(rule, null, MinReviewers(2, creatorVoteCounts: true), MinReviewers(1)).Status);
        Assert.Equal(RuleStatus.Fail, Evaluate(rule).Status);
    }

    [Fact]
    public void Reset_votes_on_push()
    {
        var rule = new ResetVotesOnPushRule();
        Assert.Equal(RuleStatus.Pass, Evaluate(rule, null, MinReviewers(2, resetOnPush: true)).Status);
        Assert.Equal(RuleStatus.Fail, Evaluate(rule, null, MinReviewers(2, resetOnPush: false)).Status);
    }

    [Fact]
    public void Build_validation_warns_when_it_only_covers_some_paths()
    {
        var rule = new BuildValidationBlockingRule();
        Assert.Equal(RuleStatus.Pass, Evaluate(rule, null, Policy(PolicyKind.Build, new { buildDefinitionId = 1 })).Status);
        Assert.Equal(RuleStatus.Warn, Evaluate(rule, null, Policy(PolicyKind.Build, new { filenamePatterns = new[] { "/src/*" } })).Status);
        Assert.Equal(RuleStatus.Fail, Evaluate(rule, null, Policy(PolicyKind.Build, new { }, blocking: false)).Status);
        Assert.Contains("disabled", Evaluate(rule, null, Policy(PolicyKind.Build, new { }, enabled: false)).Reason);
    }

    [Fact]
    public void Comment_resolution_that_is_optional_is_a_warning()
    {
        var rule = new CommentResolutionRule();
        Assert.Equal(RuleStatus.Pass, Evaluate(rule, null, Policy(PolicyKind.CommentRequirements, new { })).Status);
        Assert.Equal(RuleStatus.Warn, Evaluate(rule, null, Policy(PolicyKind.CommentRequirements, new { }, blocking: false)).Status);
        Assert.Equal(RuleStatus.Fail, Evaluate(rule).Status);
    }

    [Fact]
    public void Merge_types_fail_when_more_types_are_allowed_than_the_baseline()
    {
        var rule = new AllowedMergeTypesRule();
        var squashOnly = Policy(PolicyKind.MergeStrategy, new { allowSquash = true });
        var squashAndRebase = Policy(PolicyKind.MergeStrategy, new { allowSquash = true, allowRebase = true });

        Assert.Equal(RuleStatus.Pass, Evaluate(rule, new { allowed = new[] { "squash" } }, squashOnly).Status);
        var result = Evaluate(rule, new { allowed = new[] { "squash" } }, squashAndRebase);
        Assert.Equal(RuleStatus.Fail, result.Status);
        Assert.Contains("Rebase and fast-forward", result.Reason);
        Assert.Equal(RuleStatus.Pass, Evaluate(rule, new { allowed = new[] { "squash", "rebase" } }, squashAndRebase).Status);
        Assert.Equal(RuleStatus.Fail, Evaluate(rule).Status);
    }

    [Fact]
    public void Merge_types_understand_the_legacy_use_squash_merge_setting()
    {
        Assert.Equal(RuleStatus.Pass, Evaluate(new AllowedMergeTypesRule(), null, Policy(PolicyKind.MergeStrategy, new { useSquashMerge = true })).Status);
    }

    [Fact]
    public void Policies_are_blocking_lists_the_optional_ones()
    {
        var rule = new PoliciesAreBlockingRule();
        var result = Evaluate(rule, null, MinReviewers(2), Policy(PolicyKind.WorkItemLinking, new { }, blocking: false));
        Assert.Equal(RuleStatus.Fail, result.Status);
        Assert.Contains("WorkItemLinking", result.Reason);
        Assert.Equal(RuleStatus.Pass, Evaluate(rule, null, MinReviewers(2), Policy(PolicyKind.Build, new { }, enabled: false, blocking: false)).Status);
        Assert.Equal(RuleStatus.Fail, Evaluate(rule).Status);
    }

    [Fact]
    public void Default_branch_name()
    {
        var rule = new DefaultBranchIsMainRule();
        Assert.Equal(RuleStatus.Pass, Evaluate(rule).Status);
        Assert.Equal(RuleStatus.Fail, Evaluate(rule, new { name = "trunk" }).Status);
    }

    [Fact]
    public void Max_file_size_reads_repository_policies()
    {
        var rule = new MaxFileSizeRule();
        var limit = Policy(PolicyKind.FileSize, new { maximumGitBlobSizeInBytes = 200L * 1024 * 1024 }, scopes: new PolicyScope(null, null, PolicyMatchKind.Exact));
        Assert.Equal(RuleStatus.Pass, Evaluate(rule, null, limit).Status);
        Assert.Equal(RuleStatus.Fail, Evaluate(rule, new { maxMB = 100 }, limit).Status);
        Assert.Equal(RuleStatus.Fail, Evaluate(rule).Status);
    }
}
