namespace RestartAMDAdrenalin.Configuration;

internal static class AppConfig
{
    #region Adrenalin
    // Paths to the Adrenalin Executable
    internal static readonly string[] s_adrenalinExecutablePaths =
    [
        @"C:\Program Files\AMD\CNext\CNext\RadeonSoftware.exe",
        @"C:\Program Files\AMD\CNext\CNext\RadeonSettings.exe",
    ];

    // AMD Process Names to Kill by Name
    internal static readonly string[] s_amdProcessNameAllowlist =
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

    // Path Substrings Identifying AMD Executables
    internal static readonly string[] s_amdExecutablePathMarkers =
    [
        @"\AMD\",
        @"\Advanced Micro Devices\",
        @"\CNext\",
    ];
    #endregion

    #region Detect
    // Process Scan Frequency
    internal static readonly TimeSpan s_pollInterval = TimeSpan.FromSeconds(2);

    // Delay Before Resetting After Game Start
    internal static readonly TimeSpan s_gameStartDelay = TimeSpan.FromSeconds(2);

    // Minimum Time Between Resets
    internal static readonly TimeSpan s_resetDebounce = TimeSpan.FromMinutes(5);
    #endregion

    #region Game Filter
    // Minimum File Size to Qualify as a Game Executable
    internal const long MinGameExeBytes = 15L * 1024L * 1024L;

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
    ];
    #endregion
}
