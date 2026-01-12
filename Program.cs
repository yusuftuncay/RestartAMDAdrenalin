using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace RestartAMDAdrenalin;

[SupportedOSPlatform("windows")]
internal static class Program
{
    #region Configuration
    private static readonly string[] AdrenalinExecutablePaths =
    [
        @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe",
        @"C:\Program Files\AMD\CNext\CNext\RadeonSettings.exe",
    ];

    private static readonly string[] AmdServiceNameMarkers = ["AMD", "Radeon"];
    private static readonly string[] AmdProcessNameMarkers = ["AMD", "Radeon"];

    private static readonly string[] AmdProcessNameWhitelist =
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

    private const uint WindowMessageClose = 0x0010;
    #endregion

    #region Entry Point
    private static void Main()
    {
        // Ensure Windows Platform
        if (!OperatingSystem.IsWindows())
        {
            Console.WriteLine("Windows Only Tool.");
            Console.Write("Press Any Key...");
            Console.ReadKey(true);
            return;
        }

        // Ensure Administrator Rights
        if (!IsCurrentProcessAdministrator())
        {
            RelaunchCurrentProcessAsAdministrator();
            return;
        }

        // Print Tool Header
        Console.WriteLine("AMD Adrenalin Reset");
        Console.WriteLine("-------------------");

        // Stop AMD Processes
        Console.WriteLine("Stopping AMD processes...");
        StopAmdRelatedProcesses();

        // Stop AMD Services
        Console.WriteLine("Stopping AMD services...");
        StopAllAmdRelatedServices();

        // Wait Briefly
        Thread.Sleep(800);

        // Start Adrenalin Normally
        Console.WriteLine("Starting Adrenalin...");
        StartAdrenalinNormal();

        // Close UI To Tray
        Console.WriteLine("Sending Adrenalin to tray...");
        CloseAdrenalinMainWindowToTray();

        // Print Done Status
        Console.WriteLine("Done.");
        Console.WriteLine();

        // Wait Before Exit
        Console.Write("Press any key to close...");
        Console.ReadKey(true);
    }
    #endregion

    #region Elevation
    private static bool IsCurrentProcessAdministrator()
    {
        // Check Administrator Token
        using var windowsIdentity = WindowsIdentity.GetCurrent();
        var windowsPrincipal = new WindowsPrincipal(windowsIdentity);
        return windowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void RelaunchCurrentProcessAsAdministrator()
    {
        // Relaunch With Elevation
        try
        {
            var currentProcessPath =
                Environment.ProcessPath
                ?? throw new InvalidOperationException("Missing process path.");

            var startInfo = new ProcessStartInfo
            {
                FileName = currentProcessPath,
                UseShellExecute = true,
                Verb = "runas",
            };

            Process.Start(startInfo);
        }
        catch (Win32Exception)
        {
            Console.WriteLine("Admin Permission Denied.");
            Console.Write("Press Any Key...");
            Console.ReadKey(true);
        }
    }
    #endregion
    #region Process Control
    private static void StopAmdRelatedProcesses()
    {
        // Stop Whitelisted Processes
        foreach (
            var processName in AmdProcessNameWhitelist.Distinct(StringComparer.OrdinalIgnoreCase)
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
            if (!ContainsAnyMarker(processName, AmdProcessNameMarkers))
                return;

            if (AmdProcessNameWhitelist.Contains(processName, StringComparer.OrdinalIgnoreCase))
                return;

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
                return true;
        }

        return false;
    }

    private static void TryKillProcess(Process processInstance)
    {
        // Force Kill Process
        try
        {
            Console.WriteLine($"  Killing: {processInstance.ProcessName} ({processInstance.Id})");
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

        Console.WriteLine($"  Found: {amdServiceEntries.Count}");

        // Stop Matching Services
        foreach (var (ServiceName, DisplayName) in amdServiceEntries)
        {
            TryStopServiceUsingSc(ServiceName, DisplayName);
        }
    }

    private static bool IsAmdRelatedService(string serviceName, string displayName)
    {
        // Match AMD Markers
        foreach (var marker in AmdServiceNameMarkers)
        {
            if (serviceName.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;

            if (displayName.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void TryStopServiceUsingSc(string serviceName, string displayName)
    {
        // Stop Service Using Sc
        var exitCode = RunScCommand($"stop \"{serviceName}\"");
        if (exitCode == 0)
        {
            Console.WriteLine($"  Stopping: {displayName}");
            WaitForServiceState(
                serviceName,
                expectedState: "STOPPED",
                timeout: TimeSpan.FromSeconds(6)
            );
            return;
        }

        // Ignore Stop Failures
        Console.WriteLine($"  Skipped: {displayName}");
    }

    private static List<(string ServiceName, string DisplayName)> QueryAllServiceEntries()
    {
        // Query All Services
        var outputText = RunCommandCaptureOutput("sc.exe", "query state= all");
        if (string.IsNullOrWhiteSpace(outputText))
            return [];

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
                    results.Add((currentServiceName, currentDisplayName));

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
            results.Add((currentServiceName, currentDisplayName));

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
                return -1;

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
                return;

            Thread.Sleep(250);
        }
    }

    private static string? QueryServiceState(string serviceName)
    {
        // Query Service Using Sc
        var outputText = RunCommandCaptureOutput("sc.exe", $"query \"{serviceName}\"");
        if (string.IsNullOrWhiteSpace(outputText))
            return null;

        var stateIndex = outputText.IndexOf("STATE", StringComparison.OrdinalIgnoreCase);
        if (stateIndex < 0)
            return null;

        var stateBlock = outputText[stateIndex..];

        if (stateBlock.Contains("RUNNING", StringComparison.OrdinalIgnoreCase))
            return "RUNNING";

        if (stateBlock.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
            return "STOPPED";

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
                return string.Empty;

            var standardOutput = processInstance.StandardOutput.ReadToEnd();
            var standardError = processInstance.StandardError.ReadToEnd();

            processInstance.WaitForExit(8000);

            if (!string.IsNullOrWhiteSpace(standardOutput))
                return standardOutput;

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
        var executablePath = AdrenalinExecutablePaths.FirstOrDefault(System.IO.File.Exists);
        if (executablePath is null)
        {
            Console.WriteLine("  Adrenalin EXE Not Found.");
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
            Console.WriteLine("  Failed To Launch Adrenalin.");
        }
    }

    private static void CloseAdrenalinMainWindowToTray()
    {
        // Wait For UI Window
        var deadlineUtc = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadlineUtc)
        {
            var adrenalinProcesses = GetAdrenalinCandidateProcesses();
            foreach (var adrenalinProcess in adrenalinProcesses)
            {
                if (TryCloseMainWindow(adrenalinProcess))
                    return;
            }

            Thread.Sleep(250);
        }

        Console.WriteLine("  No UI Window Found.");
    }

    private static Process[] GetAdrenalinCandidateProcesses()
    {
        // Find Adrenalin Processes
        try
        {
            var radeonSoftwareProcesses = Process.GetProcessesByName("RadeonSoftware");
            if (radeonSoftwareProcesses.Length != 0)
                return radeonSoftwareProcesses;

            var radeonSettingsProcesses = Process.GetProcessesByName("RadeonSettings");
            if (radeonSettingsProcesses.Length != 0)
                return radeonSettingsProcesses;

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
                return false;

            _ = PostMessage(mainWindowHandle, WindowMessageClose, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam
    );
    #endregion
}
