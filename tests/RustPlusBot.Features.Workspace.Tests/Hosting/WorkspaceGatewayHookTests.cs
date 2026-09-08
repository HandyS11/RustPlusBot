using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Hosting;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Hosting;

/// <summary>
/// Covers the wiring <see cref="WorkspaceHostedService"/> keeps outside the event loops: the once-per-process
/// startup heal hung off the gateway's Ready, and the self-heal hung off ChannelDestroyed. The Discord client
/// is a concrete class with no seam, so both events are raised by walking the client's own subscriber lists —
/// which is also what proves the handlers were attached at start and detached again at stop.
/// </summary>
public sealed class WorkspaceGatewayHookTests
{
    private const ulong GuildA = 11UL;
    private const ulong GuildB = 22UL;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Ready_heals_every_provisioned_guild()
    {
        using var h = Harness.Create();
        h.Store.GetProvisionedGuildIdsAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<ulong>>([GuildA, GuildB]);
        var healed = h.SignalOnHealAsync(2);

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.RaiseAsync("_readyEvent");
            await healed.WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Reconciler.Received(1).HealGuildAsync(GuildA, Arg.Any<CancellationToken>());
        await h.Reconciler.Received(1).HealGuildAsync(GuildB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_gateway_reconnect_does_not_re_run_the_startup_heal()
    {
        // Ready fires on every resume. The startup sweep walks every provisioned guild's channels over REST,
        // so re-running it on each reconnect would hammer the API and re-race the reconciler with itself.
        using var h = Harness.Create();
        h.Store.GetProvisionedGuildIdsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ulong>>([GuildA]);
        var healed = h.SignalOnHealAsync(1);

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.RaiseAsync("_readyEvent");
            await healed.WaitAsync(Patience);
            await h.RaiseAsync("_readyEvent");
            await h.RaiseAsync("_readyEvent");
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Reconciler.Received(1).HealGuildAsync(GuildA, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_startup_heal_is_logged_rather_than_left_on_an_unobserved_task()
    {
        // Nothing awaits the offloaded sweep, so an escaping exception would only ever surface as an
        // unobserved task fault — after the host had already reported a clean start.
        using var h = Harness.Create();
        var boom = new TimeoutException("The operation has timed out.");
        h.Store.GetProvisionedGuildIdsAsync(Arg.Any<CancellationToken>()).ThrowsAsync(boom);

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.RaiseAsync("_readyEvent");
            await h.Log.WhenErrorLoggedAsync().WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.Contains(h.Log.Errors, e => ReferenceEquals(e, boom));
    }

    [Fact]
    public async Task A_destroyed_channel_heals_the_guild_that_owned_it()
    {
        // Somebody deleting a managed channel by hand is the whole reason self-heal exists.
        using var h = Harness.Create();
        var healed = h.SignalOnHealAsync(1);

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.RaiseAsync("_channelDestroyedEvent", GuildChannel(GuildB));
            await healed.WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Reconciler.Received(1).HealGuildAsync(GuildB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_destroyed_channel_that_belongs_to_no_guild_heals_nothing()
    {
        // Group and DM channels reach the same handler and have no guild to reconcile against.
        using var h = Harness.Create();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.RaiseAsync("_channelDestroyedEvent",
                (SocketChannel)RuntimeHelpers.GetUninitializedObject(typeof(SocketDMChannel)));
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Reconciler.DidNotReceive().HealGuildAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_failing_self_heal_is_logged_and_leaves_the_service_healthy()
    {
        using var h = Harness.Create();
        var boom = new TimeoutException("The operation has timed out.");
        h.Reconciler.HealGuildAsync(GuildA, Arg.Any<CancellationToken>()).ThrowsAsync(boom);
        var healedB = h.SignalOnHealAsync(2);

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.RaiseAsync("_channelDestroyedEvent", GuildChannel(GuildA));
            await h.RaiseAsync("_channelDestroyedEvent", GuildChannel(GuildB));
            await healedB.WaitAsync(Patience);
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        Assert.Contains(h.Log.Errors, e => ReferenceEquals(e, boom));
        await h.Reconciler.Received(1).HealGuildAsync(GuildB, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task After_StopAsync_neither_gateway_hook_is_still_attached()
    {
        using var h = Harness.Create();
        h.Store.GetProvisionedGuildIdsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ulong>>([GuildA]);

        await h.Service.StartAsync(CancellationToken.None);
        await h.Service.StopAsync(CancellationToken.None);

        await h.RaiseAsync("_readyEvent");
        await h.RaiseAsync("_channelDestroyedEvent", GuildChannel(GuildB));

        await h.Reconciler.DidNotReceive().HealGuildAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_newly_registered_server_is_reconciled()
    {
        using var h = Harness.Create();
        var serverId = Guid.NewGuid();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.PublishUntilReconciledAsync(new ServerRegisteredEvent(GuildA, serverId));
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Reconciler.Received().ReconcileServerAsync(GuildA, serverId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_ready_info_map_reconciles_the_server_so_the_render_reaches_hash_info()
    {
        using var h = Harness.Create();
        var serverId = Guid.NewGuid();

        await h.Service.StartAsync(CancellationToken.None);
        try
        {
            await h.PublishUntilReconciledAsync(new InfoMapReadyEvent(GuildA, serverId));
        }
        finally
        {
            await h.Service.StopAsync(CancellationToken.None);
        }

        await h.Reconciler.Received().ReconcileServerAsync(GuildA, serverId, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Builds a <see cref="SocketGuildChannel"/> without going through Discord.Net's internal factories:
    /// the handler only ever reads <c>Guild.Id</c>, and there is no public way to construct either object.
    /// </summary>
    /// <param name="guildId">The guild the channel belongs to.</param>
    /// <returns>A channel whose <c>Guild.Id</c> is <paramref name="guildId"/>.</returns>
    private static SocketChannel GuildChannel(ulong guildId)
    {
        var guild = RuntimeHelpers.GetUninitializedObject(typeof(SocketGuild));
        SetAutoProperty(guild, "Id", guildId);
        var channel = RuntimeHelpers.GetUninitializedObject(typeof(SocketTextChannel));
        SetAutoProperty(channel, "Guild", guild);
        return (SocketChannel)channel;
    }

    /// <summary>Assigns an auto-property's compiler-generated backing field, wherever it is declared.</summary>
    /// <param name="target">The instance to write to.</param>
    /// <param name="name">The auto-property's name.</param>
    /// <param name="value">The value to store.</param>
    /// <exception cref="InvalidOperationException">No such auto-property exists on the type.</exception>
    private static void SetAutoProperty(object target, string name, object value)
    {
        for (var type = target.GetType(); type is not null; type = type.BaseType)
        {
            var field = type.GetField($"<{name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null)
            {
                field.SetValue(target, value);
                return;
            }
        }

        throw new InvalidOperationException($"Discord.Net no longer declares {name} as an auto-property.");
    }

    private sealed class Harness : IDisposable
    {
        private readonly SemaphoreSlim _heals = new(0);

        public required InMemoryEventBus Bus { get; init; }

        public required DiscordSocketClient Client { get; init; }

        public required ErrorLog Log { get; init; }

        public required ServiceProvider Provider { get; init; }

        public required IWorkspaceReconciler Reconciler { get; init; }

        public WorkspaceHostedService Service { get; private set; } = null!;

        public required IWorkspaceStore Store { get; init; }

        public void Dispose()
        {
            Service.Dispose();
            Provider.Dispose();
            Log.Dispose();
            _heals.Dispose();
        }

        public static Harness Create()
        {
            var reconciler = Substitute.For<IWorkspaceReconciler>();
            var store = Substitute.For<IWorkspaceStore>();
            store.GetProvisionedGuildIdsAsync(Arg.Any<CancellationToken>()).Returns<IReadOnlyList<ulong>>([]);

            var services = new ServiceCollection();
            services.AddScoped(_ => reconciler);
            services.AddScoped(_ => store);
            services.AddSingleton(Substitute.For<IWorkspaceRegistry>()); // StartAsync resolves this up front.
            var provider = services.BuildServiceProvider();

            var harness = new Harness
            {
                Bus = new InMemoryEventBus(),
                Client = new DiscordSocketClient(),
                Log = new ErrorLog(),
                Provider = provider,
                Reconciler = reconciler,
                Store = store,
            };
            reconciler.When(r => r.HealGuildAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()))
                .Do(_ => harness._heals.Release());
            harness.Service = new WorkspaceHostedService(harness.Client, harness.Bus,
                provider.GetRequiredService<IServiceScopeFactory>(), harness.Log);
            return harness;
        }

        /// <summary>Republishes until the reconciler has been asked to reconcile a server.</summary>
        /// <param name="evt">The event to publish.</param>
        /// <typeparam name="TEvent">The event type being published.</typeparam>
        /// <returns>A task that completes once a reconcile has been observed.</returns>
        public async Task PublishUntilReconciledAsync<TEvent>(TEvent evt)
            where TEvent : notnull
        {
            // The bus does not replay and the loops subscribe from inside a Task.Run, so the first publish
            // can land before anyone is listening.
            var deadline = DateTimeOffset.UtcNow + Patience;
            while (DateTimeOffset.UtcNow < deadline && !Reconciler.ReceivedCalls().Any(c =>
                       c.GetMethodInfo().Name == nameof(IWorkspaceReconciler.ReconcileServerAsync)))
            {
                await Bus.PublishAsync(evt);
                await Task.Delay(20);
            }
        }

        /// <summary>Invokes every handler attached to one of the client's gateway events.</summary>
        /// <param name="eventField">The event's backing field name on the client.</param>
        /// <param name="args">The arguments to pass to each handler.</param>
        /// <returns>A task that completes when every handler has run.</returns>
        /// <exception cref="InvalidOperationException">Discord.Net no longer names that backing field.</exception>
        public async Task RaiseAsync(string eventField, params object[] args)
        {
            var field = Client.GetType().GetField(eventField, BindingFlags.Instance | BindingFlags.NonPublic)
                        ?? throw new InvalidOperationException($"Discord.Net no longer declares {eventField}.");
            var asyncEvent = field.GetValue(Client)!;
            var subscriptions = (IEnumerable)asyncEvent.GetType().GetProperty("Subscriptions")!.GetValue(asyncEvent)!;
            foreach (var handler in subscriptions.Cast<Delegate>().ToList())
            {
                await (Task)handler.DynamicInvoke(args)!;
            }
        }

        /// <summary>Completes once <paramref name="count"/> guild heals have been requested.</summary>
        /// <param name="count">How many heals to wait for.</param>
        /// <returns>A task that completes when that many heals have started.</returns>
        public async Task SignalOnHealAsync(int count)
        {
            for (var i = 0; i < count; i++)
            {
                await _heals.WaitAsync(Patience);
            }
        }
    }

    /// <summary>Records the exceptions logged at Error and says when the first one lands.</summary>
    private sealed class ErrorLog : ILogger<WorkspaceHostedService>, IDisposable
    {
        private readonly SemaphoreSlim _errors = new(0);

        public ConcurrentBag<Exception> Errors { get; } = [];

        public void Dispose() => _errors.Dispose();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error && exception is not null)
            {
                Errors.Add(exception);
                _errors.Release();
            }
        }

        public async Task WhenErrorLoggedAsync() => await _errors.WaitAsync(Patience);
    }
}
