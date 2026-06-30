using RustPlusApi.Data;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Maps a Rust+ response outcome to <see cref="DeviceReachability"/>. The ONLY place RustPlusErrorCode is interpreted.</summary>
internal static class ReachabilityMapping
{
    /// <summary>Reachable on success; Removed for not_found; NoPrivilege for access_denied; NoResponse otherwise.</summary>
    /// <param name="isSuccess">Whether the Rust+ response succeeded.</param>
    /// <param name="errorCode">The error code if the response failed, or null.</param>
    /// <returns>The mapped <see cref="DeviceReachability"/> state.</returns>
    public static DeviceReachability FromResponse(bool isSuccess, RustPlusErrorCode? errorCode)
    {
        if (isSuccess)
        {
            return DeviceReachability.Reachable;
        }

        return errorCode switch
        {
            RustPlusErrorCode.NotFound => DeviceReachability.Removed,
            RustPlusErrorCode.AccessDenied => DeviceReachability.NoPrivilege,
            _ => DeviceReachability.NoResponse,
        };
    }
}
