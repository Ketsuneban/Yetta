using System.Net;
using System.Net.Http.Headers;

namespace Yetta.Core;

/// <summary>
/// Non-privileged text-only updater. Periodically retrieves the installed release
/// catalog. It never fetches or executes code or changes security policy.
/// </summary>
public sealed class TallowTextUpdates : IDisposable
{
    private static readonly Uri CatalogUri =
        new("https://raw.githubusercontent.com/Ketsuneban/Yetta/v0.7.2-preview/local.json");
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly CancellationTokenSource _stop = new();
    private FileSystemWatcher? _watcher;
    private EntityTagHeaderValue? _etag;
    private Task? _worker;
    private int _pendingReload;

    public void Start()
    {
        if (_worker is not null) return;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Yetta-TextCatalog/1.0");
        _http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };
        string folder = AppContext.BaseDirectory;
        _watcher = new FileSystemWatcher(folder, "local.json")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        _watcher.Changed += OnLocalChanged;
        _watcher.Created += OnLocalChanged;
        _watcher.Renamed += OnLocalRenamed;
        _worker = Task.Run(() => PollAsync(_stop.Token));
    }

    private void OnLocalChanged(object sender, FileSystemEventArgs e) => ScheduleReload();
    private void OnLocalRenamed(object sender, RenamedEventArgs e) => ScheduleReload();
    private void ScheduleReload()
    {
        if (Interlocked.Exchange(ref _pendingReload, 1) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, _stop.Token);
                TallowText.ReloadFromDisk();
            }
            catch (OperationCanceledException) { }
            finally { Interlocked.Exchange(ref _pendingReload, 0); }
        });
    }

    private async Task PollAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, CatalogUri);
                if (_etag is not null) req.Headers.IfNoneMatch.Add(_etag);
                using var response = await _http.SendAsync(req,
                    HttpCompletionOption.ResponseHeadersRead, token);
                if (response.StatusCode == HttpStatusCode.OK &&
                    response.Content.Headers.ContentLength is not > TallowText.MaximumBytes)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(token);
                    using var buffer = new MemoryStream();
                    var chunk = new byte[8192];
                    int length;
                    while ((length = await stream.ReadAsync(chunk, token)) > 0)
                    {
                        if (buffer.Length + length > TallowText.MaximumBytes)
                            throw new InvalidDataException("Catalog exceeds size limit");
                        buffer.Write(chunk, 0, length);
                    }
                    byte[] data = buffer.ToArray();
                    if (TallowText.TryUpdate(data))
                    {
                        _etag = response.Headers.ETag;
                        // Read-only installations remain live in memory.
                        TallowText.TryPersist(data);
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or
                IOException or InvalidDataException or UnauthorizedAccessException) { }
            try { await Task.Delay(TimeSpan.FromSeconds(60), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        if (_watcher is not null)
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnLocalChanged;
            _watcher.Created -= OnLocalChanged;
            _watcher.Renamed -= OnLocalRenamed;
            _watcher.Dispose();
        }
        _http.Dispose();
        _stop.Dispose();
    }
}
