using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RestartAMDAdrenalin.Native;

[SupportedOSPlatform("windows")]
internal static partial class NativeMethods
{
    // WM_CLOSE Message Code
    internal const uint WindowMessageClose = 0x0010;

    // P/Invoke Signature for PostMessage
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam
    );

    // SW_HIDE Window Show State
    internal const int SwHide = 0;

    // EnumWindows Callback Delegate
    internal delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // P/Invoke: Enumerate All Top Level Windows
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    // P/Invoke: Get the Process ID Owning a Window
    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // P/Invoke: Check if a Window is Visible
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsWindowVisible(IntPtr hWnd);

    // P/Invoke: Get the Console Window Handle
    [LibraryImport("kernel32.dll")]
    internal static partial IntPtr GetConsoleWindow();

    // P/Invoke: Show or Hide a Window
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
