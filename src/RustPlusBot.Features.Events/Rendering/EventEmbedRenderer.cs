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
        var grid = GridReference.From(evt.X, evt.Y, evt.Dimensions, gridStyle);
        var key = evt.Kind switch
        {
            MapEventKind.CargoEntered => "event.cargo.entered",
            MapEventKind.CargoLeft => "event.cargo.left",
            MapEventKind.HeliEntered => "event.heli.entered",
            MapEventKind.HeliLeft => "event.heli.left",
            MapEventKind.ChinookSpawned => "event.chinook.spawned",
            _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.Kind, "Unsupported map event kind."),
        };

        return new EmbedBuilder()
            .WithAuthor(localizer.Get("event.title", culture))
            .WithDescription(localizer.Get(key, culture, grid))
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
        var grid = GridReference.From(evt.X, evt.Y, evt.Dimensions, gridStyle);
        var key = evt.Kind switch
        {
            MapEventKind.CargoEntered => "event.cargo.entered.line",
            MapEventKind.CargoLeft => "event.cargo.left.line",
            MapEventKind.HeliEntered => "event.heli.entered.line",
            MapEventKind.HeliLeft => "event.heli.left.line",
            MapEventKind.ChinookSpawned => "event.chinook.spawned.line",
            _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.Kind, "Unsupported map event kind."),
        };
        return localizer.Get(key, culture, grid);
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
