namespace DevOpsBlazor.Core.RunAnalysis;

public static class Stats
{
    public static TimeSpan? Median(IEnumerable<TimeSpan> values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        if (sorted.Length == 0) return null;
        var mid = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }
}
