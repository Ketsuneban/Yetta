using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>
/// UAC-inspired styling for all seven Yetta warning kinds. Not the Windows UAC
/// secure desktop and not a substitute for Windows elevation or OS consent.
/// Other warning types retain their existing rendering and control flows.
/// </summary>
internal static class StandardWarningDialog
{
    private static readonly Brush White = new SolidColorBrush(Color.FromRgb(248, 248, 248));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(205, 205, 205));
    // Muted shades, not solid safety-red/yellow fills. Each kind tints the
    // actual window background and the selected action, independently of
    // the header icon (which represents the source of the event).
    private sealed record Palette(Color Header, Color Body, Color Footer, Color Accent);
    private static Palette Colors(WarningKind kind) => kind switch
    {
        WarningKind.Caution => new(
            Color.FromRgb(77, 67, 53), Color.FromRgb(55, 49, 39),
            Color.FromRgb(32, 30, 27), Color.FromRgb(235, 176, 82)),
        WarningKind.Dangerous => new(
            Color.FromRgb(82, 53, 56), Color.FromRgb(59, 40, 43),
            Color.FromRgb(35, 27, 29), Color.FromRgb(229, 119, 120)),
        WarningKind.Critical => new(
            Color.FromRgb(81, 44, 57), Color.FromRgb(53, 31, 43),
            Color.FromRgb(33, 24, 30), Color.FromRgb(223, 99, 145)),
        WarningKind.Fatal => new(
            Color.FromRgb(27, 26, 34), Color.FromRgb(17, 17, 24),
            Color.FromRgb(9, 9, 15), Color.FromRgb(174, 170, 194)),
        WarningKind.Agreement => new(
            Color.FromRgb(58, 62, 76), Color.FromRgb(43, 47, 60),
            Color.FromRgb(26, 29, 38), Color.FromRgb(148, 179, 227)),
        WarningKind.Dax => new(
            Color.FromRgb(44, 65, 83), Color.FromRgb(31, 47, 64),
            Color.FromRgb(22, 30, 42), Color.FromRgb(121, 179, 232)),
        _ => new(
            Color.FromRgb(54, 54, 54), Color.FromRgb(43, 43, 43),
            Color.FromRgb(23, 23, 23), Color.FromRgb(76, 189, 242))
    };
    private static Brush BrushFor(Color color) => new SolidColorBrush(color);
    private static string KindName(WarningKind kind) => kind switch
    {
        WarningKind.Caution => "Осторожно",
        WarningKind.Dangerous => "Опасное действие",
        WarningKind.Critical => "Критическое предупреждение",
        WarningKind.Fatal => "Смертельное предупреждение",
        WarningKind.Agreement => "Соглашение",
        WarningKind.Dax => "DAX",
        _ => "Стандартное"
    };

    private static TextBlock Text(string text, double size, Brush foreground, bool bold = false) =>
        new()
        {
            Text = text, FontSize = size, Foreground = foreground,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static Button Command(string text, bool primary, Brush selectedAccent)
    {
        var button = new Button
        {
            Content = text,
            Width = 225,
            MinHeight = 42,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 15,
            Padding = new Thickness(10, 7, 10, 7),
            Margin = new Thickness(4, 0, 4, 0),
            // All actions are neutral at rest. Severity colour appears ONLY
            // under the pointer; never imply the choice is pre-approved.
            Background = new SolidColorBrush(Color.FromRgb(46, 46, 46)),
            Foreground = White,
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(84, 84, 84)),
            Cursor = Cursors.Hand
        };
        var template = new ControlTemplate(typeof(Button));
        var panel = new FrameworkElementFactory(typeof(Border));
        panel.Name = "ButtonSurface";
        panel.SetValue(Border.SnapsToDevicePixelsProperty, true);
        panel.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        panel.SetValue(Border.IsHitTestVisibleProperty, true);
        panel.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        panel.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        panel.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
        { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
        var present = new FrameworkElementFactory(typeof(ContentPresenter));
        present.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        present.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        panel.AppendChild(present);
        template.VisualTree = panel;
        var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.48));
        template.Triggers.Add(disabled);
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, selectedAccent, "ButtonSurface"));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, Brushes.Black));
        hover.Setters.Add(new Setter(Border.BorderBrushProperty,
            new SolidColorBrush(Color.FromArgb(190, 242, 245, 255)), "ButtonSurface"));
        template.Triggers.Add(hover);
        button.Template = template;
        UiMotion.AttachButtonFeedback(button);
        return button;
    }

    internal static bool Show(Window owner, WarningRequest request, string profile, bool nonModal)
    {
        // UAC is not being imitated functionally: this is an ordinary Yetta window.
        // Real Windows privilege prompts, if needed, are still exclusively OS-owned.
        var palette = Colors(request.Kind);
        var window = new Window
        {
            Owner = owner,
            Title = "Yetta · " + request.Reason,
            Width = 610,
            Height = 475,
            MinWidth = 530,
            MinHeight = 430,
            MaxWidth = SystemParameters.WorkArea.Width * 0.95,
            MaxHeight = SystemParameters.WorkArea.Height * 0.9,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent
        };

        // Single perimeter stroke for the entire warning window. It is not
        // a nested rectangular window or an outline around the page content.
        var outer = new Border
        {
            Background = BrushFor(palette.Body),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(Color.FromArgb(190,
                palette.Accent.R, palette.Accent.G, palette.Accent.B)),
            BorderThickness = new Thickness(1.35),
            ClipToBounds = true,
            SnapsToDevicePixels = true
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.Child = grid;
        grid.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width > 0 && e.NewSize.Height > 0)
                grid.Clip = new RectangleGeometry(
                    new Rect(new Point(), e.NewSize), 7, 7);
        };

        void Finish(bool accepted)
        {
            if (nonModal) window.Close();
            else window.DialogResult = accepted;
        }

        // TOP: restrained caption, independent event icon, prominent question.
        var header = new StackPanel
        {
            Background = BrushFor(palette.Header),
            Margin = new Thickness(0),
        };
        var caption = new Grid { Margin = new Thickness(28, 15, 13, 4) };
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // 48.png stays in the fatal HEADER. The skull (new #13) is the
        // fatal CAUSE icon instead.
        int headingId = YettaWarningWindow.HeaderIcon(request);
        var eventIcon = IconAssets.Image(headingId, 24);
        eventIcon.Margin = new Thickness(0, 0, 8, 0);
        caption.Children.Add(eventIcon);
        var captionText = Text("Yetta · " + KindName(request.Kind), 12, White);
        Grid.SetColumn(captionText, 1);
        caption.Children.Add(captionText);
        var close = new Button
        {
            Content = WarningArtwork.Image(5, 19),
            Width = 32, Height = 30, FontSize = 23,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Foreground = White, Cursor = Cursors.Hand, ToolTip = "Закрыть"
        };
        close.Click += (_, _) => Finish(false);
        Grid.SetColumn(close, 2);
        caption.Children.Add(close);
        header.Children.Add(caption);
        var question = Text(request.Reason, 23, White, bold: true);
        question.Margin = new Thickness(30, 14, 32, 19);
        header.Children.Add(question);
        captionText.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                try { window.DragMove(); } catch (InvalidOperationException) { }
            }
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // BODY: object and status share a line; publisher is not falsely certified.
        var body = new Grid { Background = BrushFor(palette.Body), Margin = new Thickness(30, 23, 30, 21) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var subject = new Grid();
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var causeIcon = WarningArtwork.Cause(request, 48);
        causeIcon.Margin = new Thickness(0, 0, 16, 0);
        subject.Children.Add(causeIcon);
        var name = Text(request.Subject, 20, White, bold: true);
        name.Cursor = Cursors.Hand;
        name.ToolTip = "Искать сведения об объекте";
        name.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(
                    "https://www.google.com/search?q=" + Uri.EscapeDataString(request.Subject))
                { UseShellExecute = true });
            }
            catch (Win32Exception) { }
            catch (InvalidOperationException) { }
        };
        Grid.SetColumn(name, 1);
        subject.Children.Add(name);
        int state = YettaWarningWindow.StatusArtwork(request, profile);
        if (state != 0)
        {
            var status = WarningArtwork.Image(state, 35);
            status.Margin = new Thickness(12, 0, 0, 0);
            status.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(status, 2);
            subject.Children.Add(status);
        }
        Grid.SetRow(subject, 0);
        body.Children.Add(subject);

        var publisher = Text(string.IsNullOrWhiteSpace(request.Publisher)
            ? "Издатель: неизвестен" : "Издатель: " + request.Publisher,
            14, White);
        publisher.Margin = new Thickness(0, 21, 0, 0);
        publisher.Cursor = Cursors.Hand;
        publisher.ToolTip = "Открыть расположение файла (если указано)";
        publisher.MouseLeftButtonUp += (_, _) =>
        {
            try
            {
                if (request.SourcePath is { } path && File.Exists(path))
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
                    { UseShellExecute = true });
            }
            catch (Win32Exception) { }
        };
        Grid.SetRow(publisher, 1);
        body.Children.Add(publisher);

        // "Подробнее" unfolds the explanation. Scrolling is contained in this
        // area only, so long text cannot hide confirmation controls.
        var details = new Grid { Margin = new Thickness(0, 23, 0, 0) };
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var detailsLink = new Button
        {
            Content = "Подробнее",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(0),
            Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(135, 220, 247)),
            FontSize = 14, Cursor = Cursors.Hand
        };
        details.Children.Add(detailsLink);
        var bodyText = Text(request.Description, 13, Muted);
        bodyText.LineHeight = 21;
        var explanation = new ScrollViewer
        {
            Visibility = Visibility.Collapsed,
            Content = bodyText,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(2, 12, 10, 0),
            Focusable = false
        };
        detailsLink.Click += (_, _) =>
        {
            bool expanded = explanation.Visibility != Visibility.Visible;
            explanation.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            detailsLink.Content = expanded ? "Скрыть подробности" : "Подробнее";
        };
        Grid.SetRow(explanation, 1);
        details.Children.Add(explanation);
        Grid.SetRow(details, 2);
        body.Children.Add(details);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        // FOOTER: always visible even when text is long. Existing approval,
        // checkbox, key and audit semantics remain in force.
        bool needsCheck = !request.InformationOnly &&
            (request.RequireAcknowledgement || request.Kind == WarningKind.Agreement);
        bool needsKey = !request.InformationOnly &&
            request.Kind is not (WarningKind.Fatal or WarningKind.Dax or WarningKind.Agreement) &&
            (request.RequireTty || request.Kind == WarningKind.Critical || profile == "System");
        var bottom = new StackPanel();
        var bottomSurface = new Border
        {
            Background = BrushFor(palette.Footer),
            Padding = new Thickness(22, 17, 22, 22),
            Child = bottom
        };
        CheckBox? checkbox = null;
        if (needsCheck)
        {
            checkbox = new CheckBox
            {
                Content = "Я ознакомился с предупреждением и подтверждаю действие",
                Foreground = White, FontSize = 12, IsChecked = false,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };
            bottom.Children.Add(checkbox);
        }
        PasswordBox? tty = null;
        if (needsKey)
        {
            tty = new PasswordBox
            {
                MaxLength = 64, Width = 390, Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                Foreground = White, CaretBrush = White,
                ToolTip = "TTY-ключ (64 символа)",
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            bottom.Children.Add(tty);
        }
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        string action = request.Kind switch
        {
            WarningKind.Fatal => "Сброс",
            WarningKind.Dax => "Ок",
            WarningKind.Agreement => "Согласен",
            _ => "Разрешить"
        };
        bool oneAction = request.Kind is WarningKind.Fatal or WarningKind.Dax or WarningKind.Agreement
            || request.InformationOnly;
        if (request.InformationOnly) action = "Закрыть";
        var allow = Command(action, oneAction, BrushFor(palette.Accent));
        Button? deny = null;
        if (!oneAction)
        {
            // A safe decline is highlighted by default; highlight follows
            // warning severity just like the window background.
            deny = Command("Запретить", true, BrushFor(palette.Accent));
            deny.IsCancel = !nonModal;
            controls.Children.Add(deny);
        }
        allow.IsEnabled = !needsCheck && !needsKey;
        controls.Children.Add(allow);
        bottom.Children.Add(controls);
        Grid.SetRow(bottomSurface, 2);
        grid.Children.Add(bottomSurface);

        void RefreshGate()
        {
            bool approved = checkbox is null || checkbox.IsChecked == true;
            bool keyValid = tty is null;
            if (tty is not null && tty.Password.Length == 64)
            {
                try { keyValid = TtyKeyStore.Matches(tty.Password); }
                catch (Exception e) when (e is Win32Exception or InvalidDataException) { keyValid = false; }
            }
            allow.IsEnabled = approved && keyValid;
        }
        if (checkbox is not null)
        {
            checkbox.Checked += (_, _) => RefreshGate();
            checkbox.Unchecked += (_, _) => RefreshGate();
        }
        if (tty is not null) tty.PasswordChanged += (_, _) => RefreshGate();
        allow.Click += (_, _) =>
        {
            RefreshGate();
            if (!allow.IsEnabled) return;
            if (request.Kind == WarningKind.Fatal)
            {
                // No Windows reset; existing callback only navigates to DAX Recovery.
                request.RecoveryNavigation?.Invoke();
            }
            Finish(true);
        };
        if (deny is not null) deny.Click += (_, _) => Finish(false);
        window.Content = outer;
        if (request.Kind == WarningKind.Fatal) window.Topmost = true;
        UiMotion.AnimateOpen(outer);

        DaxAudit.Write("warning", "shown", request.Kind + ": " + request.Reason);
        if (nonModal)
        {
            DaxAudit.Write("warning", "opened_nonmodal", request.Kind.ToString());
            window.Show();
            return false;
        }
        bool result = window.ShowDialog() == true;
        DaxAudit.Write("warning", result ? "accepted" : "dismissed",
            request.Kind + ": " + request.Reason);
        return result;
    }
}
