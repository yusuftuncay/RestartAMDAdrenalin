namespace AdrenalinRestart.Configuration;

internal static class AppConfig
{
    #region Adrenalin
    // Paths to the Adrenalin Executable
    internal static readonly string[] s_adrenalinExecutablePaths =
    [
        @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe",
        @"C:\Program Files\AMD\CNext\CNext\RadeonSettings.exe",
    ];

    // Keywords for Dynamic AMD Service and Process Discovery
    internal static readonly string[] s_amdKeywords = ["AMD", "Radeon"];

    // Path Substrings Identifying AMD Executables
    internal static readonly string[] s_amdExecutablePathMarkers =
    [
        @"\AMD\",
        @"\Radeon\",
        @"\Advanced Micro Devices\",
        @"\CNext\",
    ];
    #endregion

    #region Detect
    // Process Scan Frequency
    internal static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(2);

    // Delay Before Resetting After Game Start
    internal static readonly TimeSpan s_gameStartDelay = TimeSpan.FromSeconds(2);
    #endregion

    #region Game Filter
    // Minimum File Size to Qualify as a Game Executable
    internal const long MinGameExeBytes = 5L * 1024L * 1024L;

    // Exact Executable Names to Skip
    internal static readonly string[] s_exeNameBlocklist =
    [
        "steam",
        "steamwebhelper",
        "epicgameslauncher",
        "epicwebhelper",
        "rockstarservice",
        "launcher",
        "socialclubhelper",
        "riotclientservices",
        "riotclientux",
        "riotclientcrashhandler",
        "easyanticheat",
        "eac_launcher",
        "battleye",
        "beservice",
        "dxsetup",
        "vcredist",
        "vc_redist",
        "unins000",
    ];

    // Executable Name Substrings to Skip
    internal static readonly string[] s_exeNameTokenBlocklist =
    [
        "uninstall",
        "unins",
        "setup",
        "installer",
        "install",
        "update",
        "updater",
        "patch",
        "helper",
        "launcher",
        "service",
        "crash",
        "crashhandler",
        "reporter",
        "overlay",
        "redistributable",
        "redist",
        "prereq",
        "prerequisite",
        "cef",
        "webhelper",
    ];

    // Install Path Substrings to Skip
    internal static readonly string[] s_pathTokenBlocklist =
    [
        "_commonredist",
        "commonredist",
        "redistributable",
        "redistributables",
        "redist",
        "installers",
        "installer",
        "setup",
        "uninstall",
        "support",
        "directx",
        "vcredist",
        "prereq",
        "prerequisite",
        "easyanticheat",
        "battleye",
        "punkbuster",
        "anticheat",
        "crash",
        "crashreport",
        "crashreporter",
        "launcher",
        "riot client",
    ];

    // Publisher Substrings to Skip in Windows Uninstall Registry Scan
    internal static readonly string[] s_uninstallPublisherBlocklist =
    [
        "Microsoft",
        "Google",
        "Adobe",
        "Intel",
        "NVIDIA",
        "Apple",
        "Mozilla",
        "Qualcomm",
        "Realtek",
        "Logitech",
        "Corsair",
        "Razer",
    ];

    // Path Prefixes to Skip in Windows Uninstall Registry Scan
    internal static readonly string[] s_uninstallPathPrefixBlocklist =
    [
        @"C:\Windows",
        @"C:\Program Files\Common Files",
        @"C:\Program Files (x86)\Common Files",
        @"C:\ProgramData\Microsoft",
    ];
    #endregion
}
