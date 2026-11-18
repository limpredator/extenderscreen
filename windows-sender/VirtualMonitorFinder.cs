using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ExtendedScreen.Sender;

/// <summary>
/// Finds the actual virtual display driver's monitor by its adapter
/// identity (DeviceString == "Virtual Display Driver"), instead of
/// guessing "the first non-primary screen" — which breaks the moment
/// there's a second REAL monitor around (e.g. at the office, where the
/// desk monitor is the non-primary one and the virtual monitor had
/// somehow become primary). Confirmed via EnumDisplayDevices that multiple
/// monitor EDIDs can be listed under one adapter; only the one actually
/// ATTACHED_TO_DESKTOP is live.
/// </summary>
internal static class VirtualMonitorFinder
{
    private const int DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x1;
    private const int DISPLAY_DEVICE_PRIMARY_DEVICE = 0x4;
    private const string VddAdapterString = "Virtual Display Driver";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumDisplayDevicesW(string? lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    public readonly record struct Result(string DeviceName, bool IsPrimary, bool IsCurrentlyAttached);

    /// Returns the VDD adapter's GDI device name (e.g. "\\.\DISPLAY21") if
    /// one is currently attached to the desktop, else null (driver may be
    /// installed but the monitor not currently extended — caller should
    /// fall back to mirroring the primary screen in that case).
    public static Result? Find()
    {
        for (uint i = 0; i < 64; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevicesW(null, i, ref dd, 0)) break;

            if (!dd.DeviceString.Contains(VddAdapterString, StringComparison.OrdinalIgnoreCase)) continue;

            // The adapter identity is stable and shows up here whether or
            // not it's currently extended onto the desktop (confirmed live:
            // "\\.\DISPLAY21 str=Virtual Display Driver flags=0x0" while
            // fully detached) — so identify it unconditionally; the caller
            // decides what to do about geometry when it's not attached
            // (Screen.AllScreens won't have an entry for it in that case).
            bool attached = (dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) != 0;
            bool isPrimary = (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) != 0;
            return new Result(dd.DeviceName, isPrimary, attached);
        }
        return null;
    }

    /// Resolves a found device name to its current Bounds via WinForms'
    /// Screen class (needed for width/height/offset — EnumDisplayDevices
    /// doesn't give geometry, only identity).
    public static Screen? ResolveScreen(string deviceName) =>
        Screen.AllScreens.FirstOrDefault(s => s.DeviceName == deviceName);
}
