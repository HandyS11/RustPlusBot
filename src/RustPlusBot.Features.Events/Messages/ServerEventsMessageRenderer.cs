using Discord;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Formatting;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Events.Messages;

/// <summary>
///     Renders the #info events embed: cargo / patrol heli / chinook presence plus both oil rigs'
///     lifecycle phase. Reads only in-process state, so it costs no Rust+ calls.
/// </summary>
/// <param name="events">Active map-marker state.</param>
/// <param name="rigs">Inferred oil-rig state.</param>
/// <param name="mapSettings">Supplies the per-server grid convention.</param>
/// <param name="connections">Live connection status, to avoid reporting cleared state as live.</param>
/// <param name="clock">Supplies the current time for marker ages.</param>
/// <param name="localizer">String resolution.</param>
public sealed class ServerEventsMessageRenderer(
    IEventState events,
    IRigState rigs,
    IMapSettingsStore mapSettings,
    IConnectionStore connections,
    IClock clock,
    ILocalizer localizer) : IMessageRenderer
{
    /// <summary>The message key this renderer produces.</summary>
    public const string Key = "server.events";

    /// <inheritdoc />
    public string MessageKey => Key;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var settings = await mapSettings.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var state = await connections.GetStateAsync(context.GuildId, serverId, cancellationToken)
            .ConfigureAwait(false);
        var connected = state?.Status == ConnectionStatus.Connected;
        var culture = context.Culture;

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.events.title", culture))
            .WithColor(connected ? Color.DarkerGrey : Color.Red)
            // Marker and rig state are in-process and cleared on disconnect, so without this the rows
            // would read as a confident live report of an empty world.
            .WithDescription(connected ? null : localizer.Get("server.events.disconnected", culture))
            .AddField(localizer.Get("server.events.cargo.label", culture),
                Marker(context.GuildId, serverId, MarkerKind.CargoShip, settings.GridStyle, culture), inline: true)
            .AddField(localizer.Get("server.events.heli.label", culture),
                Marker(context.GuildId, serverId, MarkerKind.PatrolHelicopter, settings.GridStyle, culture),
                inline: true)
            .AddField(localizer.Get("server.events.chinook.label", culture),
                Marker(context.GuildId, serverId, MarkerKind.Chinook, settings.GridStyle, culture), inline: true)
            .AddField(localizer.Get("server.events.small.label", culture),
                Rig(context.GuildId, serverId, RigKind.Small, culture), inline: true)
            .AddField(localizer.Get("server.events.large.label", culture),
                Rig(context.GuildId, serverId, RigKind.Large, culture), inline: true);

        return new MessagePayload(null, embed.Build(), null);
    }

    private string Marker(ulong guildId, Guid serverId, MarkerKind kind, MapGridStyle style, string culture)
    {
        var active = events.GetActiveMarkers(guildId, serverId, kind);
        if (active.Count == 0)
        {
            return localizer.Get("server.events.notout", culture);
        }

        // Newest-first: the freshest sighting is the one worth reporting.
        var marker = active[0];
        return localizer.Get("server.events.out", culture,
            GridReference.From(marker.X, marker.Y, marker.Dimensions, style),
            DurationFormat.Compact(clock.UtcNow - marker.SeenAtUtc));
    }

    private string Rig(ulong guildId, Guid serverId, RigKind rig, string culture)
    {
        var state = rigs.Get(guildId, serverId, rig);
        return state.Status switch
        {
            RigStatus.Active => localizer.Get("server.events.rig.active", culture,
                DurationFormat.Compact(state.Remaining ?? TimeSpan.Zero)),
            RigStatus.Offline => localizer.Get("server.events.rig.offline", culture,
                DurationFormat.Compact(state.Remaining ?? TimeSpan.Zero)),
            _ => localizer.Get("server.events.rig.online", culture),
        };
    }
}
