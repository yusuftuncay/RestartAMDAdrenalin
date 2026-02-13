using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using System.Text.RegularExpressions;
using RestartAMDAdrenalin.Models;

namespace RestartAMDAdrenalin;

[SupportedOSPlatform("windows")]
internal static partial class Program
{
    #region Configuration
    // AMD
    private static readonly string[] s_adrenalinExecutablePaths =
    [
        @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe",
        @"C:\Program Files\AMD\CNext\CNext\RadeonSettings.exe",
    ];

    private static readonly string[] s_amdServiceNameMarkers = ["AMD", "Radeon"];
    private static readonly string[] s_amdProcessNameMarkers = ["AMD", "Radeon"];

    private static readonly string[] s_amdProcessNameWhitelist =
    [
        "RadeonSoftware",
        "RadeonSettings",
        "RadeonSettingsCore",
        "AMDRSServ",
        "AMDRSSrcExt",
        "Overlay",
        "AMDRadeonSoftware",
        "cncmd",
        "atieclxx",
        "atiesrxx",
        "amdfendrsr",
    ];

    // Game Detect
    private static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan s_gameStartDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan s_resetDebounce = TimeSpan.FromMinutes(5);

    // Game Filter
    private const long MinGameExeBytes = 15L * 1024L * 1024L; // 15 MB

    private static readonly string[] s_exeNameBlocklist =
    [
        // Launchers Helpers
        "steam",
        "steamwebhelper",
        "epicgameslauncher",
        "epicwebhelper",
        "rockstarservice",
        "launcher",
        "socialclubhelper",
        "riotclientservices",
        "riotclientux",
        "riotclientcrashhandler",
        // Anti Cheat
        "easyanticheat",
        "eac_launcher",
        "battleye",
        "beservice",
        // Common Installers
        "dxsetup",
        "vcredist",
        "vc_redist",
        "unins000",
    ];

    private static readonly string[] s_exeNameTokenBlocklist =
    [
        // Name Tokens
        "uninstall",
        "unins",
        "setup",
        "installer",
        "install",
        "update",
        "updater",
        "patch",
        "helper",
        "launcher",
        "service",
        "crash",
        "crashhandler",
        "reporter",
        "overlay",
        "redistributable",
        "redist",
        "prereq",
        "prerequisite",
        "cef",
        "webhelper",
    ];

    private static readonly string[] s_pathTokenBlocklist =
    [
        // Path Tokens
        "_commonredist",
        "commonredist",
        "redistributable",
        "redistributables",
        "redist",
        "installers",
        "installer",
        "setup",
        "uninstall",
        "support",
        "directx",
        "vcredist",
        "prereq",
        "prerequisite",
        "easyanticheat",
        "battleye",
        "punkbuster",
        "anticheat",
        "crash",
        "crashreport",
        "crashreporter",
        "launcher",
    ];

    // Console Hide
    private const int SwHide = 0;

    // Window Messages
    private const uint WindowMessageClose = 0x0010;
    #endregion

    #region Entry Point
    private static void Main(string[] args)
    {
        // Ensure Windows Platform
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows Only Tool");
            Console.Write("Press Any Key..");
            Console.ReadKey(true);
            return;
        }

        // Handle Reset Mode
        if (args.Length == 1 && args[0].Equals("--reset", StringComparison.OrdinalIgnoreCase))
        {
            RunResetFlowOnceElevated();
            return;
        }

        // Hide Console Window
        TryHideConsoleWindow();

        // Print Header
        Console.WriteLine("AMD Adrenalin Auto Reset");
        Console.WriteLine("-----------------------");

        // Scan Games
        var gameProcessNames = ScanInstalledGameProcessNames();
        Console.WriteLine($"Games Found: {gameProcessNames.Count}");

        // Print Game List
        foreach (
            var processName in gameProcessNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
        )
        {
            Console.WriteLine($"- {processName}");
        }

        // Run Watch Loop
        RunWatchLoop(gameProcessNames);
    }
    #endregion

    #region Watch Loop
    private static void RunWatchLoop(HashSet<string> gameProcessNames)
    {
        // Validate Games
        if (gameProcessNames.Count == 0)
        {
            Console.WriteLine("No Games Found");
            Console.WriteLine("Press Any Key..");
            Console.ReadKey(true);
            return;
        }

        // Setup Shutdown
        using var shutdownEvent = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) =>
        {
            // Handle Ctrl C
            e.Cancel = true;
            shutdownEvent.Set();
        };

        // Setup State
        var lastResetUtcTicks = new AtomicInt64(0);
        var pendingReset = new AtomicInt32(0);
        var previouslyRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Console.WriteLine("Watching For Games..");

        // Poll Loop
        while (!shutdownEvent.IsSet)
        {
            // Read Running Games
            var currentlyRunning = GetRunningGameProcesses(gameProcessNames);

            // Detect New Starts
            foreach (var started in currentlyRunning)
            {
                if (previouslyRunning.Contains(started))
                {
                    continue;
                }

                TryScheduleReset(started, lastResetUtcTicks, pendingReset);
            }

            // Swap Snapshot
            previouslyRunning = currentlyRunning;

            // Wait Interval
            shutdownEvent.Wait(s_pollInterval);
        }
    }

    private static HashSet<string> GetRunningGameProcesses(HashSet<string> gameProcessNames)
    {
        // Scan Running Processes
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var name = p.ProcessName;
                    if (gameProcessNames.Contains(name))
                    {
                        result.Add(name);
                    }
                }
                catch
                {
                    // Ignore Process Errors
                }
                finally
                {
                    try
                    {
                        p.Dispose();
                    }
                    catch
                    {
                        // Ignore Dispose Errors
                    }
                }
            }
        }
        catch
        {
            // Ignore Enumerate Errors
        }

        return result;
    }

    private static void TryScheduleReset(
        string processName,
        AtomicInt64 lastResetUtcTicks,
        AtomicInt32 pendingReset
    )
    {
        // Apply Debounce
        var nowUtc = DateTime.UtcNow;
        var lastTicks = lastResetUtcTicks.Read();
        var lastUtc =
            lastTicks == 0 ? DateTime.MinValue : new DateTime(lastTicks, DateTimeKind.Utc);

        if (nowUtc - lastUtc < s_resetDebounce)
        {
            return;
        }

        // Prevent Duplicate Schedule
        if (pendingReset.Exchange(1) == 1)
        {
            return;
        }

        Console.WriteLine($"Game Detected: {processName}");
        Console.WriteLine($"Reset In: {s_gameStartDelay.TotalMinutes:0} Minute(s)");

        // Schedule Task
        _ = Task.Run(async () =>
        {
            // Delay Start
            await Task.Delay(s_gameStartDelay).ConfigureAwait(false);

            // Confirm Still Running
            if (!IsProcessRunning(processName))
            {
                pendingReset.Exchange(0);
                return;
            }

            // Run Elevated Reset
            if (TryLaunchElevatedReset())
            {
                lastResetUtcTicks.Write(DateTime.UtcNow.Ticks);
            }

            // Clear Pending
            pendingReset.Exchange(0);
        });
    }

    private static bool IsProcessRunning(string processName)
    {
        // Check Process Running
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }
    #endregion

    #region Elevated Reset
    private static bool TryLaunchElevatedReset()
    {
        // Launch Elevated Child
        try
        {
            var currentProcessPath =
                Environment.ProcessPath
                ?? throw new InvalidOperationException("Missing Process Path");

            var startInfo = new ProcessStartInfo
            {
                FileName = currentProcessPath,
                Arguments = "--reset",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            _ = Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            // Handle Uac Cancel
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static void RunResetFlowOnceElevated()
    {
        // Ensure Administrator Rights
        if (!IsCurrentProcessAdministrator())
        {
            return;
        }

        // Stop AMD Processes
        StopAmdRelatedProcesses();

        // Stop AMD Services
        StopAllAmdRelatedServices();

        // Wait Briefly
        Thread.Sleep(800);

        // Start Adrenalin Normally
        StartAdrenalinNormal();

        // Close UI To Tray
        CloseAdrenalinMainWindowToTray();
    }

    private static bool IsCurrentProcessAdministrator()
    {
        // Check Administrator Token
        using var windowsIdentity = WindowsIdentity.GetCurrent();
        var windowsPrincipal = new WindowsPrincipal(windowsIdentity);
        return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
    }
    #endregion

    #region Game Scan
    private static HashSet<string> ScanInstalledGameProcessNames()
    {
        // Collect Process Names
        var processNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Scan Steam
        foreach (var root in DiscoverSteamGameRoots())
        {
            AddExeBasenamesFromGameRoot(processNames, root);
        }

        // Scan Epic
        foreach (var root in DiscoverEpicGameRoots())
        {
            AddExeBasenamesFromGameRoot(processNames, root);
        }

        // Scan Riot
        foreach (var root in DiscoverRiotGameRoots())
        {
            AddExeBasenamesFromGameRoot(processNames, root);
        }

        // Scan Rockstar
        foreach (var root in DiscoverRockstarGameRoots())
        {
            AddExeBasenamesFromGameRoot(processNames, root);
        }

        return processNames;
    }

    private static void AddExeBasenamesFromGameRoot(HashSet<string> output, string root)
    {
        // Validate Root
        if (string.IsNullOrWhiteSpace(root))
        {
            return;
        }

        if (!Directory.Exists(root))
        {
            return;
        }

        // Limit Search Dirs
        var candidateDirs = new[]
        {
            root,
            Path.Combine(root, "bin"),
            Path.Combine(root, "Binaries"),
            Path.Combine(root, "Binaries", "Win64"),
            Path.Combine(root, "Win64"),
            Path.Combine(root, "x64"),
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in candidateDirs)
        {
            // Validate Dir
            if (!Directory.Exists(dir))
            {
                continue;
            }

            // Enumerate Exes
            foreach (var exePath in SafeEnumerateFiles(dir, "*.exe"))
            {
                if (!IsLikelyGameExe(exePath))
                {
                    continue;
                }

                var name = Path.GetFileNameWithoutExtension(exePath);
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                output.Add(name);
            }
        }
    }

    private static bool IsLikelyGameExe(string exePath)
    {
        // Validate Path
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return false;
        }

        // Validate Exists
        if (!File.Exists(exePath))
        {
            return false;
        }

        // Check Name
        var name = Path.GetFileNameWithoutExtension(exePath);
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        // Apply Exact Blocklist
        if (s_exeNameBlocklist.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        // Apply Name Tokens
        foreach (var token in s_exeNameTokenBlocklist)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Apply Path Tokens
        foreach (var token in s_pathTokenBlocklist)
        {
            if (exePath.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Apply Size Filter
        try
        {
            var info = new FileInfo(exePath);
            if (info.Length < MinGameExeBytes)
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
    {
        // Enumerate Files Safely
        try
        {
            return Directory.EnumerateFiles(dir, pattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string dir)
    {
        // Enumerate Dirs Safely
        try
        {
            return Directory.EnumerateDirectories(dir, "*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return [];
        }
    }
    #endregion

    #region Steam Discovery
    private static IEnumerable<string> DiscoverSteamGameRoots()
    {
        // Find Steam Root
        var steamRoot = FindSteamInstallPath();
        if (steamRoot is null)
        {
            yield break;
        }

        var libraryFoldersPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraryFoldersPath))
        {
            yield break;
        }

        // Parse Libraries
        foreach (var lib in ParseSteamLibraryFolders(libraryFoldersPath))
        {
            var steamapps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(steamapps))
            {
                continue;
            }

            // Parse Manifests
            foreach (var manifestPath in SafeEnumerateFiles(steamapps, "appmanifest_*.acf"))
            {
                var installdir = TryParseSteamAppManifestInstallDir(manifestPath);
                if (installdir is null)
                {
                    continue;
                }

                var gameRoot = Path.Combine(steamapps, "common", installdir);
                if (Directory.Exists(gameRoot))
                {
                    yield return gameRoot;
                }
            }
        }
    }

    private static string? FindSteamInstallPath()
    {
        // Check Common Paths
        var candidates = new[] { @"C:\Program Files (x86)\Steam", @"C:\Program Files\Steam" };

        return candidates.FirstOrDefault(Directory.Exists);
    }

    private static IEnumerable<string> ParseSteamLibraryFolders(string path)
    {
        // Read Vdf Text
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch
        {
            yield break;
        }

        // Include Main Root
        var steamRoot = Path.GetDirectoryName(Path.GetDirectoryName(path))!;
        yield return steamRoot;

        // Extract Paths
        foreach (Match m in InstalledPathRegex().Matches(text))
        {
            var raw = m.Groups["p"].Value;
            var normalized = raw.Replace(@"\\", @"\");
            if (Directory.Exists(normalized))
            {
                yield return normalized;
            }
        }
    }

    private static string? TryParseSteamAppManifestInstallDir(string manifestPath)
    {
        // Read Manifest
        try
        {
            var text = File.ReadAllText(manifestPath);
            var m = InstalledDirectoryRegex().Match(text);
            return m.Success ? m.Groups["d"].Value : null;
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Epic Discovery
    private static IEnumerable<string> DiscoverEpicGameRoots()
    {
        // Locate Manifests
        var manifestsDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";
        if (!Directory.Exists(manifestsDir))
        {
            yield break;
        }

        // Parse Item Files
        foreach (var itemFile in SafeEnumerateFiles(manifestsDir, "*.item"))
        {
            var root = TryParseEpicInstallLocation(itemFile);
            if (root is null)
            {
                continue;
            }

            if (Directory.Exists(root))
            {
                yield return root;
            }
        }
    }

    private static string? TryParseEpicInstallLocation(string itemFile)
    {
        // Parse Item Json
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(itemFile));
            if (!doc.RootElement.TryGetProperty("InstallLocation", out var p))
            {
                return null;
            }

            if (p.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return p.GetString();
        }
        catch
        {
            return null;
        }
    }
    #endregion

    #region Riot Discovery
    private static IEnumerable<string> DiscoverRiotGameRoots()
    {
        // Locate Installs
        var installsFile = @"C:\ProgramData\Riot Games\RiotClientInstalls.json";
        if (!File.Exists(installsFile))
        {
            // Try Default Root
            var defaultRoot = @"C:\Riot Games";
            if (Directory.Exists(defaultRoot))
            {
                yield return defaultRoot;
            }

            yield break;
        }

        // Parse Installs Json
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(installsFile));
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var exePath = prop.Value.GetString();
                if (string.IsNullOrWhiteSpace(exePath))
                {
                    continue;
                }

                var dir = Path.GetDirectoryName(exePath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                {
                    result.Add(dir!);
                }
            }
        }
        catch
        {
            // Ignore Errors
        }

        foreach (var dir in result)
        {
            yield return dir;
        }
    }
    #endregion

    #region Rockstar Discovery
    private static IEnumerable<string> DiscoverRockstarGameRoots()
    {
        // Check Common Paths
        var candidates = new[]
        {
            @"C:\Program Files\Rockstar Games",
            @"C:\Program Files (x86)\Rockstar Games",
        };

        foreach (var baseDir in candidates)
        {
            if (!Directory.Exists(baseDir))
            {
                continue;
            }

            foreach (var dir in SafeEnumerateDirectories(baseDir))
            {
                yield return dir;
            }
        }
    }
    #endregion

    #region Process Control
    private static void StopAmdRelatedProcesses()
    {
        // Stop Whitelisted Processes
        foreach (
            var processName in s_amdProcessNameWhitelist.Distinct(StringComparer.OrdinalIgnoreCase)
        )
        {
            TryKillProcessesByName(processName);
        }

        // Stop Marker Matched Processes
        foreach (var processInstance in SafeGetAllProcesses())
        {
            TryKillMarkerMatchedProcess(processInstance);
        }
    }

    private static void TryKillProcessesByName(string processName)
    {
        // Kill Named Processes
        try
        {
            foreach (var processInstance in Process.GetProcessesByName(processName))
            {
                TryKillProcess(processInstance);
            }
        }
        catch
        {
            // Ignore Process Errors
        }
    }

    private static Process[] SafeGetAllProcesses()
    {
        // Enumerate All Processes
        try
        {
            return Process.GetProcesses();
        }
        catch
        {
            return [];
        }
    }

    private static void TryKillMarkerMatchedProcess(Process processInstance)
    {
        // Match Process Markers
        try
        {
            var processName = processInstance.ProcessName;
            if (!ContainsAnyMarker(processName, s_amdProcessNameMarkers))
            {
                return;
            }

            if (s_amdProcessNameWhitelist.Contains(processName, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            TryKillProcess(processInstance);
        }
        catch
        {
            // Ignore Process Errors
        }
    }

    private static bool ContainsAnyMarker(string value, string[] markers)
    {
        // Check Marker Contains
        foreach (var marker in markers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void TryKillProcess(Process processInstance)
    {
        // Force Kill Process
        try
        {
            processInstance.Kill(entireProcessTree: true);
            processInstance.WaitForExit(1500);
        }
        catch
        {
            // Ignore Kill Errors
        }
    }
    #endregion

    #region Service Control
    private static void StopAllAmdRelatedServices()
    {
        // Enumerate All Services
        var serviceEntries = QueryAllServiceEntries();
        var amdServiceEntries = serviceEntries
            .Where(serviceEntry =>
                IsAmdRelatedService(serviceEntry.ServiceName, serviceEntry.DisplayName)
            )
            .ToList();

        // Stop Matching Services
        foreach (var (ServiceName, DisplayName) in amdServiceEntries)
        {
            TryStopServiceUsingSc(ServiceName);
        }
    }

    private static bool IsAmdRelatedService(string serviceName, string displayName)
    {
        // Match AMD Markers
        foreach (var marker in s_amdServiceNameMarkers)
        {
            if (serviceName.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (displayName.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void TryStopServiceUsingSc(string serviceName)
    {
        // Stop Service Using Sc
        var exitCode = RunScCommand($"stop \"{serviceName}\"");
        if (exitCode == 0)
        {
            WaitForServiceState(
                serviceName,
                expectedState: "STOPPED",
                timeout: TimeSpan.FromSeconds(6)
            );
        }
    }

    private static List<(string ServiceName, string DisplayName)> QueryAllServiceEntries()
    {
        // Query All Services
        var outputText = RunCommandCaptureOutput("sc.exe", "query state= all");
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return [];
        }

        // Parse Service Blocks
        var lines = outputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var results = new List<(string ServiceName, string DisplayName)>();

        var currentServiceName = string.Empty;
        var currentDisplayName = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
            {
                // Commit Previous Entry
                if (!string.IsNullOrWhiteSpace(currentServiceName))
                {
                    results.Add((currentServiceName, currentDisplayName));
                }

                currentServiceName = line["SERVICE_NAME:".Length..].Trim();
                currentDisplayName = string.Empty;
                continue;
            }

            if (line.StartsWith("DISPLAY_NAME:", StringComparison.OrdinalIgnoreCase))
            {
                currentDisplayName = line["DISPLAY_NAME:".Length..].Trim();
                continue;
            }
        }

        // Commit Final Entry
        if (!string.IsNullOrWhiteSpace(currentServiceName))
        {
            results.Add((currentServiceName, currentDisplayName));
        }

        return results;
    }

    private static int RunScCommand(string arguments)
    {
        // Execute Sc Command
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var processInstance = Process.Start(startInfo);
            if (processInstance is null)
            {
                return -1;
            }

            processInstance.WaitForExit(8000);
            return processInstance.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    private static void WaitForServiceState(
        string serviceName,
        string expectedState,
        TimeSpan timeout
    )
    {
        // Poll Service State
        var deadlineUtc = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var currentState = QueryServiceState(serviceName);
            if (
                currentState is not null
                && currentState.Equals(expectedState, StringComparison.OrdinalIgnoreCase)
            )
            {
                return;
            }

            Thread.Sleep(250);
        }
    }

    private static string? QueryServiceState(string serviceName)
    {
        // Query Service Using Sc
        var outputText = RunCommandCaptureOutput("sc.exe", $"query \"{serviceName}\"");
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return null;
        }

        var stateIndex = outputText.IndexOf("STATE", StringComparison.OrdinalIgnoreCase);
        if (stateIndex < 0)
        {
            return null;
        }

        var stateBlock = outputText[stateIndex..];

        if (stateBlock.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
        {
            return "RUNNING";
        }

        if (stateBlock.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
        {
            return "STOPPED";
        }

        return null;
    }

    private static string RunCommandCaptureOutput(string fileName, string arguments)
    {
        // Capture Command Output
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using var processInstance = Process.Start(startInfo);
            if (processInstance is null)
            {
                return string.Empty;
            }

            var standardOutput = processInstance.StandardOutput.ReadToEnd();
            var standardError = processInstance.StandardError.ReadToEnd();

            processInstance.WaitForExit(8000);

            if (!string.IsNullOrWhiteSpace(standardOutput))
            {
                return standardOutput;
            }

            return standardError;
        }
        catch
        {
            return string.Empty;
        }
    }
    #endregion

    #region Adrenalin Control
    private static void StartAdrenalinNormal()
    {
        // Locate Adrenalin Executable
        var executablePath = s_adrenalinExecutablePaths.FirstOrDefault(File.Exists);
        if (executablePath is null)
        {
            return;
        }

        // Start Adrenalin Process
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
            };

            Process.Start(startInfo);
        }
        catch
        {
            // Ignore Launch Errors
        }
    }

    private static void CloseAdrenalinMainWindowToTray()
    {
        // Wait For UI Window
        var deadlineUtc = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var adrenalinProcesses = GetAdrenalinCandidateProcesses();
            foreach (var adrenalinProcess in adrenalinProcesses)
            {
                if (TryCloseMainWindow(adrenalinProcess))
                {
                    return;
                }
            }

            Thread.Sleep(250);
        }
    }

    private static Process[] GetAdrenalinCandidateProcesses()
    {
        // Find Adrenalin Processes
        try
        {
            var radeonSoftwareProcesses = Process.GetProcessesByName("RadeonSoftware");
            if (radeonSoftwareProcesses.Length != 0)
            {
                return radeonSoftwareProcesses;
            }

            var radeonSettingsProcesses = Process.GetProcessesByName("RadeonSettings");
            if (radeonSettingsProcesses.Length != 0)
            {
                return radeonSettingsProcesses;
            }

            return [];
        }
        catch
        {
            return [];
        }
    }

    private static bool TryCloseMainWindow(Process processInstance)
    {
        // Close Main Window Message
        try
        {
            processInstance.Refresh();
            var mainWindowHandle = processInstance.MainWindowHandle;
            if (mainWindowHandle == IntPtr.Zero)
            {
                return false;
            }

            _ = PostMessage(mainWindowHandle, WindowMessageClose, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }
    #endregion

    #region Console Helpers
    private static void TryHideConsoleWindow()
    {
        // Hide Console Window
        try
        {
            var handle = GetConsoleWindow();
            if (handle == IntPtr.Zero)
            {
                return;
            }

            _ = ShowWindow(handle, SwHide);
        }
        catch
        {
            // Ignore Hide Errors
        }
    }
    #endregion

    #region Native Methods
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [GeneratedRegex("\"installdir\"\\s*\"(?<d>[^\"]+)\"", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex InstalledDirectoryRegex();

    [GeneratedRegex("\"path\"\\s*\"(?<p>[^\"]+)\"", RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex InstalledPathRegex();
    #endregion
}
