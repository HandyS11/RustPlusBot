using Microsoft.Extensions.Logging.Abstractions;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class RustPlusSocketSourceTests
{
    [Fact]
    public async Task Create_ReturnsADisposableConnection()
    {
        var source = new RustPlusSocketSource(NullLogger<RustPlusSocketSource>.Instance);

        var connection = source.Create("127.0.0.1", 28015, 100UL, "12345");
        await using var _ = connection;

        Assert.NotNull(connection);
    }

    [Fact]
    public async Task Create_NonNumericToken_YieldsAuthRejectedConnection()
    {
        var source = new RustPlusSocketSource(NullLogger<RustPlusSocketSource>.Instance);

        var connection = source.Create("127.0.0.1", 28015, 100UL, "not-a-number");
        await using var _ = connection;

        Assert.Equal(SocketConnectOutcome.AuthRejected, await connection.ConnectAsync(TimeSpan.Zero, default));
    }
}
