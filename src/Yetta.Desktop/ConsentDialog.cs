using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>A visible, opt-in Yetta consent dialog; never changes OS privileges.</summary>
public static class ConsentDialog
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
    public static bool InWindowsSafeMode => OperatingSystem.IsWindows() && GetSystemMetrics(67) != 0;

    public static bool Confirm(Window owner, string title, string description,
        YettaWarningLevel level = YettaWarningLevel.Consent)
    {
        bool safeMode = InWindowsSafeMode;
        int iconId = WarningPolicy.StatusIcon(level, safeMode);
        var window = new Window
        {
            Owner = owner,
            Title = "Yetta · " + WarningPolicy.Heading(level),
            Width = 560,
            Height = 365,
            MinWidth = 480,
            MaxHeight = 500,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(44, 48, 58)),
            Foreground = Brushes.White
        };
        var root = new DockPanel { Margin = new Thickness(24) };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        var icon = new Image
        {
            Width = 42, Height = 42, Margin = new Thickness(0, 0, 12, 0),
            Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{iconId}.png"))
        };
        header.Children.Add(icon);
        header.Children.Add(new TextBlock
        {
            Text = "Yetta · " + WarningPolicy.Heading(level),
            FontSize = 20, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var accept = new Button { Content = "Я согласен", IsEnabled = false, MinWidth = 125, Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(8, 0, 0, 0) };
        var reject = new Button { Content = "Отмена", MinWidth = 100, Padding = new Thickness(12, 8, 12, 8) };
        accept.Click += (_, _) => window.DialogResult = true;
        reject.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(reject);
        buttons.Children.Add(accept);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeights.SemiBold, FontSize = 15,
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        body.Children.Add(new TextBlock
        {
            Text = description + "\n\n" +
                   "Это согласие касается только функций Yetta. Оно НЕ изменяет разрешения Windows, " +
                   "не отключает UAC и не предоставляет ADMIN/SYSTEM.",
            FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(211, 214, 225)),
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18)
        });
        if (safeMode)
            body.Children.Add(new TextBlock
            {
                Text = "Обнаружен безопасный режим Windows (иконка 35).",
                Foreground = Brushes.Gold, Margin = new Thickness(0, 0, 0, 8)
            });
        var checkbox = new CheckBox
        {
            Content = "Я прочитал описание и осознанно разрешаю действие",
            Foreground = Brushes.White, IsChecked = false
        };
        checkbox.Checked += (_, _) => accept.IsEnabled = true;
        checkbox.Unchecked += (_, _) => accept.IsEnabled = false;
        body.Children.Add(checkbox);
        root.Children.Add(body);
        window.Content = root;
        return window.ShowDialog() == true;
    }
}
