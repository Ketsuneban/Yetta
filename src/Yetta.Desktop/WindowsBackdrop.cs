using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Yetta.Desktop;

/// <summary>
/// Native rounded corners and frame attributes for an opaque WPF window.
/// </summary>
internal static class WindowsBackdrop
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute,
        ref int value, int size);

    internal static bool Configure(Window window)
    {
        if (!OperatingSystem.IsWindows()) return false;
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return false;
        try
        {
            var foreground = new Windows.UI.ViewManagement.UISettings().GetColorValue(
                Windows.UI.ViewManagement.UIColorType.Foreground);
            int dark = foreground.R + foreground.G + foreground.B > 384 ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            // DWMWA_BORDER_COLOR = 34; DWMWA_COLOR_NONE = 0xFFFFFFFE.
            // Windows 11 draws a native hairline around otherwise borderless
            // WindowChrome windows. Turn off that decorative DWM stroke.
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            {
                int noBorder = unchecked((int)0xFFFFFFFE);
                _ = DwmSetWindowAttribute(hwnd, 34, ref noBorder, sizeof(int));
            }
            int corners = 2;
            _ = DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
            // The old full-client glass frame introduced hairlines along panels
            // at fractional display scales. Keep the native corners, but draw
            // the client area as one opaque WPF surface.
            int noBackdrop = 1;
            _ = DwmSetWindowAttribute(hwnd, 38, ref noBackdrop, sizeof(int));
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
