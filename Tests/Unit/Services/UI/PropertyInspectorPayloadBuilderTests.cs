using FluentAssertions;
using Newtonsoft.Json.Linq;
using SCStreamDeck.Models;
using SCStreamDeck.Services.UI;

namespace Tests.Unit.Services.UI;

public sealed class PropertyInspectorPayloadBuilderTests
{
    [Fact]
    public void BuildFunctionsPayload_IncludesFunctionsAndPluginLocale()
    {
        JArray functions =
        [
            new JObject
            {
                ["label"] = "Flight",
                ["options"] = new JArray()
            }
        ];

        JObject payload = PropertyInspectorPayloadBuilder.BuildFunctionsPayload(
            true,
            functions,
            new PluginLocaleResolution("override", "fr", "de", "fr"));

        payload.Properties().Select(p => p.Name)
            .Should()
            .Equal("functionsLoaded", "functions", "pluginLocale");

        payload["functionsLoaded"]!.Value<bool>().Should().BeTrue();
        payload["functions"]!.Should().BeSameAs(functions);
        payload["pluginLocale"]!["mode"]!.Value<string>().Should().Be("override");
        payload["pluginLocale"]!["override"]!.Value<string>().Should().Be("fr");
        payload["pluginLocale"]!["detected"]!.Value<string>().Should().Be("de");
        payload["pluginLocale"]!["effective"]!.Value<string>().Should().Be("fr");
        ((JArray)payload["pluginLocale"]!["supported"]!).Select(v => v.Value<string>())
            .Should()
            .Equal("en", "de", "fr", "es");
    }

    [Fact]
    public void BuildFunctionsPayload_FallsBackToDefaultLocale_WhenLocaleMissing()
    {
        JObject payload = PropertyInspectorPayloadBuilder.BuildFunctionsPayload(false, [], null);

        payload["functionsLoaded"]!.Value<bool>().Should().BeFalse();
        payload["pluginLocale"]!["mode"]!.Value<string>().Should().Be("auto");
        payload["pluginLocale"]!["override"]!.Type.Should().Be(JTokenType.Null);
        payload["pluginLocale"]!["detected"]!.Type.Should().Be(JTokenType.Null);
        payload["pluginLocale"]!["effective"]!.Value<string>().Should().Be("en");
    }
}
