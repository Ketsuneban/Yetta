using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Yetta.Desktop;

public sealed class EdgeScrollViewer : ScrollViewer
{
    private FrameworkElement? _viewport;
    private Rectangle? _top, _bottom;

    public override void OnApplyTemplate()
    {
        if (_viewport is not null) _viewport.SizeChanged -= ViewportResized;
        base.OnApplyTemplate();
        _viewport = GetTemplateChild("ViewportVisual") as FrameworkElement;
        _top = GetTemplateChild("TopPreview") as Rectangle;
        _bottom = GetTemplateChild("BottomPreview") as Rectangle;
        if (_viewport is not null) _viewport.SizeChanged += ViewportResized;
        Refresh();
    }

    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        base.OnScrollChanged(e);
        Refresh();
    }

    private void ViewportResized(object sender, SizeChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        if (_viewport is not { ActualWidth: > 0, ActualHeight: > 0 } viewport) return;
        double height = Math.Min(20, viewport.ActualHeight / 4);
        bool enabled = !SystemParameters.HighContrast && ScrollableHeight > 0.01;
        UpdatePreview(_top, enabled && VerticalOffset > 0.01, 0);
        UpdatePreview(_bottom, enabled && ScrollableHeight - VerticalOffset > 0.01, viewport.ActualHeight - height);

        void UpdatePreview(Rectangle? rectangle, bool visible, double y)
        {
            if (rectangle is null) return;
            rectangle.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                rectangle.Fill = null;
                return;
            }
            rectangle.Height = height;
            var source = new Rect(0, y, viewport.ActualWidth, height);
            if (rectangle.Fill is VisualBrush brush && brush.Visual == viewport)
            {
                if (brush.Viewbox != source) brush.Viewbox = source;
                return;
            }
            rectangle.Fill = new VisualBrush(viewport) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = source };
        }
    }
}
