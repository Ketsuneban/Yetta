using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Yetta.Desktop;
using Yetta.Core;

static class Program
{
    [STAThread]
    private static void Main()
    {
        // Load the actual styles and lay out a disconnected control, with no app window.
        var app = new App();
        app.InitializeComponent();
        var viewer = new EdgeScrollViewer
        {
            Style = (Style)app.FindResource(typeof(EdgeScrollViewer)),
            Content = new Border { Height = 900, Width = 100 },
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        viewer.Measure(new Size(320, 200));
        viewer.Arrange(new Rect(0, 0, 320, 200));
        viewer.UpdateLayout();
        viewer.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
        if (viewer.ScrollableHeight < 500 ||
            viewer.Template.FindName("PART_VerticalScrollBar", viewer) is not ScrollBar)
            throw new InvalidOperationException("The scroll template lost the scroll extent or its native scroll bar.");

        if (!SystemParameters.HighContrast &&
            (viewer.Template.FindName("TopPreview", viewer) is not UIElement { Visibility: Visibility.Collapsed } ||
             viewer.Template.FindName("BottomPreview", viewer) is not UIElement { Visibility: Visibility.Visible }))
            throw new InvalidOperationException("Only the lower edge should show at the top of a scrollable viewport.");

        Console.WriteLine("PASS scrollable viewport and edge preview");
        foreach (var kind in Enum.GetValues<WarningKind>())
        {
            var request = new WarningRequest(kind, "test", "test", Subject: "Tallow");
            if (YettaWarningWindow.ObjectIcon(request) != 6)
                throw new Exception("Severity replaced the actual cause: " + kind);
            var file = request with { SourcePath = @"C:\YettaSmokeNonexistent\script.py" };
            if (YettaWarningWindow.ObjectIcon(file) != 1)
                throw new Exception("A file became its registered application: " + kind);
        }
        for (int icon = 1; icon <= 16; icon++)
            if (IconAssets.Asset("Causes/" + icon + ".png", 44).Source is null)
                throw new Exception("Missing cause icon " + icon);
        Console.WriteLine("PASS all warning severities preserve causes and all 16 assets load");
    }
}
