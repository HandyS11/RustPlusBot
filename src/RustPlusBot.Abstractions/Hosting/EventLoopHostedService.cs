using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Hosting;

/// <summary>
/// Base class for a hosted service whose whole job is to run one or more <see cref="IEventBus"/>
/// consume loops. Subclasses declare their loops in <see cref="Loops"/> and inherit the start/stop
/// sequencing, the per-loop crash isolation and the handler-failure logging.
/// </summary>
/// <remarks>
/// <para>
/// Every subscription is established <em>synchronously inside <see cref="StartAsync"/></em>, before any
/// loop task is scheduled. <see cref="EventBusConsumption"/>'s remarks explain why that matters: a service
/// that subscribes inside its background <c>Task.Run</c> is only subscribed once that task is scheduled,
/// and every event published in the meantime is dropped. Enumerating <see cref="Loops"/> is what performs
/// those subscriptions, so it happens exactly once and is materialised before the first task starts.
/// </para>
/// <para>
/// A handler that throws costs its own event and nothing else: the subscription stays live. Only a fault
/// in the stream itself ends a loop, and that is logged and contained so the host survives.
/// </para>
/// </remarks>
public abstract partial class EventLoopHostedService : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly IEventBus _eventBus;
    private readonly ILogger _logger;
    private Task[] _loops = [];

    /// <summary>Initializes a new instance of the <see cref="EventLoopHostedService"/> class.</summary>
    /// <param name="eventBus">The in-process event bus the loops subscribe to.</param>
    /// <param name="logger">The logger used for handler failures and loop faults.</param>
    protected EventLoopHostedService(IEventBus eventBus, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(logger);

        _eventBus = eventBus;
        _logger = logger;
    }

    /// <summary>
    /// Gets the loops this service runs. Enumerated exactly once, from <see cref="StartAsync"/>, and each
    /// <see cref="Loop{TEvent}"/> call subscribes to the bus as it is evaluated.
    /// </summary>
    protected abstract IEnumerable<EventLoopRegistration> Loops { get; }

    /// <summary>Gets the token cancelled when the service stops. Valid from construction until disposal.</summary>
    protected CancellationToken StoppingToken => _cts.Token;

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        OnStarting();

        // Enumerating Loops is what subscribes: materialise it here, before the first Task.Run, so that
        // every subscription is live by the time this method returns.
        var registrations = Loops.ToArray();
        var loops = new Task[registrations.Length];
        for (var i = 0; i < registrations.Length; i++)
        {
            var registration = registrations[i];
            loops[i] = Task.Run(() => RunLoopAsync(registration), CancellationToken.None);
        }

        _loops = loops;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        OnStopping();
        await _cts.CancelAsync().ConfigureAwait(false);

        var loops = _loops;
        _loops = [];
        foreach (var loop in loops)
        {
            try
            {
#pragma warning disable VSTHRD003 // Our own loop tasks, joined on stop.
                await loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <summary>
    /// Declares one loop: subscribes to <typeparamref name="TEvent"/> now and returns a registration that
    /// drains that subscription, skipping the events whose handler throws.
    /// </summary>
    /// <typeparam name="TEvent">The event type to consume.</typeparam>
    /// <param name="name">The loop's name, used in the "loop faulted" log message.</param>
    /// <param name="handle">Handles one event; its failures are logged, not propagated.</param>
    /// <returns>The registration to return from <see cref="Loops"/>.</returns>
    protected EventLoopRegistration Loop<TEvent>(string name, Func<TEvent, CancellationToken, Task> handle)
        where TEvent : notnull
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handle);

        var events = _eventBus.SubscribeAsync<TEvent>(_cts.Token);
        return new EventLoopRegistration(
            name,
            (logger, cancellationToken) => events.ConsumeAsync(
                handle,
                ex => LogHandlerFailed(logger, ex, typeof(TEvent).Name),
                cancellationToken));
    }

    /// <summary>
    /// Called first thing in <see cref="StartAsync"/>, before any subscription, so a subclass can do
    /// synchronous fail-fast work whose exception must propagate out of <see cref="StartAsync"/>.
    /// </summary>
    protected virtual void OnStarting()
    {
        // Nothing by default.
    }

    /// <summary>Called first thing in <see cref="StopAsync"/>, before the loops are cancelled.</summary>
    protected virtual void OnStopping()
    {
        // Nothing by default.
    }

    /// <summary>Releases the resources used by this service.</summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Dispose();
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Handling {EventType} failed; skipping that event.")]
    private static partial void LogHandlerFailed(ILogger logger, Exception exception, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "The {LoopName} loop faulted.")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception, string loopName);

    private async Task RunLoopAsync(EventLoopRegistration registration)
    {
        try
        {
            await registration.RunAsync(_logger, _cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogLoopFaulted(_logger, ex, registration.Name);
        }
    }
}
