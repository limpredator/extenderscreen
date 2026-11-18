using System.IO;
using System.Windows.Forms;

namespace ExtendedScreen.Sender;

internal static class Program
{
    private static readonly string CrashLogPath = Path.Combine(Path.GetTempPath(), "ExtendedScreen.Sender.crash.log");

    /// No arguments: launch the tray app (auto-detects everything, "just
    /// open it" experience — see TrayApp.cs). Any argument: fall back to the
    /// original console CLI, unchanged, for scripting/debugging/power users
    /// who want explicit control over source/monitor/fps/bitrate.
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                File.AppendAllText(CrashLogPath, $"{DateTime.Now}: {e.ExceptionObject}\n");
            Application.ThreadException += (_, e) =>
                File.AppendAllText(CrashLogPath, $"{DateTime.Now}: {e.Exception}\n");

            // Without this, a DPI-unaware/SystemAware process gets monitor
            // bounds virtualized/scaled by Windows instead of the true pixel
            // dimensions ffmpeg's gdigrab needs — confirmed empirically: the
            // 1920x1080 virtual monitor was reported as 2400x1350 (a 1.25x
            // inflation) before this was set, which fed the wrong capture
            // size straight into ffmpeg.
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            ApplicationConfiguration.Initialize();
            try
            {
                Application.Run(new TrayAppContext());
            }
            catch (Exception e)
            {
                File.AppendAllText(CrashLogPath, $"{DateTime.Now}: {e}\n");
                throw;
            }
            return 0;
        }

        return RunConsole(args);
    }

    private static int RunConsole(string[] args)
    {
        var options = Options.Parse(args);
        if (options is null)
        {
            Options.PrintUsage();
            return 1;
        }

        if (options.Source == SourceMode.Desktop)
        {
            var screens = Screen.AllScreens;
            if (options.MonitorIndex < 0 || options.MonitorIndex >= screens.Length)
            {
                Console.Error.WriteLine($"--monitor {options.MonitorIndex} is out of range. Detected monitors:");
                for (int i = 0; i < screens.Length; i++)
                {
                    var b = screens[i].Bounds;
                    Console.Error.WriteLine($"  [{i}] {screens[i].DeviceName}  {b.Width}x{b.Height} at ({b.X},{b.Y})" +
                                             (screens[i].Primary ? "  (primary)" : ""));
                }
                return 1;
            }

            var screen = screens[options.MonitorIndex];
            var bounds = screen.Bounds;
            options.Width = bounds.Width;
            options.Height = bounds.Height;
            options.OffsetX = bounds.X;
            options.OffsetY = bounds.Y;
            options.DeviceName = screen.DeviceName;
            options.IsPrimaryDevice = screen.Primary;
        }

        var service = new SenderService(options);
        service.LogLine += Console.WriteLine;

        using var doneEvent = new ManualResetEventSlim(false);
        service.StatusChanged += status =>
        {
            if (status == "Stopped") doneEvent.Set();
        };
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            service.Stop();
        };

        service.Start();
        doneEvent.Wait();
        return 0;
    }
}
