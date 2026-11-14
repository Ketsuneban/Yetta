using System.Windows;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yetta.Desktop;

/// <summary>
/// The owner-supplied 1..50 icon set is packed as a 10-column 48px atlas.
/// All icon references are identical to the numbered PNGs in the archive.
/// </summary>
internal static class IconLibrary
{
    private static readonly Lazy<BitmapImage> Atlas = new(() =>
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri("pack://application:,,,/Assets/yetta-icons-atlas.png");
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    });

    public static Image Create(int id, double size = 28)
    {
        id = Math.Clamp(id, 1, 50);
        int index = id - 1;
        var frame = new CroppedBitmap(Atlas.Value,
            new Int32Rect((index % 10) * 48, (index / 10) * 48, 48, 48));
        frame.Freeze();
        return new Image
        {
            Source = frame,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
    }


    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellFileInfo
    {
        public IntPtr Icon;
        public int IconIndex;
        public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string path, uint fileAttributes, ref ShellFileInfo info,
        uint size, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    public static Image CreateSubject(int fallbackId, string? path, double size)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) ||
            !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
            return Create(fallbackId, size);
        var info = new ShellFileInfo();
        try
        {
            // SHGFI_ICON | SHGFI_LARGEICON, scoped to this user-selected file.
            var result = SHGetFileInfo(path, 0, ref info,
                (uint)Marshal.SizeOf<ShellFileInfo>(), 0x100);
            if (result == IntPtr.Zero || info.Icon == IntPtr.Zero)
                return Create(fallbackId, size);
            try
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(info.Icon,
                    Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return new Image { Source = bitmap, Width = size, Height = size,
                    Stretch = Stretch.Uniform, SnapsToDevicePixels = true };
            }
            finally { DestroyIcon(info.Icon); }
        }
        catch (Exception ex) when (ex is DllNotFoundException or
            EntryPointNotFoundException or System.ComponentModel.Win32Exception)
        {
            return Create(fallbackId, size);
        }
    }

    public static int NavigationIcon(string page) => page switch
    {
        "overview" => 7, "info" => 2, "console" => 1, "guard" => 6,
        "process" => 41,
        "logs" => 8, "activity" => 31, "recovery" => 9,
        "settings" => 3, "sources" => 30, _ => 30
    };

    /// <summary>The four supplied cursor images are arrow, move, text and scroll.</summary>
    public static System.Windows.Input.Cursor CursorForState(int iconId) => iconId switch
    {
        18 => System.Windows.Input.Cursors.SizeAll,
        19 => System.Windows.Input.Cursors.IBeam,
        20 => System.Windows.Input.Cursors.ScrollAll,
        _ => System.Windows.Input.Cursors.Arrow
    };
}
