namespace DevOpsBlazor.Core.RunAnalysis;

public enum LabelKind
{
    Broken,
    Flaky,
    Slow,
    GettingSlower,
    LongQueue,
    PartiallySucceeded,
    Canceled,
    TimedOut,
    Retried,
    WarningsRising,
    Stale,
    Disabled,
}

public enum LabelSeverity { High, Medium, Low, Info }

/// <summary>A hint that something may be wrong, with the evidence behind it.</summary>
public sealed record ProblemLabel(LabelKind Kind, LabelSeverity Severity, string Title, string Evidence);
