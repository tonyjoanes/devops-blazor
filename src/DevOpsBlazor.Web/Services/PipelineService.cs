using DevOpsBlazor.AzureDevOps;
using DevOpsBlazor.AzureDevOps.Clients;
using DevOpsBlazor.Core.Models;
using DevOpsBlazor.Core.RunAnalysis;
using Microsoft.Extensions.Options;

namespace DevOpsBlazor.Web.Services;

/// <summary>Per-run results that need the run's timeline.</summary>
public sealed record TimelineInsight(
    IReadOnlyList<ProblemLabel> Labels,
    int Warnings,
    TimelineRecord? FirstFailedJob,
    TimelineRecord? FirstFailedTask);

public sealed class PipelineService(
    IBuildClient builds,
    ProjectScope scope,
    IOptionsMonitor<AzureDevOpsOptions> adoOptions,
    IOptionsMonitor<RunLabelOptions> labelOptions,
    ILogger<PipelineService> logger)
{
    private const int MaxConcurrency = 4;

    /// <summary>Every pipeline in the given projects with its recent runs and labels.</summary>
    public async Task<IReadOnlyList<PipelineHealth>> GetOverviewAsync(string? project, CancellationToken cancellationToken = default)
    {
        var projects = await scope.ResolveAsync(project, cancellationToken);
        var results = new List<PipelineHealth>();
        var gate = new SemaphoreSlim(1);
        await Parallel.ForEachAsync(projects, new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = cancellationToken },
            async (name, ct) =>
            {
                var health = await GetProjectOverviewAsync(name, ct);
                await gate.WaitAsync(ct);
                try { results.AddRange(health); }
                finally { gate.Release(); }
            });
        return results;
    }

    private async Task<IReadOnlyList<PipelineHealth>> GetProjectOverviewAsync(string project, CancellationToken cancellationToken)
    {
        var definitions = await builds.GetDefinitionsAsync(project, cancellationToken);
        if (definitions.Count == 0) return [];

        var runs = await builds.GetRecentRunsAsync(project, definitions.Select(d => d.Id).ToList(), adoOptions.CurrentValue.RunsPerPipeline, cancellationToken);
        var byDefinition = runs.ToLookup(r => r.DefinitionId);
        var now = DateTimeOffset.UtcNow;
        return definitions.Select(d => RunAnalyzer.Analyze(d, byDefinition[d.Id], labelOptions.CurrentValue, now)).ToList();
    }

    public async Task<PipelineDefinition?> GetDefinitionAsync(string project, int definitionId, CancellationToken cancellationToken = default)
    {
        var definitions = await builds.GetDefinitionsAsync(project, cancellationToken);
        return definitions.FirstOrDefault(d => d.Id == definitionId);
    }

    public async Task<PipelineHealth> GetPipelineAsync(
        PipelineDefinition definition, int count, string? branch, CancellationToken cancellationToken = default)
    {
        var runs = await builds.GetRunsAsync(definition.Project, definition.Id, count, branch, cancellationToken);
        return RunAnalyzer.Analyze(definition, runs, labelOptions.CurrentValue, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Fetches timelines for the given runs (a few at a time) and works out the labels that need them.
    /// Runs whose timeline can't be read are left out rather than failing the page.
    /// </summary>
    public async Task<IReadOnlyDictionary<int, TimelineInsight>> GetTimelineInsightsAsync(
        string project, IEnumerable<PipelineRun> runs, CancellationToken cancellationToken = default)
    {
        var insights = new System.Collections.Concurrent.ConcurrentDictionary<int, TimelineInsight>();
        await Parallel.ForEachAsync(runs.Where(r => r.Status != RunStatus.NotStarted),
            new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrency, CancellationToken = cancellationToken },
            async (run, ct) =>
            {
                try
                {
                    var records = await builds.GetTimelineAsync(project, run, ct);
                    insights[run.Id] = Insight(records);
                }
                catch (AdoApiException ex)
                {
                    logger.LogWarning("Couldn't read the timeline for run {RunId}: {Message}", run.Id, ex.Message);
                }
            });
        return insights;
    }

    public static TimelineInsight Insight(IReadOnlyList<TimelineRecord> records)
    {
        var (job, task) = TimelineAnalyzer.FirstFailure(records);
        return new TimelineInsight(TimelineAnalyzer.Label(records), TimelineAnalyzer.WarningCount(records), job, task);
    }

    /// <summary>"Warnings rising" across the runs on screen, from their timelines.</summary>
    public static ProblemLabel? WarningsTrend(IEnumerable<PipelineRun> runs, IReadOnlyDictionary<int, TimelineInsight> insights) =>
        TimelineAnalyzer.WarningsRising(runs
            .Where(r => r.IsCompleted && insights.ContainsKey(r.Id))
            .OrderBy(r => r.QueueTime)
            .Select(r => insights[r.Id].Warnings)
            .ToList());
}
