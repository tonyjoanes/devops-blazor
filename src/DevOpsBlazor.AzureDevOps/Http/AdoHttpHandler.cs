using DevOpsBlazor.AzureDevOps.Auth;

namespace DevOpsBlazor.AzureDevOps.Http;

/// <summary>
/// Outermost handler for every Azure DevOps request: refuses anything but GET, adds the PAT and the api-version.
/// </summary>
public sealed class AdoHttpHandler(IAdoTokenProvider tokens) : DelegatingHandler
{
    public const string ApiVersion = "7.1";

    /// <summary>Set on a request to stop the api-version being added (some endpoints, like connectionData, don't accept it).</summary>
    public static readonly HttpRequestOptionsKey<bool> SkipApiVersion = new("ado.skip-api-version");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
        {
            throw new ReadOnlyViolationException(
                $"Blocked a {request.Method} request to {request.RequestUri?.AbsolutePath}. This app only reads from Azure DevOps.");
        }

        request.Headers.Authorization = await tokens.GetAuthorizationAsync(cancellationToken);
        request.Headers.Accept.ParseAdd("application/json");

        if (request.RequestUri is { } uri
            && !(request.Options.TryGetValue(SkipApiVersion, out var skip) && skip)
            && !uri.Query.Contains("api-version=", StringComparison.OrdinalIgnoreCase))
        {
            var separator = string.IsNullOrEmpty(uri.Query) ? "?" : "&";
            request.RequestUri = new Uri(uri.AbsoluteUri + separator + "api-version=" + ApiVersion);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
