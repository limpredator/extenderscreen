using System.Windows.Forms;

namespace ExtendedScreen.Sender;

/// <summary>
/// Listens for Windows' WM_DISPLAYCHANGE notification (fired whenever any
/// display's resolution, position, scale, or orientation changes) via a
/// hidden native message window. Without this, moving/resizing the virtual
/// monitor while a viewer is connected desyncs ffmpeg's capture region from
/// the desktop (gdigrab has no way to pick up a new region without being
/// restarted) — confirmed live as visible frame corruption/ghosting — and
/// required manually exiting and relaunching the whole app to recover.
/// This lets SenderService restart just the capture, automatically.
/// </summary>
internal sealed class DisplayChangeWatcher : NativeWindow, IDisposable
{
    private const int WM_DISPLAYCHANGE = 0x007E;

    private readonly System.Windows.Forms.Timer _debounce;

    /// Fired once, after a short quiet period — dragging a display's
    /// position or applying a resolution change can fire WM_DISPLAYCHANGE
    /// more than once in quick succession, and restarting capture on every
    /// one of those would be wasteful (and could itself cause visible
    /// hiccups mid-drag).
    public event Action? DisplayChanged;

    public DisplayChangeWatcher()
    {
        CreateHandle(new CreateParams());
        _debounce = new System.Windows.Forms.Timer { Interval = 800 };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            DisplayChanged?.Invoke();
        };
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DISPLAYCHANGE)
        {
            _debounce.Stop();
            _debounce.Start();
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        _debounce.Dispose();
        if (Handle != IntPtr.Zero) DestroyHandle();
    }
}
