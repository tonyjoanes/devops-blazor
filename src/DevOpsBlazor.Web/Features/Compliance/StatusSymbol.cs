using DevOpsBlazor.Core.Compliance.Rules;

namespace DevOpsBlazor.Web.Features.Compliance;

public static class StatusSymbol
{
    public static string For(RuleStatus status) => status switch
    {
        RuleStatus.Pass => "✓",
        RuleStatus.Fail => "✗",
        RuleStatus.Warn => "!",
        RuleStatus.Exempt => "E",
        _ => "–",
    };

    public static string Text(RuleStatus status) => status switch
    {
        RuleStatus.NotApplicable => "N/A",
        var s => s.ToString(),
    };
}
