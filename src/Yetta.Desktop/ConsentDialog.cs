using System.Runtime.InteropServices;
using System.Windows;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>Shared explicit consent entry point for Yetta actions.</summary>
public static class ConsentDialog
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    public static bool InWindowsSafeMode =>
        OperatingSystem.IsWindows() && GetSystemMetrics(67) != 0;

    public static bool Confirm(Window owner, string title, string description,
        YettaWarningLevel level = YettaWarningLevel.Consent)
    {
        var type = level switch
        {
            YettaWarningLevel.Caution => WarningKind.Caution,
            YettaWarningLevel.Dangerous => WarningKind.Dangerous,
            YettaWarningLevel.Critical => WarningKind.Critical,
            YettaWarningLevel.Fatal => WarningKind.Fatal,
            YettaWarningLevel.Information => WarningKind.Dax,
            _ => WarningKind.Agreement
        };
        return YettaWarningWindow.Show(owner, new WarningRequest(
            type, title, description,
            Subject: level == YettaWarningLevel.Information ? "DAX" : "Yetta",
            RequireAcknowledgement: WarningPolicy.RequiresAdditionalAcknowledgement(level),
            RequireTty: level == YettaWarningLevel.Critical,
            PotentialDamage: level is YettaWarningLevel.Dangerous or YettaWarningLevel.Critical));
    }
}
