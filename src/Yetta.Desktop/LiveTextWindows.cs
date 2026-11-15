using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>
/// Updates visible, static WPF labels without destroying controls or erasing
/// entered text, checkboxes, warnings or an active PowerShell session.
/// DynamicResource-backed values update through App.Resources instead.
/// </summary>
internal static class LiveTextWindows
{
    internal static void Refresh(Window window, TextCatalogChangedEventArgs change)
    {
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in change.Previous)
        {
            if (change.Current.TryGetValue(pair.Key, out var updated) &&
                !string.Equals(pair.Value, updated, StringComparison.Ordinal))
                replacements[pair.Value] = updated;
        }
        if (replacements.Count == 0) return;
        var visited = new HashSet<DependencyObject>();
        Walk(window);

        void Walk(DependencyObject element)
        {
            if (!visited.Add(element)) return;
            if (element is Window) Replace(element, Window.TitleProperty);
            if (element is TextBlock) Replace(element, TextBlock.TextProperty);
            if (element is Run) Replace(element, Run.TextProperty);
            if (element is ContentControl) Replace(element, ContentControl.ContentProperty);
            if (element is HeaderedContentControl) Replace(element, HeaderedContentControl.HeaderProperty);
            if (element is FrameworkElement) Replace(element, FrameworkElement.ToolTipProperty);

            foreach (object child in LogicalTreeHelper.GetChildren(element))
                if (child is DependencyObject nested) Walk(nested);
            if (element is Visual or Visual3D)
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
                    Walk(VisualTreeHelper.GetChild(element, i));
        }

        void Replace(DependencyObject element, DependencyProperty property)
        {
            // Never override DynamicResource, Binding or any other expression.
            if (DependencyPropertyHelper.GetValueSource(element, property).IsExpression) return;
            if (element.GetValue(property) is string existing &&
                replacements.TryGetValue(existing, out var replacement))
                element.SetValue(property, replacement);
        }
    }
}
