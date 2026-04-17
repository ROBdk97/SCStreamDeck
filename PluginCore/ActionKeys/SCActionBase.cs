using BarRaider.SdTools;
using BarRaider.SdTools.Events;
using BarRaider.SdTools.Wrappers;
using Newtonsoft.Json.Linq;
using SCStreamDeck.ActionKeys.Settings;
using SCStreamDeck.Common;
using SCStreamDeck.Infrastructure;
using SCStreamDeck.Logging;
using SCStreamDeck.Models;
using SCStreamDeck.Services.Audio;
using SCStreamDeck.Services.Core;
using SCStreamDeck.Services.Keybinding;
using SCStreamDeck.Services.UI;
using System.Diagnostics.CodeAnalysis;

namespace SCStreamDeck.ActionKeys;

/// <summary>
///     Base class for Star Citizen Stream Deck key actions.
/// </summary>
public abstract class SCActionBase : KeypadBase
{
    #region Constructor and Initialization

    /// <summary>
    ///     Initializes the action with connection and payload.
    ///     Marked as [ExcludeFromCodeCoverage] because:
    ///     - Depends on Stream Deck SDK runtime (SDConnection, InitialPayload, ServiceLocator)
    ///     - SDK cannot be properly mocked without external dependencies
    /// </summary>
    [ExcludeFromCodeCoverage]
    protected SCActionBase(SDConnection connection, InitialPayload payload) : base(connection, payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Settings == null || payload.Settings.Count == 0)
        {
            Settings = new FunctionSettings();
        }
        else
        {
            Settings = payload.Settings.ToObject<FunctionSettings>() ?? new FunctionSettings();
        }

        SCActionBaseDependencies deps = ActionDependencies.ForSCActionBase();
        InitializationService = deps.InitializationService;
        KeybindingService = deps.KeybindingService;
        AudioPlayerService = deps.AudioPlayerService;
        PluginLocaleService = deps.PluginLocaleService;

        InitializationService.KeybindingsStateChanged += OnKeybindingsStateChanged;
        PluginLocaleService.LocaleChanged += OnPluginLocaleChanged;
        Connection.OnPropertyInspectorDidAppear += OnPropertyInspectorDidAppear;
        Connection.OnSendToPlugin += OnSendToPlugin;

        // Attempt migration immediately for keys created after initialization.
        // (Stream Deck may not call ReceivedSettings on startup.)
        TryMigrateFunctionSettingIfPossible();

        if (CanExecuteBindings)
        {
            SendPropertyInspectorUpdate();
        }
    }

    #endregion

    #region Audio Playback

    /// <summary>
    ///     Plays the configured click sound if ClickSoundPath is set.
    ///     TODO: Support ActivationMode - currently only plays on KeyPressed.
    ///     Future enhancement: Check Settings.ActivationMode and play on appropriate event
    ///     (KeyPressed, KeyReleased, or both depending on activation mode).
    /// </summary>
    protected void PlayClickSoundIfConfigured()
    {
        if (string.IsNullOrWhiteSpace(Settings.ClickSoundPath))
        {
            return;
        }

        if (!File.Exists(Settings.ClickSoundPath))
        {
            Settings.ClickSoundPath = null;
            return;
        }

        try
        {
            AudioPlayerService.Play(Settings.ClickSoundPath);
        }
        catch (Exception ex)
        {
            Log.Err($"[{GetType().Name}] Audio playback error: {ex.Message}", ex);
        }
    }

    #endregion

    #region Fields and Properties

    private InitializationService InitializationService { get; }
    private AudioPlayerService AudioPlayerService { get; }
    private PluginLocaleService PluginLocaleService { get; }
    protected KeybindingService KeybindingService { get; }

    protected bool CanExecuteBindings =>
        InitializationService.IsInitialized &&
        KeybindingService.IsLoaded &&
        InitializationService.KeybindingsJsonExists();

    protected FunctionSettings Settings { get; private set; }

    #endregion

    #region Property Inspector Methods

    /// <summary>
    ///     Sends the current keybinding status and available actions to the Property Inspector.
    /// </summary>
    private void SendPropertyInspectorUpdate() => _ = SendPropertyInspectorUpdateAsync();

    private async Task SendPropertyInspectorUpdateAsync()
    {
        PluginLocaleResolution pluginLocale = await GetPluginLocaleResolutionAsync().ConfigureAwait(false);

        try
        {
            if (!InitializationService.KeybindingsJsonExists() || !KeybindingService.IsLoaded)
            {
                await Connection.SendToPropertyInspectorAsync(
                        PropertyInspectorPayloadBuilder.BuildFunctionsPayload(false, [], pluginLocale))
                    .ConfigureAwait(false);

                return;
            }

            IReadOnlyList<KeybindingAction> allActions = KeybindingService.GetAllActions();
            IntPtr hkl = KeyboardLayoutDetector.DetectCurrent().Hkl;
            JArray groups = FunctionsPayloadBuilder.BuildGroupedFunctionsPayload(allActions, hkl);

            await Connection.SendToPropertyInspectorAsync(
                    PropertyInspectorPayloadBuilder.BuildFunctionsPayload(true, groups, pluginLocale))
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Err($"[{GetType().Name}]: {ex.Message}", ex);
            await Connection.SendToPropertyInspectorAsync(
                    PropertyInspectorPayloadBuilder.BuildFunctionsPayload(
                        false,
                        [],
                        PluginLocaleResolution.Default))
                .ConfigureAwait(false);
        }
    }

    private async Task<PluginLocaleResolution> GetPluginLocaleResolutionAsync()
    {
        try
        {
            return await PluginLocaleService.GetCurrentAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Err($"[{GetType().Name}] Failed to resolve plugin locale: {ex.Message}", ex);
            return PluginLocaleResolution.Default;
        }
    }

    /// <summary>
    ///     Called when the Property Inspector appears.
    /// </summary>
    private void OnPropertyInspectorDidAppear(object? sender,
        SDEventReceivedEventArgs<PropertyInspectorDidAppear> e) =>
        SendPropertyInspectorUpdate();

    private void OnKeybindingsStateChanged()
    {
        TryMigrateFunctionSettingIfPossible();
        SendPropertyInspectorUpdate();
    }

    private void OnPluginLocaleChanged() => SendPropertyInspectorUpdate();

    private void TryMigrateFunctionSettingIfPossible()
    {
        if (string.IsNullOrWhiteSpace(Settings.Function))
        {
            return;
        }

        if (!CanExecuteBindings)
        {
            return;
        }

        if (!KeybindingService.TryNormalizeActionId(Settings.Function, out string normalizedId) ||
            string.IsNullOrWhiteSpace(normalizedId))
        {
            return;
        }

        if (string.Equals(Settings.Function, normalizedId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Settings.Function = normalizedId;

        try
        {
            // Persist migrated settings without requiring PI interaction.
            _ = Connection.SetSettingsAsync(JObject.FromObject(Settings));
        }
        catch (Exception ex)
        {
            Log.Err($"[{GetType().Name}] Failed to persist migrated function id: {ex.Message}", ex);
        }
    }

    [ExcludeFromCodeCoverage]
    private void OnSendToPlugin(object? sender, SDEventReceivedEventArgs<SendToPlugin> e)
    {
        try
        {
            if (e.Event?.Payload == null)
            {
                return;
            }

            string? piEvent = null;
            if (e.Event.Payload.TryGetValue("event", out JToken? eventToken))
            {
                piEvent = eventToken.ToString();
            }
            else if (e.Event.Payload.TryGetValue("property_inspector", out JToken? legacyToken))
            {
                piEvent = legacyToken.ToString();
            }

            if (piEvent == "propertyInspectorConnected")
            {
                SendPropertyInspectorUpdate();
            }
        }
        catch (Exception ex)
        {
            Log.Err($"[{GetType().Name}] PI message handling error: {ex.Message}", ex);
        }
    }

    #endregion

    #region Lifecycle Methods

    /// <summary>
    ///     Disposes resources and unsubscribes from events.
    /// </summary>
    public override void Dispose()
    {
        Connection.OnPropertyInspectorDidAppear -= OnPropertyInspectorDidAppear;
        Connection.OnSendToPlugin -= OnSendToPlugin;
        InitializationService.KeybindingsStateChanged -= OnKeybindingsStateChanged;
        PluginLocaleService.LocaleChanged -= OnPluginLocaleChanged;
        GC.SuppressFinalize(this);
    }

    /// <summary>
    ///     Called when settings are received.
    ///     Marked as [ExcludeFromCodeCoverage] because:
    ///     - Depends on Stream Deck SDK payload handling
    ///     - Settings deserialization tested through FunctionSettings unit tests
    ///     - Integration testing requires Stream Deck host application
    /// </summary>
    [ExcludeFromCodeCoverage]
    public override void ReceivedSettings(ReceivedSettingsPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (payload.Settings is { Count: > 0 })
        {
            Settings = payload.Settings.ToObject<FunctionSettings>() ?? Settings;
        }

        TryMigrateFunctionSettingIfPossible();
    }

    /// <summary>
    ///     Called when global settings are received.
    /// </summary>
    public override void ReceivedGlobalSettings(ReceivedGlobalSettingsPayload payload)
    {
    }

    public override void OnTick()
    {
    }

    #endregion
}
