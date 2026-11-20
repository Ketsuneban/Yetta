using System.Diagnostics;
using System.Management;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private Window MiniWindow(string title, UIElement body, bool modal = true)
    {
        var w = new Window
        {
            Owner = this, Title = title, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
            Width = 460, SizeToContent = SizeToContent.Height, Background = Surface, Foreground = Ink,
            FontFamily = SystemFonts.MessageFontFamily, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false
        };
        var header = new DockPanel { Background = Resource("HoverBackground"), Margin = new Thickness(0, 0, 0, 1) };
        var close = new Button
        {
            Content = "×", Width = 38, Height = 32, Margin = new Thickness(4), FontSize = 19,
            Foreground = Ink, Cursor = Cursors.Hand, Style = (Style)FindResource("DialogCloseButton"),
            ToolTip = "Закрыть", IsCancel = modal
        };
        if (!modal) close.Click += (_, _) => w.Close();
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        var caption = Text(title, 13, heading: true);
        caption.VerticalAlignment = VerticalAlignment.Center;
        caption.Margin = new Thickness(18, 0, 0, 0);
        header.Children.Add(caption);
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed && !close.IsMouseOver)
            {
                try { w.DragMove(); } catch (InvalidOperationException) { }
            }
        };
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new Border { Child = body, Padding = new Thickness(20) });
        w.Content = root;
        w.SourceInitialized += (_, _) => WarningGlass.Apply(w);
        return w;
    }

    private bool ApproveAction(string title, string target, string reason, bool destructive = false, string? holdLabel = null)
    {
        var panel = new StackPanel();
        var headline = Text(title, 19, heading: true);
        headline.Margin = new Thickness(0, 0, 0, 9);
        panel.Children.Add(headline);
        panel.Children.Add(new Border { Child = Text(target), Padding = new Thickness(12, 7, 12, 7),
            Background = Resource("HoverBackground"), CornerRadius = new CornerRadius(8) });
        var message = Text(reason, muted: true);
        message.Margin = new Thickness(0, 10, 0, 12);
        panel.Children.Add(message);

        var dialog = MiniWindow("Подтверждение действия", panel);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = ActionButton("Отмена", () => dialog.DialogResult = false);
        var confirm = ActionButton(destructive ? holdLabel ?? "Удерживай для удаления" : "Продолжить",
            () => { if (!destructive) dialog.DialogResult = true; }, primary: true);
        cancel.Margin = new Thickness(0, 0, 8, 0);
        confirm.Margin = new Thickness(0);
        cancel.MinWidth = 100;
        confirm.MinWidth = 122;
        buttons.Children.Add(cancel);
        buttons.Children.Add(confirm);
        panel.Children.Add(buttons);

        if (destructive)
        {
            var progress = new ProgressBar { Minimum = 0, Maximum = 1, Height = 3, Opacity = 0,
                Margin = new Thickness(0, 9, 0, 0), Foreground = Accent };
            panel.Children.Add(progress);
            var hold = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            void Release()
            {
                hold.Stop();
                progress.BeginAnimation(ProgressBar.ValueProperty, null);
                progress.Value = 0;
                progress.Opacity = 0;
            }
            void Press()
            {
                if (hold.IsEnabled) return;
                progress.Opacity = 1;
                progress.BeginAnimation(ProgressBar.ValueProperty, new DoubleAnimation(0, 1, hold.Interval));
                hold.Start();
            }
            hold.Tick += (_, _) => { Release(); dialog.DialogResult = true; };
            confirm.PreviewMouseLeftButtonDown += (_, e) => { confirm.Focus(); Press(); e.Handled = true; };
            confirm.PreviewMouseLeftButtonUp += (_, _) => Release();
            confirm.MouseLeave += (_, _) => Release();
            confirm.LostKeyboardFocus += (_, _) => Release();
            confirm.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Enter or Key.Space) { if (!e.IsRepeat) Press(); e.Handled = true; }
            };
            confirm.PreviewKeyUp += (_, e) => { if (e.Key is Key.Enter or Key.Space) { Release(); e.Handled = true; } };
            dialog.Deactivated += (_, _) => Release();
            dialog.Closed += (_, _) => Release();
        }

        dialog.Loaded += (_, _) => cancel.Focus();
        return dialog.ShowDialog() == true;
    }

    private Border BuildDefenderSwitchPreview()
    {
        bool previewEnabled = true;
        var previewIcon = IconAssets.Asset("Defender/Enabled.png", 26);
        previewIcon.Margin = new Thickness(0, 0, 10, 0);
        var previewStatus = Text("Включено", 12, muted: true);
        var toggle = new ToggleButton
        {
            Style = (Style)FindResource("SettingToggle"), IsChecked = previewEnabled,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0)
        };
        toggle.Resources["HoverBackground"] = Surface;
        var previewPill = new Border
        {
            Child = previewStatus, Background = Surface, CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left
        };
        System.Windows.Automation.AutomationProperties.SetName(toggle, "Демонстрационный переключатель Defender");
        toggle.Click += (_, _) =>
        {
            bool next = toggle.IsChecked == true;
            toggle.IsChecked = previewEnabled;
            if (!next && !ApproveAction("Выключить в предпросмотре?", "Windows Defender · демо",
                "Подтверждение изменит только вид переключателя и значка.",
                destructive: true, holdLabel: "Удерживай для подтверждения"))
                return;

            previewEnabled = next;
            toggle.IsChecked = next;
            previewIcon.Source = IconAssets.Asset(next ? "Defender/Enabled.png" : "Defender/Disabled.png", 26).Source;
            previewStatus.Text = next ? "✓ Включено" : "✓ Выключено";
            if (UiMotion.AnimationsEnabled)
                previewPill.BeginAnimation(OpacityProperty, new DoubleAnimation(.4, 1, TimeSpan.FromMilliseconds(180)));
        };

        var row = new DockPanel();
        DockPanel.SetDock(toggle, Dock.Right);
        row.Children.Add(toggle);
        row.Children.Add(previewIcon);
        row.Children.Add(new StackPanel
        {
            Children = { Text("Предпросмотр переключателя", 13, heading: true), previewPill }
        });
        return new Border
        {
            Child = row, Background = Resource("HoverBackground"), CornerRadius = new CornerRadius(8),
            Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(12, 8, 12, 8)
        };
    }

    private void BuildDeveloperSecurity()
    {
        AddSection("Безопасность");
        var panel = new StackPanel();
        var identity = new DockPanel();
        var icon = IconAssets.Asset("Defender/Enabled.png", 32);
        icon.Margin = new Thickness(0, 0, 11, 0);
        identity.Children.Add(icon);
        var caption = new StackPanel();
        caption.Children.Add(Text("Windows Defender", 17, heading: true));
        caption.Children.Add(Text("Защита Windows от вредоносных программ. Здесь отображается состояние защиты в реальном времени.", muted: true));
        identity.Children.Add(caption);
        panel.Children.Add(identity);

        var controls = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var status = Text("Проверка…", 12, heading: true);
        var pill = new Border { CornerRadius = new CornerRadius(15), Background = Resource("HoverBackground"),
            Child = status, Margin = new Thickness(0, 0, 10, 10), Padding = new Thickness(12, 6, 12, 6),
            VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(pill);
        var refresh = ActionButton("Проверить статус", () => { });
        refresh.Click += async (_, _) => await RefreshAsync();
        controls.Children.Add(refresh);
        controls.Children.Add(ActionButton("Открыть Безопасность Windows", () =>
        {
            if (!ApproveAction("Открыть Безопасность Windows", "Windows Defender",
                "Управление защитой откроется в системном приложении Windows.")) return;
            try { Process.Start(new ProcessStartInfo("windowsdefender:") { UseShellExecute = true })?.Dispose(); }
            catch (System.ComponentModel.Win32Exception ex) { ShowYettaNotice("Безопасность Windows не открылась", ex.Message, WarningKind.Caution); }
        }));
        panel.Children.Add(controls);
        panel.Children.Add(BuildDefenderSwitchPreview());
        ContentPanel.Children.Add(Card(panel));
        panel.Loaded += async (_, _) => await RefreshAsync();

        async Task RefreshAsync()
        {
            if (!refresh.IsEnabled) return;
            refresh.IsEnabled = false;
            status.Text = "Проверка…";
            status.Foreground = Muted;
            bool? enabled = await Task.Run(ReadDefender);
            status.Text = enabled switch { true => "✓  Защита активна", false => "Защита неактивна", _ => "Статус недоступен" };
            status.Foreground = enabled switch { true => Hex("#4CCA89"), false => Hex("#D99045"), _ => Muted };
            icon.Source = IconAssets.Asset(enabled == false ? "Defender/Disabled.png" : "Defender/Enabled.png", 32).Source;
            if (UiMotion.AnimationsEnabled)
                pill.BeginAnimation(OpacityProperty, new DoubleAnimation(.45, 1, TimeSpan.FromMilliseconds(200)));
            refresh.IsEnabled = true;
        }
    }

    private static bool? ReadDefender()
    {
        try
        {
            using var query = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender",
                "SELECT AntivirusEnabled, RealTimeProtectionEnabled FROM MSFT_MpComputerStatus",
                new EnumerationOptions { Timeout = TimeSpan.FromSeconds(5) });
            using var result = query.Get();
            foreach (ManagementObject item in result)
            {
                using (item)
                    return item["AntivirusEnabled"] is bool antivirus &&
                        item["RealTimeProtectionEnabled"] is bool realtime ? antivirus && realtime : null;
            }
            return null;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
