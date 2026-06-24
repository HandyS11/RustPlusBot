using NSubstitute;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class MuteHandlersTests
{
    private static readonly ILocalizer Loc = new ResxLocalizer();

    private static CommandContext Ctx(string culture = "en") =>
        new(1, Guid.NewGuid(), culture, 7, "alice", []);

    [Fact]
    public async Task Mute_SetsMutedTrue_AndConfirms()
    {
        var store = Substitute.For<IMuteStore>();
        var handler = new MuteCommandHandler(store, Loc);
        var ctx = Ctx();
        var reply = await handler.ExecuteAsync(ctx, CancellationToken.None);
        await store.Received(1).SetMutedAsync(ctx.GuildId, ctx.ServerId, true, Arg.Any<CancellationToken>());
        Assert.Equal("Bot muted.", reply);
        Assert.Equal("mute", handler.Name);
    }

    [Fact]
    public async Task Unmute_SetsMutedFalse_AndConfirms_InFrench()
    {
        var store = Substitute.For<IMuteStore>();
        var handler = new UnmuteCommandHandler(store, Loc);
        var ctx = Ctx("fr");
        var reply = await handler.ExecuteAsync(ctx, CancellationToken.None);
        await store.Received(1).SetMutedAsync(ctx.GuildId, ctx.ServerId, false, Arg.Any<CancellationToken>());
        Assert.Equal("Bot réactivé.", reply);
        Assert.Equal("unmute", handler.Name);
    }
}
