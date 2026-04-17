using Newtonsoft.Json;

namespace SCStreamDeck.Models;

public sealed record PluginLocaleSettings(
    [property: JsonProperty("mode")]
    string Mode,
    [property: JsonProperty("override")]
    string? Override,
    [property: JsonProperty("lastDetected")]
    string? LastDetected)
{
    public const string AutoMode = "auto";
    public const string OverrideMode = "override";
    public const string DefaultLocale = "en";

    private static readonly string[] s_supportedLocales = ["en", "de", "fr", "es"];
    private static readonly HashSet<string> s_supportedLocaleSet =
        new(s_supportedLocales, StringComparer.OrdinalIgnoreCase);

    public static PluginLocaleSettings Default { get; } = new(AutoMode, null, null);

    public static IReadOnlyList<string> SupportedLocales => s_supportedLocales;

    public PluginLocaleSettings Normalize() =>
        new(NormalizeMode(Mode), NormalizeLocale(Override), NormalizeLocale(LastDetected));

    public static string NormalizeMode(string? mode) =>
        string.Equals(mode, OverrideMode, StringComparison.OrdinalIgnoreCase) ? OverrideMode : AutoMode;

    public static string? NormalizeLocale(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        string normalized = locale.Trim().Replace('_', '-').ToLowerInvariant();

        if (s_supportedLocaleSet.Contains(normalized))
        {
            return normalized;
        }

        string[] parts = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && s_supportedLocaleSet.Contains(parts[0]) ? parts[0] : null;
    }
}
