using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class DurabilityHandlerTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly ILocalizer _loc = new ResxLocalizer();
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Name_is_durability()
        => Assert.Equal("durability", new DurabilityCommandHandler(_db, _names, _loc).Name);

    [Fact]
    public async Task Found_returnsRaidCosts()
    {
        var reply = await new DurabilityCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("Stone Wall"), CancellationToken.None);
        Assert.Contains("Stone Wall", reply, StringComparison.Ordinal);
        Assert.Contains("sulfur", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotFound_returnsMessage()
    {
        var reply = await new DurabilityCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }
}
