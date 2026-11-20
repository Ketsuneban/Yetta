using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Yetta.Desktop;

internal static class TextSelection
{
    internal static void Enable() => EventManager.RegisterClassHandler(typeof(TextBlock),
        UIElement.MouseLeftButtonDownEvent, new MouseButtonEventHandler(Begin));

    private static void Begin(object sender, MouseButtonEventArgs e)
    {
        var label = (TextBlock)sender;
        if (e.Handled || label.Text.Length == 0 || label.Inlines.Any(inline => inline is Hyperlink)) return;
        for (DependencyObject? node = label; node is Visual; node = VisualTreeHelper.GetParent(node))
            if (node is ButtonBase or TextBoxBase or ComboBox or Selector || node is FrameworkElement { Cursor: { } cursor } && cursor == Cursors.Hand)
                return;
        var layer = AdornerLayer.GetAdornerLayer(label);
        if (layer is null) return;
        var overlay = new SelectionOverlay(label);
        layer.Add(overlay);
        overlay.UpdateLayout();
        var box = overlay.Field;
        box.LostKeyboardFocus += (_, _) => layer.Remove(overlay);
        box.Focus();
        int Index(Point point) => Math.Max(0, box.GetCharacterIndexFromPoint(point, true));
        int anchor = Index(e.GetPosition(box));
        box.Select(anchor, 0);
        if (e.ClickCount > 1) box.SelectAll();
        else
        {
            box.CaptureMouse();
            box.PreviewMouseMove += (_, move) =>
            {
                if (!box.IsMouseCaptured || move.LeftButton != MouseButtonState.Pressed) return;
                int end = Index(move.GetPosition(box));
                box.Select(Math.Min(anchor, end), Math.Abs(end - anchor));
                move.Handled = true;
            };
            box.PreviewMouseLeftButtonUp += (_, up) => { box.ReleaseMouseCapture(); up.Handled = true; };
        }
        box.PreviewKeyDown += (_, key) =>
        {
            if (key.Key != Key.Escape) return;
            Keyboard.ClearFocus();
            key.Handled = true;
        };
        e.Handled = true;
    }

    private sealed class SelectionOverlay : Adorner
    {
        internal TextBox Field { get; }
        internal SelectionOverlay(TextBlock label) : base(label)
        {
            Field = new TextBox
            {
                Text = label.Text, IsReadOnly = true, BorderThickness = new Thickness(0), Padding = label.Padding,
                FontFamily = label.FontFamily, FontSize = label.FontSize, FontStyle = label.FontStyle,
                FontWeight = label.FontWeight, TextAlignment = label.TextAlignment, TextWrapping = label.TextWrapping,
                Background = Brushes.Transparent, Foreground = Brushes.Transparent, CaretBrush = Brushes.Transparent,
                SelectionOpacity = 0.45, SelectionBrush = SystemColors.HighlightBrush,
                VerticalContentAlignment = VerticalAlignment.Top
            };
            if (!double.IsNaN(label.LineHeight)) TextBlock.SetLineHeight(Field, label.LineHeight);
            AddVisualChild(Field);
        }
        protected override int VisualChildrenCount => 1;
        protected override Visual GetVisualChild(int index) => Field;
        protected override Size MeasureOverride(Size size) { Field.Measure(AdornedElement.RenderSize); return AdornedElement.RenderSize; }
        protected override Size ArrangeOverride(Size size) { Field.Arrange(new Rect(size)); return size; }
    }
}
