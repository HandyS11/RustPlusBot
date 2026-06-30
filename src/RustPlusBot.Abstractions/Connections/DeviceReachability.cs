namespace RustPlusBot.Abstractions.Connections;

/// <summary>Per-device reachability, independent of whole-server connection status.</summary>
public enum DeviceReachability
{
    /// <summary>The device read/actuated successfully.</summary>
    Reachable = 0,

    /// <summary>The in-game entity no longer exists (was destroyed). Maps from <c>not_found</c>.</summary>
    Removed = 1,

    /// <summary>The active player lacks building privilege / token access. Maps from <c>access_denied</c>.</summary>
    NoPrivilege = 2,

    /// <summary>The device did not answer in time (timeout / unknown / other server error).</summary>
    NoResponse = 3,
}
