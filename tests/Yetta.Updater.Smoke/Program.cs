using System.Text;
using Yetta.Desktop;

const string feed = """
<?xml version="1.0" encoding="UTF-8"?>
<feed xmlns="http://www.w3.org/2005/Atom">
  <entry><link rel="alternate" href="https://github.com/Ketsuneban/Yetta/releases/tag/v0.3.25-preview"/></entry>
  <entry><link rel="alternate" href="https://evil.example/Ketsuneban/Yetta/releases/tag/v9.9.9-preview"/></entry>
  <entry><link rel="alternate" href="https://github.com/Ketsuneban/Yetta/releases/tag/../../evil"/></entry>
</feed>
""";

await using var input = new MemoryStream(Encoding.UTF8.GetBytes(feed));
var releases = await YettaUpdater.ParseFeedAsync(input);
if (releases.Count != 1 || releases[0].Tag != "v0.3.25-preview" ||
    releases[0].Version != new Version(0, 3, 25) ||
    releases[0].InstallerUrl.AbsoluteUri !=
    "https://github.com/Ketsuneban/Yetta/releases/download/v0.3.25-preview/YettaSetup.exe")
    throw new Exception("Release feed parser accepted an invalid entry or lost the valid release.");

if (YettaUpdater.FindReleaseByTag("../../evil") is not null ||
    YettaUpdater.FindReleaseByTag("v0.3.25-preview") is null)
    throw new Exception("Release tag validation failed.");

Console.WriteLine("PASS release feed and tag validation");
