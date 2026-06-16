using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands;
using RustPlusBot.Features.Commands.Dispatching;

namespace RustPlusBot.Features.Commands.Tests.Dispatching;

public sealed class CommandCooldownTests
{
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private static CommandCooldown Make(TestClock clock) =>
        new(clock, Options.Create(new CommandOptions { Cooldown = TimeSpan.FromSeconds(4) }));

    [Fact]
    public void FirstUse_IsAllowed()
    {
        Assert.True(Make(new TestClock()).TryConsume(Guid.NewGuid(), "pop"));
    }

    [Fact]
    public void WithinWindow_IsBlocked()
    {
        var clock = new TestClock();
        var cd = Make(clock);
        var server = Guid.NewGuid();
        Assert.True(cd.TryConsume(server, "pop"));
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        Assert.False(cd.TryConsume(server, "pop"));
    }

    [Fact]
    public void AfterWindow_IsAllowed()
    {
        var clock = new TestClock();
        var cd = Make(clock);
        var server = Guid.NewGuid();
        Assert.True(cd.TryConsume(server, "pop"));
        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        Assert.True(cd.TryConsume(server, "pop"));
    }

    [Fact]
    public void DifferentCommand_OrServer_IsIndependent()
    {
        var clock = new TestClock();
        var cd = Make(clock);
        var server = Guid.NewGuid();
        Assert.True(cd.TryConsume(server, "pop"));
        Assert.True(cd.TryConsume(server, "time"));
        Assert.True(cd.TryConsume(Guid.NewGuid(), "pop"));
    }
}
