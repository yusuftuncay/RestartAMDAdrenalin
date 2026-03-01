using System.Diagnostics;

namespace RestartAMDAdrenalin.Utilities;

internal static class ProcessTools
{
    internal static Process[] SafeGetAllProcesses()
    {
        try
        {
            return Process.GetProcesses();
        }
        catch
        {
            return [];
        }
    }

    internal static string? TryGetExecutablePath(Process processInstance)
    {
        try
        {
            return processInstance.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    internal static void TryKill(Process processInstance, int waitMs)
    {
        try
        {
            // Kill the Full Process Tree and Wait for Exit
            processInstance.Kill(entireProcessTree: true);
            processInstance.WaitForExit(waitMs);
        }
        catch { }
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
