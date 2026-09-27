using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VRudders;

internal record Axis(string Name, uint Min, uint Max, int Index);
internal record Joystick(uint Id, string Name, ushort Vendor, ushort Product, Axis[] Axes, Guid? DirectInputId = null, string? InputError = null)
{
    public bool IsVirtual => Vendor == 0xDEED && Product == 0xFEED && IsVirtualName(Name);
    public static bool IsVirtualName(string name) => name.Equals("VRudders Yaw", StringComparison.OrdinalIgnoreCase) || name.Equals("VRudders POC", StringComparison.OrdinalIgnoreCase);
    public string Backend => DirectInputId.HasValue ? "DirectInput" : "WinMM";
    public string Key => DirectInputId is Guid guid ? $"di:{guid:D}" : $"winmm:{Vendor:X4}:{Product:X4}:{Name}";
    public IJoystickReader OpenReader() => DirectInputId is Guid guid ? DirectInputReader.Open(guid) : new WinMmReader(this);
    public override string ToString() => $"{Name}  [{Backend}, {Vendor:X4}:{Product:X4}]";
}

internal interface IJoystickReader : IDisposable
{
    bool TryRead(out Joysticks.Position position);
}

internal sealed class WinMmReader(Joystick joystick) : IJoystickReader
{
    public bool TryRead(out Joysticks.Position position)
    {
        position = default;
        return Joysticks.Matches(joystick) && Joysticks.TryRead(joystick.Id, out position);
    }
    public void Dispose() { }
}

internal static class Joysticks
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Caps
    {
        public ushort Mid, Pid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        public uint Xmin, Xmax, Ymin, Ymax, Zmin, Zmax, Buttons, PeriodMin, PeriodMax;
        public uint Rmin, Rmax, Umin, Umax, Vmin, Vmax, Capabilities, MaxAxes, NumAxes, MaxButtons;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string RegKey;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Oem;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct Position
    {
        public uint Size, Flags, X, Y, Z, R, U, V, Buttons, ButtonNumber, Pov, Reserved1, Reserved2;
        public uint Axis(int index) => index switch { 0 => X, 1 => Y, 2 => Z, 3 => R, 4 => U, 5 => V, _ => throw new ArgumentOutOfRangeException(nameof(index)) };
    }
    [DllImport("winmm.dll")] private static extern uint joyGetNumDevs();
    [DllImport("winmm.dll", EntryPoint = "joyGetDevCapsW", CharSet = CharSet.Unicode)] private static extern uint GetCaps(nuint id, out Caps caps, uint size);
    [DllImport("winmm.dll")] private static extern uint joyGetPosEx(uint id, ref Position position);

    public static List<Joystick> Enumerate()
    {
        var devices = new List<Joystick>();
        for (uint id = 0; id < joyGetNumDevs(); id++)
        {
            if (GetCaps(id, out var c, (uint)Marshal.SizeOf<Caps>()) != 0 || !TryRead(id, out _)) continue;
            var axes = new List<Axis>();
            if (c.NumAxes >= 1 && c.Xmax > c.Xmin) axes.Add(new("X", c.Xmin, c.Xmax, 0));
            if (c.NumAxes >= 2 && c.Ymax > c.Ymin) axes.Add(new("Y", c.Ymin, c.Ymax, 1));
            if ((c.Capabilities & 1) != 0) axes.Add(new("Z", c.Zmin, c.Zmax, 2));
            if ((c.Capabilities & 2) != 0) axes.Add(new("R", c.Rmin, c.Rmax, 3));
            if ((c.Capabilities & 4) != 0) axes.Add(new("U", c.Umin, c.Umax, 4));
            if ((c.Capabilities & 8) != 0) axes.Add(new("V", c.Vmin, c.Vmax, 5));
            devices.Add(new(id, FriendlyName(id, c), c.Mid, c.Pid, axes.ToArray()));
        }
        // Some VelocityOne installations expose valid HID/DirectInput axes but zero
        // WinMM axes and constant-zero positions. Never fabricate X/Y or force a Z
        // entry on that broken reader: zero would command full-left yaw.
        var affected = devices.Where(d => d.Vendor == 0x10F5 && d.Product == 0x7012 && !d.Axes.Any(a => a.Index == 2)).ToList();
        if (affected.Count != 0 || !devices.Any(d => d.Vendor == 0x10F5 && d.Product == 0x7012))
        {
            try
            {
                var fallback = DirectInputReader.EnumerateVelocityOne();
                devices.RemoveAll(d => affected.Contains(d));
                foreach (var d in fallback) devices.Add(d);
                if (fallback.Count == 0) devices.AddRange(affected.Select(d => d with { Axes = [], InputError = "Windows reports no yaw axis. Check PC mode and reconnect, then Refresh." }));
            }
            catch (Exception ex)
            {
                foreach (var d in affected) devices[devices.IndexOf(d)] = d with { Axes = [], InputError = "DirectInput unavailable: " + ex.Message };
            }
        }
        return devices;
    }
    private static string FriendlyName(uint id, Caps c)
    {
        const string root = @"System\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick";
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(root + $@"\OEM\VID_{c.Mid:X4}&PID_{c.Pid:X4}");
            if (key?.GetValue("OEMName") is string name) return name;
        }
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        foreach (string suffix in new[] { @"\CurrentJoystickSettings", $@"\{c.RegKey}\CurrentJoystickSettings" })
        {
            using var settings = hive.OpenSubKey(root + suffix);
            if (settings?.GetValue($"Joystick{id + 1}OEMName") is not string oem) continue;
            foreach (var oemHive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = oemHive.OpenSubKey(root + @"\OEM\" + oem);
                if (key?.GetValue("OEMName") is string name) return name;
            }
        }
        return c.Name;
    }
    public static bool TryRead(uint id, out Position position)
    {
        position = new() { Size = (uint)Marshal.SizeOf<Position>(), Flags = 0xFF };
        return joyGetPosEx(id, ref position) == 0;
    }
    public static bool Matches(Joystick d) => GetCaps(d.Id, out var c, (uint)Marshal.SizeOf<Caps>()) == 0 && c.Mid == d.Vendor && c.Pid == d.Product;
    public static double Normalize(uint raw, Axis axis) => axis.Max <= axis.Min ? 0 : Math.Clamp(((double)raw - axis.Min) / (axis.Max - axis.Min) * 2 - 1, -1, 1);
    public static ushort ToVirtual(double value) => (ushort)Math.Clamp(Math.Round((value + 1) * 32767.5), 0, 65535);
}
