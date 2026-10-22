using System.Text;
using DevOpsBlazor.AzureDevOps;
using DevOpsBlazor.Core.Compliance;
using DevOpsBlazor.Core.RunAnalysis;
using DevOpsBlazor.Web.Components;
using DevOpsBlazor.Web.Services;
using Microsoft.FluentUI.AspNetCore.Components;

var builder = WebApplication.CreateBuilder(args);

// The PAT lives in user-secrets. Load them in every environment, not just Development.
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddFluentUIComponents();

builder.Services.AddAzureDevOps(builder.Configuration.GetSection(AzureDevOpsOptions.SectionName));
builder.Services.Configure<RunLabelOptions>(builder.Configuration.GetSection("RunLabels"));
builder.Services.Configure<ComplianceOptions>(builder.Configuration.GetSection("Compliance"));
builder.Services.AddTransient<ProjectScope>();
builder.Services.AddTransient<PipelineService>();
builder.Services.AddTransient<ComplianceService>();
builder.Services.AddTransient<HealthService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.MapGet("/export/compliance.{format}", async (string format, string? project, ComplianceService compliance, CancellationToken ct) =>
{
    var load = compliance.LoadBaseline();
    if (load.Baseline is null) return Results.Problem(load.Error, statusCode: 500, title: "Compliance baseline problem");

    var report = await compliance.EvaluateAsync(load.Baseline, project, ct);
    var stamp = report.GeneratedAt.ToLocalTime().ToString("yyyy-MM-dd");
    return format.ToLowerInvariant() switch
    {
        "csv" => Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ComplianceReportWriter.ToCsv(report))).ToArray(),
            "text/csv", $"repo-compliance-{stamp}.csv"),
        "md" => Results.File(Encoding.UTF8.GetBytes(ComplianceReportWriter.ToMarkdown(report)), "text/markdown", $"repo-compliance-{stamp}.md"),
        _ => Results.NotFound(),
    };
});

app.Run();

public partial class Program;
