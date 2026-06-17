using System.Collections.Concurrent;
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
    private int _createCount;

    private HeartbeatResult _lastHeartbeat = HeartbeatResult.Ok(0);

    /// <summary>Number of times <see cref="Create"/> has been called. Safe to read from any thread.</summary>
    public int CreateCount => Volatile.Read(ref _createCount);

    /// <summary>The IP address passed to the most recent <see cref="Create"/> call. Read after the operation under test has settled.</summary>
    public string? LastIp { get; private set; }

    /// <summary>The Steam ID passed to the most recent <see cref="Create"/> call. Read after the operation under test has settled.</summary>
    public ulong LastSteamId { get; private set; }

    /// <summary>The connection produced by the most recent <see cref="Create"/> call (for driving inbound/inspecting sends).</summary>
    internal FakeConnection? LastConnection { get; private set; }

    public IRustServerConnection Create(string ip, int port, ulong steamId, string playerToken)
    {
        Interlocked.Increment(ref _createCount);
        LastIp = ip;
        LastSteamId = steamId;
        var outcome = _connectOutcomes.TryDequeue(out var next) ? next : SocketConnectOutcome.Connected;
        var connection = new FakeConnection(outcome, this);
        LastConnection = connection;
        return connection;
    }

    public void EnqueueConnect(SocketConnectOutcome outcome) => _connectOutcomes.Enqueue(outcome);

    public void EnqueueHeartbeat(HeartbeatResult result) => _heartbeats.Enqueue(result);

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
        /// <summary>Gets the messages sent via <see cref="SendTeamMessageAsync"/>.</summary>
        public List<string> SentMessages { get; } = [];

        /// <summary>The snapshot returned by <see cref="GetServerInfoAsync"/>. Defaults to a non-null zero snapshot.</summary>
        public ServerInfoSnapshot? InfoResult { get; set; } = new(0, 0, 0, null);

        /// <summary>The snapshot returned by <see cref="GetTimeAsync"/>. Defaults to a non-null zero snapshot.</summary>
        public ServerTimeSnapshot? TimeResult { get; set; } = new(0f, 0f, 0f);

        /// <summary>The snapshot returned by <see cref="GetTeamInfoAsync"/>. Defaults to a non-null empty snapshot.</summary>
        public TeamInfoSnapshot? TeamResult { get; set; } = new(0UL, []);

        /// <summary>The result returned by <see cref="PromoteToLeaderAsync"/>. Defaults to true.</summary>
        public bool PromoteResult { get; set; } = true;

        /// <summary>The Steam ID passed to the most recent <see cref="PromoteToLeaderAsync"/> call.</summary>
        public ulong LastPromotedSteamId { get; private set; }

        /// <summary>Raised when a team chat message arrives on this connection.</summary>
        public event EventHandler<TeamChatLine>? TeamMessageReceived;

        public Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(outcome);

        public Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(source.NextHeartbeat());

        public Task<ServerInfoSnapshot?> GetServerInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(InfoResult);

        public Task<ServerTimeSnapshot?> GetTimeAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(TimeResult);

        public Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(TeamResult);

        public Task SendTeamMessageAsync(string message, CancellationToken cancellationToken)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

#pragma warning disable RCS1163 // Unused parameters for fake implementation
        public Task<bool> PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken cancellationToken)
#pragma warning restore RCS1163
        {
            LastPromotedSteamId = steamId;
            return Task.FromResult(PromoteResult);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        /// <summary>Raises <see cref="TeamMessageReceived"/> to simulate an inbound team chat line.</summary>
        /// <param name="line">The team chat line to raise.</param>
        public void RaiseTeamMessage(TeamChatLine line) => TeamMessageReceived?.Invoke(this, line);
    }
}
