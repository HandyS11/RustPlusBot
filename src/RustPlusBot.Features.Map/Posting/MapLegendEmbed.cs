using System.Globalization;
using System.Text;
using Discord;
using RustPlusBot.Features.Map.Composing;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Builds the Discord legend embed for a rendered map's players (color square, name, status).</summary>
public static class MapLegendEmbed
{
    /// <summary>Builds the legend embed, or null when there is nothing to show.</summary>
    /// <param name="legend">The legend, or null.</param>
    /// <returns>An embed, or null when the legend is null or empty.</returns>
    public static Embed? Build(MapLegend? legend)
    {
        if (legend is null || legend.Entries.Count == 0)
        {
            return null;
        }

        var description = new StringBuilder();
        foreach (var entry in legend.Entries)
        {
            description.Append(CultureInfo.InvariantCulture, $"{entry.Emoji} **{entry.Name}** — {entry.Status}")
                .Append('\n');
        }

        return new EmbedBuilder()
            .WithDescription(description.ToString().TrimEnd('\n'))
            .Build();
    }
}
