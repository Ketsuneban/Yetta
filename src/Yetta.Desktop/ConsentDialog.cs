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
            // The warning category alone is not evidence of a DAX risk result.
            PotentialDamage: false,
            HeaderKind: level == YettaWarningLevel.Information
                ? WarningHeaderKind.Information : WarningHeaderKind.Generic,
            ObjectKind: level == YettaWarningLevel.Information
                ? WarningObjectKind.Dax : WarningObjectKind.Application,
            InformationOnly: level == YettaWarningLevel.Information));
    }
}
