using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Connections.Tests.Fakes;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Connections.Tests;

/// <summary>
/// Covers the supervisor's clan-event republishing: the connect-time probe, in-game clan changes,
/// clan chat lines (with active-player classification), and unsubscription on disconnect.
/// Mirrors the harness established by <see cref="ConnectionSupervisorTests"/>.
/// </summary>
public sealed class ClanSupervisorTests
{
    private static Harness CreateHarness(FakeRustSocketSource source)
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Unprotect(Arg.Any<string>()).Returns(c => c.Arg<string>());

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var dm = Substitute.For<IUserDmSender>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton(protector);
        services.AddSingleton(dm);
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // Each scope opens its OWN connection to a shared-cache in-memory database, so the background
        // supervisor loop and the test's polling never run concurrent commands on a single SqliteConnection
        // (which throws "active statements" misuse errors). One kept-open connection keeps the in-memory DB alive.
        var connectionString = $"DataSource=clansup-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connectionString);
        keepAlive.Open();
        using (var seed = new BotDbContext(
                   new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options))
        {
            seed.Database.Migrate();
        }

        services.AddSingleton(keepAlive);
        services.AddScoped(_ => new BotDbContext(
            new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options));
        services.AddScoped<IConnectionStore, ConnectionStore>();
        services
            .AddScoped<RustPlusBot.Persistence.Servers.IServerService, RustPlusBot.Persistence.Servers.ServerService>();

        services.AddSingleton<IRustSocketSource>(source);
        services.AddSingleton(Options.Create(new ConnectionOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(1),
            InitialRetryDelay = TimeSpan.FromMilliseconds(5),
            MaxRetryDelay = TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(200),
            MarkerPollInterval = TimeSpan.FromMilliseconds(20),
            MarkerPollFastInterval = TimeSpan.FromMilliseconds(20),
        }));
        services.AddSingleton<ConnectionSecurity>();
        services.AddSingleton<ConnectionSupervisor>();

        var provider = services.BuildServiceProvider();
        return new Harness
        {
            Provider = provider,
            Dm = dm,
            Supervisor = provider.GetRequiredService<ConnectionSupervisor>(),
            Bus = provider.GetRequiredService<IEventBus>(),
        };
    }

    private static async Task<(Guid ServerId, Guid CredA, Guid CredB)> SeedAsync(
        ServiceProvider provider,
        CredentialStatus bStatus = CredentialStatus.Standby)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        var a = new PlayerCredential
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            OwnerUserId = 1UL,
            SteamId = 100UL,
            ProtectedPlayerToken = "111",
            Status = CredentialStatus.Active,
        };
        var b = new PlayerCredential
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            OwnerUserId = 2UL,
            SteamId = 200UL,
            ProtectedPlayerToken = "222",
            Status = bStatus,
        };
        await context.PlayerCredentials.AddRangeAsync(a, b);
        await context.SaveChangesAsync();
        return (server.Id, a.Id, b.Id);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(10, ct);
        }
    }

    [Fact]
    public async Task Publishes_a_clan_state_event_on_connect()
    {
        var snapshot = new ClanSnapshot(
            ClanId: 1L,
            Name: "Clan",
            Created: DateTimeOffset.UnixEpoch,
            Creator: 100UL,
            Motd: "MOTD",
            MotdTimestamp: null,
            MotdAuthor: null,
            LogoHash: null,
            Color: null,
            MaxMemberCount: null,
            Score: null,
            Roles: [],
            Members: [],
            Invites: []);
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<ClanStateChangedEvent>();
        // Register the subscription synchronously on this thread BEFORE any connection work runs.
        // InMemoryEventBus registers the channel eagerly when SubscribeAsync is invoked, so buffering
        // starts here. Deferring the call into the Task.Run below would race the supervisor's connect-time
        // probe publish.
        var stream = h.Bus.SubscribeAsync<ClanStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in stream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        // Staged before EnsureConnectionAsync so the probe reads it as soon as the connect window opens —
        // eliminates the race between the test assigning ClanProbe and the supervisor's connect-time read.
        source.SetClanProbe(ClanProbeResult.From(snapshot));

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);

        Assert.True(captured.TryPeek(out var evt));
        Assert.NotNull(evt);
        Assert.Equal(ClanProbeStatus.HasClan, evt!.Status);
        Assert.NotNull(evt.Snapshot);
        Assert.Equal(1L, evt.Snapshot!.ClanId);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Publishes_NoClan_on_connect_when_the_player_has_no_clan()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<ClanStateChangedEvent>();
        // Registered synchronously before any connection work runs — see comment above.
        var stream = h.Bus.SubscribeAsync<ClanStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in stream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        // FakeConnection.ClanProbe defaults to ClanProbeResult.NoClan — no staging required.
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);

        Assert.True(captured.TryPeek(out var evt));
        Assert.NotNull(evt);
        Assert.Equal(ClanProbeStatus.NoClan, evt!.Status);
        Assert.Null(evt.Snapshot);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Publishes_a_clan_state_event_when_the_socket_reports_a_change()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<ClanStateChangedEvent>();
        // Registered synchronously before any connection work runs — see comment in the first test above.
        var stream = h.Bus.SubscribeAsync<ClanStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in stream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);

        // Wait for the connect-time probe's NoClan event (default) before raising the in-game change, so
        // the two events are unambiguous and the second one asserted below is definitely the raised one.
        // Its arrival also proves OnClanChanged is already subscribed (subscription happens before the
        // connect-time probe call), so RaiseClanChanged below is guaranteed to reach the handler.
        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);
        Assert.NotNull(source.LastConnection);
        source.LastConnection!.RaiseClanChanged(ClanProbeResult.NoClan);

        await WaitUntilAsync(() => captured.Count >= 2, cts.Token);

        var events = captured.ToArray();
        Assert.Equal(ClanProbeStatus.NoClan, events[1].Status);
        Assert.Null(events[1].Snapshot);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Publishes_clan_messages_and_flags_the_active_player()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider); // credential A: SteamId 100UL is active

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<ClanMessageReceivedEvent>();
        // The connect-time ClanStateChangedEvent is published (see Step 4) only AFTER OnClanMessage/
        // OnClanChanged are subscribed, so waiting for it — rather than polling source.LastConnection —
        // proves the handlers are wired before RaiseClanMessage is called below. Both subscriptions are
        // registered synchronously here, before any connection work runs (see comment in the first test).
        var stateCaptured = new System.Collections.Concurrent.ConcurrentQueue<ClanStateChangedEvent>();
        var messageStream = h.Bus.SubscribeAsync<ClanMessageReceivedEvent>(cts.Token);
        var stateStream = h.Bus.SubscribeAsync<ClanStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in messageStream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);
        var stateSubTask = Task.Run(async () =>
        {
            await foreach (var e in stateStream)
            {
                stateCaptured.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => !stateCaptured.IsEmpty, cts.Token);

        Assert.NotNull(source.LastConnection);
        source.LastConnection!.RaiseClanMessage(new ClanChatLine(100UL, "Active", "hi", DateTimeOffset.UnixEpoch));
        source.LastConnection.RaiseClanMessage(new ClanChatLine(999UL, "Other", "yo", DateTimeOffset.UnixEpoch));

        await WaitUntilAsync(() => captured.Count >= 2, cts.Token);

        var events = captured.ToArray();
        var fromActive = Assert.Single(events, e => e.SenderSteamId == 100UL);
        Assert.True(fromActive.FromActivePlayer);
        Assert.Equal("hi", fromActive.Message);
        Assert.Equal("Active", fromActive.SenderName);

        var fromOther = Assert.Single(events, e => e.SenderSteamId == 999UL);
        Assert.False(fromOther.FromActivePlayer);
        Assert.Equal("yo", fromOther.Message);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }

        try
        {
            await stateSubTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Stops_publishing_clan_events_after_the_socket_closes()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<ClanMessageReceivedEvent>();
        // The connect-time ClanStateChangedEvent is published only AFTER OnClanMessage is subscribed (see
        // Step 4), so waiting for it proves the handler was really wired before it gets unsubscribed below —
        // otherwise this test would trivially pass because the handler was never subscribed in the first
        // place. Both subscriptions are registered synchronously here (see comment in the first test).
        var stateCaptured = new System.Collections.Concurrent.ConcurrentQueue<ClanStateChangedEvent>();
        var messageStream = h.Bus.SubscribeAsync<ClanMessageReceivedEvent>(cts.Token);
        var stateStream = h.Bus.SubscribeAsync<ClanStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in messageStream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);
        var stateSubTask = Task.Run(async () =>
        {
            await foreach (var e in stateStream)
            {
                stateCaptured.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => !stateCaptured.IsEmpty, cts.Token);

        Assert.NotNull(source.LastConnection);
        var closedConnection = source.LastConnection!;

        // Stop the connection loop — this joins RunConnectedAsync's finally block, which unsubscribes
        // the clan handlers, before StopAsync returns.
        await h.Supervisor.StopAsync(10UL, serverId);

        // The now-detached fake connection is still holding its event delegate references (it wasn't
        // disposed by the test); raising on it must reach no subscribed handler, so nothing publishes.
        closedConnection.RaiseClanMessage(new ClanChatLine(100UL, "Active", "hi", DateTimeOffset.UnixEpoch));

        // Give the bus a beat to deliver anything that (incorrectly) got published.
        await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);

        Assert.Empty(captured);

        await cts.CancelAsync();
        try
        {
            await subTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }

        try
        {
            await stateSubTask;
        }
        catch (OperationCanceledException)
        {
            /* expected */
        }
    }

    [Fact]
    public async Task Routes_a_team_send_to_team_chat_and_a_clan_send_to_clan_chat()
    {
        // One IChatSender, two destinations: assert the kind selects the right socket call and
        // that neither leaks into the other's buffer.
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => h.Supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        var teamResult =
            await h.Supervisor.SendAsync(ChatChannelKind.Team, 10UL, serverId, "[Alice] team line", cts.Token);
        var clanResult =
            await h.Supervisor.SendAsync(ChatChannelKind.Clan, 10UL, serverId, "[Bob] clan line", cts.Token);

        Assert.Equal(ChatSendResult.Sent, teamResult);
        Assert.Equal(ChatSendResult.Sent, clanResult);

        Assert.NotNull(source.LastConnection);
        var connection = source.LastConnection!;

        // Assert both destinations independently, so swapping the routing fails on both sides.
        Assert.Equal("[Alice] team line", Assert.Single(connection.SentMessages));
        Assert.Equal("[Bob] clan line", Assert.Single(connection.SentClanMessages));

        await h.Supervisor.StopAllAsync();
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IUserDmSender Dm { get; init; }
        public required ConnectionSupervisor Supervisor { get; init; }
        public required IEventBus Bus { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAllAsync();
            await Provider.DisposeAsync();
        }
    }
}
