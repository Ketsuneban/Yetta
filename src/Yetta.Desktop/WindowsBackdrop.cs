using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Yetta.Desktop;

/// <summary>
/// Opt-in native backdrop for a regular non-layered WPF window. Windows 11 can
/// provide real DWM Acrylic; older Windows gracefully keep opaque Fluent.
/// Never enables WPF AllowsTransparency on the main HWND (incompatible with DWM).
/// </summary>
internal static class WindowsBackdrop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins { public int Left, Right, Top, Bottom; }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute,
        ref int value, int size);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

    internal static bool Configure(Window window, bool glass)
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
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621))
                return false;
            int backdrop = SystemParameters.HighContrast ? 1 : glass ? 3 : 2;
            int result = DwmSetWindowAttribute(hwnd, 38, ref backdrop, sizeof(int));
            if (result < 0) return false;
            if (backdrop is 2 or 3)
            {
                var fullFrame = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                return DwmExtendFrameIntoClientArea(hwnd, ref fullFrame) >= 0;
            }
            return false;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
    }
}
