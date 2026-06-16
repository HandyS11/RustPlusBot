using RustPlusBot.Features.Commands;

namespace RustPlusBot.Features.Commands.Tests;

public sealed class CommandOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new CommandOptions();
        Assert.Equal("!", options.DefaultPrefix);
        Assert.True(options.Cooldown > TimeSpan.Zero);
    }
}
