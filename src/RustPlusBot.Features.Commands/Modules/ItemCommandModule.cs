using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>The /item, /recycle, /craft, /research, /decay, /upkeep, /durability, and /smelt slash commands.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class ItemCommandModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string MustBeUsedInServer = "This command must be used in a server.";
    private const string AmbiguousKey = "command.item.ambiguous";
    private const string NotFoundKey = "command.item.notfound";

    /// <summary>Looks up an item's name, id, stack size, and despawn time.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("item", "Look up an item")]
    public Task ItemAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.NamesAsOf, (_, _, rec, loc, culture) =>
            loc.Get("command.item.ok", culture, ItemLine.Format(rec)));

    /// <summary>Shows recycler output for an item.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("recycle", "Show recycler output for an item")]
    public Task RecycleAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.RecycleAsOf, (_, names, rec, loc, culture) => rec.Recycle is not null
            ? loc.Get("command.recycle.ok", culture, RecycleLine.Format(rec, names))
            : loc.Get("command.recycle.none", culture, rec.Name));

    /// <summary>Shows an item's craft recipe.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("craft", "Show an item's craft recipe")]
    public Task CraftAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.CraftAsOf, (_, names, rec, loc, culture) => rec.Craft is not null
            ? loc.Get("command.craft.ok", culture, CraftLine.Format(rec, names))
            : loc.Get("command.craft.none", culture, rec.Name));

    /// <summary>Shows an item's research scrap cost.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("research", "Show an item's research scrap cost")]
    public Task ResearchAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.ResearchAsOf, (_, _, rec, loc, culture) => rec.Research is not null
            ? loc.Get("command.research.ok", culture, ResearchLine.Format(rec))
            : loc.Get("command.research.none", culture, rec.Name));

    /// <summary>Shows an item's decay time.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("decay", "Show an item's decay time")]
    public Task DecayAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.DecayAsOf, (_, _, rec, loc, culture) => rec.Decay is not null
            ? loc.Get("command.decay.ok", culture, DecayLine.Format(rec))
            : loc.Get("command.decay.none", culture, rec.Name));

    /// <summary>Shows a building block's upkeep cost.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("upkeep", "Show a building block's upkeep cost")]
    public Task UpkeepAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.UpkeepAsOf, (_, names, rec, loc, culture) => rec.Upkeep is not null
            ? loc.Get("command.upkeep.ok", culture, UpkeepLine.Format(rec, names))
            : loc.Get("command.upkeep.none", culture, rec.Name));

    /// <summary>Lists the explosives needed to destroy a target.</summary>
    /// <param name="target">The item, building block, or vehicle name.</param>
    [SlashCommand("durability", "Show the explosives needed to destroy a target")]
    public Task DurabilityAsync([Summary("target", "Item, wall/door, or vehicle name")] string target) =>
        RespondForRaidAsync(target);

    /// <summary>Shows what a smelter converts and its fuel/time cost.</summary>
    /// <param name="smelter">The smelter name or id.</param>
    [SlashCommand("smelt", "Show what a smelter converts and its fuel/time cost")]
    public Task SmeltAsync([Summary("smelter", "Furnace, Camp Fire, Electric Furnace, …")] string smelter) =>
        RespondForSmeltAsync(smelter);

    /// <summary>Shows a monument's Computer Station CCTV camera codes.</summary>
    /// <param name="monument">The monument to look up.</param>
    [SlashCommand("cctv", "Show the CCTV camera codes for a monument")]
    public Task CctvAsync(
        [Summary("monument", "The monument to look up")]
        [Choice("Abandoned Military Base", "Abandoned Military Base")]
        [Choice("Airfield", "Airfield")]
        [Choice("Bandit Camp", "Bandit Camp")]
        [Choice("Dome", "Dome")]
        [Choice("Large Oil Rig", "Large Oil Rig")]
        [Choice("Missile Silo", "Missile Silo")]
        [Choice("Outpost", "Outpost")]
        [Choice("Small Oil Rig", "Small Oil Rig")]
        [Choice("Underwater Labs", "Underwater Labs")]
        [Choice("Cargo Ship", "Cargo Ship")]
        [Choice("Ferry Terminal", "Ferry Terminal")]
        string monument) => RespondForCctvAsync(monument);

    private async Task RespondWithEmbedAsync(
        Func<IItemDatabase, IItemNameResolver, ILocalizer, string, string> describe,
        Func<IItemDatabase, DateOnly> asOf)
    {
        if (Context.Guild is null)
        {
            await RespondAsync(MustBeUsedInServer, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<IItemDatabase>();
            var names = scope.ServiceProvider.GetRequiredService<IItemNameResolver>();
            var loc = scope.ServiceProvider.GetRequiredService<ILocalizer>();
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);

            var embed = new EmbedBuilder()
                .WithDescription(describe(db, names, loc, culture))
                .WithFooter($"data as of {asOf(db):yyyy-MM-dd}")
                .Build();
            await RespondAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
        }
    }

    private static string Ambiguous(ILocalizer loc, string culture, IEnumerable<string> candidates) =>
        loc.Get(AmbiguousKey, culture, string.Join(", ", candidates));

    private static string NotFound(ILocalizer loc, string culture, string query) =>
        loc.Get(NotFoundKey, culture, query);

    private Task RespondForAsync(
        string query,
        Func<IItemDatabase, DateOnly> dateSelector,
        Func<IItemDatabase, IItemNameResolver, ItemRecord, ILocalizer, string, string> onFound) =>
        RespondWithEmbedAsync(
            (db, names, loc, culture) => db.Resolve(query) switch
            {
                ItemMatch.Found f => onFound(db, names, f.Item, loc, culture),
                ItemMatch.Ambiguous a => Ambiguous(loc, culture, a.Candidates.Select(c => c.Name)),
                _ => NotFound(loc, culture, query),
            },
            dateSelector);

    private Task RespondForRaidAsync(string query) =>
        RespondWithEmbedAsync(
            (db, names, loc, culture) => db.ResolveRaidTarget(query) switch
            {
                RaidMatch.Found f => loc.Get("command.durability.ok", culture, DurabilityLine.Format(f.Target, names)),
                RaidMatch.Ambiguous a => Ambiguous(loc, culture, a.Candidates.Select(c => c.Name)),
                _ => NotFound(loc, culture, query),
            },
            db => db.Sources.DurabilityAsOf);

    private Task RespondForSmeltAsync(string query) =>
        RespondWithEmbedAsync(
            (db, names, loc, culture) => db.ResolveSmelter(query) switch
            {
                SmeltMatch.Found f => loc.Get("command.smelt.ok", culture, SmeltLine.Format(f.Smelter, names)),
                SmeltMatch.Ambiguous a => Ambiguous(loc, culture, a.Candidates.Select(c => c.Name)),
                _ => NotFound(loc, culture, query),
            },
            db => db.Sources.SmeltingAsOf);

    private Task RespondForCctvAsync(string query) =>
        RespondWithEmbedAsync(
            (db, _, loc, culture) => db.ResolveCctv(query) switch
            {
                CctvMatch.Found f => RenderEmbed(f.Monument, loc, culture),
                CctvMatch.Ambiguous a => Ambiguous(loc, culture, a.Candidates.Select(c => c.Name)),
                _ => NotFound(loc, culture, query),
            },
            db => db.Sources.CctvAsOf);

    /// <summary>
    /// Discord-only presentation: fence the codes so wildcard asterisks render literally and the
    /// codes are copy-clean; the localized wildcard note rides outside the fence as prose.
    /// </summary>
    /// <param name="monument">The resolved CCTV monument.</param>
    /// <param name="loc">The localizer for string resources.</param>
    /// <param name="culture">The guild culture code.</param>
    private static string RenderEmbed(CctvMonument monument, ILocalizer loc, string culture)
    {
        var fenced = loc.Get("command.cctv.ok", culture,
            $"```\n{CctvLine.Format(monument)}\n```");
        return monument.Dynamic
            ? $"{fenced}\n\n{loc.Get("command.cctv.note", culture)}"
            : fenced;
    }
}
