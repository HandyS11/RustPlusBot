using RustPlusBot.Features.Commands.Dispatching;

namespace RustPlusBot.Features.Commands.Tests.Dispatching;

public sealed class CommandLineTests
{
    private static readonly string[] NowHere = ["now", "here"];
    private static readonly string[] AB = ["a", "b"];

    [Fact]
    public void Parses_NameAndArgs()
    {
        var parsed = CommandLine.TryParse("!", "!pop now here", out var result);
        Assert.True(parsed);
        Assert.Equal("pop", result.Name);
        Assert.Equal(NowHere, result.Args);
    }

    [Fact]
    public void NameIsLowercased()
    {
        Assert.True(CommandLine.TryParse("!", "!POP", out var result));
        Assert.Equal("pop", result.Name);
    }

    [Theory]
    [InlineData("pop")]
    [InlineData("!")]
    [InlineData("!   ")]
    [InlineData("")]
    [InlineData("  ")]
    public void Rejects_NonCommands(string line)
    {
        Assert.False(CommandLine.TryParse("!", line, out _));
    }

    [Fact]
    public void TrimsAndCollapsesWhitespace()
    {
        Assert.True(CommandLine.TryParse("!", "  !time   a    b ", out var result));
        Assert.Equal("time", result.Name);
        Assert.Equal(AB, result.Args);
    }

    [Fact]
    public void Honors_CustomPrefix()
    {
        Assert.True(CommandLine.TryParse(".", ".pop", out var result));
        Assert.Equal("pop", result.Name);
        Assert.False(CommandLine.TryParse(".", "!pop", out _));
    }
}
