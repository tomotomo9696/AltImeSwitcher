namespace AltImeSwitcher;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly HiddenDispatcher _dispatcher = new();
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _pause = new("Pause") { CheckOnClick = true };
    private readonly ToolStripMenuItem _thresholdMenu = new();
    private readonly Dictionary<int, ToolStripMenuItem> _thresholdItems = [];
    private readonly Icon _appIcon = ApplicationIcon.Load();
    private readonly NotifyIcon _icon;
    private readonly string? _settingsPath;
    private AppSettings _settings;
    private AltKeyHook? _hook;
    private bool _disposed;
    private int _generation;

    internal ContextMenuStrip TrayMenu => _menu;
    internal int CurrentHoldMilliseconds => _hook?.HoldMilliseconds ?? _settings.HoldMilliseconds;

    public TrayApplicationContext(string? settingsPath = null)
    {
        _settingsPath = settingsPath;
        _settings = AppSettings.Load(settingsPath);
        _ = _dispatcher.Handle;
        foreach (var milliseconds in AppSettings.HoldOptions)
        {
            var label = milliseconds == AppSettings.DefaultHoldMilliseconds ? $"{milliseconds} ms (既定)" : $"{milliseconds} ms";
            var item = new ToolStripMenuItem(label) { Tag = milliseconds };
            item.Click += (_, _) => SetHoldMilliseconds(milliseconds);
            _thresholdItems.Add(milliseconds, item);
            _thresholdMenu.DropDownItems.Add(item);
        }
        _menu.Items.Add(_thresholdMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _pause.CheckedChanged += (_, _) => SetPaused(_pause.Checked);
        _menu.Items.Add(_pause);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _icon = new NotifyIcon { Icon = _appIcon, ContextMenuStrip = _menu };
        UpdateMenu();
        try
        {
            StartHook();
            _icon.Visible = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private void StartHook()
    {
        var generation = ++_generation;
        _hook = new AltKeyHook(request => Post(() => ImeController.Apply(request), generation),
            error => Post(() =>
            {
                _pause.Checked = true;
                _icon.ShowBalloonTip(5000, "AltImeSwitcher", $"Paused: {error.Message}", ToolTipIcon.Error);
            }, generation), holdMilliseconds: _settings.HoldMilliseconds);
        _hook.Install();
    }

    private void Post(Action action, int generation)
    {
        if (_disposed) return;
        try { _dispatcher.BeginInvoke((Action)(() => { if (!_disposed && generation == _generation && !_pause.Checked) action(); })); }
        catch (InvalidOperationException) { /* Shutdown raced with a queued notification. */ }
    }

    private void SetPaused(bool paused)
    {
        ++_generation;
        _hook?.Dispose();
        _hook = null;
        if (!paused)
        {
            try { StartHook(); }
            catch (Exception ex)
            {
                AppLog.Write(ex);
                _pause.Checked = true;
                _icon.ShowBalloonTip(5000, "AltImeSwitcher", ex.Message, ToolTipIcon.Error);
            }
        }
        UpdateMenu();
    }

    private void SetHoldMilliseconds(int milliseconds)
    {
        if (_settings.HoldMilliseconds == milliseconds) return;
        _settings = _settings with { HoldMilliseconds = milliseconds };
        _hook?.SetHoldMilliseconds(milliseconds);
        UpdateMenu();
        if (!_settings.TrySave(_settingsPath))
            _icon.ShowBalloonTip(5000, "AltImeSwitcher", "設定を保存できませんでした。今回の起動中のみ適用されます。", ToolTipIcon.Warning);
    }

    private void UpdateMenu()
    {
        foreach (var (milliseconds, item) in _thresholdItems) item.Checked = milliseconds == _settings.HoldMilliseconds;
        _thresholdMenu.Text = $"長押し判定 ({_settings.HoldMilliseconds} ms)";
        _icon.Text = $"AltImeSwitcher{(_pause.Checked ? " (paused)" : "")} | {_settings.HoldMilliseconds} ms";
    }

    protected override void ExitThreadCore()
    {
        Dispose();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _hook?.Dispose();
            _hook = null;
            _icon.Visible = false;
            _icon.Dispose();
            _appIcon.Dispose();
            _menu.Dispose();
            _dispatcher.Dispose();
        }
        base.Dispose(disposing);
    }
}
