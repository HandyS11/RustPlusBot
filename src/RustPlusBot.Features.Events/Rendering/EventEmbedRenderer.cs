using Discord;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Formatting;

namespace RustPlusBot.Features.Events.Rendering;

/// <summary>Renders one <see cref="RustMapEvent"/> as a Discord embed.</summary>
/// <param name="localizer">The reply localizer.</param>
internal sealed class EventEmbedRenderer(IEventLocalizer localizer)
{
    /// <summary>Renders the event for a guild culture.</summary>
    /// <param name="evt">The event to render.</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <returns>The built embed.</returns>
    public Embed Render(RustMapEvent evt, string culture)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var grid = GridReference.From(evt.X, evt.Y, evt.Dimensions);
        var key = evt.Kind switch
        {
            MapEventKind.CargoEntered => "event.cargo.entered",
            MapEventKind.CargoLeft => "event.cargo.left",
            MapEventKind.HeliEntered => "event.heli.entered",
            MapEventKind.HeliLeft => "event.heli.left",
            MapEventKind.ChinookSpawned => "event.chinook.spawned",
            _ => "event.chinook.spawned",
        };

        return new EmbedBuilder()
            .WithAuthor(localizer.Get("event.title", culture))
            .WithDescription(localizer.Get(key, culture, grid))
            .WithTimestamp(evt.AtUtc)
            .Build();
    }
}
