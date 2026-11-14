using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yetta.Core;

namespace Yetta.Desktop;

internal sealed record WarningRequest(
    YettaWarningLevel Level, string Title, string Description,
    string ObjectName = "Yetta", string? FilePath = null,
    string? Publisher = null, WarningObjectKind ObjectKind = WarningObjectKind.Unknown,
    bool NeedsAgreement = false, bool NeedsTty = false,
    Action? OnReset = null, bool Modal = true, bool IsNotice = false);

/// <summary>
/// Consistent seven-level Yetta warning presenter. TTY gates explicit UI
/// consent only; checks never grant privileged Windows tokens.
/// </summary>
internal static class WarningDialog
{
    private static readonly Brush White = Brushes.White;
    private static readonly Brush Secondary = new SolidColorBrush(Color.FromRgb(224, 228, 242));
    private static readonly Brush Good = new SolidColorBrush(Color.FromRgb(132, 239, 180));
    private static readonly Brush Bad = new SolidColorBrush(Color.FromRgb(255, 128, 144));

    private static Color Tint(YettaWarningLevel level) => level switch
    {
        YettaWarningLevel.Caution => Color.FromRgb(237, 182, 55),
        YettaWarningLevel.Dangerous => Color.FromRgb(237, 83, 81),
        YettaWarningLevel.Critical => Color.FromRgb(145, 30, 55),
        YettaWarningLevel.Fatal => Color.FromRgb(24, 27, 35),
        YettaWarningLevel.Consent => Color.FromRgb(143, 150, 169),
        YettaWarningLevel.Dax => Color.FromRgb(56, 100, 162),
        _ => Color.FromRgb(111, 155, 221)
    };

    private static TextBlock Label(string text, int size, Brush foreground, bool bold = false) =>
        new()
        {
            Text = text, FontSize = size, Foreground = foreground,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            TextWrapping = TextWrapping.Wrap
        };

    private static Image Icon(int id, double size) => new()
    {
        Source = IconAtlas.Source(id),
        Width = size, Height = size,
        Stretch = Stretch.Uniform,
        SnapsToDevicePixels = true
    };

    private static Image Logo(string path, double size) => new()
    {
        Source = new BitmapImage(new Uri("pack://application:,,,/Assets/" + path)),
        Width = size, Height = size, Stretch = Stretch.Uniform
    };

    private static void OpenWeb(string term)
    {
        try
        {
            string url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(term);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception) { }
        catch (InvalidOperationException) { }
    }

    private static void RevealFile(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return;
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add("/select,");
            start.ArgumentList.Add(path);
            Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception) { }
    }

    internal static bool Show(Window owner, WarningRequest request, TtyKeyStore? keyStore = null,
        string profile = "User")
    {
        var level = request.Level;
        bool fatal = level == YettaWarningLevel.Fatal;
        bool consent = level == YettaWarningLevel.Consent;
        bool dax = level == YettaWarningLevel.Dax;
        bool needsKey = !request.IsNotice && (request.NeedsTty || WarningPolicy.RequiresTty(level, profile));
        if (fatal || dax) needsKey = false;
        bool needsAgreement = request.NeedsAgreement;
        if (fatal || dax || request.IsNotice) needsAgreement = false;

        var window = GlassDialog.Create(owner, "Yetta · " + WarningPolicy.Heading(level),
            710, 565, 530, 420);
        if (fatal) window.Topmost = true;

        var color = Tint(level);
        var heading = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 14) };
        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        titleStack.Children.Add(Icon(WarningPolicy.HeaderIcon(level), 34));
        var message = Label(WarningPolicy.Heading(level), 18, White, true);
        message.VerticalAlignment = VerticalAlignment.Center;
        message.Margin = new Thickness(10, 0, 0, 0);
        titleStack.Children.Add(message);
        DockPanel.SetDock(titleStack, Dock.Left);
        heading.Children.Add(titleStack);

        var close = GlassDialog.Button("", false);
        close.Content = Icon(16, 22);
        close.Width = 39; close.MinWidth = 39; close.Height = 36;
        close.ToolTip = "Закрыть";
        close.IsCancel = !fatal;
        close.Click += (_, _) => { if (!fatal) Finish(false); };
        if (!fatal)
        {
            DockPanel.SetDock(close, Dock.Right);
            heading.Children.Add(close);
        }

        var titleLine = new Grid { Margin = new Thickness(0, 5, 0, 18) };
        titleLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        UIElement objectIcon = fatal ? Icon(48, 45) : consent ? Logo("yetta-logo.png", 45) :
            dax ? Logo("dax-logo.png", 45) : Icon(WarningPolicy.ObjectIcon(request.ObjectKind), 45);
        var objectFrame = new Border
        {
            Width = 62, Height = 62, CornerRadius = new CornerRadius(14),
            Padding = new Thickness(8), Child = objectIcon,
            Background = new SolidColorBrush(Color.FromArgb(95, color.R, color.G, color.B)),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(170, 215, 232, 255)),
            Margin = new Thickness(0, 0, 14, 0)
        };
        titleLine.Children.Add(objectFrame);

        var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var subject = new TextBlock
        {
            Text = request.ObjectName, FontSize = 17,
            Foreground = White, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Cursor = Cursors.Hand, ToolTip = "Поиск сведений в браузере"
        };
        subject.MouseLeftButtonUp += (_, _) => OpenWeb(request.ObjectName);
        identity.Children.Add(subject);
        var secondary = Label(request.Publisher ?? "Неизвестный издатель", 11, Secondary);
        secondary.Margin = new Thickness(0, 5, 0, 0);
        secondary.Cursor = request.FilePath is null ? Cursors.Arrow : Cursors.Hand;
        secondary.ToolTip = "Показать файл в проводнике";
        secondary.MouseLeftButtonUp += (_, _) =>
        {
            if (request.FilePath is string path) RevealFile(path);
        };
        identity.Children.Add(secondary);
        Grid.SetColumn(identity, 1);
        titleLine.Children.Add(identity);

        int status = WarningPolicy.StatusIcon(level, ConsentDialog.InWindowsSafeMode, profile);
        if (status != 0)
        {
            var state = Icon(status, 29);
            state.Margin = new Thickness(12, 0, 0, 0);
            state.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(state, 2);
            titleLine.Children.Add(state);
        }

        var description = new StackPanel();
        var descriptionText = Label(request.Description, 13, White);
        descriptionText.LineHeight = 20;
        description.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(122, 11, 19, 37)),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(15),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 216, 229, 255)),
            BorderThickness = new Thickness(1), Child = descriptionText
        });
        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            var path = Label(request.FilePath, 11, Secondary);
            path.Margin = new Thickness(4, 12, 0, 0);
            description.Children.Add(path);
        }

        var inputs = new StackPanel { Margin = new Thickness(0, 8, 0, 10) };
        var checkbox = new CheckBox
        {
            Content = "Я прочитал предупреждение и осознанно разрешаю действие",
            IsChecked = false, Foreground = White,
            Margin = new Thickness(0, 4, 0, 9), FontSize = 12
        };
        bool validKey = false;
        if (needsAgreement) inputs.Children.Add(checkbox);
        PasswordBox? password = null;
        TextBox? visible = null;
        if (needsKey)
        {
            var keyGroup = new StackPanel();
            var keyRow = new DockPanel { LastChildFill = true };
            keyRow.Children.Add(Icon(36, 29));
            password = new PasswordBox
            {
                MaxLength = 64, MinHeight = 39, FontSize = 15,
                Padding = new Thickness(10, 7, 10, 7), Background = Brushes.White,
                Foreground = Brushes.Black, Margin = new Thickness(9, 0, 0, 0),
                ToolTip = "TTY-Ключ (64 символа)"
            };
            visible = new TextBox
            {
                MaxLength = 64, MinHeight = 39, FontSize = 15,
                Padding = new Thickness(10, 7, 10, 7),
                Visibility = Visibility.Collapsed,
                Background = Brushes.White, Foreground = Brushes.Black,
                Margin = new Thickness(9, 0, 0, 0)
            };
            var eye = GlassDialog.Button("", false);
            eye.Content = Icon(49, 21);
            eye.Width = 40; eye.MinWidth = 40;
            eye.Margin = new Thickness(9, 0, 0, 0);
            DockPanel.SetDock(eye, Dock.Right);
            keyRow.Children.Add(eye);
            DockPanel.SetDock(password, Dock.Left);
            // Use Grid for inputs, so revealed/masked entries share the same location.
            var inputGrid = new Grid();
            inputGrid.Children.Add(password);
            inputGrid.Children.Add(visible);
            keyRow.Children.Add(inputGrid);
            keyGroup.Children.Add(keyRow);
            var feedback = Label("Введите TTY-Ключ", 11, Secondary);
            feedback.Margin = new Thickness(39, 5, 0, 0);
            keyGroup.Children.Add(feedback);
            inputs.Children.Add(keyGroup);

            var pwd = password;
            var plain = visible;
            var feedbackLabel = feedback;
            bool syncing = false;
            void Validate(string candidate)
            {
                try { validKey = keyStore is not null && keyStore.Matches(candidate); }
                catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or InvalidDataException)
                { validKey = false; }
                feedbackLabel.Text = candidate.Length == 0 ? "Введите TTY-Ключ" :
                    validKey ? "Ключ совпадает ✔" : "Ключ неверный ✘";
                feedbackLabel.Foreground = candidate.Length == 0 ? Secondary : validKey ? Good : Bad;
            }
            pwd.PasswordChanged += (_, _) =>
            {
                if (syncing) return;
                Validate(pwd.Password);
                UpdateAccept();
            };
            plain.TextChanged += (_, _) =>
            {
                if (syncing) return;
                Validate(plain.Text);
                UpdateAccept();
            };
            eye.Click += (_, _) =>
            {
                syncing = true;
                if (plain.Visibility == Visibility.Collapsed)
                {
                    plain.Text = pwd.Password;
                    plain.Visibility = Visibility.Visible;
                    pwd.Visibility = Visibility.Collapsed;
                    eye.Content = Icon(50, 21);
                }
                else
                {
                    pwd.Password = plain.Text;
                    pwd.Visibility = Visibility.Visible;
                    plain.Visibility = Visibility.Collapsed;
                    eye.Content = Icon(49, 21);
                }
                syncing = false;
            };
            if (needsAgreement)
            {
                keyGroup.Visibility = Visibility.Collapsed;
                checkbox.Checked += (_, _) => keyGroup.Visibility = Visibility.Visible;
                checkbox.Unchecked += (_, _) => keyGroup.Visibility = Visibility.Collapsed;
            }
        }

        var actionRow = new Grid();
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var decline = GlassDialog.Button("Запретить");
        var approve = GlassDialog.Button(fatal ? "Сброс" :
            consent ? "Согласен" : dax || request.IsNotice ? "Ок" : "Разрешить", true);
        decline.HorizontalAlignment = HorizontalAlignment.Stretch;
        approve.HorizontalAlignment = HorizontalAlignment.Stretch;
        approve.Margin = new Thickness(7, 0, 0, 0);
        decline.Margin = new Thickness(0, 0, 7, 0);
        if (fatal || consent || dax || request.IsNotice)
        {
            actionRow.ColumnDefinitions.Clear();
            actionRow.Children.Add(approve);
            approve.Margin = new Thickness(0);
        }
        else
        {
            actionRow.Children.Add(decline);
            Grid.SetColumn(approve, 1);
            actionRow.Children.Add(approve);
        }
        void UpdateAccept() => approve.IsEnabled = WarningPolicy.CanApprove(
            checkbox.IsChecked == true, validKey, needsAgreement, needsKey);
        if (needsAgreement)
        {
            checkbox.Checked += (_, _) => UpdateAccept();
            checkbox.Unchecked += (_, _) => UpdateAccept();
        }
        UpdateAccept();
        void Finish(bool result)
        {
            if (request.Modal)
                window.DialogResult = result;
            else window.Close();
        }
        decline.Click += (_, _) => Finish(false);
        approve.Click += (_, _) =>
        {
            if (!WarningPolicy.CanApprove(checkbox.IsChecked == true, validKey,
                needsAgreement, needsKey)) return;
            if (fatal) request.OnReset?.Invoke();
            Finish(true);
        };

        var headerRegion = new StackPanel();
        headerRegion.Children.Add(heading);
        headerRegion.Children.Add(titleLine);
        window.Content = GlassDialog.Surface(
            GlassDialog.Layout(headerRegion, description, actionRow, inputs),
            new Thickness(24, 20, 24, 20), color, owner.Content as Visual);

        // ShowDialog returns an explicit decision; non-modal information-only
        // notices may overlap, but confirmations must return a decision.
        if (!request.Modal && (dax || request.IsNotice || level == YettaWarningLevel.Information))
        {
            window.Show();
            return false;
        }
        return window.ShowDialog() == true;
    }
}
