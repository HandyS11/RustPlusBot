namespace RustPlusBot.Abstractions.Vending;

/// <summary>One sell order, resolved to a grid reference and ready to display or compare.</summary>
/// <param name="MachineId">The vending machine's marker id.</param>
/// <param name="ShopName">The shopfront name, if any.</param>
/// <param name="Grid">The grid reference of the machine, e.g. "D7".</param>
/// <param name="Key">The listing identity.</param>
/// <param name="Quantity">Items yielded by one order; always at least 1.</param>
/// <param name="CostPerOrder">Currency charged for one order.</param>
/// <param name="AmountInStock">Orders remaining; 0 means sold out.</param>
public sealed record VendingOffer(
    ulong MachineId,
    string? ShopName,
    string Grid,
    ListingKey Key,
    int Quantity,
    int CostPerOrder,
    int AmountInStock)
{
    /// <summary>True when at least one order remains.</summary>
    public bool InStock => AmountInStock > 0;
}
