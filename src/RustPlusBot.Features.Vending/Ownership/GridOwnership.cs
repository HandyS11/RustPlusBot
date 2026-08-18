using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Features.Vending.Ownership;

/// <summary>
/// Decides which machines belong to the team and projects them into comparable offers. Ownership is by
/// grid cell rather than by machine id, so a machine deployed after registration is picked up on the
/// next poll without anyone re-registering.
/// </summary>
internal static class GridOwnership
{
    /// <summary>
    /// Stands in for a grid label when the world size needed to compute one is unknown (0). Map
    /// dimensions are fetched once per connection and can legitimately fail, leaving a snapshot with
    /// <c>WorldSize == 0</c>; <see cref="MapGrid.CellCount"/> clamps that to a single cell, so a computed
    /// label would place every machine on the server at "A0" — a confident lie. "?" is honest instead:
    /// the price is still useful to a searching player even when the location is not.
    /// </summary>
    public const string UnknownGrid = "?";

    /// <summary>Gets the grid reference a machine stands in.</summary>
    /// <param name="machine">The observed machine.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin against.</param>
    /// <returns>The grid label, e.g. "D7", or <see cref="UnknownGrid"/> when <paramref name="worldSize"/> is 0.</returns>
    public static string GridOf(VendingMachineSnapshot machine, uint worldSize, MapGridStyle style)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return worldSize == 0 ? UnknownGrid : MapGrid.LabelFor(machine.X, machine.Y, worldSize, style);
    }

    /// <summary>True when the machine stands in one of the registered cells.</summary>
    /// <param name="machine">The observed machine.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin against.</param>
    /// <param name="grids">The registered cells; should use an ordinal-ignore-case comparer.</param>
    /// <returns>True when the machine is the team's own.</returns>
    public static bool IsOwned(
        VendingMachineSnapshot machine,
        uint worldSize,
        MapGridStyle style,
        IReadOnlySet<string> grids)
    {
        ArgumentNullException.ThrowIfNull(grids);
        return grids.Contains(GridOf(machine, worldSize, style));
    }

    /// <summary>Projects a machine's sell orders into grid-resolved offers.</summary>
    /// <param name="machine">The observed machine.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin against.</param>
    /// <returns>One offer per sell order.</returns>
    public static IReadOnlyList<VendingOffer> ToOffers(
        VendingMachineSnapshot machine,
        uint worldSize,
        MapGridStyle style)
    {
        ArgumentNullException.ThrowIfNull(machine);
        var grid = GridOf(machine, worldSize, style);
        return
        [
            .. machine.Offers
                .Select(o => new VendingOffer(
                    machine.Id,
                    machine.Name,
                    grid,
                    new ListingKey(o.ItemId, o.ItemIsBlueprint, o.CurrencyId, o.CurrencyIsBlueprint),
                    o.Quantity,
                    o.CostPerOrder,
                    o.AmountInStock))
        ];
    }
}
