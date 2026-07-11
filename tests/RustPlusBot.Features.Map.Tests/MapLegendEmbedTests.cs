using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Posting;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapLegendEmbedTests
{
    [Fact]
    public void Build_returns_null_for_no_legend()
    {
        Assert.Null(MapLegendEmbed.Build(null));
        Assert.Null(MapLegendEmbed.Build(new MapLegend([])));
    }

    [Fact]
    public void Build_lists_one_line_per_player()
    {
        var legend = new MapLegend(
        [
            new MapLegendEntry("🟥", "Ada", "online"),
            new MapLegendEntry("🟦", "Bob", "offline, dead"),
        ]);

        var embed = MapLegendEmbed.Build(legend);

        Assert.NotNull(embed);
        Assert.Contains("Ada", embed!.Description, StringComparison.Ordinal);
        Assert.Contains("online", embed.Description, StringComparison.Ordinal);
        Assert.Contains("Bob", embed.Description, StringComparison.Ordinal);
        Assert.Contains("🟦", embed.Description, StringComparison.Ordinal);
    }
}
