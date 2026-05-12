using System.Runtime.Versioning;
using AdrenalinRestart.Configuration;

namespace AdrenalinRestart.Tray;

[SupportedOSPlatform("windows")]
internal sealed class TrayManager : IDisposable
{
    // Tray Icon Instance
    private readonly NotifyIcon _notifyIcon;

    // Setting Toggle Items That Need Checked State Refreshed on Open
    private readonly ToolStripMenuItem _startupToggleItem;
    private readonly ToolStripMenuItem _trayToggleItem;
    private readonly ToolStripMenuItem _startMinimizedToggleItem;

    // Settings Accessor and Mutators
    private readonly Func<UserSettings> _getSettings;
    private readonly Action<bool> _setStartup;
    private readonly Action<bool> _setTray;
    private readonly Action<bool> _setStartMinimized;

    #region Methods
    internal TrayManager(
        Action openConsoleCallback,
        Action resetCallback,
        Action restartMonitoringCallback,
        Action statusCallback,
        Action saveCallback,
        Func<UserSettings> getSettings,
        Action<bool> setStartup,
        Action<bool> setTray,
        Action<bool> setStartMinimized,
        Action exitCallback
    )
    {
        _getSettings = getSettings;
        _setStartup = setStartup;
        _setTray = setTray;
        _setStartMinimized = setStartMinimized;

        // Build Context Menu
        var contextMenu = new ContextMenuStrip();
        contextMenu.Opening += (_, _) => RefreshToggleStates();

        // Utility Group
        var openConsoleItem = new ToolStripMenuItem("Open Console");
        openConsoleItem.Click += (_, _) => openConsoleCallback();
        contextMenu.Items.Add(openConsoleItem);

        var restartMonitoringItem = new ToolStripMenuItem("Restart Monitoring");
        restartMonitoringItem.Click += (_, _) => restartMonitoringCallback();
        contextMenu.Items.Add(restartMonitoringItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Actions Group
        var resetItem = new ToolStripMenuItem("Reset");
        resetItem.Click += (_, _) => resetCallback();
        contextMenu.Items.Add(resetItem);

        var statusItem = new ToolStripMenuItem("Status");
        statusItem.Click += (_, _) =>
        {
            openConsoleCallback();
            statusCallback();
        };
        contextMenu.Items.Add(statusItem);

        var saveItem = new ToolStripMenuItem("Save");
        saveItem.Click += (_, _) =>
        {
            openConsoleCallback();
            saveCallback();
        };
        contextMenu.Items.Add(saveItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Settings Toggles Group (Checkable)
        _startupToggleItem = new ToolStripMenuItem("Run on Startup") { CheckOnClick = true };
        _startupToggleItem.Click += (_, _) => _setStartup(_startupToggleItem.Checked);
        contextMenu.Items.Add(_startupToggleItem);

        _trayToggleItem = new ToolStripMenuItem("Minimize to Tray") { CheckOnClick = true };
        _trayToggleItem.Click += (_, _) => _setTray(_trayToggleItem.Checked);
        contextMenu.Items.Add(_trayToggleItem);

        _startMinimizedToggleItem = new ToolStripMenuItem("Start Minimized")
        {
            CheckOnClick = true,
        };
        _startMinimizedToggleItem.Click += (_, _) =>
            _setStartMinimized(_startMinimizedToggleItem.Checked);
        contextMenu.Items.Add(_startMinimizedToggleItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => exitCallback();
        contextMenu.Items.Add(exitItem);

        // Build Tray Icon
        _notifyIcon = new NotifyIcon
        {
            Text = "Adrenalin Restart",
            Icon = SystemIcons.Application,
            ContextMenuStrip = contextMenu,
            Visible = true,
        };

        // Double Click Opens Console
        _notifyIcon.DoubleClick += (_, _) => openConsoleCallback();
    }

    internal void ShowBalloonTip(string title, string message)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void RefreshToggleStates()
    {
        // Sync Checked State with Current Settings
        var currentSettings = _getSettings();
        _startupToggleItem.Checked = currentSettings.StartupEnabled;
        _trayToggleItem.Checked = currentSettings.MinimizeToTray;
        _startMinimizedToggleItem.Checked = currentSettings.StartMinimized;
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
    #endregion
}
