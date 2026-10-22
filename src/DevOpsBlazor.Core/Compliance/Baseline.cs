using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Core.Compliance;

public enum RuleSeverity { High, Medium, Low }

/// <param name="Settings">The rule's whole JSON entry, so rules can read their own parameters (e.g. "min").</param>
public sealed record BaselineRule(string Id, RuleSeverity Severity, JsonElement Settings);

/// <param name="Repo">"Project/Repo", where either part may use * as a wildcard.</param>
/// <param name="Rules">Rule ids, or "*" for every rule.</param>
public sealed record Exemption(string Repo, IReadOnlyList<string> Rules, string Reason, DateOnly? Until)
{
    public bool IsExpired(DateOnly today) => Until is { } until && today > until;

    public bool Covers(Repository repository, string ruleId) =>
        Rules.Any(r => r == "*" || r.Equals(ruleId, StringComparison.OrdinalIgnoreCase))
        && Glob(Repo).IsMatch(repository.FullName);

    private static Regex Glob(string pattern) =>
        new("^" + Regex.Escape(pattern).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
}

public sealed record ComplianceBaseline(
    string Version,
    IReadOnlyList<string> Branches,
    IReadOnlyList<BaselineRule> Rules,
    IReadOnlyList<Exemption> Exemptions);

public static class BaselineParser
{
    private static readonly JsonDocumentOptions Options = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <exception cref="FormatException">The JSON is malformed or a required field is missing.</exception>
    public static ComplianceBaseline Parse(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, Options);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"The baseline isn't valid JSON: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new FormatException("The baseline must be a JSON object.");

            var version = root.GetString("version") ?? "unversioned";
            var branches = root.GetStrings("branches");
            if (branches.Count == 0) branches = [BranchTarget.DefaultLabel];

            var rules = new List<BaselineRule>();
            if (root.TryGet("rules", out var rulesElement) && rulesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in rulesElement.EnumerateArray())
                {
                    var id = entry.GetString("id") ?? throw new FormatException("Every rule needs an \"id\".");
                    var severityText = entry.GetString("severity") ?? nameof(RuleSeverity.Medium);
                    if (!Enum.TryParse<RuleSeverity>(severityText, ignoreCase: true, out var severity))
                    {
                        throw new FormatException($"Rule '{id}' has an unknown severity '{severityText}'. Use High, Medium or Low.");
                    }
                    rules.Add(new BaselineRule(id, severity, entry.Clone()));
                }
            }

            var exemptions = new List<Exemption>();
            if (root.TryGet("exemptions", out var exemptionsElement) && exemptionsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in exemptionsElement.EnumerateArray())
                {
                    var repo = entry.GetString("repo") ?? throw new FormatException("Every exemption needs a \"repo\" (Project/Repo).");
                    var ruleIds = entry.GetStrings("rules");
                    DateOnly? until = null;
                    if (entry.GetString("until") is { } untilText)
                    {
                        if (!DateOnly.TryParse(untilText, CultureInfo.InvariantCulture, out var parsed))
                        {
                            throw new FormatException($"Exemption for '{repo}' has an invalid \"until\" date '{untilText}'. Use yyyy-MM-dd.");
                        }
                        until = parsed;
                    }
                    exemptions.Add(new Exemption(repo, ruleIds.Count == 0 ? ["*"] : ruleIds, entry.GetString("reason") ?? "", until));
                }
            }

            return new ComplianceBaseline(version, branches, rules, exemptions);
        }
    }
}
