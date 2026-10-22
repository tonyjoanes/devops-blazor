using DevOpsBlazor.Core.Compliance;
using DevOpsBlazor.Core.Models;
using DevOpsBlazor.Core.Text;
using static DevOpsBlazor.Core.Tests.TestData;

namespace DevOpsBlazor.Core.Tests.Compliance;

public class ReportWriterTests
{
    private static ComplianceReport Report() =>
        ComplianceEvaluator.Evaluate(
            BaselineParser.Parse("""{ "version": "v|1", "rules": [ { "id": "MinReviewers", "severity": "High" } ] }"""),
            [Repo("=calc"), Repo("good", id: OtherRepoId)],
            [MinReviewers(2) with { Scopes = [new PolicyScope(OtherRepoId, Main, PolicyMatchKind.Exact)] }],
            Now);

    [Fact]
    public void Csv_has_one_line_per_repo_and_rule_and_is_safe_to_open_in_a_spreadsheet()
    {
        var lines = ComplianceReportWriter.ToCsv(Report()).TrimEnd().Split(Environment.NewLine);
        Assert.Equal("Project,Repository,Disabled,Rule,Branch,Severity,Status,Reason", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("Platform,'=calc,no,Min reviewers,default,High,Fail,", lines[1]);
    }

    [Fact]
    public void Csv_quotes_values_containing_commas()
    {
        var report = ComplianceEvaluator.Evaluate(
            BaselineParser.Parse("""{ "rules": [ { "id": "MinReviewers" } ] }"""),
            [Repo()],
            [MinReviewers(2, blocking: false)],
            Now);

        var line = ComplianceReportWriter.ToCsv(report).Split(Environment.NewLine)[1];
        Assert.EndsWith(",Fail,\"The minimum reviewer policy is configured but optional, so it doesn't block completion.\"", line);
    }

    [Fact]
    public void Markdown_summarises_and_lists_only_repos_with_findings()
    {
        var md = ComplianceReportWriter.ToMarkdown(Report());
        Assert.Contains("Baseline v\\|1", md);
        Assert.Contains("### Platform/=calc", md);
        Assert.DoesNotContain("### Platform/good", md);
        Assert.Contains("| 2 | 1 (50%) | 0 | 1 | 0 |", md);
    }
}

public class PolicyDescriberTests
{
    [Fact]
    public void Describes_minimum_reviewer_settings_in_plain_words()
    {
        var policy = Policy(PolicyKind.MinimumReviewers, new { minimumApproverCount = 2, creatorVoteCounts = true, resetOnSourcePush = false });
        var description = PolicyDescriber.Describe(new EffectivePolicy(policy, policy.Scopes[0], PolicySource.Repository));

        Assert.Contains("Requires 2 approvers", description.Details);
        Assert.Contains("Authors' own approvals count towards the total", description.Details);
        Assert.Contains("Votes are kept when new changes are pushed", description.Details);
        Assert.Equal("This repo · main", description.ScopeText);
    }

    [Fact]
    public void Build_validation_uses_the_pipeline_name_lookup_and_flags_optional()
    {
        var policy = Policy(PolicyKind.Build, new { buildDefinitionId = 7, validDuration = 720 }, blocking: false);
        var description = PolicyDescriber.Describe(new EffectivePolicy(policy, policy.Scopes[0], PolicySource.Repository), id => id == 7 ? "api-ci" : null);

        Assert.Equal("Optional: it doesn't block completion", description.Details[0]);
        Assert.Contains("Pipeline: api-ci", description.Details);
        Assert.Contains("Result expires after 12 hour(s)", description.Details);
    }

    [Theory]
    [InlineData(null, "refs/heads/release/", PolicyMatchKind.Prefix, "All repos in the project · branches starting release/")]
    [InlineData(null, null, PolicyMatchKind.DefaultBranch, "All repos in the project · default branch")]
    [InlineData("11111111-1111-1111-1111-111111111111", null, PolicyMatchKind.Exact, "This repo · repository setting")]
    public void Scope_text(string? repoId, string? refName, PolicyMatchKind kind, string expected)
    {
        Assert.Equal(expected, PolicyDescriber.ScopeText(new PolicyScope(repoId is null ? null : Guid.Parse(repoId), refName, kind)));
    }

    [Theory]
    [InlineData("Minimum number of reviewers", PolicyKind.MinimumReviewers)]
    [InlineData("Require a merge strategy", PolicyKind.MergeStrategy)]
    [InlineData("Something new", PolicyKind.Other)]
    public void Classifies_unknown_type_ids_by_display_name(string name, PolicyKind expected)
    {
        Assert.Equal(expected, PolicyClassifier.Classify(Guid.NewGuid(), name));
    }
}

public class DisplayFormatTests
{
    [Theory]
    [InlineData(45, "45s")]
    [InlineData(192, "3m 12s")]
    [InlineData(3840, "1h 04m")]
    public void Durations(int seconds, string expected) => Assert.Equal(expected, DisplayFormat.Duration(TimeSpan.FromSeconds(seconds)));

    [Fact]
    public void Missing_values_show_a_dash() => Assert.Equal("–", DisplayFormat.Duration(null));
}
