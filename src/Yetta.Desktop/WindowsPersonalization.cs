using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Yetta.Desktop;

internal static class WindowsPersonalization
{
    // SPI_SETMOUSESPEED and SPI_SETCLIENTAREAANIMATION take the value in pvParam.
    private static (uint Read, uint Write, bool InPointer) Action(string key) => key switch
    {
        "mouse-speed" => (0x70, 0x71, true), "key-speed" => (0x0A, 0x0B, false),
        "key-delay" => (0x16, 0x17, false), "animations" => (0x1042, 0x1043, true),
        "drag-content" => (0x26, 0x25, false), _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool ReadParameter(uint action, uint param, out int value, uint flags);
    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true)]
    private static extern bool WriteParameter(uint action, uint param, nint value, uint flags);

    internal static int Read(string key) => ReadParameter(Action(key).Read, 0, out int value, 0)
        ? value : throw new Win32Exception(Marshal.GetLastWin32Error());

    internal static void Write(string key, int value)
    {
        var action = Action(key);
        const uint persistAndBroadcast = 3;
        if (!WriteParameter(action.Write, action.InPointer ? 0 : (uint)value, action.InPointer ? value : 0, persistAndBroadcast))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [ComImport, Guid("B92B56A9-8B55-4E14-9A89-0199BBB6F93B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDesktopWallpaper
    {
        void SetWallpaper([MarshalAs(UnmanagedType.LPWStr)] string? monitor, [MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    internal static void SetWallpaper(string file)
    {
        var type = Type.GetTypeFromCLSID(new Guid("C2CF3110-460E-4FC1-B9D0-8A1C0C9CC4BD"), throwOnError: true)!;
        var desktop = (IDesktopWallpaper)Activator.CreateInstance(type)!;
        try { desktop.SetWallpaper(null, file); }
        finally { Marshal.ReleaseComObject(desktop); }
    }
}
