using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<Guid, Action<ScriptOutput>> _scriptSessions = [];
    private System.Windows.Media.Brush ConsoleErrorBrush => _settings.MarvineColorset == "No Color" ? ConsoleInk :
        Dracula ? Hex("#FF7777") : ConsoleSurface is System.Windows.Media.SolidColorBrush s && s.Color.G > 150 ? Hex("#A3212A") : Hex("#FF9696");

    private void OpenScriptSession(string id, string path, string interpreter, string directory)
    {
        var layout = new DockPanel();
        var window = MiniWindow("Subsy · " + id, layout, modal: false);
        window.SizeToContent = SizeToContent.Manual;
        window.Width = 720;
        window.Height = 515;
        window.MinWidth = 460;
        window.MinHeight = 320;
        window.ResizeMode = ResizeMode.CanResizeWithGrip;
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
        header.Children.Add(Text(Path.GetFileName(path), 19, heading: true));
        var folder = Text(directory, muted: true);
        folder.TextTrimming = TextTrimming.CharacterEllipsis;
        folder.TextWrapping = TextWrapping.NoWrap;
        folder.ToolTip = directory;
        header.Children.Add(folder);
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);

        var input = new TextBox
        {
            MaxLength = 4096, MinHeight = 36, Padding = new Thickness(8),
            Background = ConsolePanel, Foreground = ConsoleInk, CaretBrush = ConsoleInk,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        System.Windows.Automation.AutomationProperties.SetName(input, "Строка для скрипта");
        var stateLabel = Text("Скрипт выполняется", 12, muted: true);
        var run = Guid.Empty;
        bool running = true, closed = false, sending = false;
        PromptState? prompt = null;
        var send = SubsyButton("Отправить", () => { }, true);
        var stop = SubsyButton("Остановить", () => Stop());
        send.Click += async (_, _) => await SendAsync();
        var row = new DockPanel();
        DockPanel.SetDock(stop, Dock.Right);
        DockPanel.SetDock(send, Dock.Right);
        row.Children.Add(stop);
        row.Children.Add(send);
        row.Children.Add(input);
        var footer = new StackPanel { Margin = new Thickness(0, 9, 0, 0), Children = { stateLabel, row } };
        DockPanel.SetDock(footer, Dock.Bottom);
        layout.Children.Add(footer);

        var transcript = new ConsoleTranscript(500);
        var pendingLine = LiteralConsoleLine("", ConsoleInk);
        pendingLine.Visibility = Visibility.Collapsed;
        var scroll = new EdgeScrollViewer
        {
            Content = new StackPanel { Children = { transcript, pendingLine }, Margin = new Thickness(8) },
            Background = ConsoleSurface, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        layout.Children.Add(scroll);
        var follow = new ConsoleFollowController(scroll);
        SubsyView? progress = null;
        SubsyFrame? lastFrame = null;

        void RefreshInput()
        {
            input.IsEnabled = send.IsEnabled = running && !sending && prompt is null;
            stop.IsEnabled = running;
            input.ToolTip = prompt is null ? "Введите строку и нажмите Enter" : "Ответь на форму в выводе скрипта";
        }

        async Task SendAsync()
        {
            if (!input.IsEnabled) return;
            sending = true;
            RefreshInput();
            try
            {
                await _scripts.SendAsync(run, input.Text, _updateStop.Token);
                input.Clear();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or OperationCanceledException)
            {
                if (!closed) stateLabel.Text = "Не удалось передать строку скрипту.";
            }
            finally
            {
                sending = false;
                if (!closed) { RefreshInput(); if (input.IsEnabled) input.Focus(); }
            }
        }

        bool Stop()
        {
            try
            {
                if (_scripts.StopRun(run)) stateLabel.Text = "Остановка…";
                return true;
            }
            catch (Win32Exception ex)
            {
                stateLabel.Text = "Не удалось остановить скрипт. " + ex.Message;
                return false;
            }
        }

        void Receive(ScriptOutput output)
        {
            if (closed) return;
            if (output.Partial)
            {
                const string prefix = "@subsy ";
                if (!prefix.StartsWith(output.Text, StringComparison.Ordinal) &&
                    !output.Text.StartsWith(prefix, StringComparison.Ordinal))
                {
                    pendingLine.Text = output.Text;
                    pendingLine.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (!output.IsError || output.Exited) pendingLine.Visibility = Visibility.Collapsed;
                if (output.Exited)
                {
                    running = false;
                    if (prompt is { } current)
                    {
                        current.Result = "Скрипт завершён";
                        current.Refresh();
                        prompt = null;
                    }
                    if (progress is not null && lastFrame is not null)
                        RefreshSubsyView(progress, id, lastFrame with { IsBusy = false, Finished = true, Failed = output.IsError });
                    stateLabel.Text = output.Text;
                    RefreshInput();
                }
                else if (!output.IsError && SubsyPrompt.Parse(output.Text) is { } request)
                {
                    if (prompt is null)
                    {
                        var form = new PromptState(request, async value =>
                        {
                            try { await _scripts.AnswerAsync(run, request.Id, value, _updateStop.Token); }
                            finally
                            {
                                prompt = null;
                                RefreshInput();
                                if (input.IsEnabled && !closed) input.Focus();
                            }
                        }, id);
                        prompt = form;
                        transcript.Append(RenderPrompt(form, window, directory), () => form.Result is null);
                        RefreshInput();
                    }
                    else transcript.Append(LiteralConsoleLine("Скрипт уже ждёт ответа на другую форму.", ConsoleErrorBrush));
                }
                else if (!output.IsError && SubsyFrame.Parse(output.Text) is { } frame)
                {
                    if (progress is null)
                    {
                        var (card, view) = BuildProgressCard(id, frame);
                        transcript.Append(card, () => ReferenceEquals(progress, view));
                        progress = view;
                    }
                    else RefreshSubsyView(progress, id, frame);
                    lastFrame = frame;
                    if (frame.Finished) progress = null;
                }
                else transcript.Append(LiteralConsoleLine(output.Text, output.IsError ? ConsoleErrorBrush : ConsoleInk));
            }
            follow.ContentChanged();
        }

        input.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await SendAsync();
        };
        window.Closing += (_, e) => { if (running && !Stop()) e.Cancel = true; };
        window.Closed += (_, _) => { closed = true; follow.Dispose(); };
        try
        {
            run = _scripts.Start(id, path, interpreter, EnqueueScriptOutput, directory);
            _scriptSessions.Add(run, Receive);
            window.Show();
            input.Focus();
        }
        catch
        {
            follow.Dispose();
            if (run != Guid.Empty) { closed = true; Stop(); }
            throw;
        }
    }
}
