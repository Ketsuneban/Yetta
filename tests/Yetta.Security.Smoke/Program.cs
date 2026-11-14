using System.Text;
using Yetta.Desktop;

static void Check(bool condition, string name)
{
    if (!condition) throw new Exception("Security smoke test failed: " + name);
    Console.WriteLine("PASS " + name);
}

var root = Path.Combine(Path.GetTempPath(), "yetta-tty-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    string storage = Path.Combine(root, "tty-key.dpapi");
    var store = new TtyKeyStore(storage);
    Check(!store.IsConfigured, "TTY key absent initially");
    string first = store.IssueNew();
    Check(first.Length == 64, "TTY key length 64");
    Check(store.IsConfigured && store.Validate(first), "DPAPI key protected and validated");
    Check(!store.Validate(first[..63]), "incomplete TTY key rejected");
    Check(!store.Validate(new string('Z', 64)), "wrong TTY key rejected");
    Check(first == store.GetOrCreate(), "key persists with DPAPI");
    var raw = File.ReadAllBytes(storage);
    Check(!Encoding.UTF8.GetString(raw).Contains(first, StringComparison.Ordinal),
        "TTY key never stored as plaintext");
    string second = store.IssueNew();
    Check(first != second && store.Validate(second), "TTY key rotation");
    Check(!store.Validate(first), "old TTY key invalid after rotation");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
}
Console.WriteLine("All security smoke tests passed.");
