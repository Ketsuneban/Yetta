using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Yetta.Desktop;
using Yetta.Core;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using System.IO;

static class Program
{
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
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
        if (viewer.Template.FindName("ViewportVisual", viewer) is not FrameworkElement { OpacityMask: LinearGradientBrush })
            throw new Exception("Sharp content must fade before the blurred edge is composited.");
        var selectable = SelectableConsole.Wrap(new TextBlock { Text = "Первый вывод\nВторой вывод" });
        if (!new TextRange(selectable.Document.ContentStart, selectable.Document.ContentEnd).Text.Contains("Второй вывод"))
            throw new Exception("Selectable output lost its text.");
        if (!UpdateNotification.Template.Contains("value=\"{value}\"") || !UpdateNotification.Template.Contains("status=\"{status}\""))
            throw new Exception("Toast progress must use data bindings.");
        var owner = new Window { Width = 100, Height = 100, Left = -10000, Top = -10000, ShowInTaskbar = false };
        owner.Show();
        Directory.CreateDirectory("artifacts/ui-preview");
        foreach (var kind in new[] { WarningKind.Standard, WarningKind.Critical, WarningKind.Dax })
        {
            bool fields = kind == WarningKind.Critical;
            StandardWarningDialog.Show(owner, new WarningRequest(kind, "Проверка окна", "Описание всегда видно. Технические сведения раскрываются отдельно.",
                Subject: "DAX", IsPreview: true, RequirePassword: fields, RequireTty: fields, RequireAcknowledgement: fields,
                TechnicalDetails: "Техническая причина теста", RelatedPaths: [@"C:\Work\first.py", @"C:\Work\second.txt"]), "User", true,
                dialog =>
                {
                    var root = (FrameworkElement)dialog.Content;
                    var size = new Size(720, fields ? 770 : 560);
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    var elements = Descendants(root).ToArray();
                    var labels = elements.OfType<TextBlock>().ToArray();
                    var publisher = labels.Single(x => x.Text.StartsWith("Издатель:", StringComparison.Ordinal));
                    var subject = labels.Single(x => x.Text == "DAX");
                    if (publisher.Parent != subject.Parent) throw new Exception("Publisher must be directly below cause name.");
                    var details = labels.Single(x => x.Text == "Техническая причина теста");
                    if (details.Parent is not StackPanel { Visibility: Visibility.Collapsed }) throw new Exception("Details must start collapsed.");
                    if (elements.OfType<PasswordBox>().Count() != (fields ? 2 : 0)) throw new Exception("Password and TTY fields are independent.");
                    if (!labels.Any(x => x.Text.StartsWith("Описание всегда видно"))) throw new Exception("Description missing.");
                    var image = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
                    image.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    using var output = File.Create("artifacts/ui-preview/" + kind + ".png"); encoder.Save(output);
                }, renderOnly: true);
        }
        foreach (int stateIcon in new[] { 1, 2, 3 })
            if (IconAssets.Asset("States/" + stateIcon + ".png", 38).Source is null) throw new Exception("Missing state icon");
        if (YettaWarningWindow.State(new WarningRequest(WarningKind.Information, "i", "info", PotentialDamage: true), true) != WarningState.None ||
            YettaWarningWindow.State(new WarningRequest(WarningKind.Standard, "s", "decision"), false) != WarningState.Safe)
            throw new Exception("Badge policy confused information or Guest with Windows safe mode.");
        var parsedIdentity = PublisherInspector.Parse("""{"Status":"Valid","Signer":"Example Publisher","Thumbprint":"AB","Message":"OK"}""");
        if (!parsedIdentity.Verified || !parsedIdentity.Publisher.Contains("Example Publisher")) throw new Exception("Verified signer missing");
        var invalidIdentity = PublisherInspector.Parse("""{"Status":"HashMismatch","Signer":"Untrusted Claim","Message":"Changed"}""");
        if (invalidIdentity.Verified || invalidIdentity.Publisher.Contains("Untrusted Claim")) throw new Exception("Broken signature trusted its publisher");
        string unsignedFile = Path.Combine(Path.GetTempPath(), "Yetta signature's test; " + Guid.NewGuid() + ".ps1");
        File.WriteAllText(unsignedFile, "# Unsigned PowerShell script; verification never executes this file");
        try
        {
            var unsigned = PublisherInspector.InspectAsync(unsignedFile, default).GetAwaiter().GetResult();
            if (unsigned.Verified || unsigned.Status != "Файл не подписан") throw new Exception("Unsigned file verification failed: " + unsigned.Status + " " + unsigned.Technical);
            var folder = PublisherInspector.InspectAsync(Path.GetTempPath(), default).GetAwaiter().GetResult();
            if (folder.Verified || !folder.Publisher.StartsWith("Владелец:")) throw new Exception("Directory owner is not a publisher");
            string signedFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var signed = PublisherInspector.InspectAsync(signedFile, default).GetAwaiter().GetResult();
            if (!signed.Verified || !signed.Publisher.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Windows signature query failed: " + signed.Status + " " + signed.Technical);
        }
        finally { File.Delete(unsignedFile); }
        // Actual layout: output growth follows, manual wheel up pauses, wheel down to the bottom resumes.
        var lines = new StackPanel();
        for (int i = 0; i < 100; i++) lines.Children.Add(new TextBlock { Text = "Line " + i, Height = 20 });
        var scroll = new EdgeScrollViewer { Content = lines, Height = 160, Width = 320, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        owner.Content = scroll; owner.Width = 340; owner.Height = 220;
        using (var follow = new ConsoleFollowController(scroll))
        {
            void Pump() { owner.UpdateLayout(); owner.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { })); owner.UpdateLayout(); }
            Pump(); follow.ContentChanged(); Pump();
            if (scroll.VerticalOffset < scroll.ScrollableHeight - 1) throw new Exception("Console did not follow initial output");
            var mouse = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, 120) { RoutedEvent = UIElement.PreviewMouseWheelEvent };
            scroll.RaiseEvent(mouse); Pump();
            if (follow.Following) throw new Exception("Wheel up failed to pause following");
            double offset = scroll.VerticalOffset;
            lines.Children.Add(new TextBlock { Height = 100, Text = "More output" }); Pump();
            if (Math.Abs(offset - scroll.VerticalOffset) > 1) throw new Exception("New output moved a manually positioned console");
            scroll.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, -12000) { RoutedEvent = UIElement.PreviewMouseWheelEvent }); Pump();
            lines.Children.Add(new TextBlock { Height = 80, Text = "Last output" }); Pump();
            if (!follow.Following || scroll.VerticalOffset < scroll.ScrollableHeight - 1) throw new Exception("Following did not resume at bottom");
        }
        Console.WriteLine("PASS Authenticode, owner metadata, semantic badges and sticky console scrolling");
        owner.Close();
        Console.WriteLine("PASS selectable output, bound toast progress and rendered warning layouts");
    }
}
