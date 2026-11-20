using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private void OpenMarvineHistory()
    {
        if (_page != "console" || _consoleInput is not { IsReadOnly: false } input) return;
        var dialog = new Window
        {
            Owner = this, Title = "История команд Marvine", Width = 540, Height = 400, MinWidth = 340, MinHeight = 240,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Background = ConsoleSurface
        };
        var panel = new DockPanel { Margin = new Thickness(16) };
        var filter = SearchField("Поиск по истории команд");
        var caption = LiteralConsoleLine("Поиск по истории команд");
        DockPanel.SetDock(caption, Dock.Top);
        panel.Children.Add(caption);
        DockPanel.SetDock(filter, Dock.Top);
        panel.Children.Add(filter);
        var accept = SubsyButton("Вставить команду", () => dialog.DialogResult = true, true);
        DockPanel.SetDock(accept, Dock.Bottom);
        accept.HorizontalAlignment = HorizontalAlignment.Right;
        panel.Children.Add(accept);
        var list = new ListBox
        {
            Background = ConsolePanel, Foreground = ConsoleInk, FontFamily = ConsoleFont,
            BorderThickness = new Thickness(0), Padding = new Thickness(4)
        };
        System.Windows.Automation.AutomationProperties.SetName(list, "Найденные команды");
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var results = new Grid();
        results.Children.Add(list);
        var empty = LiteralConsoleLine("");
        empty.Margin = new Thickness(12);
        empty.IsHitTestVisible = false;
        results.Children.Add(empty);
        panel.Children.Add(results);
        dialog.Content = panel;
        var commands = _enteredCommands.AsEnumerable().Reverse().Distinct(StringComparer.Ordinal).ToArray();
        void Refresh()
        {
            string query = filter.Text.Trim();
            var matches = commands.Where(c => c.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            list.ItemsSource = matches;
            list.SelectedIndex = matches.Length == 0 ? -1 : 0;
            if (list.SelectedItem is { } first) list.ScrollIntoView(first);
            accept.IsEnabled = matches.Length > 0;
            empty.Visibility = matches.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            empty.Text = commands.Length == 0 ? "В истории пока нет команд." : "Совпадений нет.";
        }
        filter.TextChanged += (_, _) => Refresh();
        filter.PreviewKeyDown += (_, e) =>
        {
            if (list.Items.Count == 0 || e.Key is not (Key.Up or Key.Down)) return;
            list.SelectedIndex = Math.Clamp(list.SelectedIndex + (e.Key == Key.Up ? -1 : 1), 0, list.Items.Count - 1);
            list.ScrollIntoView(list.SelectedItem);
            e.Handled = true;
        };
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; dialog.DialogResult = false; }
            else if (e.Key == Key.Enter && list.SelectedItem is string) { e.Handled = true; dialog.DialogResult = true; }
        };
        list.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject hit && ItemsControl.ContainerFromElement(list, hit) is ListBoxItem)
                dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => filter.Focus();
        Refresh();
        if (dialog.ShowDialog() == true && list.SelectedItem is string command)
        {
            input.Text = command;
            _commandHistoryIndex = _enteredCommands.Count;
            input.CaretIndex = input.Text.Length;
        }
        input.Focus();
    }
}
