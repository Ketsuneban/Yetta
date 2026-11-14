using System.Windows;
using Yetta.Core;

namespace Yetta.Desktop;

/// <summary>Replaces native MessageBox surfaces with the user-defined Yetta alerts.</summary>
internal static class YettaMessageBox
{
    internal static MessageBoxResult Show(Window owner, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        bool choice = buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel;
        var level = image switch
        {
            MessageBoxImage.Error => YettaWarningLevel.Dangerous,
            MessageBoxImage.Warning => YettaWarningLevel.Caution,
            MessageBoxImage.Question => YettaWarningLevel.Standard,
            _ => YettaWarningLevel.Dax
        };
        var request = new DaxWarningRequest(level, title, message,
            SubjectName: "Yetta", SubjectIconId: 2,
            AcknowledgmentOnly: !choice);
        var result = DaxWarningDialog.Show(owner, request,
            activeProfile: "User", safeMode: ConsentDialog.InWindowsSafeMode);
        return choice
            ? result == DaxWarningResult.Allowed
                ? MessageBoxResult.Yes : MessageBoxResult.No
            : MessageBoxResult.OK;
    }
}
