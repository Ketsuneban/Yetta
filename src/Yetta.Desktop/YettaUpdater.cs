using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Yetta.Desktop;

public sealed record UpdateRelease(long Id, Version Version, string Tag, Uri InstallerUrl, Uri ChecksumUrl);

/// <summary>
/// Opt-in unprivileged preview updater: GitHub release metadata + SHA-256
/// integrity checking. A SHA-256 file from the same origin is NOT a substitute
/// for signing and secure release provenance. Never update privileged services.
/// </summary>
public static class YettaUpdater
{
    private const string Repo = "Ketsuneban/Yetta";
    private const long MaxInstallerBytes = 512L * 1024 * 1024;
    private static DateTimeOffset _apiRetryAfter;

    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Yetta", "0.4.3"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<UpdateRelease?> FindUpdateAsync(Version current, CancellationToken token = default)
    {
        var releases = await FeedReleasesAsync(token);
        return releases.Where(release => release.Version > current)
            .OrderByDescending(release => release.Version).FirstOrDefault();
    }

    private static async Task<IReadOnlyList<UpdateRelease>> FeedReleasesAsync(CancellationToken token)
    {
        using var http = Client();
        using var reply = await http.GetAsync($"https://github.com/{Repo}/releases.atom",
            HttpCompletionOption.ResponseHeadersRead, token);
        reply.EnsureSuccessStatusCode();
        if (reply.Content.Headers.ContentLength is long length && length > 2_000_000)
            throw new InvalidDataException("Лента релизов GitHub слишком большая.");
        await using var stream = await reply.Content.ReadAsStreamAsync(token);
        return await ParseFeedAsync(stream, token);
    }

    public static async Task<IReadOnlyList<UpdateRelease>> ParseFeedAsync(Stream stream,
        CancellationToken token = default)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 2_000_000
        });
        var feed = await XDocument.LoadAsync(reader, LoadOptions.None, token);
        XNamespace atom = "http://www.w3.org/2005/Atom";
        var releases = new List<UpdateRelease>();
        string prefix = $"/{Repo}/releases/tag/";
        foreach (var entry in feed.Root?.Elements(atom + "entry") ?? [])
        {
            string? href = entry.Elements(atom + "link")
                .FirstOrDefault(link => (string?)link.Attribute("rel") == "alternate")?
                .Attribute("href")?.Value;
            if (!Uri.TryCreate(href, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            string tag = uri.AbsolutePath[prefix.Length..];
            if (!Regex.IsMatch(tag, @"\Av\d+\.\d+\.\d+(?:-preview)?\z", RegexOptions.CultureInvariant) ||
                !Version.TryParse(tag[1..].Replace("-preview", "", StringComparison.OrdinalIgnoreCase),
                    out var version)) continue;
            releases.Add(new UpdateRelease(0, version, tag,
                new Uri($"https://github.com/{Repo}/releases/download/{tag}/YettaSetup.exe"),
                new Uri($"https://github.com/{Repo}/releases/download/{tag}/INSTALLER-SHA256.txt")));
        }
        if (releases.Count == 0) throw new InvalidDataException("Лента релизов GitHub пуста или повреждена.");
        return releases.OrderByDescending(item => item.Version).ToArray();
    }

    public static async Task<IReadOnlyList<UpdateRelease>> ListReleasesAsync(CancellationToken token = default)
    {
        if (DateTimeOffset.UtcNow < _apiRetryAfter) return await FeedReleasesAsync(token);
        using var http = Client();
        var releases = new List<UpdateRelease>();
        for (int page = 1; page <= 10; page++)
        {
            using var reply = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases?per_page=100&page={page}", token);
            if (IsRateLimited(reply))
            {
                RememberRateLimit(reply);
                return await FeedReleasesAsync(token);
            }
            reply.EnsureSuccessStatusCode();
            await using var stream = await reply.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            int count = 0;
            foreach (var release in json.RootElement.EnumerateArray())
            {
                count++;
                if (ParseRelease(release) is UpdateRelease item) releases.Add(item);
            }
            if (count < 100) break;
        }
        return releases.OrderByDescending(item => item.Version).ToArray();
    }

    public static async Task<UpdateRelease?> FindReleaseByIdAsync(long id, CancellationToken token = default)
    {
        if (DateTimeOffset.UtcNow < _apiRetryAfter)
            throw new InvalidOperationException("Лимит GitHub API достигнут. Используй тег релиза из /yetta version вместо числового ID.");
        using var http = Client();
        using var reply = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases/{id}", token);
        if (IsRateLimited(reply))
        {
            RememberRateLimit(reply);
            throw new InvalidOperationException("Лимит GitHub API достигнут. Используй тег релиза из /yetta version вместо числового ID.");
        }
        if (reply.StatusCode == HttpStatusCode.NotFound) return null;
        reply.EnsureSuccessStatusCode();
        await using var stream = await reply.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
        var release = ParseRelease(json.RootElement);
        return release?.Id == id ? release : null;
    }

    public static UpdateRelease? FindReleaseByTag(string? tag)
    {
        if (tag is null || !Regex.IsMatch(tag,
            @"\Av\d+\.\d+\.\d+(?:-preview)?\z", RegexOptions.CultureInvariant)) return null;
        if (!Version.TryParse(tag[1..].Replace("-preview", "", StringComparison.OrdinalIgnoreCase),
            out var version)) return null;
        return new UpdateRelease(0, version, tag,
            new Uri($"https://github.com/{Repo}/releases/download/{tag}/YettaSetup.exe"),
            new Uri($"https://github.com/{Repo}/releases/download/{tag}/INSTALLER-SHA256.txt"));
    }

    private static bool IsRateLimited(HttpResponseMessage reply) =>
        reply.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests;

    private static void RememberRateLimit(HttpResponseMessage reply)
    {
        DateTimeOffset retry = DateTimeOffset.UtcNow.AddMinutes(15);
        if (reply.Headers.TryGetValues("X-RateLimit-Reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), out long seconds))
        {
            try { retry = DateTimeOffset.FromUnixTimeSeconds(seconds).AddSeconds(5); }
            catch (ArgumentOutOfRangeException) { }
        }
        _apiRetryAfter = retry > DateTimeOffset.UtcNow ? retry : DateTimeOffset.UtcNow.AddMinutes(15);
    }

    private static UpdateRelease? ParseRelease(JsonElement release)
    {
        if (release.GetProperty("draft").GetBoolean()) return null;
        string tag = release.GetProperty("tag_name").GetString() ?? "";
        string raw = tag.TrimStart('v').Replace("-preview", "", StringComparison.OrdinalIgnoreCase);
        if (!Version.TryParse(raw, out var version)) return null;
        Uri? setup = null, checksum = null;
        foreach (var asset in release.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.StartsWith($"/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name == "YettaSetup.exe") setup = uri;
            if (name == "INSTALLER-SHA256.txt") checksum = uri;
        }
        return setup is not null && checksum is not null
            ? new UpdateRelease(release.GetProperty("id").GetInt64(), version, tag, setup, checksum)
            : null;
    }

    public static async Task<string> DownloadInstallerAsync(UpdateRelease release,
        IProgress<double>? progress = null, CancellationToken token = default)
    {
        using var http = Client();
        using var manifestReply = await http.GetAsync(release.ChecksumUrl,
            HttpCompletionOption.ResponseHeadersRead, token);
        manifestReply.EnsureSuccessStatusCode();
        if (manifestReply.Content.Headers.ContentLength is long manifestLength && manifestLength > 8192)
            throw new InvalidDataException("Файл контрольной суммы слишком большой.");
        await using var manifestStream = await manifestReply.Content.ReadAsStreamAsync(token);
        using var manifestBuffer = new MemoryStream();
        var manifestChunk = new byte[4096];
        int manifestRead;
        while ((manifestRead = await manifestStream.ReadAsync(manifestChunk, token)) > 0)
        {
            if (manifestBuffer.Length + manifestRead > 8192)
                throw new InvalidDataException("Файл контрольной суммы слишком большой.");
            manifestBuffer.Write(manifestChunk, 0, manifestRead);
        }
        string manifest = Encoding.UTF8.GetString(manifestBuffer.ToArray());
        var match = Regex.Match(manifest,
            @"\bSHA256\s+YettaSetup\.exe\s+([0-9a-fA-F]{64})\b",
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) throw new InvalidDataException("No installer SHA-256 in the release manifest.");

        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Yetta", "Updates");
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "YettaSetup-" + release.Version + ".exe");
        string partial = destination + "." + Guid.NewGuid().ToString("N") + ".download";

        // A verified installer can be reused after a cancelled or interrupted launch.
        try
        {
            if (File.Exists(destination) && new FileInfo(destination).Length <= MaxInstallerBytes)
            {
                await using var cached = File.OpenRead(destination);
                string cachedHash = Convert.ToHexString(await SHA256.HashDataAsync(cached, token));
                if (cachedHash.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(1);
                    return destination;
                }
            }
        }
        catch (IOException) { /* Redownload when the previous file is unavailable. */ }
        catch (UnauthorizedAccessException) { /* The new download reports a useful error if the folder is blocked. */ }

        try
        {
            using var reply = await http.GetAsync(release.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead, token);
            reply.EnsureSuccessStatusCode();
            if (reply.Content.Headers.ContentLength is long length && length > MaxInstallerBytes)
                throw new InvalidDataException("Installer exceeds size limit.");

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var input = await reply.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(partial, FileMode.CreateNew,
                FileAccess.Write, FileShare.None, 262144, useAsync: true))
            {
                var buffer = new byte[262144];
                long downloaded = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    downloaded += count;
                    if (downloaded > MaxInstallerBytes)
                        throw new InvalidDataException("Installer exceeds size limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    hash.AppendData(buffer, 0, count);
                    if (reply.Content.Headers.ContentLength is long total && total > 0)
                        progress?.Report(Math.Clamp((double)downloaded / total * 0.98, 0, 0.98));
                }
            }

            progress?.Report(0.99);
            string actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!actual.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer checksum mismatch.");
            File.Move(partial, destination, overwrite: true);
            progress?.Report(1);
            return destination;
        }
        catch
        {
            try { if (File.Exists(partial)) File.Delete(partial); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
    }
}
