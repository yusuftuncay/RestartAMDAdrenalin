using System.Diagnostics;

namespace AdrenalinRestart.Utilities;

internal static class CommandRunner
{
    #region Methods
    internal static int RunExitCode(string fileName, string arguments, int timeoutMs)
    {
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
            {
                return -1;
            }

            processInstance.WaitForExit(timeoutMs);
            // Return Exit Code
            return processInstance.ExitCode;
        }
        catch
        {
            return -1;
        }
    }

    internal static string CaptureOutput(string fileName, string arguments, int timeoutMs)
    {
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
            {
                return string.Empty;
            }

            // Capture Standard Output and Error
            var stdout = processInstance.StandardOutput.ReadToEnd();
            var stderr = processInstance.StandardError.ReadToEnd();

            processInstance.WaitForExit(timeoutMs);

            // Prefer Standard Output
            if (!string.IsNullOrWhiteSpace(stdout))
            {
                return stdout;
            }

            return stderr;
        }
        catch
        {
            return string.Empty;
        }
    }
    #endregion
}
