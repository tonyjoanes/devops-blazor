using System.Globalization;
using System.Text.Json;
using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Core.Compliance;

public sealed record PolicyDescription(string Title, string ScopeText, IReadOnlyList<string> Details);

/// <summary>Turns a policy's raw settings into readable lines for the per-repo policy page.</summary>
public static class PolicyDescriber
{
    /// <param name="pipelineName">Optional lookup for build validation pipeline names by definition id.</param>
    public static PolicyDescription Describe(EffectivePolicy effective, Func<long, string?>? pipelineName = null)
    {
        var p = effective.Policy;
        var s = p.Settings;
        var details = new List<string>();

        switch (p.Kind)
        {
            case PolicyKind.MinimumReviewers:
                var count = s.GetLong("minimumApproverCount") ?? 1;
                details.Add($"Requires {count} {(count == 1 ? "approver" : "approvers")}");
                details.Add(s.GetBool("creatorVoteCounts") == true
                    ? "Authors' own approvals count towards the total"
                    : "Authors can't approve their own changes");
                if (s.GetBool("allowDownvotes") == true) details.Add("Can complete even if some reviewers vote 'Wait' or 'Reject'");
                if (s.GetBool("blockLastPusherVote") == true) details.Add("The most recent pusher can't approve");
                if (s.GetBool("requireVoteOnLastIteration") == true) details.Add("Approval required on the most recent iteration");
                if (s.GetBool("requireVoteOnEachIteration") == true) details.Add("Approval required on every iteration");
                details.Add(s.GetBool("resetOnSourcePush") == true
                    ? "All votes reset when new changes are pushed"
                    : "Votes are kept when new changes are pushed");
                if (s.GetBool("resetRejectionsOnSourcePush") == true) details.Add("Rejections reset when new changes are pushed");
                break;

            case PolicyKind.Build:
                var definitionId = s.GetLong("buildDefinitionId");
                var name = s.GetString("displayName")
                           ?? (definitionId is { } id ? pipelineName?.Invoke(id) ?? $"pipeline #{id}" : "unknown pipeline");
                details.Add($"Pipeline: {name}");
                var validMinutes = s.GetLong("validDuration");
                details.Add(validMinutes is null or 0
                    ? "Result never expires"
                    : $"Result expires after {FormatMinutes(validMinutes.Value)}");
                if (s.GetBool("manualQueueOnly") == true) details.Add("Only queued manually");
                if (s.GetBool("queueOnSourceUpdateOnly") == true) details.Add("Not re-queued when the target branch changes");
                AddPaths(details, s, "Only for paths");
                break;

            case PolicyKind.RequiredReviewers:
                var reviewers = s.ArrayLength("requiredReviewerIds");
                details.Add($"{reviewers} required {(reviewers == 1 ? "reviewer or group" : "reviewers or groups")}");
                if (s.GetLong("minimumApproverCount") is { } min) details.Add($"At least {min} of them must approve");
                if (s.GetBool("creatorVoteCounts") == true) details.Add("The author's own approval counts");
                AddPaths(details, s, "Only for paths");
                if (s.GetString("message") is { Length: > 0 } message) details.Add($"Message: {message}");
                break;

            case PolicyKind.CommentRequirements:
                details.Add("All comments must be resolved before completing");
                break;

            case PolicyKind.WorkItemLinking:
                details.Add("Pull requests must link at least one work item");
                break;

            case PolicyKind.MergeStrategy:
                var allowed = MergeTypes.Allowed(s);
                details.Add(allowed.Count == 0
                    ? "No merge types allowed"
                    : "Allowed: " + string.Join(", ", allowed.Order().Select(MergeTypes.DisplayName)));
                break;

            case PolicyKind.Status:
                var genre = s.GetString("statusGenre");
                var statusName = s.GetString("statusName");
                details.Add($"Status check: {string.Join("/", new[] { genre, statusName }.Where(x => !string.IsNullOrEmpty(x)))}");
                AddPaths(details, s, "Only for paths");
                break;

            case PolicyKind.FileSize:
                if (s.GetLong("maximumGitBlobSizeInBytes") is { } bytes)
                {
                    details.Add($"Blocks files larger than {(bytes / 1024d / 1024d).ToString("0.#", CultureInfo.InvariantCulture)} MB");
                }
                break;

            case PolicyKind.PathLength:
                if (s.GetLong("maxPathLength") is { } maxPath) details.Add($"Maximum path length {maxPath}");
                break;

            case PolicyKind.ReservedNames:
                details.Add("Blocks file and folder names reserved by Windows");
                break;

            case PolicyKind.CommitAuthorEmail:
                var patterns = s.GetStrings("authorEmailPatterns");
                if (patterns.Count > 0) details.Add("Allowed author emails: " + string.Join(", ", patterns));
                break;

            case PolicyKind.FileName:
                AddPaths(details, s, "Blocked file names");
                break;

            default:
                details.AddRange(GenericDetails(s));
                break;
        }

        if (!p.IsEnabled) details.Insert(0, "Disabled, so it has no effect");
        else if (!p.IsBlocking) details.Insert(0, "Optional: it doesn't block completion");

        return new PolicyDescription(p.TypeName, ScopeText(effective.Scope), details);
    }

    public static string ScopeText(PolicyScope scope)
    {
        var repos = scope.RepositoryId is null ? "All repos in the project" : "This repo";
        return scope.MatchKind switch
        {
            PolicyMatchKind.DefaultBranch => $"{repos} · default branch",
            _ when scope.RefName is null => $"{repos} · repository setting",
            PolicyMatchKind.Prefix => $"{repos} · branches starting {BranchNames.Short(scope.RefName)}",
            _ => $"{repos} · {BranchNames.Short(scope.RefName)}",
        };
    }

    private static void AddPaths(List<string> details, JsonElement settings, string label)
    {
        var paths = settings.GetStrings("filenamePatterns");
        if (paths.Count > 0) details.Add($"{label}: {string.Join(", ", paths)}");
    }

    private static string FormatMinutes(long minutes) =>
        minutes % 1440 == 0 ? $"{minutes / 1440} day(s)" : minutes % 60 == 0 ? $"{minutes / 60} hour(s)" : $"{minutes} minutes";

    private static IEnumerable<string> GenericDetails(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in settings.EnumerateObject())
        {
            if (property.NameEquals("scope")) continue;
            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) continue;
            yield return $"{property.Name}: {property.Value}";
        }
    }
}
