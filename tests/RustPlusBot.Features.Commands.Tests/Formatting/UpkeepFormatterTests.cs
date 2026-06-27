using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class UpkeepFormatterTests
{
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void UpkeepLine_RendersRange()
    {
        var item = new ItemRecord(1, "Wooden Door", 1, null, null, null, null, null,
            new UpkeepCost([new UpkeepEntry(-151838493, 8, 25)]));
        var line = UpkeepLine.Format(item, _names);
        Assert.Contains("Wooden Door", line, StringComparison.Ordinal);
        Assert.Contains("8–25", line, StringComparison.Ordinal); // 8–25
        Assert.Contains("Wood", line, StringComparison.Ordinal);
    }

    [Fact]
    public void UpkeepLine_RendersSingleQuantityWithoutRange()
    {
        var item = new ItemRecord(1, "Reinforced Glass Window", 1, null, null, null, null, null,
            new UpkeepCost([new UpkeepEntry(317398316, 1, 1)]));
        var line = UpkeepLine.Format(item, _names);
        Assert.DoesNotContain("–", line, StringComparison.Ordinal); // no en-dash for a single value
        Assert.Contains("1", line, StringComparison.Ordinal);
    }
}
