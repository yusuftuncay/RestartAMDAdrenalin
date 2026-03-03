using System.Runtime.Versioning;
using RestartAMDAdrenalin.Amd;
using RestartAMDAdrenalin.Configuration;
using RestartAMDAdrenalin.Game;
using static RestartAMDAdrenalin.Utilities.Logger;

namespace RestartAMDAdrenalin;

[SupportedOSPlatform("windows")]
internal static partial class Program
{
    // Reset State Tracking
    private static int s_pendingResetFlag;

    private static async Task Main()
    {
        // Exit if not Running on Windows
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows Only Tool");
            Console.WriteLine("Press Any Key");
            Console.ReadKey(true);
            return;
        }

        // Require Admin Rights at Startup
        if (!AmdReset.IsAdministrator())
        {
            if (AmdReset.TryRelaunchElevated())
            {
                Environment.Exit(0);
            }
            else
            {
                Console.WriteLine("Admin Rights Required");
                Console.WriteLine("Press Any Key");
                Console.ReadKey(true);
            }

            return;
        }

        LogHeader("AMD Adrenalin Auto Reset");

        // Scan Installed Games
        var gameProcessNameToDisplayName = GameScanner.ScanInstalledGameProcessNames();
        var gameProcessNames = new HashSet<string>(
            gameProcessNameToDisplayName.Keys,
            StringComparer.OrdinalIgnoreCase
        );

        // Print Discovered Games (Deduplicated by Display Name)
        var uniqueDisplayNames = gameProcessNameToDisplayName
            .Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        LogList($"Games Found: {uniqueDisplayNames.Count}", uniqueDisplayNames);

        // Exit if no Games are Found
        if (gameProcessNames.Count == 0)
        {
            Log("No Games Found", ConsoleColor.Red);
            Console.WriteLine("Press Any Key");
            Console.ReadKey(true);
            return;
        }

        // Set up Ctrl+C Cancellation
        using var cancellationTokenSource = new CancellationTokenSource();

        Console.CancelKeyPress += (_, cancelEventArgs) =>
        {
            cancelEventArgs.Cancel = true;
            cancellationTokenSource.Cancel();
        };

        Log("Watching for Games", ConsoleColor.Cyan);

        var previouslyRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!cancellationTokenSource.IsCancellationRequested)
        {
            // Detect Newly Started Games
            var currentlyRunning = GetRunningGameProcesses(gameProcessNames);

            var newGameStarted = false;
            string? startedProcessName = null;

            foreach (var runningProcessName in currentlyRunning)
            {
                if (previouslyRunning.Contains(runningProcessName))
                    continue;

                newGameStarted = true;
                startedProcessName = runningProcessName;
                break;
            }

            // Fire and Forget Reset Sequence on New Game Start
            if (newGameStarted)
            {
                var startedDisplayName =
                    startedProcessName is not null
                    && gameProcessNameToDisplayName.TryGetValue(
                        startedProcessName,
                        out var niceName
                    )
                        ? niceName
                        : (startedProcessName ?? "Game");

                _ = TryTriggerResetAsync(
                    gameProcessNames,
                    startedDisplayName,
                    cancellationTokenSource.Token
                );
            }

            // Advance the Tracking Snapshot
            previouslyRunning = currentlyRunning;

            // Wait for Next Poll Interval
            try
            {
                await Task.Delay(AppConfig.s_pollInterval, cancellationTokenSource.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                break;
            }
        }

        Log("Shutdown Requested", ConsoleColor.DarkGray);
    }

    private static HashSet<string> GetRunningGameProcesses(HashSet<string> gameProcessNames)
    {
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var processInstance in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    var processName = processInstance.ProcessName;
                    if (gameProcessNames.Contains(processName))
                    {
                        running.Add(processName);
                    }
                }
                catch { }
                finally
                {
                    try
                    {
                        processInstance.Dispose();
                    }
                    catch { }
                }
            }
        }
        catch { }

        return running;
    }

    private static bool IsAnyTrackedGameRunning(HashSet<string> gameProcessNames)
    {
        try
        {
            foreach (var processInstance in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    var processName = processInstance.ProcessName;
                    if (gameProcessNames.Contains(processName))
                        return true;
                }
                catch { }
                finally
                {
                    try
                    {
                        processInstance.Dispose();
                    }
                    catch { }
                }
            }
        }
        catch { }

        return false;
    }

    private static async Task TryTriggerResetAsync(
        HashSet<string> gameProcessNames,
        string startedDisplayName,
        CancellationToken cancellationToken
    )
    {
        // Skip if a Reset is Already Pending
        if (Interlocked.Exchange(ref s_pendingResetFlag, 1) == 1)
            return;

        try
        {
            Log($"Game Detected: {startedDisplayName}", ConsoleColor.Yellow);

            // Wait for Configured Delay (Not Cancellable — Reset Runs to Completion Once Triggered)
            await Task.Delay(AppConfig.s_gameStartDelay, cancellationToken).ConfigureAwait(false);

            // Abort if Game Closed During Delay
            if (!IsAnyTrackedGameRunning(gameProcessNames))
            {
                Log("Game Closed Before Reset", ConsoleColor.DarkYellow);
                Log("Watching for Games", ConsoleColor.Cyan);
                return;
            }

            // Execute Reset
            AmdReset.ExecuteReset();
            Log("Reset Done", ConsoleColor.Green);
            Log("Watching for Games", ConsoleColor.Cyan);
        }
        finally
        {
            Interlocked.Exchange(ref s_pendingResetFlag, 0);
        }
    }
}
