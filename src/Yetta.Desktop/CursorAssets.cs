using System.Windows;
using System.Windows.Input;

namespace Yetta.Desktop;

/// <summary>Native Windows cursor shapes supplied as icons 17–20 by the owner.</summary>
internal static class CursorAssets
{
    private static Cursor Load(int id, Cursor fallback)
    {
        try
        {
            var uri = new Uri($"pack://application:,,,/Assets/Cursors/{id}.cur");
            var resource = Application.GetResourceStream(uri);
            if (resource is null) return fallback;
            using var source = resource.Stream;
            return new Cursor(source);
        }
        catch (IOException) { return fallback; }
        catch (ArgumentException) { return fallback; }
    }

    internal static Cursor Pointer { get; } = Load(17, Cursors.Arrow);
    internal static Cursor Move { get; } = Load(18, Cursors.SizeAll);
    internal static Cursor Text { get; } = Load(19, Cursors.IBeam);
    internal static Cursor VerticalSize { get; } = Load(20, Cursors.SizeNS);
}
