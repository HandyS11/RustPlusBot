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
                MarkerKind.PatrolHelicopter => MapEventKind.HeliLeft,
                _ => null, // Chinook/Crate removal is silent.
            };
            if (kind is { } k)
            {
                events.Add(new RustMapEvent(k, m.X, m.Y, evt.Dimensions, now));
            }
        }

        return events;
    }
}
