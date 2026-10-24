namespace VRudders;

internal sealed partial class MainForm
{
    readonly bool initiallyHidden;
    readonly ToolStripMenuItem trayToggle = new("Start forwarding");
    bool initialVisibilityHandled, exitRequested, lastUiForwarding, resourcesDisposed;

    void ConfigureTray()
    {
        trayOption.Checked = settings.MinimizeToTray;
        closeTrayOption.Checked = settings.CloseToTray;
        startTrayOption.Checked = settings.StartInTray;
        trayOption.CheckedChanged += (_, _) => SaveTrayOptions();
        closeTrayOption.CheckedChanged += (_, _) => SaveTrayOptions();
        startTrayOption.CheckedChanged += (_, _) => SaveTrayOptions();
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show VRudders", null, (_, _) => RestoreWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(trayToggle); trayToggle.Click += (_, _) => ToggleForwarding();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit VRudders", null, (_, _) => RequestExit());
        menu.Opening += (_, _) => UpdateLocks();
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => RestoreWindow();
        tray.Visible = true;
        Resize += (_, _) =>
        {
            if (WindowState == FormWindowState.Minimized && settings.MinimizeToTray) HideToTray();
        };
    }

    void SaveTrayOptions()
    {
        settings.MinimizeToTray = trayOption.Checked;
        settings.CloseToTray = closeTrayOption.Checked;
        settings.StartInTray = startTrayOption.Checked;
        SafeAction(Persist);
    }
    void ToggleForwarding()
    {
        if (Forwarding) StopForwarding(); else SafeAction(StartForwarding);
    }
    void UpdateTrayState()
    {
        bool running = Forwarding;
        trayToggle.Text = running ? "Stop forwarding" : "Start forwarding";
        trayToggle.Enabled = start.Enabled;
        trayToggle.BackColor = !start.Enabled ? Color.FromArgb(125, 139, 152)
            : running ? Color.FromArgb(180, 35, 50) : Color.FromArgb(20, 120, 70);
        trayToggle.ForeColor = Color.White;
        tray.Text = running ? "VRudders · Forwarding yaw" : "VRudders · Stopped";
    }
    void HideToTray()
    {
        tray.Visible = true;
        Hide(); ShowInTaskbar = false;
    }
    void RestoreWindow()
    {
        initialVisibilityHandled = true;
        ShowInTaskbar = Opacity > 0; WindowState = FormWindowState.Normal; Show();
        if (Opacity > 0) Activate();
    }
    void RequestExit() { exitRequested = true; Close(); }
    protected override bool ShowWithoutActivation => Opacity == 0 || base.ShowWithoutActivation;

    protected override void SetVisibleCore(bool value)
    {
        if (value && !initialVisibilityHandled)
        {
            initialVisibilityHandled = true;
            if (initiallyHidden)
            {
                // Suppress the first Show entirely, rather than flashing then hiding.
                if (!IsHandleCreated) CreateHandle();
                ShowInTaskbar = false; value = false;
            }
        }
        base.SetVisibleCore(value);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel) return;
        if (e.CloseReason == CloseReason.UserClosing && !exitRequested && settings.CloseToTray)
        {
            e.Cancel = true; HideToTray(); return;
        }
        closing = true; timer.Stop(); worker.Dispose();
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !resourcesDisposed)
        {
            resourcesDisposed = true; closing = true; timer.Stop(); worker.Dispose();
            timer.Dispose(); tray.Visible = false;
            tray.ContextMenuStrip?.Dispose(); tray.Dispose(); tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
