using Microsoft.Extensions.Logging.Abstractions;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class RustPlusSocketSourceTests
{
    [Fact]
    public async Task Create_ReturnsADisposableConnection()
    {
        var source = new RustPlusSocketSource(NullLogger<RustPlusSocketSource>.Instance, NullLoggerFactory.Instance);

        var connection = source.Create("127.0.0.1", 28015, 100UL, "12345");
        await using var _ = connection;

        Assert.NotNull(connection);
    }

    [Fact]
    public async Task Create_NonNumericToken_YieldsAuthRejectedConnection()
    {
        var source = new RustPlusSocketSource(NullLogger<RustPlusSocketSource>.Instance, NullLoggerFactory.Instance);

        var connection = source.Create("127.0.0.1", 28015, 100UL, "not-a-number");
        await using var _ = connection;

        Assert.Equal(SocketConnectOutcome.AuthRejected, await connection.ConnectAsync(TimeSpan.Zero, default));
    }

    [Fact]
    public void FakeConnection_RaiseTeamChanged_InvokesSubscribers()
    {
        var source = new Fakes.FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        var connection = (Fakes.FakeRustSocketSource.FakeConnection)source.Create("127.0.0.1", 28015, 1UL, "1");

        TeamInfoSnapshot? received = null;
        connection.TeamChanged += (_, s) => received = s;
        var snapshot = new TeamInfoSnapshot(5UL, []);
        connection.RaiseTeamChanged(snapshot);

        Assert.Same(snapshot, received);
    }
}
