using System.Diagnostics;

namespace VRudders;

internal sealed partial class MainForm
{
    static void CheckForwarding(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Forwarding regression: " + message);
    }

    // Real session code with simulated I/O/time; no installed driver is opened.
    internal static void VerifyForwardingWorkflow()
    {
        var backend = new TestControllers();
        using (var session = new ForwardingSession(backend))
        {
            session.SelectInput(backend.Physical, backend.Physical.Axes[0]);
            void Tick(long ms = 16) { backend.Now += ms; session.Tick(); }
            CheckForwarding(session.Snapshot.InputAvailable, "live physical preview before Start");
            backend.EnumerationDelay = 600; backend.ConnectionDelay = 450;
            session.Start(new(), null); Tick();
            CheckForwarding(session.Snapshot.Forwarding && session.Snapshot.ReadbackZ == 49152,
                "slow startup survives and uses fresh input from after connection");
            for (int i = 0; i < 20; i++) Tick();
            CheckForwarding(backend.LastOutput!.ActiveWrites == 21, "continued forwarding");
            Tick(251);
            CheckForwarding(!session.Snapshot.Forwarding && backend.LastOutput is { Disposed: true, LastZ: 32768 },
                "a real forwarding-thread pause stops and centers output");
            CheckForwarding(session.Snapshot.StopReason!.Contains("forwarding pause"), "pause explanation");
            Tick(); CheckForwarding(!session.Snapshot.Forwarding, "a real forwarding pause requires manual resume");
            Tick(5000); session.Start(new(), null); Tick();
            CheckForwarding(session.Snapshot.Forwarding, "manual restart discards stopped/connection time");
            session.Stop();
            CheckForwarding(backend.LastOutput is { Disposed: true, LastZ: 32768 }, "manual Stop centers output");
            Tick(); backend.LoseInputOnConnect = true;
            bool rejected = false;
            try { session.Start(new(), null); } catch (IOException) { rejected = true; }
            CheckForwarding(rejected && !session.Snapshot.Forwarding &&
                backend.LastOutput is { Disposed: true, ActiveWrites: 0 }, "input lost during connection releases pending output");
            Tick(); CheckForwarding(!session.Snapshot.InputAvailable && session.Snapshot.InputError != null, "input loss is reported");
        }
        backend = new();
        using (var session = new ForwardingSession(backend))
        {
            session.SelectInput(backend.Physical, backend.Physical.Axes[0]);
            foreach (bool reverse in new[] { false, true })
            {
                session.Start(new() { Reverse = reverse }, null);
                for (uint raw = 0; raw <= 65535; raw++)
                {
                    backend.RawZ = raw; backend.Now += 16; session.Tick();
                    CheckForwarding(session.Snapshot.Forwarding && backend.LastOutput!.LastZ == (reverse ? 65535 - raw : raw),
                        $"exact session passthrough at {raw}, reversed={reverse}");
                }
                session.Stop();
            }
        }
        VerifyBackgroundAndTray();
    }

    static void VerifyBackgroundAndTray()
    {
        var backend = new TestControllers { RealTime = true };
        using (var form = new MainForm(backend, startInTray: false) { Opacity = 0, ShowInTaskbar = false })
        {
            form.timer.Stop(); form.Show(); form.RefreshDevices(); form.ReloadProfiles(FlightProfile.BaselineId);
            form.Poll(); form.StartForwarding();
            CheckForwarding(SpinWait.SpinUntil(() => backend.LastOutput?.ActiveWrites >= 2, 3000), "background loop starts");
            int writes = backend.LastOutput!.ActiveWrites;
            // Deliberately block the UI longer than the old 250 ms pause threshold.
            Thread.Sleep(650);
            CheckForwarding(backend.LastOutput.ActiveWrites > writes + 3 && form.Forwarding,
                "UI thread stalled 650 ms without interrupting output");
            form.Poll(); CheckForwarding(form.Forwarding && form.outputTrack.Active, "UI resumes without stopping yaw");
            form.settings.MinimizeToTray = form.settings.CloseToTray = true;
            for (int i = 0; i < 3; i++)
            {
                form.OnDeactivate(EventArgs.Empty); form.WindowState = FormWindowState.Minimized;
                CheckForwarding(!form.Visible && form.tray.Visible, "minimize hides to tray");
                foreach (uint raw in new uint[] { 0, 32768, 65535 })
                {
                    backend.RawZ = raw;
                    CheckForwarding(SpinWait.SpinUntil(() => form.worker.Snapshot.ReadbackZ == raw, 3000),
                        "fresh input/output keeps following pedals while the UI is hidden and blocked");
                }
                form.RestoreWindow(); form.OnActivated(EventArgs.Empty); form.Poll();
                CheckForwarding(form.Visible && form.Forwarding && form.trayToggle.Text == "Stop forwarding",
                    "restore and focus changes preserve forwarding");
                form.Close();
                CheckForwarding(!form.IsDisposed && !form.Visible && form.Forwarding, "Close hides without stopping");
                form.RestoreWindow();
            }
            form.trayToggle.PerformClick();
            CheckForwarding(!form.Forwarding && backend.LastOutput is { Disposed: true, LastZ: 32768 }, "tray Stop centers output");
            form.trayToggle.PerformClick(); CheckForwarding(form.Forwarding, "tray Start resumes manually");
            form.RequestExit();
            CheckForwarding(backend.LastOutput is { Disposed: true, LastZ: 32768 }, "Exit closes worker and centers yaw");
        }
        backend = new() { RealTime = true };
        using (var form = new MainForm(backend, startInTray: true) { Opacity = 0, ShowInTaskbar = false })
        {
            form.timer.Stop(); form.Show(); form.RefreshDevices(); form.ReloadProfiles(FlightProfile.BaselineId);
            CheckForwarding(!form.Visible && form.tray.Visible && !form.Forwarding, "tray startup suppresses window and does not auto-forward");
            form.trayToggle.PerformClick(); CheckForwarding(form.Forwarding, "Start works without ever opening the window");
            backend.InputAvailable = false;
            CheckForwarding(SpinWait.SpinUntil(() => form.worker.Snapshot.InputError != null, 3000), "input loss detected without UI ticks");
            CheckForwarding(!form.Forwarding && backend.LastOutput is { Disposed: true, LastZ: 32768 }, "disconnect centers while hidden");
            form.Poll(); CheckForwarding(form.disconnected && !form.trayToggle.Enabled, "tray Start disabled after input loss");
            backend.InputAvailable = true; form.RefreshDevices(backend.Physical.Key, true); form.Poll();
            CheckForwarding(!form.disconnected && !form.Forwarding, "reconnection needs manual resume");
            form.StartForwarding(); backend.ReadbackAvailable = false;
            CheckForwarding(SpinWait.SpinUntil(() => form.worker.Snapshot.InputError != null, 3000), "readback failure detected in background");
            CheckForwarding(!form.Forwarding && backend.LastOutput is { Disposed: true, LastZ: 32768 }, "readback failure centers output");
            form.settings.CloseToTray = false; form.Close(); CheckForwarding(form.IsDisposed, "Close exits when close-to-tray is disabled");
        }
    }

    sealed class TestControllers : ControllerBackend
    {
        readonly Stopwatch clock = Stopwatch.StartNew();
        public long Now = 1000, EnumerationDelay, ConnectionDelay;
        public bool RealTime;
        public volatile bool InputAvailable = true, LoseInputOnConnect, ReadbackAvailable = true;
        public volatile uint RawZ = 32768;
        public volatile TestYawOutput? LastOutput;
        public readonly Joystick Physical = new(0, "Test pedals", 0x1234, 0x5678, [new("Z", 0, 65535, 2)]);
        readonly Joystick virtualJoystick = new(1, "VRudders Yaw", 0xDEED, 0xFEED, [new("Z", 0, 65535, 2)]);
        public override long NowMilliseconds => RealTime ? clock.ElapsedMilliseconds : Now;
        public override List<Joystick> Enumerate() { Now += EnumerationDelay; return [Physical, virtualJoystick]; }
        public override IJoystickReader OpenReader(Joystick device) => new TestReader(this);
        public override bool IsKeyPressed(int key) => false;
        public override IYawOutput Connect()
        {
            Now += ConnectionDelay; RawZ = 49152;
            if (LoseInputOnConnect) InputAvailable = false;
            return LastOutput = new TestYawOutput();
        }
        public override bool TryReadback(Joystick device, out Joysticks.Position position)
        {
            position = new() { Z = LastOutput?.LastZ ?? 32768 };
            return ReadbackAvailable && LastOutput is { Disposed: false };
        }
    }
    sealed class TestReader(TestControllers backend) : IJoystickReader
    {
        readonly int owner = Environment.CurrentManagedThreadId;
        public bool TryRead(out Joysticks.Position position)
        {
            CheckForwarding(Environment.CurrentManagedThreadId == owner, "reader used only on its owning thread");
            position = new() { Z = backend.RawZ }; return backend.InputAvailable;
        }
        public void Dispose() => CheckForwarding(Environment.CurrentManagedThreadId == owner, "reader disposed on its owning thread");
    }
    sealed class TestYawOutput : IYawOutput
    {
        readonly int owner = Environment.CurrentManagedThreadId;
        int lastZ = 32768, activeWrites;
        public ushort LastZ => (ushort)Volatile.Read(ref lastZ);
        public int ActiveWrites => Volatile.Read(ref activeWrites);
        public volatile bool Disposed;
        public void Send(ushort z, bool active = true)
        {
            CheckForwarding(Environment.CurrentManagedThreadId == owner, "output used only on its owning thread");
            Volatile.Write(ref lastZ, z); if (active) Interlocked.Increment(ref activeWrites);
        }
        public void Dispose()
        {
            CheckForwarding(Environment.CurrentManagedThreadId == owner, "output disposed on its owning thread");
            Volatile.Write(ref lastZ, 32768); Disposed = true;
        }
    }
}
