using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using RestartAMDAdrenalin.Configuration;
using RestartAMDAdrenalin.Native;
using RestartAMDAdrenalin.Utilities;
using static RestartAMDAdrenalin.Utilities.Logger;

namespace RestartAMDAdrenalin.Amd;

[SupportedOSPlatform("windows")]
internal static class AmdReset
{
    internal static void ExecuteReset()
    {
        Log("Stopping AMD Services", ConsoleColor.DarkYellow);
        StopAmdServices();

        Log("Stopping AMD Processes", ConsoleColor.DarkYellow);
        StopAmdProcesses();

        Thread.Sleep(1500);

        Log("Starting Adrenalin", ConsoleColor.DarkGreen);
        StartAdrenalin();

        Log("Starting AMD Services", ConsoleColor.DarkGreen);
        StartAmdServices();
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

    private static void StopAmdProcesses()
    {
        // Kill Known AMD Processes by Name
        foreach (
            var allowlistedName in AppConfig.s_amdProcessNameAllowlist.Distinct(
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            TryKillByName(allowlistedName);
        }

        // Kill any Remaining AMD-Backed Processes
        foreach (var processInstance in ProcessTools.SafeGetAllProcesses())
        {
            TryKillIfAmdBinary(processInstance);
        }

        // Verify All AMD Processes Have Exited Before Proceeding
        WaitForAmdProcessesToExit();
    }

    private static void WaitForAmdProcessesToExit()
    {
        // Poll Until All Known AMD Processes Have Exited or Timeout
        // Re-kill Any That Were Restarted by the Windows Service Controller
        var deadlineUtc = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var anyRunning = false;
            foreach (var name in AppConfig.s_amdProcessNameAllowlist)
            {
                try
                {
                    var instances = Process.GetProcessesByName(name);
                    if (instances.Length > 0)
                    {
                        anyRunning = true;
                        foreach (var p in instances)
                            ProcessTools.TryKill(p, waitMs: 500);
                    }
                }
                catch { }
            }

            if (!anyRunning)
                return;

            Thread.Sleep(200);
        }
    }

    private static void TryKillByName(string processName)
    {
        try
        {
            // Kill All Instances of the Process by Name
            foreach (var processInstance in Process.GetProcessesByName(processName))
            {
                LogItem(
                    $"{processInstance.ProcessName} (PID {processInstance.Id})",
                    ConsoleColor.Yellow
                );
                ProcessTools.TryKill(processInstance, waitMs: 1500);
            }
        }
        catch { }
    }

    private static void TryKillIfAmdBinary(Process processInstance)
    {
        try
        {
            // Skip if Already Handled by the Name Allowlist
            var processName = processInstance.ProcessName;
            if (
                AppConfig.s_amdProcessNameAllowlist.Contains(
                    processName,
                    StringComparer.OrdinalIgnoreCase
                )
            )
                return;

            // Resolve and Validate the Executable Path
            var executablePath = ProcessTools.TryGetExecutablePath(processInstance);
            if (string.IsNullOrWhiteSpace(executablePath))
                return;

            // Skip if Path Does not Match AMD Markers
            if (
                !TextMatchers.ContainsAnyMarker(
                    executablePath,
                    AppConfig.s_amdExecutablePathMarkers
                )
            )
                return;

            // Kill the AMD-Backed Process
            LogItem($"{processName} (PID {processInstance.Id})", ConsoleColor.Yellow);
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

    private static void StopAmdServices()
    {
        // Stop Known AMD Services by Name
        foreach (
            var serviceName in AppConfig.s_amdServiceNameAllowlist.Distinct(
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            TryStopService(serviceName);
        }
    }

    private static void TryStopService(string serviceName)
    {
        // Issue Stop Command via sc.exe
        var exitCode = CommandRunner.RunExitCode(
            "sc.exe",
            $"stop \"{serviceName}\"",
            timeoutMs: 8000
        );
        if (exitCode != 0)
            return;

        LogItem(serviceName, ConsoleColor.Yellow);
        WaitForServiceStopped(serviceName, timeout: TimeSpan.FromSeconds(6));
    }

    private static void StartAmdServices()
    {
        // Start Known AMD Services by Name
        foreach (
            var serviceName in AppConfig.s_amdServiceNameAllowlist.Distinct(
                StringComparer.OrdinalIgnoreCase
            )
        )
        {
            TryStartService(serviceName);
        }
    }

    private static void TryStartService(string serviceName)
    {
        // Issue Start Command via sc.exe
        var exitCode = CommandRunner.RunExitCode(
            "sc.exe",
            $"start \"{serviceName}\"",
            timeoutMs: 8000
        );
        if (exitCode != 0)
            return;

        LogItem(serviceName, ConsoleColor.Green);
    }

    private static void WaitForServiceStopped(string serviceName, TimeSpan timeout)
    {
        // Poll Until Service is Stopped or Timeout Expires
        var deadlineUtc = DateTime.UtcNow.Add(timeout);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var state = QueryServiceState(serviceName);
            if (string.Equals(state, "STOPPED", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Thread.Sleep(250);
        }
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
            // Launch Adrenalin Minimized
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Minimized,
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
}
