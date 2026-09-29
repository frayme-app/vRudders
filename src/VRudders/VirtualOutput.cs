using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace VRudders;

internal sealed class VirtualOutput : IYawOutput
{
    private readonly SafeFileHandle handle;
    private VirtualOutput(SafeFileHandle handle) => this.handle = handle;
    [StructLayout(LayoutKind.Sequential)] private struct InterfaceData { public int Size; public Guid Class; public int Flags; public nuint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct Attributes { public int Size; public ushort Vendor, Product, Version; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] text, int size);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] report, int length);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid guid, string? enumerator, nint hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(nint set, nint info, ref Guid guid, uint index, ref InterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint set, ref InterfaceData data, nint detail, uint size, out uint required, nint info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(nint set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFile(string path, uint access, uint share, nint security, uint creation, uint flags, nint template);

    public static VirtualOutput Connect()
    {
        HidD_GetHidGuid(out var guid);
        nint set = SetupDiGetClassDevs(ref guid, null, 0, 0x12);
        if (set == -1) throw new Win32Exception();
        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new InterfaceData { Size = Marshal.SizeOf<InterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref guid, i, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception();
                }
                SetupDiGetDeviceInterfaceDetail(set, ref data, 0, 0, out uint size, 0);
                nint detail = Marshal.AllocHGlobal((int)size);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, size, out _, 0)) continue;
                    string path = Marshal.PtrToStringUni(detail + 4)!;
                    // Root-enumerated virtual HID paths need not contain their VID/PID.
                    // Read identity without requesting access to another device's input.
                    using var identity = CreateFile(path, 0, 3, 0, 3, 0, 0);
                    if (identity.IsInvalid) continue;
                    var a = new Attributes { Size = Marshal.SizeOf<Attributes>() };
                    var text = new byte[256];
                    if (HidD_GetAttributes(identity, ref a) && a.Vendor == 0xDEED && a.Product == 0xFEED &&
                        HidD_GetProductString(identity, text, text.Length) && Joystick.IsVirtualName(Encoding.Unicode.GetString(text).TrimEnd('\0')))
                    {
                        var h = CreateFile(path, 0xC0000000, 3, 0, 3, 0, 0);
                        if (!h.IsInvalid) return new(h);
                        h.Dispose();
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "The virtual device was found but cannot be opened for forwarding");
                    }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        throw new IOException("VRudders virtual driver not found. The physical monitor still works; install the signed driver to enable forwarding.");
    }
    public static byte[] Encode(ushort z, bool active) => [2, 1, (byte)z, (byte)(z >> 8), active ? (byte)1 : (byte)0];
    internal bool TryReport(byte[] report) => HidD_SetFeature(handle, report, report.Length);
    public void Send(ushort z, bool active = true)
    {
        var report = Encode(z, active);
        if (!HidD_SetFeature(handle, report, report.Length)) throw new Win32Exception(Marshal.GetLastWin32Error(), "Virtual axis write failed");
    }
    public void Dispose()
    {
        try { if (!handle.IsClosed && !handle.IsInvalid) Send(32768, false); } catch { }
        handle.Dispose();
    }
}
