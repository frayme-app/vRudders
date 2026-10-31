namespace VRudders;

// An immutable handoff to the window. The forwarding thread never waits for UI work.
internal sealed record ForwardingSnapshot(bool Forwarding = false, bool InputAvailable = false,
    uint Raw = 32768, double Processed = 0, double Trim = 0, bool PrecisionActive = false,
    ushort? SentZ = null, uint? ReadbackZ = null, double Readback = 0,
    long LastReadMilliseconds = 0, string? StopReason = null, string? InputError = null);

// All methods except Snapshot are called on one owning thread, including COM/DirectInput I/O.
internal sealed class ForwardingSession(ControllerBackend controllers) : IDisposable
{
    readonly YawProcessor processor = new();
    ForwardingSnapshot snapshot = new();
    IJoystickReader? reader;
    IYawOutput? output;
    Axis? axis;
    Joystick? virtualDevice;
    Tuning tuning = new();
    Calibration? calibration;
    long lastTick = controllers.NowMilliseconds;
    bool trimLeftDown, trimRightDown, trimResetDown;
    public ForwardingSnapshot Snapshot => Volatile.Read(ref snapshot);
    void Publish(ForwardingSnapshot value) => Volatile.Write(ref snapshot, value);

    public void SelectInput(Joystick? device, Axis? inputAxis)
    {
        Stop(); reader?.Dispose(); reader = null; axis = inputAxis;
        Publish(new()); lastTick = controllers.NowMilliseconds;
        if (device == null || axis == null) return;
        try { reader = controllers.OpenReader(device); Tick(); }
        catch (Exception ex) { LoseInput("Input unavailable: " + ex.Message); }
    }

    public void Start(Tuning settings, Calibration? deviceCalibration)
    {
        if (output != null) return;
        settings.Validate();
        if (!settings.Bypass && settings.UseCalibration)
        {
            if (axis == null || deviceCalibration == null) throw new IOException("Save a valid calibration first.");
            deviceCalibration.Validate(axis);
        }
        if (reader == null || axis == null || !Snapshot.InputAvailable ||
            controllers.NowMilliseconds - Snapshot.LastReadMilliseconds > 200)
            throw new IOException("Wait for live physical input before starting.");
        var device = controllers.Enumerate().SingleOrDefault(d => d.IsVirtual)
            ?? throw new IOException("Virtual controller not found. Install the signed VRudders driver, then Refresh.");
        IYawOutput? pending = controllers.Connect();
        try
        {
            if (!reader.TryRead(out var p))
                throw new IOException("Pedal input was lost while connecting. Check the pedals and try Start again.");
            tuning = settings; calibration = deviceCalibration; processor.Reset();
            trimLeftDown = trimRightDown = trimResetDown = false;
            output = pending; pending = null; virtualDevice = device;
            // Connection time is not an interruption during flight (beta.4 regression).
            lastTick = controllers.NowMilliseconds;
            Publish(new(Forwarding: true, InputAvailable: true, Raw: p.Axis(axis.Index),
                LastReadMilliseconds: lastTick));
        }
        finally { pending?.Dispose(); }
    }

    public void Stop(string? reason = null)
    {
        bool wasForwarding = output != null;
        output?.Dispose(); output = null; virtualDevice = null; processor.Reset();
        Publish(Snapshot with { Forwarding = false, Processed = 0, Trim = 0, PrecisionActive = false,
            SentZ = null, ReadbackZ = null, Readback = 0,
            StopReason = reason ?? (wasForwarding ? "Stopped · yaw resets to center" : null) });
    }

    void LoseInput(string reason)
    {
        Stop(reason); reader?.Dispose(); reader = null;
        Publish(Snapshot with { InputAvailable = false, LastReadMilliseconds = 0, InputError = reason });
    }
    internal void Fail(string reason) => LoseInput(reason);

    public void ChangeTrim(double amount)
    {
        if (output != null && !tuning.Bypass && tuning.TrimEnabled) processor.ChangeTrim(amount);
    }
    public void ResetTrim() => processor.ResetTrim();

    public void Tick()
    {
        long now = controllers.NowMilliseconds, elapsed = now - lastTick; lastTick = now;
        if (reader == null || axis == null) return;
        // This measures the input/output thread, never window painting or focus changes.
        // The existing driver watchdog also centers yaw when heartbeats cease.
        if (output != null && elapsed > 250)
            Stop($"Stopped after a forwarding pause ({elapsed} ms). Verify input and restart when ready.");
        try
        {
            if (!reader.TryRead(out var p)) { LoseInput("Physical input disconnected or unavailable."); return; }
            uint raw = p.Axis(axis.Index);
            if (output == null)
            {
                Publish(Snapshot with { InputAvailable = true, Raw = raw, LastReadMilliseconds = now });
                return;
            }
            bool held = !tuning.Bypass && tuning.PrecisionEnabled && controllers.IsKeyPressed(tuning.PrecisionKey);
            if (!tuning.Bypass && tuning.TrimEnabled)
            {
                TrimKey(0x78, ref trimLeftDown, () => processor.ChangeTrim(-0.01));
                TrimKey(0x79, ref trimRightDown, () => processor.ChangeTrim(0.01));
                TrimKey(0x7A, ref trimResetDown, processor.ResetTrim);
            }
            double processed = processor.Process(raw, axis, tuning, calibration, elapsed / 1000.0, held);
            ushort z = Joysticks.ToVirtual(processed); output.Send(z);
            if (virtualDevice == null || !controllers.TryReadback(virtualDevice, out var readback))
                throw new IOException("Virtual Windows readback unavailable.");
            Publish(new(true, true, raw, processed, processor.Trim, processor.PrecisionActive, z,
                readback.Z, Joysticks.Normalize(readback.Z, virtualDevice.Axes.Single(a => a.Name == "Z")), now));
        }
        catch (Exception ex) { LoseInput("Forwarding stopped: " + ex.Message); }
    }

    void TrimKey(int key, ref bool previous, Action action)
    {
        bool pressed = controllers.IsKeyPressed(key);
        if (pressed && !previous) action(); previous = pressed;
    }
    public void Dispose() { Stop(); reader?.Dispose(); reader = null; }
}
