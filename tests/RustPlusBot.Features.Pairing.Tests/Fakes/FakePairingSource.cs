using System.Collections.Concurrent;
using RustPlusBot.Features.Pairing.Listening;

namespace RustPlusBot.Features.Pairing.Tests.Fakes;

/// <summary>A scripted <see cref="IPairingSource"/>: each created listener returns the next queued outcome.</summary>
internal sealed class FakePairingSource : IPairingSource
{
    private readonly ConcurrentQueue<PairingConnectOutcome> _outcomes = new();

    private int _createCount;

    private int _disposeCount;

    /// <summary>How many listeners have been created.</summary>
    public int CreateCount => Volatile.Read(ref _createCount);

    /// <summary>How many created listeners have been disposed.</summary>
    public int DisposeCount => Volatile.Read(ref _disposeCount);

    /// <summary>The last notification callback registered by the supervisor.</summary>
    public Func<PairingNotification, CancellationToken, Task>? LastCallback { get; private set; }

    /// <summary>Signaled when the first Connected outcome fires.</summary>
    public TaskCompletionSource ConnectedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public IPairingListener Create(
        string fcmCredentialsJson,
        Func<PairingNotification, CancellationToken, Task> onNotification)
    {
        Interlocked.Increment(ref _createCount);
        LastCallback = onNotification;
        var outcome = _outcomes.TryDequeue(out var next) ? next : PairingConnectOutcome.Connected;
        return new FakeListener(outcome, () => ConnectedSignal.TrySetResult(),
            () => Interlocked.Increment(ref _disposeCount));
    }

    /// <summary>Enqueues an outcome to be returned by the next listener created.</summary>
    /// <param name="outcome">The outcome the next listener will return from <c>ConnectAsync</c>.</param>
    public void EnqueueOutcome(PairingConnectOutcome outcome) => _outcomes.Enqueue(outcome);

    private sealed class FakeListener(PairingConnectOutcome outcome, Action onConnected, Action onDisposed)
        : IPairingListener
    {
        public Task<PairingConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (outcome == PairingConnectOutcome.Connected)
            {
                onConnected();
            }

            return Task.FromResult(outcome);
        }

        public ValueTask DisposeAsync()
        {
            onDisposed();
            return ValueTask.CompletedTask;
        }
    }
}
