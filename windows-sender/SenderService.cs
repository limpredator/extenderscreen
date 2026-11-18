using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Windows.Forms;

namespace ExtendedScreen.Sender;

/// <summary>
/// The streaming pipeline. The TCP listener and the adb-reverse retry loop
/// run for as long as the service is started; ffmpeg capture itself only
/// runs while a viewer is actually connected — see OnClientConnected /
/// OnClientDisconnected — and the virtual monitor is attached/detached to
/// match, so it only exists on the Windows desktop while something is
/// actually watching it (same experience as unplugging a real monitor).
/// </summary>
public sealed class SenderService
{
    public event Action<string>? LogLine;
    public event Action<string>? StatusChanged;

    private readonly Options _options;
    private readonly object _captureLock = new();

    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private StreamHub? _hub;

    private Process? _ffmpeg;
    private CancellationTokenSource? _captureCts;
    private bool _usbWasConnected;

    public SenderService(Options options)
    {
        _options = options;
    }

    public bool IsRunning => _runTask is { IsCompleted: false };

    public void Start()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        Log($"Ready: {_options.Width}x{_options.Height} @ {_options.Fps}fps, {_options.BitrateKbps} kbps, port {_options.Port}");
        foreach (var ip in LocalIPs()) Log($"  LAN IP: {ip}");

        // Self-heal: if the app was previously killed abruptly (crash, Task
        // Manager, power loss) instead of exiting cleanly, the monitor can
        // be left attached from a prior session with no viewer actually
        // connected. Since we're only just starting — nobody's connected
        // yet — detach it now rather than leaving a stale extended display
        // until the next connect/disconnect cycle fixes it by accident.
        // Re-checks live rather than trusting _options, since the normal
        // idle state is "not attached" and this should stay silent then.
        if (VirtualMonitorFinder.Find() is { IsCurrentlyAttached: true, IsPrimary: false } stale)
        {
            if (DisplayConfig.Detach(stale.DeviceName)) Log($"Cleared stale attached monitor from a previous session ({stale.DeviceName})");
        }

        _hub = new StreamHub(_options.Width, _options.Height, _options.Fps);
        _hub.ClientConnected += OnClientConnected;
        _hub.ClientDisconnected += OnClientDisconnected;

        var acceptTask = Task.Run(() => AcceptLoop(_options.Port, _hub, ct), ct);
        var adbRetryTask = Task.Run(() => AdbRetryLoop(_options.Port, ct), ct);

        StatusChanged?.Invoke("Waiting for viewer");

        _runTask = Task.Run(async () =>
        {
            try { await Task.WhenAll(acceptTask, adbRetryTask); } catch (OperationCanceledException) { /* normal shutdown */ }
            StopCaptureLocked();
            StatusChanged?.Invoke("Stopped");
        }, CancellationToken.None);
    }

    public void Stop()
    {
        if (_cts is null) return;
        _cts.Cancel();
        try { _runTask?.Wait(TimeSpan.FromSeconds(5)); } catch { /* best effort */ }
    }

    private void OnClientConnected()
    {
        lock (_captureLock)
        {
            if (_ffmpeg is not null) return; // already capturing (shouldn't happen, StreamHub only allows one client)

            if (!_options.IsPrimaryDevice && _options.DeviceName is { } name)
            {
                bool ok = DisplayConfig.Attach(name, _options.Width, _options.Height, _options.OffsetX, _options.OffsetY, Math.Min(_options.Fps, 60));
                Log(ok ? $"Monitor attached ({name})" : $"Warning: failed to attach monitor {name} — it may already be attached, continuing anyway");
            }

            Log("Viewer connected — starting capture");
            _captureCts = new CancellationTokenSource();
            var capCt = _captureCts.Token;

            _ffmpeg = StartFfmpeg(_options);
            Task.Run(() => PumpFfmpegOutput(_ffmpeg, _hub!, capCt), capCt);
            Task.Run(() => PumpFfmpegStderr(_ffmpeg));

            StatusChanged?.Invoke("Streaming");
        }
    }

    private void OnClientDisconnected()
    {
        StopCaptureLocked();
        if (IsRunning) StatusChanged?.Invoke("Waiting for viewer");
    }

    private void StopCaptureLocked()
    {
        lock (_captureLock)
        {
            if (_ffmpeg is null) return;

            Log("Viewer disconnected — stopping capture");
            _captureCts?.Cancel();
            try { if (!_ffmpeg.HasExited) _ffmpeg.Kill(entireProcessTree: true); } catch { /* best effort */ }
            _ffmpeg = null;

            if (!_options.IsPrimaryDevice && _options.DeviceName is { } name)
            {
                bool ok = DisplayConfig.Detach(name);
                Log(ok ? $"Monitor detached ({name})" : $"Warning: failed to detach monitor {name}");
            }
        }
    }

    private static Process StartFfmpeg(Options options)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "ffmpeg",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in BuildFfmpegArgs(options)) psi.ArgumentList.Add(arg);

        return Process.Start(psi) ?? throw new InvalidOperationException("Failed to start ffmpeg. Is it on PATH?");
    }

    private static IEnumerable<string> BuildFfmpegArgs(Options o)
    {
        yield return "-hide_banner";
        yield return "-loglevel"; yield return "warning";

        if (o.Source == SourceMode.Mock)
        {
            yield return "-re";
            yield return "-f"; yield return "lavfi";
            yield return "-i"; yield return $"testsrc2=size={o.Width}x{o.Height}:rate={o.Fps}";
            yield return "-vf"; yield return
                "drawtext=fontfile='C\\:/Windows/Fonts/arial.ttf':text='%{localtime\\:%X}  frame %{n}':" +
                "fontcolor=white:fontsize=36:x=(w-text_w)/2:y=h-60:box=1:boxcolor=black@0.5:borderw=2";
        }
        else
        {
            yield return "-f"; yield return "gdigrab";
            yield return "-framerate"; yield return o.Fps.ToString();
            yield return "-offset_x"; yield return o.OffsetX.ToString();
            yield return "-offset_y"; yield return o.OffsetY.ToString();
            yield return "-video_size"; yield return $"{o.Width}x{o.Height}";
            yield return "-i"; yield return "desktop";
        }

        yield return "-pix_fmt"; yield return "yuv420p";
        yield return "-c:v"; yield return "libx264";
        yield return "-preset"; yield return "ultrafast";
        yield return "-tune"; yield return "zerolatency";
        // See git history on Program.cs for the full story: -threads 1 is
        // required to stop libx264 from splitting each picture into
        // multiple slice NALs, which every decoder in this repo assumes
        // never happens.
        yield return "-threads"; yield return "1";
        yield return "-x264-params"; yield return "repeat-headers=1:scenecut=0";
        yield return "-g"; yield return (o.Fps * 2).ToString();
        yield return "-bf"; yield return "0";
        yield return "-b:v"; yield return $"{o.BitrateKbps}k";
        yield return "-maxrate"; yield return $"{o.BitrateKbps}k";
        yield return "-bufsize"; yield return $"{o.BitrateKbps / 2}k";
        yield return "-f"; yield return "h264";
        yield return "-";
    }

    private async Task PumpFfmpegOutput(Process ffmpeg, StreamHub hub, CancellationToken ct)
    {
        var splitter = new AnnexBAccessUnitSplitter();
        var buffer = new byte[64 * 1024];
        var stdout = ffmpeg.StandardOutput.BaseStream;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stdout.ReadAsync(buffer, ct);
                if (read == 0) break;

                foreach (var (data, isKeyFrame) in splitter.Feed(buffer.AsSpan(0, read)))
                {
                    hub.Publish(data, isKeyFrame);
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (IOException) { /* ffmpeg process went away */ }
    }

    private async Task PumpFfmpegStderr(Process ffmpeg)
    {
        try
        {
            string? line;
            while ((line = await ffmpeg.StandardError.ReadLineAsync()) != null)
            {
                Log($"[ffmpeg] {line}");
            }
        }
        catch (IOException) { /* ffmpeg process went away */ }
    }

    private async Task AcceptLoop(int port, StreamHub hub, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        Log($"Listening for a viewer on port {port}...");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct);
                client.NoDelay = true;
                Log($"Viewer connected from {client.Client.RemoteEndPoint}");
                hub.AttachClient(client);
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        finally
        {
            listener.Stop();
        }
    }

    /// Keeps retrying `adb reverse` every few seconds for as long as the
    /// service is running, instead of only once at startup — so plugging
    /// the tablet in AFTER the sender's already running (the normal case
    /// for a tray app that's always on) still gets picked up automatically.
    /// Only logs on state transitions so it doesn't spam the log.
    private async Task AdbRetryLoop(int port, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            bool connected = TrySetupAdbReverse(port);
            if (connected != _usbWasConnected)
            {
                _usbWasConnected = connected;
                Log(connected
                    ? "USB: tablet detected, \"Connect via USB\" is ready"
                    : "USB: no tablet detected (plug in + enable USB debugging to use \"Connect via USB\")");
            }

            try { await Task.Delay(TimeSpan.FromSeconds(5), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private static bool TrySetupAdbReverse(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = FindAdb(),
                ArgumentList = { "reverse", $"tcp:{port}", $"tcp:{port}" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var adb = Process.Start(psi);
            if (adb is null) return false;
            if (!adb.WaitForExit(3000))
            {
                try { adb.Kill(entireProcessTree: true); } catch { /* best effort */ }
                return false;
            }
            return adb.ExitCode == 0;
        }
        catch (Exception e) when (e is Win32Exception or IOException)
        {
            return false;
        }
    }

    private static string FindAdb()
    {
        var sdkLocal = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Android", "Sdk", "platform-tools", "adb.exe");
        return File.Exists(sdkLocal) ? sdkLocal : "adb";
    }

    private static IEnumerable<string> LocalIPs()
    {
        foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
                yield return ip.ToString();
        }
    }

    private void Log(string line) => LogLine?.Invoke(line);

    /// Finds the actual virtual display driver's monitor by its adapter
    /// identity (see VirtualMonitorFinder — NOT "the first non-primary
    /// screen", which breaks the moment a second REAL monitor is around,
    /// e.g. at an office desk: confirmed live that heuristic would have
    /// targeted the real Samsung monitor instead of the virtual one, and
    /// would have tried to detach it on disconnect).
    ///
    /// The adapter's identity is stable whether or not it's currently
    /// attached to the desktop (confirmed live), but its Bounds only exist
    /// in Screen.AllScreens while attached — and the normal idle state is
    /// DETACHED now (see "the virtual monitor only exists while something's
    /// watching it" in the README), so this has to handle "found but not
    /// attached right now" as the common case, not an edge case: it picks a
    /// default geometry (TierFor's 1080p default, positioned just right of
    /// the primary monitor) rather than falling back to mirroring, since
    /// falling back here would mean every normal cold start mirrors the
    /// primary screen instead of targeting the virtual monitor.
    ///
    /// Only truly falls back to mirroring the primary screen if the driver
    /// isn't installed/found at all.
    public static (Options options, string? warning) AutoDetect(int port)
    {
        var found = VirtualMonitorFinder.Find();
        string deviceName;
        int width, height, offsetX, offsetY;
        bool isPrimary;
        string? warning;

        if (found is { } f && f.IsCurrentlyAttached && VirtualMonitorFinder.ResolveScreen(f.DeviceName) is { } vddScreen)
        {
            deviceName = f.DeviceName;
            (width, height) = (vddScreen.Bounds.Width, vddScreen.Bounds.Height);
            (offsetX, offsetY) = (vddScreen.Bounds.X, vddScreen.Bounds.Y);
            isPrimary = f.IsPrimary;
            warning = f.IsPrimary
                ? "The virtual monitor is currently set as your Windows primary display — skipping auto attach/detach for safety. Set your real monitor as primary in Display Settings to enable it."
                : null;
        }
        else if (found is { } f2)
        {
            // Installed, identified, but not currently extended onto the
            // desktop (the expected idle state) — default to 1080p
            // positioned right of whatever Windows considers primary now.
            deviceName = f2.DeviceName;
            (width, height) = (1920, 1080);
            var primaryBounds = (Screen.PrimaryScreen ?? Screen.AllScreens[0]).Bounds;
            (offsetX, offsetY) = (primaryBounds.Right, primaryBounds.Y);
            isPrimary = false;
            warning = null;
        }
        else
        {
            var primary = Screen.PrimaryScreen ?? Screen.AllScreens[0];
            deviceName = primary.DeviceName;
            (width, height) = (primary.Bounds.Width, primary.Bounds.Height);
            (offsetX, offsetY) = (primary.Bounds.X, primary.Bounds.Y);
            isPrimary = true;
            warning = "No virtual display driver found — mirroring your primary screen instead. Set up the virtual display driver (see README) for true extend mode.";
        }

        var (fps, bitrate) = TierFor(width, height);

        var options = new Options
        {
            Source = SourceMode.Desktop,
            Width = width,
            Height = height,
            OffsetX = offsetX,
            OffsetY = offsetY,
            Fps = fps,
            BitrateKbps = bitrate,
            Port = port,
            DeviceName = deviceName,
            IsPrimaryDevice = isPrimary,
        };
        return (options, warning);
    }

    private static (int fps, int bitrateKbps) TierFor(int width, int height)
    {
        long area = (long)width * height;
        if (area <= 1920 * 1080) return (60, 10_000);
        if (area <= 2560 * 1440) return (30, 14_000);
        return (30, 20_000); // 4K+: capped at 30fps, decode on mobile hardware can't reliably keep up with 4K60
    }
}
