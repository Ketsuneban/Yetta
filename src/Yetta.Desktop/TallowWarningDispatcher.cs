using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>
/// Routes non-modal warnings through Tallow's five-slot admission policy.
/// The sixth surface is a browsable, denial-capable queue: queued requests
/// are NEVER approved without passing through their full warning gate.
/// </summary>
internal static class TallowWarningDispatcher
{
    private static readonly TallowWarningQueue<QueuedWarning> Policy = new();
    private static readonly Dictionary<Guid, Window> OpenWindows = [];
    private static Window? _queueWindow;
    private static TextBlock? _queueLabel;
    private static Button? _previous;
    private static Button? _next;
    private static Button? _reject;
    private static int _selection;

    private sealed record QueuedWarning(Window Owner, WarningRequest Request, string Profile);

    internal static bool Show(Window owner, WarningRequest request, string profile, bool nonModal)
    {
        // Modal actions remain synchronous; never turn an authorization result
        // into an implicit false or queued approval.
        if (!nonModal) return StandardWarningDialog.Show(owner, request, profile, false);
        if (!owner.Dispatcher.CheckAccess())
            throw new InvalidOperationException("Warning dispatcher must run on the WPF UI thread.");

        var entry = new QueuedWarning(owner, request, profile);
        if (request.Kind == WarningKind.Fatal)
        {
            // Clear FIRST, so Closed callbacks cannot promote now-obsolete requests.
            Policy.Reset();
            foreach (var oldWindow in OpenWindows.Values.ToArray())
                oldWindow.Close();
            OpenWindows.Clear();
            _queueWindow?.Close();
            _queueWindow = null;
            _selection = 0;
        }

        string key = string.Join("|", request.Kind, request.Reason, request.Subject, request.SourcePath ?? "");
        var admission = Policy.Submit(key, entry, request.Kind == WarningKind.Fatal);
        if (admission.Item is null)
        {
            if (admission.Result == TallowWarningQueue<QueuedWarning>.Outcome.Suppressed)
                DaxAudit.Write("warning", "duplicate_suppressed", request.Kind + ": " + request.Reason);
            return false;
        }
        if (admission.Result == TallowWarningQueue<QueuedWarning>.Outcome.Queued)
        {
            DaxAudit.Write("warning", "queued", request.Kind + ": " + request.Reason);
            EnsureQueue(owner);
            RefreshQueue();
            return false;
        }

        Open(admission.Item);
        return false;
    }

    private static void Open(TallowWarningQueue<QueuedWarning>.Ticket ticket)
    {
        var data = ticket.Payload;
        StandardWarningDialog.Show(data.Owner, data.Request, data.Profile, true, window =>
        {
            OpenWindows[ticket.Id] = window;
            window.Closed += (_, _) =>
            {
                OpenWindows.Remove(ticket.Id);
                var next = Policy.Close(ticket.Id);
                if (next is not null) Open(next);
                RefreshQueue();
            };
        });
    }

    private static void EnsureQueue(Window owner)
    {
        if (_queueWindow is not null) return;
        var window = new Window
        {
            Title = TallowText.Get("tallow.queue.0001"),
            Owner = owner, Width = 480, Height = 290,
            MinWidth = 420, MinHeight = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(29, 32, 40)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };
        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = TallowText.Get("tallow.queue.0002"), FontSize = 21, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });
        _queueLabel = new TextBlock
        {
            FontSize = 14, TextWrapping = TextWrapping.Wrap, MinHeight = 90,
            Foreground = Brushes.White
        };
        root.Children.Add(new EdgeScrollViewer
        {
            Content = _queueLabel, MaxHeight = 108, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Background = window.Background
        });
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _previous = MakeButton(TallowText.Get("tallow.queue.0003"), () => { _selection--; RefreshQueue(); });
        _next = MakeButton(TallowText.Get("tallow.queue.0004"), () => { _selection++; RefreshQueue(); });
        _reject = MakeButton(TallowText.Get("tallow.queue.0005"), () =>
        {
            var items = Policy.Pending;
            if (items.Count == 0) return;
            var item = items[Math.Clamp(_selection, 0, items.Count - 1)];
            Policy.RejectQueued(item.Id);
            DaxAudit.Write("warning", "queued_rejected", item.Payload.Request.Kind + ": " + item.Payload.Request.Reason);
            RefreshQueue();
        });
        row.Children.Add(_previous);
        row.Children.Add(_next);
        row.Children.Add(_reject);
        root.Children.Add(row);
        root.Children.Add(new TextBlock
        {
            Text = TallowText.Get("tallow.queue.0006"),
            Foreground = Brushes.LightGray, FontSize = 11, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 16, 0, 0)
        });
        window.Content = root;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_queueWindow, window))
                _queueWindow = null;
            _queueLabel = null;
            _previous = null;
            _next = null;
            _reject = null;
        };
        _queueWindow = window;
        window.Show();
    }

    private static Button MakeButton(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(3), MinHeight = 34, FocusVisualStyle = null,
            Style = (Style)Application.Current.MainWindow.FindResource("SecondaryButton") };
        UiMotion.AttachButtonFeedback(button);
        button.Click += (_, _) => action();
        return button;
    }

    private static void RefreshQueue()
    {
        var queued = Policy.Pending;
        if (queued.Count == 0)
        {
            _queueWindow?.Close();
            _selection = 0;
            return;
        }
        _selection = Math.Clamp(_selection, 0, queued.Count - 1);
        var item = queued[_selection].Payload.Request;
        if (_queueLabel is not null)
            _queueLabel.Text = TallowText.Format("tallow.queue.0007", _selection + 1, queued.Count, Policy.VisibleCount, item.Reason, item.Subject, item.Description);
        if (_previous is not null) _previous.IsEnabled = _selection > 0;
        if (_next is not null) _next.IsEnabled = _selection < queued.Count - 1;
        if (_reject is not null) _reject.IsEnabled = queued.Count > 0;
    }
}
