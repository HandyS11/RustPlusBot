using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class ItemCommandHandlersTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly ILocalizer _loc = new ResxLocalizer();
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Names_areCorrect()
    {
        Assert.Equal("item", new ItemCommandHandler(_db, _loc).Name);
        Assert.Equal("recycle", new RecycleCommandHandler(_db, _names, _loc).Name);
        Assert.Equal("craft", new CraftCommandHandler(_db, _names, _loc).Name);
        Assert.Equal("research", new ResearchCommandHandler(_db, _loc).Name);
    }

    [Fact]
    public async Task Item_Found_returnsCard()
    {
        var reply = await new ItemCommandHandler(_db, _loc).ExecuteAsync(Ctx("Assault Rifle"), CancellationToken.None);
        Assert.Contains("Assault Rifle", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Item_NotFound_returnsMessage()
    {
        var reply = await new ItemCommandHandler(_db, _loc).ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recycle_NotRecyclable_returnsNone()
    {
        // "Wood" is in the seed with null recycle.
        var reply = await new RecycleCommandHandler(_db, _names, _loc).ExecuteAsync(Ctx("Wood"),
            CancellationToken.None);
        Assert.Contains("Wood", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_args_returnsNotFound()
    {
        var reply = await new ItemCommandHandler(_db, _loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.NotNull(reply);
    }

    // --- Craft ---

    [Fact]
    public async Task Craft_HasRecipe_returnsIngredients()
    {
        // "Assault Rifle" is craftable; reply should contain item name and ingredient names.
        var reply = await new CraftCommandHandler(_db, _names, _loc).ExecuteAsync(
            Ctx("Assault Rifle"), CancellationToken.None);
        Assert.Contains("Assault Rifle", reply, StringComparison.Ordinal);
        Assert.Contains("High Quality Metal", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Craft_NotCraftable_returnsNoneMessage()
    {
        // "Wood" has no craft recipe.
        var reply = await new CraftCommandHandler(_db, _names, _loc).ExecuteAsync(
            Ctx("Wood"), CancellationToken.None);
        Assert.Contains("Wood", reply, StringComparison.Ordinal);
        Assert.Contains("not craftable", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Craft_NotFound_returnsNotFoundMessage()
    {
        var reply = await new CraftCommandHandler(_db, _names, _loc).ExecuteAsync(
            Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }

    // --- Research ---

    [Fact]
    public async Task Research_HasCost_returnsScrapAmount()
    {
        // "Assault Rifle" costs 500 scrap to research.
        var reply = await new ResearchCommandHandler(_db, _loc).ExecuteAsync(
            Ctx("Assault Rifle"), CancellationToken.None);
        Assert.Contains("Assault Rifle", reply, StringComparison.Ordinal);
        Assert.Contains("500", reply, StringComparison.Ordinal);
        Assert.Contains("scrap", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Research_NotResearchable_returnsNoneMessage()
    {
        // "Wood" has no research cost.
        var reply = await new ResearchCommandHandler(_db, _loc).ExecuteAsync(
            Ctx("Wood"), CancellationToken.None);
        Assert.Contains("Wood", reply, StringComparison.Ordinal);
        Assert.Contains("cannot be researched", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Research_NotFound_returnsNotFoundMessage()
    {
        var reply = await new ResearchCommandHandler(_db, _loc).ExecuteAsync(
            Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }
}
