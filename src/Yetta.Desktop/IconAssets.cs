using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Yetta.Desktop;

/// <summary>
/// Numbered icons 1–50 ship as separate PNGs so every surface can use the
/// resolution it actually needs: small badges stay on their 32px master while
/// large artwork keeps its 384px original. The Yetta and DAX marks live next
/// to them as <c>icons/yetta.png</c> and <c>icons/dax.png</c>.
/// </summary>
internal static class IconAssets
{
    private static readonly Dictionary<int, BitmapSource?> Cache = [];

    private static BitmapSource? Get(int number)
    {
        if (number < 1 || number > 50) return null;
        if (Cache.TryGetValue(number, out var existing)) return existing;
        var source = Load("icons/" + number + ".png");
        Cache[number] = source;
        return source;
    }

    private static BitmapSource? Load(string asset)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri("pack://application:,,,/Assets/" + asset);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (IOException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static Image Render(BitmapSource? source, double size)
    {
        var control = new Image
        {
            Source = source,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(control, BitmapScalingMode.HighQuality);
        return control;
    }

    internal static Image Image(int number, double size) => Render(Get(number), size);

    internal static Image Asset(string asset, double size) => Render(Load(asset), size);

    internal static Image YettaLogo(double size) => Asset("icons/yetta.png", size);

    internal static Image DaxLogo(double size) => Asset("icons/dax.png", size);

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

    internal static void SetNavLogo(Button button, string asset, string text)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(Asset(asset, 23));
        stack.Children.Add(new TextBlock
        {
            Text = text, Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontWeight = FontWeights.Medium
        });
        button.Content = stack;
    }

    internal static Cursor? CursorFrom(int number, int hotX, int hotY)
    {
        var source = Get(number);
        if (source is null) return null;
        // CUR directory with one embedded PNG. Source is a transparent icon.
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var png = new MemoryStream();
        encoder.Save(png);
        byte[] bytes = png.ToArray();
        using var cur = new MemoryStream();
        using (var writer = new BinaryWriter(cur, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write((ushort)0); writer.Write((ushort)2); writer.Write((ushort)1);
            writer.Write((byte)(source.PixelWidth >= 256 ? 0 : source.PixelWidth));
            writer.Write((byte)(source.PixelHeight >= 256 ? 0 : source.PixelHeight));
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
