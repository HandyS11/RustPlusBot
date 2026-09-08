using NSubstitute;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

/// <summary>Unit tests for <see cref="VTrackCommandHandler"/>.</summary>
public sealed class VTrackCommandHandlerTests
{
    private static readonly Guid ServerId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private readonly IVendingTrackService _trackService = Substitute.For<IVendingTrackService>();

    private readonly VTrackCommandHandler _handler;

    /// <summary>Builds the handler over a substituted track service and the real localizer.</summary>
    public VTrackCommandHandlerTests() => _handler = new VTrackCommandHandler(_trackService, new ResxLocalizer());

    private static CommandContext Ctx(params string[] args) => new(7UL, ServerId, "en", 99UL, "Caller", args);

    [Fact]
    public void Name_is_vtrack() => Assert.Equal("vtrack", _handler.Name);

    [Fact]
    public async Task NoArgs_ReturnsUsageAndNeverRegistersACell()
    {
        // Without the guard the handler indexes Args[0] on an empty list. The service assertion is the
        // part that matters: "!vtrack" with no grid must not reach the store at all.
        var reply = await _handler.ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.Equal("Usage: !vtrack <grid>", reply);
        await _trackService.DidNotReceiveWithAnyArgs()
            .TrackGridAsync(default, Guid.Empty, default!, default, default);
    }

    [Fact]
    public async Task ValidGrid_RegistersTheCellForTheCallerAndReportsWhatItHolds()
    {
        _trackService
            .TrackGridAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ulong>(),
                Arg.Any<CancellationToken>())
            .Returns(new GridTrackResult(GridValid: true, MachinesFound: 3, ListingsTracked: 7));

        var reply = await _handler.ExecuteAsync(Ctx("d7"), CancellationToken.None);

        // The grid is passed through verbatim — normalisation is the service's job, not the handler's —
        // and the Steam id must be the in-game caller's, since that is what !vtracked attributes the cell to.
        await _trackService.Received(1)
            .TrackGridAsync(7UL, ServerId, "d7", 99UL, Arg.Any<CancellationToken>());
        Assert.Equal("Tracking d7: 3 machines, 7 listings.", reply);
    }

    [Fact]
    public async Task InvalidGrid_SaysSoRatherThanClaimingTheCellIsTracked()
    {
        _trackService
            .TrackGridAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<ulong>(),
                Arg.Any<CancellationToken>())
            .Returns(new GridTrackResult(GridValid: false, MachinesFound: 0, ListingsTracked: 0));

        var reply = await _handler.ExecuteAsync(Ctx("Z99"), CancellationToken.None);

        Assert.Equal("Z99 is not a grid on this map.", reply);
        Assert.DoesNotContain("Tracking", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullContext_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _handler.ExecuteAsync(null!, CancellationToken.None));
}
