using SCStreamDeck.Models;
using System.Globalization;

namespace SCStreamDeck.Services.Core;

public sealed class PluginLocaleService(StateService stateService)
{
    private readonly StateService _stateService = stateService ?? throw new ArgumentNullException(nameof(stateService));

    public event Action? LocaleChanged;

    public static PluginLocaleResolution Resolve(PluginLocaleSettings? settings, string? detectedLocale)
    {
        PluginLocaleSettings normalizedSettings = (settings ?? PluginLocaleSettings.Default).Normalize();
        string? normalizedDetectedLocale = PluginLocaleSettings.NormalizeLocale(detectedLocale);
        string effectiveLocale = ResolveEffectiveLocale(normalizedSettings, normalizedDetectedLocale);

        return new PluginLocaleResolution(
            normalizedSettings.Mode,
            normalizedSettings.Override,
            normalizedDetectedLocale,
            effectiveLocale);
    }

    public async Task<PluginLocaleResolution> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        PluginState? currentState = await _stateService.LoadStateAsync(cancellationToken).ConfigureAwait(false);
        PluginLocaleSettings settings = (currentState?.PluginLocale ?? PluginLocaleSettings.Default).Normalize();
        string? detectedLocale = DetectCurrentLocale();

        if (currentState != null &&
            detectedLocale != null &&
            !string.Equals(settings.LastDetected, detectedLocale, StringComparison.Ordinal))
        {
            PluginLocaleSettings updatedSettings = settings with { LastDetected = detectedLocale };
            await _stateService.SaveStateAsync(currentState.WithPluginLocale(updatedSettings), cancellationToken)
                .ConfigureAwait(false);
            settings = updatedSettings;
        }

        return Resolve(settings, detectedLocale);
    }

    public async Task UpdateSettingsAsync(
        string? mode,
        string? overrideLocale,
        CancellationToken cancellationToken = default)
    {
        PluginState currentState = await LoadOrCreateStateAsync(cancellationToken).ConfigureAwait(false);
        PluginLocaleSettings currentSettings = currentState.PluginLocale ?? PluginLocaleSettings.Default;

        PluginLocaleSettings updatedSettings = new(
            mode ?? PluginLocaleSettings.AutoMode,
            overrideLocale,
            currentSettings.LastDetected);

        updatedSettings = updatedSettings.Normalize();

        if (updatedSettings == currentSettings)
        {
            return;
        }

        await _stateService.SaveStateAsync(currentState.WithPluginLocale(updatedSettings), cancellationToken)
            .ConfigureAwait(false);
        LocaleChanged?.Invoke();
    }

    public static string? DetectCurrentLocale()
    {
        string? locale = TryNormalizeCulture(CultureInfo.CurrentUICulture);
        return locale ?? TryNormalizeCulture(CultureInfo.CurrentCulture);
    }

    public static string ResolveEffectiveLocale(PluginLocaleSettings? settings, string? detectedLocale)
    {
        PluginLocaleSettings normalizedSettings = (settings ?? PluginLocaleSettings.Default).Normalize();
        string? normalizedDetectedLocale = PluginLocaleSettings.NormalizeLocale(detectedLocale);

        if (string.Equals(normalizedSettings.Mode, PluginLocaleSettings.OverrideMode, StringComparison.Ordinal) &&
            normalizedSettings.Override != null)
        {
            return normalizedSettings.Override;
        }

        return normalizedDetectedLocale ?? normalizedSettings.LastDetected ?? PluginLocaleSettings.DefaultLocale;
    }

    private async Task<PluginState> LoadOrCreateStateAsync(CancellationToken cancellationToken)
    {
        PluginState? currentState = await _stateService.LoadStateAsync(cancellationToken).ConfigureAwait(false);
        return currentState ?? PluginState.CreateDefault();
    }

    private static string? TryNormalizeCulture(CultureInfo cultureInfo)
    {
        ArgumentNullException.ThrowIfNull(cultureInfo);

        return PluginLocaleSettings.NormalizeLocale(cultureInfo.Name) ??
               PluginLocaleSettings.NormalizeLocale(cultureInfo.TwoLetterISOLanguageName);
    }
}
