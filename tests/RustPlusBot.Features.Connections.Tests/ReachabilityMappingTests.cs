using RustPlusApi.Data;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ReachabilityMappingTests
{
    [Fact]
    public void Success_IsReachable() =>
        Assert.Equal(DeviceReachability.Reachable, ReachabilityMapping.FromResponse(true, null));

    [Theory]
    [InlineData(RustPlusErrorCode.NotFound, DeviceReachability.Removed)]
    [InlineData(RustPlusErrorCode.AccessDenied, DeviceReachability.NoPrivilege)]
    [InlineData(RustPlusErrorCode.Unknown, DeviceReachability.NoResponse)]
    [InlineData(RustPlusErrorCode.ServerError, DeviceReachability.NoResponse)]
    [InlineData(RustPlusErrorCode.RateLimit, DeviceReachability.NoResponse)]
    public void Failure_MapsByCode(RustPlusErrorCode code, DeviceReachability expected) =>
        Assert.Equal(expected, ReachabilityMapping.FromResponse(false, code));

    [Fact]
    public void Failure_NullCode_IsNoResponse() =>
        Assert.Equal(DeviceReachability.NoResponse, ReachabilityMapping.FromResponse(false, null));
}
