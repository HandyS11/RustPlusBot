using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class EventHandlersTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 5, 0, TimeSpan.Zero);

    private static (IClock Clock, ILocalizer Loc) Deps()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return (clock, new ResxLocalizer());
    }

    private static CommandContext Ctx() => new(Guild, Server, "en", 0UL, string.Empty, []);

    [Fact]
    public async Task Cargo_with_active_marker_reports_grid()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        var dims = new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u);
        state.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip).Returns(
            [new ActiveMarker(1, MarkerKind.CargoShip, 10f, 3990f, dims, Now.AddMinutes(-5))]);
        var reply = await new CargoCommandHandler(state, loc, clock).ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("Cargo Ship at", reply, StringComparison.Ordinal);
        // Dimensions present → a grid ref (e.g. "A0"), NOT raw "(10, 3990)" coords.
        Assert.DoesNotContain("(10, 3990)", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cargo_without_marker_reports_none()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip).Returns([]);
        var reply = await new CargoCommandHandler(state, loc, clock).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No cargo ship on the map.", reply);
    }

    [Fact]
    public async Task Events_lists_recent_or_reports_none()
    {
        var (_, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetRecentEvents(Guild, Server).Returns([]);
        Assert.Equal("No recent events.",
            await new EventsCommandHandler(state, loc).ExecuteAsync(Ctx(), CancellationToken.None));

        state.GetRecentEvents(Guild, Server).Returns(
            [new RustMapEvent(MapEventKind.CargoEntered, 10f, 3990f,
                new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u), Now)]);
        var reply = await new EventsCommandHandler(state, loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Contains("Recent:", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handlers_expose_expected_names()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        Assert.Equal("cargo", new CargoCommandHandler(state, loc, clock).Name);
        Assert.Equal("heli", new HeliCommandHandler(state, loc, clock).Name);
        Assert.Equal("chinook", new ChinookCommandHandler(state, loc, clock).Name);
        Assert.Equal("events", new EventsCommandHandler(state, loc).Name);
    }
}
