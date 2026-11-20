using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private TextBlock? _diagnosticsStatus;

    private void OnDaxStatusChanged(DaxStatus status)
    {
        DaxAudit.Write("connection", status.State.ToString(), "Статус спутника DAX получен.");
        if (_diagnosticsStatus is not null) _diagnosticsStatus.Text = DaxStatusText(status);
        SidebarVersion.Text = "YETTA " + ReleaseVersion + " · DAX " + DaxStateLabel(status.State);
    }

    private static string DaxStateLabel(DaxState state) => state switch
    {
        DaxState.Ready => "На связи",
        DaxState.Working => "Проверяет компоненты",
        DaxState.Notice => "Требует внимания",
        DaxState.Disabled => "Отключён в трее",
        DaxState.Threat => "Сообщил об угрозе",
        DaxState.Fatal => "Сообщил о критическом сбое",
        DaxState.Incompatible => "Нужна совместимая версия DAX",
        _ => "Нет связи"
    };

    private static string DaxStatusText(DaxStatus status)
    {
        string state = "DAX · " + DaxStateLabel(status.State);
        if (status.AgentVersion.Length == 0) return state;
        return state + "\nВерсия модуля: " + status.AgentVersion +
            "\nYetta.exe: " + (status.DesktopPresent ? "на месте" : "не найден") +
            "\nlocal.json: " + (status.CatalogPresent ? "на месте" : "не найден");
    }

    private void BuildDiagnostics()
    {
        var status = _daxGate.Status;
        var panel = new StackPanel();
        panel.Children.Add(Text("Связь с DAX", 17, heading: true));
        var report = Text(DaxStatusText(status), 13);
        _diagnosticsStatus = report;
        report.Margin = new Thickness(0, 8, 0, 12);
        panel.Children.Add(report);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Проверить связь", async () =>
        {
            await _daxGate.CheckAsync();
            report.Text = DaxStatusText(_daxGate.Status);
        }, true));
        actions.Children.Add(ActionButton("Запустить DAX", async () =>
        {
            _daxGate.TryLaunch();
            await _daxGate.CheckAsync();
            report.Text = DaxStatusText(_daxGate.Status);
        }));
        actions.Children.Add(ActionButton("Открыть журнал", () => Navigate("logs")));
        panel.Children.Add(actions);
        ContentPanel.Children.Add(Card(panel));

        var integrity = new StackPanel();
        integrity.Children.Add(Text("Контрольные суммы файлов", 16, heading: true));
        var hashes = Text("Вычисли SHA-256, чтобы сравнить файлы с контрольными суммами выпуска.", 12, muted: true);
        hashes.Margin = new Thickness(0, 5, 0, 10);
        integrity.Children.Add(hashes);
        var hashButton = ActionButton("Вычислить SHA-256", () => { });
        hashButton.Click += async (_, _) =>
        {
            hashButton.IsEnabled = false;
            hashes.Text = "Чтение файлов…";
            try { hashes.Text = await ReadDaxHashesAsync(_updateStop.Token); }
            catch (OperationCanceledException) { }
            finally { hashButton.IsEnabled = true; }
        };
        integrity.Children.Add(hashButton);
        ContentPanel.Children.Add(Card(integrity));

        AddSection("Компоненты");
        var modules = new StackPanel();
        modules.Children.Add(Text("Guard · проверка профиля доступа и разрешения на запуск скриптов.", 13));
        modules.Children.Add(Text("Marvine · запущенные скрипты и их состояние.", 13));
        modules.Children.Add(Text("Audit · журнал решений и результатов Yetta.", 13));
        modules.Children.Add(Text("Update · проверка SHA-256 установщика.", 13));
        modules.Children.Add(Text("Threat и System · системный анализ и восстановление пока недоступны.", 13, muted: true));
        ContentPanel.Children.Add(Card(modules));
    }

    private static async Task<string> ReadDaxHashesAsync(CancellationToken token)
    {
        var results = new List<string>();
        foreach (string name in new[] { "Yetta.exe", "Yetta.DAX.exe", "local.json" })
        {
            try
            {
                await using var stream = new FileStream(name == "Yetta.exe" ? Environment.ProcessPath! : Path.Combine(AppContext.BaseDirectory, name),
                    FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024, useAsync: true);
                byte[] hash = await SHA256.HashDataAsync(stream, token);
                results.Add(name + "\n" + Convert.ToHexStringLower(hash));
            }
            catch (FileNotFoundException) { results.Add(name + " · не найден"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                results.Add(name + " · не удалось прочитать");
            }
        }
        return string.Join("\n\n", results);
    }

    private void BuildAuditSearch(StackPanel rows)
    {
        ContentPanel.Children.Add(Text("Поиск по категории, результату или описанию", 12, muted: true));
        var filter = SearchField("Поиск в журнале DAX");
        ContentPanel.Children.Add(filter);
        IReadOnlyList<DaxAudit.Entry> entries = [];
        bool loading = false;
        string? failure = null;
        var refresh = ActionButton("Обновить журнал", () => { });
        ContentPanel.Children.Add(refresh);
        void Render()
        {
            rows.Children.Clear();
            if (loading || failure is not null)
            {
                rows.Children.Add(Text(failure ?? "Чтение журнала…", 13, muted: true));
                return;
            }
            var query = filter.Text.Trim();
            var visible = entries.Where(entry => entry.Category.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Result.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                entry.Summary.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToArray();
            if (visible.Length == 0)
                rows.Children.Add(Text(entries.Count == 0 ? "Пока нет записей." : "Ничего не найдено.", 13, muted: true));
            foreach (var entry in visible)
            {
                var item = new StackPanel();
                item.Children.Add(Text($"{entry.Utc.ToLocalTime():dd.MM HH:mm:ss}  ·  {(entry.Actor?.Equals("TALLOW", StringComparison.OrdinalIgnoreCase) == true ? "Tallow" : entry.Actor ?? "Yetta")}  ·  {entry.Category}", 11, muted: true));
                item.Children.Add(Text(entry.Result, 13, heading: true));
                item.Children.Add(Text(entry.Summary, 12));
                rows.Children.Add(Card(item));
            }
        }

        async Task LoadAsync()
        {
            if (loading) return;
            loading = true;
            failure = null;
            refresh.IsEnabled = false;
            Render();
            try { entries = await Task.Run(() => DaxAudit.Recent(100)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failure = "Журнал недоступен.";
            }
            finally
            {
                loading = false;
                refresh.IsEnabled = true;
                Render();
            }
        }
        filter.TextChanged += (_, _) => Render();
        refresh.Click += async (_, _) => await LoadAsync();
        _ = LoadAsync();
    }
}
