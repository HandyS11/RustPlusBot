using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Ownership;
using RustPlusBot.Features.Vending.Searching;

namespace RustPlusBot.Features.Vending.Evaluating;

/// <summary>
/// Works out which of our listings are being matched or beaten. Pure: the same inputs always give the
/// same notices, which is what lets the relay treat its output as a desired state to reconcile.
/// </summary>
internal static class UndercutEvaluator
{
    /// <summary>Evaluates undercuts across a server's observed machines.</summary>
    /// <param name="machines">Every machine observed in the latest poll.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin positions against.</param>
    /// <param name="grids">Our registered grid cells; use an ordinal-ignore-case set.</param>
    /// <param name="listings">Our manually registered listings.</param>
    /// <returns>One notice per listing of ours with at least one rival at or below our price.</returns>
    public static IReadOnlyList<UndercutNotice> Evaluate(
        IReadOnlyList<VendingMachineSnapshot> machines,
        uint worldSize,
        MapGridStyle style,
        IReadOnlySet<string> grids,
        IReadOnlyList<TrackedListing> listings)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentNullException.ThrowIfNull(listings);

        var mine = new Dictionary<ListingKey, (int Quantity, int CostPerOrder)>();
        var theirs = new List<VendingOffer>();

        foreach (var machine in machines)
        {
            var owned = GridOwnership.IsOwned(machine, worldSize, style, grids);
            foreach (var offer in GridOwnership.ToOffers(machine, worldSize, style))
            {
                if (owned)
                {
                    Reference(mine, offer.Key, offer.Quantity, offer.CostPerOrder);
                }
                else
                {
                    theirs.Add(offer);
                }
            }
        }

        foreach (var listing in listings)
        {
            Reference(mine, listing.Key, listing.Quantity, listing.CostPerOrder);
        }

        var notices = new List<UndercutNotice>();
        foreach (var (key, reference) in mine)
        {
            // A sold-out rival takes none of our sales, so it is not a threat — but it is still a real
            // search result, which is why the exclusion lives here and not in the index.
            List<VendingOffer> undercutters =
            [
                .. theirs.Where(o => o.Key == key
                                     && o.InStock
                                     && UnitPrice.IsAtOrBelow(
                                         o.CostPerOrder, o.Quantity, reference.CostPerOrder, reference.Quantity)),
            ];

            if (undercutters.Count > 0)
            {
                notices.Add(new UndercutNotice(
                    key, reference.Quantity, reference.CostPerOrder, VendingSearch.Order(undercutters)));
            }
        }

        return notices;
    }

    /// <summary>
    /// Keeps the cheapest of our offers for a listing as its reference: that is what defines our
    /// competitiveness, so a rival who beats our dearer duplicate but not our best price has not
    /// actually taken anything.
    /// </summary>
    /// <param name="mine">The reference prices accumulated so far, keyed by listing.</param>
    /// <param name="key">The listing identity.</param>
    /// <param name="quantity">Items yielded by one order of the candidate offer.</param>
    /// <param name="costPerOrder">Currency charged for one order of the candidate offer.</param>
    private static void Reference(
        Dictionary<ListingKey, (int Quantity, int CostPerOrder)> mine,
        ListingKey key,
        int quantity,
        int costPerOrder)
    {
        if (!mine.TryGetValue(key, out var best)
            || UnitPrice.IsAtOrBelow(costPerOrder, quantity, best.CostPerOrder, best.Quantity))
        {
            mine[key] = (quantity, costPerOrder);
        }
    }
}
