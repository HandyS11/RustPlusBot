# Subsystem 3a — Team chat bridge Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a two-way `#teamchat` ↔ in-game team chat bridge (one channel per registered server): in-game lines post into Discord via per-player webhooks, and messages typed in `#teamchat` relay into the game as the active player, with dedup-based loop prevention.

**Architecture:** Connections owns the live socket and gains team-chat send + a received-message hook; its supervisor publishes a new `TeamMessageReceivedEvent` on the in-process bus and implements a public `ITeamChatSender` seam. A new `Features.Chat` project owns the Discord side — a bus-subscriber relay that posts via webhook (dropping its own echoes), a gateway `MessageReceived` listener that records-then-sends, an in-memory dedup buffer, and a webhook poster. Workspace contributes the `#teamchat` channel spec and a public `ITeamChatChannelLocator` (channelId ⇄ (guild,server)). Cross-feature comms use the bus + thin seams, mirroring the established `ConnectionStatusChangedEvent` / `IServerWorkspaceRemover` pattern.

**Tech Stack:** .NET 10, C#, Discord.Net 3.20, RustPlusApi 2.0.0-beta.1, EF Core + SQLite (Persistord), xUnit + NSubstitute. Spec: `docs/superpowers/specs/2026-06-15-rustplusbot-3a-chat-bridge-design.md`.

**Conventions (every task):**
- Branch `feat/chat-bridge` off `develop`.
- Build: `dotnet build RustPlusBot.slnx`. Test all: `dotnet test RustPlusBot.slnx`. Single test: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~<Name>"`.
- Before any push: `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (the pre-push hook enforces ReSharper formatting Roslynator doesn't catch).
- XML doc comments on all public/internal members (strict analyzers). Never log/throw a token or secret.

---

## File Structure

**Created:**
- `src/RustPlusBot.Abstractions/Events/TeamMessageReceivedEvent.cs` — the bus event (game→Discord).
- `src/RustPlusBot.Features.Connections/Listening/TeamChatLine.cs` — raw inbound line raised by a socket.
- `src/RustPlusBot.Features.Connections/Listening/ITeamChatSender.cs` — public send seam + `TeamChatSendResult`.
- `src/RustPlusBot.Features.Chat/` — new project (csproj, `AddChat` extension).
- `src/RustPlusBot.Features.Chat/Relaying/RelayDedupBuffer.cs`
- `src/RustPlusBot.Features.Chat/Relaying/TeamChatRelay.cs`
- `src/RustPlusBot.Features.Chat/Inbound/InboundMessage.cs` + `InboundOutcome.cs` + `TeamChatInboundProcessor.cs`
- `src/RustPlusBot.Features.Chat/Webhooks/ITeamChatWebhookPoster.cs` + `DiscordTeamChatWebhookPoster.cs`
- `src/RustPlusBot.Features.Chat/Hosting/ChatHostedService.cs`
- `src/RustPlusBot.Features.Workspace/Locating/ITeamChatChannelLocator.cs` + `TeamChatChannelLocator.cs`
- `tests/RustPlusBot.Features.Chat.Tests/` — new test project.

**Modified:**
- `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs` — add send + received-message event.
- `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` — implement new members on real shim + `RejectedConnection`.
- `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` — live-socket registry, publish event, implement `ITeamChatSender`.
- `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs` — forward `ITeamChatSender`.
- `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` — fake supports raising/recording team messages.
- `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs` — `ServerTeamChat` key.
- `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs` — `#teamchat` spec.
- `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs` — EN/FR `channel.teamchat.name`.
- `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs` — register locator.
- `src/RustPlusBot.Persistence/Workspace/IWorkspaceStore.cs` + `WorkspaceStore.cs` — `GetChannelsByKeyAsync`.
- `src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs` — gateway intents.
- `src/RustPlusBot.Host/Program.cs` — `AddChat()`.
- `RustPlusBot.slnx` — two new projects.
- `docs/development/running-locally.md` — Message Content intent toggle.

---

## Task 1: `TeamMessageReceivedEvent` (Abstractions)

**Files:**
- Create: `src/RustPlusBot.Abstractions/Events/TeamMessageReceivedEvent.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/TeamMessageReceivedEventTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests;

public sealed class TeamMessageReceivedEventTests
{
    [Fact]
    public void Carries_all_fields()
    {
        var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 7656119UL, "Alice", "hello", FromActivePlayer: true);

        Assert.Equal(10UL, evt.GuildId);
        Assert.Equal(7656119UL, evt.SenderSteamId);
        Assert.Equal("Alice", evt.SenderName);
        Assert.Equal("hello", evt.Message);
        Assert.True(evt.FromActivePlayer);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamMessageReceivedEventTests"`
Expected: FAIL — `TeamMessageReceivedEvent` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when an in-game team chat line is received, so the chat bridge can relay it to Discord.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server whose socket received the line.</param>
/// <param name="SenderSteamId">The Steam64 id of the in-game sender.</param>
/// <param name="SenderName">The in-game display name of the sender.</param>
/// <param name="Message">The message text.</param>
/// <param name="FromActivePlayer">True when the sender is the bot's active player (used to drop relay echoes).</param>
public sealed record TeamMessageReceivedEvent(
    ulong GuildId,
    Guid ServerId,
    ulong SenderSteamId,
    string SenderName,
    string Message,
    bool FromActivePlayer);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamMessageReceivedEventTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Abstractions/Events/TeamMessageReceivedEvent.cs tests/RustPlusBot.Abstractions.Tests/TeamMessageReceivedEventTests.cs
git commit -m "feat(abstractions): add TeamMessageReceivedEvent"
```

---

## Task 2: Extend the socket seam (`IRustServerConnection` + `TeamChatLine`)

This task adds the send method and the received-message event to the connection seam, then updates the three implementations (real shim, `RejectedConnection`, and the test fake) so the solution compiles and the existing suite stays green. No new behavior test here — behavior lands in Task 3.

**Files:**
- Create: `src/RustPlusBot.Features.Connections/Listening/TeamChatLine.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`

- [ ] **Step 1: Create `TeamChatLine`**

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A raw team chat line as received from a socket (before active-player classification).</summary>
/// <param name="SteamId">The Steam64 id of the sender.</param>
/// <param name="Name">The in-game display name of the sender.</param>
/// <param name="Message">The message text.</param>
internal sealed record TeamChatLine(ulong SteamId, string Name, string Message);
```

- [ ] **Step 2: Extend `IRustServerConnection`**

Add these two members to the interface (keep existing members):

```csharp
    /// <summary>Sends a message to in-game team chat.</summary>
    /// <param name="message">The message text to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the send has been issued.</returns>
    Task SendTeamMessageAsync(string message, CancellationToken cancellationToken);

    /// <summary>Raised for every in-game team chat line received on this socket.</summary>
    event EventHandler<TeamChatLine>? TeamMessageReceived;
```

- [ ] **Step 3: Implement on `RejectedConnection` (in `RustPlusSocketSource.cs`)**

Add to the `RejectedConnection` private class (it never connects, so it never raises):

```csharp
        public Task SendTeamMessageAsync(string message, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public event EventHandler<TeamChatLine>? TeamMessageReceived
        {
            add { }
            remove { }
        }
```

- [ ] **Step 4: Implement on the real `RustPlusServerConnection` (in `RustPlusSocketSource.cs`)**

In the `RustPlusServerConnection` constructor, after `_rustPlus = new RustPlus(connection);`, wire the inbound event. Then add the members. **NOTE:** the `OnTeamChatReceived` handler arg shape (`SteamId`/`Name`/`Message`) and `SendTeamMessageAsync` signature are RustPlusApi `2.0.0-beta.1` members — verify exact names against the package during execution, as with the rest of this shim.

In the constructor:

```csharp
            // CONFIRMED (DLL): event OnTeamChatReceived with a TeamMessageEventArg carrying SteamId/Name/Message.
            // VERIFY exact member names at execution.
            _rustPlus.OnTeamChatReceived += OnTeamChatReceived;
```

Add the field + members to the class:

```csharp
        public event EventHandler<TeamChatLine>? TeamMessageReceived;

        public async Task SendTeamMessageAsync(string message, CancellationToken cancellationToken)
        {
            // CONFIRMED (DLL): RustPlus.SendTeamMessageAsync exists. VERIFY exact signature at execution.
            await _rustPlus.SendTeamMessageAsync(message).WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        private void OnTeamChatReceived(object? sender, RustPlusApi.Data.Events.TeamMessageEventArg e) =>
            TeamMessageReceived?.Invoke(this, new TeamChatLine(e.SteamId, e.Name, e.Message));
```

In `DisposeAsync`, before disposing the socket, unhook: `_rustPlus.OnTeamChatReceived -= OnTeamChatReceived;`

- [ ] **Step 5: Update the test fake `FakeConnection` (in `FakeRustSocketSource.cs`)**

Replace the `FakeConnection` private class with one that records sent messages and can raise inbound lines on demand:

```csharp
    private sealed class FakeConnection(SocketConnectOutcome outcome, FakeRustSocketSource source)
        : IRustServerConnection
    {
        public List<string> SentMessages { get; } = [];

        public event EventHandler<TeamChatLine>? TeamMessageReceived;

        public Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(outcome);

        public Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(source.NextHeartbeat());

        public Task SendTeamMessageAsync(string message, CancellationToken cancellationToken)
        {
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public void RaiseTeamMessage(TeamChatLine line) => TeamMessageReceived?.Invoke(this, line);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
```

Then expose the most-recently-created connection on the source so tests can drive/inspect it. Add a field and assign it in `Create`:

```csharp
    /// <summary>The connection produced by the most recent <see cref="Create"/> call (for driving inbound/inspecting sends).</summary>
    internal FakeConnection? LastConnection { get; private set; }
```

In `Create`, replace `return new FakeConnection(outcome, this);` with:

```csharp
        var connection = new FakeConnection(outcome, this);
        LastConnection = connection;
        return connection;
```

Change `FakeConnection` and `LastConnection` visibility so the test class (same assembly) can use them — `internal` is sufficient.

- [ ] **Step 6: Build + run the full suite**

Run: `dotnet build RustPlusBot.slnx` then `dotnet test RustPlusBot.slnx`
Expected: build succeeds; all 121 existing tests still pass.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Listening tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs
git commit -m "feat(connections): add team-chat send + received-message to the socket seam"
```

---

## Task 3: `ITeamChatSender` + supervisor send/publish

The supervisor registers each live socket during its connected window, publishes `TeamMessageReceivedEvent` for inbound lines (stamping `FromActivePlayer`), and implements `ITeamChatSender`.

**Files:**
- Create: `src/RustPlusBot.Features.Connections/Listening/ITeamChatSender.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Modify: `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/TeamChatSenderTests.cs`

- [ ] **Step 1: Create the `ITeamChatSender` seam**

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>The outcome of relaying a Discord message into in-game team chat.</summary>
public enum TeamChatSendResult
{
    /// <summary>The message was handed to the live socket.</summary>
    Sent = 0,

    /// <summary>There is no live socket for that (guild, server) right now.</summary>
    NotConnected = 1,

    /// <summary>A live socket exists but the send failed.</summary>
    Failed = 2,
}

/// <summary>Relays a message into a server's in-game team chat (implemented by the connection supervisor).</summary>
public interface ITeamChatSender
{
    /// <summary>Sends <paramref name="message"/> to the live socket for (<paramref name="guildId"/>, <paramref name="serverId"/>).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="message">The message text to relay.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The send result.</returns>
    Task<TeamChatSendResult> SendAsync(ulong guildId, Guid serverId, string message, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write the failing tests**

Add to `tests/RustPlusBot.Features.Connections.Tests/`. Reuse the existing `CreateHarness`/`SeedAsync` helpers' approach; this file uses its own minimal harness mirroring `ConnectionSupervisorTests` (shared-cache in-memory SQLite). Three tests: publish-on-inbound, send-routes-when-connected, send-NotConnected-when-absent.

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Connections.Tests.Fakes;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamChatSenderTests
{
    private static (ServiceProvider Provider, ConnectionSupervisor Supervisor, IEventBus Bus) CreateHarness(
        FakeRustSocketSource source)
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Unprotect(Arg.Any<string>()).Returns(c => c.Arg<string>());
        var dm = Substitute.For<IUserDmSender>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(protector);
        services.AddSingleton(dm);
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        var cs = $"DataSource=teamchat-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(cs);
        keepAlive.Open();
        using (var seed = new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options))
        {
            seed.Database.Migrate();
        }

        services.AddSingleton(keepAlive);
        services.AddScoped(_ => new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options));
        services.AddScoped<IConnectionStore, ConnectionStore>();
        services.AddScoped<IServerService, ServerService>();
        services.AddSingleton<IRustSocketSource>(source);
        services.AddSingleton(Options.Create(new ConnectionOptions
        {
            ConnectTimeout = TimeSpan.FromSeconds(1),
            InitialRetryDelay = TimeSpan.FromMilliseconds(5),
            MaxRetryDelay = TimeSpan.FromMilliseconds(20),
            HeartbeatInterval = TimeSpan.FromMilliseconds(20),
            HeartbeatTimeout = TimeSpan.FromMilliseconds(200),
        }));
        services.AddSingleton<ConnectionSupervisor>();

        var provider = services.BuildServiceProvider();
        return (provider, provider.GetRequiredService<ConnectionSupervisor>(),
            provider.GetRequiredService<IEventBus>());
    }

    private static async Task<Guid> SeedServerWithActiveAsync(ServiceProvider provider, ulong steamId)
    {
        using var scope = provider.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        ctx.RustServers.Add(server);
        ctx.PlayerCredentials.Add(new PlayerCredential
        {
            GuildId = 10UL, RustServerId = server.Id, OwnerUserId = 1UL, SteamId = steamId,
            ProtectedPlayerToken = "123", Status = CredentialStatus.Active,
        });
        await ctx.SaveChangesAsync();
        return server.Id;
    }

    [Fact]
    public async Task Inbound_line_publishes_event_with_active_player_flag()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, bus) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var received = new TaskCompletionSource<TeamMessageReceivedEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
        var subscription = Task.Run(async () =>
        {
            await foreach (var e in bus.SubscribeAsync<TeamMessageReceivedEvent>(cts.Token))
            {
                received.TrySetResult(e);
                return;
            }
        }, cts.Token);

        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        // Wait until the socket is live, then raise an inbound line from a NON-active steam id.
        await WaitUntilAsync(() => source.LastConnection is not null, cts.Token);
        await WaitUntilAsync(() => SocketRegistered(supervisor, 10UL, serverId), cts.Token);
        source.LastConnection!.RaiseTeamMessage(new TeamChatLine(999UL, "Bob", "[Alice] hi"));

        var evt = await received.Task.WaitAsync(cts.Token);
        Assert.Equal("Bob", evt.SenderName);
        Assert.Equal("[Alice] hi", evt.Message);
        Assert.False(evt.FromActivePlayer);

        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task SendAsync_routes_to_live_socket_when_connected()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var _p = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => SocketRegistered(supervisor, 10UL, serverId), cts.Token);

        var result = await supervisor.SendAsync(10UL, serverId, "[Alice] hi", cts.Token);

        Assert.Equal(TeamChatSendResult.Sent, result);
        Assert.Contains("[Alice] hi", source.LastConnection!.SentMessages);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task SendAsync_returns_NotConnected_when_no_socket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor, _) = CreateHarness(source);
        await using var _p = provider;

        var result = await supervisor.SendAsync(10UL, Guid.NewGuid(), "hi", CancellationToken.None);

        Assert.Equal(TeamChatSendResult.NotConnected, result);
    }

    private static bool SocketRegistered(ConnectionSupervisor supervisor, ulong guild, Guid server) =>
        supervisor.HasLiveSocket(guild, server);

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(10, ct);
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamChatSenderTests"`
Expected: FAIL — `ConnectionSupervisor` has no `SendAsync` / `HasLiveSocket`, and no event is published.

- [ ] **Step 4: Implement in `ConnectionSupervisor`**

Add `ITeamChatSender` to the class declaration:

```csharp
internal sealed partial class ConnectionSupervisor(
    ...) : IConnectionSupervisor, ITeamChatSender, IAsyncDisposable
```

Add a live-socket registry field (next to `_connections`):

```csharp
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), LiveSocket> _liveSockets = new();
```

Add the `LiveSocket` record (near the `Prepared` record):

```csharp
    private sealed record LiveSocket(IRustServerConnection Connection, ulong ActiveSteamId);
```

Change `RunConnectedAsync` to receive the active SteamId, register the live socket, subscribe to inbound lines, and clean up in a `finally`. Update the signature and call site (`RunAsync` line ~190 passes `p.CredentialId`; also pass `p.SteamId`):

Call site change in `RunAsync`:

```csharp
                    reason = await RunConnectedAsync(key, connection, p.CredentialId, p.SteamId, ct).ConfigureAwait(false);
```

New `RunConnectedAsync` body (replace the existing method):

```csharp
    private async Task<ReconnectReason> RunConnectedAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        Guid credentialId,
        ulong activeSteamId,
        CancellationToken ct)
    {
        var first = await connection.GetInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
        if (first.Kind == HeartbeatKind.AuthRejected)
        {
            return ReconnectReason.AuthRejected;
        }

        if (first.Kind == HeartbeatKind.Unreachable)
        {
            return ReconnectReason.Unreachable;
        }

        await PublishStatusAsync(key, ConnectionStatus.Connected, first.PlayerCount, credentialId, ct)
            .ConfigureAwait(false);

        void OnTeamMessage(object? sender, TeamChatLine line) =>
            _ = PublishTeamMessageAsync(key, activeSteamId, line);

        connection.TeamMessageReceived += OnTeamMessage;
        _liveSockets[key] = new LiveSocket(connection, activeSteamId);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(_options.HeartbeatInterval, ct).ConfigureAwait(false);
                var beat = await connection.GetInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
                switch (beat.Kind)
                {
                    case HeartbeatKind.Ok:
                        await PublishStatusAsync(key, ConnectionStatus.Connected, beat.PlayerCount, credentialId, ct)
                            .ConfigureAwait(false);
                        break;
                    case HeartbeatKind.AuthRejected:
                        return ReconnectReason.AuthRejected;
                    default:
                        return ReconnectReason.Unreachable;
                }
            }

            return ReconnectReason.Stopped;
        }
        finally
        {
            _liveSockets.TryRemove(key, out _);
            connection.TeamMessageReceived -= OnTeamMessage;
        }
    }
```

Add the publish helper, the test accessor, and the `SendAsync` implementation (place near `PublishStatusAsync`):

```csharp
    /// <summary>Test seam: true when a live socket is currently registered for the key.</summary>
    internal bool HasLiveSocket(ulong guildId, Guid serverId) => _liveSockets.ContainsKey((guildId, serverId));

    /// <inheritdoc />
    public async Task<TeamChatSendResult> SendAsync(
        ulong guildId,
        Guid serverId,
        string message,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return TeamChatSendResult.NotConnected;
        }

        try
        {
            await live.Connection.SendTeamMessageAsync(message, cancellationToken).ConfigureAwait(false);
            return TeamChatSendResult.Sent;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a failed relay send must not crash the caller; report Failed.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSendFailed(logger, ex, serverId);
            return TeamChatSendResult.Failed;
        }
    }

    private async Task PublishTeamMessageAsync((ulong Guild, Guid Server) key, ulong activeSteamId, TeamChatLine line)
    {
        try
        {
            var evt = new TeamMessageReceivedEvent(
                key.Guild, key.Server, line.SteamId, line.Name, line.Message, line.SteamId == activeSteamId);
            await eventBus.PublishAsync(evt, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a publish failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPublishTeamMessageFailed(logger, ex, key.Server);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Relaying a message to team chat for server {ServerId} failed.")]
    private static partial void LogSendFailed(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing a received team message for server {ServerId} failed.")]
    private static partial void LogPublishTeamMessageFailed(ILogger logger, Exception exception, Guid serverId);
```

- [ ] **Step 5: Forward `ITeamChatSender` in DI**

In `ConnectionServiceCollectionExtensions.AddConnections`, replace:

```csharp
        services.AddSingleton<IConnectionSupervisor, ConnectionSupervisor>();
```

with:

```csharp
        services.AddSingleton<ConnectionSupervisor>();
        services.AddSingleton<IConnectionSupervisor>(sp => sp.GetRequiredService<ConnectionSupervisor>());
        services.AddSingleton<ITeamChatSender>(sp => sp.GetRequiredService<ConnectionSupervisor>());
```

Add `using RustPlusBot.Features.Connections.Listening;` if not present.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamChatSenderTests"` then the full `dotnet test RustPlusBot.slnx`.
Expected: the 3 new tests PASS; all existing tests still PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests/TeamChatSenderTests.cs
git commit -m "feat(connections): publish TeamMessageReceivedEvent + implement ITeamChatSender"
```

---

## Task 4: `#teamchat` channel spec, key, and localization (Workspace)

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/ServerWorkspaceSpecProviderTests.cs` (create if absent)

- [ ] **Step 1: Write the failing test**

Create or extend the spec-provider test:

```csharp
using RustPlusBot.Features.Workspace;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Specs;

namespace RustPlusBot.Features.Workspace.Tests;

public sealed class ServerWorkspaceSpecProviderTests
{
    [Fact]
    public void Contributes_teamchat_as_interactive_per_server_channel()
    {
        var provider = new ServerWorkspaceSpecProvider();

        var teamchat = provider.GetChannelSpecs()
            .Single(c => c.Key == WorkspaceChannelKeys.ServerTeamChat);

        Assert.Equal(WorkspaceScope.PerServer, teamchat.Scope);
        Assert.Equal(ChannelPermissionProfile.Interactive, teamchat.Permissions);
        Assert.Equal("channel.teamchat.name", teamchat.NameKey);
    }
}
```

If `ServerWorkspaceSpecProvider`/`ChannelSpec`/`WorkspaceChannelKeys` are `internal`, the Workspace project already grants `InternalsVisibleTo` to its test project (verify in the csproj; the other Features projects do). If not present, add `<InternalsVisibleTo Include="RustPlusBot.Features.Workspace.Tests" />`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~ServerWorkspaceSpecProviderTests"`
Expected: FAIL — `WorkspaceChannelKeys.ServerTeamChat` does not exist.

- [ ] **Step 3: Add the channel key**

In `WorkspaceKeys.cs`, add to `WorkspaceChannelKeys`:

```csharp
    /// <summary>Key for the per-server #teamchat channel.</summary>
    public const string ServerTeamChat = "teamchat";
```

- [ ] **Step 4: Add the channel spec**

In `ServerWorkspaceSpecProvider.GetChannelSpecs`, add the `#teamchat` entry after `#info` (order 1 so it sorts after info):

```csharp
    public IEnumerable<ChannelSpec> GetChannelSpecs() =>
    [
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerInfo, "channel.info.name",
            ChannelPermissionProfile.ReadOnly, 0),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerTeamChat, "channel.teamchat.name",
            ChannelPermissionProfile.Interactive, 1),
    ];
```

- [ ] **Step 5: Add EN/FR localization**

In `LocalizationCatalog.Default`, add to the `"en"` dictionary: `["channel.teamchat.name"] = "teamchat",`
and to the `"fr"` dictionary: `["channel.teamchat.name"] = "tchat-equipe",`

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~ServerWorkspaceSpecProviderTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests/ServerWorkspaceSpecProviderTests.cs
git commit -m "feat(workspace): provision a per-server #teamchat channel (EN/FR)"
```

---

## Task 5: `GetChannelsByKeyAsync` on the workspace store

The locator needs all `#teamchat` rows across guilds (for the reverse `channelId → (guild,server)` lookup).

**Files:**
- Modify: `src/RustPlusBot.Persistence/Workspace/IWorkspaceStore.cs`
- Modify: `src/RustPlusBot.Persistence/Workspace/WorkspaceStore.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/WorkspaceStoreByKeyTests.cs`

- [ ] **Step 1: Write the failing test**

Mirror the existing persistence test harness (look at a sibling test in `tests/RustPlusBot.Persistence.Tests/` for the in-memory SQLite setup helper and reuse it):

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Persistence.Tests;

public sealed class WorkspaceStoreByKeyTests
{
    private static BotDbContext NewContext()
    {
        var ctx = new BotDbContext(new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite($"DataSource=wsbykey-{Guid.NewGuid():N};Mode=Memory;Cache=Shared").Options);
        ctx.Database.OpenConnection();
        ctx.Database.Migrate();
        return ctx;
    }

    [Fact]
    public async Task GetChannelsByKeyAsync_returns_all_rows_with_that_key()
    {
        await using var ctx = NewContext();
        var store = new WorkspaceStore(ctx);
        var serverId = Guid.NewGuid();
        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 10UL, RustServerId = serverId, ChannelKey = "teamchat", DiscordChannelId = 777UL,
        });
        await store.SaveChannelAsync(new ProvisionedChannel
        {
            GuildId = 10UL, RustServerId = serverId, ChannelKey = "info", DiscordChannelId = 888UL,
        });

        var rows = await store.GetChannelsByKeyAsync("teamchat");

        Assert.Single(rows);
        Assert.Equal(777UL, rows[0].DiscordChannelId);
    }
}
```

> If the persistence tests use a shared base/fixture, follow it instead of the inline `NewContext` above; match the existing file's pattern exactly.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~WorkspaceStoreByKeyTests"`
Expected: FAIL — `GetChannelsByKeyAsync` not defined.

- [ ] **Step 3: Add to the interface**

In `IWorkspaceStore.cs`:

```csharp
    /// <summary>Gets every provisioned channel with the given key across all guilds and scopes.</summary>
    /// <param name="channelKey">The stable channel key (e.g. "teamchat").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>All provisioned channels with that key.</returns>
    Task<IReadOnlyList<ProvisionedChannel>> GetChannelsByKeyAsync(
        string channelKey,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement in `WorkspaceStore`**

Mirror the existing `GetChannelsAsync` implementation style:

```csharp
    public async Task<IReadOnlyList<ProvisionedChannel>> GetChannelsByKeyAsync(
        string channelKey,
        CancellationToken cancellationToken = default) =>
        await context.ProvisionedChannels
            .Where(c => c.ChannelKey == channelKey)
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
```

> Match the exact field name (`context` vs `_context`) and `AsNoTracking()` usage of the existing methods in this file.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~WorkspaceStoreByKeyTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Persistence/Workspace tests/RustPlusBot.Persistence.Tests/WorkspaceStoreByKeyTests.cs
git commit -m "feat(persistence): add GetChannelsByKeyAsync to the workspace store"
```

---

## Task 6: `ITeamChatChannelLocator` (Workspace)

A singleton that caches the `#teamchat` channel set (TTL-based, driven by `IClock`) and resolves both directions.

**Files:**
- Create: `src/RustPlusBot.Features.Workspace/Locating/ITeamChatChannelLocator.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/TeamChatChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/TeamChatChannelLocatorTests.cs`

- [ ] **Step 1: Create the public interface**

```csharp
namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the per-server #teamchat channel in both directions (for the chat bridge).</summary>
public interface ITeamChatChannelLocator
{
    /// <summary>Gets the Discord channel id of the #teamchat for (<paramref name="guildId"/>, <paramref name="serverId"/>), or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);

    /// <summary>Resolves a Discord channel id to its (guild, server) if it is a #teamchat channel, else null.</summary>
    /// <param name="channelId">The Discord channel snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The owning (guild, server), or null if the channel is not a #teamchat.</returns>
    Task<(ulong GuildId, Guid ServerId)?> ResolveAsync(ulong channelId, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests;

public sealed class TeamChatChannelLocatorTests
{
    private static (TeamChatChannelLocator Locator, ServiceProvider Provider, Guid ServerId) Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var cs = $"DataSource=locator-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(cs);
        keepAlive.Open();
        using (var seed = new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options))
        {
            seed.Database.Migrate();
        }

        var services = new ServiceCollection();
        services.AddSingleton(keepAlive);
        services.AddScoped(_ => new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options));
        services.AddScoped<IWorkspaceStore, WorkspaceStore>();
        var provider = services.BuildServiceProvider();

        var serverId = Guid.NewGuid();
        using (var scope = provider.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            store.SaveChannelAsync(new ProvisionedChannel
            {
                GuildId = 10UL, RustServerId = serverId, ChannelKey = "teamchat", DiscordChannelId = 777UL,
            }).GetAwaiter().GetResult();
        }

        var locator = new TeamChatChannelLocator(
            provider.GetRequiredService<IServiceScopeFactory>(), clock);
        return (locator, provider, serverId);
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_provisioned_channel()
    {
        var (locator, provider, serverId) = Build();
        await using var _ = provider;

        var channelId = await locator.GetChannelIdAsync(10UL, serverId, CancellationToken.None);

        Assert.Equal(777UL, channelId);
    }

    [Fact]
    public async Task ResolveAsync_maps_channel_to_guild_and_server()
    {
        var (locator, provider, serverId) = Build();
        await using var _ = provider;

        var resolved = await locator.ResolveAsync(777UL, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(10UL, resolved!.Value.GuildId);
        Assert.Equal(serverId, resolved.Value.ServerId);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_for_unknown_channel()
    {
        var (locator, provider, _) = Build();
        await using var _p = provider;

        Assert.Null(await locator.ResolveAsync(123456UL, CancellationToken.None));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamChatChannelLocatorTests"`
Expected: FAIL — `TeamChatChannelLocator` not defined.

- [ ] **Step 4: Implement the locator**

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>
/// Caches the small set of provisioned #teamchat channels (rebuilt when the cache goes stale) and resolves
/// both directions. The Discord MessageReceived handler calls <see cref="ResolveAsync"/> for every guild
/// message, so a per-call DB hit is avoided by serving hits AND misses from the cached snapshot.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class TeamChatChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : ITeamChatChannelLocator
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private DateTimeOffset _builtAt = DateTimeOffset.MinValue;
    private IReadOnlyDictionary<ulong, (ulong GuildId, Guid ServerId)> _byChannelId =
        new Dictionary<ulong, (ulong, Guid)>();
    private IReadOnlyDictionary<(ulong, Guid), ulong> _byServer = new Dictionary<(ulong, Guid), ulong>();

    /// <inheritdoc />
    public async Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        return _byServer.TryGetValue((guildId, serverId), out var id) ? id : null;
    }

    /// <inheritdoc />
    public async Task<(ulong GuildId, Guid ServerId)?> ResolveAsync(ulong channelId, CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        return _byChannelId.TryGetValue(channelId, out var pair) ? pair : null;
    }

    private async Task EnsureFreshAsync(CancellationToken cancellationToken)
    {
        if (clock.UtcNow - _builtAt < CacheTtl)
        {
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (clock.UtcNow - _builtAt < CacheTtl)
            {
                return;
            }

            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
                var rows = await store.GetChannelsByKeyAsync(WorkspaceChannelKeys.ServerTeamChat, cancellationToken)
                    .ConfigureAwait(false);

                var byChannel = new Dictionary<ulong, (ulong, Guid)>();
                var byServer = new Dictionary<(ulong, Guid), ulong>();
                foreach (var row in rows)
                {
                    if (row.RustServerId is not { } serverId)
                    {
                        continue;
                    }

                    byChannel[row.DiscordChannelId] = (row.GuildId, serverId);
                    byServer[(row.GuildId, serverId)] = row.DiscordChannelId;
                }

                _byChannelId = byChannel;
                _byServer = byServer;
                _builtAt = clock.UtcNow;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
```

- [ ] **Step 5: Register in DI**

In `WorkspaceServiceCollectionExtensions` (the `AddWorkspace` method), register the locator as a singleton:

```csharp
        services.AddSingleton<ITeamChatChannelLocator, TeamChatChannelLocator>();
```

Add `using RustPlusBot.Features.Workspace.Locating;`. Confirm `IClock` is already registered by the Host/Abstractions (it is used elsewhere); if `AddWorkspace` needs it, it is supplied by the composed container.

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamChatChannelLocatorTests"` then full `dotnet test RustPlusBot.slnx`.
Expected: 3 new tests PASS; everything else PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests/TeamChatChannelLocatorTests.cs
git commit -m "feat(workspace): add ITeamChatChannelLocator with a TTL cache"
```

---

## Task 7: `Features.Chat` project scaffold + `RelayDedupBuffer`

**Files:**
- Create: `src/RustPlusBot.Features.Chat/RustPlusBot.Features.Chat.csproj`
- Create: `tests/RustPlusBot.Features.Chat.Tests/RustPlusBot.Features.Chat.Tests.csproj`
- Modify: `RustPlusBot.slnx`
- Create: `src/RustPlusBot.Features.Chat/Relaying/RelayDedupBuffer.cs`
- Test: `tests/RustPlusBot.Features.Chat.Tests/RelayDedupBufferTests.cs`

- [ ] **Step 1: Create the source project**

`src/RustPlusBot.Features.Chat/RustPlusBot.Features.Chat.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="RustPlusBot.Features.Chat.Tests" />
    <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
    <ProjectReference Include="..\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
    <ProjectReference Include="..\RustPlusBot.Domain\RustPlusBot.Domain.csproj" />
    <ProjectReference Include="..\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
    <ProjectReference Include="..\RustPlusBot.Features.Workspace\RustPlusBot.Features.Workspace.csproj" />
    <ProjectReference Include="..\RustPlusBot.Features.Connections\RustPlusBot.Features.Connections.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Discord.Net" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
  </ItemGroup>

</Project>
```

> `Features.Chat` references `Features.Connections` (for `ITeamChatSender`) and `Features.Workspace` (for `ITeamChatChannelLocator`) — both are public seams. This is the established cross-feature seam direction.

- [ ] **Step 2: Create the test project**

`tests/RustPlusBot.Features.Chat.Tests/RustPlusBot.Features.Chat.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\RustPlusBot.Features.Chat\RustPlusBot.Features.Chat.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Register both projects in `RustPlusBot.slnx`**

Add under the src group: `<Project Path="src/RustPlusBot.Features.Chat/RustPlusBot.Features.Chat.csproj" />`
Add under the tests group: `<Project Path="tests/RustPlusBot.Features.Chat.Tests/RustPlusBot.Features.Chat.Tests.csproj" />`

- [ ] **Step 4: Write the failing dedup-buffer test**

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Relaying;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class RelayDedupBufferTests
{
    private static (RelayDedupBuffer Buffer, IClock Clock) Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        return (new RelayDedupBuffer(clock), clock);
    }

    [Fact]
    public void Consumes_a_matching_entry_once()
    {
        var (buffer, _) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(key, "[Alice] hi");

        Assert.True(buffer.TryConsume(key, "[Alice] hi"));
        Assert.False(buffer.TryConsume(key, "[Alice] hi")); // already consumed
    }

    [Fact]
    public void Does_not_match_other_text()
    {
        var (buffer, _) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(key, "[Alice] hi");

        Assert.False(buffer.TryConsume(key, "[Bob] hi"));
    }

    [Fact]
    public void Entry_expires_after_ttl()
    {
        var (buffer, clock) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(key, "[Alice] hi");

        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch + TimeSpan.FromMinutes(1));

        Assert.False(buffer.TryConsume(key, "[Alice] hi"));
    }

    [Fact]
    public void Two_identical_records_consume_independently()
    {
        var (buffer, _) = Build();
        var key = (10UL, Guid.Empty);
        buffer.Record(key, "[Alice] hi");
        buffer.Record(key, "[Alice] hi");

        Assert.True(buffer.TryConsume(key, "[Alice] hi"));
        Assert.True(buffer.TryConsume(key, "[Alice] hi"));
        Assert.False(buffer.TryConsume(key, "[Alice] hi"));
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~RelayDedupBufferTests"`
Expected: FAIL — `RelayDedupBuffer` not defined (and the new projects must build).

- [ ] **Step 6: Implement `RelayDedupBuffer`**

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Chat.Relaying;

/// <summary>
/// Short-lived record of lines the bridge relayed into the game, keyed by (guild, server). When the bot's
/// active player echoes a relayed line back on the socket, <see cref="TryConsume"/> matches and removes one
/// entry so the relay drops the echo instead of re-posting it to Discord.
/// </summary>
/// <param name="clock">Drives entry expiry.</param>
internal sealed class RelayDedupBuffer(IClock clock)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(15);
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), List<Entry>> _entries = new();

    /// <summary>Records that <paramref name="text"/> was relayed into the game for <paramref name="key"/>.</summary>
    /// <param name="key">The (guild, server) the line was relayed to.</param>
    /// <param name="text">The exact formatted text that was sent.</param>
    public void Record((ulong Guild, Guid Server) key, string text)
    {
        var list = _entries.GetOrAdd(key, _ => []);
        lock (list)
        {
            Prune(list);
            list.Add(new Entry(text, clock.UtcNow + Ttl));
        }
    }

    /// <summary>Removes and returns true for the first live entry exactly matching <paramref name="text"/>.</summary>
    /// <param name="key">The (guild, server) the echo arrived on.</param>
    /// <param name="text">The echoed text to match.</param>
    /// <returns>True if a matching entry was consumed (the line is our own echo).</returns>
    public bool TryConsume((ulong Guild, Guid Server) key, string text)
    {
        if (!_entries.TryGetValue(key, out var list))
        {
            return false;
        }

        lock (list)
        {
            Prune(list);
            var index = list.FindIndex(e => string.Equals(e.Text, text, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            list.RemoveAt(index);
            return true;
        }
    }

    private void Prune(List<Entry> list)
    {
        var now = clock.UtcNow;
        list.RemoveAll(e => e.ExpiresAt <= now);
    }

    private readonly record struct Entry(string Text, DateTimeOffset ExpiresAt);
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~RelayDedupBufferTests"`
Expected: 4 tests PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Chat tests/RustPlusBot.Features.Chat.Tests RustPlusBot.slnx
git commit -m "feat(chat): scaffold Features.Chat project + RelayDedupBuffer"
```

---

## Task 8: `ITeamChatWebhookPoster` seam + real impl

**Files:**
- Create: `src/RustPlusBot.Features.Chat/Webhooks/ITeamChatWebhookPoster.cs`
- Create: `src/RustPlusBot.Features.Chat/Webhooks/DiscordTeamChatWebhookPoster.cs`

This is the second untested integration shim (Discord.Net webhook API). No unit test; verified by build + the final smoke run. The relay (Task 9) tests use a fake poster.

- [ ] **Step 1: Create the seam**

```csharp
namespace RustPlusBot.Features.Chat.Webhooks;

/// <summary>Posts an in-game team chat line into a Discord #teamchat channel as the player (via a webhook).</summary>
public interface ITeamChatWebhookPoster
{
    /// <summary>Posts <paramref name="message"/> to channel <paramref name="channelId"/> impersonating <paramref name="username"/>.</summary>
    /// <param name="channelId">The target Discord channel snowflake.</param>
    /// <param name="username">The webhook display name (the in-game player's Steam name).</param>
    /// <param name="message">The message text.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been posted.</returns>
    Task PostAsync(ulong channelId, string username, string message, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implement the real poster**

```csharp
using System.Collections.Concurrent;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Chat.Webhooks;

/// <summary>
/// Real <see cref="ITeamChatWebhookPoster"/>. Ensures one webhook named <see cref="WebhookName"/> per channel
/// (created if missing, re-discovered by name on restart) and caches the webhook client. Untested integration shim.
/// </summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordTeamChatWebhookPoster(DiscordSocketClient client, ILogger<DiscordTeamChatWebhookPoster> logger)
    : ITeamChatWebhookPoster, IAsyncDisposable
{
    private const string WebhookName = "RustPlusBot TeamChat";
    private readonly ConcurrentDictionary<ulong, DiscordWebhookClient> _clients = new();

    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, string username, string message, CancellationToken cancellationToken)
    {
        try
        {
            var webhook = await GetOrCreateClientAsync(channelId).ConfigureAwait(false);
            if (webhook is null)
            {
                return;
            }

            await webhook.SendMessageAsync(message, username: username,
                allowedMentions: AllowedMentions.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Broad catch: a webhook post failure must not crash the relay loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, channelId);
        }
    }

    private async Task<DiscordWebhookClient?> GetOrCreateClientAsync(ulong channelId)
    {
        if (_clients.TryGetValue(channelId, out var cached))
        {
            return cached;
        }

        if (client.GetChannel(channelId) is not ITextChannel channel)
        {
            return null;
        }

        var hooks = await channel.GetWebhooksAsync().ConfigureAwait(false);
        var hook = hooks.FirstOrDefault(h => h.Name == WebhookName)
                   ?? await channel.CreateWebhookAsync(WebhookName).ConfigureAwait(false);
        var webhookClient = new DiscordWebhookClient(hook);
        return _clients.GetOrAdd(channelId, webhookClient);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        foreach (var c in _clients.Values)
        {
            c.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting a team chat line to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);
}
```

> `AllowedMentions.None` prevents relayed in-game text from pinging Discord users/roles.

- [ ] **Step 3: Build**

Run: `dotnet build RustPlusBot.slnx`
Expected: build succeeds.

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Chat/Webhooks
git commit -m "feat(chat): add team-chat webhook poster seam + Discord.Net impl"
```

---

## Task 9: `TeamChatRelay` (game → Discord)

**Files:**
- Create: `src/RustPlusBot.Features.Chat/Relaying/TeamChatRelay.cs`
- Test: `tests/RustPlusBot.Features.Chat.Tests/TeamChatRelayTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class TeamChatRelayTests
{
    private static (TeamChatRelay Relay, ITeamChatWebhookPoster Poster, RelayDedupBuffer Dedup, ITeamChatChannelLocator Locator)
        Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);
        var poster = Substitute.For<ITeamChatWebhookPoster>();
        var locator = Substitute.For<ITeamChatChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)777UL);
        var relay = new TeamChatRelay(locator, poster, dedup);
        return (relay, poster, dedup, locator);
    }

    [Fact]
    public async Task Posts_a_normal_message_via_webhook()
    {
        var (relay, poster, _, _) = Build();
        var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 999UL, "Bob", "hello", FromActivePlayer: false);

        await relay.RelayAsync(evt, CancellationToken.None);

        await poster.Received(1).PostAsync(777UL, "Bob", "hello", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drops_our_own_echo()
    {
        var (relay, poster, dedup, _) = Build();
        var key = (10UL, Guid.Empty);
        dedup.Record(key, "[Alice] hello");
        var echo = new TeamMessageReceivedEvent(10UL, Guid.Empty, 555UL, "BotPlayer", "[Alice] hello",
            FromActivePlayer: true);

        await relay.RelayAsync(echo, CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Posts_active_player_message_that_is_not_an_echo()
    {
        var (relay, poster, _, _) = Build();
        // From the active player, but nothing recorded in the dedup buffer => a genuine in-game message.
        var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 555UL, "BotPlayer", "genuine", FromActivePlayer: true);

        await relay.RelayAsync(evt, CancellationToken.None);

        await poster.Received(1).PostAsync(777UL, "BotPlayer", "genuine", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_when_channel_not_provisioned()
    {
        var (relay, poster, _, locator) = Build();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);
        var evt = new TeamMessageReceivedEvent(10UL, Guid.Empty, 999UL, "Bob", "hello", FromActivePlayer: false);

        await relay.RelayAsync(evt, CancellationToken.None);

        await poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamChatRelayTests"`
Expected: FAIL — `TeamChatRelay` not defined.

- [ ] **Step 3: Implement `TeamChatRelay`**

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Chat.Webhooks;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Chat.Relaying;

/// <summary>Relays one received in-game team message into its Discord #teamchat channel, dropping our own echoes.</summary>
/// <param name="locator">Resolves the target #teamchat channel.</param>
/// <param name="poster">Posts the line via webhook.</param>
/// <param name="dedup">Tracks lines the bridge relayed into the game so their echoes can be dropped.</param>
internal sealed class TeamChatRelay(
    ITeamChatChannelLocator locator,
    ITeamChatWebhookPoster poster,
    RelayDedupBuffer dedup)
{
    /// <summary>Handles one <see cref="TeamMessageReceivedEvent"/>.</summary>
    /// <param name="evt">The received team message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the line has been relayed or dropped.</returns>
    public async Task RelayAsync(TeamMessageReceivedEvent evt, CancellationToken cancellationToken)
    {
        if (evt.FromActivePlayer && dedup.TryConsume((evt.GuildId, evt.ServerId), evt.Message))
        {
            return; // Our own relayed line echoing back; do not re-post.
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is null)
        {
            return;
        }

        await poster.PostAsync(channelId.Value, evt.SenderName, evt.Message, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamChatRelayTests"`
Expected: 4 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Chat/Relaying/TeamChatRelay.cs tests/RustPlusBot.Features.Chat.Tests/TeamChatRelayTests.cs
git commit -m "feat(chat): add TeamChatRelay (game -> Discord with echo drop)"
```

---

## Task 10: `TeamChatInboundProcessor` (Discord → game)

The testable core of the inbound listener: filter bot/webhook authors, resolve the channel, format, record in the dedup buffer, send, and report the outcome.

**Files:**
- Create: `src/RustPlusBot.Features.Chat/Inbound/InboundMessage.cs`
- Create: `src/RustPlusBot.Features.Chat/Inbound/InboundOutcome.cs`
- Create: `src/RustPlusBot.Features.Chat/Inbound/TeamChatInboundProcessor.cs`
- Test: `tests/RustPlusBot.Features.Chat.Tests/TeamChatInboundProcessorTests.cs`

- [ ] **Step 1: Create the DTO + outcome enum**

`InboundMessage.cs`:

```csharp
namespace RustPlusBot.Features.Chat.Inbound;

/// <summary>A Discord message observed in a channel, reduced to what the bridge needs (no Discord.Net types).</summary>
/// <param name="AuthorIsBotOrWebhook">True if the author is a bot or a webhook (ignored to avoid loops).</param>
/// <param name="ChannelId">The channel the message was posted in.</param>
/// <param name="DisplayName">The author's guild display name.</param>
/// <param name="Content">The message text.</param>
internal sealed record InboundMessage(bool AuthorIsBotOrWebhook, ulong ChannelId, string DisplayName, string Content);
```

`InboundOutcome.cs`:

```csharp
namespace RustPlusBot.Features.Chat.Inbound;

/// <summary>What the inbound processor did with a Discord message.</summary>
internal enum InboundOutcome
{
    /// <summary>Not a #teamchat message, empty, or a bot/webhook author — nothing relayed.</summary>
    Ignored = 0,

    /// <summary>Relayed into the game.</summary>
    Sent = 1,

    /// <summary>Belongs to a #teamchat but could not be relayed (no live socket or send failed).</summary>
    Failed = 2,
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class TeamChatInboundProcessorTests
{
    private static readonly Guid ServerId = Guid.NewGuid();

    private static (TeamChatInboundProcessor Processor, ITeamChatSender Sender, RelayDedupBuffer Dedup) Build(
        TeamChatSendResult sendResult = TeamChatSendResult.Sent)
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        var dedup = new RelayDedupBuffer(clock);
        var sender = Substitute.For<ITeamChatSender>();
        sender.SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(sendResult);
        var locator = Substitute.For<ITeamChatChannelLocator>();
        locator.ResolveAsync(777UL, Arg.Any<CancellationToken>()).Returns(((ulong, Guid)?)(10UL, ServerId));
        locator.ResolveAsync(Arg.Is<ulong>(c => c != 777UL), Arg.Any<CancellationToken>())
            .Returns(((ulong, Guid)?)null);
        var processor = new TeamChatInboundProcessor(locator, sender, dedup);
        return (processor, sender, dedup);
    }

    [Fact]
    public async Task Ignores_bot_or_webhook_authors()
    {
        var (processor, sender, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: true, 777UL, "Alice", "hi");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_non_teamchat_channels()
    {
        var (processor, sender, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 555UL, "Alice", "hi");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_empty_content()
    {
        var (processor, sender, _) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "   ");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Ignored, outcome);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Formats_records_and_sends()
    {
        var (processor, sender, dedup) = Build();
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Sent, outcome);
        await sender.Received(1).SendAsync(10UL, ServerId, "[Alice] hello", Arg.Any<CancellationToken>());
        // The formatted text was recorded for echo-dropping (consuming it proves it was there).
        Assert.True(dedup.TryConsume((10UL, ServerId), "[Alice] hello"));
    }

    [Fact]
    public async Task Reports_failed_when_not_connected()
    {
        var (processor, _, _) = Build(TeamChatSendResult.NotConnected);
        var msg = new InboundMessage(AuthorIsBotOrWebhook: false, 777UL, "Alice", "hello");

        var outcome = await processor.ProcessAsync(msg, CancellationToken.None);

        Assert.Equal(InboundOutcome.Failed, outcome);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamChatInboundProcessorTests"`
Expected: FAIL — `TeamChatInboundProcessor` not defined.

- [ ] **Step 4: Implement `TeamChatInboundProcessor`**

```csharp
using System.Globalization;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Chat.Inbound;

/// <summary>Turns a Discord #teamchat message into an in-game relay (record-then-send), reporting the outcome.</summary>
/// <param name="locator">Resolves whether/which server a channel maps to.</param>
/// <param name="sender">Relays the formatted line into the game.</param>
/// <param name="dedup">Records the relayed line so its in-game echo can be dropped.</param>
internal sealed class TeamChatInboundProcessor(
    ITeamChatChannelLocator locator,
    ITeamChatSender sender,
    RelayDedupBuffer dedup)
{
    /// <summary>Processes one observed Discord message.</summary>
    /// <param name="message">The reduced message.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>What was done with the message.</returns>
    public async Task<InboundOutcome> ProcessAsync(InboundMessage message, CancellationToken cancellationToken)
    {
        if (message.AuthorIsBotOrWebhook || string.IsNullOrWhiteSpace(message.Content))
        {
            return InboundOutcome.Ignored;
        }

        var target = await locator.ResolveAsync(message.ChannelId, cancellationToken).ConfigureAwait(false);
        if (target is not { } t)
        {
            return InboundOutcome.Ignored;
        }

        var key = (t.GuildId, t.ServerId);
        var text = string.Create(CultureInfo.InvariantCulture, $"[{message.DisplayName}] {message.Content}");

        // Record BEFORE sending: the in-game echo can arrive before SendAsync returns. Unused entries expire.
        dedup.Record(key, text);
        var result = await sender.SendAsync(t.GuildId, t.ServerId, text, cancellationToken).ConfigureAwait(false);

        return result == TeamChatSendResult.Sent ? InboundOutcome.Sent : InboundOutcome.Failed;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test RustPlusBot.slnx --filter "FullyQualifiedName~TeamChatInboundProcessorTests"`
Expected: 5 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Chat/Inbound tests/RustPlusBot.Features.Chat.Tests/TeamChatInboundProcessorTests.cs
git commit -m "feat(chat): add TeamChatInboundProcessor (Discord -> game)"
```

---

## Task 11: `ChatHostedService`, `AddChat`, gateway intents, Host wiring

Wires the relay subscription loop and the gateway `MessageReceived` listener; registers the feature; enables the Message Content intent.

**Files:**
- Create: `src/RustPlusBot.Features.Chat/Hosting/ChatHostedService.cs`
- Create: `src/RustPlusBot.Features.Chat/ChatServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Host/Program.cs`

- [ ] **Step 1: Implement `ChatHostedService`**

Mirror `WorkspaceHostedService`'s loop/lifetime style. The `MessageReceived` handler maps `SocketMessage` → `InboundMessage`, calls the processor, and adds a ❌ reaction on `Failed`.

```csharp
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;

namespace RustPlusBot.Features.Chat.Hosting;

/// <summary>Runs the game→Discord relay loop and the Discord→game #teamchat listener.</summary>
/// <param name="client">The Discord socket client (for MessageReceived).</param>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Relays received team messages into Discord.</param>
/// <param name="processor">Processes Discord #teamchat messages for relay into the game.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ChatHostedService(
    DiscordSocketClient client,
    IEventBus eventBus,
    TeamChatRelay relay,
    TeamChatInboundProcessor processor,
    ILogger<ChatHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _relayLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived += OnMessageReceivedAsync;
        _relayLoop = Task.Run(() => ConsumeTeamMessagesAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.MessageReceived -= OnMessageReceivedAsync;
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_relayLoop is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // Our own loop task, joined on stop.
                await _relayLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task ConsumeTeamMessagesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<TeamMessageReceivedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.RelayAsync(evt, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting consumer must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRelayLoopFaulted(logger, ex);
        }
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        try
        {
            var displayName = (message.Author as SocketGuildUser)?.DisplayName ?? message.Author.Username;
            var inbound = new InboundMessage(
                message.Author.IsBot || message.Author.IsWebhook,
                message.Channel.Id,
                displayName,
                message.Content);

            var outcome = await processor.ProcessAsync(inbound, _cts.Token).ConfigureAwait(false);
            if (outcome == InboundOutcome.Failed && message is IUserMessage userMessage)
            {
                await userMessage.AddReactionAsync(new Emoji("❌")).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a listener failure must not crash the gateway dispatcher.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogListenerFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Team message relay loop faulted.")]
    private static partial void LogRelayLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Team chat inbound listener faulted.")]
    private static partial void LogListenerFaulted(ILogger logger, Exception exception);
}
```

- [ ] **Step 2: Implement `ChatServiceCollectionExtensions`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Chat.Hosting;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Chat.Webhooks;

namespace RustPlusBot.Features.Chat;

/// <summary>DI registration for the team chat bridge.</summary>
public static class ChatServiceCollectionExtensions
{
    /// <summary>Registers the dedup buffer, webhook poster, relay, inbound processor, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddChat(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RelayDedupBuffer>();
        services.AddSingleton<ITeamChatWebhookPoster, DiscordTeamChatWebhookPoster>();
        services.AddSingleton<TeamChatRelay>();
        services.AddSingleton<TeamChatInboundProcessor>();
        services.AddHostedService<ChatHostedService>();

        return services;
    }
}
```

- [ ] **Step 3: Enable the Message Content intent**

In `DiscordServiceCollectionExtensions.cs`, change the intents line:

```csharp
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMessages | GatewayIntents.MessageContent,
            AlwaysDownloadUsers = false,
```

- [ ] **Step 4: Register `AddChat` in the Host**

In `src/RustPlusBot.Host/Program.cs`, after `builder.Services.AddConnections();` add:

```csharp
builder.Services.AddChat();
```

Add `using RustPlusBot.Features.Chat;` at the top if a global using is not already in place.

- [ ] **Step 5: Build + full test run**

Run: `dotnet build RustPlusBot.slnx` then `dotnet test RustPlusBot.slnx`
Expected: build succeeds; all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Chat src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs src/RustPlusBot.Host/Program.cs
git commit -m "feat(chat): wire ChatHostedService, AddChat, and the Message Content intent"
```

---

## Task 12: Docs + final verification

**Files:**
- Modify: `docs/development/running-locally.md`

- [ ] **Step 1: Document the Message Content intent toggle**

Add a short subsection to `docs/development/running-locally.md` explaining that the bot now requires the **Message Content** privileged intent and **Manage Webhooks** permission:

```markdown
### Discord application setup (team chat bridge)

The team chat bridge reads messages typed in each server's `#teamchat` channel, which requires a
privileged gateway intent:

1. Open the [Discord Developer Portal](https://discord.com/developers/applications) → your application
   → **Bot**.
2. Enable **Message Content Intent** (under *Privileged Gateway Intents*). No verification is required
   while the bot is in fewer than 100 servers.
3. Ensure the bot's role/invite grants **Manage Webhooks** (used to post in-game lines as each player)
   and **Send Messages** in the provisioned channels.
```

- [ ] **Step 2: Run the ReSharper formatter (required before push)**

Run: `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Expected: it may reorder members/usings in the new files; review the diff.

- [ ] **Step 3: Final full build + test**

Run: `dotnet build RustPlusBot.slnx` then `dotnet test RustPlusBot.slnx`
Expected: build 0 warnings / 0 errors under strict analyzers; all tests pass (121 prior + ~19 new ≈ 140).

- [ ] **Step 4: Verify no EF model drift**

Run: `dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host` (or the repo's established check).
Expected: no pending model changes — 3a adds **no** new entities, tables, or migrations (the locator only reads existing `ProvisionedChannel` rows).

- [ ] **Step 5: Commit**

```bash
git add docs/development/running-locally.md
git commit -m "docs: document Message Content intent + Manage Webhooks for the chat bridge"
```

- [ ] **Step 6: Push and open a PR**

```bash
git push -u origin feat/chat-bridge
```

Open a PR `feat/chat-bridge` → `develop` titled **"Subsystem 3a: team chat bridge (two-way #teamchat ↔ in-game)"**. Then run a whole-feature review before merge.

---

## Self-Review (completed during planning)

- **Spec coverage:** Game→Discord webhook relay (Tasks 1–3, 8, 9), Discord→game send (Tasks 3, 10, 11), dedup loop prevention (Tasks 7, 9, 10), `#teamchat` channel Interactive (Task 4), locator both directions (Tasks 5, 6), Message Content intent + Manage Webhooks docs (Tasks 11, 12), error handling/broad-catch (Tasks 3, 8, 9, 11), no new persistence (verified Task 12 step 4). All spec sections map to a task.
- **Type consistency:** `TeamMessageReceivedEvent` fields, `TeamChatLine`, `ITeamChatSender.SendAsync`/`TeamChatSendResult`, `RelayDedupBuffer.Record`/`TryConsume`, `ITeamChatChannelLocator.GetChannelIdAsync`/`ResolveAsync`, `ITeamChatWebhookPoster.PostAsync`, `InboundMessage`/`InboundOutcome`/`ProcessAsync` are used identically across the tasks that define and consume them.
- **No placeholders:** every implementation step contains complete code; commands have expected outcomes.
- **Verify-at-execution flags (carried from the spec):** RustPlusApi `OnTeamChatReceived` arg shape + `SendTeamMessageAsync` signature (Task 2 step 4) and the Discord.Net webhook calls (Task 8) are the two untested integration shims — confirm member names against the actual packages while implementing.
