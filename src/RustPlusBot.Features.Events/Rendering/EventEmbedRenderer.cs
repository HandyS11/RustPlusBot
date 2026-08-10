using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Events.Rendering;

/// <summary>Renders one <see cref="RustMapEvent"/> as a Discord embed.</summary>
/// <param name="localizer">The reply localizer.</param>
internal sealed class EventEmbedRenderer(ILocalizer localizer)
{
    /// <summary>Renders the event for a guild culture.</summary>
    /// <param name="evt">The event to render.</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <param name="gridStyle">Which grid convention the reference uses.</param>
    /// <returns>The built embed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The event kind is not a supported <see cref="MapEventKind"/>.</exception>
    public Embed Render(RustMapEvent evt, string culture, MapGridStyle gridStyle = MapGridStyle.InGame)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var (key, suffix, text) = Locate(evt, culture, gridStyle);

        return new EmbedBuilder()
            .WithAuthor(localizer.Get("event.title", culture))
            .WithDescription(localizer.Get(key + suffix, culture, text))
            .WithTimestamp(evt.AtUtc)
            .Build();
    }

    /// <summary>Renders a rig boundary event as a Discord embed.</summary>
    /// <param name="evt">The rig event.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="gridStyle">Which grid convention the reference uses.</param>
    /// <returns>The built embed.</returns>
    public Embed RenderRig(RigStateChangedEvent evt, string culture, MapGridStyle gridStyle = MapGridStyle.InGame)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var grid = GridReference.From(evt.X, evt.Y, evt.Dimensions, gridStyle);
        return new EmbedBuilder()
            .WithAuthor(localizer.Get("event.title", culture))
            .WithDescription(localizer.Get(RigKey(evt), culture, grid))
            .Build();
    }

    /// <summary>Renders the in-game team-chat line for a rig boundary event.</summary>
    /// <param name="evt">The rig event.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="gridStyle">Which grid convention the reference uses.</param>
    /// <returns>The line text.</returns>
    public string RenderRigLine(RigStateChangedEvent evt,
        string culture,
        MapGridStyle gridStyle = MapGridStyle.InGame)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var grid = GridReference.From(evt.X, evt.Y, evt.Dimensions, gridStyle);
        return localizer.Get(RigKey(evt) + ".line", culture, grid);
    }

    /// <summary>Renders the in-game team-chat line for a cargo/heli/chinook event.</summary>
    /// <param name="evt">The map event.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="gridStyle">Which grid convention the reference uses.</param>
    /// <returns>The line text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The event kind is not a supported <see cref="MapEventKind"/>.</exception>
    public string RenderLine(RustMapEvent evt, string culture, MapGridStyle gridStyle = MapGridStyle.InGame)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var (key, suffix, text) = Locate(evt, culture, gridStyle);
        return localizer.Get(key + ".line" + suffix, culture, text);
    }

    /// <summary>
    /// Departures report the direction the marker headed, which outlives the cell it was last seen in;
    /// a crash always happened inside the map, so it reports a cell. Everything else prefers a cell and
    /// falls back to a direction only when the marker is outside the world. The ".dir" suffix picks the
    /// matching message wording — with no map dimensions the location is raw coordinates, IsDirection is
    /// false, and the plain key keeps today's text.
    /// </summary>
    /// <param name="evt">The map event.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="style">Which grid convention the reference uses.</param>
    /// <returns>The base localization key, the ".dir" suffix (or empty), and the location text to interpolate.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The event kind is not a supported <see cref="MapEventKind"/>.</exception>
    private (string Key, string Suffix, string Text) Locate(RustMapEvent evt, string culture, MapGridStyle style)
    {
        var location = evt.Kind switch
        {
            MapEventKind.CargoLeft or MapEventKind.HeliLeft =>
                MapLocation.DescribeDirection(localizer, culture, evt.X, evt.Y, evt.Dimensions),
            MapEventKind.HeliCrashed =>
                new MapLocationText(false, GridReference.From(evt.X, evt.Y, evt.Dimensions, style)),
            _ => MapLocation.Describe(localizer, culture, evt.X, evt.Y, evt.Dimensions, style),
        };

        var key = evt.Kind switch
        {
            MapEventKind.CargoEntered => "event.cargo.entered",
            MapEventKind.CargoLeft => "event.cargo.left",
            MapEventKind.HeliEntered => "event.heli.entered",
            MapEventKind.HeliLeft => "event.heli.left",
            MapEventKind.HeliCrashed => "event.heli.crashed",
            MapEventKind.ChinookSpawned => "event.chinook.spawned",
            _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.Kind, "Unsupported map event kind."),
        };

        return (key, location.IsDirection ? ".dir" : string.Empty, location.Text);
    }

    private static string RigKey(RigStateChangedEvent evt)
    {
        var rig = evt.Rig == RigKind.Small ? "small" : "large";
        var phase = evt.Kind switch
        {
            RigEventKind.Activated => "activated",
            RigEventKind.CrateLootable => "lootable",
            RigEventKind.Respawned => "respawned",
            _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.Kind, "Unsupported rig event kind."),
        };
        return $"event.rig.{rig}.{phase}";
    }
}
