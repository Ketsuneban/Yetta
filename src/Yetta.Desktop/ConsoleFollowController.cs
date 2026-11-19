using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
namespace Yetta.Desktop;

internal sealed class ConsoleFollowController : IDisposable
{
    private readonly ScrollViewer _view;
    private bool _following = true, _scheduled, _disposed;
    internal bool Following => _following;
    internal double Offset => _view.VerticalOffset;
    internal event Action<bool>? FollowingChanged;

    private void SetFollowing(bool value)
    {
        if (value == _following) return;
        _following = value;
        FollowingChanged?.Invoke(value);
    }

    internal void Resume()
    {
        SetFollowing(true);
        ContentChanged();
    }
    internal ConsoleFollowController(ScrollViewer view)
    {
        _view = view;
        _view.PreviewMouseWheel += Wheel;
        _view.PreviewMouseDown += MouseDown;
        _view.ScrollChanged += Changed;
        _view.Loaded += Loaded;
        ContentChanged();
    }
    private void Loaded(object sender, RoutedEventArgs e) => ContentChanged();
    private void Wheel(object sender, MouseWheelEventArgs e)
    {
        double lines = SystemParameters.WheelScrollLines;
        double step = lines < 0 ? _view.ViewportHeight : Math.Max(1, lines) * 18;
        double next = Math.Clamp(_view.VerticalOffset - e.Delta / 120.0 * step, 0, _view.ScrollableHeight);
        SetFollowing(next >= _view.ScrollableHeight - .5);
        _view.ScrollToVerticalOffset(next);
        e.Handled = true;
    }
    private void MouseDown(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? item = e.OriginalSource as DependencyObject; item is not null && item != _view; item = item is Visual ? VisualTreeHelper.GetParent(item) : LogicalTreeHelper.GetParent(item))
            if (item is ScrollBar) { SetFollowing(false); break; }
    }
    private void Changed(object sender, ScrollChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, _view)) return;
        if (!_following && _view.VerticalOffset >= _view.ScrollableHeight - .5) SetFollowing(true);
        if (_following) ContentChanged();
    }
    internal void ContentChanged()
    {
        if (_disposed || !_following || _scheduled) return;
        _scheduled = true;
        _view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _scheduled = false;
            if (!_disposed && _following) _view.ScrollToEnd();
        }));
    }
    internal void PauseAt(double offset)
    {
        SetFollowing(false);
        _view.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_disposed) { SetFollowing(false); _view.ScrollToVerticalOffset(offset); }
        }));
    }
    public void Dispose()
    {
        _disposed = true;
        _view.PreviewMouseWheel -= Wheel; _view.PreviewMouseDown -= MouseDown;
        _view.ScrollChanged -= Changed; _view.Loaded -= Loaded;
    }
}
