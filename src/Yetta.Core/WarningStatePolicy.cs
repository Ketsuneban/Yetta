namespace Yetta.Core;
public enum WarningState { None = 0, SafeMode = 1, Danger = 2, Safe = 3 }
public static class WarningStatePolicy
{
    public static WarningState Resolve(bool decision, bool threatensSystem, bool windowsSafeMode) => !decision
        ? WarningState.None : windowsSafeMode ? WarningState.SafeMode : threatensSystem ? WarningState.Danger : WarningState.Safe;
}
