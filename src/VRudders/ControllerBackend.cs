using System.Diagnostics;
using System.Runtime.InteropServices;

namespace VRudders;

internal interface IYawOutput : IDisposable
{
    void Send(ushort z, bool active = true);
}

// Keep Windows I/O and the monotonic clock replaceable for hardware-free regressions.
internal class ControllerBackend
{
    readonly Stopwatch clock = Stopwatch.StartNew();
    public virtual long NowMilliseconds => clock.ElapsedMilliseconds;
    public virtual List<Joystick> Enumerate() => Joysticks.Enumerate();
    public virtual IJoystickReader OpenReader(Joystick device) => device.OpenReader();
    public virtual IYawOutput Connect() => VirtualOutput.Connect();
    public virtual bool IsKeyPressed(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    public virtual bool TryReadback(Joystick device, out Joysticks.Position position)
    {
        position = default;
        return Joysticks.Matches(device) && Joysticks.TryRead(device.Id, out position);
    }
}
