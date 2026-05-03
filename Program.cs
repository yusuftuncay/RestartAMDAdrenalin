using System.Runtime.Versioning;
using RestartAMDAdrenalin.Amd;
using RestartAMDAdrenalin.Configuration;
using RestartAMDAdrenalin.Game;
using static RestartAMDAdrenalin.Utilities.Logger;

namespace RestartAMDAdrenalin;

[SupportedOSPlatform("windows")]
internal static partial class Program
{
    // Pending Reset Flag
    private static int s_pendingResetFlag;

    #region Entry Point
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

        LogList($"Games Found: {uniqueDisplayNames.Count}", uniqueDisplayNames, ConsoleColor.Cyan);

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
            Log("Shutdown Requested", ConsoleColor.DarkGray);
            Environment.Exit(0);
        };

        Log("Watching for Games or Type \"Reset\" to Force Reset", ConsoleColor.Gray);

        // Background Key Listener for Manual Reset
        _ = WatchForManualTriggerAsync(gameProcessNames, cancellationTokenSource.Token);

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

                _ = TryTriggerResetAsync(gameProcessNames, startedDisplayName, isManual: false);
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
    #endregion

    #region Methods
    private static async Task WatchForManualTriggerAsync(
        HashSet<string> gameProcessNames,
        CancellationToken cancellationToken
    )
    {
        var inputBuffer = new System.Text.StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Accumulate Typed Characters and Trigger on "reset" + Enter
                while (Console.KeyAvailable)
                {
                    var keyInfo = Console.ReadKey(intercept: true);

                    if (keyInfo.Key == ConsoleKey.Enter)
                    {
                        var typed = inputBuffer.ToString();
                        inputBuffer.Clear();

                        // Clear the Typed Input Line
                        var clearLength = typed.Length;
                        Console.Write('\r');
                        Console.Write(new string(' ', clearLength));
                        Console.Write('\r');

                        if (typed.Equals("reset", StringComparison.OrdinalIgnoreCase))
                        {
                            _ = TryTriggerResetAsync(
                                gameProcessNames,
                                "Manual Reset",
                                isManual: true
                            );
                        }
                    }
                    else if (keyInfo.Key == ConsoleKey.Backspace)
                    {
                        if (inputBuffer.Length > 0)
                        {
                            inputBuffer.Remove(inputBuffer.Length - 1, 1);
                            // Erase Last Character on Screen
                            Console.Write("\b \b");
                        }
                    }
                    else if (!char.IsControl(keyInfo.KeyChar))
                    {
                        inputBuffer.Append(keyInfo.KeyChar);
                        // Echo the Typed Character
                        Console.Write(keyInfo.KeyChar);
                    }
                }

                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                break;
            }
        }
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
        bool isManual
    )
    {
        // Skip if a Reset is Already Pending
        if (Interlocked.Exchange(ref s_pendingResetFlag, 1) == 1)
            return;

        try
        {
            Log($"Game Detected: {startedDisplayName}", ConsoleColor.Yellow);

            if (!isManual)
            {
                // Wait for Configured Delay
                await Task.Delay(AppConfig.s_gameStartDelay, CancellationToken.None)
                    .ConfigureAwait(false);

                // Abort if Game Closed During Delay
                if (!IsAnyTrackedGameRunning(gameProcessNames))
                {
                    Log("Game Closed Before Reset", ConsoleColor.DarkYellow);
                    Log("Watching for Games or Type \"Reset\" to Force Reset", ConsoleColor.Gray);
                    return;
                }
            }

            // Execute Reset
            AmdReset.ExecuteReset();
            Log("Reset Done", ConsoleColor.Green);
            Log("Watching for Games or Type \"Reset\" to Force Reset", ConsoleColor.Gray);
        }
        finally
        {
            Interlocked.Exchange(ref s_pendingResetFlag, 0);
        }
    }
    #endregion
}
