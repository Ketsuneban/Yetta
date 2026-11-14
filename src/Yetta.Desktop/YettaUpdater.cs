using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Yetta.Desktop;

public sealed record UpdateRelease(Version Version, string Tag, Uri InstallerUrl, Uri ChecksumUrl);

/// <summary>
/// Opt-in unprivileged preview updater: GitHub release metadata + SHA-256
/// integrity checking. A SHA-256 file from the same origin is NOT a substitute
/// for signing and secure release provenance. Never update privileged services.
/// </summary>
public static class YettaUpdater
{
    private const string Repo = "Ketsuneban/Yetta";
    private const long MaxInstallerBytes = 512L * 1024 * 1024;

    private static HttpClient Client()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Yetta", "0.3.2"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static async Task<UpdateRelease?> FindUpdateAsync(Version current, CancellationToken token = default)
    {
        using var http = Client();
        using var reply = await http.GetAsync($"https://api.github.com/repos/{Repo}/releases?per_page=15", token);
        reply.EnsureSuccessStatusCode();
        await using var stream = await reply.Content.ReadAsStreamAsync(token);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);

        UpdateRelease? latest = null;
        foreach (var release in json.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string tag = release.GetProperty("tag_name").GetString() ?? "";
            string raw = tag.TrimStart('v').Replace("-preview", "", StringComparison.OrdinalIgnoreCase);
            if (!Version.TryParse(raw, out var version) || version <= current) continue;
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
            if (setup is not null && checksum is not null &&
                (latest is null || version > latest.Version))
                latest = new UpdateRelease(version, tag, setup, checksum);
        }
        return latest;
    }

    public static async Task<string> DownloadInstallerAsync(UpdateRelease release,
        CancellationToken token = default)
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
                }
            }

            await using var verify = File.OpenRead(destination);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(verify, token));
            if (!actual.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer checksum mismatch.");
            return destination;
        }
        catch
        {
            if (File.Exists(destination)) File.Delete(destination);
            throw;
        }
    }
}
