using System.Globalization;
using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Features.Vending.Evaluating;

/// <summary>One machine of ours with something sold out.</summary>
/// <param name="MachineId">The machine's marker id.</param>
/// <param name="ShopName">The shopfront name, if any.</param>
/// <param name="Grid">The grid reference the machine stands in.</param>
/// <param name="MachineEmpty">True when the whole machine reports empty; <see cref="SoldOut"/> is then empty.</param>
/// <param name="SoldOut">The individual sold-out offers, cheapest first.</param>
public sealed record StockNotice(
    ulong MachineId,
    string? ShopName,
    string Grid,
    bool MachineEmpty,
    IReadOnlyList<VendingOffer> SoldOut)
{
    /// <summary>
    /// The sold-out set as a comparable string: "*" for a wholly empty machine, otherwise the sold-out
    /// item ids sorted ascending and comma-joined. The relay compares this against the persisted value to
    /// tell an owner restock (delete the message) from an item selling out (edit it).
    /// </summary>
    public string Signature => MachineEmpty
        ? "*"
        : string.Join(',', SoldOut
            .Select(o => o.Key.ItemId)
            .Order()
            .Select(id => id.ToString(CultureInfo.InvariantCulture)));
}
