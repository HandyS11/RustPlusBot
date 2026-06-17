using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class RigCommandHandlersTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    private static CommandContext Ctx() => new(Guild, Server, "en", 0UL, string.Empty, []);

    private static ICommandLocalizer RealLocalizer() =>
        new CommandLocalizer(CommandLocalizationCatalog.Default);

    [Fact]
    public async Task Small_reports_online_when_untracked()
    {
        var rig = Substitute.For<IRigState>();
        rig.Get(Guild, Server, RigKind.Small).Returns(new RigState(RigStatus.Online, null));
        var handler = new SmallCommandHandler(rig, RealLocalizer());

        var reply = await handler.ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.Equal("small", handler.Name);
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    [Fact]
    public async Task Large_reports_active_with_remaining()
    {
        var rig = Substitute.For<IRigState>();
        rig.Get(Guild, Server, RigKind.Large)
            .Returns(new RigState(RigStatus.Active, TimeSpan.FromMinutes(8)));
        var handler = new LargeCommandHandler(rig, RealLocalizer());

        var reply = await handler.ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.Equal("large", handler.Name);
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    [Fact]
    public async Task Small_reports_offline_with_remaining()
    {
        var rig = Substitute.For<IRigState>();
        rig.Get(Guild, Server, RigKind.Small)
            .Returns(new RigState(RigStatus.Offline, TimeSpan.FromMinutes(6)));
        var handler = new SmallCommandHandler(rig, RealLocalizer());

        var reply = await handler.ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }
}
