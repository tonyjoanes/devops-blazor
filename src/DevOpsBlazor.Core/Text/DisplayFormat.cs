using System.Globalization;

namespace DevOpsBlazor.Core.Text;

public static class DisplayFormat
{
    /// <summary>Compact duration: "45s", "3m 12s", "1h 04m".</summary>
    public static string Duration(TimeSpan? value)
    {
        if (value is not { } d) return "–";
        if (d.TotalHours >= 1) return $"{(int)d.TotalHours}h {d.Minutes:00}m";
        if (d.TotalMinutes >= 1) return $"{d.Minutes}m {d.Seconds:00}s";
        return $"{Math.Max(0, (int)Math.Round(d.TotalSeconds))}s";
    }

    /// <summary>"just now", "5m ago", "3h ago", "2d ago", or a date for anything older than a month.</summary>
    public static string Relative(DateTimeOffset? value, DateTimeOffset now)
    {
        if (value is not { } t) return "–";
        var ago = now - t;
        if (ago < TimeSpan.FromMinutes(1)) return "just now";
        if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes}m ago";
        if (ago < TimeSpan.FromDays(1)) return $"{(int)ago.TotalHours}h ago";
        if (ago < TimeSpan.FromDays(31)) return $"{(int)ago.TotalDays}d ago";
        return ShortDate(t);
    }

    /// <summary>"12 Oct", or "12 Oct 2025" when not in the current year. Shown in the machine's local time.</summary>
    public static string ShortDate(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var format = local.Year == DateTimeOffset.Now.Year ? "d MMM" : "d MMM yyyy";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    public static string DateTime(DateTimeOffset? value) =>
        value is { } t ? t.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture) : "–";

    public static string Percent(double? value) =>
        value is { } v ? v.ToString("P0", CultureInfo.InvariantCulture).Replace(" ", "") : "–";
}
