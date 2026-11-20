using System.Windows;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class App : Application
{
    private TallowTextUpdates? _live;

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.SequenceEqual(["--verify-bundle"]))
        {
            bool complete = new[] { "Yetta.DAX.exe", "YettaDaxAdministratorSetup.exe", "local.json", "Python/python.exe", "Python/python314.zip" }
                .All(name => System.IO.File.Exists(System.IO.Path.Combine(AppContext.BaseDirectory, name)));
            if (complete)
            {
                using var helper = System.IO.File.OpenRead(System.IO.Path.Combine(AppContext.BaseDirectory, "YettaDaxAdministratorSetup.exe"));
                string actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(helper));
                complete = actual.Equals(Yetta.Desktop.MainWindow.BundledAdministratorHash, StringComparison.OrdinalIgnoreCase);
            }
            Shutdown(complete ? 0 : 2);
            return;
        }
        TextSelection.Enable();
        foreach (var pair in TallowText.All)
            Resources["Local." + pair.Key] = pair.Value;
        TallowText.Changed += OnCatalogChanged;
        base.OnStartup(e);
        _live = new TallowTextUpdates();
        _live.Start();
    }

    private void OnCatalogChanged(object? sender, TextCatalogChangedEventArgs change)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            foreach (var pair in change.Current)
                if (!change.Previous.TryGetValue(pair.Key, out var old) || old != pair.Value)
                    Resources["Local." + pair.Key] = pair.Value;
            // Existing programmatically-created windows have plain string values,
            // unlike XAML DynamicResources. Update those without rebuilding forms.
            foreach (Window window in Windows)
                LiveTextWindows.Refresh(window, change);
        }));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        TallowText.Changed -= OnCatalogChanged;
        _live?.Dispose();
        base.OnExit(e);
    }
}
