using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Yetta.Desktop;

/// <summary>Native Windows 11 frame styling for ordinary Yetta dialogs.</summary>
internal static class WarningGlass
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    internal static bool Apply(Window window)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;
            int dark = 1;
            _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
            int round = 2;
            _ = DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
            int noBorder = unchecked((int)0xFFFFFFFE);
            _ = DwmSetWindowAttribute(handle, 34, ref noBorder, sizeof(int));
            // A solid dialog surface keeps text readable and never dims the desktop.
            int noBackdrop = 1;
            _ = DwmSetWindowAttribute(handle, 38, ref noBackdrop, sizeof(int));
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException
            or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }
}
