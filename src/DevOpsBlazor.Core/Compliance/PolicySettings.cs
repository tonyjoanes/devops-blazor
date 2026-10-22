using System.Text.Json;

namespace DevOpsBlazor.Core.Compliance;

/// <summary>Tolerant readers for the free-form <c>settings</c> object on a policy configuration.</summary>
public static class PolicySettings
{
    public static bool TryGet(this JsonElement settings, string name, out JsonElement value)
    {
        value = default;
        if (settings.ValueKind != JsonValueKind.Object) return false;
        if (settings.TryGetProperty(name, out value)) return true;
        foreach (var property in settings.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        return false;
    }

    public static bool? GetBool(this JsonElement settings, string name) =>
        settings.TryGet(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    public static long? GetLong(this JsonElement settings, string name) =>
        settings.TryGet(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : null;

    public static string? GetString(this JsonElement settings, string name) =>
        settings.TryGet(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    public static IReadOnlyList<string> GetStrings(this JsonElement settings, string name)
    {
        if (!settings.TryGet(name, out var v) || v.ValueKind != JsonValueKind.Array) return [];
        return v.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : e.ToString())
            .Where(s => !string.IsNullOrEmpty(s))
            .Select(s => s!)
            .ToList();
    }

    public static int ArrayLength(this JsonElement settings, string name) =>
        settings.TryGet(name, out var v) && v.ValueKind == JsonValueKind.Array ? v.GetArrayLength() : 0;
}

public enum MergeType { Squash, NoFastForward, Rebase, RebaseMerge }

public static class MergeTypes
{
    public static string DisplayName(MergeType type) => type switch
    {
        MergeType.Squash => "Squash",
        MergeType.NoFastForward => "Merge (no fast-forward)",
        MergeType.Rebase => "Rebase and fast-forward",
        MergeType.RebaseMerge => "Semi-linear merge",
        _ => type.ToString(),
    };

    /// <summary>The merge types a "Require a merge strategy" policy allows.</summary>
    public static IReadOnlySet<MergeType> Allowed(JsonElement settings)
    {
        var allowed = new HashSet<MergeType>();
        if (settings.GetBool("allowSquash") == true) allowed.Add(MergeType.Squash);
        if (settings.GetBool("allowNoFastForward") == true) allowed.Add(MergeType.NoFastForward);
        if (settings.GetBool("allowRebase") == true) allowed.Add(MergeType.Rebase);
        if (settings.GetBool("allowRebaseMerge") == true) allowed.Add(MergeType.RebaseMerge);

        // Older policies only have useSquashMerge: true means squash only, false means merge only.
        if (allowed.Count == 0 && settings.GetBool("useSquashMerge") is { } squash)
        {
            allowed.Add(squash ? MergeType.Squash : MergeType.NoFastForward);
        }
        return allowed;
    }

    public static bool TryParse(string value, out MergeType type) =>
        Enum.TryParse(value.Replace("-", "").Replace(" ", ""), ignoreCase: true, out type);
}
