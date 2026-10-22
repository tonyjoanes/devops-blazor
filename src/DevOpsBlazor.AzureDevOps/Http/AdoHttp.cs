using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DevOpsBlazor.AzureDevOps.Dtos;

namespace DevOpsBlazor.AzureDevOps.Http;

/// <summary>Typed HttpClient wrapper: GET + JSON, continuation-token paging and friendly error messages.</summary>
public sealed class AdoHttp(HttpClient client)
{
    public const string ContinuationHeader = "x-ms-continuationtoken";

    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken, bool skipApiVersion = false)
    {
        using var response = await SendAsync(relativeUrl, skipApiVersion, cancellationToken);
        return await ReadAsync<T>(response, relativeUrl, cancellationToken);
    }

    /// <summary>Follows continuation tokens until every page is read, or <paramref name="maxItems"/> is reached.</summary>
    public async IAsyncEnumerable<T> GetPagedAsync<T>(
        string relativeUrl, [EnumeratorCancellation] CancellationToken cancellationToken, int maxItems = int.MaxValue)
    {
        string? continuation = null;
        var returned = 0;
        do
        {
            var url = continuation is null
                ? relativeUrl
                : relativeUrl + (relativeUrl.Contains('?') ? "&" : "?") + "continuationToken=" + Uri.EscapeDataString(continuation);

            using var response = await SendAsync(url, false, cancellationToken);
            var page = await ReadAsync<ListResponse<T>>(response, relativeUrl, cancellationToken);
            foreach (var item in page.Value ?? [])
            {
                yield return item;
                if (++returned >= maxItems) yield break;
            }

            continuation = response.Headers.TryGetValues(ContinuationHeader, out var values) ? values.FirstOrDefault() : null;
        }
        while (!string.IsNullOrEmpty(continuation));
    }

    public async Task<List<T>> GetListAsync<T>(string relativeUrl, CancellationToken cancellationToken, int maxItems = int.MaxValue)
    {
        var items = new List<T>();
        await foreach (var item in GetPagedAsync<T>(relativeUrl, cancellationToken, maxItems)) items.Add(item);
        return items;
    }

    private async Task<HttpResponseMessage> SendAsync(string relativeUrl, bool skipApiVersion, CancellationToken cancellationToken)
    {
        if (client.BaseAddress is null) throw new AdoConfigurationException(AzureDevOpsOptions.MissingOrganizationMessage);
        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        if (skipApiVersion) request.Options.Set(AdoHttpHandler.SkipApiVersion, true);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new AdoApiException($"Couldn't reach Azure DevOps at {client.BaseAddress}: {ex.Message}", ex.StatusCode, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AdoApiException($"Azure DevOps didn't respond in time ({PathOf(relativeUrl)}).", HttpStatusCode.RequestTimeout, ex);
        }

        if (response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NonAuthoritativeInformation) return response;

        var message = await ErrorMessageAsync(response, relativeUrl, cancellationToken);
        var status = response.StatusCode;
        response.Dispose();
        throw new AdoApiException(message, status);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string relativeUrl, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken)
                   ?? throw new AdoApiException($"Azure DevOps returned an empty response for {PathOf(relativeUrl)}.");
        }
        catch (JsonException ex)
        {
            throw new AdoApiException(
                $"Azure DevOps returned something that isn't the expected JSON for {PathOf(relativeUrl)}. " +
                "Check the organisation URL is right (e.g. https://dev.azure.com/contoso).", response.StatusCode, ex);
        }
    }

    internal static async Task<string> ErrorMessageAsync(HttpResponseMessage response, string relativeUrl, CancellationToken cancellationToken)
    {
        var path = PathOf(relativeUrl);
        var detail = await ServerMessageAsync(response, cancellationToken);
        var suffix = string.IsNullOrEmpty(detail) ? "" : $" Azure DevOps said: {detail}";
        return response.StatusCode switch
        {
            // 203 is what ADO returns (with a sign-in page) for a bad PAT on some endpoints.
            HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation =>
                "Azure DevOps rejected the PAT (401). It may be expired, revoked or mistyped. Create a new one and update user-secrets.",
            HttpStatusCode.Forbidden =>
                $"The PAT isn't allowed to read {path}. It probably needs the {ScopeHint(path)} scope.{suffix}",
            HttpStatusCode.NotFound =>
                $"Not found: {path}. Check the organisation URL and project name.{suffix}",
            HttpStatusCode.TooManyRequests =>
                "Azure DevOps is throttling requests (429). Wait a minute and refresh.",
            _ => $"Azure DevOps returned {(int)response.StatusCode} {response.ReasonPhrase} for {path}.{suffix}",
        };
    }

    internal static string ScopeHint(string path) => path switch
    {
        _ when path.Contains("/build/", StringComparison.OrdinalIgnoreCase) => "Build (Read)",
        _ when path.Contains("/git/", StringComparison.OrdinalIgnoreCase) || path.Contains("/policy/", StringComparison.OrdinalIgnoreCase) => "Code (Read)",
        _ when path.Contains("projects", StringComparison.OrdinalIgnoreCase) => "Project and Team (Read)",
        _ => "right",
    };

    private static async Task<string?> ServerMessageAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(body) || body.TrimStart()[0] != '{') return null;
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string PathOf(string relativeUrl)
    {
        var q = relativeUrl.IndexOf('?');
        return Uri.UnescapeDataString(q >= 0 ? relativeUrl[..q] : relativeUrl);
    }
}
