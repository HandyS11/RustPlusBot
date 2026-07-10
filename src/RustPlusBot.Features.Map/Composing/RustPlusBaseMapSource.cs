using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Fallback base-map source: the JPEG tile served by the Rust+ server itself.</summary>
/// <param name="query">The live query seam.</param>
public sealed class RustPlusBaseMapSource(IRustServerQuery query) : IBaseMapSource
{
    /// <inheritdoc />
    public async Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var bytes = await query.GetMapImageAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        var dims = await query.GetMapDimensionsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (dims is null)
        {
            return null;
        }

        return new BaseMapImage(bytes, (int)dims.Width, (int)dims.Height, dims.OceanMargin);
    }
}
