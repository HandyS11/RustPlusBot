using Microsoft.Extensions.Logging;
using RustMapsApi.Results;
using RustMapsApi.V4;
using RustMapsApi.V4.Models;
using RustMapsApi.V4.Requests;

namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>
/// Advances one RustMaps map key's generation one step: GET → (limits-gated) CreateMap → poll → download.
/// Credit-safe: CreateMap only on a genuine NotFound with confirmed budget, once per key, no retry.
/// </summary>
/// <param name="client">The RustMaps API client.</param>
/// <param name="coordinator">The shared generation state.</param>
/// <param name="httpClientFactory">Creates the image-download client.</param>
/// <param name="logger">The logger.</param>
public sealed partial class RustMapsGenerationDriver(
    IRustMapsClient client,
    IRustMapsMapCoordinator coordinator,
    IHttpClientFactory httpClientFactory,
    ILogger<RustMapsGenerationDriver> logger)
{
    /// <summary>Named HTTP client used to download the RustMaps render.</summary>
    public const string HttpClientName = "RustMapsImages";

    /// <summary>Advances the key one step based on its current state.</summary>
    /// <param name="key">The map key.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the step is done.</returns>
    public async Task AdvanceAsync(RustMapsMapKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        try
        {
            var snapshot = coordinator.Snapshot(key);
            switch (snapshot.State)
            {
                case RustMapsGenerationState.Idle:
                    await StartAsync(key, cancellationToken).ConfigureAwait(false);
                    break;
                case RustMapsGenerationState.Generating:
                    await PollAsync(key, snapshot.MapId, cancellationToken).ConfigureAwait(false);
                    break;
                // Ready / Failed / LimitReached — terminal; nothing to do.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: any RustMaps failure marks the key Failed; the #info fallback still renders.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogAdvanceFailed(logger, ex, key.Size, key.Seed);
            coordinator.SetFailed(key);
        }
    }

    private async Task StartAsync(RustMapsMapKey key, CancellationToken cancellationToken)
    {
        var get = await client.GetMapBySeedAndSizeAsync(key.Size, key.Seed, staging: false, cancellationToken)
            .ConfigureAwait(false);
        if (IsReady(get))
        {
            await SetReadyAsync(key, get.Data!, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (get.Error?.Kind == RustMapsErrorKind.Queued)
        {
            // Already generating (elsewhere/earlier) — poll without spending.
            coordinator.TrySetGenerating(key, mapId: null);
            return;
        }

        if (get.Error?.Kind != RustMapsErrorKind.NotFound)
        {
            // Transient/other GET error (or an unexpected non-ready success): do NOT spend a credit.
            // Leave the key Idle so the free GET simply retries on the next tick.
            return;
        }

        // Genuine miss → pre-check limits (fail closed) before spending a credit.
        var limits = await client.GetLimitsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!limits.IsSuccess || limits.Data is null)
        {
            LogLimitsUnavailable(logger, key.Size, key.Seed);
            coordinator.SetFailed(key);
            return;
        }

        if (IsExhausted(limits.Data.Concurrent) || IsExhausted(limits.Data.Monthly))
        {
            LogLimitReached(logger, key.Size, key.Seed);
            coordinator.SetLimitReached(key);
            return;
        }

        var create = await client.CreateMapAsync(
                new MapGenerationRequest
                {
                    Size = key.Size, Seed = key.Seed, Staging = false
                }, cancellationToken)
            .ConfigureAwait(false);
        if (!create.IsSuccess)
        {
            LogCreateFailed(logger, key.Size, key.Seed, create.StatusCode);
            coordinator.SetFailed(key);
            return;
        }

        coordinator.TrySetGenerating(key, create.Data?.MapId);
    }

    private async Task PollAsync(RustMapsMapKey key, string? mapId, CancellationToken cancellationToken)
    {
        var get = mapId is { } id
            ? await client.GetMapByIdAsync(id, cancellationToken).ConfigureAwait(false)
            : await client.GetMapBySeedAndSizeAsync(key.Size, key.Seed, staging: false, cancellationToken)
                .ConfigureAwait(false);
        if (IsReady(get))
        {
            await SetReadyAsync(key, get.Data!, cancellationToken).ConfigureAwait(false);
        }
        // else: still generating (or a transient poll miss) — leave Generating, poll again next tick.
    }

    private async Task SetReadyAsync(RustMapsMapKey key, MapInfo info, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        var bytes = await http.GetByteArrayAsync(new Uri(info.ImageUrl!), cancellationToken).ConfigureAwait(false);
        coordinator.SetReady(key, new RustMapsReadyMap(bytes, info.Url));
    }

    private static bool IsExhausted(MapGenerationStat? stat) => stat is { } s && s.Current >= s.Allowed;

    private static bool IsReady(Result<MapInfo>? get) => get is { IsSuccess: true, Data.ImageUrl: not null };

    [LoggerMessage(Level = LogLevel.Warning, Message = "RustMaps advance failed for size {Size} seed {Seed}.")]
    private static partial void LogAdvanceFailed(ILogger logger, Exception exception, int size, int seed);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RustMaps limits unavailable for size {Size} seed {Seed}; skipping generation (fail closed).")]
    private static partial void LogLimitsUnavailable(ILogger logger, int size, int seed);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "RustMaps credit limit reached; skipping generation for size {Size} seed {Seed}.")]
    private static partial void LogLimitReached(ILogger logger, int size, int seed);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RustMaps CreateMap failed for size {Size} seed {Seed} (status {StatusCode}).")]
    private static partial void LogCreateFailed(ILogger logger, int size, int seed, int statusCode);
}
