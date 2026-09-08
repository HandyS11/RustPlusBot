using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IRustSocketSource"/>. Each <see cref="Create"/> dequeues the next connect outcome;
/// each created connection dequeues heartbeat results (default Ok(0) when the queue empties).
/// </summary>
/// <remarks>
/// Connect outcomes and heartbeat results are single FIFO queues shared by every connection this
/// source creates, consumed in enqueue order. This is intentional: the supervisor tests drive one
/// server at a time and enqueue a deterministic sequence that must flow across reconnections of that
/// server. Tests that need concurrent multi-server interleaving would require per-connection queues.
/// Once the heartbeat queue is exhausted, the fake HOLDS the last dequeued heartbeat (mimicking a
/// healthy server that keeps reporting the same state), rather than falling back to Ok(0).
/// </remarks>
internal sealed class FakeRustSocketSource : IRustSocketSource
{
    private readonly ConcurrentQueue<SocketConnectOutcome> _connectOutcomes = new();
    private readonly ConcurrentQueue<HeartbeatResult> _heartbeats = new();
    private readonly Dictionary<ulong, DeviceReachability> _pendingDeviceReachabilityOverrides = [];
    private readonly Dictionary<ulong, bool?> _pendingDeviceStates = [];
    private readonly ConcurrentQueue<IReadOnlyList<MapMarkerSnapshot>> _pendingMarkerScript = new();
    private readonly Dictionary<ulong, StorageContentsSnapshot?> _pendingStorageContents = [];
    private int _createCount;

    private HeartbeatResult _lastHeartbeat = HeartbeatResult.Ok(0);
    private ClanProbeResult? _pendingClanProbe;
    private bool _pendingMapTimeout;
    private IReadOnlyList<MonumentSnapshot> _pendingMonuments = [];
    private IReadOnlyList<VendingMachineSnapshot> _pendingVendingMachines = [];

    /// <summary>Number of times <see cref="Create"/> has been called. Safe to read from any thread.</summary>
    public int CreateCount => Volatile.Read(ref _createCount);

    /// <summary>The IP address passed to the most recent <see cref="Create"/> call. Read after the operation under test has settled.</summary>
    public string? LastIp { get; private set; }

    /// <summary>The Steam ID passed to the most recent <see cref="Create"/> call. Read after the operation under test has settled.</summary>
    public ulong LastSteamId { get; private set; }

    /// <summary>The connection produced by the most recent <see cref="Create"/> call (for driving inbound/inspecting sends).</summary>
    internal FakeConnection? LastConnection { get; private set; }

    /// <summary>Optional hook invoked on each new <see cref="FakeConnection"/> at creation time, before it is
    /// returned and the poll loops start. Lets a test stage <see cref="FakeConnection.TeamResult"/> (or null)
    /// without racing the immediate priming team poll.</summary>
    internal Action<FakeConnection>? LastConnectionSetup { get; set; }

    public IRustServerConnection Create(string ip, int port, ulong steamId, string playerToken)
    {
        Interlocked.Increment(ref _createCount);
        LastIp = ip;
        LastSteamId = steamId;
        var outcome = _connectOutcomes.TryDequeue(out var next) ? next : SocketConnectOutcome.Connected;
        var connection = new FakeConnection(outcome, this);
        // Transfer any pre-staged marker script so it is in place before the poll loop starts.
        while (_pendingMarkerScript.TryDequeue(out var markers))
        {
            connection.EnqueueMarkers(markers);
        }

        // Transfer any pre-staged monuments so they are available before the supervisor fetches them on connect.
        // Reset after transfer so the staging applies to the NEXT connection only (no leak across connections).
        connection.MonumentsResult = _pendingMonuments;
        _pendingMonuments = [];

        // Transfer any pre-staged map timeout so the marker poll's rig fetch throws for the NEXT connection
        // only (mimicking a real per-request timeout, which surfaces as OperationCanceledException).
        connection.MapTimeout = _pendingMapTimeout;
        _pendingMapTimeout = false;

        // Transfer any pre-staged vending machines so they are available before the supervisor's marker
        // poll reads them. Reset after transfer so the staging applies to the NEXT connection only.
        connection.VendingResult = _pendingVendingMachines;
        _pendingVendingMachines = [];

        // Transfer any pre-staged storage contents so they are in place before the prime loop starts.
        foreach (var (entityId, contents) in _pendingStorageContents)
        {
            connection.StorageContents[entityId] = contents;
        }

        _pendingStorageContents.Clear();

        // Transfer any pre-staged device-reachability overrides so they are available before the prime loop starts.
        foreach (var (entityId, reachability) in _pendingDeviceReachabilityOverrides)
        {
            connection.DeviceReachabilityOverrides[entityId] = reachability;
        }

        _pendingDeviceReachabilityOverrides.Clear();

        // Transfer any pre-staged device states so they are in place before the prime/sweep loops read them.
        foreach (var (entityId, state) in _pendingDeviceStates)
        {
            connection.SwitchStates[entityId] = state;
        }

        _pendingDeviceStates.Clear();

        // Transfer any pre-staged clan probe so it is in place before the supervisor's connect-time probe
        // reads it. Reset after transfer so the staging applies to the NEXT connection only.
        if (_pendingClanProbe is { } clanProbe)
        {
            connection.ClanProbe = clanProbe;
            _pendingClanProbe = null;
        }

        LastConnectionSetup?.Invoke(connection);

        LastConnection = connection;
        return connection;
    }

    public void EnqueueConnect(SocketConnectOutcome outcome) => _connectOutcomes.Enqueue(outcome);

    public void EnqueueHeartbeat(HeartbeatResult result) => _heartbeats.Enqueue(result);

    /// <summary>
    /// Pre-stages a scripted marker list for the NEXT connection created by <see cref="Create"/>.
    /// All items enqueued here are transferred to the new <see cref="FakeConnection"/> at creation
    /// time, before the supervisor can start the poll loop, eliminating the setup race. Call this
    /// before <see cref="EnsureConnectionAsync"/> so the script is in place when polls begin.
    /// </summary>
    /// <param name="markers">The marker list to deliver on the corresponding poll.</param>
    public void EnqueueMarkers(IReadOnlyList<MapMarkerSnapshot> markers) =>
        _pendingMarkerScript.Enqueue(markers);

    /// <summary>
    /// Pre-stages the monument list returned by <see cref="FakeConnection.GetMonumentsAsync"/> for the NEXT
    /// connection created by <see cref="Create"/>. The list is transferred to the new connection at creation
    /// time, before the supervisor fetches monuments on connect, eliminating the setup race.
    /// Call this before <see cref="EnsureConnectionAsync"/>.
    /// </summary>
    /// <param name="monuments">The monument list to return from <see cref="IRustServerConnection.GetMonumentsAsync"/>.</param>
    public void SetMonuments(IReadOnlyList<MonumentSnapshot> monuments) => _pendingMonuments = monuments;

    /// <summary>
    /// Makes the NEXT connection's <see cref="FakeConnection.GetServerMapAsync"/> throw
    /// <see cref="OperationCanceledException"/>, simulating a per-request timeout (the outer connection
    /// token is NOT cancelled). The supervisor issues this fetch from its background marker poll, not the
    /// connect path. Applies to the next connection only. Call before <see cref="EnsureConnectionAsync"/>.
    /// </summary>
    public void TimeoutOnMapOnce() => _pendingMapTimeout = true;

    /// <summary>
    /// Pre-stages the vending-machine set returned as the vending half of every <see cref="MapMarkersSnapshot"/>
    /// produced by the NEXT connection created by <see cref="Create"/>. Transferred to the new connection at
    /// creation time, before the supervisor's poll loop starts, eliminating the setup race.
    /// Call this before <see cref="EnsureConnectionAsync"/>.
    /// </summary>
    /// <param name="machines">The vending-machine list to return from every <see cref="IRustServerConnection.GetMapMarkersAsync"/> call.</param>
    public void SetVendingMachines(IReadOnlyList<VendingMachineSnapshot> machines) =>
        _pendingVendingMachines = machines;

    /// <summary>
    /// Pre-stages the probe result returned by <see cref="FakeConnection.GetClanInfoAsync"/> for the NEXT
    /// connection created by <see cref="Create"/>. Transferred to the new connection at creation time, before
    /// the supervisor's connect-time clan probe runs, eliminating the setup race between the test assigning
    /// <see cref="FakeConnection.ClanProbe"/> and the supervisor reading it. Call this before
    /// <see cref="EnsureConnectionAsync"/>.
    /// </summary>
    /// <param name="probe">The probe result to return from <see cref="IRustServerConnection.GetClanInfoAsync"/>.</param>
    public void SetClanProbe(ClanProbeResult probe) => _pendingClanProbe = probe;

    /// <summary>
    /// Pre-stages storage contents for a given entity, to be transferred to the NEXT connection created by
    /// <see cref="Create"/>. Eliminates the setup race when the prime loop reads contents before the test can
    /// assign them on <see cref="FakeConnection.StorageContents"/>.
    /// Call this before <see cref="EnsureConnectionAsync"/>.
    /// </summary>
    /// <param name="entityId">The storage-monitor entity id to stage.</param>
    /// <param name="contents">The contents snapshot to return from <see cref="IRustServerConnection.GetStorageMonitorInfoAsync"/>.</param>
    public void EnqueueStorageInfo(ulong entityId, StorageContentsSnapshot? contents) =>
        _pendingStorageContents[entityId] = contents;

    /// <summary>
    /// Pre-stages a device-reachability override for a given entity, to be transferred to the NEXT connection
    /// created by <see cref="Create"/>. Eliminates the setup race when the prime loop reads reachability before
    /// the test can assign it on <see cref="FakeConnection.DeviceReachabilityOverrides"/>.
    /// Call this before <see cref="EnsureConnectionAsync"/>.
    /// </summary>
    /// <param name="entityId">The entity id to stage.</param>
    /// <param name="reachability">The reachability to return from
    /// <see cref="IRustServerConnection.GetSmartDeviceInfoAsync"/> or
    /// <see cref="IRustServerConnection.GetStorageMonitorInfoAsync"/> for this entity.</param>
    public void StageDeviceReachability(ulong entityId, DeviceReachability reachability) =>
        _pendingDeviceReachabilityOverrides[entityId] = reachability;

    /// <summary>
    /// Pre-stages the on/off state returned by <see cref="FakeConnection.GetSmartDeviceInfoAsync"/> for a given
    /// entity, to be transferred to the NEXT connection created by <see cref="Create"/>. Eliminates the setup
    /// race when the prime/sweep loops read state before the test can assign
    /// <see cref="FakeConnection.SwitchStates"/>. Call this before <see cref="EnsureConnectionAsync"/>.
    /// </summary>
    /// <param name="entityId">The entity id to stage.</param>
    /// <param name="isActive">The on/off state to return for this entity.</param>
    public void StageDeviceState(ulong entityId, bool? isActive) =>
        _pendingDeviceStates[entityId] = isActive;

    internal HeartbeatResult NextHeartbeat()
    {
        if (_heartbeats.TryDequeue(out var next))
        {
            _lastHeartbeat = next;
        }

        return _lastHeartbeat;
    }

    internal sealed class FakeConnection(SocketConnectOutcome outcome, FakeRustSocketSource source)
        : IRustServerConnection
    {
        private readonly ConcurrentQueue<IReadOnlyList<MapMarkerSnapshot>> _markerScript = new();

        private readonly TaskCompletionSource _teamInfoEntered =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private int _disposeCount;
        private IReadOnlyList<MapMarkerSnapshot> _lastMarkers = [];
        private int _mapFetchCount;
        private bool _markerScriptStarted;

        /// <summary>Gets the messages sent via <see cref="SendTeamMessageAsync"/>.</summary>
        public List<string> SentMessages { get; } = [];

        /// <summary>Number of times <see cref="DisposeAsync"/> has been called. Safe to read from any thread.</summary>
        public int DisposeCount => Volatile.Read(ref _disposeCount);

        /// <summary>
        /// When set, <see cref="ConnectAsync"/> throws this instead of answering — models a socket library
        /// that faults while connecting rather than reporting a <see cref="SocketConnectOutcome"/>.
        /// </summary>
        public Exception? ConnectFault { get; set; }

        /// <summary>
        /// When true, <see cref="ConnectAsync"/> never answers until its cancellation token fires (then
        /// throws <see cref="OperationCanceledException"/>) — models a connect attempt still in flight when
        /// the supervisor is stopped.
        /// </summary>
        public bool BlockConnectUntilCancelled { get; set; }

        /// <summary>When set, <see cref="GetTeamInfoAsync"/> throws this instead of answering.</summary>
        public Exception? TeamInfoFault { get; set; }

        /// <summary>
        /// When true, <see cref="GetTeamInfoAsync"/> never answers until its cancellation token fires (then
        /// throws <see cref="OperationCanceledException"/>) — models a team poll parked in a request while
        /// the connected window is torn down.
        /// </summary>
        public bool BlockTeamInfoUntilCancelled { get; set; }

        /// <summary>
        /// When set, <see cref="GetTeamInfoAsync"/> awaits this — deliberately ignoring its cancellation
        /// token — before answering. Lets a test hold the team poll, and with it the connected window's
        /// teardown, open across a disposal.
        /// </summary>
        public Task? TeamInfoHold { get; set; }

        /// <summary>Completes the first time <see cref="GetTeamInfoAsync"/> is entered.</summary>
        public Task TeamInfoEntered => _teamInfoEntered.Task;

        /// <summary>When set, <see cref="GetSmartDeviceInfoAsync"/> throws this instead of answering.</summary>
        public Exception? DeviceInfoFault { get; set; }

        /// <summary>
        /// When set, <see cref="SendTeamMessageAsync"/> and <see cref="SendClanMessageAsync"/> return a task
        /// that never completes, reproducing a Rust+ send whose response the server never delivers.
        /// </summary>
        public bool HangOnSend { get; set; }

        /// <summary>The snapshot returned by <see cref="GetServerInfoAsync"/>. Defaults to a non-null zero snapshot.</summary>
        public ServerInfoSnapshot? InfoResult { get; set; } = new(0, 0, 0, null);

        /// <summary>The snapshot returned by <see cref="GetTimeAsync"/>. Defaults to a non-null zero snapshot.</summary>
        public ServerTimeSnapshot? TimeResult { get; set; } = new(0f, 0f, 0f);

        /// <summary>The snapshot returned by <see cref="GetTeamInfoAsync"/>. Defaults to a non-null empty snapshot.</summary>
        public TeamInfoSnapshot? TeamResult { get; set; } = new(0UL, []);

        /// <summary>Number of times <see cref="GetTeamInfoAsync"/> has been called (to assert team info is
        /// no longer polled on the fast marker cadence).</summary>
        public int TeamInfoCallCount { get; private set; }

        /// <summary>The result returned by <see cref="PromoteToLeaderAsync"/>. Defaults to true.</summary>
        public bool PromoteResult { get; set; } = true;

        /// <summary>The Steam ID passed to the most recent <see cref="PromoteToLeaderAsync"/> call.</summary>
        public ulong LastPromotedSteamId { get; private set; }

        /// <summary>The state returned by <see cref="GetSmartDeviceInfoAsync"/> per entity id; absent → null.</summary>
        public Dictionary<ulong, bool?> SwitchStates { get; } = [];

        /// <summary>Records (entityId, kind) for every <see cref="GetSmartDeviceInfoAsync"/> call, in call order.</summary>
        public List<(ulong EntityId, SmartDeviceKind Kind)> DeviceReadCalls { get; } = [];

        /// <summary>The contents returned by <see cref="GetStorageMonitorInfoAsync"/> per entity id; absent → null.</summary>
        public Dictionary<ulong, StorageContentsSnapshot?> StorageContents { get; } = [];

        /// <summary>
        /// Per-entity reachability override consulted by <see cref="GetSmartDeviceInfoAsync"/> and
        /// <see cref="GetStorageMonitorInfoAsync"/>. Absent → <see cref="DeviceReachability.Reachable"/>.
        /// Set this in tests (Task 5+) to inject <see cref="DeviceReachability.Removed"/> or
        /// <see cref="DeviceReachability.NoPrivilege"/> for specific entities.
        /// </summary>
        public Dictionary<ulong, DeviceReachability> DeviceReachabilityOverrides { get; } = [];

        /// <summary>The result returned by <see cref="SetSmartSwitchValueAsync"/>. Defaults to <see cref="DeviceReachability.Reachable"/>.</summary>
        public DeviceReachability SetSwitchReachability { get; set; } = DeviceReachability.Reachable;

        /// <summary>The result returned by <see cref="StrobeSmartSwitchAsync"/>. Defaults to <see cref="DeviceReachability.Reachable"/>.</summary>
        public DeviceReachability StrobeSwitchReachability { get; set; } = DeviceReachability.Reachable;

        /// <summary>Records (entityId, value) passed to <see cref="SetSmartSwitchValueAsync"/>.</summary>
        public List<(ulong EntityId, bool Value)> SetSwitchCalls { get; } = [];

        /// <summary>
        /// The fallback markers returned by <see cref="GetMapMarkersAsync"/> when no scripted results remain.
        /// Defaults to empty (nothing on the map). Callers that do not use <see cref="EnqueueMarkers"/> see
        /// this value on every poll, matching the original Task-2 behavior.
        /// </summary>
        public IReadOnlyList<MapMarkerSnapshot> MarkersResult { get; set; } = [];

        /// <summary>When true, <see cref="GetMapMarkersAsync"/> throws regardless of any enqueued script.</summary>
        public bool MarkersThrow { get; set; }

        /// <summary>
        /// The vending machines returned as the vending half of every <see cref="GetMapMarkersAsync"/> result.
        /// Defaults to empty. Unlike <see cref="MarkersResult"/> there is no per-poll scripting queue: Rust
        /// re-sends the full vending set on every poll, so a single settable value is all tests need.
        /// </summary>
        public IReadOnlyList<VendingMachineSnapshot> VendingResult { get; set; } = [];

        /// <summary>The geometry half of <see cref="GetServerMapAsync"/>. Defaults to a non-null snapshot.</summary>
        public MapGeometry? GeometryResult { get; set; } = new(4000u, 4000u, 500);

        /// <summary>Number of GetMap round trips this connection has issued. Dimensions, monuments and the
        /// map image are all served by the one Rust+ GetMap endpoint, which answers with the whole map JPEG
        /// (~683 KB), so every one of those calls costs a full map download on the real socket. Tests assert
        /// on this to pin how much a connected window actually pulls off the wire.</summary>
        public int MapFetchCount => Volatile.Read(ref _mapFetchCount);

        /// <summary>The snapshot returned by <see cref="GetWorldAsync"/>, which also completes the map
        /// dimensions with the world size. Defaults to a snapshot matching <see cref="GeometryResult"/>.</summary>
        public WorldSnapshot? World { get; set; } = new(4000u, 0u);

        /// <summary>The monuments half of <see cref="GetServerMapAsync"/>. Defaults to empty.</summary>
        public IReadOnlyList<MonumentSnapshot> MonumentsResult { get; set; } = [];

        /// <summary>When true, <see cref="GetServerMapAsync"/> throws <see cref="OperationCanceledException"/>
        /// (a per-request timeout) instead of answering.</summary>
        public bool MapTimeout { get; set; }

        /// <summary>When set, <see cref="GetServerMapAsync"/> throws this instead of answering — models the
        /// real socket's "GetMap returned no data" throw when the Rust+ endpoint answers with an error
        /// (rate limit, no map, …).</summary>
        public Exception? MapFault { get; set; }

        /// <summary>The image half of <see cref="GetServerMapAsync"/>. Defaults to null.</summary>
        public byte[]? MapImageResult { get; set; }

        /// <summary>The probe result this fake returns; defaults to no clan.</summary>
        public ClanProbeResult ClanProbe { get; set; } = ClanProbeResult.NoClan;

        /// <summary>Messages sent to in-game clan chat through this fake.</summary>
        public List<string> SentClanMessages { get; } = [];

        /// <summary>Liveness flag consulted by the supervisor's watchdog. Set false to simulate a
        /// server-initiated socket close (which the real library signals via <c>IsConnected</c>, not an event).</summary>
        public bool IsConnected { get; set; } = true;

        /// <summary>Raised when a team chat message arrives on this connection.</summary>
        public event EventHandler<TeamChatLine>? TeamMessageReceived;

        /// <summary>Raised when a clan chat message arrives on this connection.</summary>
        public event EventHandler<ClanChatLine>? ClanMessageReceived;

        /// <summary>Raised when the clan snapshot changes on this connection.</summary>
        public event EventHandler<ClanProbeResult>? ClanChanged;

        /// <summary>Raised by <see cref="RaiseSmartDeviceTriggered"/>.</summary>
        public event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered;

        /// <summary>Raised by <see cref="RaiseStorageMonitorTriggered"/>.</summary>
        public event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered;

        /// <summary>Raised by <see cref="RaiseTeamChanged"/> to simulate a pushed team_changed broadcast.</summary>
        public event EventHandler<TeamInfoSnapshot>? TeamChanged;

        public async Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (ConnectFault is { } fault)
            {
                throw fault;
            }

            if (BlockConnectUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            return outcome;
        }

        public Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(source.NextHeartbeat());

        public Task<ServerInfoSnapshot?> GetServerInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(InfoResult);

        public Task<ServerTimeSnapshot?> GetTimeAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(TimeResult);

        public async Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            TeamInfoCallCount++;
            _teamInfoEntered.TrySetResult();
            if (TeamInfoHold is { } hold)
            {
                await hold.ConfigureAwait(false);
            }

            if (BlockTeamInfoUntilCancelled)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }

            if (TeamInfoFault is { } fault)
            {
                throw fault;
            }

            return TeamResult;
        }

        public Task SendTeamMessageAsync(string message, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (HangOnSend)
            {
                return new TaskCompletionSource().Task;
            }

            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task<ClanProbeResult> GetClanInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(ClanProbe);

        public Task SendClanMessageAsync(string message, TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (HangOnSend)
            {
                return new TaskCompletionSource().Task;
            }

            SentClanMessages.Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> SetClanMotdAsync(string motd, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(true);

#pragma warning disable RCS1163 // Unused parameters for fake implementation
        public Task<bool> PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken cancellationToken)
#pragma warning restore RCS1163
        {
            LastPromotedSteamId = steamId;
            return Task.FromResult(PromoteResult);
        }

#pragma warning disable RCS1163 // Unused parameters for fake implementation
        public Task<DeviceReading> GetSmartDeviceInfoAsync(ulong entityId,
            SmartDeviceKind kind,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            lock (DeviceReadCalls)
            {
                DeviceReadCalls.Add((entityId, kind));
            }

            if (DeviceInfoFault is { } fault)
            {
                return Task.FromException<DeviceReading>(fault);
            }

            var reachability = DeviceReachabilityOverrides.TryGetValue(entityId, out var r)
                ? r
                : DeviceReachability.Reachable;
            var state = SwitchStates.TryGetValue(entityId, out var s) ? s : null;
            return Task.FromResult(new DeviceReading(reachability == DeviceReachability.Reachable ? state : null,
                reachability));
        }
#pragma warning restore RCS1163

#pragma warning disable RCS1163 // Unused parameters for fake implementation
        public Task<StorageReading> GetStorageMonitorInfoAsync(ulong entityId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            var reachability = DeviceReachabilityOverrides.TryGetValue(entityId, out var r)
                ? r
                : DeviceReachability.Reachable;
            var contents = StorageContents.TryGetValue(entityId, out var c) ? c : null;
            return Task.FromResult(new StorageReading(reachability == DeviceReachability.Reachable ? contents : null,
                reachability));
        }
#pragma warning restore RCS1163

#pragma warning disable RCS1163 // Unused parameters for fake implementation
        public Task<DeviceReachability> SetSmartSwitchValueAsync(ulong entityId,
            bool value,
            TimeSpan timeout,
            CancellationToken cancellationToken)
#pragma warning restore RCS1163
        {
            SetSwitchCalls.Add((entityId, value));
            return Task.FromResult(SetSwitchReachability);
        }

#pragma warning disable RCS1163 // Unused parameters for fake implementation
        public Task<DeviceReachability> StrobeSmartSwitchAsync(ulong entityId,
            int timeoutMs,
            bool value,
            TimeSpan timeout,
            CancellationToken cancellationToken) =>
            Task.FromResult(StrobeSwitchReachability);
#pragma warning restore RCS1163

        public Task<MapMarkersSnapshot> GetMapMarkersAsync(TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            if (MarkersThrow)
            {
                return Task.FromException<MapMarkersSnapshot>(
                    new InvalidOperationException("poll failed"));
            }

            if (_markerScript.TryDequeue(out var scripted))
            {
                _markerScriptStarted = true;
                _lastMarkers = scripted;
                return Task.FromResult(new MapMarkersSnapshot(_lastMarkers, VendingResult));
            }

            // Once any scripted result has been dequeued, hold the last one (mirroring NextHeartbeat).
            // If the script was never started, fall back to MarkersResult so Task-2 callers are unaffected.
            return Task.FromResult(
                new MapMarkersSnapshot(_markerScriptStarted ? _lastMarkers : MarkersResult, VendingResult));
        }

        public Task<ServerMapSnapshot> GetServerMapAsync(TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _mapFetchCount);
            var fault = MapFault ?? (MapTimeout ? new OperationCanceledException() : null);
            return fault is null
                ? Task.FromResult(new ServerMapSnapshot(GeometryResult, MonumentsResult, MapImageResult))
                : Task.FromException<ServerMapSnapshot>(fault);
        }

        public Task<WorldSnapshot?> GetWorldAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(World);

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Enqueues a scripted marker list to be returned by the next <see cref="GetMapMarkersAsync"/> call.
        /// Once the queue empties the last dequeued list is held and returned on every subsequent poll,
        /// mirroring the heartbeat "hold last" pattern. Enqueued results take priority over
        /// <see cref="MarkersResult"/>; if nothing has been enqueued, <see cref="MarkersResult"/> is used.
        /// </summary>
        /// <param name="markers">The marker list to return for the next poll.</param>
        public void EnqueueMarkers(IReadOnlyList<MapMarkerSnapshot> markers) => _markerScript.Enqueue(markers);

        /// <summary>Raises <see cref="TeamMessageReceived"/> to simulate an inbound team chat line.</summary>
        /// <param name="line">The team chat line to raise.</param>
        public void RaiseTeamMessage(TeamChatLine line) => TeamMessageReceived?.Invoke(this, line);

        /// <summary>Raises <see cref="ClanMessageReceived"/> to simulate an inbound clan chat line.</summary>
        /// <param name="line">The clan chat line to raise.</param>
        public void RaiseClanMessage(ClanChatLine line) => ClanMessageReceived?.Invoke(this, line);

        /// <summary>Raises <see cref="ClanChanged"/> to simulate a clan snapshot change.</summary>
        /// <param name="result">The probe result to raise.</param>
        public void RaiseClanChanged(ClanProbeResult result) => ClanChanged?.Invoke(this, result);

        /// <summary>Raises <see cref="TeamChanged"/> to simulate a pushed team_changed broadcast.</summary>
        /// <param name="snapshot">The team snapshot to deliver.</param>
        public void RaiseTeamChanged(TeamInfoSnapshot snapshot) => TeamChanged?.Invoke(this, snapshot);

        /// <summary>Simulates an in-game smart-device state change.</summary>
        /// <param name="entityId">The smart-device entity id to raise the event for.</param>
        /// <param name="isActive">The current active state carried on the trigger arg.</param>
        public void RaiseSmartDeviceTriggered(ulong entityId, bool isActive) =>
            SmartDeviceTriggered?.Invoke(this, new SmartDeviceTrigger(entityId, isActive));

        /// <summary>Simulates an in-game storage-monitor contents change.</summary>
        /// <param name="entityId">The storage-monitor entity id to raise the event for.</param>
        /// <param name="contents">The contents snapshot carried on the trigger.</param>
        public void RaiseStorageMonitorTriggered(ulong entityId, StorageContentsSnapshot contents) =>
            StorageMonitorTriggered?.Invoke(this, new StorageMonitorTrigger(entityId, contents));
    }
}
