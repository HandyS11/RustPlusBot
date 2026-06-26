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
        var reply = await new ItemCommandHandler(_db, _loc).ExecuteAsync(Ctx("AK-47"), CancellationToken.None);
        Assert.Contains("AK-47", reply, StringComparison.Ordinal);
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
}
