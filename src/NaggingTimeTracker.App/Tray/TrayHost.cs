using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using NaggingTimeTracker.App.Tracking;
using NaggingTimeTracker.App.Windows;
using NaggingTimeTracker.Core.Formatting;
using NaggingTimeTracker.Core.Services;
using NaggingTimeTracker.Core.Settings;
using NaggingTimeTracker.Core.Time;
using NaggingTimeTracker.Core.Tray;

namespace NaggingTimeTracker.App.Tray;

/// <summary>Owns the tray icon, its tooltip and context menu, and the quick panel.</summary>
public sealed class TrayHost(
    TrackingService tracking,
    CatalogService catalog,
    SettingsService settings,
    TrackingActions actions,
    IServiceProvider services,
    TimeProvider time) : IDisposable
{
    private static readonly TimeSpan ToggleGuard = TimeSpan.FromMilliseconds(300);

    private readonly TrayToolTipViewModel _toolTip = new();
    private TaskbarIcon? _icon;
    private DispatcherTimer? _toolTipTimer;
    private QuickPanelWindow? _quickPanel;
    private string? _currentIconFile;

    private bool _hasAttention;

    // Menu items whose state is updated when the menu opens.
    private MenuItem? _stopAll;
    private MenuItem? _continueLast;
    private MenuItem? _pause;
    private MenuItem? _resume;

    public void Initialize()
    {
        _icon = new TaskbarIcon
        {
            ToolTipText = TrayToolTipText.Build([]),
            TrayToolTip = new TrayToolTipView(_toolTip),
            NoLeftClickDelay = true,
            ContextMenu = BuildMenu(),
        };
        _icon.TrayLeftMouseUp += (_, _) => OnLeftClick();

        // Required for an icon outside a XAML visual tree. The default (true) would put the
        // process into Windows 11 efficiency mode, which throttles timers.
        _icon.ForceCreate(enablesEfficiencyMode: false);

        tracking.RunningEntriesChanged += OnRunningEntriesChanged;
        _toolTipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _toolTipTimer.Tick += (_, _) => RefreshToolTip();
        _toolTipTimer.Start();
        Refresh();
    }

    public void ShowQuickPanel()
    {
        _quickPanel ??= services.GetRequiredService<QuickPanelWindow>();
        _quickPanel.ShowPanel();
    }

    /// <summary>
    /// Whether a nag is visible or queued (switches to the attention icons). Called by the nag
    /// window manager from Phase 5; nothing calls it yet.
    /// </summary>
    public void SetAttention(bool hasAttention)
    {
        _hasAttention = hasAttention;
        UpdateIcon();
    }

    public void Dispose()
    {
        tracking.RunningEntriesChanged -= OnRunningEntriesChanged;
        _toolTipTimer?.Stop();
        _icon?.Dispose(); // also disposes its current Icon
    }

    private void OnRunningEntriesChanged(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        UpdateIcon();
        RefreshToolTip();
    }

    private void OnLeftClick()
    {
        // Clicking the icon while the panel is open deactivates (and hides) it just before the
        // click arrives; treat that click as "close" rather than reopening.
        if (_quickPanel is { } panel && DateTime.UtcNow - panel.LastAutoHideUtc < ToggleGuard)
        {
            return;
        }

        ShowQuickPanel();
    }

    private void UpdateIcon()
    {
        if (_icon is null)
        {
            return;
        }

        var file = TrayIconSelector.Select(_hasAttention, tracking.GetRunning().Count);
        if (file == _currentIconFile)
        {
            return;
        }

        _icon.Icon = LoadIcon(file);
        _currentIconFile = file;
    }

    /// <summary>
    /// Loads a fresh Icon each time: TaskbarIcon disposes the previous Icon whenever the property
    /// changes, so a cached instance would be disposed by the time it is reused.
    /// </summary>
    private static System.Drawing.Icon LoadIcon(string file)
    {
        var size = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
        if (size <= 0)
        {
            size = 16;
        }

        var info = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/tray/{file}"))
            ?? throw new InvalidOperationException($"Tray icon resource '{file}' is missing.");
        using var stream = info.Stream;
        return new System.Drawing.Icon(stream, size, size);
    }

    private void RefreshToolTip()
    {
        var running = tracking.GetRunning();
        var names = catalog.GetNames();
        var now = time.NowUtc();
        var lines = running
            .Select(e => $"{names.Customer(e.CustomerId)} · {names.TaskType(e.TaskTypeId)} — {DurationFormat.Short(now - e.StartUtc)}")
            .ToList();

        // Windows 11 often shows the native tooltip instead of the rich one, so both carry the status.
        _toolTip.Update(lines);
        if (_icon is not null)
        {
            _icon.ToolTipText = TrayToolTipText.Build(lines);
        }
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        menu.Items.Add(Item("Start new…", "TrayMenu.StartNew", ShowQuickPanel));
        _stopAll = Item("Stop all", "TrayMenu.StopAll", () => tracking.StopAll());
        menu.Items.Add(_stopAll);
        _continueLast = Item("Continue last", "TrayMenu.ContinueLast", () => actions.ContinueLast(owner: null));
        menu.Items.Add(_continueLast);
        menu.Items.Add(new Separator());

        _pause = new MenuItem { Header = "Pause reminders" };
        AutomationProperties.SetAutomationId(_pause, "TrayMenu.Pause");
        _pause.Items.Add(Item("1 hour", "TrayMenu.Pause1h", () => SetPause(time.NowUtc().AddHours(1))));
        _pause.Items.Add(Item("Until tomorrow", "TrayMenu.PauseTomorrow", () => SetPause(NextLocalMidnight())));
        _resume = Item("Resume", "TrayMenu.Resume", () => SetPause(null));
        _pause.Items.Add(_resume);
        menu.Items.Add(_pause);
        menu.Items.Add(new Separator());

        menu.Items.Add(Item("Exit", "TrayMenu.Exit", RequestExit));

        menu.Opened += (_, _) => UpdateMenuState();
        return menu;
    }

    private static MenuItem Item(string header, string automationId, Action onClick)
    {
        var item = new MenuItem { Header = header };
        AutomationProperties.SetAutomationId(item, automationId);
        item.Click += (_, _) => onClick();
        return item;
    }

    private void UpdateMenuState()
    {
        _stopAll!.IsEnabled = tracking.GetRunning().Count > 0;
        _continueLast!.IsEnabled = tracking.HasStoppedEntries();

        var pausedUntil = settings.GetInstant(SettingKeys.RemindersPausedUntilUtc);
        var now = time.NowUtc();
        var paused = pausedUntil is { } until && until > now;
        _resume!.IsEnabled = paused;
        _pause!.Header = paused ? $"Pause reminders ({PauseLabel(pausedUntil!.Value)})" : "Pause reminders";
    }

    private string PauseLabel(DateTimeOffset until)
    {
        if (until == NextLocalMidnight())
        {
            return "until tomorrow";
        }

        var local = TimeZoneInfo.ConvertTime(until, time.LocalTimeZone);
        return "until " + local.ToString("t", CultureInfo.CurrentCulture);
    }

    private void SetPause(DateTimeOffset? until) =>
        settings.SetInstant(SettingKeys.RemindersPausedUntilUtc, until);

    private DateTimeOffset NextLocalMidnight()
    {
        var tz = time.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.NowUtc(), tz).DateTime);
        return LocalDateRange.ToUtcRange(today, today, tz).ToUtc;
    }

    private void RequestExit()
    {
        var n = tracking.GetRunning().Count;
        if (n > 0)
        {
            var text = n == 1
                ? "1 timer is still running. It will keep counting while the app is closed. Exit anyway?"
                : string.Create(CultureInfo.CurrentCulture,
                    $"{n} timers are still running. They will keep counting while the app is closed. Exit anyway?");
            if (MessageBox.Show(text, "Nagging Time Tracker", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                != MessageBoxResult.Yes)
            {
                return;
            }
        }

        Application.Current.Shutdown();
    }
}
