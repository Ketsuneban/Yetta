using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private TextBox SearchField(string label)
    {
        var field = new TextBox
        {
            MaxLength = 120, MinHeight = 36, Padding = new Thickness(9, 7, 9, 7),
            Margin = new Thickness(0, 0, 0, 8), Foreground = Ink, CaretBrush = Ink,
            Background = Surface, BorderThickness = new Thickness(0), ToolTip = label
        };
        System.Windows.Automation.AutomationProperties.SetName(field, label);
        return field;
    }

    private void OnMainShortcut(object sender, KeyEventArgs args)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control) return;
        if (args.Key == Key.K)
        {
            args.Handled = true;
            OpenPageFinder();
        }
        else if (args.Key == Key.L && _page == "console")
        {
            args.Handled = true;
            _consoleInput?.Focus();
        }
    }

    private void OpenPageFinder()
    {
        (string Id, string Label)[] pages =
        [
            ("overview", TallowText.Get("ui.main.0001")), ("info", TallowText.Get("ui.main.0002")),
            ("console", "Консоль Marvine"), ("guard", "DAX Guard · Профили доступа"),
            ("logs", "DAX Logs · Журнал"), ("activity", TallowText.Get("ui.main.0006")),
            ("recovery", "DAX Recovery · Диагностика"), ("settings", TallowText.Get("ui.main.0008")),
            ("sources", TallowText.Get("ui.main.0009"))
        ];
        if (AdministratorActive) pages = [.. pages, ("developers", "Для разработчиков")];
        var window = new Window
        {
            Owner = this, Title = "Перейти в раздел Yetta", Width = 480, Height = 410,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, Background = Resource("WindowBackground")
        };
        var panel = new DockPanel { Margin = new Thickness(20), LastChildFill = true };
        var header = new StackPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var title = Text("Перейти в раздел", 19, heading: true);
        title.Margin = new Thickness(0, 0, 0, 10);
        header.Children.Add(title);
        var filter = SearchField("Название раздела");
        header.Children.Add(filter);
        var choices = new StackPanel();
        panel.Children.Add(new EdgeScrollViewer { Content = choices, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        void Go(string page)
        {
            Navigate(page);
            window.Close();
        }
        void Refresh()
        {
            choices.Children.Clear();
            string query = filter.Text.Trim();
            foreach (var page in pages.Where(page => page.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                         page.Label.Contains(query, StringComparison.CurrentCultureIgnoreCase)))
            {
                var action = ActionButton(page.Label, () => Go(page.Id));
                action.HorizontalAlignment = HorizontalAlignment.Stretch;
                action.Margin = new Thickness(0, 0, 0, 6);
                choices.Children.Add(action);
            }
            if (choices.Children.Count == 0) choices.Children.Add(Text("Раздел не найден.", 13, muted: true));
        }
        filter.TextChanged += (_, _) => Refresh();
        filter.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter || choices.Children.OfType<Button>().FirstOrDefault() is not { } first) return;
            args.Handled = true;
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };
        window.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            args.Handled = true;
            window.Close();
        };
        window.Content = panel;
        window.Loaded += (_, _) => filter.Focus();
        UiMotion.AnimateOpen(panel);
        Refresh();
        window.ShowDialog();
    }

    private void OnOpenPageFinder(object sender, RoutedEventArgs args) => OpenPageFinder();
}
