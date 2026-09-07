using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One live Rust+ socket to a single server, driven by one player credential.</summary>
internal interface IRustServerConnection : IAsyncDisposable
{
    /// <summary>
    /// Whether the underlying socket is currently open. Cheap and synchronous: the Rust+ library raises no
    /// event when the <em>server</em> closes the socket, so a liveness watchdog polls this to detect a drop
    /// promptly instead of waiting for the next (up to a minute apart) heartbeat.
    /// </summary>
    bool IsConnected { get; }

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
    /// <param name="timeout">How long to wait for the send to be acknowledged.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the server has acknowledged the send, not when it was written to the socket.</returns>
    /// <remarks>
    /// Unlike the probe methods, this surfaces send failures to the caller (the supervisor maps them to a
    /// failed send result). <paramref name="timeout"/> is mandatory for the same reason it is on every other
    /// call here: a Rust+ request whose response the server never delivers otherwise parks the caller
    /// forever, and the callers are the relay loops that drive #events, #playerevents and alarms.
    /// </remarks>
    Task SendTeamMessageAsync(string message, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Probes the authenticated player's clan, distinguishing "no clan" from "could not ask".</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The classified probe result.</returns>
    Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sends a message to in-game clan chat.</summary>
    /// <param name="message">The message text to send.</param>
    /// <param name="timeout">How long to wait for the send to be acknowledged.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the server has acknowledged the send, not when it was written to the socket.</returns>
    /// <remarks>Like <see cref="SendTeamMessageAsync"/>, this surfaces failures to the caller and is bounded by <paramref name="timeout"/>.</remarks>
    Task SendClanMessageAsync(string message, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sets the clan message of the day; returns true on success.</summary>
    /// <param name="motd">The new message of the day.</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if the MOTD was set; false on failure/timeout.</returns>
    Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken cancellationToken);

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

    /// <summary>Polls the current map markers and vending machines. Throws on failure.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The markers the bot diffs plus every player vending machine.</returns>
    Task<MapMarkersSnapshot> GetMapMarkersAsync(TimeSpan timeout,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the static map dimensions for grid-reference rendering, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The map dimensions, or null on failure/timeout.</returns>
    Task<MapDimensions?> GetMapDimensionsAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Gets the world size and seed from server info, or null when unavailable.</summary>
    /// <param name="timeout">The per-call timeout.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The world snapshot, or null.</returns>
    Task<WorldSnapshot?> GetWorldAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

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

    /// <summary>Raised for every in-game clan chat line received on this socket.</summary>
    event EventHandler<ClanChatLine>? ClanMessageReceived;

    /// <summary>
    /// Raised when the clan snapshot changes in game. A dissolved or departed clan arrives as
    /// <see cref="ClanProbeStatus.NoClan"/> — a definitive signal, never <see cref="ClanProbeStatus.Unavailable"/>.
    /// </summary>
    event EventHandler<ClanProbeResult>? ClanChanged;

    /// <summary>Raised when a managed smart device's state changes in-game; carries the entity id and new state.</summary>
    event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered;

    /// <summary>Raised when a managed storage monitor's contents change in-game; carries the entity id and the new contents.</summary>
    event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered;

    /// <summary>
    /// Raised when the server pushes a <c>team_changed</c> broadcast (member join/leave, online/offline,
    /// death/respawn, movement, leader change). Carries the full team snapshot — the same shape a
    /// <see cref="GetTeamInfoAsync"/> poll returns.
    /// </summary>
    event EventHandler<TeamInfoSnapshot>? TeamChanged;
}
