using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yetta.Desktop;

/// <summary>
/// Original owner's 1–50 Icons8 atlas, 10 columns x 5 rows.
/// Each asset cell is 24x24 pixels; source PNG is bundled with the app.
/// The exact icon IDs are kept stable across the UI.
/// </summary>
internal static class IconAtlas
{
    private const int Cell = 24;
    private static readonly Lazy<BitmapSource> Atlas = new(() =>
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri("pack://application:,,,/Assets/icons-atlas.png");
        image.EndInit();
        image.Freeze();
        return image;
    });

    internal static ImageSource Source(int id)
    {
        if (id is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(id));
        int x = (id - 1) % 10 * Cell;
        int y = (id - 1) / 10 * Cell;
        var cropped = new CroppedBitmap(Atlas.Value, new Int32Rect(x, y, Cell, Cell));
        cropped.Freeze();
        return cropped;
    }

    internal static Image Image(int id, double size = 24) => new()
    {
        Source = Source(id),
        Width = size,
        Height = size,
        Stretch = Stretch.Uniform,
        SnapsToDevicePixels = true
    };
}
