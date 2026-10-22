using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Options;

namespace DevOpsBlazor.AzureDevOps.Auth;

/// <summary>Supplies the Authorization header for Azure DevOps calls. Swap the implementation to move from PAT to Entra ID.</summary>
public interface IAdoTokenProvider
{
    ValueTask<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken cancellationToken);
}

public sealed class PatTokenProvider(IOptionsMonitor<AzureDevOpsOptions> options) : IAdoTokenProvider
{
    public ValueTask<AuthenticationHeaderValue> GetAuthorizationAsync(CancellationToken cancellationToken)
    {
        var pat = options.CurrentValue.Pat;
        if (string.IsNullOrWhiteSpace(pat))
        {
            throw new AdoConfigurationException(
                "No PAT is configured. Run: dotnet user-secrets set \"AzureDevOps:Pat\" \"<your PAT>\" --project src/DevOpsBlazor.Web");
        }
        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat.Trim()));
        return ValueTask.FromResult(new AuthenticationHeaderValue("Basic", encoded));
    }
}
