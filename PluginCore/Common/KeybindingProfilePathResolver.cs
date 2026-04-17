using SCStreamDeck.Logging;

namespace SCStreamDeck.Common;

/// <summary>
///     Attempts to locate the user profile folder containing actionmaps.xml.
///     Assumes path structure: {channelPath}\user\client\{instanceId}\Profiles\default\actionmaps.xml
/// </summary>
public static class KeybindingProfilePathResolver
{
    private static readonly Lock s_cacheLock = new();
    private static readonly Dictionary<string, string?> s_cachedActionMapsByChannel = new(StringComparer.OrdinalIgnoreCase);

    public static string? TryFindActionMapsXml(string? channelPath)
    {
        if (string.IsNullOrWhiteSpace(channelPath))
        {
            return null;
        }

        try
        {
            if (TryGetCachedPath(channelPath, out string? cachedPath))
            {
                return cachedPath;
            }

            string userDir = Path.Combine(channelPath, "user");
            string clientDir = Path.Combine(userDir, "client");
            if (!Directory.Exists(userDir) || !Directory.Exists(clientDir))
            {
                return null;
            }

            string? resolvedPath = FindFirstExistingProfile(clientDir);
            if (!string.IsNullOrWhiteSpace(resolvedPath))
            {
                CachePath(channelPath, resolvedPath);
            }
            return resolvedPath;
        }
        catch (Exception ex)
        {
            Log.Err($"[{nameof(KeybindingProfilePathResolver)}] {ex.Message}", ex);
        }

        return null;
    }

    private static string? FindFirstExistingProfile(string clientDir)
    {
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (string instanceDir in Directory.GetDirectories(clientDir))
        {
            string candidate = Path.Combine(instanceDir, "Profiles", "default", SCConstants.Files.ActionMapsFileName);

            if (!File.Exists(candidate))
            {
                continue;
            }

            return SecurePathValidator.TryNormalizePath(candidate, out string normalized) ? normalized : null;
        }

        return null;
    }

    private static bool TryGetCachedPath(string channelPath, out string? cachedPath)
    {
        lock (s_cacheLock)
        {
            if (!s_cachedActionMapsByChannel.TryGetValue(channelPath, out cachedPath) ||
                string.IsNullOrWhiteSpace(cachedPath))
            {
                cachedPath = null;
                return false;
            }
        }

        if (File.Exists(cachedPath))
        {
            return true;
        }

        lock (s_cacheLock)
        {
            s_cachedActionMapsByChannel.Remove(channelPath);
        }
        cachedPath = null;
        return false;
    }

    private static void CachePath(string channelPath, string? actionMapsPath)
    {
        lock (s_cacheLock)
        {
            s_cachedActionMapsByChannel[channelPath] = actionMapsPath;
        }
    }
}
