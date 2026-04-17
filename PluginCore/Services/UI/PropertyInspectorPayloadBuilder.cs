using Newtonsoft.Json.Linq;
using SCStreamDeck.Models;

namespace SCStreamDeck.Services.UI;

internal static class PropertyInspectorPayloadBuilder
{
    public static JObject BuildFunctionsPayload(
        bool functionsLoaded,
        JArray functions,
        PluginLocaleResolution? pluginLocale)
    {
        ArgumentNullException.ThrowIfNull(functions);

        return new JObject
        {
            ["functionsLoaded"] = functionsLoaded,
            ["functions"] = functions,
            ["pluginLocale"] = BuildPluginLocalePayload(pluginLocale ?? PluginLocaleResolution.Default)
        };
    }

    private static JObject BuildPluginLocalePayload(PluginLocaleResolution pluginLocale) =>
        new()
        {
            ["mode"] = pluginLocale.Mode,
            ["override"] = pluginLocale.Override == null ? JValue.CreateNull() : pluginLocale.Override,
            ["detected"] = pluginLocale.Detected == null ? JValue.CreateNull() : pluginLocale.Detected,
            ["effective"] = pluginLocale.Effective,
            ["supported"] = new JArray(PluginLocaleSettings.SupportedLocales)
        };
}
