namespace Yetta.Core;

public enum DaxOperation { Inspect, ManagePackages, RunScript, StopScript, UpdateApplication }
public sealed record DaxDecision(bool Allowed, string Reason);

/// <summary>DAX owns policy; Tallow must obtain a decision before invoking a child module.</summary>
public static class DaxPolicy
{
    public static DaxDecision Decide(DaxOperation operation, string profile,
        DaxAdministratorState administrator, DaxState agent)
    {
        if (!Enum.IsDefined(operation)) return new(false, "Неизвестная операция DAX.");
        if (operation is DaxOperation.Inspect or DaxOperation.StopScript or DaxOperation.UpdateApplication)
            return new(true, "");
        if (profile is not ("User" or "Administrator"))
            return new(false, "Выбери профиль Пользователь или Администратор в DAX Guard.");
        if (operation == DaxOperation.ManagePackages) return new(true, "");
        if (administrator != DaxAdministratorState.Active || profile != "Administrator")
            return new(false, "DAX не подтвердил активный профиль Администратор.");
        return agent == DaxState.Ready ? new(true, "") :
            new(false, "DAX недоступен или не готов. Запуск отменён.");
    }
}
