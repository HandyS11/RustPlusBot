using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Events.Tests.State;

public sealed class EventStateStoreTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private static EventStateStore Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new EventStateStore(clock);
    }

    private static MapMarkersChangedEvent Delta(
        IReadOnlyList<MapMarkerSnapshot> added,
        IReadOnlyList<MapMarkerSnapshot> removed) =>
        new(Guild, Server, null, added, removed, []);

    private static MapMarkersChangedEvent DeltaWithDims(
        IReadOnlyList<MapMarkerSnapshot> added,
        IReadOnlyList<MapMarkerSnapshot> removed,
        MapDimensions? dims) =>
        new(Guild, Server, dims, added, removed, []);

    [Fact]
    public void Added_marker_becomes_active_and_carries_dimensions()
    {
        var store = Build();
        var dims = new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u);
        store.Apply(
            DeltaWithDims([new MapMarkerSnapshot(1, MarkerKind.CargoShip, 10f, 20f, null)], [], dims),
            [new RustMapEvent(MapEventKind.CargoEntered, 10f, 20f, dims, Now)]);

        var active = store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip);
        Assert.Single(active);
        Assert.Equal(10f, active[0].X);
        Assert.Equal(dims, active[0].Dimensions);
        Assert.Equal(Now, active[0].SeenAtUtc);
    }

    [Fact]
    public void Removed_marker_is_cleared_from_active_even_when_unalerted()
    {
        var store = Build();
        // A Crate marker produces NO classified event (core-3), but is still tracked in the active set...
        store.Apply(Delta([new MapMarkerSnapshot(4, MarkerKind.Crate, 0f, 0f, null)], []), []);
        Assert.Single(store.GetActiveMarkers(Guild, Server, MarkerKind.Crate));
        // ...and its (un-alerted) removal must still clear it.
        store.Apply(Delta([], [new MapMarkerSnapshot(4, MarkerKind.Crate, 0f, 0f, null)]), []);

        Assert.Empty(store.GetActiveMarkers(Guild, Server, MarkerKind.Crate));
    }

    [Fact]
    public void Recent_events_are_newest_first_and_bounded_to_ten()
    {
        var store = Build();
        for (var i = 0; i < 12; i++)
        {
            store.Apply(
                Delta([new MapMarkerSnapshot((ulong)i, MarkerKind.CargoShip, i, 0f, null)], []),
                [new RustMapEvent(MapEventKind.CargoEntered, i, 0f, null, Now.AddMinutes(i))]);
        }

        var recent = store.GetRecentEvents(Guild, Server);
        Assert.Equal(10, recent.Count);
        Assert.Equal(Now.AddMinutes(11), recent[0].AtUtc); // newest first
    }

    [Fact]
    public void Clear_drops_active_and_recent_for_that_server()
    {
        var store = Build();
        store.Apply(Delta([new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null)], []),
            [new RustMapEvent(MapEventKind.CargoEntered, 0f, 0f, null, Now)]);

        store.Clear(Guild, Server);

        Assert.Empty(store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
        Assert.Empty(store.GetRecentEvents(Guild, Server));
    }
}
