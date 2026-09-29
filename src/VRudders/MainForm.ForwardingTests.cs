namespace VRudders;

internal sealed partial class MainForm
{
    // Exercises the actual Start/Poll/Stop path with a fake clock and controllers.
    // No sleeps, physical input, installed driver, or virtual controller writes.
    internal static void VerifyForwardingWorkflow()
    {
        static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Forwarding regression: " + message);
        }

        var backend = new TestControllers();
        using var form = new MainForm(backend) { Opacity = 0, ShowInTaskbar = false };
        try
        {
            form.timer.Stop(); form.Show();
            form.RefreshDevices(); form.ReloadProfiles(FlightProfile.BaselineId);
            void Tick(long milliseconds = 16) { backend.Now += milliseconds; form.Poll(); }
            Tick();
            Check(form.inputTrack.Active, "physical preview is available before Start");

            // Simulate both device discovery and HID connection exceeding the pause limit.
            backend.EnumerationDelay = 600; backend.ConnectionDelay = 450;
            form.StartForwarding(); Tick();
            Check(form.output != null && form.outputTrack.Active && form.start.Text == "STOP",
                "slow connection must survive the first update and reach Windows readback");
            Check(backend.LastOutput is { ActiveWrites: 1, LastZ: 49152 },
                "first output uses fresh input from after connection, not the earlier preview");
            for (int i = 0; i < 20; i++) Tick();
            Check(form.output != null && backend.LastOutput!.ActiveWrites == 21, "normal forwarding continues");

            Tick(251);
            Check(form.output == null && !form.outputTrack.Active && backend.LastOutput is { Disposed: true, LastZ: 32768 },
                "a real pause after starting still stops and centers output");
            Check(form.outputStatus.Text.Contains("app pause"), "real pause has an explanation");
            Tick(); Check(form.output == null, "a pause never resumes output automatically");

            // A second Start must discard stopped time and its own new connection delay too.
            Tick(5000); form.StartForwarding(); Tick();
            Check(form.output != null && form.outputTrack.Active, "manual restart after a pause works");
            form.StopForwarding();
            Check(backend.LastOutput is { Disposed: true, LastZ: 32768 }, "manual Stop centers and releases output");

            Tick(); backend.LoseInputOnConnect = true;
            bool rejected = false;
            try { form.StartForwarding(); } catch (IOException) { rejected = true; }
            Check(rejected && form.output == null && !form.outputTrack.Active &&
                backend.LastOutput is { Disposed: true, ActiveWrites: 0 },
                "input lost during connection cannot start output and releases the pending handle");
            Tick(); Check(form.disconnected && form.reader == null, "input loss enters reconnect handling");
        }
        finally { form.Close(); }
    }

    sealed class TestControllers : ControllerBackend
    {
        public long Now = 1000, EnumerationDelay, ConnectionDelay;
        public bool InputAvailable = true, LoseInputOnConnect;
        public uint RawZ = 32768;
        public TestYawOutput? LastOutput;
        readonly Joystick physical = new(0, "Test pedals", 0x1234, 0x5678, [new("Z", 0, 65535, 2)]);
        readonly Joystick virtualJoystick = new(1, "VRudders Yaw", 0xDEED, 0xFEED, [new("Z", 0, 65535, 2)]);
        public override long NowMilliseconds => Now;
        public override List<Joystick> Enumerate() { Now += EnumerationDelay; return [physical, virtualJoystick]; }
        public override IJoystickReader OpenReader(Joystick device) => new TestReader(this);
        public override IYawOutput Connect()
        {
            Now += ConnectionDelay; RawZ = 49152;
            if (LoseInputOnConnect) InputAvailable = false;
            return LastOutput = new TestYawOutput();
        }
        public override bool TryReadback(Joystick device, out Joysticks.Position position)
        {
            position = new() { Z = LastOutput?.LastZ ?? 32768 };
            return LastOutput is { Disposed: false };
        }
    }
    sealed class TestReader(TestControllers backend) : IJoystickReader
    {
        public bool TryRead(out Joysticks.Position position)
        {
            position = new() { Z = backend.RawZ }; return backend.InputAvailable;
        }
        public void Dispose() { }
    }
    sealed class TestYawOutput : IYawOutput
    {
        public ushort LastZ = 32768;
        public int ActiveWrites;
        public bool Disposed;
        public void Send(ushort z, bool active = true) { LastZ = z; if (active) ActiveWrites++; }
        public void Dispose() { LastZ = 32768; Disposed = true; }
    }
}
