namespace ExtendedScreen.Sender;

public enum SourceMode { Mock, Desktop }

public sealed class Options
{
    public SourceMode Source = SourceMode.Mock;
    public int Width = 1280;
    public int Height = 720;
    public int Fps = 30;
    public int Port = 7654;
    public int BitrateKbps = 6000;
    public int MonitorIndex = 0;
    public int OffsetX, OffsetY;

    /// Win32 device name (e.g. "\\.\DISPLAY40") of the monitor being
    /// captured, when known. Used to attach/detach that specific monitor
    /// from the desktop as viewers connect/disconnect — see
    /// SenderService/DisplayConfig. Left null for --source mock, or when
    /// the console CLI path doesn't bother to resolve it (it only matters
    /// for the tray app's auto-detected path).
    public string? DeviceName;

    /// True when DeviceName is the primary monitor (i.e. we're mirroring
    /// your main screen, not a real extend target) — attach/detach must
    /// never be attempted on it, that would black out your main display.
    public bool IsPrimaryDevice;

    public static Options? Parse(string[] args)
    {
        var o = new Options();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--source":
                    if (!TryNext(args, ref i, out var src)) return null;
                    if (!Enum.TryParse<SourceMode>(src, ignoreCase: true, out o.Source)) return null;
                    break;
                case "--width":
                    if (!TryNextInt(args, ref i, out o.Width)) return null;
                    break;
                case "--height":
                    if (!TryNextInt(args, ref i, out o.Height)) return null;
                    break;
                case "--fps":
                    if (!TryNextInt(args, ref i, out o.Fps)) return null;
                    break;
                case "--port":
                    if (!TryNextInt(args, ref i, out o.Port)) return null;
                    break;
                case "--bitrate":
                    if (!TryNextInt(args, ref i, out o.BitrateKbps)) return null;
                    break;
                case "--monitor":
                    if (!TryNextInt(args, ref i, out o.MonitorIndex)) return null;
                    break;
                case "-h":
                case "--help":
                    return null;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return null;
            }
        }

        return o;
    }

    private static bool TryNext(string[] args, ref int i, out string value)
    {
        if (i + 1 >= args.Length) { value = ""; return false; }
        value = args[++i];
        return true;
    }

    private static bool TryNextInt(string[] args, ref int i, out int value)
    {
        value = 0;
        return TryNext(args, ref i, out var s) && int.TryParse(s, out value);
    }

    public static void PrintUsage()
    {
        Console.WriteLine("""
            ExtendedScreen.Sender - streams H.264 video to the ExtendedScreen iPad app

            Usage:
              ExtendedScreen.Sender [options]

            Options:
              --source mock|desktop   Video source. 'mock' draws a test pattern with a
                                      clock overlay (default, use this to test the iPad
                                      app without any real capture). 'desktop' mirrors
                                      one of your real monitors via ffmpeg's gdigrab.
              --monitor N             Which monitor to mirror when --source desktop
                                      (0 = primary). Run once with a bad index to see
                                      the detected monitor list.
              --width N               Mock pattern width (default 1280). Ignored for
                                      --source desktop (uses the monitor's resolution).
              --height N              Mock pattern height (default 720).
              --fps N                 Target frame rate (default 30).
              --bitrate N             Target bitrate in kbps (default 6000).
              --port N                TCP port to listen on (default 7654).

            Examples:
              ExtendedScreen.Sender
              ExtendedScreen.Sender --source desktop --monitor 0 --fps 60 --bitrate 12000
            """);
    }
}
