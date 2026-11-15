using System.Windows;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class App : Application
{
    private TallowTextUpdates? _live;

    protected override void OnStartup(StartupEventArgs e)
    {
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
