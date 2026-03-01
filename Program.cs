using System.Runtime.Versioning;
using RestartAMDAdrenalin.Amd;
using RestartAMDAdrenalin.Configuration;
using RestartAMDAdrenalin.Game;

namespace RestartAMDAdrenalin;

[SupportedOSPlatform("windows")]
internal static class Program
{
    // Reset State Tracking
    private static long s_lastResetUtcTicks;
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
            if (!AmdReset.TryRelaunchElevated())
            {
                Console.WriteLine("Admin Rights Required");
                Console.WriteLine("Press Any Key");
                Console.ReadKey(true);
            }
            return;
        }

        Console.WriteLine("AMD Adrenalin Auto Reset");
        Console.WriteLine("------------------------");

        // Scan Installed Games
        var gameProcessNameToDisplayName = GameScanner.ScanInstalledGameProcessNames();
        var gameProcessNames = new HashSet<string>(
            gameProcessNameToDisplayName.Keys,
            StringComparer.OrdinalIgnoreCase
        );

        // Print Discovered Games
        Console.WriteLine($"Games Found: {gameProcessNames.Count}");

        foreach (
            var processName in gameProcessNames.OrderBy(
                name => name,
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            var displayName = gameProcessNameToDisplayName.TryGetValue(
                processName,
                out var nameValue
            )
                ? nameValue
                : processName;

            if (displayName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"- {displayName}");
            }
            else
            {
                Console.WriteLine($"- {displayName} ({processName})");
            }
        }

        // Exit if no Games are Found
        if (gameProcessNames.Count == 0)
        {
            Console.WriteLine("No Games Found");
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

        Console.WriteLine("Watching For Games");

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
                {
                    continue;
                }

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

        Console.WriteLine("Shutdown Requested");
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
                    {
                        return true;
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

        return false;
    }

    private static async Task TryTriggerResetAsync(
        HashSet<string> gameProcessNames,
        string startedDisplayName,
        CancellationToken cancellationToken
    )
    {
        // Skip if Within Debounce Window
        var nowUtc = DateTime.UtcNow;
        var lastTicks = Interlocked.Read(ref s_lastResetUtcTicks);
        var lastUtc =
            lastTicks == 0 ? DateTime.MinValue : new DateTime(lastTicks, DateTimeKind.Utc);

        if (nowUtc - lastUtc < AppConfig.s_resetDebounce)
        {
            return;
        }

        // Skip if a Reset is Already Pending
        if (Interlocked.Exchange(ref s_pendingResetFlag, 1) == 1)
        {
            return;
        }

        Console.WriteLine($"Game Detected: {startedDisplayName}");
        Console.WriteLine($"Reset In {AppConfig.s_gameStartDelay.TotalSeconds:0} Seconds");

        // Wait for Configured Delay
        try
        {
            await Task.Delay(AppConfig.s_gameStartDelay, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Interlocked.Exchange(ref s_pendingResetFlag, 0);
            return;
        }

        // Abort if Game Closed During Delay
        if (!IsAnyTrackedGameRunning(gameProcessNames))
        {
            Console.WriteLine("Game Closed");
            Interlocked.Exchange(ref s_pendingResetFlag, 0);
            return;
        }

        // Execute Reset and Record Timestamp
        AmdReset.ExecuteReset();
        Interlocked.Exchange(ref s_lastResetUtcTicks, DateTime.UtcNow.Ticks);
        Console.WriteLine("Reset Done");
        Interlocked.Exchange(ref s_pendingResetFlag, 0);
    }
}
