using System.Windows;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        foreach (var pair in TallowText.All)
            Resources["Local." + pair.Key] = pair.Value;
        base.OnStartup(e);
    }
}
