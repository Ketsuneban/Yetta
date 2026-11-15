using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Yetta.Desktop;

/// <summary>
/// Tallow-side health gate for the separate unprivileged DAX tray companion.
/// Fail closed for the Yetta UI if agent is absent/disabled/restarting.
/// The protocol is intentionally NOT an authentication or security boundary.
/// </summary>
internal sealed class DaxAgentGate : IDisposable
{
    private const string PipeName = "Yetta-DAX-Local-v1";
    private readonly Window _owner;
    private readonly Grid _cover;
    private readonly Border _overlay;
    private readonly TextBlock _status;
    private readonly DispatcherTimer _timer;
    private bool _checking;
    private bool _disposed;

    internal DaxAgentGate(Window owner)
    {
        _owner = owner;
        if (owner.Content is not UIElement root) throw new InvalidOperationException("Yetta root missing.");
        _cover = new Grid();
        owner.Content = null;
        _cover.Children.Add(root);
        _overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(248, 9, 13, 21)),
            BorderThickness = new Thickness(0),
            Visibility = Visibility.Visible,
            Padding = new Thickness(24)
        };
        var content = new StackPanel
        {
            MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(new TextBlock
        {
            Text = "DAX НЕ ДОСТУПЕН", FontSize = 24, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 14)
        });
        _status = new TextBlock
        {
            Text = "Проверка связи с DAX...",
            FontSize = 15, TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.LightGray, Margin = new Thickness(0, 0, 0, 20)
        };
        content.Children.Add(_status);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var retry = new Button
        {
            Content = "Включить DAX", MinWidth = 160,
            Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 10, 0)
        };
        retry.Click += async (_, _) => { TryLaunch(); await CheckAsync(); };
        var reinstall = new Button
        {
            Content = "Переустановить Yetta", MinWidth = 190,
            Padding = new Thickness(12, 9, 12, 9)
        };
        reinstall.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://github.com/Ketsuneban/Yetta/releases") { UseShellExecute = true });
            }
            catch (Win32Exception) { }
        };
        row.Children.Add(retry);
        row.Children.Add(reinstall);
        content.Children.Add(row);
        _overlay.Child = content;
        _cover.Children.Add(_overlay);
        Panel.SetZIndex(_overlay, 10000);
        owner.Content = _cover;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += async (_, _) => await CheckAsync();
        owner.Loaded += async (_, _) =>
        {
            TryLaunch();
            _timer.Start();
            await CheckAsync();
        };
    }

    private string AgentFile => Path.Combine(AppContext.BaseDirectory, "Yetta.DAX.exe");

    private void TryLaunch()
    {
        if (!File.Exists(AgentFile))
        {
            _status.Text = "Не найден отдельный компонент DAX. Переустановите Yetta из официального релиза.";
            return;
        }
        try
        {
            if (Process.GetProcessesByName("Yetta.DAX").Length == 0)
                Process.Start(new ProcessStartInfo(AgentFile) { UseShellExecute = true, WorkingDirectory = AppContext.BaseDirectory });
        }
        catch (Exception ex) when (ex is Win32Exception or UnauthorizedAccessException or IOException)
        {
            _status.Text = "Не удалось включить DAX: " + ex.GetType().Name;
        }
    }

    internal static async Task<string> RequestAsync(string command, CancellationToken token = default)
    {
        using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(800, token);
        using var writer = new StreamWriter(client, new UTF8Encoding(false), bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        await writer.WriteLineAsync(command);
        return await reader.ReadLineAsync(token) ?? "OFFLINE";
    }

    private async Task CheckAsync()
    {
        if (_disposed || _checking) return;
        _checking = true;
        try
        {
            string response = await RequestAsync("PING");
            bool healthy = response == "READY";
            _overlay.Visibility = healthy ? Visibility.Collapsed : Visibility.Visible;
            _status.Text = response switch
            {
                "DISABLED" => "DAX отключён. Для работы Yetta снова включите DAX.",
                "WORKING" => "DAX выполняет проверку или перезапускается. Управление Yetta временно приостановлено.",
                "READY" => "",
                _ => "DAX не подтвердил готовность. Использование Yetta приостановлено."
            };
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException or InvalidOperationException)
        {
            _overlay.Visibility = Visibility.Visible;
            _status.Text = File.Exists(AgentFile)
                ? "Нет связи с DAX. Включите его или повторно установите Yetta."
                : "Компонент DAX не установлен рядом с Yetta. Требуется официальная переустановка.";
        }
        finally { _checking = false; }
    }

    public void Dispose() { _disposed = true; _timer.Stop(); }
}
