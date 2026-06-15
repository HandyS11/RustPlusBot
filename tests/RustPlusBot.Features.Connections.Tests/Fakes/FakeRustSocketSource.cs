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
/// </remarks>
internal sealed class FakeRustSocketSource : IRustSocketSource
{
    private readonly ConcurrentQueue<SocketConnectOutcome> _connectOutcomes = new();
    private readonly ConcurrentQueue<HeartbeatResult> _heartbeats = new();

    private int _createCount;

    /// <summary>Number of times <see cref="Create"/> has been called. Safe to read from any thread.</summary>
    public int CreateCount => Volatile.Read(ref _createCount);

    /// <summary>The IP address passed to the most recent <see cref="Create"/> call. Read after the operation under test has settled.</summary>
    public string? LastIp { get; private set; }

    /// <summary>The Steam ID passed to the most recent <see cref="Create"/> call. Read after the operation under test has settled.</summary>
    public ulong LastSteamId { get; private set; }

    public IRustServerConnection Create(string ip, int port, ulong steamId, string playerToken)
    {
        Interlocked.Increment(ref _createCount);
        LastIp = ip;
        LastSteamId = steamId;
        var outcome = _connectOutcomes.TryDequeue(out var next) ? next : SocketConnectOutcome.Connected;
        return new FakeConnection(outcome, _heartbeats);
    }

    public void EnqueueConnect(SocketConnectOutcome outcome) => _connectOutcomes.Enqueue(outcome);

    public void EnqueueHeartbeat(HeartbeatResult result) => _heartbeats.Enqueue(result);

    private sealed class FakeConnection(SocketConnectOutcome outcome, ConcurrentQueue<HeartbeatResult> heartbeats)
        : IRustServerConnection
    {
        public Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(outcome);

        public Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(heartbeats.TryDequeue(out var next) ? next : HeartbeatResult.Ok(0));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
