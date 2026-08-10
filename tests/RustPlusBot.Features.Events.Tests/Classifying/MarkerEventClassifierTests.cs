using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Events.Classifying;

namespace RustPlusBot.Features.Events.Tests.Classifying;

public sealed class MarkerEventClassifierTests
{
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private static MarkerEventClassifier Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new MarkerEventClassifier(clock);
    }

    private static MapMarkersChangedEvent Evt(
        IReadOnlyList<MapMarkerSnapshot> added,
        IReadOnlyList<MapMarkerSnapshot> removed) =>
        new(1UL, Server, new MapDimensions(4000u, 4000u, 500, WorldSize: 4000u), added, removed, []);

    [Fact]
    public void Cargo_added_is_CargoEntered()
    {
        var result = Build().Classify(Evt(
            [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 10f, 20f, null)], []));

        var e = Assert.Single(result);
        Assert.Equal(MapEventKind.CargoEntered, e.Kind);
        Assert.Equal(Now, e.AtUtc);
        Assert.NotNull(e.Dimensions);
    }

    [Fact]
    public void Cargo_removed_is_CargoLeft()
    {
        var result = Build().Classify(Evt([],
            [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 10f, 20f, null)]));
        Assert.Equal(MapEventKind.CargoLeft, Assert.Single(result).Kind);
    }

    [Fact]
    public void Heli_added_and_removed_map_to_entered_and_left()
    {
        Assert.Equal(MapEventKind.HeliEntered, Assert.Single(Build().Classify(
            Evt([new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 0f, 0f, null)], []))).Kind);
        Assert.Equal(MapEventKind.HeliLeft, Assert.Single(Build().Classify(
            Evt([], [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 0f, 0f, null)]))).Kind);
    }

    [Fact]
    public void Chinook_added_is_spawned_but_removal_is_silent()
    {
        Assert.Equal(MapEventKind.ChinookSpawned, Assert.Single(Build().Classify(
            Evt([new MapMarkerSnapshot(3, MarkerKind.Chinook, 0f, 0f, null)], []))).Kind);
        Assert.Empty(Build().Classify(
            Evt([], [new MapMarkerSnapshot(3, MarkerKind.Chinook, 0f, 0f, null)])));
    }

    [Fact]
    public void Crate_and_other_markers_produce_nothing()
    {
        // The game no longer sends crate markers; MarkerKind.Crate (and Other) classify to nothing.
        Assert.Empty(Build().Classify(Evt(
            [
                new MapMarkerSnapshot(4, MarkerKind.Crate, 0f, 0f, null),
                new MapMarkerSnapshot(5, MarkerKind.Other, 0f, 0f, null)
            ],
            [
                new MapMarkerSnapshot(6, MarkerKind.Crate, 0f, 0f, null),
                new MapMarkerSnapshot(7, MarkerKind.Other, 0f, 0f, null)
            ])));
    }

    [Fact]
    public void Multiple_deltas_produce_multiple_events()
    {
        var result = Build().Classify(Evt(
            [
                new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null),
                new MapMarkerSnapshot(3, MarkerKind.Chinook, 0f, 0f, null)
            ],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 0f, 0f, null)]));
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Heli_removed_inside_the_map_is_HeliCrashed()
    {
        // Dead centre of a 4000 world: nowhere near the border, so it came down here.
        var result = Build().Classify(Evt([],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 2000f, 2000f, null)]));

        Assert.Equal(MapEventKind.HeliCrashed, Assert.Single(result).Kind);
    }

    [Fact]
    public void Heli_removed_within_one_cell_of_the_edge_is_HeliLeft()
    {
        // One cell is 146.25 units, so x = 100 is inside the border band: a routine departure.
        var result = Build().Classify(Evt([],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 100f, 2000f, null)]));

        Assert.Equal(MapEventKind.HeliLeft, Assert.Single(result).Kind);
    }

    [Fact]
    public void Heli_removed_outside_the_world_is_HeliLeft()
    {
        var result = Build().Classify(Evt([],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 4500f, 2000f, null)]));

        Assert.Equal(MapEventKind.HeliLeft, Assert.Single(result).Kind);
    }

    [Fact]
    public void Heli_removed_without_dimensions_is_HeliLeft()
    {
        // No world size means neither a cell nor a direction is computable: keep the old behaviour.
        var evt = new MapMarkersChangedEvent(1UL, Server, null, [],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 2000f, 2000f, null)], []);

        Assert.Equal(MapEventKind.HeliLeft, Assert.Single(Build().Classify(evt)).Kind);
    }

    [Fact]
    public void Heli_removed_with_zero_world_size_is_HeliLeft()
    {
        // A zero world size is as unusable as no dimensions at all: keep the old behaviour, even at a
        // position that would otherwise read as dead centre and classify as a crash.
        var evt = new MapMarkersChangedEvent(1UL, Server, new MapDimensions(0u, 0u, 0, WorldSize: 0u), [],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 2000f, 2000f, null)], []);

        Assert.Equal(MapEventKind.HeliLeft, Assert.Single(Build().Classify(evt)).Kind);
    }
}
