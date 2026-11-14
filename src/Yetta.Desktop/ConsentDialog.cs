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
        var window = GlassDialog.Create(owner, "Yetta · " + WarningPolicy.Heading(level),
            660, 485, minWidth: 520, minHeight: 380);

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 14)
        };
        header.Children.Add(new Border
        {
            Width = 60, Height = 60, CornerRadius = new CornerRadius(16),
            Padding = new Thickness(9), Margin = new Thickness(0, 0, 16, 0),
            BorderBrush = new SolidColorBrush(Color.FromArgb(85, 204, 224, 255)),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(64, 126, 166, 215)),
            Child = new Image
            {
                Source = new BitmapImage(new Uri($"pack://application:,,,/Assets/{iconId}.png")),
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            }
        });
        var heading = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(new TextBlock
        {
            Text = "Yetta · " + WarningPolicy.Heading(level),
            FontSize = 20, FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap
        });
        heading.Children.Add(new TextBlock
        {
            Text = "Подтверждение операции",
            FontSize = 11,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = GlassDialog.SecondaryText
        });
        header.Children.Add(heading);

        var text = new StackPanel { Margin = new Thickness(0, 2, 12, 0) };
        text.Children.Add(new TextBlock
        {
            Text = title, FontWeight = FontWeights.SemiBold, FontSize = 16,
            Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        text.Children.Add(new TextBlock
        {
            Text = description + "\n\n" +
                "Согласие относится только к функциям Yetta. Оно не изменяет разрешения Windows, " +
                "не отключает UAC и не предоставляет ADMIN/SYSTEM.",
            FontSize = 13, Foreground = GlassDialog.SecondaryText,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14)
        });
        if (safeMode)
            text.Children.Add(new TextBlock
            {
                Text = "Windows запущена в безопасном режиме.",
                FontSize = 12, Foreground = Brushes.Gold,
                TextWrapping = TextWrapping.Wrap
            });

        var checkbox = new CheckBox
        {
            Content = "Я прочитал описание и осознанно разрешаю действие",
            Foreground = Brushes.White,
            IsChecked = false,
            FontSize = 12,
            Margin = new Thickness(0, 10, 0, 12),
            VerticalAlignment = VerticalAlignment.Center
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var reject = GlassDialog.Button("Отмена");
        reject.IsCancel = true;
        var accept = GlassDialog.Button("Я согласен", primary: true);
        accept.IsEnabled = false;
        accept.IsDefault = true;
        checkbox.Checked += (_, _) => accept.IsEnabled = checkbox.IsChecked == true;
        checkbox.Unchecked += (_, _) => accept.IsEnabled = false;
        accept.Click += (_, _) =>
        {
            if (checkbox.IsChecked == true) window.DialogResult = true;
        };
        reject.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(reject);
        buttons.Children.Add(accept);

        window.Content = GlassDialog.Surface(
            GlassDialog.Layout(header, text, buttons, checkbox));
        return window.ShowDialog() == true;
    }
}
