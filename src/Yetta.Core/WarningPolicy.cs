namespace Yetta.Core;

public enum YettaWarningLevel
{
    Information, Consent, Caution, Dangerous, Critical, Fatal, Dax
}

public enum WarningObjectKind
{
    Unknown, Application, Photo, Folder, ProtectedFile, Archive,
    Document, WindowsSettingsFile, Executable, PythonScript, ImportantFile
}

/// <summary>
/// The UI never grants Windows privileges. High severity is a user-facing
/// classification, not a Defender verdict or proof of compromise.
/// </summary>
public static class WarningPolicy
{
    public static int HeaderIcon(YettaWarningLevel level) => level switch
    {
        YettaWarningLevel.Consent => 2,
        YettaWarningLevel.Caution => 23,
        YettaWarningLevel.Dangerous => 24,
        YettaWarningLevel.Critical => 25,
        YettaWarningLevel.Fatal => 48,
        YettaWarningLevel.Dax => 33,
        _ => 22
    };

    public static int ObjectIcon(WarningObjectKind kind) => kind switch
    {
        WarningObjectKind.Application => 45,
        WarningObjectKind.Photo => 47,
        WarningObjectKind.Folder => 43,
        WarningObjectKind.ProtectedFile => 40,
        WarningObjectKind.Archive => 44,
        WarningObjectKind.Document => 39,
        WarningObjectKind.WindowsSettingsFile => 42,
        WarningObjectKind.Executable => 41,
        WarningObjectKind.PythonScript => 37,
        WarningObjectKind.ImportantFile => 38,
        _ => 46
    };

    public static int StatusIcon(YettaWarningLevel level, bool safeMode,
        string profile = "User")
    {
        if (level == YettaWarningLevel.Fatal) return 0;
        if (safeMode || profile is "Guest" or "SafeUser" ||
            level is YettaWarningLevel.Consent or YettaWarningLevel.Dax)
            return 35;
        return level is YettaWarningLevel.Information ? 27 : 26;
    }

    public static string Heading(YettaWarningLevel level) => level switch
    {
        YettaWarningLevel.Information => "Стандартное предупреждение",
        YettaWarningLevel.Consent => "Соглашение",
        YettaWarningLevel.Caution => "Предупреждение",
        YettaWarningLevel.Dangerous => "Опасное действие",
        YettaWarningLevel.Critical => "Критическое предупреждение",
        YettaWarningLevel.Fatal => "Смертельная ошибка",
        _ => "Сообщение DAX"
    };

    public static bool RequiresAdditionalAcknowledgement(YettaWarningLevel level) =>
        level is YettaWarningLevel.Consent or YettaWarningLevel.Caution or
            YettaWarningLevel.Dangerous or YettaWarningLevel.Critical;

    public static bool RequiresTty(YettaWarningLevel level, string profile) =>
        level == YettaWarningLevel.Critical || profile == "System";

    public static bool RequiresAllowDeny(YettaWarningLevel level) =>
        level is YettaWarningLevel.Information or YettaWarningLevel.Caution or
            YettaWarningLevel.Dangerous or YettaWarningLevel.Critical;

    public static bool CanApprove(bool checkedAgreement, bool validKey,
        bool needsAgreement, bool needsKey) =>
        (!needsAgreement || checkedAgreement) && (!needsKey || validKey);
}
