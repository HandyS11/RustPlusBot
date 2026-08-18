using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Connections.Servers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Features.Vending.Rendering;
using RustPlusBot.Features.Vending.Searching;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Vending.Modules;

/// <summary>The /vending, /vending-track, /vending-untrack, and /vending-tracked slash commands.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class VendingModule(IServiceScopeFactory scopeFactory) : InteractionModuleBase<SocketInteractionContext>
{
    private const string MustBeUsedInServer = "This command must be used in a server.";
    private const string AmbiguousKey = "command.item.ambiguous";
    private const string NotFoundKey = "command.item.notfound";
    private const string ServerSummary = "Which server (only needed if more than one)";
    private const int SearchLimit = 10;

    /// <summary>Discord rejects an embed field value longer than this; see <see cref="Fit"/>.</summary>
    private const int FieldValueLimit = 1024;

    /// <summary>Finds every vending machine selling an item.</summary>
    /// <param name="item">The item name or id.</param>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("vending", "Find vending machines selling an item")]
    public Task VendingAsync(
        [Summary("item", "Item name or id")] string item,
        [Summary("server", ServerSummary)] [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => SearchAsync(item, server);

    /// <summary>Registers a listing you sell, to be alerted when someone matches or beats your price.</summary>
    /// <param name="item">The item name or id you sell.</param>
    /// <param name="price">The currency charged for one order.</param>
    /// <param name="currency">The currency item name or id; defaults to Scrap.</param>
    /// <param name="quantity">Items yielded by one order; defaults to 1.</param>
    /// <param name="blueprint">True when you sell the item's blueprint, not the item itself; defaults to false.</param>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("vending-track", "Track a listing you sell and get alerted when undercut")]
    public Task TrackAsync(
        [Summary("item", "Item name or id")] string item,
        [Summary("price", "Cost of one order")]
        int price,
        [Summary("currency", "Currency item name or id")]
        string currency = "scrap",
        [Summary("quantity", "Items per order")]
        int quantity = 1,
        [Summary("blueprint", "I am selling the blueprint, not the item, defaults to false")]
        bool blueprint = false,
        [Summary("server", ServerSummary)] [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => TrackListingCommandAsync(item, price, currency, quantity, blueprint, server);

    /// <summary>Unregisters a tracked grid cell or manually tracked listing.</summary>
    /// <param name="target">The grid or listing to stop tracking, picked from the autocompleted list.</param>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("vending-untrack", "Stop tracking a grid cell or a manually tracked listing")]
    public Task UntrackAsync(
        [Summary("target", "The grid or listing to stop tracking")]
        [Autocomplete(typeof(VendingUntrackAutocompleteHandler))]
        string target,
        [Summary("server", ServerSummary)] [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => UntrackTargetCommandAsync(target, server);

    /// <summary>Shows every grid cell and listing this team currently tracks.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("vending-tracked", "Show everything this team tracks")]
    public Task TrackedAsync(
        [Summary("server", ServerSummary)] [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => ShowTrackedCommandAsync(server);

    private async Task SearchAsync(string item, string? server)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(MustBeUsedInServer, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var sp = scope.ServiceProvider;
            var loc = sp.GetRequiredService<ILocalizer>();
            var resolved = await ResolveAsync(sp, server).ConfigureAwait(false);
            if (resolved is not { } ctx)
            {
                return;
            }

            var items = sp.GetRequiredService<IItemDatabase>();
            switch (items.Resolve(item))
            {
                case ItemMatch.Found found:
                    var mapSettings = sp.GetRequiredService<IMapSettingsStore>();
                    var settings = await mapSettings.GetAsync(ctx.GuildId, ctx.ServerId).ConfigureAwait(false);
                    var readModel = sp.GetRequiredService<IVendingReadModel>();
                    // IVendingReadModel.Search contracts its result as already ordered, so re-ordering
                    // here would only be a second sort over the same comparison.
                    var offers = readModel.Search(ctx.GuildId, ctx.ServerId, found.Item.Id, settings.GridStyle);
                    var (shown, more) = VendingSearch.Take(offers, SearchLimit);
                    var renderer = sp.GetRequiredService<VendingEmbedRenderer>();
                    var embed = renderer.RenderSearch(found.Item.Name, shown, more, ctx.Culture);
                    await FollowupAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
                    break;
                case ItemMatch.Ambiguous ambiguous:
                    await FollowupAsync(Ambiguous(loc, ctx.Culture, ambiguous.Candidates.Select(c => c.Name)),
                        ephemeral: true).ConfigureAwait(false);
                    break;
                default:
                    await FollowupAsync(NotFound(loc, ctx.Culture, item), ephemeral: true).ConfigureAwait(false);
                    break;
            }
        }
    }

    private async Task TrackListingCommandAsync(
        string item,
        int price,
        string currency,
        int quantity,
        bool blueprint,
        string? server)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(MustBeUsedInServer, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var sp = scope.ServiceProvider;
            var loc = sp.GetRequiredService<ILocalizer>();
            var resolved = await ResolveAsync(sp, server).ConfigureAwait(false);
            if (resolved is not { } ctx)
            {
                return;
            }

            if (price < 1 || quantity < 1)
            {
                await FollowupAsync(loc.Get("vending.track.badprice", ctx.Culture), ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            var items = sp.GetRequiredService<IItemDatabase>();
            if (!TryResolveOne(items, item, loc, ctx.Culture, out var itemRecord, out var itemError))
            {
                await FollowupAsync(itemError, ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (!TryResolveOne(items, currency, loc, ctx.Culture, out var currencyRecord, out var currencyError))
            {
                await FollowupAsync(currencyError, ephemeral: true).ConfigureAwait(false);
                return;
            }

            // CurrencyIsBlueprint is deliberately hardcoded false: selling blueprints is common enough to
            // deserve its own option, but charging in blueprints is vanishingly rare — a second option
            // here would double the clutter on every invocation to cover a case almost nobody has. The
            // grid-registration path (!vtrack) still gets this right for the rare case, since it reads
            // the flag off the real machine rather than asking a human to spell it out.
            var key = new ListingKey(itemRecord.Id, blueprint, currencyRecord.Id, CurrencyIsBlueprint: false);
            var trackService = sp.GetRequiredService<IVendingTrackService>();
            await trackService
                .TrackListingAsync(ctx.GuildId, ctx.ServerId, key, quantity, price, Context.User.Id,
                    CancellationToken.None)
                .ConfigureAwait(false);

            await FollowupAsync(
                    loc.Get("vending.track.ok", ctx.Culture, quantity, itemRecord.Name, price, currencyRecord.Name),
                    ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    private async Task UntrackTargetCommandAsync(string target, string? server)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(MustBeUsedInServer, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var sp = scope.ServiceProvider;
            var loc = sp.GetRequiredService<ILocalizer>();
            var resolved = await ResolveAsync(sp, server).ConfigureAwait(false);
            if (resolved is not { } ctx)
            {
                return;
            }

            var items = sp.GetRequiredService<IItemDatabase>();
            var parsed = ParseTarget(target, items);
            if (parsed is null)
            {
                await FollowupAsync(loc.Get("vending.untrack.notfound", ctx.Culture, target), ephemeral: true)
                    .ConfigureAwait(false);
                return;
            }

            var trackService = sp.GetRequiredService<IVendingTrackService>();
            var removed = parsed.Value.Grid is { } grid
                ? await trackService.UntrackGridAsync(ctx.GuildId, ctx.ServerId, grid, CancellationToken.None)
                    .ConfigureAwait(false)
                : await trackService
                    .UntrackListingAsync(ctx.GuildId, ctx.ServerId, parsed.Value.Listing!.Value, CancellationToken.None)
                    .ConfigureAwait(false);

            var key = removed ? "vending.untrack.ok" : "vending.untrack.notfound";
            await FollowupAsync(loc.Get(key, ctx.Culture, parsed.Value.Display), ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    private async Task ShowTrackedCommandAsync(string? server)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(MustBeUsedInServer, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var sp = scope.ServiceProvider;
            var loc = sp.GetRequiredService<ILocalizer>();
            var resolved = await ResolveAsync(sp, server).ConfigureAwait(false);
            if (resolved is not { } ctx)
            {
                return;
            }

            var trackService = sp.GetRequiredService<IVendingTrackService>();
            var items = sp.GetRequiredService<IItemDatabase>();
            var summary = await trackService.GetTrackedAsync(ctx.GuildId, ctx.ServerId, CancellationToken.None)
                .ConfigureAwait(false);

            var builder = new EmbedBuilder().WithTitle(loc.Get("vending.tracked.title", ctx.Culture));
            if (summary.Grids.Count == 0 && summary.Listings.Count == 0)
            {
                builder.WithDescription(loc.Get("vending.tracked.none", ctx.Culture));
            }
            else
            {
                if (summary.Grids.Count > 0)
                {
                    builder.AddField(loc.Get("vending.tracked.grids", ctx.Culture),
                        Fit(summary.Grids, ", ", loc, ctx.Culture));
                }

                if (summary.Listings.Count > 0)
                {
                    IReadOnlyList<string> lines =
                        [.. summary.Listings.Select(listing => FormatListing(listing, items, loc, ctx.Culture))];
                    builder.AddField(loc.Get("vending.tracked.listings", ctx.Culture),
                        Fit(lines, "\n", loc, ctx.Culture));
                }
            }

            await FollowupAsync(ephemeral: true, embed: builder.Build()).ConfigureAwait(false);
        }
    }

    /// <summary>Resolves the target server for this interaction, replying with a localized error on failure.</summary>
    /// <param name="sp">The per-interaction DI scope's service provider.</param>
    /// <param name="server">The raw server argument (a server id string) or null.</param>
    /// <returns>The resolved guild/server/culture, or null when a reply has already been sent.</returns>
    private async Task<ResolvedContext?> ResolveAsync(IServiceProvider sp, string? server)
    {
        var workspace = sp.GetRequiredService<IWorkspaceStore>();
        var resolver = sp.GetRequiredService<ServerResolver>();
        var guildId = Context.Guild!.Id;
        var culture = await workspace.GetCultureAsync(guildId).ConfigureAwait(false);
        var resolution = await resolver.ResolveAsync(guildId, server, culture, CancellationToken.None)
            .ConfigureAwait(false);
        if (resolution.ErrorMessage is { } error)
        {
            await FollowupAsync(error, ephemeral: true).ConfigureAwait(false);
            return null;
        }

        return new ResolvedContext(guildId, resolution.ServerId!.Value, culture);
    }

    /// <summary>Resolves a user query to exactly one item, replying-ready on ambiguous/not-found.</summary>
    /// <param name="items">The item database.</param>
    /// <param name="query">The raw user input.</param>
    /// <param name="loc">The localizer for error text.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="record">The resolved item, when this returns true.</param>
    /// <param name="error">The localized error to show, when this returns false.</param>
    /// <returns>True when exactly one item matched.</returns>
    private static bool TryResolveOne(
        IItemDatabase items,
        string query,
        ILocalizer loc,
        string culture,
        out ItemRecord record,
        out string error)
    {
        switch (items.Resolve(query))
        {
            case ItemMatch.Found found:
                record = found.Item;
                error = "";
                return true;
            case ItemMatch.Ambiguous ambiguous:
                record = null!;
                error = Ambiguous(loc, culture, ambiguous.Candidates.Select(c => c.Name));
                return false;
            default:
                record = null!;
                error = NotFound(loc, culture, query);
                return false;
        }
    }

    private static string Ambiguous(ILocalizer loc, string culture, IEnumerable<string> candidates) =>
        loc.Get(AmbiguousKey, culture, string.Join(", ", candidates));

    private static string NotFound(ILocalizer loc, string culture, string query) =>
        loc.Get(NotFoundKey, culture, query);

    /// <summary>Formats one manually tracked listing as quantity, item name, cost, and currency name.</summary>
    /// <param name="listing">The listing to format.</param>
    /// <param name="items">The item database, for name resolution.</param>
    /// <param name="loc">The localizer, for the blueprint-item indicator.</param>
    /// <param name="culture">The guild culture.</param>
    private static string FormatListing(TrackedListing listing, IItemDatabase items, ILocalizer loc, string culture) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{listing.Quantity} x {ItemDisplayName(items, loc, culture, listing.Key.ItemId, listing.Key.ItemIsBlueprint)} — " +
            $"{listing.CostPerOrder} {ItemName(items, listing.Key.CurrencyId)}");

    private static string ItemName(IItemDatabase items, int itemId) =>
        items.GetById(itemId)?.Name ?? itemId.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// An item's display name, marked as a blueprint when that is what the listing sells. The rule
    /// itself lives in <see cref="ListingDisplay"/> so every surface tells the two apart the same way;
    /// currency is never a blueprint by ruling (see <see cref="TrackListingCommandAsync"/>), so only the
    /// item side goes through it.
    /// </summary>
    /// <param name="items">The item database, for name resolution.</param>
    /// <param name="loc">The localizer, for the blueprint indicator text.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="itemId">The Rust item id.</param>
    /// <param name="isBlueprint">True when this listing is for the item's blueprint.</param>
    private static string ItemDisplayName(
        IItemDatabase items,
        ILocalizer loc,
        string culture,
        int itemId,
        bool isBlueprint) =>
        ListingDisplay.MarkBlueprint(ItemName(items, itemId), isBlueprint, loc, culture);

    /// <summary>
    /// Joins parts into one embed field value that Discord will accept. A field value over 1024
    /// characters is rejected outright — and registering thirty listings, which is precisely the
    /// workflow /vending-track exists for, crosses that line — so drop whole parts from the end until it
    /// fits and say how many went. Mirrors the roster renderer's approach, for the same reason.
    /// </summary>
    /// <param name="parts">The rendered parts, in display order.</param>
    /// <param name="separator">The separator to join them with.</param>
    /// <param name="loc">The localizer, for the omission notice.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>A value of at most <see cref="FieldValueLimit"/> characters.</returns>
    internal static string Fit(IReadOnlyList<string> parts, string separator, ILocalizer loc, string culture)
    {
        ArgumentNullException.ThrowIfNull(parts);
        ArgumentNullException.ThrowIfNull(loc);

        var full = string.Join(separator, parts);
        if (full.Length <= FieldValueLimit)
        {
            return full;
        }

        for (var kept = parts.Count - 1; kept > 0; kept--)
        {
            var candidate = string.Join(separator, parts.Take(kept)) + separator
                                                                     + Omitted(loc, culture, parts.Count - kept);
            if (candidate.Length <= FieldValueLimit)
            {
                return candidate;
            }
        }

        // Even a single part does not fit: say nothing but the count, which always does.
        return Omitted(loc, culture, parts.Count);
    }

    private static string Omitted(ILocalizer loc, string culture, int count) =>
        loc.Get("vending.tracked.omitted", culture, count);

    /// <summary>
    /// Parses an autocompleted /vending-untrack target back into a grid reference or listing key. The
    /// value always comes from <see cref="VendingUntrackAutocompleteHandler"/>'s choice list; see its
    /// docs for the exact encoding.
    /// </summary>
    /// <param name="target">The raw target argument.</param>
    /// <param name="items">The item database, for the display name.</param>
    /// <returns>The parsed target, or null when it matches neither known shape.</returns>
    private static ParsedTarget? ParseTarget(string target, IItemDatabase items)
    {
        var parts = target.Split(':');
        if (parts.Length == 2 && parts[0] == "grid" && parts[1].Length > 0)
        {
            return new ParsedTarget(parts[1], null, parts[1]);
        }

        if (parts.Length == 5 && parts[0] == "listing"
                              && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                  out var itemId)
                              && bool.TryParse(parts[2], out var itemIsBlueprint)
                              && int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture,
                                  out var currencyId)
                              && bool.TryParse(parts[4], out var currencyIsBlueprint))
        {
            var key = new ListingKey(itemId, itemIsBlueprint, currencyId, currencyIsBlueprint);
            var display = string.Create(CultureInfo.InvariantCulture,
                $"{ItemName(items, itemId)} / {ItemName(items, currencyId)}");
            return new ParsedTarget(null, key, display);
        }

        return null;
    }

    private readonly record struct ResolvedContext(ulong GuildId, Guid ServerId, string Culture);

    private readonly record struct ParsedTarget(string? Grid, ListingKey? Listing, string Display);
}
