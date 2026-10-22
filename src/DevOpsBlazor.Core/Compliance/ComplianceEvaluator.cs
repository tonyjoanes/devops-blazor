using System.Text.Json;
using DevOpsBlazor.Core.Compliance.Rules;
using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Core.Compliance;

/// <summary>One column of the compliance matrix: a rule, evaluated for one branch (or for the repository).</summary>
public sealed record ComplianceColumn(string RuleId, string Title, string Description, RuleSeverity Severity, string? Branch)
{
    public string Key => Branch is null ? RuleId : $"{RuleId}@{Branch}";
    public string Group => Branch ?? "Repository";
}

public sealed record ComplianceCell(RuleStatus Status, string Reason);

public sealed record RepoCompliance(Repository Repository, IReadOnlyList<ComplianceCell> Cells)
{
    public int Failures => Cells.Count(c => c.Status == RuleStatus.Fail);
    public int Warnings => Cells.Count(c => c.Status == RuleStatus.Warn);
    public bool IsFullyCompliant => !Repository.IsDisabled && Failures == 0 && Warnings == 0;
}

public sealed record ComplianceReport(
    DateTimeOffset GeneratedAt,
    string BaselineVersion,
    IReadOnlyList<string> Branches,
    IReadOnlyList<ComplianceColumn> Columns,
    IReadOnlyList<RepoCompliance> Rows,
    IReadOnlyList<string> Problems)
{
    /// <summary>Rows that count towards the summary: disabled repositories are left out.</summary>
    public IReadOnlyList<RepoCompliance> ActiveRows => Rows.Where(r => !r.Repository.IsDisabled).ToList();

    public int FailuresIn(int columnIndex) => ActiveRows.Count(r => r.Cells[columnIndex].Status == RuleStatus.Fail);
    public int WarningsIn(int columnIndex) => ActiveRows.Count(r => r.Cells[columnIndex].Status == RuleStatus.Warn);
}

public static class ComplianceEvaluator
{
    public static ComplianceReport Evaluate(
        ComplianceBaseline baseline,
        IEnumerable<Repository> repositories,
        IEnumerable<PolicyConfiguration> policies,
        DateTimeOffset now,
        IReadOnlyDictionary<string, IComplianceRule>? rules = null)
    {
        rules ??= BuiltInRules.All;
        var problems = new List<string>();
        var today = DateOnly.FromDateTime(now.LocalDateTime);

        var resolved = new List<(BaselineRule Entry, IComplianceRule Rule)>();
        foreach (var entry in baseline.Rules)
        {
            if (rules.TryGetValue(entry.Id, out var rule)) resolved.Add((entry, rule));
            else problems.Add($"Unknown rule '{entry.Id}' in the baseline, so it was skipped. Known rules: {string.Join(", ", rules.Keys.Order())}.");
        }
        foreach (var expired in baseline.Exemptions.Where(e => e.IsExpired(today)))
        {
            problems.Add($"The exemption for '{expired.Repo}' expired on {expired.Until:yyyy-MM-dd}, so it no longer applies.");
        }

        var columns = new List<(ComplianceColumn Column, BaselineRule Entry, IComplianceRule Rule)>();
        foreach (var branch in baseline.Branches)
        foreach (var (entry, rule) in resolved.Where(r => r.Rule.Scope == RuleScope.Branch))
            columns.Add((new ComplianceColumn(rule.Id, rule.Title, rule.Description, entry.Severity, branch), entry, rule));
        foreach (var (entry, rule) in resolved.Where(r => r.Rule.Scope == RuleScope.Repository))
            columns.Add((new ComplianceColumn(rule.Id, rule.Title, rule.Description, entry.Severity, null), entry, rule));

        var policiesByProject = policies
            .GroupBy(p => p.Project, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var activeExemptions = baseline.Exemptions.Where(e => !e.IsExpired(today)).ToList();

        var rows = new List<RepoCompliance>();
        foreach (var repository in repositories.OrderBy(r => r.Project).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            var projectPolicies = policiesByProject.GetValueOrDefault(repository.Project) ?? [];
            var repoPolicies = EffectivePolicyResolver.ForRepository(repository, projectPolicies);
            var branchPolicies = new Dictionary<string, (BranchTarget Target, IReadOnlyList<EffectivePolicy> Policies)>();
            foreach (var branch in baseline.Branches)
            {
                var target = BranchTarget.For(branch, repository);
                branchPolicies[branch] = (target, EffectivePolicyResolver.ForBranch(repository, target, projectPolicies));
            }

            var cells = new List<ComplianceCell>();
            foreach (var (column, entry, rule) in columns)
            {
                cells.Add(EvaluateCell(repository, column, entry, rule, branchPolicies, repoPolicies, activeExemptions));
            }
            rows.Add(new RepoCompliance(repository, cells));
        }

        return new ComplianceReport(now, baseline.Version, baseline.Branches, columns.Select(c => c.Column).ToList(), rows, problems);
    }

    private static ComplianceCell EvaluateCell(
        Repository repository,
        ComplianceColumn column,
        BaselineRule entry,
        IComplianceRule rule,
        Dictionary<string, (BranchTarget Target, IReadOnlyList<EffectivePolicy> Policies)> branchPolicies,
        IReadOnlyList<EffectivePolicy> repoPolicies,
        List<Exemption> exemptions)
    {
        if (repository.IsDisabled) return new ComplianceCell(RuleStatus.NotApplicable, "The repository is disabled.");

        var exemption = exemptions.FirstOrDefault(e => e.Covers(repository, rule.Id));
        if (exemption is not null)
        {
            var until = exemption.Until is { } u ? $" until {u:yyyy-MM-dd}" : "";
            return new ComplianceCell(RuleStatus.Exempt, $"Exempt{until}: {exemption.Reason}".TrimEnd(' ', ':'));
        }

        BranchTarget? target = null;
        IReadOnlyList<EffectivePolicy> forBranch = [];
        if (column.Branch is { } branch)
        {
            (target, forBranch) = branchPolicies[branch];
            if (target.NotApplicableReason is { } reason) return new ComplianceCell(RuleStatus.NotApplicable, reason);
        }

        var result = rule.Evaluate(new RuleContext(repository, target, forBranch, repoPolicies, entry.Settings));
        return new ComplianceCell(result.Status, result.Reason);
    }
}
