namespace RustPlusBot.Domain.Credentials;

/// <summary>Lifecycle state of a stored player credential within a server's pool.</summary>
public enum CredentialStatus
{
    /// <summary>Eligible to drive the live connection, currently inactive.</summary>
    Standby = 0,

    /// <summary>Currently driving the live connection.</summary>
    Active = 1,

    /// <summary>Rejected by the server (expired/invalid token); excluded from failover.</summary>
    Invalid = 2,
}
