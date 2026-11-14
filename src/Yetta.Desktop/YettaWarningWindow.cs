using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yetta.Core;

namespace Yetta.Desktop;

internal enum WarningKind { Standard, Caution, Dangerous, Critical, Fatal, Agreement, Dax }

internal sealed record WarningRequest(
    WarningKind Kind,
    string Reason,
    string Description,
    string Subject = "Yetta",
    string? SourcePath = null,
    string? Publisher = null,
    bool RequireAcknowledgement = false,
    bool RequireTty = false,
    bool PotentialDamage = false,
    Action? RecoveryNavigation = null);

/// <summary>
/// Seven documented warning layouts. A visual permission or TTY acknowledgement
/// never elevates OS permissions; dangerous actions need independent checks.
/// </summary>
internal static class YettaWarningWindow
{
    private static readonly Brush Pale = new SolidColorBrush(Color.FromRgb(247, 248, 255));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(211, 221, 238));

    private static Color ColorFor(WarningKind kind) => kind switch
    {
        WarningKind.Caution => Color.FromRgb(239, 188, 72),
        WarningKind.Dangerous => Color.FromRgb(250, 92, 106),
        WarningKind.Critical => Color.FromRgb(197, 43, 73),
        WarningKind.Fatal => Color.FromRgb(131, 140, 162),
        WarningKind.Agreement => Color.FromRgb(180, 187, 208),
        WarningKind.Dax => Color.FromRgb(133, 184, 251),
        _ => Color.FromRgb(153, 222, 241)
    };

    private static int HeadingIcon(WarningKind kind) => kind switch
    {
        WarningKind.Caution => 23, WarningKind.Dangerous => 24,
        WarningKind.Critical => 25, WarningKind.Fatal => 48,
        WarningKind.Agreement => 2, WarningKind.Dax => 33, _ => 22
    };

    private static string Title(WarningKind kind) => kind switch
    {
        WarningKind.Caution => "Предупреждение",
        WarningKind.Dangerous => "Опасное действие",
        WarningKind.Critical => "Критическое предупреждение",
        WarningKind.Fatal => "Смертельная ошибка",
        WarningKind.Agreement => "Соглашение",
        WarningKind.Dax => "DAX",
        _ => "Подтверждение действия"
    };

    private static int ObjectIcon(WarningRequest request)
    {
        if (request.Kind == WarningKind.Fatal) return 48;
        if (request.Kind == WarningKind.Agreement) return 2;
        if (request.Kind == WarningKind.Dax) return 29;
        var path = request.SourcePath;
        if (string.IsNullOrWhiteSpace(path)) return 46;
        if (Directory.Exists(path)) return 43;
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg" => 47,
            ".zip" or ".7z" or ".rar" or ".tar" or ".gz" => 44,
            ".txt" or ".doc" or ".docx" or ".pdf" or ".rtf" or ".md" => 39,
            ".py" => 37,
            ".exe" or ".msi" or ".bat" or ".cmd" or ".ps1" => 41,
            ".sys" or ".dll" => 42,
            _ => 46
        };
    }

    private static int Status(WarningRequest request, string activeProfile)
    {
        if (request.Kind == WarningKind.Fatal) return 0;
        if (request.Kind is WarningKind.Agreement or WarningKind.Dax) return 35;
        if (activeProfile is "Guest" or "SafeUser" || ConsentDialog.InWindowsSafeMode) return 35;
        return request.PotentialDamage || request.Kind is WarningKind.Dangerous or WarningKind.Critical
            ? 26 : 27;
    }

    private static TextBlock Label(string text, double size, Brush brush, bool bold = false) =>
        new()
        {
            Text = text, FontSize = size, Foreground = brush,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };

    private static void OpenSearch(string subject)
    {
        string url = "https://www.google.com/search?q=" + Uri.EscapeDataString(subject);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private static void OpenLocation(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        var args = Directory.Exists(path) ? "\"" + path + "\"" : "/select,\"" + path + "\"";
        Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
    }

    /// <summary>Returns true only after the required acknowledgements are satisfied.</summary>
    internal static bool Show(Window owner, WarningRequest request,
        string activeProfile = "User", bool nonModal = false)
    {
        var window = GlassDialog.Create(owner, "Yetta · " + Title(request.Kind),
            730, 575, minWidth: 570, minHeight: 435);
        if (request.Kind == WarningKind.Fatal)
        {
            // Above all Yetta windows; never silently launches OS recovery.
            window.Topmost = true;
        }

        Color accent = ColorFor(request.Kind);
        var top = new Grid { Margin = new Thickness(0, 0, 0, 15) };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleIcon = IconAssets.Image(HeadingIcon(request.Kind), 35);
        titleIcon.Margin = new Thickness(0, 0, 12, 0);
        top.Children.Add(titleIcon);
        var heading = Label(Title(request.Kind) + " · " + request.Reason, 17, Pale, bold: true);
        heading.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(heading, 1);
        top.Children.Add(heading);
        var close = new Button
        {
            Content = IconAssets.Image(16, 20),
            Width = 36, Height = 34,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Закрыть"
        };
        void Finish(bool accepted)
        {
            if (nonModal) window.Close();
            else window.DialogResult = accepted;
        }
        close.Click += (_, _) => Finish(false);
        Grid.SetColumn(close, 2);
        top.Children.Add(close);

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var subject = new Grid { Margin = new Thickness(0, 0, 0, 15) };
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        int objectIcon = ObjectIcon(request);
        var subjectIcon = request.Kind == WarningKind.Dax
            ? new Image
            {
                Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Assets/dax-logo.png")),
                Width = 46, Height = 46, Stretch = Stretch.Uniform
            }
            : request.Kind == WarningKind.Agreement
                ? new Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/Assets/yetta-logo.png")),
                    Width = 46, Height = 46, Stretch = Stretch.Uniform
                }
                : IconAssets.Image(objectIcon, 46);
        var container = new Border
        {
            Width = 63, Height = 63, Padding = new Thickness(7),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(125, accent.R, accent.G, accent.B)),
            Background = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(15),
            Child = subjectIcon, Margin = new Thickness(0, 0, 12, 0)
        };
        subject.Children.Add(container);
        var subjectContent = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var subjectName = new TextBlock
        {
            Text = request.Subject, Foreground = Pale,
            FontSize = 17, FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            TextWrapping = TextWrapping.Wrap,
            ToolTip = "Найти сведения в браузере"
        };
        subjectName.MouseLeftButtonUp += (_, _) =>
        {
            try { OpenSearch(request.Subject); }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
        };
        subjectContent.Children.Add(subjectName);
        string publisher = request.Publisher ?? "Неизвестный издатель";
        var publisherText = Label(publisher, 11, Dim);
        publisherText.Margin = new Thickness(0, 5, 0, 0);
        publisherText.Cursor = Cursors.Hand;
        publisherText.ToolTip = "Открыть расположение файла, если оно известно";
        publisherText.MouseLeftButtonUp += (_, _) =>
        {
            try { OpenLocation(request.SourcePath); }
            catch (Win32Exception) { }
        };
        subjectContent.Children.Add(publisherText);
        Grid.SetColumn(subjectContent, 1);
        subject.Children.Add(subjectContent);
        main.Children.Add(subject);

        var paragraph = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 18, 26, 46)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(18, 17, 18, 17),
            Margin = new Thickness(0, 0, 0, 5),
            Child = Label(request.Description, 13, Pale)
        };
        ((TextBlock)paragraph.Child).LineHeight = 21;
        Grid.SetRow(paragraph, 1);
        main.Children.Add(paragraph);

        var bottom = new Grid();
        bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        bottom.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var gate = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        bottom.Children.Add(gate);

        bool requiresCheck = request.RequireAcknowledgement ||
            request.Kind == WarningKind.Agreement;
        bool requiresKey = request.Kind is not (WarningKind.Fatal or WarningKind.Dax or WarningKind.Agreement) &&
            (request.RequireTty || request.Kind == WarningKind.Critical || activeProfile == "System");

        CheckBox? checkbox = null;
        if (requiresCheck)
        {
            checkbox = new CheckBox
            {
                Content = "Я прочитал предупреждение и осознанно подтверждаю действие",
                Foreground = Pale,
                FontSize = 12, Margin = new Thickness(0, 0, 0, 9),
                IsChecked = false
            };
            gate.Children.Add(checkbox);
        }

        PasswordBox? secret = null;
        TextBox? visibleSecret = null;
        TextBlock? keyStatus = null;
        Border? secretBorder = null;
        if (requiresKey)
        {
            var keyRow = new DockPanel { LastChildFill = true };
            keyRow.Children.Add(IconAssets.Image(36, 26));
            var toggle = new Button
            {
                Content = IconAssets.Image(49, 24),
                Width = 36, MinHeight = 38, Margin = new Thickness(8, 0, 0, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand, ToolTip = "Показать/скрыть TTY-ключ"
            };
            DockPanel.SetDock(toggle, Dock.Right);
            keyRow.Children.Add(toggle);
            secret = new PasswordBox
            {
                PasswordChar = '•', MaxLength = 64, FontSize = 13,
                Foreground = Pale, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(8),
                CaretBrush = Brushes.White, ToolTip = "TTY-Ключ"
            };
            visibleSecret = new TextBox
            {
                Visibility = Visibility.Collapsed, MaxLength = 64,
                Foreground = Pale, Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(8),
                FontSize = 13, CaretBrush = Brushes.White, ToolTip = "TTY-Ключ"
            };
            var input = new Grid();
            input.Children.Add(secret);
            input.Children.Add(visibleSecret);
            keyRow.Children.Add(input);
            secretBorder = new Border
            {
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 160, 183, 218)),
                Background = new SolidColorBrush(Color.FromArgb(80, 29, 40, 63)),
                Padding = new Thickness(10, 4, 10, 4),
                Child = keyRow
            };
            gate.Children.Add(secretBorder);
            keyStatus = Label("TTY-Ключ · 64 символа", 11, Dim);
            keyStatus.Margin = new Thickness(8, 4, 0, 0);
            gate.Children.Add(keyStatus);

            var pw = secret;
            var shown = visibleSecret;
            toggle.Click += (_, _) =>
            {
                if (shown.Visibility == Visibility.Collapsed)
                {
                    shown.Text = pw.Password;
                    pw.Visibility = Visibility.Collapsed;
                    shown.Visibility = Visibility.Visible;
                    toggle.Content = IconAssets.Image(50, 24);
                }
                else
                {
                    pw.Password = shown.Text;
                    shown.Visibility = Visibility.Collapsed;
                    pw.Visibility = Visibility.Visible;
                    toggle.Content = IconAssets.Image(49, 24);
                }
            };
        }

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.Margin = new Thickness(0, 6, 0, 0);
        Grid.SetRow(footer, 1);
        bottom.Children.Add(footer);

        int state = Status(request, activeProfile);
        if (state != 0)
        {
            var status = IconAssets.Image(state, 29);
            status.HorizontalAlignment = HorizontalAlignment.Left;
            status.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(status);
        }
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);

        bool isAgreement = request.Kind == WarningKind.Agreement;
        bool oneButton = isAgreement || request.Kind is WarningKind.Fatal or WarningKind.Dax;
        Button? deny = null;
        if (!oneButton)
        {
            deny = GlassDialog.Button("Запретить");
            deny.Click += (_, _) => Finish(false);
            actions.Children.Add(deny);
        }
        string actionName = request.Kind switch
        {
            WarningKind.Fatal => "Сброс",
            WarningKind.Dax => "Ок",
            WarningKind.Agreement => "Согласен",
            _ => "Разрешить"
        };
        var allow = GlassDialog.Button(actionName, primary: true);
        if (oneButton) allow.MinWidth = 240;
        actions.Children.Add(allow);

        void CheckGate()
        {
            bool checkedOk = checkbox is null || checkbox.IsChecked == true;
            string typed = visibleSecret?.Visibility == Visibility.Visible
                ? visibleSecret.Text : secret?.Password ?? "";
            bool keyOk = !requiresKey;
            if (requiresKey)
            {
                keyOk = false;
                if (typed.Length == 64)
                {
                    try { keyOk = TtyKeyStore.Matches(typed); }
                    catch (Exception ex) when (ex is Win32Exception or InvalidDataException)
                    {
                        keyStatus!.Text = "Хранилище TTY недоступно";
                    }
                }
                if (keyStatus is not null)
                {
                    keyStatus.Text = typed.Length == 0 ? "TTY-Ключ · 64 символа"
                        : keyOk ? "Ключ совпадает ✔" : "Ключ неверный ✘";
                    keyStatus.Foreground = keyOk ? Brushes.LightGreen : typed.Length == 0 ? Dim : Brushes.Salmon;
                }
                if (secretBorder is not null)
                    secretBorder.BorderBrush = keyOk ? Brushes.LightGreen : typed.Length == 0
                        ? new SolidColorBrush(Color.FromArgb(120, 160, 183, 218)) : Brushes.Salmon;
            }
            allow.IsEnabled = checkedOk && keyOk;
        }

        if (checkbox is not null)
        {
            checkbox.Checked += (_, _) => CheckGate();
            checkbox.Unchecked += (_, _) => CheckGate();
        }
        if (secret is not null) secret.PasswordChanged += (_, _) => CheckGate();
        if (visibleSecret is not null) visibleSecret.TextChanged += (_, _) => CheckGate();

        allow.Click += (_, _) =>
        {
            CheckGate();
            if (!allow.IsEnabled) return;
            if (request.Kind == WarningKind.Fatal)
                request.RecoveryNavigation?.Invoke();
            Finish(true);
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(top, 0);
        layout.Children.Add(top);
        var scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            PanningMode = PanningMode.VerticalOnly,
            Content = main, Margin = new Thickness(0, 3, 0, 12)
        };
        Grid.SetRow(scroller, 1);
        layout.Children.Add(scroller);
        Grid.SetRow(bottom, 2);
        layout.Children.Add(bottom);
        window.Content = GlassDialog.Surface(layout, new Thickness(24), owner);
        CheckGate();
        DaxAudit.Write("warning", "shown", request.Kind + ": " + request.Reason);
        if (nonModal)
        {
            // Informational windows and UI previews may overlap like Explorer
            // windows. No privileged action is approved through this path.
            DaxAudit.Write("warning", "opened_nonmodal", request.Kind.ToString());
            window.Show();
            return false;
        }
        bool result = window.ShowDialog() == true;
        DaxAudit.Write("warning", result ? "accepted" : "dismissed", request.Kind + ": " + request.Reason);
        return result;
    }
}
