using System.Globalization;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!vtracked — lists the grid cells and manually tracked listings registered for this server.</summary>
/// <param name="trackService">Registers and reads grid/listing tracking.</param>
/// <param name="names">Resolves item and currency ids to display names.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class VTrackedCommandHandler(
    IVendingTrackService trackService, IItemNameResolver names, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "vtracked";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var summary = await trackService.GetTrackedAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);

        if (summary.Grids.Count == 0 && summary.Listings.Count == 0)
        {
            return localizer.Get("command.vtracked.none", context.Culture);
        }

        var entries = new List<string>();
        if (summary.Grids.Count > 0)
        {
            entries.Add(string.Join(", ", summary.Grids));
        }

        entries.AddRange(summary.Listings.Select(FormatListing));

        return localizer.Get("command.vtracked.ok", context.Culture, string.Join(", ", entries));
    }

    /// <summary>
    /// Formats one manually tracked listing as quantity, item name, cost, and currency name — the same
    /// shape as the price side of <see cref="Formatting.VendingLine.Format"/>, so the two surfaces read
    /// consistently.
    /// </summary>
    /// <param name="listing">The listing to format.</param>
    private string FormatListing(TrackedListing listing) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{listing.Quantity} {names.Resolve(listing.Key.ItemId)} for {listing.CostPerOrder} {names.Resolve(listing.Key.CurrencyId)}");
}
