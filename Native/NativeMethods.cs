using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AdrenalinRestart.Native;

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

    // SW_SHOW Window Show State
    internal const int SwShow = 5;

    // SW_RESTORE Window Show State
    internal const int SwRestore = 9;

    // CTRL_CLOSE_EVENT Signal Code
    internal const uint CtrlCloseEvent = 2;

    // System Menu Close Item Identifier
    internal const uint ScClose = 0xF060;

    // DeleteMenu By Command Flag
    internal const uint MfByCommand = 0x00000000;
    #endregion

    #region Delegates
    // EnumWindows Callback
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // SetConsoleCtrlHandler Callback
    internal delegate bool ConsoleCtrlHandlerDelegate(uint controlType);
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

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetConsoleCtrlHandler(
        ConsoleCtrlHandlerDelegate? handlerRoutine,
        [MarshalAs(UnmanagedType.Bool)] bool add
    );

    [LibraryImport("user32.dll")]
    internal static partial IntPtr GetSystemMenu(
        IntPtr hWnd,
        [MarshalAs(UnmanagedType.Bool)] bool bRevert
    );

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsIconic(IntPtr hWnd);
    #endregion
}
