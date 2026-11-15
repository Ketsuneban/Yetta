using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yetta.Desktop;

/// <summary>
/// Owner-provided 19 warning graphics. Source dimensions are 96 or 384px,
/// stored in a losslessly positioned 5x4 sprite grid (384px cell). Cropping
/// uses the source image's TRUE bounds so no empty 384px margin shrinks a
/// 96px icon. The legacy 32px UI atlas is never used for these objects.
/// </summary>
internal static class WarningArtwork
{
    private const int Cell = 384;
    private static readonly int[] NativeWidths =
        [96, 96, 96, 384, 96, 384, 384, 96, 384, 384,
         96, 96, 96, 384, 384, 96, 96, 96, 96];
    private static readonly Dictionary<int, BitmapSource> Cache = new();
    private static BitmapSource? _atlas;

    private static BitmapSource Source(int number)
    {
        if (number is < 1 or > 19) number = 14;
        if (Cache.TryGetValue(number, out BitmapSource? cached)) return cached;
        _atlas ??= LoadAtlas();
        int side = NativeWidths[number - 1];
        int cellX = ((number - 1) % 5) * Cell;
        int cellY = ((number - 1) / 5) * Cell;
        int margin = (Cell - side) / 2;
        var crop = new CroppedBitmap(_atlas,
            new Int32Rect(cellX + margin, cellY + margin, side, side));
        crop.Freeze();
        Cache[number] = crop;
        return crop;
    }

    private static BitmapSource LoadAtlas()
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri("pack://application:,,,/Assets/yetta-warning-artwork-19.png");
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    internal static Image Image(int number, double size) => MakeImage(Source(number), size);

    private static Image MakeImage(ImageSource src, double size)
    {
        var image = new Image
        {
            Source = src,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        return image;
    }

    internal static FrameworkElement Cause(WarningRequest request, double size)
    {
        var kind = YettaWarningWindow.ResolveObjectKind(request);
        if (kind == WarningObjectKind.Dax)
            return MakeImage(new BitmapImage(new Uri(
                "pack://application:,,,/Assets/dax-logo.png")), size);

        // A real local file can supply its own Windows icon (not merely the
        // generic extension illustration). Never attempt extraction for remote
        // resources, unknown paths, or the fatal-event skull.
        if (request.ObjectKind == WarningObjectKind.Auto &&
            kind != WarningObjectKind.FatalSkull &&
            request.SourcePath is { Length: > 0 } path &&
            File.Exists(path) &&
            TryFileIcon(path, out BitmapSource? found))
            return MakeImage(found, size);

        int number = kind switch
        {
            WarningObjectKind.Windows => 1,
            WarningObjectKind.Python => 2,
            WarningObjectKind.ImportantFile => 3,
            WarningObjectKind.TextFile => 4,
            WarningObjectKind.ProtectedFile => 6,
            WarningObjectKind.Executable => 7,
            WarningObjectKind.SystemFile => 8,
            WarningObjectKind.CloudFile => 9,
            WarningObjectKind.Folder => 10,
            WarningObjectKind.Archive => 11,
            WarningObjectKind.Application => 12,
            WarningObjectKind.FatalSkull => 13,
            WarningObjectKind.MediaFile => 15,
            WarningObjectKind.WindowsServer => 19,
            _ => 14
        };
        return Image(number, size);
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
    private static extern IntPtr SHGetFileInfo(string path, uint fileAttributes,
        ref ShellFileInfo info, uint cbInfo, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    private static bool TryFileIcon(string path, out BitmapSource? bitmap)
    {
        bitmap = null;
        if (!OperatingSystem.IsWindows()) return false;
        var info = new ShellFileInfo();
        const uint ShgfiIcon = 0x100;
        try
        {
            if (SHGetFileInfo(path, 0, ref info,
                (uint)Marshal.SizeOf<ShellFileInfo>(), ShgfiIcon) == IntPtr.Zero ||
                info.Icon == IntPtr.Zero)
                return false;
            try
            {
                var src = Imaging.CreateBitmapSourceFromHIcon(info.Icon,
                    Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                bitmap = src;
                return true;
            }
            finally { DestroyIcon(info.Icon); }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException
            or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
