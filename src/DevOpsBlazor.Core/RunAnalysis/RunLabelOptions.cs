namespace DevOpsBlazor.Core.RunAnalysis;

/// <summary>Thresholds for problem labels. Bound from the "RunLabels" configuration section.</summary>
public sealed class RunLabelOptions
{
    /// <summary>A run is "Slow" when its duration exceeds this multiple of the median successful duration.</summary>
    public double SlowFactor { get; set; } = 1.5;

    /// <summary>"Getting slower" when the median of the newest third of successful runs exceeds the oldest third by this factor.</summary>
    public double SlowerTrendFactor { get; set; } = 1.25;

    public int LongQueueMinutes { get; set; } = 10;

    /// <summary>Pass↔fail flips on one branch, within the fetched window, needed for "Flaky".</summary>
    public int FlakyFlipsThreshold { get; set; } = 3;

    public int StaleDays { get; set; } = 30;

    /// <summary>Minimum number of successful runs before duration-based labels are computed.</summary>
    public int MinSamples { get; set; } = 3;
}
