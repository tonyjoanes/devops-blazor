namespace DevOpsBlazor.AzureDevOps;

/// <summary>Bound from the "AzureDevOps" configuration section. Pat and PatExpiresOn belong in user-secrets.</summary>
public sealed class AzureDevOpsOptions
{
    public const string SectionName = "AzureDevOps";

    /// <summary>Organisation URL, e.g. https://dev.azure.com/contoso.</summary>
    public string? Organization { get; set; }

    /// <summary>Projects to show. Empty means every project the PAT can see.</summary>
    public List<string> Projects { get; set; } = [];

    public string? Pat { get; set; }

    /// <summary>A PAT can't report its own expiry, so it's recorded here to drive the expiry warning.</summary>
    public DateOnly? PatExpiresOn { get; set; }

    public int RunsPerPipeline { get; set; } = 20;

    public CacheMinutesOptions CacheMinutes { get; set; } = new();

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Organization) && !string.IsNullOrWhiteSpace(Pat);

    public const string MissingOrganizationMessage =
        "AzureDevOps:Organization isn't set to a valid URL. Set it in appsettings.json, e.g. https://dev.azure.com/contoso.";

    /// <summary>The organisation URL with exactly one trailing slash, ready to be a base address.</summary>
    public bool TryGetOrganizationUri(out Uri uri)
    {
        uri = null!;
        return !string.IsNullOrWhiteSpace(Organization)
               && Uri.TryCreate(Organization.Trim().TrimEnd('/') + "/", UriKind.Absolute, out uri!)
               && uri.Scheme is "https" or "http";
    }
}

public sealed class CacheMinutesOptions
{
    public double Pipelines { get; set; } = 10;
    public double Runs { get; set; } = 1;
    public double Policies { get; set; } = 10;
}
