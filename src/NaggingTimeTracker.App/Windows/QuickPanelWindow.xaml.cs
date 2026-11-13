using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NaggingTimeTracker.App.ViewModels;

namespace NaggingTimeTracker.App.Windows;

/// <summary>
/// The tray's left-click panel: a borderless, topmost window at the bottom-right of the work
/// area that hides itself when it loses activation (except while one of its dialogs is open).
/// It lives for the life of the app; closing only hides it.
/// </summary>
public partial class QuickPanelWindow : Window, IModalHost
{
    private const double EdgeMargin = 8;

    private readonly QuickPanelViewModel _vm;
    private readonly DispatcherTimer _ticker;
    private int _modalDepth;

    public QuickPanelWindow(QuickPanelViewModel vm)
    {
        ArgumentNullException.ThrowIfNull(vm);
        InitializeComponent();
        _vm = vm;
        _vm.Host = this;
        DataContext = vm;

        MaxHeight = SystemParameters.WorkArea.Height - 2 * EdgeMargin;
        _ticker = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _ticker.Tick += (_, _) => _vm.Tick();

        SizeChanged += (_, _) => PlaceAtCorner();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                _ticker.Start();
            }
            else
            {
                _ticker.Stop();
            }
        };
        Deactivated += (_, _) => AutoHide();
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>When the panel last hid itself because it lost activation (UTC wall clock).</summary>
    public DateTime LastAutoHideUtc { get; private set; } = DateTime.MinValue;

    Window IModalHost.Owner => this;

    public IDisposable BeginModal()
    {
        _modalDepth++;
        return new Releaser(this);
    }

    public void ShowPanel()
    {
        _vm.Refresh();
        Show();
        PlaceAtCorner();
        Activate();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Only hide: the panel lives as long as the app (Alt+F4 included).
        e.Cancel = true;
        AutoHide();
        base.OnClosing(e);
    }

    private void AutoHide()
    {
        if (_modalDepth > 0 || !IsVisible)
        {
            return;
        }

        _vm.FlushAllNotes();
        Hide();
        LastAutoHideUtc = DateTime.UtcNow;
    }

    private void PlaceAtCorner()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - EdgeMargin;
        Top = area.Bottom - ActualHeight - EdgeMargin;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            AutoHide();
        }
    }

    private void RunningNotes_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: RunningRowViewModel row })
        {
            row.FlushNotes();
        }
    }

    private sealed class Releaser(QuickPanelWindow owner) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner._modalDepth--;
            }
        }
    }
}
