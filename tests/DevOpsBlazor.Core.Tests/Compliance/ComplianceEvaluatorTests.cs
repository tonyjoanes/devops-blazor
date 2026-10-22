using DevOpsBlazor.Core.Compliance;
using DevOpsBlazor.Core.Compliance.Rules;
using DevOpsBlazor.Core.Models;
using static DevOpsBlazor.Core.Tests.TestData;

namespace DevOpsBlazor.Core.Tests.Compliance;

public class ComplianceEvaluatorTests
{
    private const string Baseline = """
        {
          // comments and trailing commas are allowed
          "version": "v1",
          "branches": [ "default", "release/*" ],
          "rules": [
            { "id": "MinReviewers", "severity": "High", "min": 2 },
            { "id": "DefaultBranchIsMain", "severity": "low" },
            { "id": "NoSuchRule" },
          ],
          "exemptions": [
            { "repo": "Platform/sandbox*", "rules": [ "*" ], "reason": "Playground" },
            { "repo": "Platform/old", "rules": [ "MinReviewers" ], "reason": "Retiring", "until": "2026-01-01" }
          ]
        }
        """;

    private static ComplianceReport Evaluate(IEnumerable<Repository> repos, params PolicyConfiguration[] policies) =>
        ComplianceEvaluator.Evaluate(BaselineParser.Parse(Baseline), repos, policies, Now);

    [Fact]
    public void Branch_rules_get_a_column_per_branch_and_repository_rules_one_column()
    {
        var report = Evaluate([Repo()]);
        Assert.Equal(["MinReviewers@default", "MinReviewers@release/*", "DefaultBranchIsMain"], report.Columns.Select(c => c.Key));
        Assert.Equal(RuleSeverity.Low, report.Columns[2].Severity);
    }

    [Fact]
    public void Unknown_rules_and_expired_exemptions_are_reported()
    {
        var report = Evaluate([Repo()]);
        Assert.Contains(report.Problems, p => p.Contains("NoSuchRule"));
        Assert.Contains(report.Problems, p => p.Contains("Platform/old") && p.Contains("expired"));
    }

    [Fact]
    public void Each_cell_is_evaluated_against_the_right_branch()
    {
        var report = Evaluate([Repo()], MinReviewers(2));
        var row = Assert.Single(report.Rows);
        Assert.Equal([RuleStatus.Pass, RuleStatus.Fail, RuleStatus.Pass], row.Cells.Select(c => c.Status));
        Assert.Equal(1, row.Failures);
        Assert.False(row.IsFullyCompliant);
    }

    [Fact]
    public void Active_exemptions_apply_and_expired_ones_do_not()
    {
        var sandbox = Repo("sandbox-tony", id: Guid.NewGuid());
        var old = Repo("old", id: Guid.NewGuid());
        var report = Evaluate([sandbox, old]);

        var oldRow = report.Rows.Single(r => r.Repository.Name == "old");
        var sandboxRow = report.Rows.Single(r => r.Repository.Name == "sandbox-tony");
        Assert.All(sandboxRow.Cells, c => Assert.Equal(RuleStatus.Exempt, c.Status));
        Assert.Contains("Playground", sandboxRow.Cells[0].Reason);
        Assert.Equal(RuleStatus.Fail, oldRow.Cells[0].Status);
    }

    [Fact]
    public void Disabled_and_empty_repositories_are_not_applicable()
    {
        var report = Evaluate([Repo("disabled", disabled: true, id: Guid.NewGuid()), Repo("empty", defaultBranch: null, id: Guid.NewGuid())]);

        Assert.All(report.Rows.Single(r => r.Repository.Name == "disabled").Cells, c => Assert.Equal(RuleStatus.NotApplicable, c.Status));
        Assert.Equal(RuleStatus.NotApplicable, report.Rows.Single(r => r.Repository.Name == "empty").Cells[0].Status);
        Assert.Single(report.ActiveRows);
    }

    [Fact]
    public void Policies_from_other_projects_are_not_applied()
    {
        var otherProject = MinReviewers(2) with { Project = "Payments" };
        Assert.Equal(RuleStatus.Fail, Evaluate([Repo()], otherProject).Rows[0].Cells[0].Status);
    }
}

public class BaselineParserTests
{
    [Fact]
    public void Defaults_to_the_default_branch_and_medium_severity()
    {
        var baseline = BaselineParser.Parse("""{ "rules": [ { "id": "MinReviewers" } ] }""");
        Assert.Equal(["default"], baseline.Branches);
        Assert.Equal(RuleSeverity.Medium, baseline.Rules[0].Severity);
        Assert.Equal("unversioned", baseline.Version);
    }

    [Theory]
    [InlineData("not json", "isn't valid JSON")]
    [InlineData("""{ "rules": [ { "severity": "High" } ] }""", "needs an \"id\"")]
    [InlineData("""{ "rules": [ { "id": "X", "severity": "Urgent" } ] }""", "unknown severity")]
    [InlineData("""{ "exemptions": [ { "repo": "a/b", "until": "soon" } ] }""", "invalid \"until\"")]
    public void Explains_what_is_wrong(string json, string expected)
    {
        var ex = Assert.Throws<FormatException>(() => BaselineParser.Parse(json));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void The_shipped_baseline_parses_and_only_uses_known_rules()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "compliance-baseline.json");
        var baseline = BaselineParser.Parse(File.ReadAllText(path));
        Assert.NotEmpty(baseline.Rules);
        Assert.All(baseline.Rules, r => Assert.True(BuiltInRules.All.ContainsKey(r.Id), r.Id));
    }
}
