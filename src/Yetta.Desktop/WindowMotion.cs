using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Yetta.Desktop;

/// <summary>One entrance for the entire top-level window surface, including its chrome.
/// No per-frame layout, resize or focus changes; Windows animation preferences are respected.</summary>
internal static class WindowMotion
{
    private static bool _registered;
    private static readonly DependencyProperty EnteredProperty =
        DependencyProperty.RegisterAttached("Entered", typeof(bool), typeof(WindowMotion), new PropertyMetadata(false));

    internal static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnLoaded));
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || !ReferenceEquals(e.OriginalSource, window) ||
            (bool)window.GetValue(EnteredProperty)) return;
        window.SetValue(EnteredProperty, true);
        if (!UiMotion.AnimationsEnabled || SystemParameters.HighContrast ||
            window.WindowState != WindowState.Normal || window.Content is not FrameworkElement surface) return;
        var original = surface.RenderTransform;
        var origin = surface.RenderTransformOrigin;
        var opacity = surface.Opacity;
        var scale = new ScaleTransform(0.97, 0.97);
        var transforms = new TransformGroup();
        transforms.Children.Add(original);
        transforms.Children.Add(scale);
        surface.SetCurrentValue(UIElement.RenderTransformProperty, transforms);
        surface.SetCurrentValue(UIElement.RenderTransformOriginProperty, new Point(0.5, 0.5));
        var duration = TimeSpan.FromMilliseconds(200);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var zoom = new DoubleAnimation(0.97, 1, duration) { EasingFunction = ease, FillBehavior = FillBehavior.HoldEnd };
        var fade = new DoubleAnimation(0, opacity, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop };
        bool restored = false;
        void Restore()
        {
            if (restored) return;
            restored = true;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            surface.BeginAnimation(UIElement.OpacityProperty, null);
            surface.SetCurrentValue(UIElement.RenderTransformProperty, original);
            surface.SetCurrentValue(UIElement.RenderTransformOriginProperty, origin);
            window.Closed -= OnClosed;
        }
        void OnClosed(object? _, EventArgs __) => Restore();
        window.Closed += OnClosed;
        zoom.Completed += (_, _) => Restore();
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom.Clone());
        surface.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
