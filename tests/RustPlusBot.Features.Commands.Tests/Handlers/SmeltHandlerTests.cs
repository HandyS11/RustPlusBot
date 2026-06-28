using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class SmeltHandlerTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly ILocalizer _loc = new ResxLocalizer();
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Name_is_smelt()
        => Assert.Equal("smelt", new SmeltCommandHandler(_db, _names, _loc).Name);

    [Fact]
    public async Task Found_returnsConversions()
    {
        var reply = await new SmeltCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("Furnace"), CancellationToken.None);
        Assert.Contains("Furnace", reply, StringComparison.Ordinal);
        Assert.Contains("Metal Fragments", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_returnsMessage()
    {
        var reply = await new SmeltCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }
}
