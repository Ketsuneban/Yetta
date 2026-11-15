using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Yetta.Desktop;

/// <summary>
/// Applies a translucent acrylic backdrop so warning windows read as frosted
/// glass, matching the Windows 11 look. This is cosmetic only: it never changes
/// what a window can do, and it silently degrades to a solid surface when the
/// compositor or the OS build does not support it.
/// </summary>
internal static class WarningGlass
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmsbtTransientWindow = 3; // Acrylic
    private const int WcaAccentPolicy = 19;

    private enum AccentState
    {
        Disabled = 0,
        Gradient = 1,
        TransparentGradient = 2,
        BlurBehind = 3,
        AcrylicBlurBehind = 4,
        HostBackdrop = 5
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    /// <summary>Returns true when a real backdrop was applied and the caller
    /// should leave the window background transparent.</summary>
    internal static bool Apply(Window window, Color tint)
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return false;
            int dark = 1;
            _ = DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
            if (Environment.OSVersion.Version.Build >= 22621)
            {
                int backdrop = DwmsbtTransientWindow;
                if (DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int)) == 0)
                    return true;
            }
            return ApplyAccent(handle, tint);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException
            or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool ApplyAccent(IntPtr handle, Color tint)
    {
        var policy = new AccentPolicy
        {
            AccentState = AccentState.AcrylicBlurBehind,
            AccentFlags = 2,
            GradientColor = ToAbgr(tint, 0xC0)
        };
        int size = Marshal.SizeOf<AccentPolicy>();
        IntPtr block = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(policy, block, false);
            var data = new WindowCompositionAttributeData
            {
                Attribute = WcaAccentPolicy,
                Data = block,
                SizeOfData = size
            };
            return SetWindowCompositionAttribute(handle, ref data) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }
    }

    private static uint ToAbgr(Color color, byte alpha) =>
        ((uint)alpha << 24) | ((uint)color.B << 16) | ((uint)color.G << 8) | color.R;
}
