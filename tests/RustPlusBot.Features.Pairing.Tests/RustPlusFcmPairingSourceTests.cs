using Microsoft.Extensions.Logging.Abstractions;
using RustPlusBot.Features.Pairing.Listening;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class RustPlusFcmPairingSourceTests
{
    [Fact]
    public async Task Create_DoesNotThrow_AndConnectReturnsRejected_ForUnparseableCredentials()
    {
        var source = new RustPlusFcmPairingSource(NullLogger<RustPlusFcmPairingSource>.Instance);

        // "null" deserializes to a null Credentials, which the listener ctor rejects — Create must not throw.
        var listener = source.Create("null", (_, _) => Task.CompletedTask);
        var outcome = await listener.ConnectAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(PairingConnectOutcome.Rejected, outcome);
        await listener.DisposeAsync();
    }
}
