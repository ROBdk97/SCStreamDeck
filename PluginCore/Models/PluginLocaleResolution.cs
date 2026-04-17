namespace SCStreamDeck.Models;

public sealed record PluginLocaleResolution(
    string Mode,
    string? Override,
    string? Detected,
    string Effective)
{
    public static PluginLocaleResolution Default { get; } =
        new(PluginLocaleSettings.AutoMode, null, null, PluginLocaleSettings.DefaultLocale);
}
