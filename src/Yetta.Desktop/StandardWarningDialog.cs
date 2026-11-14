using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>
/// UAC-inspired styling for Standard warnings ONLY. Not the Windows UAC
/// secure desktop and not a substitute for Windows elevation or OS consent.
/// Other warning types retain their existing rendering and control flows.
/// </summary>
internal static class StandardWarningDialog
{
    private static readonly Brush White = new SolidColorBrush(Color.FromRgb(248, 248, 248));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(205, 205, 205));
    private static readonly Brush Header = new SolidColorBrush(Color.FromRgb(54, 54, 54));
    private static readonly Brush Body = new SolidColorBrush(Color.FromRgb(43, 43, 43));
    private static readonly Brush Footer = new SolidColorBrush(Color.FromRgb(23, 23, 23));
    private static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(76, 189, 242));

    private static TextBlock Text(string text, double size, Brush foreground, bool bold = false) =>
        new()
        {
            Text = text, FontSize = size, Foreground = foreground,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static Button Command(string text, bool primary)
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
            Background = primary ? Accent : new SolidColorBrush(Color.FromRgb(46, 46, 46)),
            Foreground = primary ? Brushes.Black : White,
            BorderThickness = new Thickness(primary ? 2 : 1),
            BorderBrush = primary ? Brushes.White : new SolidColorBrush(Color.FromRgb(68, 68, 68)),
            Cursor = Cursors.Hand
        };
        var template = new ControlTemplate(typeof(Button));
        var panel = new FrameworkElementFactory(typeof(Border));
        panel.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
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
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.86));
        template.Triggers.Add(hover);
        button.Template = template;
        return button;
    }

    internal static bool Show(Window owner, WarningRequest request, string profile, bool nonModal)
    {
        // UAC is not being imitated functionally: this is an ordinary Yetta window.
        // Real Windows privilege prompts, if needed, are still exclusively OS-owned.
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

        var outer = new Border
        {
            Background = Body,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            SnapsToDevicePixels = true
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.Child = grid;

        void Finish(bool accepted)
        {
            if (nonModal) window.Close();
            else window.DialogResult = accepted;
        }

        // TOP: restrained caption, independent event icon, prominent question.
        var header = new StackPanel
        {
            Background = Header,
            Margin = new Thickness(0),
        };
        var caption = new Grid { Margin = new Thickness(28, 15, 13, 4) };
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        caption.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var eventIcon = IconAssets.WarningGlyph(
            request.HeaderIconId is >= 1 and <= 50 ? request.HeaderIconId.Value : 22, 20);
        eventIcon.Margin = new Thickness(0, 0, 8, 0);
        caption.Children.Add(eventIcon);
        var captionText = Text("Предупреждение Yetta · Стандартное", 12, White);
        Grid.SetColumn(captionText, 1);
        caption.Children.Add(captionText);
        var close = new Button
        {
            Content = "×", Width = 28, Height = 25, FontSize = 23,
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
        var body = new Grid { Background = Body, Margin = new Thickness(30, 23, 30, 21) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var subject = new Grid();
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var causeIcon = IconAssets.Image(YettaWarningWindow.ObjectIcon(request), 44);
        RenderOptions.SetBitmapScalingMode(causeIcon, BitmapScalingMode.HighQuality);
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
        int state = YettaWarningWindow.Status(request, profile);
        if (state != 0)
        {
            var status = IconAssets.WarningGlyph(state, 30);
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
        bool needsCheck = request.RequireAcknowledgement;
        bool needsKey = request.RequireTty || profile == "System";
        var bottom = new StackPanel();
        var bottomSurface = new Border
        {
            Background = Footer,
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
        var allow = Command("Разрешить", false);
        var deny = Command("Запретить", true);
        deny.IsCancel = !nonModal;
        allow.IsEnabled = !needsCheck && !needsKey;
        controls.Children.Add(allow);
        controls.Children.Add(deny);
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
        allow.Click += (_, _) => { RefreshGate(); if (allow.IsEnabled) Finish(true); };
        deny.Click += (_, _) => Finish(false);
        window.Content = outer;

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
