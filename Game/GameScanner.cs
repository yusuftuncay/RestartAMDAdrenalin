using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using AdrenalinRestart.Utilities;
using Microsoft.Win32;

namespace AdrenalinRestart.Game;

[SupportedOSPlatform("windows")]
internal static partial class GameScanner
{
    #region Public API
    internal static Dictionary<string, string> ScanInstalledGameProcessNames()
    {
        // Map: Process Key -> Display Name
        var processNameToDisplayName = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );

        // Collect From Steam
        foreach (var (displayName, rootDirectory) in DiscoverSteamGames())
        {
            TryAddNamedGame(processNameToDisplayName, rootDirectory, displayName);
        }

        // Collect From Epic
        foreach (var (displayName, rootDirectory) in DiscoverEpicGames())
        {
            TryAddNamedGame(processNameToDisplayName, rootDirectory, displayName);
        }

        // Collect From Riot
        foreach (var (displayName, rootDirectory) in DiscoverRiotGames())
        {
            TryAddNamedGame(processNameToDisplayName, rootDirectory, displayName);
        }

        // Collect From Rockstar
        foreach (var rootDirectory in DiscoverRockstarGameRoots())
        {
            TryAddResolvedGame(processNameToDisplayName, rootDirectory);
        }

        // Collect From Roblox
        foreach (var (displayName, rootDirectory) in DiscoverRobloxGames())
        {
            TryAddNamedGame(processNameToDisplayName, rootDirectory, displayName);
        }

        // Collect From Common Games Directories
        foreach (var rootDirectory in DiscoverCommonGamesDirectories())
        {
            TryAddResolvedGame(processNameToDisplayName, rootDirectory);
        }

        return processNameToDisplayName;
    }
    #endregion

    #region Integration
    private static void TryAddResolvedGame(Dictionary<string, string> map, string rootDirectory)
    {
        var executablePath = ResolveMainExecutable(rootDirectory);
        if (executablePath == null)
            return;

        if (!IsLikelyGameExecutable(executablePath))
            return;

        var processName = NormalizeProcessKey(Path.GetFileNameWithoutExtension(executablePath));
        if (string.IsNullOrWhiteSpace(processName))
            return;

        // Build Display Name From Root Folder
        var displayName = NormalizeDisplayName(new DirectoryInfo(rootDirectory).Name);
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length < 2)
            return;

        map[processName] = displayName;
    }

    private static void TryAddNamedGame(
        Dictionary<string, string> map,
        string rootDirectory,
        string displayName
    )
    {
        var executablePath = ResolveMainExecutable(rootDirectory);
        if (executablePath == null)
            return;

        if (!IsLikelyGameExecutable(executablePath))
            return;

        var processName = NormalizeProcessKey(Path.GetFileNameWithoutExtension(executablePath));
        if (string.IsNullOrWhiteSpace(processName))
            return;

        var normalizedDisplayName = NormalizeDisplayName(displayName);
        if (string.IsNullOrWhiteSpace(normalizedDisplayName) || normalizedDisplayName.Length < 2)
            return;

        map[processName] = normalizedDisplayName;
    }
    #endregion

    #region Executable Resolution
    private static string? ResolveMainExecutable(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
            return null;

        // Scan Root and Known Subdirectory Layouts
        var probedDirectories = new[]
        {
            rootDirectory,
            Path.Combine(rootDirectory, "Binaries"),
            Path.Combine(rootDirectory, "Binaries", "Win64"),
            Path.Combine(rootDirectory, "bin", "win64"),
            Path.Combine(rootDirectory, "game", "bin", "win64"),
            Path.Combine(rootDirectory, "live", "ShooterGame", "Binaries", "Win64"),
        };

        var folderName = new DirectoryInfo(rootDirectory).Name;
        var bestExe = (string?)null;
        var bestScore = int.MinValue;

        foreach (var probeDirectory in probedDirectories)
        {
            if (!Directory.Exists(probeDirectory))
                continue;

            foreach (var executablePath in SafeFs.EnumerateFiles(probeDirectory, "*.exe"))
            {
                var score = ScoreExecutable(executablePath, folderName);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestExe = executablePath;
                }
            }
        }

        return bestExe;
    }

    private static int ScoreExecutable(string executablePath, string folderName)
    {
        var name = Path.GetFileNameWithoutExtension(executablePath);
        var score = 0;

        // Penalize Utility Executables
        if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase))
            score -= 5;
        if (name.Contains("helper", StringComparison.OrdinalIgnoreCase))
            score -= 5;
        if (name.Contains("crash", StringComparison.OrdinalIgnoreCase))
            score -= 5;
        if (name.Contains("report", StringComparison.OrdinalIgnoreCase))
            score -= 5;
        if (name.Contains("uninstall", StringComparison.OrdinalIgnoreCase))
            score -= 5;
        if (name.Contains("setup", StringComparison.OrdinalIgnoreCase))
            score -= 5;

        // Reward Known Game Exe Patterns
        if (name.Contains("win64", StringComparison.OrdinalIgnoreCase))
            score += 3;
        if (name.Contains("shipping", StringComparison.OrdinalIgnoreCase))
            score += 3;

        // Reward Name Match to Folder
        if (name.Equals(folderName, StringComparison.OrdinalIgnoreCase))
            score += 4;
        else if (folderName.Contains(name, StringComparison.OrdinalIgnoreCase))
            score += 2;

        return score;
    }

    private static bool IsLikelyGameExecutable(string executablePath)
    {
        var name = Path.GetFileNameWithoutExtension(executablePath);

        // Reject Known Non-Game Executable Patterns
        if (name.Contains("helper", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.Contains("service", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.Contains("crash", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.Contains("report", StringComparison.OrdinalIgnoreCase))
            return false;

        return true;
    }
    #endregion

    #region Steam
    private static IEnumerable<(string DisplayName, string Root)> DiscoverSteamGames()
    {
        // Find Steam Installation
        var steamRoot = FindSteamInstallPath();
        if (steamRoot is null)
            yield break;

        // Locate Library Folders VDF Config
        var libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
            yield break;

        foreach (var libraryRoot in ParseSteamLibraryFolders(libraryFoldersPath))
        {
            var steamAppsDirectory = Path.Combine(libraryRoot, "steamapps");
            if (!Directory.Exists(steamAppsDirectory))
                continue;

            // Read Each App Manifest
            foreach (
                var manifestPath in SafeFs.EnumerateFiles(steamAppsDirectory, "appmanifest_*.acf")
            )
            {
                var (manifestName, installDir) = TryParseSteamAppManifest(manifestPath);
                if (installDir is null)
                    continue;

                var gameRoot = Path.Combine(steamAppsDirectory, "common", installDir);
                if (!Directory.Exists(gameRoot))
                    continue;

                // Use Manifest Name if Available
                var displayName = !string.IsNullOrWhiteSpace(manifestName)
                    ? manifestName
                    : installDir;
                yield return (displayName, gameRoot);
            }
        }
    }

    private static string? FindSteamInstallPath()
    {
        var candidatePaths = new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" };
        return candidatePaths.FirstOrDefault(Directory.Exists);
    }

    private static IEnumerable<string> ParseSteamLibraryFolders(string libraryFoldersPath)
    {
        string fileText;
        try
        {
            fileText = File.ReadAllText(libraryFoldersPath);
        }
        catch
        {
            yield break;
        }

        // Always Include the Primary Steam Library
        var steamRoot = Path.GetDirectoryName(Path.GetDirectoryName(libraryFoldersPath))!;
        yield return steamRoot;

        // Yield Additional Library Paths From VDF
        foreach (Match match in InstalledPathRegex().Matches(fileText))
        {
            var rawPath = match.Groups["p"].Value;
            var normalizedPath = rawPath.Replace(@"\\", @"\");
            if (Directory.Exists(normalizedPath))
                yield return normalizedPath;
        }
    }

    private static (string? Name, string? InstallDir) TryParseSteamAppManifest(string manifestPath)
    {
        try
        {
            var manifestText = File.ReadAllText(manifestPath);

            var installDirMatch = InstalledDirectoryRegex().Match(manifestText);
            var nameMatch = ManifestNameRegex().Match(manifestText);

            var installDir = installDirMatch.Success ? installDirMatch.Groups["d"].Value : null;
            var name = nameMatch.Success ? nameMatch.Groups["n"].Value : null;

            return (name, installDir);
        }
        catch
        {
            return (null, null);
        }
    }
    #endregion

    #region Epic
    private static IEnumerable<(string DisplayName, string Root)> DiscoverEpicGames()
    {
        // Locate Epic Manifests Directory
        var manifestsDirectory = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        if (!Directory.Exists(manifestsDirectory))
            yield break;

        foreach (var itemFilePath in SafeFs.EnumerateFiles(manifestsDirectory, "*.item"))
        {
            var result = TryParseEpicManifest(itemFilePath);
            if (result is null)
                continue;

            var (displayName, installLocation) = result.Value;
            if (Directory.Exists(installLocation))
                yield return (displayName, installLocation);
        }
    }

    private static (string DisplayName, string InstallLocation)? TryParseEpicManifest(
        string itemFilePath
    )
    {
        try
        {
            using var jsonDocument = JsonDocument.Parse(File.ReadAllText(itemFilePath));
            var root = jsonDocument.RootElement;

            if (!root.TryGetProperty("InstallLocation", out var locationProperty))
                return null;
            if (locationProperty.ValueKind != JsonValueKind.String)
                return null;

            var installLocation = locationProperty.GetString();
            if (string.IsNullOrWhiteSpace(installLocation))
                return null;

            var displayName = string.Empty;
            if (
                root.TryGetProperty("DisplayName", out var nameProperty)
                && nameProperty.ValueKind == JsonValueKind.String
            )
            {
                displayName = nameProperty.GetString() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(displayName))
                displayName = new DirectoryInfo(installLocation).Name;

            return (displayName, installLocation);
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Riot
    private static IEnumerable<(string DisplayName, string Root)> DiscoverRiotGames()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Well-Known Riot Game Directories
        var knownPaths = new[]
        {
            (@"C:\Riot Games\VALORANT", "VALORANT"),
            (@"C:\Riot Games\League of Legends", "League of Legends"),
        };

        foreach (var (path, displayName) in knownPaths)
        {
            if (Directory.Exists(path) && seen.Add(path))
                yield return (displayName, path);
        }

        // Discover Additional Paths From RiotClientInstalls.json
        var installsFilePath = @"C:\ProgramData\Riot Games\RiotClientInstalls.json";
        if (!File.Exists(installsFilePath))
            yield break;

        var additionalRoots = new List<string>();

        try
        {
            using var jsonDocument = JsonDocument.Parse(File.ReadAllText(installsFilePath));
            foreach (var property in jsonDocument.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    continue;

                var rawPath = property.Value.GetString()?.TrimEnd('\\', '/');
                if (string.IsNullOrWhiteSpace(rawPath))
                    continue;

                // Walk Up Two Levels to Find Riot Games Root, Then Scan Subdirs
                var clientDirectory = Path.GetDirectoryName(rawPath);
                var riotRoot = Path.GetDirectoryName(clientDirectory);
                if (string.IsNullOrWhiteSpace(riotRoot) || !Directory.Exists(riotRoot))
                    continue;

                foreach (var gameDirectory in SafeFs.EnumerateDirectories(riotRoot))
                {
                    if (seen.Add(gameDirectory))
                        additionalRoots.Add(gameDirectory);
                }
            }
        }
        catch { }

        foreach (var root in additionalRoots)
            yield return (new DirectoryInfo(root).Name, root);
    }
    #endregion

    #region Rockstar
    private static IEnumerable<string> DiscoverRockstarGameRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Try Registry First
        foreach (var registryRoot in DiscoverRockstarFromRegistry(seen))
            yield return registryRoot;

        // Fall Back to Known Directories
        var candidateDirectories = new[]
        {
            @"C:\Program Files\Rockstar Games",
            @"C:\Program Files (x86)\Rockstar Games",
        };

        foreach (var baseDirectory in candidateDirectories)
        {
            if (!Directory.Exists(baseDirectory))
                continue;

            // Only First-Level Subdirectories
            foreach (var childDirectory in SafeFs.EnumerateDirectories(baseDirectory))
            {
                if (seen.Add(childDirectory))
                    yield return childDirectory;
            }
        }
    }

    private static IEnumerable<string> DiscoverRockstarFromRegistry(HashSet<string> seen)
    {
        var results = new List<string>();

        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Rockstar Games");
            if (baseKey is not null)
            {
                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = baseKey.OpenSubKey(subKeyName);
                        if (subKey is null)
                            continue;

                        var installLocation =
                            (subKey.GetValue("InstallFolder") as string)
                            ?? (subKey.GetValue("InstallLocation") as string)
                            ?? string.Empty;

                        installLocation = installLocation.TrimEnd('\\', '/');

                        if (
                            !string.IsNullOrWhiteSpace(installLocation)
                            && Directory.Exists(installLocation)
                            && seen.Add(installLocation)
                        )
                        {
                            results.Add(installLocation);
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        foreach (var result in results)
            yield return result;
    }
    #endregion

    #region Roblox
    private static IEnumerable<(string DisplayName, string Root)> DiscoverRobloxGames()
    {
        var robloxVersionsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Roblox",
            "Versions"
        );

        if (!Directory.Exists(robloxVersionsPath))
            yield break;

        // Check First-Level Version Directories for RobloxPlayerBeta.exe
        foreach (var versionDirectory in SafeFs.EnumerateDirectories(robloxVersionsPath))
        {
            var playerExePath = Path.Combine(versionDirectory, "RobloxPlayerBeta.exe");
            if (File.Exists(playerExePath))
            {
                yield return ("Roblox", versionDirectory);
                yield break;
            }
        }
    }
    #endregion

    #region Common Games Directories
    private static IEnumerable<string> DiscoverCommonGamesDirectories()
    {
        // Scan Only Known Common Game Directories
        var candidateDirectories = new[] { @"C:\Games", @"D:\Games", @"E:\Games" };

        foreach (var gamesDirectory in candidateDirectories)
        {
            if (!Directory.Exists(gamesDirectory))
                continue;

            // Only First-Level Subdirectories
            foreach (var childDirectory in SafeFs.EnumerateDirectories(gamesDirectory))
            {
                yield return childDirectory;
            }
        }
    }
    #endregion

    #region Display Name Helpers
    private static string NormalizeDisplayName(string value)
    {
        var cleaned = value.Trim();

        if (cleaned.Length < 2)
            return string.Empty;

        // Strip Trademark and Registered Symbols
        cleaned = cleaned.Replace("\u00AE", "").Replace("\u2122", "");

        // Normalize Separators to Spaces
        cleaned = cleaned.Replace('_', ' ');
        cleaned = cleaned.Replace('-', ' ');

        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();

        // Strip Platform and Build Tags
        cleaned = Win64Regex().Replace(cleaned, "").Trim();
        cleaned = Win32Regex().Replace(cleaned, "").Trim();
        cleaned = X64Regex().Replace(cleaned, "").Trim();
        cleaned = X86Regex().Replace(cleaned, "").Trim();
        cleaned = ShippingRegex().Replace(cleaned, "").Trim();
        cleaned = ReleaseRegex().Replace(cleaned, "").Trim();
        cleaned = LauncherRegex().Replace(cleaned, "").Trim();

        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();

        if (cleaned.Length < 2)
            return string.Empty;

        return ToTitleCaseInvariant(cleaned);
    }

    private static string InsertSpacesBetweenWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var characters = new List<char>(value.Length + 8);

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            var previous = index > 0 ? value[index - 1] : '\0';
            var next = index + 1 < value.Length ? value[index + 1] : '\0';

            // Detect Word Boundary
            var isBoundary =
                index > 0
                && current != ' '
                && previous != ' '
                && (
                    (char.IsLower(previous) && char.IsUpper(current))
                    || (char.IsLetter(previous) && char.IsDigit(current))
                    || (char.IsDigit(previous) && char.IsLetter(current))
                    || (char.IsUpper(previous) && char.IsUpper(current) && char.IsLower(next))
                );

            if (isBoundary)
                characters.Add(' ');

            characters.Add(current);
        }

        return new string([.. characters]);
    }

    private static string ToTitleCaseInvariant(string value)
    {
        var lower = value.ToLowerInvariant();
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Capitalize Each Word
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            words[index] = char.ToUpperInvariant(word[0]) + word[1..];
        }

        return string.Join(' ', words);
    }
    #endregion

    #region Process Key Helpers
    internal static string NormalizeProcessKey(string name)
    {
        var cleaned = name.Replace("_", "").Replace("-", "").ToLowerInvariant();

        if (cleaned.StartsWith("acs") || cleaned.Contains("assettocorsa"))
            return "assettocorsa";

        if (cleaned.Contains("valorant"))
            return "valorant";

        return cleaned;
    }
    #endregion

    #region Regexes
    [GeneratedRegex("\"name\"\\s*\"(?<n>[^\"]+)\"", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ManifestNameRegex();

    [GeneratedRegex("\"installdir\"\\s*\"(?<d>[^\"]+)\"", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex InstalledDirectoryRegex();

    [GeneratedRegex("\"path\"\\s*\"(?<p>[^\"]+)\"", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex InstalledPathRegex();

    [GeneratedRegex("\\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex("\\bWin64\\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex Win64Regex();

    [GeneratedRegex("\\bWin32\\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex Win32Regex();

    [GeneratedRegex("\\bx64\\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex X64Regex();

    [GeneratedRegex("\\bx86\\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex X86Regex();

    [GeneratedRegex("\\bShipping\\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ShippingRegex();

    [GeneratedRegex("\\bRelease\\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex ReleaseRegex();

    [GeneratedRegex("\\bLauncher\\b", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex LauncherRegex();
    #endregion
}
