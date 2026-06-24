using NSubstitute;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Tests;

public sealed class AfkCommandHandlerTests
{
    private readonly IAfkState _afk = Substitute.For<IAfkState>();
    private readonly ICommandLocalizer _localizer = new CommandLocalizer(CommandLocalizationCatalog.Default);

    private static CommandContext Ctx() => new(1, Guid.NewGuid(), "en", 99, "Caller", []);

    [Fact]
    public async Task Name_is_afk() => Assert.Equal("afk", new AfkCommandHandler(_afk, _localizer).Name);

    [Fact]
    public async Task Reports_not_connected_when_state_null()
    {
        _afk.GetAfkMembersAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<AfkMember>?)null);
        var reply = await new AfkCommandHandler(_afk, _localizer).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal(_localizer.Get("command.notconnected", "en"), reply);
    }

    [Fact]
    public async Task Reports_none_when_empty()
    {
        _afk.GetAfkMembersAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var reply = await new AfkCommandHandler(_afk, _localizer).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal(_localizer.Get("command.afk.none", "en"), reply);
    }

    [Fact]
    public async Task Lists_afk_members_with_durations()
    {
        _afk.GetAfkMembersAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(
            [
                new(1, "Bob", TimeSpan.FromMinutes(6))
            ]);
        var reply = await new AfkCommandHandler(_afk, _localizer).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Contains("Bob", reply, StringComparison.Ordinal);
    }
}
