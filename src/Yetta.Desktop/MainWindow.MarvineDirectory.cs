using System.IO;
using System.Windows.Controls;

namespace Yetta.Desktop;

public partial class MainWindow
{
    private string? _marvineDirectory;
    private TextBlock? _marvineDirectoryLabel;
    private string MarvineDirectory => _marvineDirectory ?? Environment.CurrentDirectory;

    private string ResolveMarvinePath(string value)
    {
        string path = Environment.ExpandEnvironmentVariables(value.Trim().Trim('"'));
        if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal))
            path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
        return Path.GetFullPath(path, MarvineDirectory);
    }

    private string OpenMarvineDirectory(string path)
    {
        string directory = ResolveMarvinePath(path);
        if (!Directory.Exists(directory)) return $"[✖] Папка не найдена или недоступна: {directory}";
        _marvineDirectory = directory;
        if (_marvineDirectoryLabel is { } label)
        {
            label.Text = directory;
            label.ToolTip = directory;
        }
        return "[✔] Рабочая папка Marvine: " + directory;
    }
}
