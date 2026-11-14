using System.Windows;
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
