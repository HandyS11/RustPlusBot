using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the per-server #map control message: six ManageGuild layer-toggle buttons.</summary>
/// <param name="mapSettings">Per-(guild, server) layer toggles.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class MapControlMessageRenderer(
    IMapSettingsStore mapSettings,
    ILocalizer localizer) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.ServerMap;

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

        var builder = new ComponentBuilder();
        // Discord caps an action row at five buttons, so split the six toggles across two rows.
        AddToggle(builder, MapLayer.Grid, "map.layer.grid", settings.Grid, serverId, context.Culture, row: 0);
        AddToggle(builder, MapLayer.Markers, "map.layer.markers", settings.Markers, serverId, context.Culture, row: 0);
        AddToggle(builder, MapLayer.Monuments, "map.layer.monuments", settings.Monuments, serverId, context.Culture,
            row: 0);
        AddToggle(builder, MapLayer.Vendor, "map.layer.vendor", settings.Vendor, serverId, context.Culture, row: 1);
        AddToggle(builder, MapLayer.Players, "map.layer.players", settings.Players, serverId, context.Culture, row: 1);
        AddToggle(builder, MapLayer.Rigs, "map.layer.rigs", settings.Rigs, serverId, context.Culture, row: 1);

        var header = localizer.Get("map.control.header", context.Culture);
        return new MessagePayload(header, null, builder.Build());
    }

    private void AddToggle(ComponentBuilder builder,
        MapLayer layer,
        string labelKey,
        bool enabled,
        Guid serverId,
        string culture,
        int row) =>
        builder.WithButton(
            localizer.Get(labelKey, culture),
            $"{WorkspaceComponentIds.MapTogglePrefix}{layer}:{serverId}",
            enabled ? ButtonStyle.Success : ButtonStyle.Secondary,
            row: row);
}
