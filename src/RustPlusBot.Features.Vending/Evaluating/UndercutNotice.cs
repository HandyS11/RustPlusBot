using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Features.Vending.Evaluating;

/// <summary>One listing of ours that rivals are matching or beating right now.</summary>
/// <param name="Key">The listing identity.</param>
/// <param name="ReferenceQuantity">Our best offer's order quantity.</param>
/// <param name="ReferenceCostPerOrder">Our best offer's order cost.</param>
/// <param name="Undercutters">The rival offers at or below our price, cheapest first.</param>
public sealed record UndercutNotice(
    ListingKey Key,
    int ReferenceQuantity,
    int ReferenceCostPerOrder,
    IReadOnlyList<VendingOffer> Undercutters);
