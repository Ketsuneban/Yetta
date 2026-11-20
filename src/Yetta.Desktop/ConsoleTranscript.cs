using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Yetta.Desktop;

// Adjacent text entries share a document so selection can cross command/output boundaries.
// Interactive Subsy forms stay in the regular focus tree and retain their own input controls.
internal sealed class ConsoleTranscript : StackPanel
{
    private sealed record Entry(UIElement View, Paragraph? Text, Func<bool>? Pinned);
    private readonly List<Entry> _entries = [];

    private readonly int _limit;
    private readonly UIElement? _empty;

    internal ConsoleTranscript(int limit = 500, UIElement? empty = null)
    {
        _limit = limit;
        _empty = empty;
        if (empty is not null) Children.Add(empty);
    }

    internal void Append(UIElement content, Func<bool>? pinned = null)
    {
        if (_entries.Count == 0 && _empty is not null) Children.Remove(_empty);
        if (content is TextBlock text)
        {
            var paragraph = new Paragraph
            {
                Margin = text.Margin, LineHeight = 20, FontSize = text.FontSize,
                FontFamily = text.FontFamily, Foreground = text.Foreground
            };
            foreach (var inline in text.Inlines.ToArray())
            {
                text.Inlines.Remove(inline);
                paragraph.Inlines.Add(inline);
            }
            var document = _entries.LastOrDefault() is { Text: not null, View: RichTextBox last } ? last : null;
            if (document is null)
            {
                document = new RichTextBox
                {
                    Document = new FlowDocument { PagePadding = new Thickness(0) },
                    IsReadOnly = true, IsDocumentEnabled = true, IsTabStop = false,
                    Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    Padding = new Thickness(0), FocusVisualStyle = null,
                    SelectionBrush = SystemColors.HighlightBrush,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
                Children.Add(document);
            }
            document.Document.Blocks.Add(paragraph);
            _entries.Add(new(document, paragraph, pinned));
        }
        else
        {
            Children.Add(content);
            _entries.Add(new(content, null, pinned));
        }
        while (_entries.Count > _limit)
        {
            int index = _entries.FindIndex(entry => entry.Pinned?.Invoke() != true);
            if (index < 0) break;
            var entry = _entries[index];
            _entries.RemoveAt(index);
            if (entry.View is RichTextBox box && entry.Text is { } block)
            {
                box.Document.Blocks.Remove(block);
                if (box.Document.Blocks.Count > 0) continue;
            }
            Children.Remove(entry.View);
        }
    }

    internal void Reset()
    {
        _entries.Clear();
        Children.Clear();
        if (_empty is not null) Children.Add(_empty);
    }
}
