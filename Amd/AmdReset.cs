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
        Console.WriteLine("[1/3] Stopping AMD Services..");
        StopAmdServices();

        Console.WriteLine();
        Console.WriteLine("[2/3] Stopping AMD Processes..");
        StopAmdProcesses();

        Thread.Sleep(800);

        Console.WriteLine();
        Console.WriteLine("[3/3] Starting Adrenalin..");
        if (StartAdrenalin())
        {
            MinimizeAdrenalinWindow();
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
        var deadlineUtc = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var anyRunning = false;
            foreach (var name in AppConfig.s_amdProcessNameAllowlist)
            {
                try
                {
                    var instances = Process.GetProcessesByName(name);
                    foreach (var p in instances)
                        try
                        {
                            p.Dispose();
                        }
                        catch { }

                    if (instances.Length > 0)
                    {
                        anyRunning = true;
                        break;
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
                Console.WriteLine(
                    $"  Killing Process: {processInstance.ProcessName} (PID {processInstance.Id})"
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
            Console.WriteLine($"  Killing AMD Process: {processName} (PID {processInstance.Id})");
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

        // Log and Wait for Service to Reach Stopped State
        Console.WriteLine($"  Stopping Service: {serviceName}");
        WaitForServiceStopped(serviceName, timeout: TimeSpan.FromSeconds(6));
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
                Console.WriteLine($"  Service Stopped: {serviceName}");
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
            Console.WriteLine("  Adrenalin Not Found");
            return false;
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
        }
        catch
        {
            Console.WriteLine("  Adrenalin Start Failed");
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
                    try
                    {
                        candidate.Dispose();
                    }
                    catch { }

                Console.WriteLine("  Adrenalin Started");
                return true;
            }

            Thread.Sleep(250);
        }

        Console.WriteLine("  Adrenalin Start Timed Out");
        return false;
    }

    private static void MinimizeAdrenalinWindow()
    {
        // Poll Until Adrenalin Window Can be Minimized or Timeout
        var deadlineUtc = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var minimized = false;
            foreach (var processInstance in GetAdrenalinCandidateProcesses())
            {
                // TryMinimizeProcess Always Disposes the Process Handle
                if (TryMinimizeProcess(processInstance))
                    minimized = true;
            }

            if (minimized)
            {
                Console.WriteLine("  Adrenalin Minimized");
                return;
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

    private static bool TryMinimizeProcess(Process processInstance)
    {
        try
        {
            processInstance.Refresh();

            // Try the Main Window Handle First
            var windowHandle = processInstance.MainWindowHandle;

            // Fall Back to Enumerating All Windows for This Process
            if (windowHandle == IntPtr.Zero)
                windowHandle = FindVisibleProcessWindow(processInstance.Id);

            if (windowHandle == IntPtr.Zero)
                return false;

            // Send Minimize Command to the Window
            _ = NativeMethods.ShowWindow(windowHandle, NativeMethods.SwMinimize);
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

    private static IntPtr FindVisibleProcessWindow(int processId)
    {
        var result = IntPtr.Zero;

        // Enumerate All Top Level Windows to Find a Visible One for This Process
        NativeMethods.EnumWindows(
            (hWnd, _) =>
            {
                NativeMethods.GetWindowThreadProcessId(hWnd, out var pid);
                if (pid != (uint)processId || !NativeMethods.IsWindowVisible(hWnd))
                    return true;

                result = hWnd;
                return false; // Stop Enumeration
            },
            IntPtr.Zero
        );

        return result;
    }
}
