using System.IO;
using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private string ShowWarningTest(string command)
    {
        if (!WarningTestCommand.TryParse(command, out var test) || test is null)
            return WarningTestCommand.Usage;
        var kind = Enum.Parse<WarningKind>(test.Kind);
        string cause = test.Cause;
        string? path = null;
        var type = cause.ToUpperInvariant() switch
        {
            "YETTA" => WarningCauseType.Yetta, "DAX" => WarningCauseType.Dax,
            "TALLOW" => WarningCauseType.Tallow, "WINDOWS" => WarningCauseType.Windows,
            _ => WarningCauseType.File
        };
        if (type == WarningCauseType.File)
        {
            if (cause.StartsWith("app:", StringComparison.OrdinalIgnoreCase))
            {
                type = WarningCauseType.Application;
                cause = cause[4..];
            }
            if (string.IsNullOrWhiteSpace(cause)) return WarningTestCommand.Usage;
            path = Uri.TryCreate(cause, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? cause : ResolveMarvinePath(cause);
        }
        _ = YettaWarningWindow.Show(this, new WarningRequest(kind, "Тест предупреждения · " + kind,
            test.Text, Subject: path is null ? cause : Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), SourcePath: path,
            RequireAcknowledgement: test.Check, RequireTty: test.Tty,
            PotentialDamage: kind is WarningKind.Dangerous or WarningKind.Critical,
            CauseType: type, IsPreview: true, RequirePassword: test.Password),
            _settings.ActiveProfile, nonModal: true);
        return "Тест передан в очередь предупреждений. Действия с причиной не выполняются.";
    }
}
