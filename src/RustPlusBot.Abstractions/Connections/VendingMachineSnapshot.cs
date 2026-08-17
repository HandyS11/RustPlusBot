namespace RustPlusBot.Abstractions.Connections;

/// <summary>One player vending machine observed in a <c>GetMapMarkers</c> poll.</summary>
/// <param name="Id">The stable marker id.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Name">The shopfront name, if any.</param>
/// <param name="IsOutOfStock">Whole-machine empty flag; null when the server did not report it.</param>
/// <param name="Offers">The machine's sell orders.</param>
public sealed record VendingMachineSnapshot(
    ulong Id,
    float X,
    float Y,
    string? Name,
    bool? IsOutOfStock,
    IReadOnlyList<VendingOfferSnapshot> Offers);
