using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Events.Classifying;

/// <summary>Maps raw marker add/remove deltas to domain map events.</summary>
/// <param name="clock">Stamps each produced event.</param>
internal sealed class MarkerEventClassifier(IClock clock)
{
    /// <summary>Classifies one marker-change delta into zero or more domain events.</summary>
    /// <param name="evt">The marker-change delta.</param>
    /// <returns>The classified events, in delta order (added first, then removed).</returns>
    public IReadOnlyList<RustMapEvent> Classify(MapMarkersChangedEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var now = clock.UtcNow;
        var events = new List<RustMapEvent>();

        foreach (var m in evt.Added)
        {
            MapEventKind? kind = m.Kind switch
            {
                MarkerKind.CargoShip => MapEventKind.CargoEntered,
                MarkerKind.PatrolHelicopter => MapEventKind.HeliEntered,
                MarkerKind.Chinook => MapEventKind.ChinookSpawned,
                _ => null, // Crate/Other → no event (the game no longer sends crate markers).
            };
            if (kind is { } k)
            {
                events.Add(new RustMapEvent(k, m.X, m.Y, evt.Dimensions, now));
            }
        }

        foreach (var m in evt.Removed)
        {
            MapEventKind? kind = m.Kind switch
            {
                MarkerKind.CargoShip => MapEventKind.CargoLeft,
                MarkerKind.PatrolHelicopter => HeliRemoval(m, evt.Dimensions),
                _ => null, // Chinook/Crate removal is silent.
            };
            if (kind is { } k)
            {
                events.Add(new RustMapEvent(k, m.X, m.Y, evt.Dimensions, now));
            }
        }

        return events;
    }

    /// <summary>
    /// Classifies heli marker removal: crashed if it vanished well inside the map, left if at or beyond the border.
    /// </summary>
    /// <param name="marker">The removed marker snapshot.</param>
    /// <param name="dims">The world dimensions, or null if unavailable.</param>
    /// <remarks>
    /// The heli marker vanishes either because players shot it down or because it finished its patrol
    /// and flew off the map. Polling samples position, so a heli that has just crossed the border is
    /// usually still reported slightly inside it — hence the one-cell band rather than a strict
    /// inside/outside test, which would misreport most routine departures as crashes.
    /// </remarks>
    private static MapEventKind HeliRemoval(MapMarkerSnapshot marker, MapDimensions? dims) =>
        dims is null || dims.WorldSize == 0 || MapGrid.IsAtOrBeyondBorder(marker.X, marker.Y, dims.WorldSize)
            ? MapEventKind.HeliLeft
            : MapEventKind.HeliCrashed;
}
