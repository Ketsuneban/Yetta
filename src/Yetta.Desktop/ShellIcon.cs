using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Yetta.Desktop;

/// <summary>
/// Reads the real shell icon of a file or folder so a warning can show the
/// actual object instead of a generic placeholder. Every failure path returns
/// null and the caller falls back to a numbered icon.
/// </summary>
internal static class ShellIcon
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiLargeIcon = 0x000000000;

    // SHFILEINFO is read through a raw buffer: only hIcon (the first pointer)
    // is consumed, so the trailing fixed-size name fields never need a struct.
    private static int BufferSize => IntPtr.Size + 8 + (260 + 80) * 2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, IntPtr psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    internal static BitmapSource? Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        IntPtr buffer = IntPtr.Zero;
        IntPtr icon = IntPtr.Zero;
        try
        {
            int size = BufferSize;
            buffer = Marshal.AllocHGlobal(size);
            if (SHGetFileInfo(path, 0, buffer, (uint)size, ShgfiIcon | ShgfiLargeIcon) == IntPtr.Zero) return null;
            icon = Marshal.ReadIntPtr(buffer);
            if (icon == IntPtr.Zero) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException
            or ArgumentException or COMException or InvalidOperationException)
        {
            return null;
        }
        finally
        {
            if (icon != IntPtr.Zero) DestroyIcon(icon);
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
        }
    }
}
