using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Supervisor;

/// <summary>
/// Holds the map for one connected window. Rust+ serves the map geometry, the monuments and the base-map
/// JPEG from a single <c>GetMap</c> response that always ships the whole ~683 KB image, while the readers —
/// the #map composer, the team panel renderer, oil-rig detection — re-read several times a minute. Resolving
/// once per window and serving from memory turns tens of megabytes an hour into one fetch.
/// </summary>
/// <remarks>
/// <para>
/// Correctness rests on the window boundary: the map is fixed for a wipe, and the window is torn down and
/// re-resolved on reconnect — exactly when a new map can appear.
/// </para>
/// <para>
/// The window therefore holds the JPEG for as long as it is connected, whether or not the guild uses #map.
/// That is one bounded ~683 KB array per connected server, released on disconnect; the alternative — fetching
/// the image separately when #map first asks — costs a second full map download per window.
/// </para>
/// </remarks>
/// <param name="connection">The window's live socket.</param>
/// <param name="timeout">The per-fetch timeout.</param>
#pragma warning disable CA1001 // The gate is never awaited via AvailableWaitHandle, so SemaphoreSlim has
// nothing to dispose; making the window cache disposable would only push lifetime plumbing into the
// supervisor's teardown path for no benefit.
internal sealed class ServerMapWindowCache(IRustServerConnection connection, TimeSpan timeout)
#pragma warning restore CA1001
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile MapDimensions? _dimensions;
    private volatile ServerMapSnapshot? _snapshot;

    /// <summary>
    /// The resolved dimensions, or null while the window has not resolved them yet. A non-blocking peek for
    /// readers that tolerate a miss (the team-state publishers render without a grid reference).
    /// </summary>
    public MapDimensions? DimensionsOrNull => _dimensions;

    /// <summary>Gets the map dimensions, resolving the window's map if needed.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The dimensions, or null while either half is unavailable.</returns>
    public async Task<MapDimensions?> GetDimensionsAsync(CancellationToken cancellationToken)
    {
        if (_dimensions is { } cached)
        {
            return cached;
        }

        var geometry = (await ResolveAsync(cancellationToken).ConfigureAwait(false)).Geometry;
        if (geometry is null)
        {
            return null;
        }

        // The world size comes from GetInfo, not GetMap. It is a cheap call with an independent failure
        // mode (a rate limit on the connect burst, say), so it is retried per read until it lands rather
        // than latching "no dimensions" — and failing it never costs another map download.
        var world = await connection.GetWorldAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (world is null)
        {
            return null;
        }

        var dimensions = new MapDimensions(geometry.Width, geometry.Height, geometry.OceanMargin, world.WorldSize);
        _dimensions = dimensions;
        return dimensions;
    }

    /// <summary>Gets the map monuments, resolving the window's map if needed.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The monuments (token + position).</returns>
    public async Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(CancellationToken cancellationToken) =>
        (await ResolveAsync(cancellationToken).ConfigureAwait(false)).Monuments;

    /// <summary>Gets the base-map JPEG, resolving the window's map if needed.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The JPEG bytes, or null when the server sends no image.</returns>
    public async Task<byte[]?> GetImageAsync(CancellationToken cancellationToken) =>
        (await ResolveAsync(cancellationToken).ConfigureAwait(false)).JpgImage;

    private async Task<ServerMapSnapshot> ResolveAsync(CancellationToken cancellationToken)
    {
        if (_snapshot is { } cached)
        {
            return cached;
        }

        // Single-flight: on connect the marker poll and a #map compose can both arrive before either
        // resolves, and each miss costs a full map download. Taken outside the try so a reader that never
        // acquires the gate — a cancelled one, say — cannot release it on the way out.
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A throw here leaves the window unresolved, so the next reader retries rather than inheriting
            // a failure. Each reader fetches under its own token, so one cancelling never poisons another.
            return _snapshot ??= await connection.GetServerMapAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
