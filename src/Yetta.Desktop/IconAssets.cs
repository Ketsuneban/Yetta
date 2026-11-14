using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

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
        var control = new Image
        {
            Source = image,
            Width = size, Height = size,
            Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(control, BitmapScalingMode.HighQuality);
        return control;
    }

    /// <summary>
    /// DPI-independent recreation of tiny notification badges. The supplied PNGs
    /// were only 32x32; these retain their circle/shield/monitor semantics without
    /// magnifying the original pixel grid.
    /// </summary>
    internal static FrameworkElement WarningGlyph(int number, double size)
    {
        if (number is not (22 or 23 or 26 or 27 or 35))
            return Image(number, size);

        var canvas = new Canvas { Width = 64, Height = 64 };
        static Brush Fill(byte r, byte g, byte b) => new SolidColorBrush(Color.FromRgb(r, g, b));
        var bg = number switch
        {
            26 => (Fill(247, 151, 147), Fill(201, 48, 48)),
            27 => (Fill(160, 237, 181), Fill(37, 143, 67)),
            35 => (Fill(150, 201, 255), Fill(53, 108, 198)),
            _ => (Fill(255, 213, 57), Fill(180, 129, 13))
        };
        var gradient = new LinearGradientBrush(((SolidColorBrush)bg.Item1).Color, ((SolidColorBrush)bg.Item2).Color, 90);
        if (number is 22 or 23)
        {
            Shape outline;
            if (number == 22)
                outline = new Ellipse { Width = 52, Height = 52 };
            else
                outline = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(26, 0), new Point(52, 11), new Point(52, 42),
                        new Point(26, 55), new Point(0, 42), new Point(0, 11)
                    }
                };
            outline.Fill = gradient;
            outline.Stroke = Fill(184, 129, 13);
            outline.StrokeThickness = 1.6;
            Canvas.SetLeft(outline, 6); Canvas.SetTop(outline, 4);
            canvas.Children.Add(outline);
            var bang = new TextBlock
            {
                Text = "!", FontSize = 39, FontWeight = FontWeights.Bold,
                Foreground = Brushes.White, Width = 42, TextAlignment = TextAlignment.Center,
                FontFamily = new FontFamily("Segoe UI")
            };
            Canvas.SetLeft(bang, 11); Canvas.SetTop(bang, 7);
            canvas.Children.Add(bang);
        }
        else
        {
            var frame = new Rectangle
            {
                Width = 56, Height = 45,
                Fill = gradient, Stroke = bg.Item2, StrokeThickness = 2.5
            };
            Canvas.SetLeft(frame, 4); Canvas.SetTop(frame, 6);
            canvas.Children.Add(frame);
            var stand = new Rectangle { Width = 21, Height = 5, Fill = bg.Item2 };
            Canvas.SetLeft(stand, 21); Canvas.SetTop(stand, 51);
            canvas.Children.Add(stand);
            var foot = new Rectangle { Width = 32, Height = 4, Fill = bg.Item2 };
            Canvas.SetLeft(foot, 16); Canvas.SetTop(foot, 56);
            canvas.Children.Add(foot);
            if (number == 35)
            {
                var shield = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M32,14 L47,20 L46,34 Q43,43 32,47 Q21,43 18,34 L17,20 Z"),
                    Fill = Fill(38, 112, 203), Stroke = Fill(21, 87, 179), StrokeThickness = 1.5
                };
                canvas.Children.Add(shield);
            }
            else
            {
                var mark = new TextBlock
                {
                    Text = number == 27 ? "✓" : "!", FontSize = 34,
                    FontWeight = FontWeights.Bold, Foreground = number == 27 ? Fill(18, 117, 44) : Fill(177, 33, 36),
                    Width = 46, TextAlignment = TextAlignment.Center,
                    FontFamily = new FontFamily("Segoe UI")
                };
                Canvas.SetLeft(mark, 9); Canvas.SetTop(mark, 7);
                canvas.Children.Add(mark);
            }
        }
        return new Viewbox
        {
            Width = size, Height = size, Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true, Child = canvas
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

    internal static void SetNavLogo(Button button, string asset, string text)
    {
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        var logo = new Image
        {
            Source = new BitmapImage(new Uri("pack://application:,,,/Assets/" + asset)),
            Width = 23, Height = 23, Stretch = Stretch.Uniform,
            SnapsToDevicePixels = true
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        stack.Children.Add(logo);
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
