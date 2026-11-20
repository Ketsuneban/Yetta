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
    private static void VerifyMarvineLayout()
    {
        var window = new MainWindow();
        var nav = (Button)window.FindName("NavConsole");
        nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var host = (Border)window.FindName("ConsoleHost");
        var root = (FrameworkElement)host.Child;
        void Layout(int width, int height)
        {
            var size = new Size(width, height);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
        }
        Layout(560, 390);
        var input = Descendants(root).OfType<TextBox>().Single();
        input.Text = "/test warn";
        var expand = Descendants(root).OfType<Button>().Single(b => (b.Content as TextBlock)?.Text.StartsWith("Все варианты") == true);
        expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        foreach (var (width, height) in new[] { (560, 390), (850, 570) })
        {
            Layout(width, height);
            var viewer = Descendants(root).OfType<EdgeScrollViewer>().Single();
            var origin = input.TranslatePoint(new Point(0, 0), root);
            if (origin.Y <= 0 || origin.Y + input.ActualHeight > height || input.ActualWidth < 120 ||
                viewer.ActualHeight < 90 || root.DesiredSize.Height > height)
                throw new Exception("Expanded completions displaced the command input or transcript.");
        }
        input.Text = "script";
        var send = Descendants(root).OfType<Button>().Single(b => (b.Content as TextBlock)?.Text == "Выполнить");
        send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Layout(700, 430);
        var output = Descendants(root).OfType<RichTextBox>().Single();
        output.SelectAll();
        if (!output.Selection.Text.Contains("stop —")) throw new Exception("Script help unavailable from console.");
        input.Text = "script list";
        ((Button)window.FindName("NavInfo")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        root = (FrameworkElement)host.Child;
        Layout(700, 430);
        if (Descendants(root).OfType<TextBox>().Single().Text != "script list")
            throw new Exception("Returning to Marvine lost the draft.");
        var bitmap = new RenderTargetBitmap(700, 430, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create("artifacts/ui-preview/Marvine.png");
        png.Save(file);
        window.Close();
        Console.WriteLine("PASS Marvine at two sizes, expanded options, script help, selection and draft");
    }

    private static void VerifyPython()
    {
        var python = Environment.GetEnvironmentVariable("YETTA_SMOKE_PYTHON");
        if (string.IsNullOrEmpty(python)) return;
        var folder = Directory.CreateTempSubdirectory("Marvine Python ");
        try
        {
            var file = Path.Combine(folder.FullName, "Проба с пробелами.py");
            File.WriteAllText(Path.Combine(folder.FullName, "helper.py"), "greeting = 'Привет'");
            File.WriteAllText(file, "import sys, helper\nprint(helper.greeting, flush=True)\nprint('stderr', file=sys.stderr)");
            using var scripts = new MarvineScriptManager();
            var lines = new System.Collections.Concurrent.ConcurrentQueue<ScriptOutput>();
            using var exit = new ManualResetEventSlim();
            scripts.Start("probe", file, python, line => { lines.Enqueue(line); if (line.Exited) exit.Set(); });
            if (!exit.Wait(TimeSpan.FromSeconds(15)) || !lines.Any(l => l.Text == "Привет") ||
                !lines.Any(l => l.IsError && l.Text == "stderr") || !lines.Any(l => l.Exited && !l.IsError) ||
                scripts.List().Single().Running) throw new Exception("Python run, output or exit failed.");
            File.WriteAllText(file, "import time\nprint('ready', flush=True)\ntime.sleep(60)");
            using var ready = new ManualResetEventSlim();
            using var stopped = new ManualResetEventSlim();
            scripts.Start("again", file, python, l =>
            {
                if (l.Text == "ready") ready.Set();
                if (l.Exited) stopped.Set();
            });
            if (!ready.Wait(TimeSpan.FromSeconds(15)) || !scripts.Stop(file) ||
                !stopped.Wait(TimeSpan.FromSeconds(15)) || scripts.List().Any(s => s.Running))
                throw new Exception("Stopping a second run by the same file path failed.");
        }
        finally { folder.Delete(true); }
        Console.WriteLine("PASS real Python process, Unicode path, both output streams, exit and list");
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
        var transcript = new ConsoleTranscript(3);
        var form = new TextBox { Text = "pending" };
        transcript.Append(form, () => true);
        foreach (string word in new[] { "old", "first", "second" }) transcript.Append(new TextBlock { Text = word });
        var box = transcript.Children.OfType<RichTextBox>().Single();
        box.SelectAll();
        if (!box.Selection.Text.Contains("first") || !box.Selection.Text.Contains("second") ||
            box.Selection.Text.Contains("old") || !transcript.Children.Contains(form))
            throw new Exception("Cross-entry selection, trimming or form retention failed");
        transcript.Reset();
        if (transcript.Children.Count != 0) throw new Exception("Clear must also reset the grouped document.");
        VerifyPython();
        var owner = new Window { Width = 100, Height = 100, Left = -10000, Top = -10000, ShowInTaskbar = false };
        owner.Show();
        Directory.CreateDirectory("artifacts/ui-preview");
        VerifyMarvineLayout();
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
        var parsedIdentity = PublisherInspector.Describe(Microsoft.Security.Extensions.SignatureState.SignedAndTrusted, "Example Publisher", "OK");
        if (!parsedIdentity.Verified || !parsedIdentity.Publisher.Contains("Example Publisher")) throw new Exception("Verified signer missing");
        var invalidIdentity = PublisherInspector.Describe(Microsoft.Security.Extensions.SignatureState.Invalid, "Untrusted Claim", "Changed");
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
        Console.WriteLine("PASS selectable output, Marvine and rendered warning layouts");
    }
}
