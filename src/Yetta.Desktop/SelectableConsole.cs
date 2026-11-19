using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Yetta.Desktop;
internal static class SelectableConsole
{
    internal static RichTextBox Wrap(TextBlock text)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 20 };
        foreach (Inline inline in text.Inlines.ToArray()) { text.Inlines.Remove(inline); paragraph.Inlines.Add(inline); }
        var document = new FlowDocument(paragraph) { PagePadding = new Thickness(0),
            FontFamily = text.FontFamily, FontSize = text.FontSize, Foreground = text.Foreground };
        return new RichTextBox { Document = document, IsReadOnly = true, IsDocumentEnabled = true,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            Margin = text.Margin, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, FocusVisualStyle = null,
            SelectionBrush = new SolidColorBrush(Color.FromRgb(65, 103, 157)), SelectionOpacity = .7 };
    }
}
