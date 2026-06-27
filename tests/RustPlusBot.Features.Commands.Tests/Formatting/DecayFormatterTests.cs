using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class DecayFormatterTests
{
    [Fact]
    public void DecayLine_ShowsBaseDecayAndHp()
    {
        var item = new ItemRecord(1, "Stone Barricade", 1, null, null, null, null,
            new DecayInfo(900, null, null, null, 100), null);
        var line = DecayLine.Format(item);
        Assert.Contains("Stone Barricade", line, StringComparison.Ordinal);
        Assert.Contains("15m", line, StringComparison.Ordinal);
        Assert.Contains("100", line, StringComparison.Ordinal);
    }

    [Fact]
    public void DecayLine_ShowsVariantsWhenPresent()
    {
        var item = new ItemRecord(1, "Wall", 1, null, null, null, null,
            new DecayInfo(null, 3600, 7200, null, 250), null);
        var line = DecayLine.Format(item);
        Assert.Contains("outside", line, StringComparison.Ordinal);
        Assert.Contains("inside", line, StringComparison.Ordinal);
        Assert.Contains("250", line, StringComparison.Ordinal);
    }

    [Fact]
    public void DecayLine_ShowsDashAndHpWhenNoDecaySeconds()
    {
        var item = new ItemRecord(1, "Thing", 1, null, null, null, null,
            new DecayInfo(null, null, null, null, 100), null);
        var line = DecayLine.Format(item);
        Assert.Contains("—", line, StringComparison.Ordinal);
        Assert.Contains("100", line, StringComparison.Ordinal);
    }
}
