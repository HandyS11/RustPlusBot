using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

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

    /// <summary>A settings store returning the all-on defaults (in-game grid style).</summary>
    private static IMapSettingsStore Settings()
    {
        var store = Substitute.For<IMapSettingsStore>();
        store.GetAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(MapLayerSettings.AllOn);
        return store;
    }

    private static CommandContext Ctx() => new(Guild, Server, "en", 0UL, string.Empty, []);

    [Fact]
    public async Task Cargo_with_active_marker_reports_grid()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        var dims = new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u);
        state.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip).Returns(
        [
            new ActiveMarker(1, MarkerKind.CargoShip, 10f, 3990f, dims, Now.AddMinutes(-5),
                [new TrailPoint(10f, 3990f)], null)
        ]);
        var reply = await new CargoCommandHandler(state, loc, clock, Settings()).ExecuteAsync(Ctx(),
            CancellationToken.None);

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
        var reply = await new CargoCommandHandler(state, loc, clock, Settings()).ExecuteAsync(Ctx(),
            CancellationToken.None);
        Assert.Equal("No cargo ship on the map.", reply);
    }

    [Fact]
    public async Task Events_lists_recent_or_reports_none()
    {
        var (_, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetRecentEvents(Guild, Server).Returns([]);
        Assert.Equal("No recent events.",
            await new EventsCommandHandler(state, loc, Settings()).ExecuteAsync(Ctx(), CancellationToken.None));

        state.GetRecentEvents(Guild, Server).Returns(
        [
            new RustMapEvent(MapEventKind.CargoEntered, 10f, 3990f,
                new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u), Now)
        ]);
        var reply = await new EventsCommandHandler(state, loc, Settings()).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Contains("Recent:", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handlers_expose_expected_names()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        Assert.Equal("cargo", new CargoCommandHandler(state, loc, clock, Settings()).Name);
        Assert.Equal("heli", new HeliCommandHandler(state, loc, clock, Settings()).Name);
        Assert.Equal("chinook", new ChinookCommandHandler(state, loc, clock, Settings()).Name);
        Assert.Equal("events", new EventsCommandHandler(state, loc, Settings()).Name);
    }

    private static readonly MapDimensions Dims4000 = new(4000u, 4000u, 500, WorldSize: 4000u);

    [Fact]
    public async Task Heli_off_the_map_reports_a_direction()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetActiveMarkers(Guild, Server, MarkerKind.PatrolHelicopter).Returns(
        [
            new ActiveMarker(1, MarkerKind.PatrolHelicopter, 4500f, 4500f, Dims4000, Now.AddMinutes(-5),
                [new TrailPoint(4500f, 4500f)], null)
        ]);

        var reply = await new HeliCommandHandler(state, loc, clock, Settings()).ExecuteAsync(Ctx(),
            CancellationToken.None);

        Assert.Equal("Patrol Helicopter to the north-east (5m ago)", reply);
    }

    [Fact]
    public async Task Events_reports_a_crash_and_an_off_map_spawn()
    {
        var (_, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetRecentEvents(Guild, Server).Returns(
        [
            new RustMapEvent(MapEventKind.HeliCrashed, 2000f, 2000f, Dims4000, Now),
            new RustMapEvent(MapEventKind.CargoEntered, 4500f, 4500f, Dims4000, Now)
        ]);

        var reply = await new EventsCommandHandler(state, loc, Settings()).ExecuteAsync(Ctx(),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("heli crashed in", reply, StringComparison.Ordinal);
        Assert.Contains("cargo from the north-east", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Events_off_map_departure_reports_a_direction()
    {
        var (_, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetRecentEvents(Guild, Server).Returns(
        [
            new RustMapEvent(MapEventKind.CargoLeft, -500f, 100f, Dims4000, Now)
        ]);

        var reply = await new EventsCommandHandler(state, loc, Settings()).ExecuteAsync(Ctx(),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("cargo left to the south-west", reply, StringComparison.Ordinal);
    }
}
