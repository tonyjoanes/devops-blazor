using DevOpsBlazor.AzureDevOps.Clients;
using DevOpsBlazor.Core.Models;
using RichardSzalay.MockHttp;

namespace DevOpsBlazor.AzureDevOps.Tests;

public class BuildClientTests
{
    private static BuildClient Client(TestHost host) => new(host.CreateHttp(), host.Cache, host.Monitor);

    [Fact]
    public async Task Maps_runs_including_unfinished_ones_and_unknown_fields()
    {
        var host = new TestHost();
        host.Http.When(TestHost.Org + "Platform/_apis/build/builds").Respond("application/json", TestHost.Fixture("builds.json"));

        var runs = await Client(host).GetRunsAsync("Platform", 1, 25);

        var done = runs[0];
        Assert.Equal(RunResult.PartiallySucceeded, done.Result);
        Assert.Equal(RunStatus.Completed, done.Status);
        Assert.Equal(TimeSpan.FromMinutes(1), done.QueueWait);
        Assert.Equal(TimeSpan.FromSeconds(216), done.Duration);
        Assert.Equal("Alex Kim", done.RequestedFor);
        Assert.Equal("1214590", done.ShortCommit);

        var queued = runs[1];
        Assert.Equal(RunStatus.NotStarted, queued.Status);
        Assert.Equal(RunResult.None, queued.Result);
        Assert.Null(queued.FinishTime);
        Assert.Null(queued.Duration);
        Assert.Equal("PR 42", queued.BranchName);
        Assert.Equal("Platform", queued.Project);
    }

    [Fact]
    public async Task Runs_query_uses_top_branch_and_newest_first()
    {
        var host = new TestHost();
        host.Http.Expect(TestHost.Org + "Platform/_apis/build/builds")
            .WithQueryString(new Dictionary<string, string>
            {
                ["definitions"] = "7",
                ["$top"] = "10",
                ["queryOrder"] = "queueTimeDescending",
                ["branchName"] = "refs/heads/main",
            })
            .Respond("application/json", """{ "count": 0, "value": [] }""");

        await Client(host).GetRunsAsync("Platform", 7, 10, "main");
        host.Http.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task Recent_runs_for_many_pipelines_are_fetched_in_chunks()
    {
        var host = new TestHost();
        var request = host.Http.When(TestHost.Org + "Platform/_apis/build/builds")
            .WithQueryString("maxBuildsPerDefinition", "20")
            .Respond("application/json", """{ "count": 0, "value": [] }""");

        await Client(host).GetRecentRunsAsync("Platform", Enumerable.Range(1, 120).ToList(), 20);

        Assert.Equal(3, host.Http.GetMatchCount(request));
    }

    [Fact]
    public async Task Project_names_are_escaped_and_results_are_cached()
    {
        var host = new TestHost();
        var request = host.Http.When(TestHost.Org + "My%20Project/_apis/build/definitions")
            .Respond("application/json", """
                { "count": 1, "value": [ { "id": 3, "name": "web", "path": "\\Apps", "queueStatus": "disabled",
                  "process": { "type": 2 }, "repository": { "name": "web", "defaultBranch": "refs/heads/main" } } ] }
                """);

        var client = Client(host);
        var definitions = await client.GetDefinitionsAsync("My Project");
        await client.GetDefinitionsAsync("My Project");

        var definition = Assert.Single(definitions);
        Assert.Equal(PipelineProcess.Yaml, definition.Process);
        Assert.Equal("refs/heads/main", definition.DefaultBranch);
        Assert.True(definition.IsDisabled);
        Assert.Equal("Apps", definition.Folder);
        Assert.Equal(1, host.Http.GetMatchCount(request));
    }

    [Fact]
    public async Task Timeline_records_map_results_logs_and_issues()
    {
        var host = new TestHost();
        host.Http.When(TestHost.Org + "Platform/_apis/build/builds/5/timeline").Respond("application/json", """
            { "records": [ { "id": "6f0b6c43-6c5b-4a5c-8d3a-1d7e2f3a4b5c", "parentId": null, "type": "Task", "name": "Test",
              "result": "failed", "errorCount": 1, "warningCount": 0, "attempt": 1, "order": 3, "log": { "id": 9 },
              "issues": [ { "type": "error", "message": "boom" }, { "type": "warning", "message": "hmm" }, { "type": "error" } ] } ] }
            """);
        var run = new PipelineRun(5, "Platform", 1, "x", "5", RunStatus.Completed, RunResult.Failed, "", null, "", null, null, null,
            DateTimeOffset.UtcNow, null, null);

        var record = Assert.Single(await Client(host).GetTimelineAsync("Platform", run));

        Assert.Equal(RunResult.Failed, record.Result);
        Assert.Equal(9, record.LogId);
        Assert.Equal([TimelineIssueType.Error, TimelineIssueType.Warning], record.Issues.Select(i => i.Type));
    }
}

public class PolicyClientTests
{
    [Fact]
    public async Task Maps_policies_with_their_scopes_and_kinds()
    {
        var host = new TestHost();
        host.Http.When(TestHost.Org + "Platform/_apis/policy/configurations").Respond("application/json", TestHost.Fixture("policies.json"));

        var policies = await new PolicyClient(host.CreateHttp(), host.Cache, host.Monitor).GetPolicyConfigurationsAsync("Platform");

        Assert.Equal([PolicyKind.MinimumReviewers, PolicyKind.CommentRequirements, PolicyKind.FileSize], policies.Select(p => p.Kind));
        Assert.Equal(new PolicyScope(Guid.Parse("11111111-1111-1111-1111-111111111111"), "refs/heads/main", PolicyMatchKind.Exact), policies[0].Scopes[0]);
        Assert.Equal(new PolicyScope(null, null, PolicyMatchKind.DefaultBranch), policies[1].Scopes[0]);
        Assert.False(policies[1].IsBlocking);
        Assert.True(policies[2].Scopes[0].IsRepositoryLevel);
        Assert.All(policies, p => Assert.Equal("Platform", p.Project));
    }
}

public class LinksTests
{
    [Fact]
    public void Builds_portal_urls()
    {
        var host = new TestHost();
        var links = new Links.AdoLinks(host.Monitor);
        var job = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var task = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

        Assert.Equal("https://dev.azure.com/contoso/My%20Project/_build/results?buildId=5&view=logs&j=" + job + "&t=" + task,
            links.Logs("My Project", 5, job, task));
        Assert.Equal("https://dev.azure.com/contoso/P/_apis/build/builds/5/logs?$format=zip", links.LogsZip("P", 5));
        Assert.Equal("https://dev.azure.com/contoso/P/_build?definitionId=3", links.Pipeline("P", 3));
    }
}
