using System.Runtime.InteropServices;

namespace ExtendedScreen.Sender;

/// <summary>
/// Attaches/detaches a specific monitor from the Windows desktop at
/// runtime, so the virtual display only "exists" (and can receive the
/// mouse cursor / have windows dragged onto it) while a viewer is actually
/// connected — same experience as unplugging a real monitor's cable, rather
/// than a permanently-extended desktop with a dead second screen whenever
/// nothing's watching it.
/// </summary>
internal static class DisplayConfig
{
    private const int DM_POSITION = 0x00000020;
    private const int DM_PELSWIDTH = 0x00080000;
    private const int DM_PELSHEIGHT = 0x00100000;
    private const int DM_BITSPERPEL = 0x00040000;
    private const int DM_DISPLAYFREQUENCY = 0x00400000;
    private const int CDS_UPDATEREGISTRY = 0x00000001;
    private const int CDS_NORESET = 0x10000000;
    private const int DISP_CHANGE_SUCCESSFUL = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINTL { public int x; public int y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public POINTL dmPosition;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    private static extern int ChangeDisplaySettingsExSet(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, int dwflags, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsExW")]
    private static extern int ChangeDisplaySettingsExApply(IntPtr lpszDeviceNameNull, IntPtr lpDevModeNull, IntPtr hwnd, int dwflags, IntPtr lParam);

    /// Reattaches the device at the given position/resolution and extends
    /// the desktop onto it. Returns true on success.
    public static bool Attach(string deviceName, int width, int height, int x, int y, int frequency = 60)
    {
        var dm = new DEVMODE
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = (short)Marshal.SizeOf<DEVMODE>(),
            dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT | DM_BITSPERPEL | DM_DISPLAYFREQUENCY,
            dmPosition = new POINTL { x = x, y = y },
            dmPelsWidth = width,
            dmPelsHeight = height,
            dmBitsPerPel = 32,
            dmDisplayFrequency = frequency,
        };

        int r1 = ChangeDisplaySettingsExSet(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
        if (r1 != DISP_CHANGE_SUCCESSFUL) return false;

        int r2 = ChangeDisplaySettingsExApply(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        return r2 == DISP_CHANGE_SUCCESSFUL;
    }

    /// Detaches the device from the desktop entirely (not just "no signal"
    /// — Windows stops extending onto it, and anything on it moves back to
    /// the remaining screen, same as a real monitor being unplugged).
    ///
    /// Passing a NULL DEVMODE here (the technique MSDN itself documents for
    /// "remove this display") reports DISP_CHANGE_SUCCESSFUL but doesn't
    /// actually detach anything on this driver — confirmed empirically: the
    /// monitor stayed in Screen.AllScreens afterward. A DEVMODE with
    /// dmPelsWidth/Height explicitly zeroed is what actually works.
    public static bool Detach(string deviceName)
    {
        var dm = new DEVMODE
        {
            dmDeviceName = string.Empty,
            dmFormName = string.Empty,
            dmSize = (short)Marshal.SizeOf<DEVMODE>(),
            dmFields = DM_POSITION | DM_PELSWIDTH | DM_PELSHEIGHT,
            dmPosition = new POINTL(),
            dmPelsWidth = 0,
            dmPelsHeight = 0,
        };

        int r1 = ChangeDisplaySettingsExSet(deviceName, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
        if (r1 != DISP_CHANGE_SUCCESSFUL) return false;

        int r2 = ChangeDisplaySettingsExApply(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
        return r2 == DISP_CHANGE_SUCCESSFUL;
    }
}
