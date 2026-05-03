using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using RestartAMDAdrenalin.Configuration;
using RestartAMDAdrenalin.Utilities;

namespace RestartAMDAdrenalin.Game;

[SupportedOSPlatform("windows")]
internal static partial class GameScanner
{
    internal static Dictionary<string, string> ScanInstalledGameProcessNames()
    {
        var processNameToDisplayName = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase
        );

        // Discover Games From Each Supported Store
        foreach (var rootDirectory in DiscoverSteamGameRoots())
        {
            AddGameExecutablesRecursive(processNameToDisplayName, rootDirectory);
        }

        foreach (var rootDirectory in DiscoverEpicGameRoots())
        {
            AddGameExecutablesRecursive(processNameToDisplayName, rootDirectory);
        }

        foreach (var rootDirectory in DiscoverRiotGameRoots())
        {
            AddGameExecutablesRecursive(processNameToDisplayName, rootDirectory);
        }

        foreach (var rootDirectory in DiscoverRockstarGameRoots())
        {
            AddGameExecutablesRecursive(processNameToDisplayName, rootDirectory);
        }

        foreach (var rootDirectory in DiscoverUninstallRegistryGameRoots())
        {
            AddGameExecutablesRecursive(processNameToDisplayName, rootDirectory);
        }

        return processNameToDisplayName
            .Select(kvp => new
            {
                kvp.Key,
                Name = TryGetNiceDisplayName(kvp.Value, kvp.Key, Path.GetDirectoryName(kvp.Value)!),
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.Name))
            .ToDictionary(x => x.Key, x => x.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddGameExecutablesRecursive(
        Dictionary<string, string> output,
        string rootDirectory
    )
    {
        // Validate Root Directory
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return;
        }

        if (!Directory.Exists(rootDirectory))
        {
            return;
        }

        // Enumerate and Filter Executable Files
        foreach (
            var executablePath in SafeFs.EnumerateFilesRecursivePruned(
                rootDirectory,
                "*.exe",
                AppConfig.s_pathTokenBlocklist,
                maxDepth: 4
            )
        )
        {
            // Skip Files That Fail the Game Exe Heuristic
            if (!IsLikelyGameExe(executablePath))
            {
                continue;
            }

            var rawName = Path.GetFileNameWithoutExtension(executablePath);
            var processName = NormalizeProcessKey(rawName);
            static string NormalizeProcessKey(string name)
            {
                var cleaned = name.Replace("_", "").Replace("-", "").ToLowerInvariant();

                if (cleaned.StartsWith("acs"))
                    return "assettocorsa";
                if (cleaned.Contains("assettocorsa"))
                    return "assettocorsa";

                return cleaned;
            }
            if (string.IsNullOrWhiteSpace(processName))
            {
                continue;
            }

            if (output.TryGetValue(processName, out var existingPath))
            {
                var currentScore = ScoreExe(executablePath);
                var existingScore = ScoreExe(existingPath);

                static int ScoreExe(string path)
                {
                    var name = Path.GetFileNameWithoutExtension(path);

                    var score = 0;

                    if (name.Contains("win64", StringComparison.OrdinalIgnoreCase))
                    {
                        score += 2;
                    }
                    if (name.Contains("shipping", StringComparison.OrdinalIgnoreCase))
                    {
                        score += 2;
                    }
                    if (name.Contains("launcher", StringComparison.OrdinalIgnoreCase))
                    {
                        score -= 3;
                    }
                    if (name.Contains("helper", StringComparison.OrdinalIgnoreCase))
                    {
                        score -= 3;
                    }

                    return score;
                }

                if (currentScore <= existingScore)
                {
                    continue;
                }
            }

            // Register the Display Name for This Executable
            output[processName] = executablePath;
        }
    }

    private static string TryGetNiceDisplayName(
        string executablePath,
        string processName,
        string rootDirectory
    )
    {
        // Prefer Name From File Version Metadata
        var fileDisplayName = TryGetDisplayNameFromFileVersion(executablePath);
        if (!string.IsNullOrWhiteSpace(fileDisplayName))
        {
            return NormalizeDisplayName(fileDisplayName!);
        }

        // Fall Back to Root Directory Name
        var directoryDisplayName = TryGetDisplayNameFromRootDirectory(rootDirectory);
        if (!string.IsNullOrWhiteSpace(directoryDisplayName))
        {
            return NormalizeDisplayName(directoryDisplayName!);
        }

        // Default to Process Name
        return NormalizeDisplayName(processName);
    }

    private static string? TryGetDisplayNameFromFileVersion(string executablePath)
    {
        try
        {
            // Read File Version Metadata
            var versionInfo = FileVersionInfo.GetVersionInfo(executablePath);

            // Prefer Product Name, Fall Back to File Description
            if (!string.IsNullOrWhiteSpace(versionInfo.ProductName))
            {
                return versionInfo.ProductName;
            }

            if (!string.IsNullOrWhiteSpace(versionInfo.FileDescription))
            {
                return versionInfo.FileDescription;
            }
        }
        catch { }

        return null;
    }

    private static string? TryGetDisplayNameFromRootDirectory(string rootDirectory)
    {
        try
        {
            var directoryName = new DirectoryInfo(rootDirectory).Name;
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                return null;
            }

            return directoryName;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeDisplayName(string value)
    {
        var cleaned = value.Trim();

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

        // Insert Boundaries and Apply Title Case
        cleaned = InsertSpacesBetweenWords(cleaned);
        cleaned = WhitespaceRegex().Replace(cleaned, " ").Trim();

        if (cleaned.Equals("assettocorsa", StringComparison.OrdinalIgnoreCase))
            return "Assetto Corsa";

        return ToTitleCaseInvariant(cleaned);
    }

    private static string InsertSpacesBetweenWords(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var characters = new List<char>(value.Length + 8);

        for (var index = 0; index < value.Length; index++)
        {
            var current = value[index];
            var previous = index > 0 ? value[index - 1] : '\0';
            var next = index + 1 < value.Length ? value[index + 1] : '\0';

            // Detect Word Boundary Conditions
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

            // Insert Space at Boundary
            if (isBoundary)
            {
                characters.Add(' ');
            }

            characters.Add(current);
        }

        return new string([.. characters]);
    }

    private static string ToTitleCaseInvariant(string value)
    {
        // Lowercase All Text First
        var lower = value.ToLowerInvariant();
        var words = lower.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Capitalize Words Longer Than 3 Characters
        for (var index = 0; index < words.Length; index++)
        {
            var word = words[index];
            if (word.Length <= 3)
            {
                words[index] = char.ToUpperInvariant(word[0]) + word[1..];
                continue;
            }

            words[index] = char.ToUpperInvariant(word[0]) + word[1..];
        }

        return string.Join(' ', words);
    }

    private static bool IsLikelyGameExe(string executablePath)
    {
        // Validate Path and File Existence
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        if (!File.Exists(executablePath))
            return false;

        var executableName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(executableName))
            return false;
        if (executableName.Equals("helper", StringComparison.OrdinalIgnoreCase))
            return false;
        if (executableName.Contains("helper", StringComparison.OrdinalIgnoreCase))
            return false;
        if (executableName.Contains("import", StringComparison.OrdinalIgnoreCase))
            return false;
        if (executableName.Contains("crash", StringComparison.OrdinalIgnoreCase))
            return false;
        if (executableName.Contains("report", StringComparison.OrdinalIgnoreCase))
            return false;

        // Apply Name Based and Path Based Filters
        if (AppConfig.s_exeNameBlocklist.Contains(executableName, StringComparer.OrdinalIgnoreCase))
            return false;

        //foreach (var token in AppConfig.s_exeNameTokenBlocklist)
        //{
        //    if (executableName.Contains(token, StringComparison.OrdinalIgnoreCase))
        //        return false;
        //}

        if (!executablePath.Contains("steamapps", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var token in AppConfig.s_pathTokenBlocklist)
            {
                if (executablePath.Contains(token, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        // Enforce Minimum File Size
        try
        {
            // Allow Small Known Unity / Indie Exes
            var fileInfo = new FileInfo(executablePath);
            if (fileInfo.Length < AppConfig.MinGameExeBytes)
            {
                var name = Path.GetFileNameWithoutExtension(executablePath);

                if (
                    !name.Contains("unity", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("cuphead", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("among", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return false;
                }
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<string> DiscoverSteamGameRoots()
    {
        // Find Steam Installation
        var steamRoot = FindSteamInstallPath();
        if (steamRoot is null)
            yield break;

        // Locate Library Folders VDF Config
        var libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
            yield break;

        // Enumerate Library Folders and Yield Game Roots
        foreach (var libraryRoot in ParseSteamLibraryFolders(libraryFoldersPath))
        {
            var steamAppsDirectory = Path.Combine(libraryRoot, "steamapps");
            if (!Directory.Exists(steamAppsDirectory))
            {
                continue;
            }

            foreach (
                var manifestPath in SafeFs.EnumerateFiles(steamAppsDirectory, "appmanifest_*.acf")
            )
            {
                var installDirectoryName = TryParseSteamAppManifestInstallDir(manifestPath);
                if (installDirectoryName is null)
                {
                    continue;
                }

                var gameRoot = Path.Combine(steamAppsDirectory, "common", installDirectoryName);
                if (Directory.Exists(gameRoot))
                {
                    yield return gameRoot;
                }
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
        // Read the VDF File
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

        // Yield Additional Library Paths From the VDF
        foreach (Match match in InstalledPathRegex().Matches(fileText))
        {
            var rawPath = match.Groups["p"].Value;
            var normalizedPath = rawPath.Replace(@"\\", @"\");
            if (Directory.Exists(normalizedPath))
            {
                yield return normalizedPath;
            }
        }
    }

    private static string? TryParseSteamAppManifestInstallDir(string manifestPath)
    {
        try
        {
            var manifestText = File.ReadAllText(manifestPath);
            var match = InstalledDirectoryRegex().Match(manifestText);
            return match.Success ? match.Groups["d"].Value : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> DiscoverEpicGameRoots()
    {
        // Locate Epic Manifests Directory
        var manifestsDirectory = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        if (!Directory.Exists(manifestsDirectory))
            yield break;

        // Parse Each .item Manifest for Install Location
        foreach (var itemFilePath in SafeFs.EnumerateFiles(manifestsDirectory, "*.item"))
        {
            var installRoot = TryParseEpicInstallLocation(itemFilePath);
            if (installRoot is null)
            {
                continue;
            }

            if (Directory.Exists(installRoot))
            {
                yield return installRoot;
            }
        }
    }

    private static string? TryParseEpicInstallLocation(string itemFilePath)
    {
        try
        {
            // Parse the .item JSON File and Extract InstallLocation
            using var jsonDocument = JsonDocument.Parse(File.ReadAllText(itemFilePath));
            if (!jsonDocument.RootElement.TryGetProperty("InstallLocation", out var propertyValue))
                return null;

            if (propertyValue.ValueKind != JsonValueKind.String)
                return null;

            return propertyValue.GetString();
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> DiscoverRiotGameRoots()
    {
        // Resolve Riot Games Root
        var riotGamesRoot = TryFindRiotGamesRoot();
        if (riotGamesRoot is null)
            yield break;

        // Yield Each Game Sub-Dir, Skip Launcher
        foreach (var childDirectory in SafeFs.EnumerateDirectories(riotGamesRoot))
        {
            if (TextMatchers.ContainsAnyToken(childDirectory, AppConfig.s_pathTokenBlocklist))
            {
                continue;
            }

            yield return childDirectory;
        }
    }

    private static string? TryFindRiotGamesRoot()
    {
        // Try JSON First
        var installsFilePath = @"C:\ProgramData\Riot Games\RiotClientInstalls.json";
        if (File.Exists(installsFilePath))
        {
            var rootFromJson = TryParseRiotGamesRootFromJson(installsFilePath);
            if (rootFromJson is not null)
                return rootFromJson;
        }

        // Fall Back to Known Locations
        string[] candidatePaths =
        [
            @"C:\Riot Games",
            @"C:\Program Files\Riot Games",
            @"C:\Program Files (x86)\Riot Games",
        ];

        return candidatePaths.FirstOrDefault(Directory.Exists);
    }

    private static string? TryParseRiotGamesRootFromJson(string installsFilePath)
    {
        try
        {
            using var jsonDocument = JsonDocument.Parse(File.ReadAllText(installsFilePath));
            foreach (var property in jsonDocument.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var executablePath = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    continue;
                }

                var clientDirectory = Path.GetDirectoryName(executablePath);
                if (string.IsNullOrWhiteSpace(clientDirectory))
                {
                    continue;
                }

                // Walk Up One Level to Get the Riot Games Root
                var riotGamesRoot = Path.GetDirectoryName(clientDirectory);
                if (!string.IsNullOrWhiteSpace(riotGamesRoot) && Directory.Exists(riotGamesRoot))
                    return riotGamesRoot;
            }
        }
        catch { }

        return null;
    }

    private static IEnumerable<string> DiscoverRockstarGameRoots()
    {
        var candidateDirectories = new[]
        {
            @"C:\Program Files\Rockstar Games",
            @"C:\Program Files (x86)\Rockstar Games",
        };

        // Check Each Candidate Rockstar Installation Directory
        foreach (var baseDirectory in candidateDirectories)
        {
            if (!Directory.Exists(baseDirectory))
            {
                continue;
            }

            // Yield Each Game Subdirectory
            foreach (var childDirectory in SafeFs.EnumerateDirectories(baseDirectory))
            {
                yield return childDirectory;
            }
        }
    }

    private static IEnumerable<string> DiscoverUninstallRegistryGameRoots()
    {
        // Both 64-Bit and 32-Bit Uninstall Registry Keys
        string[] keyPaths =
        [
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        ];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var keyPath in keyPaths)
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(keyPath);
            if (baseKey is null)
            {
                continue;
            }

            foreach (var subKeyName in baseKey.GetSubKeyNames())
            {
                string installLocation;
                try
                {
                    using var subKey = baseKey.OpenSubKey(subKeyName);
                    if (subKey is null)
                    {
                        continue;
                    }

                    installLocation = (
                        (subKey.GetValue("InstallLocation") as string) ?? string.Empty
                    ).TrimEnd('\\', '/');

                    if (string.IsNullOrWhiteSpace(installLocation))
                    {
                        continue;
                    }

                    var publisher = (subKey.GetValue("Publisher") as string) ?? string.Empty;
                    if (
                        TextMatchers.ContainsAnyToken(
                            publisher,
                            AppConfig.s_uninstallPublisherBlocklist
                        )
                    )
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (!Directory.Exists(installLocation))
                {
                    continue;
                }

                if (
                    TextMatchers.ContainsAnyToken(
                        installLocation,
                        AppConfig.s_uninstallPathPrefixBlocklist
                    )
                )
                {
                    continue;
                }

                if (!seen.Add(installLocation))
                {
                    continue;
                }

                yield return installLocation;
            }
        }
    }

    #region Regexes
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
