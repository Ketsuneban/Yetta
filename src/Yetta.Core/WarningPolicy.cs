namespace Yetta.Core;

/// <summary>
/// Seven visual categories. The original Information member remains for
/// compatibility with existing user notifications and smoke tests.
/// </summary>
public enum YettaWarningLevel
{
    Information = 0,
    Consent = 1,
    Caution = 2,
    Dangerous = 3,
    Critical = 4,
    Fatal = 5,
    Standard = 6,
    Dax = 7
}

public enum WarningSubjectType
{
    Application, Photo, Folder, ProtectedFile, Archive, Document,
    WindowsConfiguration, Executable, PythonFile, ImportantFile, Unknown
}

/// <summary>
/// Warning classification is not antivirus detection and does not confer any
/// Windows permissions. A real process token or broker always remains the
/// authority for the operation.
/// </summary>
public static class WarningPolicy
{
    public static bool RequiresTty(YettaWarningLevel level, string activeProfile) =>
        level == YettaWarningLevel.Critical ||
        (activeProfile == "System" && level is not
            (YettaWarningLevel.Dax or YettaWarningLevel.Information or YettaWarningLevel.Fatal));

    public static int HeaderIcon(YettaWarningLevel level, bool accessDenied = false,
        bool daxDenied = false, bool windowsFilesAffected = false) =>
        daxDenied ? 33 : accessDenied ? 28 : level switch
        {
            YettaWarningLevel.Consent => 2,
            YettaWarningLevel.Fatal => 48,
            YettaWarningLevel.Critical => 24,
            YettaWarningLevel.Dangerous => windowsFilesAffected ? 25 : 24,
            YettaWarningLevel.Caution => 23,
            _ => 22
        };

    public static int SubjectIcon(WarningSubjectType subject) => subject switch
    {
        WarningSubjectType.Application => 45,
        WarningSubjectType.Photo => 47,
        WarningSubjectType.Folder => 43,
        WarningSubjectType.ProtectedFile => 40,
        WarningSubjectType.Archive => 44,
        WarningSubjectType.Document => 39,
        WarningSubjectType.WindowsConfiguration => 42,
        WarningSubjectType.Executable => 41,
        WarningSubjectType.PythonFile => 37,
        WarningSubjectType.ImportantFile => 38,
        _ => 46
    };

    public static int StatusIcon(YettaWarningLevel level, bool safeMode, string activeProfile = "User") =>
        safeMode || activeProfile is "Guest" or "SafeUser" ||
        level is YettaWarningLevel.Consent or YettaWarningLevel.Dax
            ? 35
            : level is YettaWarningLevel.Information or YettaWarningLevel.Standard
                ? 27
                : 26;

    public static string Heading(YettaWarningLevel level) => level switch
    {
        YettaWarningLevel.Standard => "Стандартное предупреждение",
        YettaWarningLevel.Information => "Информация",
        YettaWarningLevel.Consent => "Соглашение",
        YettaWarningLevel.Caution => "Предупреждение о безопасности",
        YettaWarningLevel.Dangerous => "Опасное действие",
        YettaWarningLevel.Critical => "Критическое предупреждение",
        YettaWarningLevel.Fatal => "Смертельная ошибка",
        YettaWarningLevel.Dax => "DAX · информация",
        _ => "Yetta"
    };

    public static bool RequiresAdditionalAcknowledgement(YettaWarningLevel level) =>
        level is YettaWarningLevel.Consent or YettaWarningLevel.Caution
            or YettaWarningLevel.Dangerous or YettaWarningLevel.Critical;

    public static bool IsInformational(YettaWarningLevel level) =>
        level is YettaWarningLevel.Information or YettaWarningLevel.Dax;
}
