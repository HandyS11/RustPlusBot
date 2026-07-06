namespace RustPlusBot.Features.Commands.Tests;

public sealed class CommandOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new CommandOptions();
        Assert.Equal(TimeSpan.FromSeconds(4), options.Cooldown);
    }
}
