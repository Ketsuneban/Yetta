using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Yetta.Desktop;

/// <summary>DPAPI CurrentUser-protected 64-symbol confirmation token; NOT a Windows privilege token.</summary>
internal sealed class TtyKeyStore
{
    private const int Size = 64;
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789-_";
    private readonly string _path;

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob { public int Size; public IntPtr Data; }

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input,
        string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt,
        int flags, out DataBlob output);

    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input,
        IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt,
        int flags, out DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);

    internal TtyKeyStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "Yetta", "tty.key.dpapi");
    }

    private static byte[] Dpapi(byte[] bytes, bool encrypt)
    {
        var input = new DataBlob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            DataBlob output;
            bool success;
            if (encrypt)
                success = CryptProtectData(ref input, "Yetta TTY confirmation",
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x01, out output);
            else
                success = CryptUnprotectData(ref input, IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x01, out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                if (output.Size is <= 0 or > 4096) throw new InvalidDataException("Invalid DPAPI result.");
                var result = new byte[output.Size];
                Marshal.Copy(output.Data, result, 0, result.Length);
                return result;
            }
            finally { _ = LocalFree(output.Data); }
        }
        finally
        {
            Marshal.Copy(new byte[bytes.Length], 0, input.Data, bytes.Length);
            Marshal.FreeHGlobal(input.Data);
        }
    }

    internal bool Exists => File.Exists(_path);

    internal string CreateOrRotate()
    {
        Span<char> symbols = stackalloc char[Size];
        for (int i = 0; i < symbols.Length; i++)
            symbols[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        string key = new(symbols);
        symbols.Clear();
        byte[] plain = Encoding.UTF8.GetBytes(key);
        try
        {
            byte[] encrypted = Dpapi(plain, encrypt: true);
            string folder = Path.GetDirectoryName(_path)!;
            Directory.CreateDirectory(folder);
            string tmp = _path + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, encrypted);
                File.Move(tmp, _path, overwrite: true);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
            return key;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal string ReadForCopy()
    {
        if (!Exists) throw new FileNotFoundException("TTY key not initialized.");
        byte[] plain = Dpapi(File.ReadAllBytes(_path), encrypt: false);
        try
        {
            string key = Encoding.UTF8.GetString(plain);
            if (key.Length != Size || key.Any(c => !Alphabet.Contains(c)))
                throw new InvalidDataException("Stored TTY key is invalid.");
            return key;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    internal bool Matches(string candidate)
    {
        if (!Exists || candidate.Length != Size) return false;
        byte[] expected = Encoding.UTF8.GetBytes(ReadForCopy());
        byte[] actual = Encoding.UTF8.GetBytes(candidate);
        try { return CryptographicOperations.FixedTimeEquals(expected, actual); }
        finally
        {
            CryptographicOperations.ZeroMemory(expected);
            CryptographicOperations.ZeroMemory(actual);
        }
    }
}
