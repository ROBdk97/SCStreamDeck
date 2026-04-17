using System.Globalization;
using FluentAssertions;
using SCStreamDeck.Common;
using SCStreamDeck.Models;
using SCStreamDeck.Services.Core;
using SCStreamDeck.Services.Installation;

namespace Tests.Unit.Services.Core;

public sealed class PluginLocaleServiceTests
{
    [Theory]
    [InlineData("en", "en")]
    [InlineData("de-DE", "de")]
    [InlineData("fr_CA", "fr")]
    [InlineData("ES", "es")]
    [InlineData("it-IT", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeLocale_ReturnsSupportedLocaleOrNull(string? input, string? expected)
    {
        string? result = PluginLocaleSettings.NormalizeLocale(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveEffectiveLocale_UsesOverrideBeforeDetectedLocale()
    {
        PluginLocaleSettings settings = new(PluginLocaleSettings.OverrideMode, "fr", "de");

        string effectiveLocale = PluginLocaleService.ResolveEffectiveLocale(settings, "es");

        effectiveLocale.Should().Be("fr");
    }

    [Fact]
    public void ResolveEffectiveLocale_UsesDetectedLocaleBeforeLastDetected()
    {
        PluginLocaleSettings settings = new(PluginLocaleSettings.AutoMode, "fr", "de");

        string effectiveLocale = PluginLocaleService.ResolveEffectiveLocale(settings, "es-MX");

        effectiveLocale.Should().Be("es");
    }

    [Fact]
    public void ResolveEffectiveLocale_UsesLastDetectedBeforeDefault()
    {
        PluginLocaleSettings settings = new(PluginLocaleSettings.AutoMode, null, "de");

        string effectiveLocale = PluginLocaleService.ResolveEffectiveLocale(settings, null);

        effectiveLocale.Should().Be("de");
    }

    [Fact]
    public void ResolveEffectiveLocale_FallsBackToEnglish_WhenNothingElseAvailable()
    {
        string effectiveLocale = PluginLocaleService.ResolveEffectiveLocale(null, "it-IT");

        effectiveLocale.Should().Be("en");
    }

    [Fact]
    public async Task GetCurrentAsync_PersistsDetectedLocale_WhenStateExists()
    {
        CultureInfo originalCurrentCulture = CultureInfo.CurrentCulture;
        CultureInfo originalCurrentUiCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;

            using TestStateFile testState = new();
            PluginState initialState = new(
                DateTime.UtcNow,
                SCChannel.Live,
                null,
                null,
                null,
                null,
                null,
                null,
                new PluginLocaleSettings(PluginLocaleSettings.AutoMode, null, null));

            TestPathProvider pathProvider = new(testState.CacheDirectory);
            StateService stateService = new(pathProvider, new SystemFileSystem());
            await stateService.SaveStateAsync(initialState);

            PluginLocaleService service = new(stateService);

            PluginLocaleResolution result = await service.GetCurrentAsync();
            PluginState? savedState = await stateService.LoadStateAsync();

            result.Detected.Should().Be("de");
            result.Effective.Should().Be("de");
            savedState.Should().NotBeNull();
            savedState!.PluginLocale!.LastDetected.Should().Be("de");
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCurrentCulture;
            CultureInfo.CurrentUICulture = originalCurrentUiCulture;
        }
    }

    [Fact]
    public async Task UpdateSettingsAsync_NormalizesModeAndOverrideLocale()
    {
        using TestStateFile testState = new();
        TestPathProvider pathProvider = new(testState.CacheDirectory);
        StateService stateService = new(pathProvider, new SystemFileSystem());
        PluginLocaleService service = new(stateService);

        await service.UpdateSettingsAsync("OVERRIDE", "fr-CA");

        PluginState? savedState = await stateService.LoadStateAsync();

        savedState.Should().NotBeNull();
        savedState!.PluginLocale.Should().BeEquivalentTo(new PluginLocaleSettings("override", "fr", null));
    }

    [Fact]
    public async Task UpdateSettingsAsync_RaisesLocaleChanged_WhenSettingsChange()
    {
        using TestStateFile testState = new();
        TestPathProvider pathProvider = new(testState.CacheDirectory);
        StateService stateService = new(pathProvider, new SystemFileSystem());
        PluginLocaleService service = new(stateService);
        int raised = 0;

        service.LocaleChanged += () => raised++;

        await service.UpdateSettingsAsync("override", "de");

        raised.Should().Be(1);
    }

    [Fact]
    public async Task UpdateSettingsAsync_DoesNotRaiseLocaleChanged_WhenSettingsUnchanged()
    {
        using TestStateFile testState = new();
        TestPathProvider pathProvider = new(testState.CacheDirectory);
        StateService stateService = new(pathProvider, new SystemFileSystem());
        PluginLocaleService service = new(stateService);
        int raised = 0;

        await stateService.SaveStateAsync(PluginState.CreateDefault().WithPluginLocale(new PluginLocaleSettings("override", "de", null)));
        service.LocaleChanged += () => raised++;

        await service.UpdateSettingsAsync("OVERRIDE", "de-DE");

        raised.Should().Be(0);
    }

    private sealed class TestPathProvider(string cacheDir) : PathProviderService(cacheDir, cacheDir)
    {
        public override string GetSecureCachePath(string relativePath) =>
            SecurePathValidator.GetSecurePath(relativePath, CacheDirectory);
    }

    private sealed class TestStateFile : IDisposable
    {
        public TestStateFile()
        {
            CacheDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(CacheDirectory);
        }

        public string CacheDirectory { get; }

        public void Dispose()
        {
            if (Directory.Exists(CacheDirectory))
            {
                Directory.Delete(CacheDirectory, true);
            }
        }
    }
}
