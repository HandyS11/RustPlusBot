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

/// <summary>The /item, /recycle, /craft, and /research slash commands.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class ItemCommandModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Looks up an item's name, id, stack size, and despawn time.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("item", "Look up an item")]
    public Task ItemAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, (_, _, rec, loc, culture) =>
            loc.Get("command.item.ok", culture, ItemLine.Format(rec)));

    /// <summary>Shows recycler output for an item.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("recycle", "Show recycler output for an item")]
    public Task RecycleAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, (_, names, rec, loc, culture) => rec.Recycle is not null
            ? loc.Get("command.recycle.ok", culture, RecycleLine.Format(rec, names))
            : loc.Get("command.recycle.none", culture, rec.Name));

    /// <summary>Shows an item's craft recipe.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("craft", "Show an item's craft recipe")]
    public Task CraftAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, (_, names, rec, loc, culture) => rec.Craft is not null
            ? loc.Get("command.craft.ok", culture, CraftLine.Format(rec, names))
            : loc.Get("command.craft.none", culture, rec.Name));

    /// <summary>Shows an item's research scrap cost.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("research", "Show an item's research scrap cost")]
    public Task ResearchAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, (_, _, rec, loc, culture) => rec.Research is not null
            ? loc.Get("command.research.ok", culture, ResearchLine.Format(rec))
            : loc.Get("command.research.none", culture, rec.Name));

    private async Task RespondForAsync(
        string query,
        Func<IItemDatabase, IItemNameResolver, ItemRecord, ILocalizer, string, string> onFound)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
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

            var text = db.Resolve(query) switch
            {
                ItemMatch.Found f => onFound(db, names, f.Item, loc, culture),
                ItemMatch.Ambiguous a => loc.Get("command.item.ambiguous", culture,
                    string.Join(", ", a.Candidates.Select(c => c.Name))),
                _ => loc.Get("command.item.notfound", culture, query),
            };

            var embed = new EmbedBuilder()
                .WithDescription(text)
                .WithFooter($"data as of {db.Sources.NamesAsOf:yyyy-MM-dd}")
                .Build();
            await RespondAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
        }
    }
}
