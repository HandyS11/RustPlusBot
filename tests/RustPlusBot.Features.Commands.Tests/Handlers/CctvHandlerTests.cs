using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class CctvHandlerTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly ILocalizer _loc = new ResxLocalizer();

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Name_is_cctv() => Assert.Equal("cctv", new CctvCommandHandler(_db, _loc).Name);

    [Fact]
    public async Task Found_returnsCodes()
    {
        var reply = await new CctvCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Small", "Oil", "Rig"), CancellationToken.None);
        Assert.Contains("Small Oil Rig CCTV:", reply, StringComparison.Ordinal);
        Assert.Contains("OILRIG1HELI", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DynamicMonument_appendsNote()
    {
        var reply = await new CctvCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Underwater", "Labs"), CancellationToken.None);
        Assert.Contains("every map", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonDynamicMonument_hasNoNote()
    {
        var reply = await new CctvCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Dome"), CancellationToken.None);
        Assert.DoesNotContain("every map", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotFound_returnsMessage()
    {
        var reply = await new CctvCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }
}
