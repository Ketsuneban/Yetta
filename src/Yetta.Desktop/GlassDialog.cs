using System.Windows.Input;
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
    internal static bool IsGlass { get; set; } = false;
    private static readonly Color Frost = Color.FromRgb(190, 211, 255);
    private static readonly Brush WhiteText = new SolidColorBrush(Color.FromRgb(245, 247, 255));
    internal static Brush SecondaryText { get; } =
        new SolidColorBrush(Color.FromRgb(197, 205, 224));

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
            // The outer native window must be truly transparent: only the rounded
            // glass surface is visible, never a rectangular frame behind it.
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ShowInTaskbar = false,
            Background = Brushes.Transparent,
            Foreground = WhiteText
        };
        // AllowsTransparency produces a WPF layered window. This enables true
        // per-pixel transparency around the rounded surface, but NOT DWM blur;
        // do not request an incompatible acrylic backdrop here.
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

        // Frameless windows remain movable via their header; interactive buttons
        // and scroll content outside this row retain normal click behaviour.
        if (header is FrameworkElement dragHeader)
        {
            dragHeader.MouseLeftButtonDown += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed) return;
                var window = Window.GetWindow(dragHeader);
                if (window is null) return;
                try { window.DragMove(); }
                catch (InvalidOperationException) { }
            };
        }
        Grid.SetRow(header, 0);
        container.Children.Add(header);

        var content = new EdgeScrollViewer
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

    internal static UIElement Surface(UIElement content, Thickness? padding = null, Window? owner = null)
    {
        // There is no secondary rectangular backplate: the border below *is*
        // the modal's full visible outline, with truly transparent pixels outside
        // its rounded corners. Alpha lets the desktop or owner window show through.
        var panel = new Grid();
        var clip = new RectangleGeometry { RadiusX = 23, RadiusY = 23 };
        panel.Clip = clip;
        panel.SizeChanged += (_, e) => clip.Rect = new Rect(0, 0,
            e.NewSize.Width, e.NewSize.Height);
        if (IsGlass && owner is not null)
        {
            // Blur *only the owner's WPF pixels* behind the warning. This
            // supports Windows versions where native backdrop is unavailable
            // without blurring the dialog's own text or interactive controls.
            var blurredOwner = new Border
            {
                Background = new VisualBrush(owner)
                {
                    ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                    Stretch = Stretch.UniformToFill
                },
                Effect = new BlurEffect { Radius = 26, RenderingBias = RenderingBias.Quality },
                Opacity = 0.90,
                IsHitTestVisible = false
            };
            panel.Children.Add(blurredOwner);
        }
        var rim = new Border
        {
            CornerRadius = new CornerRadius(23),
            BorderThickness = new Thickness(1.3),
            BorderBrush = new LinearGradientBrush(
                Color.FromArgb(205, 225, 238, 255),
                Color.FromArgb(65, 145, 168, 213), 130),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(208, 76, 96, 139), 0),
                    new GradientStop(Color.FromArgb(198, 37, 48, 79), 0.45),
                    new GradientStop(Color.FromArgb(216, 30, 39, 70), 1)
                }
            }
        };
        panel.Children.Add(rim);
        // Inset highlight follows the same silhouette. No square panels are
        // drawn behind the round border, and window corners remain transparent.
        var highlight = new Border
        {
            Margin = new Thickness(2),
            CornerRadius = new CornerRadius(21),
            BorderThickness = new Thickness(1, 1, 0, 0),
            BorderBrush = new LinearGradientBrush(
                Color.FromArgb(100, 236, 247, 255),
                Color.FromArgb(0, 206, 226, 255), 115),
            IsHitTestVisible = false
        };
        panel.Children.Add(highlight);
        var contentFrame = new Border
        {
            Padding = padding ?? new Thickness(25, 22, 25, 20),
            CornerRadius = new CornerRadius(23),
            Child = content
        };
        panel.Children.Add(contentFrame);
        return panel;
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
                ? (Brush)Application.Current.FindResource("AccentForeground")
                : WhiteText,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(115, 205, 220, 255)),
            Background = primary
                ? (Brush)Application.Current.FindResource("AccentFillBrush")
                : new LinearGradientBrush(Color.FromArgb(90, 125, 147, 186),
                    Color.FromArgb(65, 70, 84, 120), 90)
        };
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        border.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background") { RelativeSource =
                new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderBrushProperty,
            new System.Windows.Data.Binding("BorderBrush") { RelativeSource =
                new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        border.SetBinding(Border.BorderThicknessProperty,
            new System.Windows.Data.Binding("BorderThickness") { RelativeSource =
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
