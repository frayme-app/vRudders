using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRudders;

internal sealed class MainForm : Form
{
    readonly ComboBox devices = Combo(400), axes = Combo(65), profiles = Combo(300), precisionKey = Combo(75);
    readonly Button start = Button("START"), refresh = Button("Refresh"), apply = Button("Apply & save");
    readonly Label forwardingState = Label("SELECT YOUR PEDALS"), forwardingHint = Label("Select your controller and input axis first.");
    readonly Label connection = Label("Looking for pedals…"), inputStatus = Label(""), processedStatus = Label(""), outputStatus = Label("Not forwarding"), editStatus = Label(""), calibrationStatus = Label("No calibration captured.");
    readonly AxisTrack inputTrack = new(), processedTrack = new(), outputTrack = new();
    readonly CurveDisplay graph = new(), tuningGraph = new();
    readonly CheckBox bypass = Check("Bypass tuning · original linear yaw", true), reverse = Check("Reverse yaw direction"), useCalibration = Check("Use this device's calibration");
    readonly CheckBox linkedEnds = Check("Link left / right ends", true), precision = Check("Enable hold-to-precision key"), trim = Check("Enable trim · F9 left / F10 right / F11 reset"), trayOption = Check("Minimize to the tray");
    readonly NumericUpDown dead = Number(0, 20), leftEnd = Number(0, 30), rightEnd = Number(0, 30), curve = Number(-100, 100), sensitivity = Number(10, 100, 100), smoothing = Number(0, 200), precisionCurve = Number(0, 100, 80);
    readonly TextBox profileName = new() { Width = 280, MaxLength = 80 };
    readonly FlowLayoutPanel profileActions = Flow(), tuningControls = Flow(), calibrationActions = Flow(), trimActions = Flow();
    readonly Button captureCenter = Button("1 · Capture center"), captureLeft = Button("2 · Capture left stop"), captureRight = Button("3 · Capture right stop"), saveCalibration = Button("Save device calibration");
    readonly Button clearCalibration = Button("Clear saved calibration");
    readonly System.Windows.Forms.Timer timer = new() { Interval = 16 };
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly YawProcessor processor = new();
    readonly NotifyIcon tray = new() { Text = "VRudders · Rudder Control", Icon = Branding.AppIcon };
    readonly ToolTip tips = new();
    readonly Queue<uint> recent = new();
    readonly UserSettings settings;
    readonly bool settingsReadable;
    IJoystickReader? reader;
    Joystick? selected, virtualDevice;
    Axis? axis;
    VirtualOutput? output;
    Tuning active = new();
    bool loading, dirty, disconnected, closing;
    bool trimLeftDown, trimRightDown, trimResetDown;
    uint raw, minSeen = uint.MaxValue, maxSeen;
    uint? capturedCenter, capturedLeft, capturedRight;
    long lastTick, lastRead, nextReconnect;
    string? reconnectKey;
    string axisPreference = "Z";
    double processed;
    FlightProfile CurrentProfile => profiles.SelectedItem as FlightProfile ?? FlightProfile.Baseline;
    string CalibrationKey => (selected?.Key ?? "") + "/" + (axis?.Name ?? "Z");
    Calibration? DeviceCalibration => settings.Calibrations.GetValueOrDefault(CalibrationKey);
    bool Protected => CurrentProfile.Id == FlightProfile.BaselineId;

    public MainForm()
    {
        settings = SettingsStore.Load(out string? settingsError);
        settingsReadable = settingsError == null;
        axisPreference = settings.Axis;
        Text = "VRudders — Rudder Control · " + Application.ProductVersion.Split('+')[0];
        Icon = Branding.AppIcon;
        ClientSize = new(1040, 920); MinimumSize = new(950, 820);
        Font = new("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 250);
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 6 };
        foreach (int height in new[] { 168, 42, 42 }) root.RowStyles.Add(new(SizeType.Absolute, height));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 88)); root.RowStyles.Add(new(SizeType.Absolute, 32));
        Controls.Add(root);
        root.Controls.Add(new FlightHeader(), 0, 0);
        var deviceRow = Flow(); deviceRow.Controls.Add(devices); deviceRow.Controls.Add(Label("Axis")); deviceRow.Controls.Add(axes); deviceRow.Controls.Add(refresh); root.Controls.Add(deviceRow, 0, 1);
        var profileRow = Flow(); profileRow.Controls.Add(Label("Flight profile")); profileRow.Controls.Add(profiles); profileRow.Controls.Add(apply); root.Controls.Add(profileRow, 0, 2);
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var flightTab = new TabPage("Live flight") { BackColor = BackColor, Padding = new(12) };
        var tuningTab = new TabPage("Tuning & profiles") { BackColor = BackColor, AutoScroll = true, Padding = new(12) };
        var calibrationTab = new TabPage("Device calibration") { BackColor = BackColor, AutoScroll = true, Padding = new(12) };
        tabs.TabPages.AddRange([flightTab, tuningTab, calibrationTab]); root.Controls.Add(tabs, 0, 3);
        var live = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        live.ColumnStyles.Add(new(SizeType.Percent, 52)); live.ColumnStyles.Add(new(SizeType.Percent, 48)); flightTab.Controls.Add(live);
        var meters = Stack(); live.Controls.Add(meters, 0, 0);
        AddMeter(meters, "1  Physical pedals · raw Windows input", inputTrack, inputStatus);
        AddMeter(meters, "2  Processed yaw · preview / sent target", processedTrack, processedStatus);
        AddMeter(meters, "3  Virtual Z · independent Windows readback", outputTrack, outputStatus);
        Button lessTrim = Button("Trim −1%"), resetTrim = Button("Reset trim"), moreTrim = Button("Trim +1%");
        lessTrim.Click += (_, _) => processor.ChangeTrim(-0.01); moreTrim.Click += (_, _) => processor.ChangeTrim(0.01); resetTrim.Click += (_, _) => processor.ResetTrim();
        trimActions.Controls.AddRange([lessTrim, resetTrim, moreTrim]); meters.Controls.Add(trimActions);
        var liveNotes = Label("Start forwarding, then bind the virtual Z axis to yaw in your game's flight or HOTAS controls.\nKeep VRudders running during your flight."); liveNotes.MaximumSize = new(460, 0); meters.Controls.Add(liveNotes);
        graph.Dock = DockStyle.Fill; live.Controls.Add(graph, 1, 0);

        var tuningLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        tuningLayout.ColumnStyles.Add(new(SizeType.Percent, 68)); tuningLayout.ColumnStyles.Add(new(SizeType.Percent, 32)); tuningTab.Controls.Add(tuningLayout);
        var editor = Stack(); tuningLayout.Controls.Add(editor, 0, 0); tuningGraph.Dock = DockStyle.Fill; tuningLayout.Controls.Add(tuningGraph, 1, 0);
        editor.SizeChanged += (_, _) =>
        {
            int width = Math.Max(280, editor.ClientSize.Width - 22);
            foreach (Control control in editor.Controls) control.MaximumSize = new(width, 0);
            foreach (Control control in tuningControls.Controls) control.MaximumSize = new(width - 6, 0);
        };
        profileActions.Controls.Add(profileName);
        AddAction(profileActions, "New", NewProfile); AddAction(profileActions, "Duplicate", DuplicateProfile);
        AddAction(profileActions, "Reset draft", () => LoadProfile(CurrentProfile));
        AddAction(profileActions, "Import…", ImportProfile); AddAction(profileActions, "Export…", ExportProfile);
        editor.Controls.Add(profileActions); editor.Controls.Add(editStatus);
        tuningControls.FlowDirection = FlowDirection.TopDown; tuningControls.WrapContents = false; tuningControls.AutoSize = true;
        editor.Controls.Add(tuningControls);
        tuningControls.Controls.Add(bypass); tuningControls.Controls.Add(reverse); tuningControls.Controls.Add(useCalibration);
        AddNumber(tuningControls, "Center dead zone", dead, "% · removes resting drift");
        var ends = Flow(); ends.Controls.Add(Label("End dead zones")); ends.Controls.Add(leftEnd); ends.Controls.Add(Label("% left")); ends.Controls.Add(rightEnd); ends.Controls.Add(Label("% right")); ends.Controls.Add(linkedEnds); tuningControls.Controls.Add(ends);
        AddNumber(tuningControls, "Response curve", curve, "% · −100 stronger / 0 linear / +100 softer");
        AddNumber(tuningControls, "Sensitivity / yaw strength", sensitivity, "% · maximum output; 100 retains full authority");
        AddNumber(tuningControls, "Smoothing time", smoothing, "ms · 0 off; higher values add response delay");
        precisionKey.Items.AddRange(new object[] { "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F12" });
        var precisionRow = Flow(); precisionRow.Controls.Add(precision); precisionRow.Controls.Add(precisionKey); precisionRow.Controls.Add(Label("Curve")); precisionRow.Controls.Add(precisionCurve); precisionRow.Controls.Add(Label("% · 300 ms blend")); tuningControls.Controls.Add(precisionRow);
        tuningControls.Controls.Add(trim);
        tuningControls.Controls.Add(Label("Trim steps 1%, limited to ±20%; resets on Stop or profile change.\nGlobal F-key shortcuts are opt-in; choose keys unbound in your game."));
        editor.Controls.Add(Label("Edits preview while stopped. Apply & save before forwarding. Calibration is stored with the device; profiles export tuning only."));
        editor.Controls.Add(trayOption);
        tips.SetToolTip(bypass, "Preserves the original linear conversion and reversal. Skips calibration, dead zones, sensitivity, curve, smoothing, precision, and trim.");
        tips.SetToolTip(smoothing, "Exponential filter time constant: about 63% of a step in this many milliseconds. This is not measured game latency.");
        tips.SetToolTip(curve, "Negative values give more yaw for less pedal travel. At −100, 25% input produces about 58% yaw. Positive values soften the center. Both keep full endpoints at 100% yaw strength.");

        var calibrationPanel = Stack(); calibrationPanel.AutoSize = true; calibrationPanel.Dock = DockStyle.Top; calibrationTab.Controls.Add(calibrationPanel);
        calibrationPanel.Controls.Add(Label("Calibrate the selected physical axis\n\nStop forwarding. Release the pedals at their natural center and capture it.\nHold each full travel stop and capture it. Save, then enable calibration in a custom profile.\nThis changes VRudders only; Windows and the pedal firmware are not modified."));
        calibrationActions.Controls.AddRange([captureCenter, captureLeft, captureRight, saveCalibration]);
        calibrationPanel.Controls.Add(calibrationActions); calibrationPanel.Controls.Add(calibrationStatus);
        calibrationPanel.Controls.Add(clearCalibration);
        clearCalibration.Click += (_, _) => SafeAction(() => { if (output != null) return; settings.Calibrations.Remove(CalibrationKey); Persist(); useCalibration.Checked = false; UpdateCalibrationStatus(); processor.Reset(); });
        captureCenter.Click += (_, _) => CapturePoint(0); captureLeft.Click += (_, _) => CapturePoint(1); captureRight.Click += (_, _) => CapturePoint(2);
        saveCalibration.Click += (_, _) => SafeAction(SaveCalibration);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new(0, 8, 0, 0) };
        actions.ColumnStyles.Add(new(SizeType.Absolute, 190)); actions.ColumnStyles.Add(new(SizeType.Percent, 100)); actions.ColumnStyles.Add(new(SizeType.Absolute, 280));
        start.AutoSize = false; start.Size = new(174, 64); start.Margin = new(0, 2, 12, 0);
        start.Font = new("Segoe UI", 16, FontStyle.Bold); start.FlatStyle = FlatStyle.Flat; start.FlatAppearance.BorderSize = 0; start.UseVisualStyleBackColor = false;
        forwardingState.Font = new("Segoe UI", 10, FontStyle.Bold); forwardingState.Margin = new(0, 1, 0, 4);
        forwardingHint.Margin = new(0); forwardingHint.MaximumSize = new(470, 0);
        var explanation = Stack(); explanation.AutoScroll = false; explanation.Controls.AddRange([forwardingState, forwardingHint]);
        explanation.SizeChanged += (_, _) => forwardingHint.MaximumSize = new(Math.Max(100, explanation.ClientSize.Width - 8), 0);
        var utilities = Flow(); utilities.Margin = new(0, 17, 0, 0);
        Button windows = Button("Windows controllers"), guide = Button("Setup guide");
        utilities.Controls.AddRange([windows, guide]); actions.Controls.Add(start, 0, 0); actions.Controls.Add(explanation, 1, 0); actions.Controls.Add(utilities, 2, 0);
        connection.AutoSize = false; connection.AutoEllipsis = true; connection.Dock = DockStyle.Fill;
        root.Controls.Add(actions, 0, 4); root.Controls.Add(connection, 0, 5);
        windows.Click += (_, _) => Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "control.exe"), "joy.cpl") { UseShellExecute = true });
        guide.Click += (_, _) => { string path = Path.Combine(AppContext.BaseDirectory, "GETTING-STARTED.html"); if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); else MessageBox.Show(this, "See README.md in the source checkout.", "Setup guide"); };
        refresh.Click += (_, _) => RefreshDevices(selected?.Key);
        start.Click += (_, _) => { if (output != null) StopForwarding(); else SafeAction(StartForwarding); };
        apply.Click += (_, _) => SafeAction(ApplyProfile);
        devices.SelectedIndexChanged += (_, _) => { if (!loading) SelectDevice(); };
        axes.SelectedIndexChanged += (_, _) => { if (!loading) SelectAxis(); };
        profiles.SelectedIndexChanged += (_, _) => { if (!loading) LoadProfile(CurrentProfile); };
        foreach (var control in new[] { bypass, reverse, useCalibration, precision, trim }) control.CheckedChanged += (_, _) => DraftChanged();
        foreach (var control in new[] { dead, leftEnd, rightEnd, curve, sensitivity, smoothing, precisionCurve }) control.ValueChanged += (_, _) => DraftChanged();
        leftEnd.ValueChanged += (_, _) => { if (linkedEnds.Checked) rightEnd.Value = leftEnd.Value; };
        linkedEnds.CheckedChanged += (_, _) => { if (linkedEnds.Checked) rightEnd.Value = leftEnd.Value; };
        precisionKey.SelectedIndexChanged += (_, _) => DraftChanged(); profileName.TextChanged += (_, _) => DraftChanged();
        trayOption.Checked = settings.MinimizeToTray;
        trayOption.CheckedChanged += (_, _) => { settings.MinimizeToTray = trayOption.Checked; SafeAction(Persist); };
        var menu = new ContextMenuStrip(); menu.Items.Add("Show VRudders", null, (_, _) => RestoreWindow()); menu.Items.Add("Stop forwarding", null, (_, _) => StopForwarding()); menu.Items.Add("Exit", null, (_, _) => Close());
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => RestoreWindow();
        Resize += (_, _) => { if (WindowState == FormWindowState.Minimized && trayOption.Checked) { tray.Visible = true; Hide(); } };
        FormClosing += (_, _) => { closing = true; timer.Stop(); StopForwarding(); reader?.Dispose(); reader = null; tray.Dispose(); tips.Dispose(); };
        timer.Tick += (_, _) => Poll();
        ReloadProfiles(settings.SelectedProfile); RefreshDevices(settings.DeviceKey);
        if (settingsError != null) connection.Text = settingsError;
        lastTick = clock.ElapsedMilliseconds; timer.Start();
    }

    static ComboBox Combo(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
    internal void SelectPreviewPage(int index) => Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TabControl>().Single().SelectedIndex = index;
    internal void VerifyEditorWorkflow()
    {
        // Invoked only by the isolated UI regression command. Never starts forwarding.
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        Check(Protected && active.Bypass && !tuningControls.Enabled, "Baseline must be protected and linear.");
        NewProfile(); Check(!Protected && active.Bypass, "A new profile starts in bypass.");
        bypass.Checked = false; dead.Value = 4; curve.Value = 65; smoothing.Value = 40;
        linkedEnds.Checked = true; leftEnd.Value = 8; Check(rightEnd.Value == 8, "Linked travel endpoints.");
        Check(dirty && !start.Enabled && apply.Enabled, "Unsaved edits must block forwarding.");
        ApplyProfile(); Check(!dirty && active.Curve == .65 && active.CenterDeadZone == .04 && active.SmoothingMs == 40, "Apply uses the edited profile.");
        curve.Value = -65; ApplyProfile(); Check(active.Curve == -.65 && !dirty, "Aggressive curve can be edited and applied.");
        var restored = SettingsStore.Load(out var error);
        Check(error == null && restored.Profiles.Single().Tuning == active, "Profile was atomically saved.");
        curve.Value = 20; LoadProfile(CurrentProfile); Check(curve.Value == -65 && !dirty, "Reset draft restores saved settings.");
        LoadProfile(FlightProfile.Baseline); // Select through the same ComboBox event as a user.
        profiles.SelectedItem = ((List<FlightProfile>)profiles.DataSource!).First(p => p.Id == FlightProfile.BaselineId);
        Check(Protected && active.Bypass && !active.TrimEnabled, "Working response remains recoverable.");
        if (selected != null)
        {
            string key = selected.Key; InputLost("UI regression: simulated input loss");
            Check(output == null && disconnected && !start.Enabled && reader == null, "Loss stops forwarding and releases the reader.");
            RefreshDevices("deliberately-absent-test-identity", true);
            Check(disconnected && reader == null, "Reconnect never substitutes a different device.");
            RefreshDevices(key, true); Check(output == null, "Reconnect never resumes forwarding automatically.");
        }
    }
    static Button Button(string text) => new() { Text = text, AutoSize = true, UseMnemonic = false, Margin = new(3) };
    static Label Label(string text) => new() { Text = text, AutoSize = true, Margin = new(3, 6, 3, 6) };
    static CheckBox Check(string text, bool value = false) => new() { Text = text, Checked = value, AutoSize = true, Margin = new(3, 6, 3, 6) };
    static NumericUpDown Number(int min, int max, int value = 0) => new() { Minimum = min, Maximum = max, Value = value, Width = 72 };
    static FlowLayoutPanel Flow() => new() { AutoSize = true, Dock = DockStyle.Top, WrapContents = true, Margin = new(0, 2, 0, 2) };
    static FlowLayoutPanel Stack() => new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
    static void AddNumber(FlowLayoutPanel panel, string title, NumericUpDown number, string note)
    {
        var row = Flow(); var label = Label(title); label.AutoSize = false; label.Width = 170;
        var help = Label(note); help.MaximumSize = new(290, 0);
        row.Controls.AddRange([label, number, help]); panel.Controls.Add(row);
    }
    void AddAction(FlowLayoutPanel panel, string text, Action action) { var button = Button(text); button.Click += (_, _) => SafeAction(action); panel.Controls.Add(button); }
    static void AddMeter(FlowLayoutPanel panel, string title, AxisTrack track, Label label)
    {
        var heading = Label(title); heading.Font = new(heading.Font, FontStyle.Bold); panel.Controls.Add(heading);
        track.Width = 450; panel.Controls.Add(track); label.MaximumSize = new(460, 0); panel.Controls.Add(label);
    }
    void SafeAction(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "VRudders", MessageBoxButtons.OK, MessageBoxIcon.Information); } }
    void RestoreWindow() { Show(); WindowState = FormWindowState.Normal; Activate(); tray.Visible = false; }

    void RefreshDevices(string? preferred = null, bool reconnect = false)
    {
        if (output != null) return;
        var list = Joysticks.Enumerate().Where(d => !d.IsVirtual).ToList();
        var matches = list.Where(d => d.Key == preferred).ToList();
        Joystick? choice = matches.Count == 1 ? matches[0] : null;
        if (!reconnect && string.IsNullOrEmpty(preferred)) choice = list.FirstOrDefault(d => d.Vendor == 0x10F5 && d.Product == 0x7012) ?? list.FirstOrDefault();
        if (reconnect && choice == null) return;
        loading = true; devices.DataSource = list; devices.SelectedItem = choice; loading = false;
        SelectDevice();
        if (choice == null) connection.Text = list.Count == 0 ? "No controllers found. Connect pedals in PC mode, then Refresh." : "Choose your controller; the previous selection is absent or ambiguous.";
        else if (reconnect) connection.Text = "Pedals reconnected. Verify movement, then Start forwarding when ready.";
    }

    void SelectDevice()
    {
        StopForwarding(); reader?.Dispose(); reader = null; selected = devices.SelectedItem as Joystick;
        loading = true; axes.DisplayMember = "Name"; axes.DataSource = selected?.Axes ?? [];
        axes.SelectedItem = selected?.Axes.FirstOrDefault(a => a.Name == axisPreference) ?? selected?.Axes.FirstOrDefault(a => a.Name == "Z") ?? selected?.Axes.FirstOrDefault(); loading = false;
        SelectAxis();
        if (selected == null || axis == null) { connection.Text = selected?.InputError ?? "Select a controller with a usable axis."; return; }
        try
        {
            reader = selected.OpenReader(); disconnected = false; reconnectKey = selected.Key;
            connection.Text = $"{selected.Name} · {selected.Backend} · {axis.Name} → virtual Z";
        }
        catch (Exception ex) { InputLost("Input unavailable: " + ex.Message); }
    }

    void SelectAxis()
    {
        StopForwarding(); axis = axes.SelectedItem as Axis; axisPreference = axis?.Name ?? "Z";
        minSeen = uint.MaxValue; maxSeen = 0; recent.Clear(); lastRead = 0;
        capturedCenter = capturedLeft = capturedRight = null; inputTrack.Active = processedTrack.Active = false; HideLivePreview();
        UpdateCalibrationStatus(); UpdateLocks();
    }

    void ReloadProfiles(string id)
    {
        loading = true;
        profiles.DataSource = new[] { FlightProfile.Baseline }.Concat(settings.Profiles).ToList();
        profiles.SelectedItem = ((List<FlightProfile>)profiles.DataSource).FirstOrDefault(p => p.Id == id) ?? FlightProfile.Baseline;
        loading = false; LoadProfile(CurrentProfile);
    }

    void LoadProfile(FlightProfile profile)
    {
        StopForwarding(); loading = true; var t = profile.Tuning;
        profileName.Text = profile.Name; bypass.Checked = t.Bypass; reverse.Checked = t.Reverse; useCalibration.Checked = t.UseCalibration;
        linkedEnds.Checked = false; dead.Value = (decimal)(t.CenterDeadZone * 100); leftEnd.Value = (decimal)(t.LeftEndZone * 100); rightEnd.Value = (decimal)(t.RightEndZone * 100); linkedEnds.Checked = leftEnd.Value == rightEnd.Value;
        curve.Value = (decimal)(t.Curve * 100); sensitivity.Value = (decimal)(t.Sensitivity * 100); smoothing.Value = (decimal)t.SmoothingMs;
        precision.Checked = t.PrecisionEnabled; precisionCurve.Value = (decimal)(t.PrecisionCurve * 100); precisionKey.SelectedItem = "F" + (t.PrecisionKey - 0x6F); trim.Checked = t.TrimEnabled;
        active = t; dirty = false; loading = false; processor.Reset(); UpdateLocks();
    }

    Tuning Draft() => new()
    {
        Bypass = bypass.Checked, Reverse = reverse.Checked, UseCalibration = useCalibration.Checked,
        CenterDeadZone = (double)dead.Value / 100, LeftEndZone = (double)leftEnd.Value / 100, RightEndZone = (double)rightEnd.Value / 100,
        Curve = (double)curve.Value / 100, Sensitivity = (double)sensitivity.Value / 100, SmoothingMs = (double)smoothing.Value,
        PrecisionEnabled = precision.Checked, PrecisionCurve = (double)precisionCurve.Value / 100,
        PrecisionKey = int.TryParse((precisionKey.SelectedItem as string)?[1..], out int f) ? 0x6F + f : 0x77,
        TrimEnabled = trim.Checked
    };

    void DraftChanged()
    {
        if (loading) return;
        dirty = Draft() != active || profileName.Text.Trim() != CurrentProfile.Name;
        processor.Reset(); UpdateLocks();
    }

    void UpdateLocks()
    {
        bool running = output != null;
        devices.Enabled = axes.Enabled = profiles.Enabled = refresh.Enabled = profileActions.Enabled = calibrationActions.Enabled = !running;
        clearCalibration.Enabled = !running && DeviceCalibration != null;
        profileName.ReadOnly = Protected;
        tuningControls.Enabled = !running && !Protected;
        foreach (var control in new Control[] { useCalibration, dead, leftEnd, rightEnd, linkedEnds, curve, sensitivity, smoothing, precision, precisionKey, precisionCurve, trim }) control.Enabled = !bypass.Checked;
        rightEnd.Enabled = !bypass.Checked && !linkedEnds.Checked;
        apply.Enabled = !running && !Protected && dirty;
        start.Enabled = running || (reader != null && axis != null && !disconnected && !dirty);
        start.BackColor = !start.Enabled ? Color.FromArgb(125, 139, 152) : running ? Color.FromArgb(180, 35, 50) : Color.FromArgb(20, 120, 70);
        start.ForeColor = Color.White;
        start.FlatAppearance.MouseOverBackColor = running ? Color.FromArgb(155, 27, 40) : Color.FromArgb(16, 101, 58);
        forwardingState.Text = running ? "OUTPUT IS LIVE" : dirty ? "APPLY YOUR CHANGES" : start.Enabled ? "READY TO START" : "WAITING FOR INPUT";
        forwardingState.ForeColor = running ? Color.FromArgb(180, 35, 50) : Color.FromArgb(42, 66, 82);
        forwardingHint.Text = running ? "Pedals are driving virtual yaw.\nStop sends yaw back to center."
            : dirty ? "Apply & save, or reset your draft, to enable Start."
            : start.Enabled ? "Send your pedals to the virtual yaw axis.\nKeep VRudders open while flying."
            : "Select your pedals and axis, then verify movement.";
        trimActions.Enabled = running && !active.Bypass && active.TrimEnabled;
        editStatus.Text = Protected ? "Protected original response. Duplicate this profile to tune it." : dirty ? "Previewing unsaved edits · Apply & save before flying." : "Saved profile · ready to fly.";
    }

    void ValidateCalibration(Tuning tuning)
    {
        tuning.Validate();
        if (!tuning.Bypass && tuning.UseCalibration)
        {
            if (axis == null || DeviceCalibration == null) throw new InvalidDataException("Capture and save calibration for this device/axis first, or turn off calibration.");
            DeviceCalibration.Validate(axis);
        }
    }

    void ApplyProfile()
    {
        if (output != null || Protected) return;
        var tuning = Draft(); ValidateCalibration(tuning);
        var profile = CurrentProfile with { Name = profileName.Text.Trim(), Tuning = tuning }; profile.Validate();
        int index = settings.Profiles.FindIndex(p => p.Id == profile.Id); var old = settings.Profiles[index];
        settings.Profiles[index] = profile;
        try { Persist(); } catch { settings.Profiles[index] = old; throw; }
        ReloadProfiles(profile.Id); connection.Text = "Profile applied and saved. Verify the preview before forwarding.";
    }

    void NewProfile() => AddProfile(new(Guid.NewGuid().ToString("D"), UniqueName("New flight profile"), new()));
    void DuplicateProfile() => AddProfile(new(Guid.NewGuid().ToString("D"), UniqueName(CurrentProfile.Name + " copy"), Draft()));
    string UniqueName(string stem)
    {
        stem = stem[..Math.Min(stem.Length, 70)]; string name = stem; int n = 2;
        while (settings.Profiles.Any(p => p.Name == name)) name = stem + " " + n++;
        return name;
    }
    void AddProfile(FlightProfile profile)
    {
        profile.Validate(); settings.Profiles.Add(profile);
        try { Persist(); } catch { settings.Profiles.Remove(profile); throw; }
        ReloadProfiles(profile.Id);
    }
    void ImportProfile()
    {
        using var dialog = new OpenFileDialog { Filter = "VRudders profile (*.json)|*.json", Title = "Import flight profile" };
        if (dialog.ShowDialog(this) == DialogResult.OK) AddProfile(SettingsStore.Import(dialog.FileName));
    }
    void ExportProfile()
    {
        using var dialog = new SaveFileDialog { Filter = "VRudders profile (*.json)|*.json", FileName = "VRudders-profile.json" };
        if (dialog.ShowDialog(this) == DialogResult.OK) SettingsStore.Export(dialog.FileName, CurrentProfile);
    }
    void Persist()
    {
        if (!settingsReadable) throw new IOException("The existing settings file could not be read. Move or repair it before saving, so it is not overwritten: " + SettingsStore.FilePath);
        settings.DeviceKey = selected?.Key ?? settings.DeviceKey; settings.Axis = axis?.Name ?? settings.Axis; settings.SelectedProfile = CurrentProfile.Id;
        SettingsStore.Save(settings);
    }

    void CapturePoint(int step)
    {
        if (output != null || axis == null || reader == null || clock.ElapsedMilliseconds - lastRead > 200 || recent.Count < 5) return;
        var samples = recent.Order().ToArray(); uint value = samples[samples.Length / 2];
        if (step == 0) capturedCenter = value; else if (step == 1) capturedLeft = value; else capturedRight = value;
        UpdateCalibrationStatus();
    }
    void SaveCalibration()
    {
        if (output != null || axis == null) return;
        if (capturedCenter == null || capturedLeft == null || capturedRight == null) throw new InvalidDataException("Capture center, left stop, and right stop first.");
        var calibration = new Calibration(Math.Min(capturedLeft.Value, capturedRight.Value), capturedCenter.Value, Math.Max(capturedLeft.Value, capturedRight.Value));
        calibration.Validate(axis);
        var previous = DeviceCalibration; settings.Calibrations[CalibrationKey] = calibration;
        try { Persist(); } catch { if (previous == null) settings.Calibrations.Remove(CalibrationKey); else settings.Calibrations[CalibrationKey] = previous; throw; }
        processor.Reset(); UpdateCalibrationStatus();
        if (capturedLeft > capturedRight) calibrationStatus.Text += "\nYour physical left direction has higher values; use Reverse yaw direction if needed.";
    }
    void UpdateCalibrationStatus()
    {
        string captured = $"Captured center: {capturedCenter?.ToString("N0") ?? "—"} · left: {capturedLeft?.ToString("N0") ?? "—"} · right: {capturedRight?.ToString("N0") ?? "—"}";
        calibrationStatus.Text = $"Device: {selected?.Name ?? "none"} · axis {axis?.Name ?? "none"}\n{captured}\n" + (DeviceCalibration is { } c ? $"Saved range: {c.Minimum:N0} ← {c.Center:N0} → {c.Maximum:N0}" : "No calibration saved for this device and axis.");
    }

    void StartForwarding()
    {
        if (dirty || selected == null || axis == null || reader == null || disconnected) return;
        ValidateCalibration(active);
        if (clock.ElapsedMilliseconds - lastRead > 200) throw new IOException("Wait for live physical input before starting.");
        virtualDevice = Joysticks.Enumerate().SingleOrDefault(d => d.IsVirtual) ?? throw new IOException("Virtual controller not found. Install the signed VRudders driver, then Refresh.");
        if (settingsReadable) Persist();
        processor.Reset(); trimLeftDown = trimRightDown = trimResetDown = false;
        output = VirtualOutput.Connect(); start.Text = "STOP"; UpdateLocks();
        connection.Text = $"Forwarding {selected.Name} {axis.Name} → {virtualDevice.Name} Z · {CurrentProfile.Name}";
    }
    void StopForwarding(string? reason = null)
    {
        bool wasForwarding = output != null;
        output?.Dispose(); output = null; virtualDevice = null; processor.Reset(); start.Text = "START";
        outputTrack.Active = false; outputTrack.Value = 0; outputStatus.Text = reason ?? (wasForwarding ? "Stopped · yaw resets to center" : "Not forwarding · connect to read virtual Z");
        UpdateLocks();
    }
    void InputLost(string message)
    {
        StopForwarding(message); reader?.Dispose(); reader = null;
        disconnected = true; reconnectKey = selected?.Key; nextReconnect = clock.ElapsedMilliseconds + 3000;
        recent.Clear(); lastRead = 0; inputTrack.Active = processedTrack.Active = false; HideLivePreview();
        connection.Text = message + " Reconnect detection is active; forwarding resumes manually."; UpdateLocks();
    }
    void Poll()
    {
        if (closing) return;
        long now = clock.ElapsedMilliseconds; double dt = (now - lastTick) / 1000.0; lastTick = now;
        if (disconnected && now >= nextReconnect)
        {
            nextReconnect = now + 3000;
            try { RefreshDevices(reconnectKey, true); } catch { /* Still absent; retain stopped state. */ }
        }
        if (reader == null || axis == null) return;
        if (output != null && dt > 0.25) StopForwarding("Stopped after an app pause. Verify input and restart when ready.");
        try
        {
            if (!reader.TryRead(out var p)) { InputLost("Physical input disconnected or unavailable."); return; }
            raw = p.Axis(axis.Index); lastRead = now; minSeen = Math.Min(raw, minSeen); maxSeen = Math.Max(raw, maxSeen);
            recent.Enqueue(raw); if (recent.Count > 9) recent.Dequeue();
            inputTrack.Active = true; inputTrack.Value = Joysticks.Normalize(raw, axis);
            inputStatus.Text = $"{axis.Name}: {raw:N0} · {inputTrack.Value:+0.0%;-0.0%;0.0%} · {(minSeen < maxSeen ? "Movement detected" : "Waiting for movement")}";
            var tuning = output != null ? active : Draft();
            Calibration? calibration = DeviceCalibration;
            bool calibrationValid = true;
            if (!tuning.Bypass && tuning.UseCalibration)
            {
                try { if (calibration == null) throw new InvalidDataException(); calibration.Validate(axis); }
                catch { calibrationValid = false; }
            }
            if (!calibrationValid) { processedTrack.Active = false; processedStatus.Text = "Save a valid calibration or turn calibration off."; HideLivePreview(); return; }
            bool held = output != null && !tuning.Bypass && tuning.PrecisionEnabled && IsKeyPressed(tuning.PrecisionKey);
            if (output != null && !tuning.Bypass && tuning.TrimEnabled)
            {
                TrimKey(0x78, ref trimLeftDown, () => processor.ChangeTrim(-0.01)); TrimKey(0x79, ref trimRightDown, () => processor.ChangeTrim(0.01)); TrimKey(0x7A, ref trimResetDown, processor.ResetTrim);
            }
            processed = processor.Process(raw, axis, tuning, calibration, dt, held);
            processedTrack.Active = true; processedTrack.Value = processed;
            processedStatus.Text = $"{processed:+0.0%;-0.0%;0.0%} · {(tuning.Bypass ? "Linear bypass" : dirty ? "Draft preview" : "Tuned")} · Trim {processor.Trim:+0%;-0%;0%}" + (processor.PrecisionActive ? " · Precision" : "");
            graph.SetResponse(axis, tuning, calibration); graph.Input = inputTrack.Value; graph.Output = processed; graph.Live = true; graph.Invalidate();
            tuningGraph.SetResponse(axis, tuning, calibration); tuningGraph.Input = inputTrack.Value; tuningGraph.Output = processed; tuningGraph.Live = true; tuningGraph.Invalidate();
            if (output != null)
            {
                ushort z = Joysticks.ToVirtual(processed); output.Send(z);
                if (virtualDevice == null || !Joysticks.Matches(virtualDevice) || !Joysticks.TryRead(virtualDevice.Id, out var readback)) throw new IOException("Virtual Windows readback unavailable.");
                outputTrack.Active = true; outputTrack.Value = Joysticks.Normalize(readback.Z, virtualDevice.Axes.Single(a => a.Name == "Z"));
                outputStatus.Text = $"Sent Z {z:N0} · Windows Z {readback.Z:N0}";
            }
            UpdateLocks();
        }
        catch (Exception ex) { InputLost("Forwarding stopped: " + ex.Message); }
    }
    static void TrimKey(int key, ref bool previous, Action action) { bool pressed = IsKeyPressed(key); if (pressed && !previous) action(); previous = pressed; }
    void HideLivePreview() { graph.Live = tuningGraph.Live = false; graph.Invalidate(); tuningGraph.Invalidate(); }
    static bool IsKeyPressed(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
}
