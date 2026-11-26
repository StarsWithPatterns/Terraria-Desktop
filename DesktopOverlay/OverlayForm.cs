using System.Diagnostics;

namespace DesktopOverlay;

/// <summary>
/// The see-through, click-through, always-on-top window the character lives in,
/// plus the tray icon that controls it.
/// </summary>
internal sealed class OverlayForm : Form
{
    private readonly Settings _settings;
    private readonly FrameClient _client;
    private readonly LayeredSurface _surface = new();
    private readonly Walker _walker = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _wanderItem;
    private readonly ToolStripMenuItem _followItem;
    private readonly List<ToolStripMenuItem> _scaleItems = new();

    private Frame? _frame;
    private bool _visible;
    private double _lastTime;
    private double _nextTopmost;

    public OverlayForm(Settings settings)
    {
        _settings = settings;
        _client = new FrameClient(settings.Port);

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Text = "Terraria Desktop";
        Bounds = new Rectangle(-10000, -10000, 1, 1);

        _statusItem = new ToolStripMenuItem("Waiting for Terraria...") { Enabled = false };
        _wanderItem = new ToolStripMenuItem("Walk around on its own", null, (_, _) => SetMode(OverlayMode.Wander));
        _followItem = new ToolStripMenuItem("Copy my in-game movement", null, (_, _) => SetMode(OverlayMode.FollowGame));

        var sizeMenu = new ToolStripMenuItem("Size");
        foreach (int s in new[] { 1, 2, 3, 4 })
        {
            var item = new ToolStripMenuItem($"{s}x", null, (_, _) => SetScale(s));
            _scaleItems.Add(item);
            sizeMenu.DropDownItems.Add(item);
        }

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_wanderItem);
        menu.Items.Add(_followItem);
        menu.Items.Add(sizeMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Close()));

        _tray = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Terraria Desktop",
            ContextMenuStrip = menu,
            Visible = true,
        };

        RefreshMenu();
        _timer.Tick += (_, _) => Tick();
    }

    // The window never takes focus, never shows in Alt+Tab, and every click goes
    // straight through it to whatever is underneath.
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_TOOLWINDOW
                | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _lastTime = _clock.Elapsed.TotalSeconds;
        _timer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _client.Dispose();
        _surface.Dispose();
        _settings.Save();
        base.OnFormClosed(e);
    }

    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        double dt = Math.Min(now - _lastTime, 0.1);
        _lastTime = now;

        int scale = _settings.Scale;
        var newFrame = _client.TakeNewFrame();
        if (newFrame != null)
            _frame = newFrame;

        bool inWorld = _frame is { InWorld: true };
        UpdateStatus(inWorld);

        if (!inWorld)
        {
            if (_visible)
            {
                _surface.Clear();
                _surface.Present(Handle, Left, Top);
                _visible = false;
            }
            _client.SendControl(_settings.Mode, _walker.Direction, false, 0);
            return;
        }

        var area = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
        int width = _frame!.Width * scale;
        _walker.Update(_settings.Mode, _frame, area, scale, dt, width);

        // Tell the mod which way we are walking so it can play the matching animation.
        if (_settings.Mode == OverlayMode.Wander)
            _client.SendControl(OverlayMode.Wander, _walker.Direction, _walker.Walking, Walker.WanderSpeed);
        else
            _client.SendControl(OverlayMode.FollowGame, 1, false, 0);

        if (newFrame != null || !_visible)
            _surface.Draw(_frame, scale);

        int x = (int)Math.Round(_walker.X - width / 2.0);
        int y = area.Bottom - _frame.FeetRow * scale + (int)Math.Round(_walker.JumpOffset);
        _surface.Present(Handle, x, y);
        _visible = true;

        // Clicking the taskbar can push it above other topmost windows; take our place back now and then.
        if (now >= _nextTopmost)
        {
            _nextTopmost = now + 2;
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    private string _lastStatus = "";

    private void UpdateStatus(bool inWorld)
    {
        string status = !_client.Connected ? "Waiting for Terraria..."
            : inWorld ? "Connected to Terraria"
            : "Connected. Enter a world to see your character.";
        if (status == _lastStatus)
            return;
        _lastStatus = status;
        _statusItem.Text = status;
        _tray.Text = "Terraria Desktop: " + (status.Length > 40 ? status[..40] : status);
    }

    private void SetMode(OverlayMode mode)
    {
        _settings.Mode = mode;
        _walker.JumpOffset = 0;
        _settings.Save();
        RefreshMenu();
    }

    private void SetScale(int scale)
    {
        _settings.Scale = scale;
        _visible = false; // redraw at the new size
        _settings.Save();
        RefreshMenu();
    }

    private void RefreshMenu()
    {
        _wanderItem.Checked = _settings.Mode == OverlayMode.Wander;
        _followItem.Checked = _settings.Mode == OverlayMode.FollowGame;
        for (int i = 0; i < _scaleItems.Count; i++)
            _scaleItems[i].Checked = _settings.Scale == i + 1;
    }

    private static Icon LoadIcon()
    {
        using var stream = typeof(OverlayForm).Assembly.GetManifestResourceStream("DesktopOverlay.app.ico");
        return stream != null ? new Icon(stream) : SystemIcons.Application;
    }
}
