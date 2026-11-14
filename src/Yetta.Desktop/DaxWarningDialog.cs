using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Yetta.Core;

namespace Yetta.Desktop;

internal enum DaxWarningResult { Denied, Allowed, Acknowledged, RecoveryOpened }

internal sealed record DaxWarningRequest(
    YettaWarningLevel Level,
    string Title,
    string Description,
    string SubjectName = "Yetta",
    string? TargetPath = null,
    string? Publisher = null,
    WarningSubjectType SubjectType = WarningSubjectType.Unknown,
    bool CheckRequired = false,
    bool KeyRequired = false,
    bool PotentialHarm = false,
    bool AccessDenied = false,
    bool DaxDenied = false,
    int? SubjectIconId = null,
    int? HeaderIconId = null);

/// <summary>
/// All owner-designed Yetta warnings: native WPF window, owner-scoped icon
/// selection, readable body area, fixed bottom actions, optional DPAPI-backed
/// TTY confirmation. Consent does not grant OS privileges.
/// </summary>
internal static class DaxWarningDialog
{
    private static readonly Brush Light = Brushes.White;
    private static readonly Brush Secondary = GlassDialog.SecondaryText;

    private static Color AccentFor(YettaWarningLevel level) => level switch
    {
        YettaWarningLevel.Caution => Color.FromRgb(255, 207, 83),
        YettaWarningLevel.Dangerous => Color.FromRgb(255, 101, 98),
        YettaWarningLevel.Critical => Color.FromRgb(219, 60, 87),
        YettaWarningLevel.Fatal => Color.FromRgb(167, 171, 183),
        YettaWarningLevel.Consent => Color.FromRgb(196, 203, 218),
        YettaWarningLevel.Dax => Color.FromRgb(105, 164, 251),
        _ => Color.FromRgb(175, 206, 255)
    };

    private static TextBlock Label(string text, double size, bool emphasis = false) =>
        new()
        {
            Text = text,
            FontSize = size,
            FontWeight = emphasis ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = emphasis ? Light : Secondary,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = size * 1.5
        };

    private static Border Panel(UIElement content, Color accent, Thickness padding) =>
        new()
        {
            Background = new LinearGradientBrush(
                Color.FromArgb(115, accent.R, accent.G, accent.B),
                Color.FromArgb(215, 16, 23, 39), 115),
            BorderBrush = new SolidColorBrush(
                Color.FromArgb(115, accent.R, accent.G, accent.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = padding,
            Child = content
        };

    private static void SearchFor(string subject)
    {
        try
        {
            var url = "https://www.google.com/search?q=" + Uri.EscapeDataString(subject);
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or InvalidOperationException) { }
    }

    private static void RevealFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var info = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
                info.ArgumentList.Add("/select,");
                info.ArgumentList.Add(Path.GetFullPath(path));
                Process.Start(info);
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(Path.GetFullPath(path))
                    { UseShellExecute = true });
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception
            or UnauthorizedAccessException or IOException or ArgumentException) { }
    }

    public static DaxWarningResult Show(Window owner, DaxWarningRequest request,
        TtyKeyStore? ttyStore = null, Action? onRecovery = null,
        string activeProfile = "User", bool safeMode = false)
    {
        var level = request.Level;
        bool fatal = level == YettaWarningLevel.Fatal;
        bool informational = WarningPolicy.IsInformational(level);
        bool agreement = level == YettaWarningLevel.Consent;
        bool checkboxRequired = request.CheckRequired;
        bool keyRequired = !fatal && (request.KeyRequired ||
            WarningPolicy.RequiresTty(level, activeProfile));
        Color accent = AccentFor(level);
        var dialog = GlassDialog.Create(owner,
            "Yetta · " + WarningPolicy.Heading(level), 675, 560,
            minWidth: 495, minHeight: 430);
        dialog.Topmost = fatal;
        dialog.ResizeMode = ResizeMode.NoResize;

        var heading = new Grid { Margin = new Thickness(0, 0, 0, 15) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        int headerIcon = request.HeaderIconId ??
            WarningPolicy.HeaderIcon(level, request.AccessDenied, request.DaxDenied);
        var smallIcon = IconLibrary.Create(headerIcon, 28);
        smallIcon.Margin = new Thickness(0, 0, 11, 0);
        heading.Children.Add(smallIcon);
        var headingLabel = Label(WarningPolicy.Heading(level), 14, true);
        headingLabel.VerticalAlignment = VerticalAlignment.Center;
        headingLabel.Foreground = new SolidColorBrush(accent);
        Grid.SetColumn(headingLabel, 1);
        heading.Children.Add(headingLabel);
        var closeButton = GlassDialog.Button("✕");
        closeButton.Width = 36;
        closeButton.MinWidth = 36;
        closeButton.MinHeight = 32;
        closeButton.Margin = new Thickness(0);
        closeButton.ToolTip = "Закрыть";
        Grid.SetColumn(closeButton, 2);
        heading.Children.Add(closeButton);

        var details = new StackPanel { Margin = new Thickness(0, 0, 5, 0) };
        var objectRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 20)
        };
        int subjectIcon = request.SubjectIconId ??
            (fatal ? 48 : agreement ? 2 : level == YettaWarningLevel.Dax
                ? 29 : WarningPolicy.SubjectIcon(request.SubjectType));
        var iconBorder = Panel(IconLibrary.Create(subjectIcon, 40),
            accent, new Thickness(12));
        iconBorder.Width = 68;
        iconBorder.Height = 68;
        iconBorder.Margin = new Thickness(0, 0, 17, 0);
        objectRow.Children.Add(iconBorder);
        var objectText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var subjectName = new Button
        {
            Content = request.SubjectName,
            Foreground = Light,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Cursor = Cursors.Hand,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Найти информацию в интернете"
        };
        subjectName.Click += (_, _) => SearchFor(request.SubjectName);
        objectText.Children.Add(subjectName);
        string publisherLabel = string.IsNullOrWhiteSpace(request.Publisher)
            ? "Неизвестный издатель" : request.Publisher!;
        var sourceButton = new Button
        {
            Content = publisherLabel,
            Foreground = Secondary,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = Cursors.Hand,
            FontSize = 11,
            ToolTip = "Показать расположение в Проводнике"
        };
        if (!string.IsNullOrWhiteSpace(request.TargetPath))
            sourceButton.Click += (_, _) => RevealFile(request.TargetPath!);
        else
            sourceButton.IsEnabled = false;
        objectText.Children.Add(sourceButton);
        objectRow.Children.Add(objectText);
        details.Children.Add(objectRow);

        var content = new StackPanel { Margin = new Thickness(0, 0, 2, 4) };
        var description = Label(request.Description, 13);
        description.Foreground = Light;
        content.Children.Add(description);
        if (!string.IsNullOrWhiteSpace(request.TargetPath))
        {
            var pathLabel = Label(request.TargetPath!, 11);
            pathLabel.Margin = new Thickness(0, 14, 0, 0);
            content.Children.Add(pathLabel);
        }
        details.Children.Add(Panel(content, accent, new Thickness(18, 16, 18, 16)));

        bool acknowledged = !checkboxRequired;
        bool verified = !keyRequired;

        var verification = new StackPanel { Margin = new Thickness(0, 8, 0, 6) };
        CheckBox? consent = null;
        if (checkboxRequired)
        {
            consent = new CheckBox
            {
                Content = "Я прочитал предупреждение и подтверждаю действие",
                IsChecked = false,
                Foreground = Light,
                Margin = new Thickness(1, 0, 0, 10),
                FontSize = 12
            };
            verification.Children.Add(consent);
        }

        var keyRow = new StackPanel { Visibility = keyRequired && checkboxRequired
            ? Visibility.Collapsed : keyRequired ? Visibility.Visible : Visibility.Collapsed };
        var field = new Grid();
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        field.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        field.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var keyIcon = IconLibrary.Create(36, 26);
        keyIcon.Margin = new Thickness(0, 0, 12, 0);
        keyIcon.VerticalAlignment = VerticalAlignment.Center;
        field.Children.Add(keyIcon);
        var input = new PasswordBox
        {
            MaxLength = 64,
            FontSize = 13,
            Height = 36,
            PasswordChar = '•',
            Background = new SolidColorBrush(Color.FromArgb(230, 20, 28, 44)),
            Foreground = Light,
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 5, 10, 5),
            ToolTip = "TTY-Ключ"
        };
        Grid.SetColumn(input, 1);
        field.Children.Add(input);
        var visibleInput = new TextBox
        {
            MaxLength = 64,
            FontSize = 13,
            Height = 36,
            Visibility = Visibility.Collapsed,
            Background = input.Background,
            Foreground = Light,
            BorderBrush = input.BorderBrush,
            Padding = input.Padding,
            ToolTip = "TTY-Ключ"
        };
        Grid.SetColumn(visibleInput, 1);
        field.Children.Add(visibleInput);
        bool revealing = false;
        var reveal = GlassDialog.Button("Показать");
        reveal.MinWidth = 90;
        reveal.MinHeight = 34;
        Grid.SetColumn(reveal, 2);
        field.Children.Add(reveal);
        var keyStatus = Label("Введите свой 64-символьный TTY-ключ", 11);
        keyStatus.Margin = new Thickness(38, 6, 0, 5);
        void Evaluate(string value)
        {
            verified = false;
            if (value.Length == 64 && ttyStore is not null)
            {
                try { verified = ttyStore.Validate(value); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException
                    or System.Security.Cryptography.CryptographicException or InvalidDataException)
                {
                    verified = false;
                }
            }
            keyStatus.Text = value.Length == 0 ? "TTY-Ключ" :
                verified ? "Ключ совпадает ✔" : "Ключ неверный ✘";
            keyStatus.Foreground = value.Length == 0 ? Secondary :
                verified ? Brushes.LightGreen : Brushes.Salmon;
            var border = value.Length == 0 ? accent :
                verified ? Colors.LightGreen : Colors.Salmon;
            input.BorderBrush = new SolidColorBrush(border);
            visibleInput.BorderBrush = new SolidColorBrush(border);
        }
        input.PasswordChanged += (_, _) =>
        {
            if (!revealing) Evaluate(input.Password);
        };
        visibleInput.TextChanged += (_, _) =>
        {
            if (revealing) Evaluate(visibleInput.Text);
        };
        reveal.Click += (_, _) =>
        {
            revealing = !revealing;
            if (revealing)
            {
                visibleInput.Text = input.Password;
                visibleInput.Visibility = Visibility.Visible;
                input.Visibility = Visibility.Collapsed;
                reveal.Content = "Скрыть";
                visibleInput.Focus();
            }
            else
            {
                input.Password = visibleInput.Text;
                input.Visibility = Visibility.Visible;
                visibleInput.Visibility = Visibility.Collapsed;
                reveal.Content = "Показать";
                input.Focus();
            }
        };
        keyRow.Children.Add(field);
        keyRow.Children.Add(keyStatus);
        if (keyRequired) verification.Children.Add(keyRow);

        if (consent is not null)
        {
            CheckBox current = consent;
            current.Checked += (_, _) =>
            {
                acknowledged = true;
                if (keyRequired)
                {
                    keyRow.Visibility = Visibility.Visible;
                    // Keep an accessible checkbox so confirmation can be revoked.
                    current.Content = "Согласие подтверждено ✓ (снять галочку)";
                }
            };
            current.Unchecked += (_, _) =>
            {
                acknowledged = false;
                if (keyRequired)
                {
                    keyRow.Visibility = Visibility.Collapsed;
                    input.Clear();
                    visibleInput.Clear();
                    verified = false;
                    current.Content = "Я прочитал предупреждение и подтверждаю действие";
                }
            };
        }

        DaxWarningResult decision = DaxWarningResult.Denied;
        var actions = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        void Finish(DaxWarningResult result)
        {
            decision = result;
            dialog.DialogResult = result is DaxWarningResult.Allowed or
                DaxWarningResult.Acknowledged or DaxWarningResult.RecoveryOpened;
        }

        if (!fatal && !agreement && !informational)
        {
            var decline = GlassDialog.Button("Запретить");
            decline.Click += (_, _) => Finish(DaxWarningResult.Denied);
            buttons.Children.Add(decline);
        }
        var proceed = GlassDialog.Button(fatal ? "Сброс" :
            agreement ? "Согласен" : informational ? "ОК" : "Разрешить", primary: true);
        if (fatal || agreement || informational)
            proceed.MinWidth = 228;
        proceed.Click += (_, _) =>
        {
            if (!acknowledged || !verified) return;
            if (fatal)
            {
                Finish(DaxWarningResult.RecoveryOpened);
                onRecovery?.Invoke();
            }
            else
                Finish(agreement || informational
                    ? DaxWarningResult.Acknowledged : DaxWarningResult.Allowed);
        };
        buttons.Children.Add(proceed);
        if (checkboxRequired || keyRequired)
        {
            proceed.IsEnabled = false;
            void SyncEnabled()
            {
                proceed.IsEnabled = acknowledged && verified;
            }
            if (consent is not null)
            {
                consent.Checked += (_, _) => SyncEnabled();
                consent.Unchecked += (_, _) => SyncEnabled();
            }
            input.PasswordChanged += (_, _) => SyncEnabled();
            visibleInput.TextChanged += (_, _) => SyncEnabled();
        }
        actions.Children.Add(buttons);
        if (!fatal)
        {
            int statusId = WarningPolicy.StatusIcon(level, safeMode, activeProfile);
            var safety = IconLibrary.Create(statusId, 32);
            safety.HorizontalAlignment = HorizontalAlignment.Right;
            safety.VerticalAlignment = VerticalAlignment.Center;
            safety.ToolTip = statusId == 35 ? "Ограниченный режим" :
                statusId == 26 ? "Потенциально опасное действие" : "Безопасное действие";
            Grid.SetColumn(safety, 1);
            actions.Children.Add(safety);
        }

        closeButton.Click += (_, _) => Finish(DaxWarningResult.Denied);
        dialog.Content = GlassDialog.Surface(
            GlassDialog.Layout(heading, details, actions, verification));
        if (fatal)
        {
            dialog.Topmost = true;
            dialog.Activate();
        }
        dialog.ShowDialog();
        return decision;
    }
}
