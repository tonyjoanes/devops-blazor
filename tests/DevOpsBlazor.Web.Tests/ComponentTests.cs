using Bunit;
using DevOpsBlazor.Core.Models;
using DevOpsBlazor.Core.RunAnalysis;
using DevOpsBlazor.Web.Components.Shared;

namespace DevOpsBlazor.Web.Tests;

public class ComponentTests : BunitContext
{
    private static PipelineRun Run(int id, RunResult result, int minutesAgo, int durationMinutes = 5)
    {
        var queued = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);
        return new PipelineRun(id, "My Project", 1, "api-ci", id.ToString(), RunStatus.Completed, result, "refs/heads/main", "abc1234def",
            "manual", null, "api", "TfsGit", queued, queued.AddSeconds(30), queued.AddSeconds(30).AddMinutes(durationMinutes));
    }

    [Fact]
    public void Run_strip_shows_newest_on_the_right_and_links_to_each_run()
    {
        var runs = new[] { Run(3, RunResult.Failed, 10), Run(2, RunResult.Succeeded, 20), Run(1, RunResult.Canceled, 30) };

        var cut = Render<RunStrip>(p => p.Add(x => x.Runs, runs));

        var links = cut.FindAll("a");
        Assert.Equal(["runs/My%20Project/1", "runs/My%20Project/2", "runs/My%20Project/3"], links.Select(a => a.GetAttribute("href")));
        Assert.Contains("dot-failed", links[^1].InnerHtml);
    }

    [Fact]
    public void Run_strip_respects_the_maximum()
    {
        var runs = Enumerable.Range(1, 30).Select(i => Run(i, RunResult.Succeeded, i)).ToArray();
        var cut = Render<RunStrip>(p => p.Add(x => x.Runs, runs).Add(x => x.Max, 5));
        Assert.Equal(5, cut.FindAll(".dot").Count);
    }

    [Fact]
    public void Problem_label_shows_its_evidence_on_hover()
    {
        var label = new ProblemLabel(LabelKind.Broken, LabelSeverity.High, "Broken", "Failed on the last 3 runs of main.");
        var cut = Render<ProblemLabelChip>(p => p.Add(x => x.Label, label));

        var span = cut.Find("span.label");
        Assert.Equal("Failed on the last 3 runs of main.", span.GetAttribute("title"));
        Assert.Contains("label-High", span.ClassName);
    }

    [Fact]
    public void Duration_chart_draws_a_bar_per_finished_run_and_a_median_line()
    {
        var runs = new[] { Run(1, RunResult.Succeeded, 30, 4), Run(2, RunResult.Failed, 20, 6), Run(3, RunResult.Succeeded, 10, 5) };
        var cut = Render<DurationChart>(p => p.Add(x => x.Runs, runs).Add(x => x.Median, TimeSpan.FromMinutes(5)));

        Assert.Equal(3, cut.FindAll("rect").Count);
        Assert.Single(cut.FindAll("line.median"));
        Assert.Equal("bar-failed", cut.FindAll("rect")[1].ClassName);
    }

    [Fact]
    public void Open_in_ado_uses_a_new_tab_and_default_text()
    {
        var cut = Render<OpenInAdo>(p => p.Add(x => x.Href, "https://dev.azure.com/x"));
        var a = cut.Find("a");
        Assert.Equal("_blank", a.GetAttribute("target"));
        Assert.Contains("Open in Azure DevOps", a.TextContent);
    }
}
