using System.Text;
using DevOpsBlazor.Core.Compliance.Rules;
using DevOpsBlazor.Core.Text;

namespace DevOpsBlazor.Core.Compliance;

/// <summary>Exports a compliance report as CSV (one line per repo and rule) or as a Markdown summary.</summary>
public static class ComplianceReportWriter
{
    public static string ToCsv(ComplianceReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Project,Repository,Disabled,Rule,Branch,Severity,Status,Reason");
        foreach (var row in report.Rows)
        {
            for (var i = 0; i < report.Columns.Count; i++)
            {
                var column = report.Columns[i];
                var cell = row.Cells[i];
                sb.AppendJoin(',',
                    Csv(row.Repository.Project),
                    Csv(row.Repository.Name),
                    row.Repository.IsDisabled ? "yes" : "no",
                    Csv(column.Title),
                    Csv(column.Branch ?? ""),
                    column.Severity,
                    StatusText(cell.Status),
                    Csv(cell.Reason));
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }

    public static string ToMarkdown(ComplianceReport report)
    {
        var active = report.ActiveRows;
        var full = active.Count(r => r.IsFullyCompliant);
        var warnOnly = active.Count(r => r.Failures == 0 && r.Warnings > 0);
        var failing = active.Count(r => r.Failures > 0);

        var sb = new StringBuilder();
        sb.AppendLine("# Repo policy compliance");
        sb.AppendLine();
        sb.AppendLine($"Generated {DisplayFormat.DateTime(report.GeneratedAt)} · Baseline {Md(report.BaselineVersion)} · Branches: {Md(string.Join(", ", report.Branches))}");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Repos checked | Fully compliant | Warnings only | Failing | Disabled (skipped) |");
        sb.AppendLine("|---:|---:|---:|---:|---:|");
        sb.AppendLine($"| {active.Count} | {full} ({DisplayFormat.Percent(Ratio(full, active.Count))}) | {warnOnly} | {failing} | {report.Rows.Count - active.Count} |");
        sb.AppendLine();

        sb.AppendLine("## Results by rule");
        sb.AppendLine();
        sb.AppendLine("| Rule | Branch | Severity | Failing | Warnings |");
        sb.AppendLine("|---|---|---|---:|---:|");
        for (var i = 0; i < report.Columns.Count; i++)
        {
            var c = report.Columns[i];
            sb.AppendLine($"| {Md(c.Title)} | {Md(c.Branch ?? "(repository)")} | {c.Severity} | {report.FailuresIn(i)} | {report.WarningsIn(i)} |");
        }
        sb.AppendLine();

        var withIssues = active.Where(r => r.Failures + r.Warnings > 0)
            .OrderByDescending(r => r.Failures).ThenByDescending(r => r.Warnings).ThenBy(r => r.Repository.FullName)
            .ToList();
        sb.AppendLine("## Repositories with findings");
        sb.AppendLine();
        if (withIssues.Count == 0) sb.AppendLine("None. Every repository meets the baseline.");
        foreach (var row in withIssues)
        {
            sb.AppendLine($"### {Md(row.Repository.FullName)}");
            sb.AppendLine();
            for (var i = 0; i < report.Columns.Count; i++)
            {
                var cell = row.Cells[i];
                if (cell.Status is not (RuleStatus.Fail or RuleStatus.Warn)) continue;
                var c = report.Columns[i];
                var icon = cell.Status == RuleStatus.Fail ? "❌" : "⚠️";
                var where = c.Branch is null ? "" : $" ({Md(c.Branch)})";
                sb.AppendLine($"- {icon} **{Md(c.Title)}**{where}, {c.Severity}: {Md(cell.Reason)}");
            }
            sb.AppendLine();
        }

        if (report.Problems.Count > 0)
        {
            sb.AppendLine("## Baseline notes");
            sb.AppendLine();
            foreach (var problem in report.Problems) sb.AppendLine($"- {Md(problem)}");
        }
        return sb.ToString();
    }

    public static string StatusText(RuleStatus status) => status switch
    {
        RuleStatus.Pass => "Pass",
        RuleStatus.Warn => "Warn",
        RuleStatus.Fail => "Fail",
        RuleStatus.Exempt => "Exempt",
        _ => "N/A",
    };

    private static double? Ratio(int part, int total) => total == 0 ? null : part / (double)total;

    private static string Csv(string value)
    {
        // Stop spreadsheet apps from treating a cell as a formula.
        if (value.Length > 0 && "=+-@".Contains(value[0])) value = "'" + value;
        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }

    private static string Md(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
