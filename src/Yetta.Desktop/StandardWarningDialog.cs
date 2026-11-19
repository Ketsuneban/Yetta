using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>
/// Windows 11-inspired presentation for Yetta warnings: a
/// native DWM framed surface with the warning reason, the affected object, a
/// scrollable explanation, an optional agreement / TTY-key gate and the
/// decision buttons. Styling never grants Windows privileges and never skips
/// the DAX confirmation requirements.
/// </summary>
internal static class StandardWarningDialog
{
    private static readonly Brush White = new SolidColorBrush(Color.FromRgb(248, 248, 248));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(198, 200, 206));
    private static readonly Brush Faint = new SolidColorBrush(Color.FromRgb(148, 152, 161));
    private static readonly Brush Success = new SolidColorBrush(Color.FromRgb(96, 200, 120));
    private static readonly Brush Failure = new SolidColorBrush(Color.FromRgb(232, 106, 106));

    private sealed record Palette(Color Accent, Color Header, Color Surface, Color Footer);

    private static Palette Colors(WarningKind kind) => kind switch
    {
        WarningKind.Information => new(Color.FromRgb(99, 192, 238), Color.FromRgb(44, 62, 73), Color.FromRgb(33, 45, 53), Color.FromRgb(20, 27, 31)),
        WarningKind.Caution => new(Color.FromRgb(255, 193, 91), Color.FromRgb(82, 65, 43), Color.FromRgb(58, 47, 36), Color.FromRgb(29, 25, 22)),
        WarningKind.Dangerous => new(Color.FromRgb(246, 126, 122), Color.FromRgb(92, 48, 49), Color.FromRgb(60, 37, 39), Color.FromRgb(30, 22, 23)),
        WarningKind.Critical => new(Color.FromRgb(255, 131, 131), Color.FromRgb(90, 26, 27), Color.FromRgb(52, 17, 19), Color.FromRgb(29, 11, 12)),
        WarningKind.Fatal => new(Color.FromRgb(222, 222, 222), Color.FromRgb(24, 24, 24), Color.FromRgb(11, 11, 11), Color.FromRgb(3, 3, 3)),
        WarningKind.Agreement => new(Color.FromRgb(208, 208, 208), Color.FromRgb(54, 54, 54), Color.FromRgb(39, 39, 39), Color.FromRgb(22, 22, 22)),
        WarningKind.Dax => new(Color.FromRgb(135, 184, 255), Color.FromRgb(24, 48, 85), Color.FromRgb(16, 32, 57), Color.FromRgb(9, 19, 35)),
        _ => new(Color.FromRgb(100, 185, 239), Color.FromRgb(55, 55, 55), Color.FromRgb(43, 43, 43), Color.FromRgb(20, 20, 20))
    };

    internal static void PlayWarningSound()
    {
        try { System.Media.SystemSounds.Hand.Play(); }
        catch (InvalidOperationException) { }
    }

    private static TextBlock Text(string text, double size, Brush foreground, bool bold = false) =>
        new()
        {
            Text = text, FontSize = size, Foreground = foreground,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };

    private static Button Command(string text, bool primary, Brush accent)
    {
        var neutral = new SolidColorBrush(Color.FromRgb(58, 65, 73));
        var button = new Button
        {
            Content = text, Width = 225, MinHeight = 42,
            FontFamily = SystemFonts.MessageFontFamily, FontSize = 15,
            Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(4, 0, 4, 0),
            Background = primary ? accent : neutral,
            Foreground = primary ? Brushes.Black : White,
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand, FocusVisualStyle = null
        };
        var template = new ControlTemplate(typeof(Button));
        var panel = new FrameworkElementFactory(typeof(Border));
        panel.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        panel.Name = "ButtonChrome";
        panel.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
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
        disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
        template.Triggers.Add(disabled);
        var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.78));
        template.Triggers.Add(pressed);
        button.Template = template;
        UiMotion.AttachButtonFeedback(button);
        return button;
    }

    private static T? Ancestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }

    private static Image CauseIcon(WarningRequest request, double size) => WarningCauseResolver.Render(request, size);

    internal static bool Show(Window owner, WarningRequest request, string profile, bool nonModal,
        Action<Window>? onWindowCreated = null, bool renderOnly = false)
    {
        // UAC is not being imitated functionally: this is an ordinary Yetta window.
        // Real Windows privilege prompts, if needed, are still exclusively OS-owned.
        var palette = Colors(request.Kind);
        bool fatal = request.Kind == WarningKind.Fatal;
        var window = new Window
        {
            Owner = owner,
            Title = TallowText.Get("ui.warning.0007") + request.Reason,
            Width = 720,
            Height = request.IsPreview ? 560 + (request.RequirePassword ? 75 : 0) + (request.RequireTty ? 95 : 0) + (request.RequireAcknowledgement ? 40 : 0)
                : request.RequireTty || request.Kind == WarningKind.Critical || profile == "System"
                    ? 650 : request.Description.Length > 250 ? 600 : 540,
            MinWidth = 540,
            MinHeight = 430,
            MaxWidth = SystemParameters.WorkArea.Width * 0.95,
            MaxHeight = SystemParameters.WorkArea.Height * 0.9,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(palette.Surface),
            FontFamily = SystemFonts.MessageFontFamily
        };
        window.SourceInitialized += (_, _) =>
        {
            WarningGlass.Apply(window);
        };

        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) e.Handled = true;
        };
        window.Loaded += (_, _) => PlayWarningSound();

        var outer = new Border
        {
            Background = new SolidColorBrush(palette.Surface),
            BorderThickness = new Thickness(0),
            ClipToBounds = true,
            SnapsToDevicePixels = true
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.Child = grid;

        bool finishing = false;
        void Finish(bool accepted)
        {
            if (finishing) return;
            finishing = true;
            void Complete()
            {
                if (nonModal) window.Close();
                else window.DialogResult = accepted;
            }
            if (!UiMotion.AnimationsEnabled) { Complete(); return; }
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                FillBehavior = FillBehavior.HoldEnd
            };
            fade.Completed += (_, _) => Complete();
            outer.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        window.Closing += (_, args) =>
        {
            if (finishing || !UiMotion.AnimationsEnabled) return;
            args.Cancel = true;
            window.Dispatcher.BeginInvoke(new Action(() => { if (window.IsVisible) Finish(false); }));
        };

        // TOP ZONE: event icon, reason and the close button.
        var header = new Border
        {
            Background = new SolidColorBrush(palette.Header),
            Padding = new Thickness(30, 20, 12, 20)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        var component = WarningCauseResolver.Type(request) switch { WarningCauseType.Dax => "DAX", WarningCauseType.Tallow => "Tallow", _ => "Yetta" };
        var caption = Text("Контроль действий " + component, 12, White);
        caption.Margin = new Thickness(0, 0, 0, 22);
        heading.Children.Add(caption);
        heading.Children.Add(Text(request.Reason, 23, White, bold: true));
        headerGrid.Children.Add(heading);
        var close = new Button
        {
            Content = new TextBlock { Text = "\uE8BB", FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 12, Foreground = White },
            Width = 44, Height = 38,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Cursor = Cursors.Hand, FocusVisualStyle = null,
            Style = (Style)Application.Current.FindResource("DialogCloseButton"),
            ToolTip = TallowText.Get("ui.warning.0008")
        };
        UiMotion.AttachButtonFeedback(close);
        close.Click += (_, _) => Finish(false);
        Grid.SetColumn(close, 1);
        headerGrid.Children.Add(close);
        header.Child = headerGrid;
        header.MouseLeftButtonDown += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (e.OriginalSource is DependencyObject source && Ancestor<Button>(source) is not null) return;
            try { window.DragMove(); } catch (InvalidOperationException) { }
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // The description is always visible; technical evidence is a separate, collapsed section.
        var body = new Grid { Margin = new Thickness(26, 20, 26, 18) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var subject = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cause = CauseIcon(request, 48);
        cause.Margin = new Thickness(0, 0, 15, 0);
        if (!string.IsNullOrWhiteSpace(request.SourcePath))
        {
            cause.Cursor = Cursors.Hand;
            cause.ToolTip = "Показать причину в Проводнике";
            cause.MouseLeftButtonUp += (_, _) => WarningFileNavigation.Reveal(request.SourcePath);
        }
        subject.Children.Add(cause);
        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        string displayName = WarningCauseResolver.Type(request) switch
        {
            WarningCauseType.Tallow => "Tallow", WarningCauseType.Yetta => "Yetta", WarningCauseType.Dax => "DAX",
            WarningCauseType.Windows => "Windows", _ => request.Subject
        };
        identity.Children.Add(Text(displayName, 19, White, bold: true));
        string publisherText = WarningCauseResolver.Type(request) is
            WarningCauseType.Yetta or WarningCauseType.Dax or WarningCauseType.Tallow ? "Tallow" : request.Publisher ?? "Неизвестный издатель";
        var publisher = Text("Издатель: " + publisherText, 12, Muted);
        publisher.Margin = new Thickness(0, 4, 0, 0);
        identity.Children.Add(publisher);
        Grid.SetColumn(identity, 1);
        subject.Children.Add(identity);
        body.Children.Add(subject);
        var content = new StackPanel();
        var description = Text(request.Description, 14, White);
        description.LineHeight = 23;
        description.Margin = new Thickness(0, 0, 0, 14);
        content.Children.Add(description);
        var more = Text("Подробнее", 13, new SolidColorBrush(palette.Accent));
        more.Cursor = Cursors.Hand;
        more.Margin = new Thickness(0, 0, 0, 10);
        content.Children.Add(more);
        var evidence = new StackPanel { Visibility = Visibility.Collapsed };
        evidence.Children.Add(Text(request.TechnicalDetails ?? "Тип: " + request.Kind +
            " · Источник: " + WarningCauseResolver.Type(request), 12, Muted));
        var paths = (request.RelatedPaths ?? Array.Empty<string>()).Concat(request.SourcePath is { Length: > 0 } source ? new[] { source } : Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            var link = Text(path, 12, new SolidColorBrush(palette.Accent));
            link.TextWrapping = TextWrapping.Wrap;
            link.Margin = new Thickness(0, 8, 0, 0);
            link.Cursor = Cursors.Hand;
            link.ToolTip = "Показать в Проводнике";
            link.MouseLeftButtonUp += (_, _) => WarningFileNavigation.Reveal(path);
            evidence.Children.Add(link);
        }
        content.Children.Add(evidence);
        more.MouseLeftButtonUp += (_, _) =>
        {
            bool expanded = evidence.Visibility != Visibility.Visible;
            evidence.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            more.Text = expanded ? "Свернуть подробности" : "Подробнее";
        };
        var explanation = new EdgeScrollViewer { Content = content, Background = new SolidColorBrush(palette.Surface),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 0, 8, 0) };
        Grid.SetRow(explanation, 1);
        body.Children.Add(explanation);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        // FOOTER: agreement / key gate and the two decision buttons.
        var footerStack = new StackPanel();
        var footer = new Border
        {
            Background = new SolidColorBrush(palette.Footer),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(26, 16, 26, 18),
            Child = footerStack
        };
        bool oneAction = !request.IsPreview && (request.Kind is WarningKind.Information or WarningKind.Fatal or WarningKind.Dax or WarningKind.Agreement);
        bool needsKey = request.IsPreview ? request.RequireTty : !fatal && !oneAction &&
            (request.RequireTty || request.Kind == WarningKind.Critical || profile == "System");
        bool needsCheck = (request.IsPreview || !fatal) && request.RequireAcknowledgement;
        PasswordBox? confirmationPassword = null;
        if (request.IsPreview && request.RequirePassword)
        {
            footerStack.Children.Add(Text("Пароль подтверждения · для теста: test", 12.5, Muted));
            confirmationPassword = new PasswordBox { MaxLength = 128, Height = 42,
                Style = (Style)Application.Current.FindResource("WarningPassword"),
                Margin = new Thickness(0, 6, 0, 12), PasswordChar = '•' };
            footerStack.Children.Add(confirmationPassword);
        }

        CheckBox? checkbox = null;
        TextBlock? checkboxLabel = null;
        if (needsCheck)
        {
            checkboxLabel = new TextBlock
            {
                Text = TallowText.Get("ui.warning.0025"),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            var box = new CheckBox
            {
                Content = checkboxLabel,
                Style = (Style)Application.Current.FindResource("WarningCheck"),
                Foreground = White, FontSize = 12.5,
                IsChecked = false,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 12),
                FocusVisualStyle = null
            };
            checkbox = box;
            footerStack.Children.Add(box);
        }

        StackPanel? keyPanel = null;
        PasswordBox? tty = null;
        TextBox? visibleKey = null;
        TextBlock? placeholder = null;
        TextBlock? keyFeedback = null;
        Border? keyIcon = null;
        if (needsKey)
        {
            keyPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var keyCaption = Text(TallowText.Get("ui.warning.0026"), 12.5, Muted);
            keyCaption.Margin = new Thickness(0, 0, 0, 6);
            keyPanel.Children.Add(keyCaption);
            var keyRow = new Grid();
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            keyIcon = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
                BorderBrush = Brushes.Transparent, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4), Padding = new Thickness(6),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = IconAssets.Image(36, 22)
            };
            keyRow.Children.Add(keyIcon);
            var inputLayer = new Grid();
            var password = new PasswordBox
            {
                MaxLength = 64, Height = 42, PasswordChar = '•',
                Style = (Style)Application.Current.FindResource("WarningPassword"),
                Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                Foreground = White, CaretBrush = White,
                BorderThickness = new Thickness(1), BorderBrush = Muted,
                Padding = new Thickness(10, 0, 10, 0),
                ToolTip = TallowText.Get("ui.warning.0013"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var plain = new TextBox
            {
                MaxLength = 64, Height = 42, Visibility = Visibility.Collapsed,
                Style = (Style)Application.Current.FindResource("WarningTextInput"),
                Background = password.Background, Foreground = White, CaretBrush = White,
                BorderThickness = new Thickness(1), BorderBrush = Muted,
                Padding = new Thickness(10, 0, 10, 0),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var hint = new TextBlock
            {
                Text = TallowText.Get("ui.warning.0015"),
                Foreground = Faint, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(11, 0, 0, 0), IsHitTestVisible = false
            };
            inputLayer.Children.Add(password);
            inputLayer.Children.Add(plain);
            inputLayer.Children.Add(hint);
            Grid.SetColumn(inputLayer, 1);
            keyRow.Children.Add(inputLayer);
            var reveal = Command("", false, new SolidColorBrush(palette.Accent));
            reveal.Content = IconAssets.Image(50, 18);
            reveal.Width = 38;
            reveal.Height = 36;
            reveal.MinHeight = 36;
            reveal.Margin = new Thickness(8, 0, 0, 0);
            reveal.Padding = new Thickness(0);
            reveal.ToolTip = TallowText.Get("ui.warning.0014");
            Grid.SetColumn(reveal, 2);
            keyRow.Children.Add(reveal);
            keyPanel.Children.Add(keyRow);
            var feedback = Text(TallowText.Get("ui.warning.0023"), 12, Muted);
            feedback.Margin = new Thickness(44, 5, 0, 0);
            feedback.Visibility = Visibility.Collapsed;
            keyPanel.Children.Add(feedback);
            footerStack.Children.Add(keyPanel);
            tty = password;
            visibleKey = plain;
            placeholder = hint;
            keyFeedback = feedback;
            reveal.Click += (_, _) =>
            {
                bool show = plain.Visibility != Visibility.Visible;
                if (show)
                {
                    plain.Text = password.Password;
                    password.Visibility = Visibility.Collapsed;
                    plain.Visibility = Visibility.Visible;
                    reveal.Content = IconAssets.Image(49, 18);
                    reveal.ToolTip = TallowText.Get("ui.warning.0016");
                }
                else
                {
                    password.Password = plain.Text;
                    plain.Visibility = Visibility.Collapsed;
                    password.Visibility = Visibility.Visible;
                    reveal.Content = IconAssets.Image(50, 18);
                    reveal.ToolTip = TallowText.Get("ui.warning.0014");
                }
            };
            plain.TextChanged += (_, _) =>
            {
                if (plain.Visibility == Visibility.Visible && password.Password != plain.Text)
                    password.Password = plain.Text;
            };
        }

        var buttonRow = new Grid();
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var controls = new Grid();
        string action = request.IsPreview ? "Разрешить" : request.Kind switch
        {
            WarningKind.Information => TallowText.Get("ui.warning.0017"),
            WarningKind.Fatal => TallowText.Get("ui.warning.0018"),
            WarningKind.Dax => TallowText.Get("ui.warning.0017"),
            WarningKind.Agreement => TallowText.Get("ui.warning.0019"),
            _ => TallowText.Get("ui.warning.0020")
        };
        var allow = Command(action, oneAction, new SolidColorBrush(palette.Accent));
        allow.HorizontalAlignment = HorizontalAlignment.Stretch;
        allow.Width = double.NaN;
        Button? deny = null;
        if (oneAction)
        {
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.Children.Add(allow);
        }
        else
        {
            // Match the Windows layout: continue on the left, decline on the right.
            deny = Command(request.IsPreview ? "Запретить" : TallowText.Get("ui.warning.0021"), true, new SolidColorBrush(palette.Accent));
            deny.IsCancel = false; // Escape must never dismiss warnings.
            deny.HorizontalAlignment = HorizontalAlignment.Stretch;
            deny.Width = double.NaN;
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(allow, 0);
            controls.Children.Add(allow);
            Grid.SetColumn(deny, 1);
            controls.Children.Add(deny);
        }
        buttonRow.Children.Add(controls);
        footerStack.Children.Add(buttonRow);
        Grid.SetRow(footer, 2);
        grid.Children.Add(footer);

        void SetKeyTint(Brush brush)
        {
            if (tty is not null) tty.BorderBrush = brush;
            if (visibleKey is not null) visibleKey.BorderBrush = brush;
            if (keyIcon is not null) keyIcon.BorderBrush = brush;
        }

        void RefreshGate()
        {
            bool approved = checkbox is null || checkbox.IsChecked == true;
            bool keyValid = tty is null;
            if (tty is not null)
            {
                keyValid = false;
                if (tty.Password.Length == 64)
                {
                    try { keyValid = TtyKeyStore.Matches(tty.Password); }
                    catch (Exception e) when (e is Win32Exception or InvalidDataException) { keyValid = false; }
                }
            }
            allow.IsEnabled = approved && keyValid && (confirmationPassword is null || confirmationPassword.Password == "test");
            if (keyFeedback is null || tty is null) return;
            if (tty.Password.Length == 0)
            {
                keyFeedback.Visibility = Visibility.Collapsed;
                SetKeyTint(Muted);
            }
            else if (tty.Password.Length < 64)
            {
                keyFeedback.Visibility = Visibility.Visible;
                keyFeedback.Text = TallowText.Get("ui.warning.0022") + tty.Password.Length + "/64";
                keyFeedback.Foreground = Faint;
                SetKeyTint(Muted);
            }
            else
            {
                keyFeedback.Visibility = Visibility.Visible;
                keyFeedback.Text = keyValid ? TallowText.Get("ui.warning.0023") : TallowText.Get("ui.warning.0024");
                keyFeedback.Foreground = keyValid ? Success : Failure;
                SetKeyTint(keyValid ? Success : Failure);
            }
        }

        void UpdatePlaceholder()
        {
            if (placeholder is null || tty is null) return;
            placeholder.Visibility = tty.Password.Length == 0 && tty.Visibility == Visibility.Visible
                ? Visibility.Visible : Visibility.Collapsed;
        }

        if (confirmationPassword is not null)
            confirmationPassword.PasswordChanged += (_, _) => RefreshGate();
        if (checkbox is not null)
        {
            CheckBox box = checkbox;
            box.Checked += (_, _) => RefreshGate();
            box.Unchecked += (_, _) => RefreshGate();
        }
        if (tty is not null)
        {
            tty.PasswordChanged += (_, _) => { UpdatePlaceholder(); RefreshGate(); };
        }
        allow.Click += (_, _) =>
        {
            RefreshGate();
            if (!allow.IsEnabled) return;
            if (fatal) request.RecoveryNavigation?.Invoke();
            Finish(true);
        };
        if (deny is not null) deny.Click += (_, _) => Finish(false);
        UpdatePlaceholder();
        RefreshGate();

        window.Content = outer;
        if (deny is not null) window.Loaded += (_, _) => deny.Focus();
        if (fatal) window.Topmost = true;
        UiMotion.AnimateOpen(outer);

        onWindowCreated?.Invoke(window);
        if (renderOnly) return false;
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

    private static void FlyInto(FrameworkElement label, bool into)
    {
        label.BeginAnimation(UIElement.OpacityProperty, null);
        if (!UiMotion.AnimationsEnabled)
        {
            label.Visibility = into ? Visibility.Collapsed : Visibility.Visible;
            label.Opacity = 1;
            label.RenderTransform = Transform.Identity;
            return;
        }
        var transform = new TranslateTransform();
        label.RenderTransform = transform;
        label.Visibility = Visibility.Visible;
        label.Opacity = into ? 0 : 1;
        var duration = TimeSpan.FromMilliseconds(170);
        var ease = new QuadraticEase { EasingMode = EasingMode.EaseIn };
        var move = new DoubleAnimation(into ? 0 : -34, into ? -34 : 0, duration)
        { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var fade = new DoubleAnimation(into ? 1 : 0, into ? 0 : 1, duration)
        { FillBehavior = FillBehavior.Stop };
        fade.Completed += (_, _) =>
        {
            label.BeginAnimation(UIElement.OpacityProperty, null);
            label.RenderTransform = Transform.Identity;
            label.Opacity = 1;
            if (into) label.Visibility = Visibility.Collapsed;
        };
        transform.BeginAnimation(TranslateTransform.XProperty, move);
        label.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static void FadeIn(UIElement element)
    {
        element.Opacity = 1;
        if (!UiMotion.AnimationsEnabled) return;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
