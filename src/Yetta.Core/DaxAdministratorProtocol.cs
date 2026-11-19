namespace Yetta.Core;

public enum DaxAdministratorState { Unavailable, NotInstalled, NotGranted, Inactive, Active, Revoked }
public enum DaxAdministratorOperation : byte { Status = 1, Enable, Disable, Revoke }

public static class DaxAdministratorProtocol
{
    public const string ServiceName = "YettaDaxAdministrator";
    public const string PipeName = "Yetta.DaxAdministrator.v1";
    public const string RegistryPath = @"SOFTWARE\YettaDaxAdministrator";
    public const string StateKey = "State";
    public const string OwnerValue = "AuthorizedSid";
    public const string StateValue = "Access";
    public const byte WireVersion = 1;
}
