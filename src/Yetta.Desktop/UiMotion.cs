using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Yetta.Desktop;

/// <summary>
/// Short, non-blocking transitions for the existing WPF interface.
/// Does not animate security decisions or delay the approval callbacks.
/// Respects the Windows reduced-animation setting where available.
/// </summary>
internal static class UiMotion
{
    private static bool Enabled
    {
        get
        {
            try { return SystemParameters.ClientAreaAnimation; }
            catch (InvalidOperationException) { return false; }
        }
    }

    private static Duration In(int milliseconds) =>
        new(TimeSpan.FromMilliseconds(milliseconds));

    private static DoubleAnimation Fade(double from, double to, int duration) =>
        new(from, to, In(duration))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

    internal static void AnimateOpen(UIElement surface)
    {
        if (!Enabled) return;
        if (surface is not FrameworkElement element) return;
        element.Loaded += (_, _) =>
        {
            element.Opacity = 1;
            var animation = Fade(0, 1, 200);
            element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
        };
    }

    internal static void AnimatePage(UIElement surface)
    {
        if (!Enabled) return;
        var animation = Fade(0.55, 1, 175);
        surface.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    internal static void AttachButtonFeedback(Button button)
    {
        // Keep the entire control surface as the hit target; only change
        // opacity in response to hover/press (no moving layout or tiny hitbox).
        button.MinHeight = Math.Max(button.MinHeight, 38);
        button.MouseEnter += (_, _) => AnimateOpacity(button, 0.88, 120);
        button.MouseLeave += (_, _) => AnimateOpacity(button, 1, 150);
        button.PreviewMouseLeftButtonDown += (_, _) => AnimateOpacity(button, 0.72, 75);
        button.PreviewMouseLeftButtonUp += (_, _) => AnimateOpacity(button, 1, 120);
    }

    private static void AnimateOpacity(UIElement element, double value, int millis)
    {
        if (!Enabled || !element.IsEnabled) return;
        var animation = new DoubleAnimation
        {
            To = value, Duration = In(millis),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(UIElement.OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
