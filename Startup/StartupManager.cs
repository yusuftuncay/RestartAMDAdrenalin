using System.Runtime.Versioning;
using Microsoft.Win32;
using Microsoft.Win32.TaskScheduler;

namespace AdrenalinRestart.Startup;

[SupportedOSPlatform("windows")]
internal static class StartupManager
{
    // Registry Key for Current User Startup (Fallback Read)
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    // Application Name Used in Both Registry and Task Scheduler
    private const string ApplicationName = "AdrenalinRestart";

    #region Methods
    internal static void Enable()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath))
            return;

        // Remove Old Registry Entry if Present
        RemoveRegistryEntry();

        // Register via Task Scheduler with Highest Privileges (No UAC Prompt)
        using var taskService = new TaskService();
        var taskDefinition = taskService.NewTask();
        taskDefinition.RegistrationInfo.Description = "Starts Adrenalin Restart on user logon";
        taskDefinition.Principal.RunLevel = TaskRunLevel.Highest;
        taskDefinition.Principal.LogonType = TaskLogonType.InteractiveToken;
        taskDefinition.Settings.DisallowStartIfOnBatteries = false;
        taskDefinition.Settings.StopIfGoingOnBatteries = false;
        taskDefinition.Settings.ExecutionTimeLimit = TimeSpan.Zero;

        taskDefinition.Triggers.Add(
            new LogonTrigger { UserId = Environment.UserDomainName + "\\" + Environment.UserName }
        );
        taskDefinition.Actions.Add(new ExecAction($"\"{executablePath}\""));

        taskService.RootFolder.RegisterTaskDefinition(ApplicationName, taskDefinition);
    }

    internal static void Disable()
    {
        RemoveRegistryEntry();
        RemoveScheduledTask();
    }

    private static void RemoveRegistryEntry()
    {
        using var registryKey = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, writable: true);
        registryKey?.DeleteValue(ApplicationName, throwOnMissingValue: false);
    }

    private static void RemoveScheduledTask()
    {
        using var taskService = new TaskService();
        try
        {
            taskService.RootFolder.DeleteTask(ApplicationName, exceptionOnNotExists: false);
        }
        catch { }
    }
    #endregion
}
