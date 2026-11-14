using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Yetta.Desktop;

/// <summary>
/// A 64-character acknowledgement token stored only as a per-user generic
/// credential in Windows Credential Manager, which protects it using Windows.
/// This token NEVER grants Administrator or SYSTEM rights.
/// </summary>
internal static class TtyKeyStore
{
    private const string Target = "Yetta/DAX/TTY/v1";
    private const uint Generic = 1;
    private const uint PersistLocalMachine = 2;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeCredential
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite([In] ref NativeCredential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string targetName, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr credential);

    private static string Generate()
    {
        var result = new char[64];
        for (int i = 0; i < result.Length; i++)
            result[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(result);
    }

    internal static string Current()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        if (CredRead(Target, Generic, 0, out IntPtr ptr))
        {
            try
            {
                var record = Marshal.PtrToStructure<NativeCredential>(ptr);
                if (record.CredentialBlobSize is > 0 and <= 512)
                {
                    var bytes = new byte[record.CredentialBlobSize];
                    Marshal.Copy(record.CredentialBlob, bytes, 0, bytes.Length);
                    try
                    {
                        string value = Encoding.Unicode.GetString(bytes);
                        if (value.Length == 64) return value;
                    }
                    finally { CryptographicOperations.ZeroMemory(bytes); }
                }
            }
            finally { CredFree(ptr); }
            throw new InvalidDataException("Неверный формат существующего TTY-ключа.");
        }

        int error = Marshal.GetLastWin32Error();
        if (error != 1168) throw new Win32Exception(error, "Не удалось прочитать Windows Credential Manager.");
        return Rotate();
    }

    internal static string Rotate()
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        string key = Generate();
        byte[] bytes = Encoding.Unicode.GetBytes(key);
        IntPtr ptr = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, ptr, bytes.Length);
            var record = new NativeCredential
            {
                Type = Generic, TargetName = Target, Comment = "Yetta DAX TTY acknowledgement (not OS privileges)",
                CredentialBlob = ptr, CredentialBlobSize = (uint)bytes.Length,
                Persist = PersistLocalMachine, UserName = Environment.UserName
            };
            if (!CredWrite(ref record, 0))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "Не удалось сохранить TTY-ключ в Windows Credential Manager.");
        }
        finally
        {
            // Clear the unmanaged buffer before releasing it.
            Marshal.Copy(new byte[bytes.Length], 0, ptr, bytes.Length);
            Marshal.FreeHGlobal(ptr);
            CryptographicOperations.ZeroMemory(bytes);
        }
        return key;
    }

    internal static bool Matches(string candidate)
    {
        if (candidate.Length != 64) return false;
        string key = Current();
        byte[] lhs = Encoding.UTF8.GetBytes(candidate), rhs = Encoding.UTF8.GetBytes(key);
        try { return CryptographicOperations.FixedTimeEquals(lhs, rhs); }
        finally { CryptographicOperations.ZeroMemory(lhs); CryptographicOperations.ZeroMemory(rhs); }
    }
}
