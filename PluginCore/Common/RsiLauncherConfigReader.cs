using SCStreamDeck.Logging;
using SCStreamDeck.Models;
using System.Text.RegularExpressions;

namespace SCStreamDeck.Common;

/// <summary>
///     Reads and parses RSI Launcher configuration files and logs to extract Star Citizen
///     installation paths. Uses channel-based detection to support any custom installation path.
/// </summary>
internal sealed partial class RsiLauncherConfigReader
{
    private string? _rsiLauncherDirectory;

    private static readonly string[] s_pathBlacklist =
    [
        @"\rsilauncher",
        @"\rsilauncher-updater",
        @"\appdata\local",
        @"\appdata\roaming",
        @"\windows\",
        @"\program files\roberts space industries\rsi launcher",
        ".exe",
        ".dll",
        ".log",
        @"\resources\",
        @"\pending\",
        "installer.exe"
    ];

    // HIGH CONFIDENCE: [LauncherSupport::validateDirectories] - C:\path1, C:\path2
    [GeneratedRegex("""\[LauncherSupport::validateDirectories\]\s*-\s*(.+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ValidateDirectoriesRegex();

    // HIGH CONFIDENCE: Launching Star Citizen LIVE from (C:\path\to\install)
    [GeneratedRegex("""Launching Star Citizen \w+ from \(([^)]+)\)""", RegexOptions.IgnoreCase)]
    private static partial Regex LaunchPathRegex();

    // MEDIUM CONFIDENCE: [Installer] - Installing/Starting/Delta update applied ... at C:\path
    [GeneratedRegex(
        """\[Installer\]\s*-\s*(?:Installing|Starting|Delta update applied).*?(?:at|in)\s+([A-Z]:[^"<>\r\n]+?)(?="|$)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex InstallerPathRegex();

    /// <summary>
    ///     Gets the RSI Launcher directory path, caching the result on first successful call.
    ///     Returns <see langword="null"/> if the directory does not exist or path normalisation fails.
    /// </summary>
    private string? GetRsiLauncherDirectory()
    {
        if (_rsiLauncherDirectory != null)
            return _rsiLauncherDirectory;

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string launcherPath = Path.Combine(appData, "rsilauncher");

        if (!Directory.Exists(launcherPath))
        {
            Log.Warn($"[{nameof(RsiLauncherConfigReader)}] RSI Launcher directory not found");
            return null;
        }

        if (!SecurePathValidator.TryNormalizePath(launcherPath, out string normalized))
        {
            Log.Warn($"[{nameof(RsiLauncherConfigReader)}] Could not normalise RSI Launcher path: {launcherPath}");
            return null;
        }

        _rsiLauncherDirectory = normalized;
        return _rsiLauncherDirectory;
    }

    /// <summary>
    ///     Finds RSI Launcher log files ordered by most recent write time.
    /// </summary>
    /// <param name="maxCount">Maximum number of log files to return.</param>
    public IEnumerable<string> FindLogFiles(int maxCount = 3)
    {
        string? launcherDir = GetRsiLauncherDirectory();
        if (launcherDir == null)
            yield break;

        string logsDir = Path.Combine(launcherDir, "logs");
        if (!Directory.Exists(logsDir))
        {
            Log.Warn($"[{nameof(RsiLauncherConfigReader)}] RSI Launcher logs directory not found");
            yield break;
        }

        // "log*.log" matches only RSI-rotated files: log.log, log.1.log, log.2.log, etc.
        IEnumerable<string> logFiles = Directory
            .GetFiles(logsDir, "log*.log")
            .OrderByDescending(File.GetLastWriteTime)
            .Take(maxCount);

        foreach (string logFile in logFiles)
            yield return logFile;
    }

    /// <summary>
    ///     Returns common default Star Citizen installation paths across all fixed drives.
    ///     Used when RSI Launcher logs contain no installation data.
    /// </summary>
    private static IEnumerable<string> GetDefaultInstallationPaths()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            SCConstants.Paths.RsiFolderName,
            SCConstants.Paths.SCFolderName);

        foreach (string driveName in DriveInfo.GetDrives()
                     .Where(d => d is { DriveType: DriveType.Fixed, IsReady: true })
                     .Select(d => d.Name))
        {
            yield return Path.Combine(driveName, SCConstants.Paths.RsiFolderName, SCConstants.Paths.SCFolderName);
            yield return Path.Combine(driveName, SCConstants.Paths.SCFolderName);
            yield return Path.Combine(driveName, "Games", SCConstants.Paths.SCFolderName);
            yield return Path.Combine(driveName, "SC");
        }
    }

    /// <summary>
    ///     Extracts Star Citizen installation root paths from a log file by detecting channel folders.
    ///     Falls back to well-known default paths when the log contains no usable data.
    /// </summary>
    /// <param name="logFilePath">Absolute path to the RSI Launcher log file to parse.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    ///     A case-insensitive <see cref="HashSet{T}"/> of resolved game root paths.
    ///     Empty if no paths could be found and no defaults exist on disk.
    /// </returns>
    public static async Task<HashSet<string>> ExtractPathsFromLogAsync(
        string logFilePath,
        CancellationToken cancellationToken = default)
    {
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(logFilePath))
            return paths;

        try
        {
            string content = await File.ReadAllTextAsync(logFilePath, cancellationToken).ConfigureAwait(false);
            ExtractPathsFromContent(content, paths);

            if (paths.Count == 0)
            {
                Log.Warn($"[{nameof(RsiLauncherConfigReader)}] No installation paths found in " +
                         $"'{Path.GetFileName(logFilePath)}', trying fallback detection");

                foreach (string defaultPath in GetDefaultInstallationPaths()
                             .Where(Directory.Exists)
                             .Where(IsValidGameRootCandidate))
                {
                    paths.Add(defaultPath);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Err($"[{nameof(RsiLauncherConfigReader)}] Could not read " +
                    $"'{Path.GetFileName(logFilePath)}': {ex.Message}", ex);
        }

        return paths;
    }

    /// <summary>
    ///     Runs all three detection strategies against the log content in priority order:
    ///     validateDirectories → launch entries → installer entries.
    /// </summary>
    private static void ExtractPathsFromContent(string content, HashSet<string> paths)
    {
        int strategy1Count = ExtractFromValidateDirectories(content, paths);
        int strategy2Count = ExtractFromLaunchEntries(content, paths);
        int strategy3Count = ExtractFromInstallerEntries(content, paths);

        // Warn if all strategies failed - possible RSI log format change
        if (strategy1Count == 0 && strategy2Count == 0 && strategy3Count == 0 && paths.Count == 0)
        {
            Log.Warn(
                $"[{nameof(RsiLauncherConfigReader)}] No installation paths found using any detection strategy. RSI Launcher log format may have changed, or no installations have been launched yet.");
        }
    }

    private static int ExtractFromValidateDirectories(string content, HashSet<string> paths)
    {
        int count = 0;

        foreach (Match match in ValidateDirectoriesRegex().Matches(content))
        {
            count++;
            foreach (string segment in match.Groups[1].Value.Split(','))
            {
                string trimmed = NormalizePath(segment.Trim());
                if (IsValidGameRootCandidate(trimmed))
                    TryExtractGameRootFromPath(trimmed, paths, requireChannelFolder: true);
            }
        }

        return count;
    }

    private static int ExtractFromLaunchEntries(string content, HashSet<string> paths)
    {
        int count = 0;

        foreach (Match match in LaunchPathRegex().Matches(content))
        {
            count++;
            string path = NormalizePath(match.Groups[1].Value.Trim());
            if (IsValidGameRootCandidate(path))
                TryExtractGameRootFromPath(path, paths, requireChannelFolder: true);
        }

        return count;
    }

    private static int ExtractFromInstallerEntries(string content, HashSet<string> paths)
    {
        int count = 0;

        foreach (Match match in InstallerPathRegex().Matches(content))
        {
            count++;
            string path = NormalizePath(match.Groups[1].Value.Trim());
            if (IsValidGameRootCandidate(path))
                TryExtractGameRootFromPath(path, paths, requireChannelFolder: false);
        }

        return count;
    }

    /// <summary>
    ///Extracts the game root from a path by detecting a known channel folder (LIVE, PTU, EPTU, HOTFIX).
    ///If a channel is found, its parent directory is added as the game root.
    ///If <paramref name="requireChannelFolder"/> is <see langword="false"/>, the path itself is added
    ///when no channel folder is detected (used for installer entries that point directly to the root).
    /// </summary>
    private static void TryExtractGameRootFromPath(
        string path,
        HashSet<string> paths,
        bool requireChannelFolder)
    {
        if (!SecurePathValidator.TryNormalizePath(path, out string normalizedPath))
            return;

        foreach (SCChannel channel in Enum.GetValues<SCChannel>())
        {
            string channelFolder = $@"\{channel.GetFolderName()}";
            int channelIndex = normalizedPath.LastIndexOf(channelFolder, StringComparison.OrdinalIgnoreCase);

            if (channelIndex > 0)
            {
                string gameRoot = normalizedPath[..channelIndex].TrimEnd('\\', '/');
                if (!string.IsNullOrWhiteSpace(gameRoot) &&
                    SecurePathValidator.TryNormalizePath(gameRoot, out string normalizedRoot))
                {
                    paths.Add(normalizedRoot);
                    return;
                }
            }
        }

        if (!requireChannelFolder)
            paths.Add(normalizedPath);
    }

    /// <summary>
    ///     Returns <see langword="true"/> if the path could plausibly be a Star Citizen game root.
    ///     Rejects launcher internals, system directories, and non-directory file references.
    /// </summary>
    private static bool IsValidGameRootCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 4)
            return false;

        return !s_pathBlacklist.Any(blocked =>
            path.Contains(blocked, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    ///     Normalises a raw path string by collapsing double backslashes to single backslashes.
    /// </summary>
    private static string NormalizePath(string path) =>
        path.Replace(@"\\", @"\");
}
