using System.Security.Cryptography;
using System.Text;

namespace Yetta.Desktop;

/// <summary>
/// User-scoped DPAPI-protected confirmation secret. A TTY key is NOT an OS
/// elevation credential and does not bypass UAC or the DAX access policy.
/// </summary>
internal sealed class TtyKeyStore
{
    private readonly string _path;
    private static readonly string Alphabet =
        "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_";
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("Yetta.DAX.TTY.v1");

    public TtyKeyStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Yetta", "security", "tty-key.dpapi");
    }

    public bool IsConfigured => File.Exists(_path);

    public string IssueNew()
    {
        var output = new char[64];
        for (int i = 0; i < output.Length; i++)
            output[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        string key = new string(output);
        Save(key);
        Array.Clear(output);
        return key;
    }

    public string GetOrCreate()
    {
        if (!IsConfigured) return IssueNew();
        byte[] protectedBytes = File.ReadAllBytes(_path);
        byte[] raw = ProtectedData.Unprotect(
            protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        try
        {
            string value = Encoding.UTF8.GetString(raw);
            if (value.Length != 64 || value.Any(c => !Alphabet.Contains(c)))
                throw new InvalidDataException("Invalid protected TTY key.");
            return value;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
    }

    public bool Validate(string? submitted)
    {
        if (submitted?.Length != 64 || !IsConfigured) return false;
        var expected = Encoding.UTF8.GetBytes(GetOrCreate());
        var input = Encoding.UTF8.GetBytes(submitted);
        try
        {
            return CryptographicOperations.FixedTimeEquals(expected, input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private void Save(string key)
    {
        string directory = Path.GetDirectoryName(_path)
            ?? throw new InvalidOperationException("Invalid TTY storage path.");
        Directory.CreateDirectory(directory);
        byte[] raw = Encoding.UTF8.GetBytes(key);
        byte[] protectedBytes;
        try
        {
            protectedBytes = ProtectedData.Protect(
                raw, Entropy, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(raw);
        }
        string temp = _path + ".tmp";
        try
        {
            File.WriteAllBytes(temp, protectedBytes);
            File.Move(temp, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
            CryptographicOperations.ZeroMemory(protectedBytes);
        }
    }
}
