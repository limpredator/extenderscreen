using System.IO;
using System.Windows.Forms;

namespace ExtendedScreen.Sender;

/// <summary>
/// The "just open it" experience: no console, no flags to remember. Lives in
/// the system tray, auto-detects the virtual/extended monitor and a sane
/// fps/bitrate for its resolution, and starts streaming immediately. The
/// underlying adb-reverse retry loop (see SenderService.AdbRetryLoop) means
/// you can open this BEFORE or AFTER plugging the tablet in — either order
/// ends with "Connect via USB" working on the tablet without restarting
/// anything here.
/// </summary>
public sealed class TrayAppContext : ApplicationContext
{
    private const int Port = 7654;

    private readonly NotifyIcon _trayIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _startItem;
    private readonly ToolStripMenuItem _stopItem;
    private readonly LogForm _logForm = new();
    private readonly SenderService _service;
    private readonly DisplayChangeWatcher _displayWatcher = new();

    public TrayAppContext()
    {
        var (options, warning) = SenderService.AutoDetect(Port);
        _service = new SenderService(options);
        var logPath = Path.Combine(Path.GetTempPath(), "ExtendedScreen.Sender.log");
        try { File.Delete(logPath); } catch { /* best effort */ }
        _service.LogLine += line =>
        {
            _logForm.AppendLine(line);
            try { File.AppendAllText(logPath, line + Environment.NewLine); } catch { /* best effort */ }
        };
        _service.StatusChanged += status => SetStatus(status);
        _displayWatcher.DisplayChanged += () => _service.RestartCaptureForDisplayChange();

        _statusItem = new ToolStripMenuItem("Status: starting…") { Enabled = false };
        _startItem = new ToolStripMenuItem("Start streaming", null, (_, _) => _service.Start());
        _stopItem = new ToolStripMenuItem("Stop streaming", null, (_, _) => _service.Stop());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_startItem);
        menu.Items.Add(_stopItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Show log", null, (_, _) => _logForm.ShowAndFocus()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));

        _trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "ExtendedScreen sender",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _trayIcon.DoubleClick += (_, _) => _logForm.ShowAndFocus();

        if (warning is not null)
        {
            _logForm.AppendLine($"[warning] {warning}");
            _trayIcon.ShowBalloonTip(5000, "ExtendedScreen", warning, ToolTipIcon.Warning);
        }

        try
        {
            _service.Start();
        }
        catch (Exception e)
        {
            _logForm.AppendLine($"[fatal] {e}");
            _trayIcon.ShowBalloonTip(8000, "ExtendedScreen", $"Failed to start: {e.Message}", ToolTipIcon.Error);
        }
    }

    private void SetStatus(string status)
    {
        _statusItem.Text = $"Status: {status}";
        _startItem.Enabled = status != "Streaming";
        _stopItem.Enabled = status == "Streaming";
    }

    private void ExitApp()
    {
        _service.Stop();
        _displayWatcher.Dispose();
        _trayIcon.Visible = false;
        Application.Exit();
    }
}

internal sealed class LogForm : Form
{
    private readonly TextBox _textBox;

    public LogForm()
    {
        Text = "ExtendedScreen sender — log";
        Width = 720;
        Height = 420;
        StartPosition = FormStartPosition.CenterScreen;

        _textBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new System.Drawing.Font("Consolas", 9),
        };
        Controls.Add(_textBox);

        // Minimize instead of closing — this is a background tray app, the
        // window is just a log viewer, not the app's lifetime.
        FormClosing += (_, e) =>
        {
            e.Cancel = true;
            Hide();
        };
    }

    public void AppendLine(string line)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLine(line));
            return;
        }
        _textBox.AppendText(line + Environment.NewLine);
    }

    public void ShowAndFocus()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }
}
