namespace DevOpsBlazor.Web.Services;

/// <summary>In-app URLs, kept in one place so pages link to each other consistently.</summary>
public static class AppRoutes
{
    private static string E(string value) => Uri.EscapeDataString(value);

    public static string Pipelines(string? project = null) =>
        string.IsNullOrEmpty(project) ? "pipelines" : $"pipelines?project={E(project)}";

    public static string Pipeline(string project, int definitionId) => $"pipelines/{E(project)}/{definitionId}";

    public static string Run(string project, int buildId) => $"runs/{E(project)}/{buildId}";

    public static string Compliance(string? project = null) =>
        string.IsNullOrEmpty(project) ? "compliance" : $"compliance?project={E(project)}";

    public static string RepoPolicies(string project, Guid repositoryId) => $"compliance/{E(project)}/{repositoryId}";

    public static string ComplianceExport(string format, string? project) =>
        $"export/compliance.{format}" + (string.IsNullOrEmpty(project) ? "" : $"?project={E(project)}");
}
