using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Connections.Tests.Fakes;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class SeamSmokeTests
{
    [Fact]
    public async Task FakeSource_CreatesConnection_AndDefaultsToConnectedOkZero()
    {
        var source = new FakeRustSocketSource();

        var connection = source.Create("1.2.3.4", 28015, 100UL, "tok");
        await using var _ = connection;

        Assert.Equal(SocketConnectOutcome.Connected, await connection.ConnectAsync(TimeSpan.Zero, default));
        var beat = await connection.GetInfoAsync(TimeSpan.Zero, default);
        Assert.Equal(HeartbeatKind.Ok, beat.Kind);
        Assert.Equal(1, source.CreateCount);
    }
}
