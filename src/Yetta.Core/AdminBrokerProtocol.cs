namespace Yetta.Core;

/// <summary>
/// Strict versioned IPC contract. A Yetta UI profile is never an OS security token.
/// Privileged operations are allowlisted independently by the installed service.
/// </summary>
public static class AdminBrokerProtocol
{
    public const int Version = 1;
    public const string ServiceName = "YettaAdminBroker";
    public const string PipeName = "Yetta.AdminBroker.v1";
    public const int MaxMessageBytes = 2048;

    public const string Ping = "ping";
    public const string WriteMachineProof = "write-machine-proof";
    public const string ReadMachineProof = "read-machine-proof";
    public const string ClearMachineProof = "clear-machine-proof";
}

public sealed record AdminBrokerRequest(int Version, string Operation);
public sealed record AdminBrokerReply(bool Success, string Message, string? Detail = null);
