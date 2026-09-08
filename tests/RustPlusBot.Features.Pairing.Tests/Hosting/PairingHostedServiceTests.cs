using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RustPlusBot.Features.Pairing.Hosting;
using RustPlusBot.Features.Pairing.Supervisor;

namespace RustPlusBot.Features.Pairing.Tests.Hosting;

/// <summary>
/// Drives <see cref="PairingHostedService"/> through the gateway lifecycle it is wired to. The Discord
/// client is a concrete class with no seam, so the gateway's Ready event is raised by walking its
/// subscriber list — which also proves the handler really was attached, and really was detached on stop.
/// </summary>
public sealed class PairingHostedServiceTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Ready_starts_every_active_listener()
    {
        var (client, supervisor, service, _) = Build();
        var started = Signal(supervisor);

        await service.StartAsync(CancellationToken.None);
        await RaiseReadyAsync(client);
        await started.Task.WaitAsync(Patience);

        await supervisor.Received(1).StartAllActiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_gateway_reconnect_does_not_start_the_listeners_a_second_time()
    {
        // Ready fires again on every gateway resume. Starting the FCM listeners twice would leave the
        // first set orphaned — still connected, still delivering, with nothing left holding their handles.
        var (client, supervisor, service, _) = Build();
        var started = Signal(supervisor);

        await service.StartAsync(CancellationToken.None);
        await RaiseReadyAsync(client);
        await started.Task.WaitAsync(Patience);
        await RaiseReadyAsync(client);
        await RaiseReadyAsync(client);

        await supervisor.Received(1).StartAllActiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_stops_the_listeners_and_leaves_no_handler_on_the_gateway()
    {
        var (client, supervisor, service, _) = Build();

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);
        await RaiseReadyAsync(client);

        await supervisor.Received(1).StopAllAsync();
        await supervisor.DidNotReceive().StartAllActiveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_startup_failure_is_logged_instead_of_being_left_on_an_unobserved_task()
    {
        // The Ready handler offloads the FCM connect and nothing awaits the result, so an escaping
        // exception would surface only as an unobserved task fault long after the fact.
        var (client, supervisor, service, log) = Build();
        var boom = new InvalidOperationException("FCM refused the credentials.");
        supervisor.StartAllActiveAsync(Arg.Any<CancellationToken>()).ThrowsAsync(boom);

        await service.StartAsync(CancellationToken.None);
        await RaiseReadyAsync(client);
        await log.WhenErrorLoggedAsync().WaitAsync(Patience);

        Assert.Contains(log.Errors, e => ReferenceEquals(e, boom));
        await service.StopAsync(CancellationToken.None);
    }

    private static (DiscordSocketClient Client, IPairingSupervisor Supervisor, PairingHostedService Service,
        ErrorRecordingLogger<PairingHostedService> Log) Build()
    {
        var client = new DiscordSocketClient();
        var supervisor = Substitute.For<IPairingSupervisor>();
        var log = new ErrorRecordingLogger<PairingHostedService>();
        return (client, supervisor, new PairingHostedService(client, supervisor, log), log);
    }

    /// <summary>Invokes every handler currently attached to the client's Ready event.</summary>
    /// <param name="client">The gateway client whose Ready subscribers to run.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    /// <exception cref="InvalidOperationException">Discord.Net no longer backs Ready with <c>_readyEvent</c>.</exception>
    private static async Task RaiseReadyAsync(DiscordSocketClient client)
    {
        var field = client.GetType().GetField("_readyEvent", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new InvalidOperationException("Discord.Net no longer backs Ready with _readyEvent.");
        var asyncEvent = field.GetValue(client)!;
        foreach (Func<Task> handler in
                 (IEnumerable)asyncEvent.GetType().GetProperty("Subscriptions")!.GetValue(asyncEvent)!)
        {
            await handler();
        }
    }

    private static TaskCompletionSource Signal(IPairingSupervisor supervisor)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        supervisor.StartAllActiveAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return Task.CompletedTask;
            });
        return started;
    }

    /// <summary>An <see cref="ILogger{T}"/> that keeps the exceptions written at Error and says when one lands.</summary>
    /// <typeparam name="T">The logger's category type.</typeparam>
    private sealed class ErrorRecordingLogger<T> : ILogger<T>
    {
        private readonly TaskCompletionSource _logged = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<Exception> Errors { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Error || exception is null)
            {
                return;
            }

            Errors.Enqueue(exception);
            _logged.TrySetResult();
        }

        public Task WhenErrorLoggedAsync() => _logged.Task;
    }
}
