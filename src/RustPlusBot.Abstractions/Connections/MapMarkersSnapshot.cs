namespace RustPlusBot.Abstractions.Connections;

/// <summary>The result of one <c>GetMapMarkers</c> poll: the markers the bot diffs, plus the vending machines.</summary>
/// <param name="Markers">Cargo ship / patrol helicopter / chinook / travelling vendor markers.</param>
/// <param name="VendingMachines">Every player vending machine on the server, with its full offer list.</param>
public sealed record MapMarkersSnapshot(
    IReadOnlyList<MapMarkerSnapshot> Markers,
    IReadOnlyList<VendingMachineSnapshot> VendingMachines)
{
    /// <summary>An empty snapshot, used when there is no live socket.</summary>
    public static MapMarkersSnapshot Empty { get; } = new([], []);
}
