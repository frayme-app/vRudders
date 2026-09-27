using System.Runtime.InteropServices;

namespace VRudders;

// Windows' built-in DirectInput8 API. No additional driver, package, or copied
// third-party implementation. The virtual output/readback remains unchanged.
internal sealed class DirectInputReader : IJoystickReader
{
    IDirectInput8? input;
    IDirectInputDevice8? device;
    public Axis[] Axes { get; private set; } = [];

    static IDirectInput8 CreateInput()
    {
        var iid = typeof(IDirectInput8).GUID;
        Check(DirectInput8Create(GetModuleHandle(null), 0x0800, ref iid, out var input, 0));
        return input;
    }

    public static List<Joystick> EnumerateVelocityOne()
    {
        var found = new List<DeviceInstance>();
        var input = CreateInput();
        try
        {
            DeviceCallback callback = (ref DeviceInstance info, nint _) =>
            {
                if (BitConverter.ToUInt32(info.Product.ToByteArray()) == 0x701210F5) found.Add(info);
                return 1;
            };
            Check(input.EnumDevices(4, callback, 0, 1)); // Game controllers, attached only.
            GC.KeepAlive(callback);
        }
        finally { Marshal.ReleaseComObject(input); }

        var result = new List<Joystick>();
        foreach (var info in found)
        {
            try
            {
                using var reader = Open(info.Instance);
                result.Add(new(uint.MaxValue, info.ProductName, 0x10F5, 0x7012, reader.Axes, info.Instance));
            }
            catch (Exception ex)
            {
                result.Add(new(uint.MaxValue, info.ProductName, 0x10F5, 0x7012, [], info.Instance, ex.Message));
            }
        }
        return result;
    }

    public static DirectInputReader Open(Guid instance)
    {
        var reader = new DirectInputReader();
        try
        {
            reader.input = CreateInput();
            Check(reader.input.CreateDevice(ref instance, out reader.device, 0));
            var objects = new List<ObjectInstance>();
            ObjectCallback callback = (ref ObjectInstance info, nint _) =>
            {
                if (info.UsagePage == 1 && info.Usage >= 0x30 && info.Usage <= 0x35 && (info.Type & 3) == 2) objects.Add(info);
                return 1;
            };
            Check(reader.device.EnumObjects(callback, 0, 3));
            GC.KeepAlive(callback);
            if (objects.Count == 0 || objects.Select(o => o.Usage).Distinct().Count() != objects.Count)
                throw new IOException("No unambiguous absolute axes available from DirectInput.");

            var axes = new List<Axis>();
            int stride = Marshal.SizeOf<ObjectFormat>();
            nint formats = Marshal.AllocHGlobal(stride * objects.Count);
            try
            {
                for (int i = 0; i < objects.Count; i++)
                {
                    var o = objects[i];
                    int index = o.Usage - 0x30;
                    var range = new PropertyRange { Size = 24, HeaderSize = 16, Object = o.Type, How = 2 };
                    Check(reader.device.GetProperty(4, ref range)); // DIPROP_RANGE, DIPH_BYID.
                    if (range.Min < 0 || range.Max <= range.Min) throw new IOException("Unsupported physical axis range.");
                    axes.Add(new(new[] { "X", "Y", "Z", "R", "U", "V" }[index], (uint)range.Min, (uint)range.Max, index));
                    Marshal.StructureToPtr(new ObjectFormat { Offset = (uint)(index * 4), Type = o.Type }, formats + i * stride, false);
                }
                var format = new DataFormat { Size = (uint)Marshal.SizeOf<DataFormat>(), ObjectSize = (uint)stride, Flags = 1, DataSize = 24, ObjectCount = (uint)objects.Count, Objects = formats };
                Check(reader.device.SetDataFormat(ref format));
            }
            finally { Marshal.FreeHGlobal(formats); }
            Check(reader.device.SetCooperativeLevel(GetDesktopWindow(), 0xA)); // Background, nonexclusive.
            Check(reader.device.Acquire());
            reader.Axes = axes.OrderBy(a => a.Index).ToArray();
            return reader;
        }
        catch { reader.Dispose(); throw; }
    }

    public bool TryRead(out Joysticks.Position position)
    {
        position = default;
        if (device == null) return false;
        // Failure is surfaced immediately; never substitute a synthetic neutral
        // value or continue sending the previous sample after a disconnect.
        if (device.Poll() < 0 || device.GetDeviceState(24, out var state) < 0) return false;
        position = new() { X = state.X, Y = state.Y, Z = state.Z, R = state.R, U = state.U, V = state.V };
        return true;
    }

    public void Dispose()
    {
        if (device != null) { device.Unacquire(); Marshal.ReleaseComObject(device); device = null; }
        if (input != null) { Marshal.ReleaseComObject(input); input = null; }
    }

    static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }

    [DllImport("dinput8.dll", ExactSpelling = true)]
    static extern int DirectInput8Create(nint module, uint version, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IDirectInput8 input, nint outer);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")] static extern nint GetDesktopWindow();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DeviceCallback(ref DeviceInstance info, nint context);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ObjectCallback(ref ObjectInstance info, nint context);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DeviceInstance
    {
        public uint Size; public Guid Instance, Product; public uint Type;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string InstanceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ProductName;
        public Guid ForceFeedbackDriver; public ushort UsagePage, Usage;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ObjectInstance
    {
        public uint Size; public Guid TypeGuid; public uint Offset, Type, Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        public uint MaxForce, ForceResolution; public ushort Collection, Designator, UsagePage, Usage;
        public uint Dimension; public ushort Exponent, ReportId;
    }
    [StructLayout(LayoutKind.Sequential)] struct PropertyRange { public uint Size, HeaderSize, Object, How; public int Min, Max; }
    [StructLayout(LayoutKind.Sequential)] struct ObjectFormat { public nint Guid; public uint Offset, Type, Flags; }
    [StructLayout(LayoutKind.Sequential)] struct DataFormat { public uint Size, ObjectSize, Flags, DataSize, ObjectCount; public nint Objects; }
    [StructLayout(LayoutKind.Sequential)] struct State { public uint X, Y, Z, R, U, V; }

    [ComImport, Guid("BF798031-483A-4DA2-AA99-5D64ED369700"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirectInput8
    {
        [PreserveSig] int CreateDevice(ref Guid instance, [MarshalAs(UnmanagedType.Interface)] out IDirectInputDevice8 device, nint outer);
        [PreserveSig] int EnumDevices(uint type, [MarshalAs(UnmanagedType.FunctionPtr)] DeviceCallback callback, nint context, uint flags);
    }

    [ComImport, Guid("54D41081-DC15-4833-A41B-748F73A38179"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDirectInputDevice8
    {
        // Unused methods retain their native vtable slots. Only the explicitly
        // typed methods are invoked. See the Windows SDK dinput.h interface.
        [PreserveSig] int GetCapabilities();
        [PreserveSig] int EnumObjects([MarshalAs(UnmanagedType.FunctionPtr)] ObjectCallback callback, nint context, uint flags);
        [PreserveSig] int GetProperty(nint property, ref PropertyRange range);
        [PreserveSig] int SetProperty();
        [PreserveSig] int Acquire();
        [PreserveSig] int Unacquire();
        [PreserveSig] int GetDeviceState(uint size, out State state);
        [PreserveSig] int GetDeviceData();
        [PreserveSig] int SetDataFormat(ref DataFormat format);
        [PreserveSig] int SetEventNotification();
        [PreserveSig] int SetCooperativeLevel(nint window, uint flags);
        [PreserveSig] int GetObjectInfo();
        [PreserveSig] int GetDeviceInfo();
        [PreserveSig] int RunControlPanel();
        [PreserveSig] int Initialize();
        [PreserveSig] int CreateEffect();
        [PreserveSig] int EnumEffects();
        [PreserveSig] int GetEffectInfo();
        [PreserveSig] int GetForceFeedbackState();
        [PreserveSig] int SendForceFeedbackCommand();
        [PreserveSig] int EnumCreatedEffectObjects();
        [PreserveSig] int Escape();
        [PreserveSig] int Poll();
    }
}
