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
            Width = 620,
            Height = 445,
            MinWidth = 500,
            MinHeight = 390,
            MaxHeight = SystemParameters.WorkArea.Height * 0.9,
            ResizeMode = ResizeMode.CanResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(44, 48, 58)),
            Foreground = Brushes.White
        };

        // Keep the required consent controls outside the scrollable description.
        // Long explanations and Windows text scaling must never hide the checkbox.
        var root = new Grid { Margin = new Thickness(24, 18, 24, 22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14)
        };
        header.Children.Add(new Image
        {
            Width = 42, Height = 42, Margin = new Thickness(0, 0, 12, 0),
            Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{iconId}.png"))
        });
        header.Children.Add(new TextBlock
        {
            Text = "Yetta · " + WarningPolicy.Heading(level),
            FontSize = 20, FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var descriptionPanel = new StackPanel();
        descriptionPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        descriptionPanel.Children.Add(new TextBlock
        {
            Text = description + "\n\n" +
                   "Это согласие касается только функций Yetta. Оно НЕ изменяет разрешения Windows, " +
                   "не отключает UAC и не предоставляет ADMIN/SYSTEM.",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(211, 214, 225)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12)
        });
        if (safeMode)
            descriptionPanel.Children.Add(new TextBlock
            {
                Text = "Обнаружен безопасный режим Windows (иконка 35).",
                Foreground = Brushes.Gold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });

        var detailsScroll = new ScrollViewer
        {
            Content = descriptionPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(detailsScroll, 1);
        root.Children.Add(detailsScroll);

        var checkbox = new CheckBox
        {
            Content = "Я прочитал описание и осознанно разрешаю действие",
            Foreground = Brushes.White,
            IsChecked = false,
            FontSize = 12,
            Margin = new Thickness(0, 6, 0, 16),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetRow(checkbox, 2);
        root.Children.Add(checkbox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var accept = new Button
        {
            Content = "Я согласен",
            IsEnabled = false,
            IsDefault = true,
            MinWidth = 125,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(8, 0, 0, 0)
        };
        var reject = new Button
        {
            Content = "Отмена",
            IsCancel = true,
            MinWidth = 100,
            Padding = new Thickness(12, 8, 12, 8)
        };

        checkbox.Checked += (_, _) => accept.IsEnabled = checkbox.IsChecked == true;
        checkbox.Unchecked += (_, _) => accept.IsEnabled = false;
        accept.Click += (_, _) =>
        {
            // A checked box is required even if a command reaches the button.
            if (checkbox.IsChecked == true) window.DialogResult = true;
        };
        reject.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(reject);
        buttons.Children.Add(accept);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);

        window.Content = root;
        return window.ShowDialog() == true;
    }
}
