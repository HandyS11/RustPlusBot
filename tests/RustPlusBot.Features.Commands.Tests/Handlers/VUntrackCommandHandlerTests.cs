using NSubstitute;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

/// <summary>Unit tests for <see cref="VUntrackCommandHandler"/>.</summary>
public sealed class VUntrackCommandHandlerTests
{
    private static readonly Guid ServerId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IVendingTrackService _trackService = Substitute.For<IVendingTrackService>();

    private readonly VUntrackCommandHandler _handler;

    /// <summary>Builds the handler over a substituted track service and the real localizer.</summary>
    public VUntrackCommandHandlerTests() => _handler = new VUntrackCommandHandler(_trackService, new ResxLocalizer());

    private static CommandContext Ctx(params string[] args) => new(7UL, ServerId, "en", 99UL, "Caller", args);

    [Fact]
    public void Name_is_vuntrack() => Assert.Equal("vuntrack", _handler.Name);

    [Fact]
    public async Task NoArgs_ReturnsUsageAndNeverRemovesACell()
    {
        // Without the guard the handler indexes Args[0] on an empty list; and a bare "!vuntrack" must
        // never be able to reach the store, where it could only delete the wrong thing.
        var reply = await _handler.ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.Equal("Usage: !vuntrack <grid>", reply);
        await _trackService.DidNotReceiveWithAnyArgs().UntrackGridAsync(default, Guid.Empty, default!, default);
    }

    [Fact]
    public async Task TrackedGrid_IsRemovedAndConfirmed()
    {
        _trackService
            .UntrackGridAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var reply = await _handler.ExecuteAsync(Ctx("d7"), CancellationToken.None);

        await _trackService.Received(1).UntrackGridAsync(7UL, ServerId, "d7", Arg.Any<CancellationToken>());
        Assert.Equal("No longer tracking d7.", reply);
    }

    [Fact]
    public async Task UntrackedGrid_SaysNothingWasRemovedRatherThanConfirming()
    {
        // The two replies are the only way the caller can tell a typo'd cell from a real removal.
        _trackService
            .UntrackGridAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var reply = await _handler.ExecuteAsync(Ctx("Z99"), CancellationToken.None);

        Assert.Equal("Z99 was not tracked.", reply);
        Assert.DoesNotContain("No longer tracking", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NullContext_Throws() =>
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _handler.ExecuteAsync(null!, CancellationToken.None));
}
