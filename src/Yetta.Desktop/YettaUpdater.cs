using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Yetta", "0.3.24"));
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
        var manifest = await http.GetStringAsync(release.ChecksumUrl, token);
        var match = Regex.Match(manifest,
            @"\bSHA256\s+YettaSetup\.exe\s+([0-9a-fA-F]{64})\b",
            RegexOptions.CultureInvariant);
        if (!match.Success) throw new InvalidDataException("No installer SHA-256 in the release manifest.");

        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Yetta", "Updates");
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, "YettaSetup-" + release.Version + ".exe");

        try
        {
            using var reply = await http.GetAsync(release.InstallerUrl,
                HttpCompletionOption.ResponseHeadersRead, token);
            reply.EnsureSuccessStatusCode();
            if (reply.Content.Headers.ContentLength is long length && length > MaxInstallerBytes)
                throw new InvalidDataException("Installer exceeds size limit.");

            await using (var input = await reply.Content.ReadAsStreamAsync(token))
            await using (var output = new FileStream(destination, FileMode.Create,
                FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                long downloaded = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, token)) > 0)
                {
                    downloaded += count;
                    if (downloaded > MaxInstallerBytes)
                        throw new InvalidDataException("Installer exceeds size limit.");
                    await output.WriteAsync(buffer.AsMemory(0, count), token);
                    if (reply.Content.Headers.ContentLength is long total && total > 0)
                        progress?.Report(Math.Clamp((double)downloaded / total, 0, 1));
                }
            }

            await using var verify = File.OpenRead(destination);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(verify, token));
            if (!actual.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer checksum mismatch.");
            progress?.Report(1);
            return destination;
        }
        catch
        {
            if (File.Exists(destination)) File.Delete(destination);
            throw;
        }
    }
}
