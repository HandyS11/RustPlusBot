using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class DecayUpkeepHandlersTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly ILocalizer _loc = new ResxLocalizer();

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Decay_name_is_decay()
        => Assert.Equal("decay", new DecayCommandHandler(_db, _loc).Name);

    [Fact]
    public async Task Decay_Found_returnsDecayLine()
    {
        var reply = await new DecayCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Stone Barricade"), CancellationToken.None);
        Assert.Contains("Stone Barricade", reply, StringComparison.Ordinal);
        Assert.Contains("15m", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decay_NoDecayData_returnsNone()
    {
        // "Assault Rifle" (id 1545779598) is in the bundle with null decay.
        var reply = await new DecayCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Assault Rifle"), CancellationToken.None);
        Assert.Contains("Assault Rifle", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decay_NotFound_returnsMessage()
    {
        var reply = await new DecayCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decay_French_returnsFrenchNone()
    {
        var fr = new CommandContext(1, Guid.NewGuid(), "fr", 99, "Caller", ["Assault Rifle"]);
        var reply = await new DecayCommandHandler(_db, _loc).ExecuteAsync(fr, CancellationToken.None);
        Assert.Contains("dégrade", reply, StringComparison.Ordinal);
    }
}
