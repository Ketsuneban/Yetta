using System.Runtime.InteropServices;
using System.Windows;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>One shared warning system for ALL Yetta-owned consent popups.</summary>
public static class ConsentDialog
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static bool InWindowsSafeMode =>
        OperatingSystem.IsWindows() && GetSystemMetrics(67) != 0;

    public static bool Confirm(Window owner, string title, string description,
        YettaWarningLevel level = YettaWarningLevel.Consent)
    {
        // Consent always requires a separate deliberate checkbox;
        // unlike TTY it never alters operating-system access rights.
        return WarningDialog.Show(owner, new WarningRequest(
            level, title, description, ObjectName: "Yetta",
            NeedsAgreement: true));
    }
}
