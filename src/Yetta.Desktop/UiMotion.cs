using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Yetta.Desktop;

/// <summary>
/// Short, non-blocking transitions for the existing WPF interface.
/// Keeps layout and button targets stable while animating their visible surfaces.
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

    internal static bool AnimationsEnabled => Enabled;

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
            AnimateEntrance(element, 210, 10);
        };
    }

    internal static void AnimatePage(UIElement surface)
    {
        if (!Enabled) return;
        if (surface is FrameworkElement element) AnimateEntrance(element, 190, 8);
    }

    private static void AnimateEntrance(FrameworkElement element, int millis, double distance)
    {
        element.BeginAnimation(UIElement.OpacityProperty, Fade(0, 1, millis),
            HandoffBehavior.SnapshotAndReplace);
        var transform = new TranslateTransform();
        element.RenderTransform = transform;
        transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(distance, 0, In(millis))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        });
    }

    internal static void AttachButtonFeedback(Button button, bool rounded = true)
    {
        button.MinHeight = Math.Max(button.MinHeight, 38);
        Border? Chrome() => button.Template?.FindName("ButtonChrome", button) as Border
            ?? button.Template?.FindName("Shell", button) as Border
            ?? button.Template?.FindName("CaptionSurface", button) as Border;
        if (rounded)
        {
            void MatchHitBox()
            {
                if (button.ActualWidth <= 0 || button.ActualHeight <= 0) return;
                button.ApplyTemplate();
                double radius = Chrome()?.CornerRadius.TopLeft ?? 7;
                button.Clip = new RectangleGeometry(
                    new Rect(0, 0, button.ActualWidth, button.ActualHeight), radius, radius);
            }
            button.Loaded += (_, _) => MatchHitBox();
            button.SizeChanged += (_, _) => MatchHitBox();
        }
        void Feedback(double opacity, int duration)
        {
            if (!button.IsEnabled) return;
            // Animate the rounded template surface, never the rectangular control box.
            var chrome = Chrome();
            if (chrome is not null) AnimateOpacity(chrome, opacity, duration);
        }
        button.MouseEnter += (_, _) => Feedback(0.88, 120);
        button.MouseLeave += (_, _) => Feedback(1, 150);
        button.PreviewMouseLeftButtonDown += (_, _) => Feedback(0.72, 75);
        button.PreviewMouseLeftButtonUp += (_, _) => Feedback(0.88, 120);
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
