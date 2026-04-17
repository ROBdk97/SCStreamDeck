using Newtonsoft.Json;
using SCStreamDeck.Common;
using SCStreamDeck.Logging;
using SCStreamDeck.Models;
using SCStreamDeck.Services.Installation;
using System.Security;

namespace SCStreamDeck.Services.Core;

/// <summary>
///     Handles persistent storage and validation of plugin state.
/// </summary>
public sealed class StateService(PathProviderService pathProvider, IFileSystem fileSystem)
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
    private readonly Lock _lock = new();

    private readonly PathProviderService
        _pathProvider = pathProvider ?? throw new ArgumentNullException(nameof(pathProvider));

    private PluginState? _cachedState;
    private bool _cacheInitialized;

    public async Task<PluginState?> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_cacheInitialized)
            {
                return _cachedState;
            }
        }

        try
        {
            _ = _pathProvider.GetSecureCachePath(".plugin-state.json");
            PluginState? loadedState = await PluginState.LoadAsync(_fileSystem, _pathProvider.CacheDirectory, cancellationToken)
                .ConfigureAwait(false);
            lock (_lock)
            {
                _cachedState = loadedState;
                _cacheInitialized = true;
            }

            return loadedState;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or SecurityException)
        {
            Log.Err($"[{nameof(StateService)}] Failed to load plugin state", ex);
            return null;
        }
    }

    public async Task SaveStateAsync(PluginState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        try
        {
            _pathProvider.EnsureDirectoriesExist();
            _ = _pathProvider.GetSecureCachePath(".plugin-state.json");
            PluginState normalizedState = state.NormalizeForPersistence();
            await normalizedState.SaveAsync(_fileSystem, _pathProvider.CacheDirectory, cancellationToken).ConfigureAwait(false);
            lock (_lock)
            {
                _cachedState = normalizedState;
                _cacheInitialized = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or SecurityException)
        {
            Log.Err($"[{nameof(StateService)}] Failed to save plugin state", ex);
            throw;
        }
    }

    public async Task<IReadOnlyList<SCInstallCandidate>?> GetCachedCandidatesAsync(
        CancellationToken cancellationToken = default)
    {
        PluginState? state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        return state?.GetCachedCandidates();
    }

    public async Task UpdateInstallationAsync(SCChannel channel, InstallationState installation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        PluginState currentState = await LoadOrCreateStateAsync(cancellationToken).ConfigureAwait(false);
        PluginState updatedState = currentState
            .WithInstallation(channel, installation)
            .WithLastInitialized(DateTime.UtcNow);

        await SaveStateAsync(updatedState, cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveInstallationAsync(SCChannel channel, CancellationToken cancellationToken = default)
    {
        PluginState? currentState = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        if (currentState == null)
        {
            return;
        }

        PluginState updatedState = currentState.WithoutInstallation(channel);
        await SaveStateAsync(updatedState, cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateSelectedChannelAsync(SCChannel channel, CancellationToken cancellationToken = default)
    {
        PluginState currentState = await LoadOrCreateStateAsync(cancellationToken).ConfigureAwait(false);
        PluginState updatedState = currentState.WithSelectedChannel(channel);
        await SaveStateAsync(updatedState, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> GetSelectedThemeAsync(CancellationToken cancellationToken = default)
    {
        PluginState? state = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        return state?.SelectedTheme;
    }

    public async Task UpdateSelectedThemeAsync(string? themeFile, CancellationToken cancellationToken = default)
    {
        PluginState currentState = await LoadOrCreateStateAsync(cancellationToken).ConfigureAwait(false);
        PluginState updatedState = currentState.WithSelectedTheme(themeFile);
        await SaveStateAsync(updatedState, cancellationToken).ConfigureAwait(false);
    }

    public void DeleteStateFile()
    {
        try
        {
            string stateFile = _pathProvider.GetSecureCachePath(".plugin-state.json");
            if (_fileSystem.FileExists(stateFile))
            {
                _fileSystem.DeleteFile(stateFile);
            }

            lock (_lock)
            {
                _cachedState = null;
                _cacheInitialized = true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            Log.Err($"[{nameof(StateService)}] Failed to delete plugin state", ex);
        }
    }

    private async Task<PluginState> LoadOrCreateStateAsync(CancellationToken cancellationToken)
    {
        PluginState? currentState = await LoadStateAsync(cancellationToken).ConfigureAwait(false);
        if (currentState != null)
        {
            return currentState;
        }

        return PluginState.CreateDefault();
    }
}
