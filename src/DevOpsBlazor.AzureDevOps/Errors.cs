using System.Net;

namespace DevOpsBlazor.AzureDevOps;

/// <summary>A call to Azure DevOps failed. The message is written to be shown to the user as-is.</summary>
public class AdoApiException(string message, HttpStatusCode? statusCode = null, Exception? inner = null)
    : Exception(message, inner)
{
    public HttpStatusCode? StatusCode { get; } = statusCode;
}

/// <summary>The app isn't configured well enough to call Azure DevOps (missing organisation or PAT).</summary>
public sealed class AdoConfigurationException(string message) : AdoApiException(message);

/// <summary>Something tried to send a non-GET request. The app is read-only by design.</summary>
public sealed class ReadOnlyViolationException(string message) : InvalidOperationException(message);
