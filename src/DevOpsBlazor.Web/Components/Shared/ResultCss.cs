using DevOpsBlazor.Core.Models;

namespace DevOpsBlazor.Web.Components.Shared;

public static class ResultCss
{
    /// <summary>CSS suffix shared by the dot, bar and badge styles.</summary>
    public static string For(PipelineRun run) => run.IsCompleted ? For(run.Result) : "running";

    public static string For(RunResult? result) => result switch
    {
        RunResult.Succeeded => "succeeded",
        RunResult.Failed => "failed",
        RunResult.PartiallySucceeded => "partiallySucceeded",
        RunResult.Canceled => "canceled",
        _ => "none",
    };

    public static string Symbol(RunResult? result) => result switch
    {
        RunResult.Succeeded => "✓",
        RunResult.Failed => "✗",
        RunResult.PartiallySucceeded => "!",
        RunResult.Canceled => "⊘",
        RunResult.Skipped => "–",
        _ => "•",
    };
}
