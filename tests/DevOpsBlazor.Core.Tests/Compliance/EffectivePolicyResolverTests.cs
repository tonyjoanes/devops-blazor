using DevOpsBlazor.Core.Compliance;
using DevOpsBlazor.Core.Models;
using static DevOpsBlazor.Core.Tests.TestData;

namespace DevOpsBlazor.Core.Tests.Compliance;

public class EffectivePolicyResolverTests
{
    private static readonly Repository Api = Repo();
    private static readonly BranchTarget MainTarget = BranchTarget.For("default", Api);

    private static PolicyConfiguration Scoped(params PolicyScope[] scopes) => Policy(PolicyKind.CommentRequirements, new { }, scopes: scopes);

    [Fact]
    public void Exact_scope_on_this_repo_and_branch_applies()
    {
        var policy = Scoped(new PolicyScope(RepoId, Main, PolicyMatchKind.Exact));
        var effective = Assert.Single(EffectivePolicyResolver.ForBranch(Api, MainTarget, [policy]));
        Assert.Equal(PolicySource.Repository, effective.Source);
    }

    [Fact]
    public void Scopes_for_other_repos_or_branches_do_not_apply()
    {
        var otherRepo = Scoped(new PolicyScope(OtherRepoId, Main, PolicyMatchKind.Exact));
        var otherBranch = Scoped(new PolicyScope(RepoId, "refs/heads/develop", PolicyMatchKind.Exact));
        Assert.Empty(EffectivePolicyResolver.ForBranch(Api, MainTarget, [otherRepo, otherBranch]));
    }

    [Fact]
    public void Project_wide_scopes_apply_and_are_marked_as_project_policies()
    {
        var projectWide = Scoped(new PolicyScope(null, Main, PolicyMatchKind.Exact));
        Assert.Equal(PolicySource.Project, Assert.Single(EffectivePolicyResolver.ForBranch(Api, MainTarget, [projectWide])).Source);
    }

    [Fact]
    public void Default_branch_match_kind_follows_each_repos_default_branch()
    {
        var policy = Scoped(new PolicyScope(null, null, PolicyMatchKind.DefaultBranch));
        var master = Repo("legacy", "refs/heads/master", id: OtherRepoId);

        Assert.Single(EffectivePolicyResolver.ForBranch(master, BranchTarget.For("default", master), [policy]));
        Assert.Empty(EffectivePolicyResolver.ForBranch(Api, BranchTarget.For("develop", Api), [policy]));
    }

    [Fact]
    public void Prefix_scope_covers_matching_branches_and_branch_patterns()
    {
        var policy = Scoped(new PolicyScope(RepoId, "refs/heads/release/", PolicyMatchKind.Prefix));

        Assert.Single(EffectivePolicyResolver.ForBranch(Api, BranchTarget.For("release/*", Api), [policy]));
        Assert.Single(EffectivePolicyResolver.ForBranch(Api, BranchTarget.For("release/1.0", Api), [policy]));
        Assert.Empty(EffectivePolicyResolver.ForBranch(Api, MainTarget, [policy]));
    }

    [Fact]
    public void An_exact_policy_on_one_release_branch_does_not_cover_the_whole_pattern()
    {
        var policy = Scoped(new PolicyScope(RepoId, "refs/heads/release/1.0", PolicyMatchKind.Exact));
        Assert.Empty(EffectivePolicyResolver.ForBranch(Api, BranchTarget.For("release/*", Api), [policy]));
    }

    [Fact]
    public void Deleted_policies_are_ignored_but_disabled_ones_are_kept()
    {
        var deleted = Policy(PolicyKind.Build, new { }, deleted: true);
        var disabled = Policy(PolicyKind.Build, new { }, enabled: false);
        var effective = Assert.Single(EffectivePolicyResolver.ForBranch(Api, MainTarget, [deleted, disabled]));
        Assert.False(effective.IsEnabled);
    }

    [Fact]
    public void Repository_level_policies_have_no_branch()
    {
        var fileSize = Policy(PolicyKind.FileSize, new { maximumGitBlobSizeInBytes = 1024 }, scopes: new PolicyScope(null, null, PolicyMatchKind.Exact));
        var branchPolicy = Policy(PolicyKind.Build, new { });

        Assert.Equal(PolicyKind.FileSize, Assert.Single(EffectivePolicyResolver.ForRepository(Api, [fileSize, branchPolicy])).Kind);
        Assert.Empty(EffectivePolicyResolver.ForBranch(Api, MainTarget, [fileSize]));
    }

    [Fact]
    public void Repos_without_a_default_branch_are_not_applicable()
    {
        var empty = Repo(defaultBranch: null);
        var target = BranchTarget.For("default", empty);
        Assert.NotNull(target.NotApplicableReason);
        Assert.Empty(EffectivePolicyResolver.ForBranch(empty, target, [Policy(PolicyKind.Build, new { })]));
    }
}
