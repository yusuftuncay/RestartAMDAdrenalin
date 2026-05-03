using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RestartAMDAdrenalin.Native;

[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    #region Constants
    // WM_CLOSE Message Code
    internal const uint WindowMessageClose = 0x0010;

    // SW_HIDE Window Show State
    internal const int SwHide = 0;

    // SW_MINIMIZE Window Show State
    internal const int SwMinimize = 6;
    #endregion

    #region Delegates
    // EnumWindows Callback
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    #endregion

    #region P/Invoke
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam
    );

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr GetConsoleWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
    #endregion
}
