using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Map;

namespace RustPlusBot.Persistence.Map;

/// <summary>EF-backed <see cref="IMapSettingsStore"/>.</summary>
/// <param name="context">The bot database context.</param>
public sealed class MapSettingsStore(BotDbContext context) : IMapSettingsStore
{
    /// <inheritdoc />
    public async Task<MapLayerSettings> GetAsync(ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var row = await context.ServerMapSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? MapLayerSettings.AllOn
            : new MapLayerSettings(row.ShowGrid, row.ShowMarkers, row.ShowMonuments, row.ShowVendor,
                row.ShowPlayers, row.ShowRigs);
    }

    /// <inheritdoc />
    public async Task SetLayerAsync(ulong guildId,
        Guid serverId,
        MapLayer layer,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.ServerMapSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);

        var row = existing ?? new ServerMapSettings
        {
            GuildId = guildId, ServerId = serverId
        };
        switch (layer)
        {
            case MapLayer.Grid:
                row.ShowGrid = enabled;
                break;
            case MapLayer.Markers:
                row.ShowMarkers = enabled;
                break;
            case MapLayer.Monuments:
                row.ShowMonuments = enabled;
                break;
            case MapLayer.Vendor:
                row.ShowVendor = enabled;
                break;
            case MapLayer.Players:
                row.ShowPlayers = enabled;
                break;
            case MapLayer.Rigs:
                row.ShowRigs = enabled;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown map layer.");
        }

        if (existing is null)
        {
            context.ServerMapSettings.Add(row);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
