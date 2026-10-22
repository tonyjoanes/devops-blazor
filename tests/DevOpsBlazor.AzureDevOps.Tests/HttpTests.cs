using System.Net;
using System.Text;
using DevOpsBlazor.AzureDevOps.Http;
using RichardSzalay.MockHttp;

namespace DevOpsBlazor.AzureDevOps.Tests;

public class AdoHttpHandlerTests
{
    [Fact]
    public async Task Blocks_anything_but_GET()
    {
        var host = new TestHost();
        var handler = new AdoHttpHandler(new Auth.PatTokenProvider(host.Monitor)) { InnerHandler = host.Http };
        using var client = new HttpClient(handler) { BaseAddress = new Uri(TestHost.Org) };

        await Assert.ThrowsAsync<ReadOnlyViolationException>(() => client.PostAsync("Platform/_apis/build/builds", new StringContent("{}")));
        await Assert.ThrowsAsync<ReadOnlyViolationException>(() => client.DeleteAsync("Platform/_apis/git/repositories/x"));
        Assert.Equal(0, host.Http.GetMatchCount(host.Http.Fallback));
    }

    [Fact]
    public async Task Adds_basic_auth_and_the_api_version()
    {
        var host = new TestHost();
        var expectedAuth = "Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(":secret-pat"));
        host.Http.Expect(HttpMethod.Get, TestHost.Org + "_apis/projects")
            .WithQueryString("api-version", "7.1")
            .WithHeaders("Authorization", expectedAuth)
            .Respond("application/json", """{ "count": 0, "value": [] }""");

        await host.CreateHttp().GetListAsync<object>("_apis/projects", default);
        host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Keeps_an_explicit_api_version_and_can_skip_it()
    {
        var host = new TestHost();
        host.Http.Expect(TestHost.Org + "_apis/a").WithExactQueryString("api-version=7.1-preview.1").Respond("application/json", "{}");
        host.Http.Expect(TestHost.Org + "_apis/connectionData").WithExactQueryString("").Respond("application/json", "{}");

        var http = host.CreateHttp();
        await http.GetAsync<object>("_apis/a?api-version=7.1-preview.1", default);
        await http.GetAsync<object>("_apis/connectionData", default, skipApiVersion: true);
        host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task A_missing_PAT_is_a_configuration_error()
    {
        var host = new TestHost();
        host.Options.Pat = " ";
        var ex = await Assert.ThrowsAsync<AdoConfigurationException>(() => host.CreateHttp().GetAsync<object>("_apis/projects", default));
        Assert.Contains("user-secrets", ex.Message);
    }
}

public class AdoHttpTests
{
    [Fact]
    public async Task Follows_continuation_tokens_until_the_last_page()
    {
        var host = new TestHost();
        host.Http.Expect(TestHost.Org + "Platform/_apis/policy/configurations").WithExactQueryString("api-version=7.1")
            .Respond(_ => Page("""[1, 2]""", "token 2"));
        host.Http.Expect(TestHost.Org + "Platform/_apis/policy/configurations").WithQueryString("continuationToken", "token 2")
            .Respond(_ => Page("""[3]""", null));

        var items = await host.CreateHttp().GetListAsync<int>("Platform/_apis/policy/configurations", default);

        Assert.Equal([1, 2, 3], items);
        host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Stops_paging_once_max_items_is_reached()
    {
        var host = new TestHost();
        var page = host.Http.When(TestHost.Org + "x").Respond(_ => Page("[1, 2, 3]", "more"));
        Assert.Equal([1, 2], await host.CreateHttp().GetListAsync<int>("x", default, maxItems: 2));
        Assert.Equal(1, host.Http.GetMatchCount(page));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "_apis/projects", "rejected the PAT")]
    [InlineData(HttpStatusCode.NonAuthoritativeInformation, "_apis/projects", "rejected the PAT")]
    [InlineData(HttpStatusCode.Forbidden, "Platform/_apis/build/builds", "Build (Read)")]
    [InlineData(HttpStatusCode.Forbidden, "Platform/_apis/policy/configurations", "Code (Read)")]
    [InlineData(HttpStatusCode.NotFound, "Nope/_apis/git/repositories", "Check the organisation URL and project name")]
    [InlineData(HttpStatusCode.TooManyRequests, "_apis/projects", "throttling")]
    public async Task Turns_failures_into_actionable_messages(HttpStatusCode status, string path, string expected)
    {
        var host = new TestHost();
        host.Http.When(TestHost.Org + path).Respond(status, "application/json", """{ "message": "server detail" }""");

        var ex = await Assert.ThrowsAsync<AdoApiException>(() => host.CreateHttp().GetAsync<object>(path, default));
        Assert.Contains(expected, ex.Message);
        Assert.Equal(status, ex.StatusCode);
    }

    [Fact]
    public async Task Includes_the_server_message_for_unexpected_errors()
    {
        var host = new TestHost();
        host.Http.When(TestHost.Org + "x").Respond(HttpStatusCode.BadRequest, "application/json", """{ "message": "TF400813: bad thing" }""");
        var ex = await Assert.ThrowsAsync<AdoApiException>(() => host.CreateHttp().GetAsync<object>("x", default));
        Assert.Contains("TF400813: bad thing", ex.Message);
    }

    [Fact]
    public async Task An_html_sign_in_page_is_reported_as_unexpected_content()
    {
        var host = new TestHost();
        host.Http.When(TestHost.Org + "x").Respond("text/html", "<html>sign in</html>");
        var ex = await Assert.ThrowsAsync<AdoApiException>(() => host.CreateHttp().GetAsync<Dictionary<string, object>>("x", default));
        Assert.Contains("organisation URL", ex.Message);
    }

    [Fact]
    public async Task No_base_address_means_the_organisation_is_not_configured()
    {
        var host = new TestHost();
        var http = new AdoHttp(new HttpClient(host.Http));
        await Assert.ThrowsAsync<AdoConfigurationException>(() => http.GetAsync<object>("_apis/projects", default));
    }

    private static HttpResponseMessage Page(string valuesJson, string? continuation)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{ "count": 0, "value": {{valuesJson}} }""", Encoding.UTF8, "application/json"),
        };
        if (continuation is not null) response.Headers.Add(AdoHttp.ContinuationHeader, continuation);
        return response;
    }
}
