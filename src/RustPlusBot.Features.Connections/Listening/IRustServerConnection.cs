using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One live Rust+ socket to a single server, driven by one player credential.</summary>
internal interface IRustServerConnection : IAsyncDisposable
{
    /// <summary>Connects within <paramref name="timeout"/>.</summary>
    /// <param name="timeout">How long to wait for the connection.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The connect outcome.</returns>
    Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sends a lightweight info request as a heartbeat (and auth probe), within <paramref name="timeout"/>.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The heartbeat result.</returns>
    Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Gets a server-info snapshot, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A server-info snapshot, or null on failure/timeout.</returns>
    Task<ServerInfoSnapshot?> GetServerInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Gets an in-game time snapshot, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An in-game time snapshot, or null on failure/timeout.</returns>
    Task<ServerTimeSnapshot?> GetTimeAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Gets a team snapshot, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A team snapshot, or null on failure/timeout.</returns>
    Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sends a message to in-game team chat.</summary>
    /// <param name="message">The message text to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the send has been issued.</returns>
    /// <remarks>Unlike the probe methods, this surfaces send failures to the caller (the supervisor maps them to a failed send result).</remarks>
    Task SendTeamMessageAsync(string message, CancellationToken cancellationToken);

    /// <summary>Promotes a team member to team leader; returns true on success.</summary>
    /// <param name="steamId">Steam64 id of the member to promote.</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the promotion succeeded; false on failure/timeout.</returns>
    Task<bool> PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Reads a smart device's on/off state and reachability. Also primes the socket's interest in the entity (so triggers fire for it thereafter).</summary>
    /// <param name="entityId">The in-game entity id (switch or alarm).</param>
    /// <param name="kind">The paired device kind; the Rust+ API validates the entity type on reads, so an alarm must be read as an alarm.</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="DeviceReading"/> with the active state (non-null only when <see cref="DeviceReachability.Reachable"/>) and the reachability outcome.</returns>
    Task<DeviceReading> GetSmartDeviceInfoAsync(ulong entityId,
        SmartDeviceKind kind,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>Reads a storage monitor's contents and reachability. Also primes the socket's interest so triggers fire for it thereafter.</summary>
    /// <param name="entityId">The in-game storage-monitor entity id.</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A <see cref="StorageReading"/> with the contents snapshot (non-null only when <see cref="DeviceReachability.Reachable"/>) and the reachability outcome.</returns>
    Task<StorageReading> GetStorageMonitorInfoAsync(ulong entityId,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>Sets a smart switch on/off; returns the reachability outcome.</summary>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="value">True to turn on, false to turn off.</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The <see cref="DeviceReachability"/> outcome of the operation.</returns>
    Task<DeviceReachability> SetSmartSwitchValueAsync(ulong entityId,
        bool value,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>Strobes a smart switch; returns the reachability outcome.</summary>
    /// <param name="entityId">The in-game smart-switch entity id.</param>
    /// <param name="timeoutMs">The in-game strobe duration in milliseconds.</param>
    /// <param name="value">The terminal value after strobing.</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The <see cref="DeviceReachability"/> outcome of the operation.</returns>
    Task<DeviceReachability> StrobeSmartSwitchAsync(ulong entityId,
        int timeoutMs,
        bool value,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>Polls the current map markers the bot tracks (cargo ship, patrol helicopter, chinook), for diffing by id. Throws on failure.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The current cargo-ship / patrol-helicopter / chinook markers. Other marker types are not surfaced (the mapped RustPlusApi facade exposes no others the bot reasons about; crates are no longer sent by the game).</returns>
    Task<IReadOnlyList<MapMarkerSnapshot>> GetMapMarkersAsync(TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the static map dimensions for grid-reference rendering, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The map dimensions, or null on failure/timeout.</returns>
    Task<MapDimensions?> GetMapDimensionsAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Gets the map monuments (for locating oil rigs). Throws on failure.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The map monuments (token + position).</returns>
    Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the base map image (JPEG bytes), or null on failure/unavailable.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The base-map JPEG bytes, or null on failure/unavailable.</returns>
    Task<byte[]?> GetMapImageAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Raised for every in-game team chat line received on this socket.</summary>
    event EventHandler<TeamChatLine>? TeamMessageReceived;

    /// <summary>Raised when a managed smart device's state changes in-game; carries the entity id and new state.</summary>
    event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered;

    /// <summary>Raised when a managed storage monitor's contents change in-game; carries the entity id and the new contents.</summary>
    event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered;
}
