using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using RestartAMDAdrenalin.Configuration;
using RestartAMDAdrenalin.Native;
using RestartAMDAdrenalin.Utilities;

namespace RestartAMDAdrenalin.Amd;

[SupportedOSPlatform("windows")]
internal static class AmdReset
{
    internal static void ExecuteReset()
    {
        StopAmdProcesses();
        StopAmdServicesByBinaryPath();

        Thread.Sleep(800);

        StartAdrenalin();
        CloseAdrenalinWindow();
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
    }

    private static void TryKillByName(string processName)
    {
        try
        {
            // Kill All Instances of the Process by Name
            foreach (var processInstance in Process.GetProcessesByName(processName))
            {
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
            {
                return;
            }

            // Resolve and Validate the Executable Path
            var executablePath = ProcessTools.TryGetExecutablePath(processInstance);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            // Skip if Path Does not Match AMD Markers
            if (
                !TextMatchers.ContainsAnyMarker(
                    executablePath,
                    AppConfig.s_amdExecutablePathMarkers
                )
            )
            {
                return;
            }

            // Kill the AMD-Backed Process
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

    private static void StopAmdServicesByBinaryPath()
    {
        // Query All Running Services
        var serviceEntries = QueryAllServiceEntries();
        // Stop Services Backed by AMD Binaries
        foreach (var (serviceName, displayName) in serviceEntries)
        {
            if (!IsServiceBackedByAmdBinary(serviceName))
            {
                continue;
            }

            Console.WriteLine($"Stopping Service: {displayName}");
            TryStopService(serviceName);
        }
    }

    private static bool IsServiceBackedByAmdBinary(string serviceName)
    {
        // Query Service Configuration via sc.exe
        var outputText = CommandRunner.CaptureOutput(
            "sc.exe",
            $"qc \"{serviceName}\"",
            timeoutMs: 8000
        );
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return false;
        }

        // Parse and Validate the Binary Path
        var binaryPathName = TryParseBinaryPathName(outputText);
        if (string.IsNullOrWhiteSpace(binaryPathName))
        {
            return false;
        }

        // Check if Binary Path Matches AMD Markers
        return TextMatchers.ContainsAnyMarker(binaryPathName, AppConfig.s_amdExecutablePathMarkers);
    }

    private static string? TryParseBinaryPathName(string scQcOutput)
    {
        // Scan Lines for BINARY_PATH_NAME
        var lines = scQcOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (!line.StartsWith("BINARY_PATH_NAME", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Extract the Value After the Colon
            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
            {
                continue;
            }

            var value = line[(colonIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            return value.Trim('"');
        }

        return null;
    }

    private static List<(string ServiceName, string DisplayName)> QueryAllServiceEntries()
    {
        // Run sc.exe to List All Services
        var outputText = CommandRunner.CaptureOutput("sc.exe", "query state= all", timeoutMs: 8000);
        if (string.IsNullOrWhiteSpace(outputText))
        {
            return [];
        }

        // Parse SERVICE_NAME and DISPLAY_NAME Entries
        var lines = outputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var results = new List<(string ServiceName, string DisplayName)>();

        var currentServiceName = string.Empty;
        var currentDisplayName = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("SERVICE_NAME:", StringComparison.OrdinalIgnoreCase))
            {
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

        // Add the Last Collected Entry
        if (!string.IsNullOrWhiteSpace(currentServiceName))
        {
            results.Add((currentServiceName, currentDisplayName));
        }

        return results;
    }

    private static void TryStopService(string serviceName)
    {
        // Issue Stop Command via sc.exe
        var exitCode = CommandRunner.RunExitCode(
            "sc.exe",
            $"stop \"{serviceName}\"",
            timeoutMs: 8000
        );
        // Wait for Service to Reach Stopped State
        if (exitCode == 0)
        {
            WaitForServiceStopped(serviceName, timeout: TimeSpan.FromSeconds(6));
        }
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
        {
            return null;
        }

        // Locate the STATE Block in the Output
        var stateIndex = outputText.IndexOf("STATE", StringComparison.OrdinalIgnoreCase);
        if (stateIndex < 0)
        {
            return null;
        }

        var stateBlock = outputText[stateIndex..];

        // Determine if the Service is Running or Stopped
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

    private static void StartAdrenalin()
    {
        // Find the First Valid Adrenalin Executable
        var executablePath = AppConfig.s_adrenalinExecutablePaths.FirstOrDefault(File.Exists);
        if (executablePath is null)
        {
            Console.WriteLine("Adrenalin Not Found");
            return;
        }

        try
        {
            // Launch Adrenalin
            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true,
            };

            Process.Start(startInfo);
            Console.WriteLine("Adrenalin Started");
        }
        catch { }
    }

    private static void CloseAdrenalinWindow()
    {
        // Poll Until Window is Found and Closed or Timeout
        var deadlineUtc = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var candidateProcesses = GetAdrenalinCandidateProcesses();
            foreach (var processInstance in candidateProcesses)
            {
                if (TryCloseMainWindow(processInstance))
                {
                    Console.WriteLine("Adrenalin Closed");
                    return;
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

    private static bool TryCloseMainWindow(Process processInstance)
    {
        try
        {
            // Refresh and Validate the Main Window Handle
            processInstance.Refresh();

            var mainWindowHandle = processInstance.MainWindowHandle;
            if (mainWindowHandle == IntPtr.Zero)
            {
                return false;
            }

            // Send a Close Message to the Window
            _ = NativeMethods.PostMessage(
                mainWindowHandle,
                NativeMethods.WindowMessageClose,
                IntPtr.Zero,
                IntPtr.Zero
            );
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            // Always Dispose the Process Handle
            try
            {
                processInstance.Dispose();
            }
            catch { }
        }
    }
}
