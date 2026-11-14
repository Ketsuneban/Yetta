using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Yetta.Desktop;

/// <summary>
/// Windows 11 22H2+ Desktop Acrylic backed by DWM; transparent WPF fallback on
/// earlier Windows builds or when accessibility high-contrast is enabled.
/// A layered AllowsTransparency window cannot use this native DWM backdrop.
/// </summary>
internal static class NativeBackdrop
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmExtendFrameIntoClientArea(
        IntPtr hwnd, ref Margins margins);

    public static bool Supported =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) &&
        !SystemParameters.HighContrast;

    public static void Remove(Window window)
    {
        if (!Supported || window.AllowsTransparency) return;
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int material = 1; // DWMSBT_NONE
            _ = DwmSetWindowAttribute(hwnd, 38, ref material, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    public static bool TryApply(Window window, bool acrylic)
    {
        if (!Supported || window.AllowsTransparency) return false;
        try
        {
            IntPtr hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return false;
            int dark = 1;
            _ = DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
            int corners = 2; // rounded corners, when supported
            _ = DwmSetWindowAttribute(hwnd, 33, ref corners, sizeof(int));
            var fullClient = new Margins
                { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            _ = DwmExtendFrameIntoClientArea(hwnd, ref fullClient);
            int material = acrylic ? 3 : 2; // Desktop Acrylic or Mica
            bool applied = DwmSetWindowAttribute(hwnd, 38, ref material, sizeof(int)) >= 0;
            if (HwndSource.FromHwnd(hwnd) is HwndSource source)
                source.CompositionTarget.BackgroundColor = Colors.Transparent;
            return applied;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (System.ComponentModel.Win32Exception) { return false; }
    }
}
