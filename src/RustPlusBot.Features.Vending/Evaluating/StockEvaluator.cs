using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Ownership;
using RustPlusBot.Features.Vending.Searching;

namespace RustPlusBot.Features.Vending.Evaluating;

/// <summary>
/// Works out which of our own machines have run dry. Pure, and deliberately separate from the undercut
/// evaluator: the two answer different questions about the same poll and are reconciled independently.
/// </summary>
internal static class StockEvaluator
{
    /// <summary>Evaluates sell-outs across our registered machines.</summary>
    /// <param name="machines">Every machine observed in the latest poll.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin positions against.</param>
    /// <param name="grids">Our registered grid cells; use an ordinal-ignore-case set.</param>
    /// <returns>One notice per machine of ours with something sold out.</returns>
    public static IReadOnlyList<StockNotice> Evaluate(
        IReadOnlyList<VendingMachineSnapshot> machines,
        uint worldSize,
        MapGridStyle style,
        IReadOnlySet<string> grids)
    {
        ArgumentNullException.ThrowIfNull(machines);

        var notices = new List<StockNotice>();
        foreach (var machine in machines)
        {
            if (!GridOwnership.IsOwned(machine, worldSize, style, grids))
            {
                continue;
            }

            var grid = GridOwnership.GridOf(machine, worldSize, style);

            // A wholly empty machine collapses to one line: enumerating twenty dead listings tells the
            // owner nothing they do not already know from "the shop is empty".
            if (machine.IsOutOfStock == true)
            {
                notices.Add(new StockNotice(machine.Id, machine.Name, grid, MachineEmpty: true, []));
                continue;
            }

            // A null flag means the server did not report one; per-offer stock is still trustworthy.
            List<VendingOffer> soldOut =
                [.. GridOwnership.ToOffers(machine, worldSize, style).Where(o => !o.InStock)];
            if (soldOut.Count > 0)
            {
                notices.Add(new StockNotice(
                    machine.Id, machine.Name, grid, MachineEmpty: false, VendingSearch.Order(soldOut)));
            }
        }

        return notices;
    }
}
