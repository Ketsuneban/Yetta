using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Yetta.Desktop;

/// <summary>
/// Shared glass-inspired chrome for the application's own modal dialogs.
/// The action row is always outside the scroll viewer, so long copy never
/// obscures the buttons. This is a WPF visual treatment, not a security boundary.
/// </summary>
internal static class GlassDialog
{
    private static readonly Color Frost = Color.FromRgb(190, 211, 255);
    private static readonly Brush WhiteText = new SolidColorBrush(Color.FromRgb(245, 247, 255));
    internal static Brush SecondaryText { get; } =
        new SolidColorBrush(Color.FromRgb(197, 205, 224));

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute,
        ref int value, int size);

    internal static Window Create(Window owner, string title, double width, double height,
        double minWidth = 480, double minHeight = 360)
    {
        var dialog = new Window
        {
            Owner = owner,
            Title = title,
            Width = width,
            Height = Math.Min(height, SystemParameters.WorkArea.Height * 0.86),
            MinWidth = Math.Min(minWidth, SystemParameters.WorkArea.Width * 0.8),
            MinHeight = Math.Min(minHeight, SystemParameters.WorkArea.Height * 0.65),
            MaxHeight = SystemParameters.WorkArea.Height * 0.94,
            MaxWidth = SystemParameters.WorkArea.Width * 0.94,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.SingleBorderWindow,
            Background = new SolidColorBrush(Color.FromRgb(19, 24, 42)),
            Foreground = WhiteText
        };
        // Best-effort native dark caption and Windows 11 backdrop.
        // Fallback to the always-rendered glass-inspired WPF panel.
        dialog.SourceInitialized += (_, _) =>
        {
            if (!OperatingSystem.IsWindows()) return;
            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(dialog).Handle;
                int dark = 1;
                _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
                {
                    int backdrop = 3; // DWMSBT_TRANSIENTWINDOW (acrylic)
                    _ = DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        };
        return dialog;
    }

    internal static Grid Layout(UIElement header, UIElement scrollableBody,
        UIElement actions, UIElement? acknowledgement = null)
    {
        var container = new Grid();
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (acknowledgement is not null)
            container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(header, 0);
        container.Children.Add(header);

        var content = new ScrollViewer
        {
            Content = scrollableBody,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Margin = new Thickness(0, 8, 0, 12)
        };
        Grid.SetRow(content, 1);
        container.Children.Add(content);

        if (acknowledgement is not null)
        {
            Grid.SetRow(acknowledgement, 2);
            container.Children.Add(acknowledgement);
        }

        var footer = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, Frost.R, Frost.G, Frost.B)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(0, 14, 0, 0),
            Child = actions
        };
        Grid.SetRow(footer, acknowledgement is null ? 2 : 3);
        container.Children.Add(footer);
        return container;
    }

    internal static UIElement Surface(UIElement content, Thickness? padding = null)
    {
        var baseLayer = new Grid
        {
            ClipToBounds = true,
            Background = new LinearGradientBrush(
                Color.FromRgb(15, 22, 43), Color.FromRgb(26, 28, 52), 130)
        };
        // Fixed ambient accents and a faint specular rim: visible even on systems
        // without DWM acrylic; no false claim of blurring arbitrary Windows content.
        var glow = new Ellipse
        {
            Width = 490, Height = 290,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -160, -180, 0),
            Fill = new RadialGradientBrush(
                Color.FromArgb(95, 118, 164, 244),
                Color.FromArgb(0, 118, 164, 244)),
            IsHitTestVisible = false
        };
        baseLayer.Children.Add(glow);
        var glow2 = new Ellipse
        {
            Width = 320, Height = 320,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(-190, 0, 0, -180),
            Fill = new RadialGradientBrush(
                Color.FromArgb(50, 161, 137, 255),
                Color.FromArgb(0, 161, 137, 255)),
            IsHitTestVisible = false
        };
        baseLayer.Children.Add(glow2);

        var frostedBorder = new Border
        {
            Margin = new Thickness(14),
            CornerRadius = new CornerRadius(22),
            Padding = padding ?? new Thickness(25, 22, 25, 20),
            BorderThickness = new Thickness(1),
            BorderBrush = new LinearGradientBrush(
                Color.FromArgb(155, 205, 224, 255),
                Color.FromArgb(35, 120, 160, 220), 120),
            Background = new LinearGradientBrush(
                Color.FromArgb(139, 80, 98, 132),
                Color.FromArgb(88, 38, 49, 76), 135),
            Effect = new DropShadowEffect
            {
                Color = Color.FromRgb(4, 7, 19),
                BlurRadius = 22, ShadowDepth = 7, Opacity = 0.52
            },
            Child = content
        };
        baseLayer.Children.Add(frostedBorder);
        return baseLayer;
    }

    internal static Border Chip(string text)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11, 6, 11, 6),
            Background = new SolidColorBrush(Color.FromArgb(70, 132, 179, 226)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(85, 188, 212, 255)),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = text, FontSize = 11, FontWeight = FontWeights.Medium,
                Foreground = WhiteText
            }
        };
    }

    internal static Button Button(string text, bool primary = false)
    {
        var button = new Button
        {
            Content = text,
            MinWidth = 118, MinHeight = 38,
            Margin = new Thickness(9, 0, 0, 0),
            Padding = new Thickness(14, 8, 14, 8),
            Foreground = primary
                ? new SolidColorBrush(Color.FromRgb(17, 31, 54))
                : WhiteText,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(115, 205, 220, 255)),
            Background = primary
                ? new LinearGradientBrush(Color.FromRgb(211, 232, 255),
                    Color.FromRgb(151, 186, 240), 90)
                : new LinearGradientBrush(Color.FromArgb(90, 125, 147, 186),
                    Color.FromArgb(65, 70, 84, 120), 90)
        };
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding(nameof(Button.Background)) { RelativeSource =
                new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding(nameof(Button.BorderBrush)) { RelativeSource =
                new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty,
            new System.Windows.Data.Binding(nameof(Button.BorderThickness)) { RelativeSource =
                new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.87));
        template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.35));
        template.Triggers.Add(disabled);
        button.Template = template;
        return button;
    }
}
