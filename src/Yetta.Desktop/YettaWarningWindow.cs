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
    Action? RecoveryNavigation = null,
    int? HeaderIconId = null);

/// <summary>
/// Seven documented warning layouts. A visual permission or TTY acknowledgement
/// never elevates OS permissions; dangerous actions need independent checks.
/// </summary>
internal static class YettaWarningWindow
{
    private static readonly Brush Pale = new SolidColorBrush(Color.FromRgb(28, 35, 49));
    private static readonly Brush Dim = new SolidColorBrush(Color.FromRgb(76, 87, 107));

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

    // Title-area illustrations identify the event/source, not the warning severity.
    // Producers can supply HeaderIconId explicitly; the default is informational.
    private static int HeadingIcon(WarningRequest request) =>
        request.HeaderIconId is >= 1 and <= 50 ? request.HeaderIconId.Value : 22;

    private static Color BackgroundFor(WarningKind kind) => kind switch
    {
        WarningKind.Caution => Color.FromRgb(255, 245, 219),
        WarningKind.Dangerous => Color.FromRgb(255, 232, 231),
        WarningKind.Critical => Color.FromRgb(247, 218, 224),
        WarningKind.Fatal => Color.FromRgb(225, 228, 234),
        WarningKind.Agreement => Color.FromRgb(235, 238, 247),
        WarningKind.Dax => Color.FromRgb(225, 237, 251),
        _ => Color.FromRgb(233, 246, 251)
    };

    private static Button WindowsAction(string caption, bool primary = false)
    {
        var button = new Button
        {
            Content = caption,
            MinWidth = 145, Height = 39,
            Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(6, 0, 6, 0),
            FontSize = 13, FontWeight = FontWeights.Medium,
            Foreground = Pale,
            Background = primary
                ? new SolidColorBrush(Color.FromRgb(183, 215, 255))
                : new SolidColorBrush(Color.FromRgb(248, 249, 252)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(142, 154, 176)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand
        };
        var template = new ControlTemplate(typeof(Button));
        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(0));
        frame.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        frame.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        frame.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var center = new FrameworkElementFactory(typeof(ContentPresenter));
        center.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        center.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        frame.AppendChild(center);
        template.VisualTree = frame;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.78));
        template.Triggers.Add(hover);
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.45));
        template.Triggers.Add(disabled);
        button.Template = template;
        return button;
    }

    private static CheckBox Acknowledgement()
    {
        var checkbox = new CheckBox
        {
            Content = "Я прочитал предупреждение и осознанно подтверждаю действие",
            Foreground = Pale, FontSize = 12, IsChecked = false,
            Margin = new Thickness(0, 0, 0, 9),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var template = new ControlTemplate(typeof(CheckBox));
        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var square = new FrameworkElementFactory(typeof(Border));
        square.Name = "CheckSurface";
        square.SetValue(FrameworkElement.WidthProperty, 20.0);
        square.SetValue(FrameworkElement.HeightProperty, 20.0);
        square.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 11, 0));
        square.SetValue(Border.CornerRadiusProperty, new CornerRadius(0));
        square.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
        square.SetValue(Border.BackgroundProperty, Brushes.White);
        square.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(112, 127, 153)));
        var mark = new FrameworkElementFactory(typeof(TextBlock));
        mark.Name = "CheckMark";
        mark.SetValue(TextBlock.TextProperty, "✓");
        mark.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
        mark.SetValue(TextBlock.FontSizeProperty, 15.0);
        mark.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        mark.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        mark.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
        square.AppendChild(mark);
        row.AppendChild(square);
        var text = new FrameworkElementFactory(typeof(ContentPresenter));
        text.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(text);
        template.VisualTree = row;
        var checkedTrigger = new Trigger { Property = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, Value = true };
        checkedTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(0, 100, 190)), "CheckSurface"));
        checkedTrigger.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromRgb(0, 100, 190)), "CheckSurface"));
        checkedTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "CheckMark"));
        template.Triggers.Add(checkedTrigger);
        checkbox.Template = template;
        return checkbox;
    }

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
        string activeProfile = "User")
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

        var titleIcon = IconAssets.Image(HeadingIcon(request), 30);
        RenderOptions.SetBitmapScalingMode(titleIcon, BitmapScalingMode.HighQuality);
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
            Foreground = Pale,
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "Закрыть"
        };
        close.Click += (_, _) => window.DialogResult = false;
        heading.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed) window.DragMove();
        };
        Grid.SetColumn(close, 2);
        top.Children.Add(close);

        var main = new Grid();
        main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var subject = new Grid { Margin = new Thickness(0, 0, 0, 15) };
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
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
        RenderOptions.SetBitmapScalingMode(subjectIcon, BitmapScalingMode.HighQuality);
        var container = new Border
        {
            Width = 61, Height = 61, Padding = new Thickness(7),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(125, accent.R, accent.G, accent.B)),
            Background = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(0),
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
        int state = Status(request, activeProfile);
        if (state != 0)
        {
            var status = IconAssets.Image(state, 30);
            RenderOptions.SetBitmapScalingMode(status, BitmapScalingMode.HighQuality);
            status.Margin = new Thickness(16, 0, 2, 0);
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(status, 2);
            subject.Children.Add(status);
        }
        main.Children.Add(subject);

        var paragraph = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(245, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(90, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
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
            checkbox = Acknowledgement();
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
                Foreground = Pale, Background = Brushes.White,
                BorderThickness = new Thickness(0), Padding = new Thickness(8),
                CaretBrush = Brushes.White, ToolTip = "TTY-Ключ"
            };
            visibleSecret = new TextBox
            {
                Visibility = Visibility.Collapsed, MaxLength = 64,
                Foreground = Pale, Background = Brushes.White,
                BorderThickness = new Thickness(0), Padding = new Thickness(8),
                FontSize = 13, CaretBrush = Brushes.White, ToolTip = "TTY-Ключ"
            };
            var input = new Grid();
            input.Children.Add(secret);
            input.Children.Add(visibleSecret);
            keyRow.Children.Add(input);
            secretBorder = new Border
            {
                CornerRadius = new CornerRadius(0),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 160, 183, 218)),
                Background = Brushes.White,
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

        var footer = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(footer, 1);
        bottom.Children.Add(footer);
        // Buttons belong to the center of the modal, never next to a status icon.
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        footer.Children.Add(actions);

        bool isAgreement = request.Kind == WarningKind.Agreement;
        bool oneButton = isAgreement || request.Kind is WarningKind.Fatal or WarningKind.Dax;
        Button? deny = null;
        if (!oneButton)
        {
            deny = WindowsAction("Запретить");
            deny.Click += (_, _) => window.DialogResult = false;
            actions.Children.Add(deny);
        }
        string actionName = request.Kind switch
        {
            WarningKind.Fatal => "Сброс",
            WarningKind.Dax => "Ок",
            WarningKind.Agreement => "Согласен",
            _ => "Разрешить"
        };
        var allow = WindowsAction(actionName, primary: true);
        if (oneButton) allow.MinWidth = 188;
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
            window.DialogResult = true;
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
        // Solid, severity-tinted Windows-style dialog. No Glass surface,
        // backdrop blur or rounded corners in the warning subsystem.
        window.Content = new Border
        {
            Background = new SolidColorBrush(BackgroundFor(request.Kind)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(138, 153, 179)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(0),
            Padding = new Thickness(25, 20, 25, 22),
            Child = layout
        };
        CheckGate();
        DaxAudit.Write("warning", "shown", request.Kind + ": " + request.Reason);
        bool result = window.ShowDialog() == true;
        DaxAudit.Write("warning", result ? "accepted" : "dismissed", request.Kind + ": " + request.Reason);
        return result;
    }
}
