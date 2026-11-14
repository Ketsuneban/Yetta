using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yetta.Desktop;

/// <summary>50 original icons packaged in one numbered PNG atlas, 10 × 5, 32px cells.</summary>
internal static class IconAssets
{
    private const int Cell = 32;
    private static BitmapSource? _atlas;
    private static readonly Dictionary<int, BitmapSource> Cache = [];

    private static BitmapSource? Get(int number)
    {
        if (number < 1 || number > 50) return null;
        if (Cache.TryGetValue(number, out var existing)) return existing;
        try
        {
            _atlas ??= new BitmapImage(new Uri("pack://application:,,,/Assets/yetta-icons-atlas.png"));
            var bounds = new Int32Rect(((number - 1) % 10) * Cell, ((number - 1) / 10) * Cell, Cell, Cell);
            var cropped = new CroppedBitmap(_atlas, bounds);
            cropped.Freeze();
            Cache[number] = cropped;
            return cropped;
        }
        catch (IOException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    internal static Image Image(int number, double size)
    {
        BitmapSource? image = Get(number);
        if (image is null)
        {
            try
            {
                image = new BitmapImage(new Uri("pack://application:,,,/Assets/" +
                    (number is 26 or 27 or 31 or 35 ? number.ToString() : "31") + ".png"));
            }
            catch (IOException) { }
        }
        return new Image
        {
            Source = image,
            Width = size, Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
    }

    internal static void SetNav(Button button, int number, string text)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(Image(number, 22));
        stack.Children.Add(new TextBlock
        {
            Text = text, Margin = new Thickness(13, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.Medium,
        });
        button.Content = stack;
    }

    internal static Cursor? CursorFrom(int number, int hotX, int hotY)
    {
        var source = Get(number);
        if (source is null) return null;
        // CUR directory with one embedded PNG. Source is a 32px transparent icon.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var png = new MemoryStream();
        encoder.Save(png);
        byte[] bytes = png.ToArray();
        using var cur = new MemoryStream();
        using (var writer = new BinaryWriter(cur, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1);
            writer.Write((byte)Cell); writer.Write((byte)Cell);
            writer.Write((byte)0); writer.Write((byte)0);
            writer.Write((ushort)hotX); writer.Write((ushort)hotY);
            writer.Write((uint)bytes.Length); writer.Write((uint)22);
            writer.Write(bytes);
        }
        cur.Position = 0;
        try { return new Cursor(cur); }
        catch (ArgumentException) { return null; }
        catch (IOException) { return null; }
    }
}
