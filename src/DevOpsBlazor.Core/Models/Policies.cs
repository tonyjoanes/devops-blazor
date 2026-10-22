using System.Text.Json;

namespace DevOpsBlazor.Core.Models;

public enum PolicyKind
{
    Other,
    MinimumReviewers,
    Build,
    RequiredReviewers,
    CommentRequirements,
    WorkItemLinking,
    MergeStrategy,
    Status,
    FileSize,
    FileName,
    PathLength,
    ReservedNames,
    CommitAuthorEmail,
}

public enum PolicyMatchKind { Exact, Prefix, DefaultBranch }

/// <summary>One entry of a policy's <c>settings.scope</c> array.</summary>
/// <param name="RepositoryId">Null means every repository in the project.</param>
/// <param name="RefName">Null (with a non-DefaultBranch match kind) means the policy applies to the repository itself, not a branch.</param>
public sealed record PolicyScope(Guid? RepositoryId, string? RefName, PolicyMatchKind MatchKind)
{
    public bool IsRepositoryLevel => RefName is null && MatchKind != PolicyMatchKind.DefaultBranch;
}

public sealed record PolicyConfiguration(
    int Id,
    string Project,
    Guid TypeId,
    string TypeName,
    PolicyKind Kind,
    bool IsEnabled,
    bool IsBlocking,
    bool IsDeleted,
    IReadOnlyList<PolicyScope> Scopes,
    JsonElement Settings);

public static class PolicyClassifier
{
    // Ids that are stable across all organisations. Anything else is matched on display name.
    private static readonly Dictionary<Guid, PolicyKind> KnownIds = new()
    {
        [Guid.Parse("fa4e907d-c16b-4a4c-9dfa-4906e5d171dd")] = PolicyKind.MinimumReviewers,
        [Guid.Parse("0609b952-1397-4640-95ec-e00a01b2c241")] = PolicyKind.Build,
        [Guid.Parse("fd2167ab-b0be-447a-8ec8-39368250530e")] = PolicyKind.RequiredReviewers,
        [Guid.Parse("c6a1889d-b943-4856-b76f-9e46bb6b0df2")] = PolicyKind.CommentRequirements,
        [Guid.Parse("40e92b44-2fe1-4dd6-b3d8-74a9c21d0c6e")] = PolicyKind.WorkItemLinking,
    };

    private static readonly (string Name, PolicyKind Kind)[] KnownNames =
    [
        ("Minimum number of reviewers", PolicyKind.MinimumReviewers),
        ("Required reviewers", PolicyKind.RequiredReviewers),
        ("Comment requirements", PolicyKind.CommentRequirements),
        ("Work item linking", PolicyKind.WorkItemLinking),
        ("Require a merge strategy", PolicyKind.MergeStrategy),
        ("File size restriction", PolicyKind.FileSize),
        ("File name restriction", PolicyKind.FileName),
        ("Path Length restriction", PolicyKind.PathLength),
        ("Reserved names restriction", PolicyKind.ReservedNames),
        ("Commit author email validation", PolicyKind.CommitAuthorEmail),
        ("Status", PolicyKind.Status),
        ("Build", PolicyKind.Build),
    ];

    public static PolicyKind Classify(Guid typeId, string? displayName)
    {
        if (KnownIds.TryGetValue(typeId, out var kind)) return kind;
        foreach (var (name, k) in KnownNames)
        {
            if (string.Equals(name, displayName?.Trim(), StringComparison.OrdinalIgnoreCase)) return k;
        }
        return PolicyKind.Other;
    }
}
