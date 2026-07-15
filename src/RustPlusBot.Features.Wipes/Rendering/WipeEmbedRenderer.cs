using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Wipes.Rendering;

/// <summary>Renders the server-wiped announcement embed posted in #events.</summary>
/// <param name="localizer">String resolution.</param>
internal sealed class WipeEmbedRenderer(ILocalizer localizer)
{
    /// <summary>Renders the announcement for a guild culture.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <returns>The built embed.</returns>
    public Embed Render(ServerWipedEvent evt, string culture)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var builder = new EmbedBuilder()
            .WithTitle(localizer.Get("wipe.title", culture))
            .WithDescription(localizer.Get("wipe.body", culture))
            .WithColor(Color.Orange);

        if (evt.NewWipeTimeUtc is { } wipedAt)
        {
            builder.AddField(localizer.Get("wipe.field.wipedat", culture),
                $"<t:{wipedAt.ToUnixTimeSeconds()}:R>", inline: true);
        }

        builder
            .AddField(localizer.Get("wipe.field.mapsize", culture),
                evt.WorldSize.ToString(CultureInfo.InvariantCulture), inline: true)
            .AddField(localizer.Get("wipe.field.seed", culture),
                evt.Seed.ToString(CultureInfo.InvariantCulture), inline: true);
        return builder.Build();
    }
}
