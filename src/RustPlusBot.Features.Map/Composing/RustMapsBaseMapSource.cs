using Microsoft.Extensions.Logging;
using RustMapsApi.V4;
using RustPlusBot.Abstractions.Connections;
using SixLabors.ImageSharp;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>
/// Preferred base-map source: the RustMaps procedural render (clean terrain, no baked icons),
/// resolved by the server's world size + seed. Any failure returns null so the chain falls
/// through to the Rust+ JPEG — custom/unpublished maps must still render.
/// </summary>
/// <param name="client">The RustMaps API client.</param>
/// <param name="query">The live query seam (world size + seed).</param>
/// <param name="httpClientFactory">Creates the image-download client.</param>
/// <param name="logger">Logs fall-through causes.</param>
public sealed partial class RustMapsBaseMapSource(
    IRustMapsClient client,
    IRustServerQuery query,
    IHttpClientFactory httpClientFactory,
    ILogger<RustMapsBaseMapSource> logger) : IBaseMapSource
{
    /// <summary>Named HTTP client used to download the rendered image.</summary>
    public const string HttpClientName = "RustMapsImages";

    /// <inheritdoc />
    public async Task<BaseMapImage?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        try
        {
            var world = await query.GetWorldAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (world is null)
            {
                return null;
            }

            var result = await client.GetMapBySeedAndSizeAsync(
                    (int)world.WorldSize, (int)world.Seed, staging: false, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess || result.Data?.RawImageUrl is not { } imageUrl)
            {
                LogRustMapsUnavailable(logger, (int)world.WorldSize, (int)world.Seed, result.StatusCode);
                return null;
            }

            var http = httpClientFactory.CreateClient(HttpClientName);
            var bytes = await http.GetByteArrayAsync(new Uri(imageUrl), cancellationToken).ConfigureAwait(false);
            var info = Image.Identify(bytes);
            // RustMaps raw renders span the playable world edge-to-edge (no ocean border).
            // VERIFY on the first live fetch via tools/RustPlusBot.MapParity; adjust here if wrong.
            return new BaseMapImage(bytes, info.Width, info.Height, OceanMarginPx: 0);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: any RustMaps failure falls through to the Rust+ JPEG source.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRustMapsFailed(logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message =
            "RustMaps has no render for size {Size} seed {Seed} (status {StatusCode}); falling back to the Rust+ map tile.")]
    private static partial void LogRustMapsUnavailable(ILogger logger, int size, int seed, int statusCode);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RustMaps base-map fetch failed; falling back to the Rust+ map tile.")]
    private static partial void LogRustMapsFailed(ILogger logger, Exception exception);
}
