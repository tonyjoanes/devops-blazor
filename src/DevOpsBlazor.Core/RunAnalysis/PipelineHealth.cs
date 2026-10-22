using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Core.RunAnalysis;

public sealed record PipelineHealth(
    PipelineDefinition Definition,
    IReadOnlyList<PipelineRun> Runs,
    double? PassRate,
    TimeSpan? MedianDuration,
    IReadOnlyList<ProblemLabel> Labels,
    IReadOnlyDictionary<int, IReadOnlyList<ProblemLabel>> RunLabels)
{
    public PipelineRun? LatestRun => Runs.Count > 0 ? Runs[0] : null;

    /// <summary>Lower is worse; used to sort "most problematic first".</summary>
    public int WorstSeverity => Labels.Count == 0 ? int.MaxValue : Labels.Min(l => (int)l.Severity);

    public IReadOnlyList<ProblemLabel> LabelsFor(PipelineRun run) =>
        RunLabels.TryGetValue(run.Id, out var labels) ? labels : [];
}
