using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Yetta.Desktop;

/// <summary>
/// Best-effort Windows 11 Mica material for the main non-layered WPF HWND.
/// Only use with a full DWM glass frame; layered warning windows use their
/// own high-contrast blur/frost fallback instead.
/// </summary>
internal static class WindowsBackdrop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
        internal static Margins Full => new() { Left = -1, Right = -1, Top = -1, Bottom = -1 };
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute,
        ref int value, int size);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr window, ref Margins margins);

    internal static bool Apply(Window window, bool enabled)
    {
        if (!OperatingSystem.IsWindows() ||
            !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) ||
            SystemParameters.HighContrast) return false;
        if (window.AllowsTransparency) return false;
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            int backgroundType = enabled ? 2 : 1; // Mica or none
            if (DwmSetWindowAttribute(hwnd, 38, ref backgroundType, sizeof(int)) < 0)
                return false;
            if (!enabled) return true;
            var margins = Margins.Full;
            if (DwmExtendFrameIntoClientArea(hwnd, ref margins) < 0)
                return false;
            var source = HwndSource.FromHwnd(hwnd);
            if (source?.CompositionTarget is not null)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            return true;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
