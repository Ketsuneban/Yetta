using Yetta.Core;

namespace Yetta.Desktop;

public partial class MainWindow
{
    // Tallow coordinates execution. DAX makes policy decisions; Marvine only routes commands.
    private async Task<DaxDecision> AuthorizeOperationAsync(DaxOperation operation)
    {
        var administrator = operation == DaxOperation.RunScript
            ? await DaxAdministratorClient.RequestAsync(DaxAdministratorOperation.Status, _updateStop.Token)
            : _administratorState;
        var agent = operation == DaxOperation.RunScript
            ? (await _daxGate.WaitForScriptAgentAsync(_updateStop.Token)).State : _daxGate.Status.State;
        var decision = DaxPolicy.Decide(operation, _settings.ActiveProfile, administrator, agent);
        DaxAudit.Write("policy", decision.Allowed ? "allowed" : "denied", operation + ": " + decision.Reason);
        return decision;
    }
}
