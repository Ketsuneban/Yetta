namespace Yetta.Core;

public enum YettaWarningLevel
{
    Information,
    Consent,
    Caution,
    Dangerous,
    Critical,
    Fatal
}

/// <summary>
/// Presentation and consent metadata only. No warning or TTY token can grant
/// Windows privileges. Actual execution is always subject to OS ACL and UAC.
/// </summary>
public static class WarningPolicy
{
    public static int StatusIcon(YettaWarningLevel level, bool safeMode) =>
        safeMode ? 35 : level is YettaWarningLevel.Information ? 27 : 26;

    public static string Heading(YettaWarningLevel level) => level switch
    {
        YettaWarningLevel.Information => "Информация",
        YettaWarningLevel.Consent => "Подтверждение согласия",
        YettaWarningLevel.Caution => "Предупреждение",
        YettaWarningLevel.Dangerous => "Опасное действие",
        YettaWarningLevel.Critical => "Критическое действие",
        _ => "Ошибка восстановления"
    };

    public static bool RequiresAdditionalAcknowledgement(YettaWarningLevel level) =>
        level is YettaWarningLevel.Consent or YettaWarningLevel.Caution
            or YettaWarningLevel.Dangerous or YettaWarningLevel.Critical;
}
