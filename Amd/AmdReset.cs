using System.ComponentModel;
using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using System.Security.Principal;
using AdrenalinRestart.Configuration;
using AdrenalinRestart.Native;
using AdrenalinRestart.Utilities;
using static AdrenalinRestart.Utilities.Logger;

namespace AdrenalinRestart.Amd;

[SupportedOSPlatform("windows")]
internal static class AmdReset
{
    #region Public Methods
    internal static void ExecuteReset()
    {
        Log("Stopping AMD Services", ConsoleColor.DarkYellow);
        var runningServiceNames = StopAmdServices();

        Log("Stopping AMD Processes", ConsoleColor.DarkYellow);
        StopAmdProcesses();

        Log("Starting AMD Services", ConsoleColor.DarkGreen);
        StartAmdServices(runningServiceNames);

        Log("Starting Adrenalin", ConsoleColor.DarkGreen);
        if (StartAdrenalin())
        {
            CloseAdrenalinWindow();
        }
    }

    internal static bool TryRelaunchElevated()
    {
        try
        {
            // Resolve the Current Executable Path
            var currentProcessPath =
                Environment.ProcessPath
                ?? throw new InvalidOperationException("Missing Process Path");

            // Launch the Current Program Elevated
            var startInfo = new ProcessStartInfo
            {
                FileName = currentProcessPath,
                UseShellExecute = true,
                Verb = "runas",
            };

            _ = Process.Start(startInfo);
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsAdministrator()
    {
        using var windowsIdentity = WindowsIdentity.GetCurrent();
        var windowsPrincipal = new WindowsPrincipal(windowsIdentity);
        return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
    }
    #endregion

    #region Private Methods
    private static void StopAmdProcesses()
    {
        // Kill by Name Keyword
        foreach (var processInstance in ProcessTools.SafeGetAllProcesses())
        {
            TryKillIfAmdByName(processInstance);
        }

        // Kill by Path Marker (Catches AMD Processes Without AMD/Radeon in Name)
        foreach (var processInstance in ProcessTools.SafeGetAllProcesses())
        {
            TryKillIfAmdByPath(processInstance);
        }

        WaitForAmdProcessesToExit();
    }

    private static void TryKillIfAmdByName(Process processInstance)
    {
        try
        {
            if (!ContainsAmdKeyword(processInstance.ProcessName))
                return;
            LogItem(
                $"{processInstance.ProcessName} (PID {processInstance.Id})",
                ConsoleColor.Yellow
            );
            ProcessTools.TryKill(processInstance, waitMs: 1500);
        }
        catch { }
    }

    private static void TryKillIfAmdByPath(Process processInstance)
    {
        try
        {
            if (ContainsAmdKeyword(processInstance.ProcessName))
                return;
            var path = ProcessTools.TryGetExecutablePath(processInstance);
            if (string.IsNullOrWhiteSpace(path))
                return;
            if (!TextMatchers.ContainsAnyMarker(path, AppConfig.s_amdExecutablePathMarkers))
                return;
            LogItem(
                $"{processInstance.ProcessName} (PID {processInstance.Id})",
                ConsoleColor.Yellow
            );
            ProcessTools.TryKill(processInstance, waitMs: 1500);
        }
        catch
        {
            try
            {
                processInstance.Dispose();
            }
            catch { }
        }
    }

    private static void WaitForAmdProcessesToExit()
    {
        // Poll Until All AMD Processes Have Exited, Re-Killing Any That Come Back
        var deadlineUtc = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var anyRunning = false;
            foreach (var processInstance in ProcessTools.SafeGetAllProcesses())
            {
                try
                {
                    var isAmdByName = ContainsAmdKeyword(processInstance.ProcessName);
                    var isAmdByPath =
                        !isAmdByName
                        && TextMatchers.ContainsAnyMarker(
                            ProcessTools.TryGetExecutablePath(processInstance) ?? string.Empty,
                            AppConfig.s_amdExecutablePathMarkers
                        );
                    if (isAmdByName || isAmdByPath)
                    {
                        anyRunning = true;
                        ProcessTools.TryKill(processInstance, waitMs: 200);
                    }
                    else
                    {
                        processInstance.Dispose();
                    }
                }
                catch { }
            }
            if (!anyRunning)
                return;
            Thread.Sleep(200);
        }
    }

    private static List<string> StopAmdServices()
    {
        // Discover Running AMD Services
        var runningNames = GetRunningAmdServiceNames();

        // Issue Stop for Each
        foreach (var name in runningNames)
        {
            CommandRunner.RunExitCode("sc.exe", $"stop \"{name}\"", timeoutMs: 8000);
            LogItem(name, ConsoleColor.Yellow);
        }

        // Wait Until All Have Stopped
        WaitForServicesToStop(runningNames);
        return runningNames;
    }

    private static List<string> GetRunningAmdServiceNames()
    {
        var result = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DisplayName, State FROM Win32_Service"
            );
            foreach (var service in searcher.Get().Cast<ManagementObject>())
            {
                try
                {
                    var name = service["Name"]?.ToString() ?? string.Empty;
                    var displayName = service["DisplayName"]?.ToString() ?? string.Empty;
                    var state = service["State"]?.ToString() ?? string.Empty;
                    if (
                        (ContainsAmdKeyword(name) || ContainsAmdKeyword(displayName))
                        && state.Equals("Running", StringComparison.OrdinalIgnoreCase)
                    )
                    {
                        result.Add(name);
                    }
                }
                catch { }
                finally
                {
                    try
                    {
                        service.Dispose();
                    }
                    catch { }
                }
            }
        }
        catch { }
        return result;
    }

    private static void WaitForServicesToStop(List<string> serviceNames)
    {
        if (serviceNames.Count == 0)
            return;
        var deadlineUtc = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var anyRunning = serviceNames.Any(name =>
                !string.Equals(
                    QueryServiceState(name),
                    "STOPPED",
                    StringComparison.OrdinalIgnoreCase
                )
            );
            if (!anyRunning)
                return;
            Thread.Sleep(250);
        }
    }

    private static void StartAmdServices(List<string> serviceNames)
    {
        foreach (var name in serviceNames)
        {
            var exitCode = CommandRunner.RunExitCode(
                "sc.exe",
                $"start \"{name}\"",
                timeoutMs: 8000
            );
            if (exitCode != 0)
            {
                continue;
            }
            LogItem(name, ConsoleColor.Green);
            WaitForServiceRunning(name, TimeSpan.FromSeconds(10));
        }
    }

    private static void WaitForServiceRunning(string serviceName, TimeSpan timeout)
    {
        // Poll Until Service is Running or Timeout Expires
        var deadlineUtc = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadlineUtc)
        {
            if (
                string.Equals(
                    QueryServiceState(serviceName),
                    "RUNNING",
                    StringComparison.OrdinalIgnoreCase
                )
            )
                return;
            Thread.Sleep(250);
        }
    }

    private static bool ContainsAmdKeyword(string text)
    {
        foreach (var keyword in AppConfig.s_amdKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string? QueryServiceState(string serviceName)
    {
        // Capture sc.exe Query Output
        var outputText = CommandRunner.CaptureOutput(
            "sc.exe",
            $"query \"{serviceName}\"",
            timeoutMs: 8000
        );
        if (string.IsNullOrWhiteSpace(outputText))
            return null;

        // Locate the STATE Block in the Output
        var stateIndex = outputText.IndexOf("STATE", StringComparison.OrdinalIgnoreCase);
        if (stateIndex < 0)
            return null;

        var stateBlock = outputText[stateIndex..];

        // Determine if the Service is Running or Stopped
        if (stateBlock.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            return "RUNNING";

        if (stateBlock.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            return "STOPPED";

        return null;
    }

    private static bool StartAdrenalin()
    {
        // Find the First Valid Adrenalin Executable
        var executablePath = AppConfig.s_adrenalinExecutablePaths.FirstOrDefault(File.Exists);
        if (executablePath is null)
        {
            Log("Adrenalin Not Found", ConsoleColor.Red);
            return false;
        }

        try
        {
            // Launch Adrenalin Hidden to Avoid Any Focus or Window Flash
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };

            Process.Start(startInfo);
        }
        catch
        {
            Log("Adrenalin Start Failed", ConsoleColor.Red);
            return false;
        }

        // Wait for Adrenalin Process to Appear
        var deadlineUtc = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var candidates = GetAdrenalinCandidateProcesses();
            if (candidates.Length > 0)
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        // Wait for the Process to Finish Initializing its Message Loop
                        candidate.WaitForInputIdle(5000);
                    }
                    catch { }
                    finally
                    {
                        try
                        {
                            candidate.Dispose();
                        }
                        catch { }
                    }
                }

                Log("Adrenalin Started", ConsoleColor.Green);
                return true;
            }

            Thread.Sleep(250);
        }

        Log("Adrenalin Start Timed Out", ConsoleColor.Red);
        return false;
    }

    private static void CloseAdrenalinWindow()
    {
        // Poll Until Main Window Handle Is Available
        var deadlineUtc = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadlineUtc)
        {
            foreach (var processInstance in GetAdrenalinCandidateProcesses())
            {
                try
                {
                    processInstance.Refresh();
                    var mainWindowHandle = processInstance.MainWindowHandle;
                    if (mainWindowHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    // WM_CLOSE on the Main Window Sends Adrenalin to the Tray
                    NativeMethods.PostMessage(
                        mainWindowHandle,
                        NativeMethods.WindowMessageClose,
                        IntPtr.Zero,
                        IntPtr.Zero
                    );
                    Log("Adrenalin Closed", ConsoleColor.Green);
                    return;
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

            Thread.Sleep(250);
        }
    }

    private static Process[] GetAdrenalinCandidateProcesses()
    {
        try
        {
            // Try RadeonSoftware First, Then RadeonSettings
            var radeonSoftware = Process.GetProcessesByName("RadeonSoftware");
            if (radeonSoftware.Length != 0)
            {
                return radeonSoftware;
            }

            var radeonSettings = Process.GetProcessesByName("RadeonSettings");
            if (radeonSettings.Length != 0)
            {
                return radeonSettings;
            }

            return [];
        }
        catch
        {
            return [];
        }
    }
    #endregion
}
