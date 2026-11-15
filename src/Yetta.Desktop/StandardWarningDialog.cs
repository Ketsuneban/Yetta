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
/// Windows 11 UAC-inspired presentation for all seven Yetta warnings: a
/// translucent glass surface with the warning reason, the affected object, a
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

    // Translucent surfaces: the alpha is what lets the acrylic backdrop read as
    // frosted glass, and it degrades to a solid tint when no backdrop is applied.
    private sealed record Palette(Color Accent, Color Header, Color Surface, Color Footer);

    private static Color Argb(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    private static Palette Colors(WarningKind kind) => kind switch
    {
        WarningKind.Caution => new(Color.FromRgb(213, 157, 68), Argb(0xB8, 76, 67, 51), Argb(0xB3, 48, 44, 36), Argb(0xD0, 27, 26, 23)),
        WarningKind.Dangerous => new(Color.FromRgb(204, 104, 105), Argb(0xB8, 76, 48, 49), Argb(0xB3, 45, 32, 33), Argb(0xD0, 24, 21, 22)),
        WarningKind.Critical => new(Color.FromRgb(190, 82, 126), Argb(0xB8, 69, 37, 49), Argb(0xB3, 40, 25, 33), Argb(0xD0, 21, 17, 22)),
        WarningKind.Fatal => new(Color.FromRgb(120, 126, 146), Argb(0xD0, 12, 13, 16), Argb(0xCC, 7, 8, 11), Argb(0xE6, 3, 4, 6)),
        WarningKind.Agreement => new(Color.FromRgb(112, 153, 210), Argb(0xB8, 52, 57, 70), Argb(0xB3, 35, 40, 52), Argb(0xD0, 20, 23, 31)),
        WarningKind.Dax => new(Color.FromRgb(99, 157, 210), Argb(0xB8, 39, 57, 73), Argb(0xB3, 27, 40, 53), Argb(0xD0, 17, 24, 33)),
        WarningKind.Information => new(Color.FromRgb(99, 157, 210), Argb(0xB8, 43, 57, 72), Argb(0xB3, 30, 40, 51), Argb(0xD0, 18, 24, 31)),
        _ => new(Color.FromRgb(100, 165, 205), Argb(0xB8, 52, 54, 59), Argb(0xB3, 35, 37, 42), Argb(0xD0, 19, 20, 24))
    };

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
        var neutral = new SolidColorBrush(Color.FromRgb(49, 51, 57));
        var button = new Button
        {
            Content = text, Width = 225, MinHeight = 42,
            FontFamily = new FontFamily("Segoe UI"), FontSize = 15,
            Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(4, 0, 4, 0),
            Background = neutral, Foreground = White,
            BorderThickness = new Thickness(primary ? 2 : 1),
            BorderBrush = primary ? accent : new SolidColorBrush(Color.FromRgb(91, 94, 103)),
            Cursor = Cursors.Hand, FocusVisualStyle = null
        };
        var template = new ControlTemplate(typeof(Button));
        var panel = new FrameworkElementFactory(typeof(Border));
        panel.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        panel.Name = "ButtonChrome";
        panel.SetValue(Border.CornerRadiusProperty, new CornerRadius(0));
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

    private static Image CauseIcon(WarningRequest request, double size)
    {
        if (request.Kind == WarningKind.Fatal) return IconAssets.Image(48, size);
        if (request.Kind == WarningKind.Agreement) return IconAssets.YettaLogo(size);
        if (request.Kind == WarningKind.Dax) return IconAssets.DaxLogo(size);
        if (request.CauseIconId is >= 1 and <= 50) return IconAssets.Image(request.CauseIconId.Value, size);
        if (request.SourcePath is { Length: > 0 } path && ShellIcon.Load(path) is { } shell)
        {
            var image = new Image { Source = shell, Width = size, Height = size, Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }
        return IconAssets.Image(YettaWarningWindow.ObjectIcon(request), size);
    }

    internal static bool Show(Window owner, WarningRequest request, string profile, bool nonModal,
        Action<Window>? onWindowCreated = null)
    {
        // UAC is not being imitated functionally: this is an ordinary Yetta window.
        // Real Windows privilege prompts, if needed, are still exclusively OS-owned.
        var palette = Colors(request.Kind);
        bool fatal = request.Kind == WarningKind.Fatal;
        var window = new Window
        {
            Owner = owner,
            Title = TallowText.Get("ui.warning.0007") + request.Reason,
            Width = 660,
            Height = 545,
            MinWidth = 560,
            MinHeight = 470,
            MaxWidth = SystemParameters.WorkArea.Width * 0.95,
            MaxHeight = SystemParameters.WorkArea.Height * 0.9,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = false,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(24, 25, 29))
        };
        window.SourceInitialized += (_, _) =>
        {
            if (WarningGlass.Apply(window, palette.Surface))
                window.Background = Brushes.Transparent;
        };

        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) e.Handled = true;
        };
        if (fatal)
        {
            try { System.Media.SystemSounds.Hand.Play(); }
            catch (InvalidOperationException) { }
        }

        var outer = new Border
        {
            Background = new SolidColorBrush(palette.Surface),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
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

        // TOP ZONE: event icon, reason and the close button.
        var header = new Border
        {
            Background = new SolidColorBrush(palette.Header),
            Padding = new Thickness(24, 15, 12, 15)
        };
        var headerGrid = new Grid();
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var eventIcon = IconAssets.Image(YettaWarningWindow.HeaderIcon(request), 26);
        eventIcon.Margin = new Thickness(0, 0, 14, 0);
        headerGrid.Children.Add(eventIcon);
        var reason = Text(request.Reason, 19, White, bold: true);
        Grid.SetColumn(reason, 1);
        headerGrid.Children.Add(reason);
        var close = new Button
        {
            Content = IconAssets.Image(16, 16), Width = 32, Height = 28,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Cursor = Cursors.Hand, FocusVisualStyle = null,
            ToolTip = TallowText.Get("ui.warning.0008")
        };
        close.Click += (_, _) => Finish(false);
        Grid.SetColumn(close, 2);
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

        // MAIN ZONE: the object, its publisher/path and the scrollable text.
        var body = new Grid { Margin = new Thickness(26, 22, 26, 16) };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var subject = new Grid();
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        subject.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cause = CauseIcon(request, 48);
        cause.Margin = new Thickness(0, 0, 16, 0);
        subject.Children.Add(cause);
        var details = new StackPanel { Margin = new Thickness(0), VerticalAlignment = VerticalAlignment.Center };
        var name = Text(request.Subject, 19, White, bold: true);
        name.Cursor = Cursors.Hand;
        name.ToolTip = TallowText.Get("ui.warning.0009");
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
        details.Children.Add(name);
        var publisherText = request.Publisher;
        if (string.IsNullOrWhiteSpace(publisherText)) publisherText = TallowText.Get("ui.warning.0010");
        var publisher = Text(publisherText, 12.5, Faint);
        publisher.Margin = new Thickness(0, 5, 0, 0);
        publisher.Cursor = Cursors.Hand;
        publisher.ToolTip = TallowText.Get("ui.warning.0011");
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
        details.Children.Add(publisher);
        Grid.SetColumn(details, 1);
        subject.Children.Add(details);
        Grid.SetRow(subject, 0);
        body.Children.Add(subject);

        var bodyText = Text(request.Description, 13.5, Muted);
        bodyText.LineHeight = 22;
        var explanation = new ScrollViewer
        {
            Content = bodyText,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(0, 16, 8, 0),
            Focusable = false
        };
        Grid.SetRow(explanation, 1);
        body.Children.Add(explanation);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        // FOOTER: agreement / key gate, decision buttons and status square.
        var footerStack = new StackPanel();
        var footer = new Border
        {
            Background = new SolidColorBrush(palette.Footer),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(26, 16, 26, 18),
            Child = footerStack
        };
        bool oneAction = request.Kind is WarningKind.Information or WarningKind.Fatal or WarningKind.Dax or WarningKind.Agreement;
        bool needsKey = !fatal && !oneAction &&
            (request.RequireTty || request.Kind == WarningKind.Critical || profile == "System");
        bool needsCheck = !fatal && request.RequireAcknowledgement;

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
                MaxLength = 64, Height = 36, PasswordChar = '•',
                Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                Foreground = White, CaretBrush = White,
                BorderThickness = new Thickness(1), BorderBrush = Muted,
                Padding = new Thickness(10, 0, 10, 0),
                ToolTip = TallowText.Get("ui.warning.0013"),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var plain = new TextBox
            {
                MaxLength = 64, Height = 36, Visibility = Visibility.Collapsed,
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
            var reveal = new Button
            {
                Content = IconAssets.Image(49, 18), Width = 38, Height = 36,
                Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(0),
                Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
                BorderThickness = new Thickness(1), BorderBrush = Muted,
                Cursor = Cursors.Hand, FocusVisualStyle = null,
                ToolTip = TallowText.Get("ui.warning.0014")
            };
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
                    reveal.Content = IconAssets.Image(50, 18);
                    reveal.ToolTip = TallowText.Get("ui.warning.0016");
                }
                else
                {
                    password.Password = plain.Text;
                    plain.Visibility = Visibility.Collapsed;
                    password.Visibility = Visibility.Visible;
                    reveal.Content = IconAssets.Image(49, 18);
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
        string action = request.Kind switch
        {
            WarningKind.Information => TallowText.Get("ui.warning.0017"),
            WarningKind.Fatal => TallowText.Get("ui.warning.0018"),
            WarningKind.Dax => TallowText.Get("ui.warning.0017"),
            WarningKind.Agreement => TallowText.Get("ui.warning.0019"),
            _ => TallowText.Get("ui.warning.0020")
        };
        var allow = Command(action, true, new SolidColorBrush(palette.Accent));
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
            // A safe decline is offered next to the approval, never instead of it.
            deny = Command(TallowText.Get("ui.warning.0021"), false, new SolidColorBrush(palette.Accent));
            deny.IsCancel = false; // Escape must never dismiss warnings.
            deny.HorizontalAlignment = HorizontalAlignment.Stretch;
            deny.Width = double.NaN;
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            controls.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(deny, 0);
            controls.Children.Add(deny);
            Grid.SetColumn(allow, 1);
            controls.Children.Add(allow);
        }
        buttonRow.Children.Add(controls);
        int state = YettaWarningWindow.Status(request, profile);
        if (state != 0)
        {
            var status = IconAssets.Image(state, 32);
            status.Margin = new Thickness(16, 0, 0, 0);
            status.VerticalAlignment = VerticalAlignment.Center;
            status.ToolTip = state switch
            {
                26 => TallowText.Get("ui.warning.0028"),
                35 => TallowText.Get("ui.warning.0029"),
                _ => TallowText.Get("ui.warning.0027")
            };
            Grid.SetColumn(status, 1);
            buttonRow.Children.Add(status);
        }
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
            allow.IsEnabled = approved && keyValid;
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
        if (checkbox is not null && checkboxLabel is not null && keyPanel is not null && tty is not null)
        {
            // Variant 3: the agreement text flies into the box and the TTY key
            // field takes its place; clearing the check reverses the transition.
            CheckBox box = checkbox;
            TextBlock label = checkboxLabel;
            StackPanel panel = keyPanel;
            PasswordBox key = tty;
            panel.Visibility = Visibility.Collapsed;
            panel.Opacity = 1;
            box.Checked += (_, _) =>
            {
                FlyInto(label, into: true);
                panel.Visibility = Visibility.Visible;
                FadeIn(panel);
                key.Focus();
            };
            box.Unchecked += (_, _) =>
            {
                panel.Visibility = Visibility.Collapsed;
                label.Visibility = Visibility.Visible;
                label.Opacity = 1;
                label.RenderTransform = Transform.Identity;
            };
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
        if (fatal) window.Topmost = true;
        UiMotion.AnimateOpen(outer);

        onWindowCreated?.Invoke(window);
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
