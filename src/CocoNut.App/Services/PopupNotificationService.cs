using Avalonia;
using Avalonia.Threading;
using CocoNut.App.ViewModels;
using CocoNut.App.Views;
using CocoNut.Core.Abstractions;

namespace CocoNut.App.Services;

/// <summary>
/// <see cref="INotificationService"/> that shows small, borderless, topmost <see cref="NotificationWindow"/>
/// popups stacked in the bottom-right corner of the primary screen's working area, replacing WinNUT's Windows
/// toast notifications (<c>ToastPopup.vb</c>) with something that works on every desktop platform. Safe to call
/// from any thread; the actual window is always created on the UI thread via <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class PopupNotificationService : INotificationService
{
    private const double WindowWidth = 320;
    private const double WindowHeight = 88;
    private const double Margin = 12;
    private static readonly TimeSpan AutoCloseDelay = TimeSpan.FromSeconds(6);
    private static readonly PixelRect FallbackWorkArea = new(0, 0, 1920, 1080);

    private readonly IUiDispatcher _dispatcher;

    // Only ever read/written from the UI thread (every entry point goes through _dispatcher.Post), so no lock is needed.
    private readonly List<NotificationWindow> _openWindows = [];

    public PopupNotificationService(IUiDispatcher dispatcher) =>
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <inheritdoc />
    public void Notify(string title, string message, NotificationKind kind = NotificationKind.Info) =>
        _dispatcher.Post(() => ShowOnUiThread(title, message, kind));

    private void ShowOnUiThread(string title, string message, NotificationKind kind)
    {
        var window = new NotificationWindow
        {
            Width = WindowWidth,
            Height = WindowHeight,
            DataContext = new NotificationContent(title, message, kind),
        };

        window.Closed += (_, _) =>
        {
            _openWindows.Remove(window);
            Reflow();
        };

        _openWindows.Add(window);
        window.Show();
        Reflow();

        var timer = new DispatcherTimer { Interval = AutoCloseDelay };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            window.Close();
        };
        timer.Start();
    }

    /// <summary>Repositions every open popup, newest closest to the screen's bottom edge, stacking upward.</summary>
    private void Reflow()
    {
        if (_openWindows.Count == 0)
        {
            return;
        }

        var (workArea, scaling) = ResolveScreen(_openWindows[^1]);
        var positions = ComputeStackPositions(workArea, scaling, _openWindows.Count);

        for (var i = 0; i < _openWindows.Count; i++)
        {
            _openWindows[i].Position = positions[i];
        }
    }

    /// <summary>
    /// Computes the top-left corner of each of <paramref name="count"/> stacked popups, oldest first, so the newest
    /// sits in the bottom-right corner of <paramref name="workArea"/> and older ones stack upward. The work area and
    /// the returned positions are physical pixels while the popup size/margin are DIPs, so they are converted with
    /// the screen's <paramref name="scaling"/> - otherwise a popup on a scaled (e.g. 150%/200%) screen is placed as
    /// if it were smaller than it is and ends up partially off-screen. Positions are clamped to the work area, so a
    /// stack taller than the screen overlaps at the top instead of running off it.
    /// </summary>
    public static PixelPoint[] ComputeStackPositions(PixelRect workArea, double scaling, int count)
    {
        if (count <= 0)
        {
            return [];
        }

        if (scaling <= 0 || double.IsNaN(scaling) || double.IsInfinity(scaling))
        {
            scaling = 1;
        }

        var width = (int)Math.Ceiling(WindowWidth * scaling);
        var height = (int)Math.Ceiling(WindowHeight * scaling);
        var margin = (int)Math.Ceiling(Margin * scaling);

        var x = Math.Max(workArea.X, workArea.Right - width - margin);
        var positions = new PixelPoint[count];

        for (var i = 0; i < count; i++)
        {
            var stackPosition = count - i; // 1 = newest/bottom-most, growing upward.
            var y = Math.Max(workArea.Y, workArea.Bottom - stackPosition * (height + margin));
            positions[i] = new PixelPoint(x, y);
        }

        return positions;
    }

    private static (PixelRect WorkArea, double Scaling) ResolveScreen(NotificationWindow window)
    {
        var screens = window.Screens;
        var screen = screens?.Primary ?? (screens?.All is { Count: > 0 } all ? all[0] : null);
        return screen is null ? (FallbackWorkArea, 1) : (screen.WorkingArea, screen.Scaling);
    }
}
