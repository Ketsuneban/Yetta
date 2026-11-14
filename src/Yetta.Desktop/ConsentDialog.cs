using System.Runtime.InteropServices;
using System.Windows;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>
/// Compatibility entrypoint for existing in-app confirmations. All consent
/// windows use the same seven-category DAX visual language and real checkbox.
/// </summary>
public static class ConsentDialog
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    public static bool InWindowsSafeMode =>
        OperatingSystem.IsWindows() && GetSystemMetrics(67) != 0;

    public static bool Confirm(Window owner, string title, string description,
        YettaWarningLevel level = YettaWarningLevel.Consent)
    {
        var request = new DaxWarningRequest(
            level,
            title,
            description,
            SubjectName: level == YettaWarningLevel.Dax ? "DAX" : "Yetta",
            SubjectIconId: level == YettaWarningLevel.Dax ? 29 : 2,
            CheckRequired: WarningPolicy.RequiresAdditionalAcknowledgement(level));
        var result = DaxWarningDialog.Show(
            owner, request, new TtyKeyStore(),
            activeProfile: "User", safeMode: InWindowsSafeMode);
        return result is DaxWarningResult.Allowed or DaxWarningResult.Acknowledged;
    }
}
