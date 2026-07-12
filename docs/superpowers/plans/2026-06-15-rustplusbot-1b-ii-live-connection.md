# RustPlusBot 1b-ii — Live Connection Lifecycle, Failover & `#info` Status — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Open and supervise one live Rust+ socket per `(guild, server)` driven by the pool's `Active` credential, with restart survival, auto-failover, heartbeat health, a ManageGuild swap select on `#info`, and a live-status `#info` embed.

**Architecture:** A new `RustPlusBot.Features.Connections` project mirrors `Features.Pairing`: an `IRustSocketSource` seam over the `RustPlusApi` core socket package, a singleton `IConnectionSupervisor` (one connection per server, connect → heartbeat → reconnect/failover), and a thin `ConnectionHostedService`. The persisted `ConnectionState` (previously unused) is the restart-surviving source of truth, read by the Workspace `#info` renderer and refreshed cross-feature via a new `ConnectionStatusChangedEvent` on the existing `IEventBus`.

**Tech Stack:** .NET 10, C# 13, EF Core 10 (SQLite), Discord.Net 3.20, `RustPlusApi` 2.0.0-beta.1, xUnit + NSubstitute. Strict analyzers (Roslynator/Sonar/NetAnalyzers/VSThreading); CI format gate is `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (ReSharper, via `dotnet tool restore`).

**Conventions to follow (already in this repo):**

- `LoggerMessage` source-gen for all logging (no string-interpolated `ILogger` calls).
- `.ConfigureAwait(false)` on every await in library code.
- Broad `catch (Exception)` only with the `#pragma warning disable CA1031` + justification comment, as in `PairingSupervisor`.
- Per-unit-of-work DI scopes via `IServiceScopeFactory` inside singletons (see `PairingSupervisor`).
- Secrets pass through `ICredentialProtector`; never logged.
- After each task: `dotnet build RustPlusBot.slnx` is 0 warnings / 0 errors, `dotnet test` green. Before any push, run `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`.

---

## Task 1: Reshape `ConnectionState` + `ConnectionStatus` enum + `LiveConnections` migration

**Files:**

- Create: `src/RustPlusBot.Domain/Connections/ConnectionStatus.cs`
- Modify: `src/RustPlusBot.Domain/Connections/ConnectionState.cs`
- Create (via EF tooling): `src/RustPlusBot.Persistence/Migrations/<timestamp>_LiveConnections.cs` (+ `.Designer.cs` + snapshot update)
- Test: `tests/RustPlusBot.Persistence.Tests/Connections/ConnectionStateSchemaTests.cs`

- [ ] **Step 1: Write the failing schema test**

Create `tests/RustPlusBot.Persistence.Tests/Connections/ConnectionStateSchemaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Connections;

namespace RustPlusBot.Persistence.Tests.Connections;

public sealed class ConnectionStateSchemaTests
{
    [Fact]
    public async Task ConnectionState_RoundTrips_StatusAndPlayerCount()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var serverId = Guid.NewGuid();
        context.ConnectionStates.Add(new ConnectionState
        {
            RustServerId = serverId,
            GuildId = 10UL,
            ActiveCredentialId = Guid.NewGuid(),
            Status = ConnectionStatus.Connected,
            PlayerCount = 42,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        var read = await context.ConnectionStates.SingleAsync(s => s.RustServerId == serverId);
        Assert.Equal(ConnectionStatus.Connected, read.Status);
        Assert.Equal(42, read.PlayerCount);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ConnectionState_RoundTrips_StatusAndPlayerCount`
Expected: FAIL — `ConnectionStatus` does not exist; `ConnectionState` has no `Status`/`PlayerCount`.

- [ ] **Step 3: Create the `ConnectionStatus` enum**

Create `src/RustPlusBot.Domain/Connections/ConnectionStatus.cs`:

```csharp
namespace RustPlusBot.Domain.Connections;

/// <summary>Live-connection lifecycle state for a server's socket.</summary>
public enum ConnectionStatus
{
    /// <summary>Attempting to connect (initial, after a swap, or after a drop).</summary>
    Connecting = 0,

    /// <summary>Socket up and the last heartbeat was healthy.</summary>
    Connected = 1,

    /// <summary>A valid active credential exists but the server is not answering; retrying with backoff.</summary>
    Unreachable = 2,

    /// <summary>No eligible credential (pool empty or every credential is Invalid).</summary>
    NoCredentials = 3,
}
```

- [ ] **Step 4: Reshape `ConnectionState`**

Replace the body of `src/RustPlusBot.Domain/Connections/ConnectionState.cs`:

```csharp
namespace RustPlusBot.Domain.Connections;

/// <summary>Persisted last-known connection state per server, so the active identity and status survive restarts.</summary>
public sealed class ConnectionState
{
    /// <summary>The server this state belongs to (primary key, one row per server).</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The credential currently selected as active, if any.</summary>
    public Guid? ActiveCredentialId { get; set; }

    /// <summary>The live-connection status.</summary>
    public ConnectionStatus Status { get; set; }

    /// <summary>Last heartbeat player count, or null if unknown.</summary>
    public int? PlayerCount { get; set; }

    /// <summary>When the state was last updated (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 5: Generate the migration**

Run (restore EF tooling first if needed: `dotnet tool restore` or `dotnet tool install --global dotnet-ef`):

```bash
dotnet ef migrations add LiveConnections --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host
```

Verify the generated `Up` contains (SQLite rebuilds the table, so exact ops may be an `Ef`-style table rebuild — confirm these column changes are present):

- `DropColumn name: "IsHealthy", table: "ConnectionStates"`
- `AddColumn<int> name: "Status", table: "ConnectionStates", nullable: false, defaultValue: 0`
- `AddColumn<int> name: "PlayerCount", table: "ConnectionStates", nullable: true`

And `Down` reverses them (drop `Status`/`PlayerCount`, add `IsHealthy` bool default false).

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ConnectionState_RoundTrips_StatusAndPlayerCount`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Domain/Connections/ src/RustPlusBot.Persistence/Migrations/ tests/RustPlusBot.Persistence.Tests/Connections/
git commit -m "feat(connections): reshape ConnectionState with status + player count"
```

---

## Task 2: `ConnectionStatusChangedEvent`

**Files:**

- Create: `src/RustPlusBot.Abstractions/Events/ConnectionStatusChangedEvent.cs`

- [ ] **Step 1: Create the event record**

Create `src/RustPlusBot.Abstractions/Events/ConnectionStatusChangedEvent.cs` (a one-line record beside `ServerRegisteredEvent`; no dedicated test, exactly like `ServerRegisteredEvent`):

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a server's live-connection state changes, so #info can re-render.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server whose connection state changed.</param>
public sealed record ConnectionStatusChangedEvent(ulong GuildId, Guid ServerId);
```

- [ ] **Step 2: Verify the build**

Run: `dotnet build RustPlusBot.slnx`
Expected: 0 warnings, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Abstractions/Events/ConnectionStatusChangedEvent.cs
git commit -m "feat(events): add ConnectionStatusChangedEvent"
```

---

## Task 3: `IConnectionStore` + `ConnectionStore` + DI

**Files:**

- Create: `src/RustPlusBot.Persistence/Connections/IConnectionStore.cs`
- Create: `src/RustPlusBot.Persistence/Connections/ConnectionStore.cs`
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Connections/ConnectionStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Persistence.Tests/Connections/ConnectionStoreTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Persistence.Tests.Connections;

public sealed class ConnectionStoreTests
{
    private static (ConnectionStore Store, BotDbContext Context, IDisposable Conn) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);
        return (new ConnectionStore(context, clock), context, connection);
    }

    private static async Task<(Guid ServerId, Guid CredA, Guid CredB)> SeedServerWithPoolAsync(BotDbContext context)
    {
        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        context.RustServers.Add(server);
        var a = new PlayerCredential
        {
            GuildId = 10UL, RustServerId = server.Id, OwnerUserId = 1UL, SteamId = 100UL,
            ProtectedPlayerToken = "ta", Status = CredentialStatus.Active,
        };
        var b = new PlayerCredential
        {
            GuildId = 10UL, RustServerId = server.Id, OwnerUserId = 2UL, SteamId = 200UL,
            ProtectedPlayerToken = "tb", Status = CredentialStatus.Standby,
        };
        context.PlayerCredentials.AddRange(a, b);
        await context.SaveChangesAsync();
        return (server.Id, a.Id, b.Id);
    }

    [Fact]
    public async Task UpsertStatus_InsertsThenReportsChangeOnlyWhenDifferent()
    {
        var (store, context, conn) = Create();
        using var _ = conn;
        await using var __ = context;
        var serverId = Guid.NewGuid();

        Assert.True(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connecting, null, null));
        Assert.True(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connected, 5, null));
        Assert.False(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connected, 5, null));
        Assert.True(await store.UpsertStatusAsync(10UL, serverId, ConnectionStatus.Connected, 6, null));

        var state = await store.GetStateAsync(10UL, serverId);
        Assert.Equal(ConnectionStatus.Connected, state!.Status);
        Assert.Equal(6, state.PlayerCount);
    }

    [Fact]
    public async Task GetActiveCredential_ReturnsTheActiveOne()
    {
        var (store, context, conn) = Create();
        using var _ = conn;
        await using var __ = context;
        var (serverId, credA, _) = await SeedServerWithPoolAsync(context);

        var active = await store.GetActiveCredentialAsync(10UL, serverId);

        Assert.Equal(credA, active!.Id);
        Assert.Equal("ta", active.ProtectedPlayerToken);
    }

    [Fact]
    public async Task Promote_MakesTargetActiveAndDemotesPrior()
    {
        var (store, context, conn) = Create();
        using var _ = conn;
        await using var __ = context;
        var (serverId, credA, credB) = await SeedServerWithPoolAsync(context);

        var ok = await store.PromoteAsync(10UL, serverId, credB);

        Assert.True(ok);
        var pool = await store.ListPoolAsync(10UL, serverId);
        Assert.Equal(CredentialStatus.Active, pool.Single(c => c.Id == credB).Status);
        Assert.Equal(CredentialStatus.Standby, pool.Single(c => c.Id == credA).Status);
    }

    [Fact]
    public async Task Promote_RejectsInvalidOrUnknownCredential()
    {
        var (store, context, conn) = Create();
        using var _ = conn;
        await using var __ = context;
        var (serverId, credA, _) = await SeedServerWithPoolAsync(context);
        await store.MarkInvalidAsync(credA);

        Assert.False(await store.PromoteAsync(10UL, serverId, credA));
        Assert.False(await store.PromoteAsync(10UL, serverId, Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkInvalid_SetsStatusInvalid()
    {
        var (store, context, conn) = Create();
        using var _ = conn;
        await using var __ = context;
        var (serverId, credA, _) = await SeedServerWithPoolAsync(context);

        await store.MarkInvalidAsync(credA);

        var pool = await store.ListPoolAsync(10UL, serverId);
        Assert.Equal(CredentialStatus.Invalid, pool.Single(c => c.Id == credA).Status);
    }

    [Fact]
    public async Task ListConnectableServers_ExcludesAllInvalidServers()
    {
        var (store, context, conn) = Create();
        using var _ = conn;
        await using var __ = context;
        var (serverId, credA, credB) = await SeedServerWithPoolAsync(context);

        var before = await store.ListConnectableServersAsync();
        Assert.Contains((10UL, serverId), before);

        await store.MarkInvalidAsync(credA);
        await store.MarkInvalidAsync(credB);

        var after = await store.ListConnectableServersAsync();
        Assert.DoesNotContain((10UL, serverId), after);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ConnectionStoreTests`
Expected: FAIL — `IConnectionStore`/`ConnectionStore` not defined.

- [ ] **Step 3: Create the interface**

Create `src/RustPlusBot.Persistence/Connections/IConnectionStore.cs`:

```csharp
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Connections;

/// <summary>
/// Persists per-server live-connection state and the operations the connection supervisor and the
/// #info renderer need. Lives in the persistence layer (returns Domain types).
/// </summary>
public interface IConnectionStore
{
    /// <summary>Gets the connection state for a server, or null.</summary>
    Task<ConnectionState?> GetStateAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Upserts the connection state. Returns true only when the persisted (status, player count, active
    /// credential) actually changed, so callers can skip a redundant #info refresh.
    /// </summary>
    Task<bool> UpsertStatusAsync(
        ulong guildId,
        Guid serverId,
        ConnectionStatus status,
        int? playerCount,
        Guid? activeCredentialId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the server's <c>Active</c> player credential (with protected token), or null.</summary>
    Task<PlayerCredential?> GetActiveCredentialAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists the server's full credential pool, ordered by id.</summary>
    Task<IReadOnlyList<PlayerCredential>> ListPoolAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes <paramref name="credentialId"/> the server's <c>Active</c> credential and demotes the prior
    /// active to <c>Standby</c>. Returns false if the credential is not an eligible (non-Invalid) pool member.
    /// </summary>
    Task<bool> PromoteAsync(
        ulong guildId,
        Guid serverId,
        Guid credentialId,
        CancellationToken cancellationToken = default);

    /// <summary>Marks a credential <c>Invalid</c> (failover on auth-reject).</summary>
    Task MarkInvalidAsync(Guid credentialId, CancellationToken cancellationToken = default);

    /// <summary>Lists every <c>(GuildId, ServerId)</c> that has a non-Invalid credential (startup enumeration).</summary>
    Task<IReadOnlyList<(ulong GuildId, Guid ServerId)>> ListConnectableServersAsync(
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create the implementation**

Create `src/RustPlusBot.Persistence/Connections/ConnectionStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Connections;

/// <summary>EF-backed <see cref="IConnectionStore"/>.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">Supplies the update timestamp.</param>
public sealed class ConnectionStore(BotDbContext context, IClock clock) : IConnectionStore
{
    /// <inheritdoc />
    public Task<ConnectionState?> GetStateAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default) =>
        context.ConnectionStates
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.RustServerId == serverId, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> UpsertStatusAsync(
        ulong guildId,
        Guid serverId,
        ConnectionStatus status,
        int? playerCount,
        Guid? activeCredentialId,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.ConnectionStates
            .SingleOrDefaultAsync(s => s.RustServerId == serverId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.ConnectionStates.Add(new ConnectionState
            {
                RustServerId = serverId,
                GuildId = guildId,
                Status = status,
                PlayerCount = playerCount,
                ActiveCredentialId = activeCredentialId,
                UpdatedAt = clock.UtcNow,
            });
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }

        if (existing.Status == status
            && existing.PlayerCount == playerCount
            && existing.ActiveCredentialId == activeCredentialId)
        {
            return false;
        }

        existing.Status = status;
        existing.PlayerCount = playerCount;
        existing.ActiveCredentialId = activeCredentialId;
        existing.UpdatedAt = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public Task<PlayerCredential?> GetActiveCredentialAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default) =>
        context.PlayerCredentials.SingleOrDefaultAsync(
            c => c.GuildId == guildId && c.RustServerId == serverId && c.Status == CredentialStatus.Active,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PlayerCredential>> ListPoolAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default) =>
        await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .OrderBy(c => c.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> PromoteAsync(
        ulong guildId,
        Guid serverId,
        Guid credentialId,
        CancellationToken cancellationToken = default)
    {
        var pool = await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var target = pool.SingleOrDefault(c => c.Id == credentialId);
        if (target is null || target.Status == CredentialStatus.Invalid)
        {
            return false;
        }

        foreach (var credential in pool)
        {
            if (credential.Status == CredentialStatus.Active && credential.Id != credentialId)
            {
                credential.Status = CredentialStatus.Standby;
            }
        }

        target.Status = CredentialStatus.Active;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc />
    public async Task MarkInvalidAsync(Guid credentialId, CancellationToken cancellationToken = default)
    {
        var credential = await context.PlayerCredentials
            .SingleOrDefaultAsync(c => c.Id == credentialId, cancellationToken)
            .ConfigureAwait(false);
        if (credential is null)
        {
            return;
        }

        credential.Status = CredentialStatus.Invalid;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(ulong GuildId, Guid ServerId)>> ListConnectableServersAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await context.PlayerCredentials
            .Where(c => c.Status != CredentialStatus.Invalid)
            .Select(c => new { c.GuildId, c.RustServerId })
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.Select(r => (r.GuildId, r.RustServerId)).ToList();
    }
}
```

- [ ] **Step 5: Register in DI**

In `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`, add the `using` and registration alongside the other scoped stores:

```csharp
using RustPlusBot.Persistence.Connections;
```

```csharp
services.AddScoped<IConnectionStore, ConnectionStore>();
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ConnectionStoreTests`
Expected: PASS (6 tests).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Persistence/Connections/ src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs tests/RustPlusBot.Persistence.Tests/Connections/ConnectionStoreTests.cs
git commit -m "feat(connections): add IConnectionStore for live-connection state and pool"
```

---

## Task 4: `IUserDmSender` (Discord layer) + refactor `DiscordOwnerNotifier`

**Files:**

- Create: `src/RustPlusBot.Discord/Notifications/IUserDmSender.cs`
- Create: `src/RustPlusBot.Discord/Notifications/DiscordUserDmSender.cs`
- Modify: `src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Features.Pairing/Notifications/DiscordOwnerNotifier.cs`
- Modify: `tests/RustPlusBot.Features.Pairing.Tests/PairingRegistrationTests.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/DiscordOwnerNotifierTests.cs`

- [ ] **Step 1: Write the failing notifier test**

Create `tests/RustPlusBot.Features.Pairing.Tests/DiscordOwnerNotifierTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Features.Pairing.Notifications;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class DiscordOwnerNotifierTests
{
    [Fact]
    public async Task NotifyCredentialsExpired_DmsTheOwner()
    {
        var dm = Substitute.For<IUserDmSender>();
        var notifier = new DiscordOwnerNotifier(dm);

        await notifier.NotifyCredentialsExpiredAsync(10UL, 99UL);

        await dm.Received(1).SendAsync(99UL, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter DiscordOwnerNotifierTests`
Expected: FAIL — `IUserDmSender` not defined; `DiscordOwnerNotifier` has no such constructor.

- [ ] **Step 3: Create `IUserDmSender`**

Create `src/RustPlusBot.Discord/Notifications/IUserDmSender.cs`:

```csharp
namespace RustPlusBot.Discord.Notifications;

/// <summary>Sends a direct message to a Discord user. A closed DM (or any send failure) is swallowed and logged.</summary>
public interface IUserDmSender
{
    /// <summary>Best-effort DM to <paramref name="userId"/>; never throws.</summary>
    /// <param name="userId">The Discord user snowflake.</param>
    /// <param name="message">The message text.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the send was attempted.</returns>
    Task SendAsync(ulong userId, string message, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Create `DiscordUserDmSender`**

Create `src/RustPlusBot.Discord/Notifications/DiscordUserDmSender.cs`:

```csharp
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Discord.Notifications;

/// <summary>Discord-backed <see cref="IUserDmSender"/>.</summary>
/// <param name="client">The socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordUserDmSender(DiscordSocketClient client, ILogger<DiscordUserDmSender> logger)
    : IUserDmSender
{
    /// <inheritdoc />
    public async Task SendAsync(ulong userId, string message, CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await client.GetUserAsync(userId).ConfigureAwait(false);
            if (user is null)
            {
                LogUserNotFound(logger, userId);
                return;
            }

            await user.SendMessageAsync(message).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Broad catch is intentional: a closed DM (or any send failure) must not break callers.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDmFailed(logger, ex, userId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cannot DM user {UserId}: user not found.")]
    private static partial void LogUserNotFound(ILogger logger, ulong userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to DM user {UserId}.")]
    private static partial void LogDmFailed(ILogger logger, Exception ex, ulong userId);
}
```

- [ ] **Step 5: Register it in `AddDiscordBot`**

In `src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs`, add the using and registration (before `AddHostedService`):

```csharp
using RustPlusBot.Discord.Notifications;
```

```csharp
services.AddSingleton<IUserDmSender, DiscordUserDmSender>();
```

- [ ] **Step 6: Refactor `DiscordOwnerNotifier` onto it**

Replace `src/RustPlusBot.Features.Pairing/Notifications/DiscordOwnerNotifier.cs`:

```csharp
using RustPlusBot.Discord.Notifications;

namespace RustPlusBot.Features.Pairing.Notifications;

/// <summary>DMs the owner when their FCM credentials expire, via the shared <see cref="IUserDmSender"/>.</summary>
/// <param name="dmSender">Sends the DM (swallows a closed DM).</param>
internal sealed class DiscordOwnerNotifier(IUserDmSender dmSender) : IOwnerNotifier
{
    /// <inheritdoc />
    public Task NotifyCredentialsExpiredAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default) =>
        dmSender.SendAsync(
            ownerUserId,
            "Your Rust+ credentials were rejected. Reconnect your account in #setup to keep receiving pairings.",
            cancellationToken);
}
```

- [ ] **Step 7: Fix `PairingRegistrationTests` (DiscordOwnerNotifier now needs `IUserDmSender`)**

In `tests/RustPlusBot.Features.Pairing.Tests/PairingRegistrationTests.cs`, add the using and register a substitute before `AddPairing()`:

```csharp
using RustPlusBot.Discord.Notifications;
```

```csharp
services.AddSingleton(Substitute.For<IUserDmSender>());
```

- [ ] **Step 8: Run the affected tests**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter "DiscordOwnerNotifierTests|PairingRegistrationTests"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Discord/Notifications/ src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs src/RustPlusBot.Features.Pairing/Notifications/DiscordOwnerNotifier.cs tests/RustPlusBot.Features.Pairing.Tests/
git commit -m "refactor(discord): extract IUserDmSender and reuse it in DiscordOwnerNotifier"
```

---

## Task 5: New `Features.Connections` project + socket seam + options + test project

**Files:**

- Create: `src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj`
- Create: `src/RustPlusBot.Features.Connections/Listening/SocketConnectOutcome.cs`
- Create: `src/RustPlusBot.Features.Connections/Listening/HeartbeatResult.cs`
- Create: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Create: `src/RustPlusBot.Features.Connections/Listening/IRustSocketSource.cs`
- Create: `src/RustPlusBot.Features.Connections/ConnectionOptions.cs`
- Create: `tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj`
- Create: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`
- Create: `tests/RustPlusBot.Features.Connections.Tests/SeamSmokeTests.cs`

- [ ] **Step 1: Create the project file**

Create `src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj` (no `RustPlusApi` package yet — added in Task 6):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="RustPlusBot.Features.Connections.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
    <ProjectReference Include="..\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
    <ProjectReference Include="..\RustPlusBot.Domain\RustPlusBot.Domain.csproj" />
    <ProjectReference Include="..\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
    <ProjectReference Include="..\RustPlusBot.Features.Workspace\RustPlusBot.Features.Workspace.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Discord.Net" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the seam types**

Create `src/RustPlusBot.Features.Connections/Listening/SocketConnectOutcome.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Result of an initial socket connect attempt.</summary>
internal enum SocketConnectOutcome
{
    /// <summary>Socket connected.</summary>
    Connected,

    /// <summary>The server rejected the player token (credential problem).</summary>
    AuthRejected,

    /// <summary>The server could not be reached (network/down).</summary>
    Unreachable,
}
```

Create `src/RustPlusBot.Features.Connections/Listening/HeartbeatResult.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Classification of a heartbeat probe.</summary>
internal enum HeartbeatKind
{
    /// <summary>Server answered; <see cref="HeartbeatResult.PlayerCount"/> is valid.</summary>
    Ok,

    /// <summary>Server did not answer in time.</summary>
    Unreachable,

    /// <summary>Server rejected the request as unauthorized (token died mid-session).</summary>
    AuthRejected,
}

/// <summary>The outcome of a heartbeat probe.</summary>
/// <param name="Kind">The classification.</param>
/// <param name="PlayerCount">Players online (only meaningful when <see cref="HeartbeatKind.Ok"/>).</param>
internal readonly record struct HeartbeatResult(HeartbeatKind Kind, int PlayerCount)
{
    /// <summary>A healthy heartbeat carrying the player count.</summary>
    public static HeartbeatResult Ok(int playerCount) => new(HeartbeatKind.Ok, playerCount);

    /// <summary>An unreachable heartbeat.</summary>
    public static HeartbeatResult Unreachable { get; } = new(HeartbeatKind.Unreachable, 0);

    /// <summary>An auth-rejected heartbeat.</summary>
    public static HeartbeatResult AuthRejected { get; } = new(HeartbeatKind.AuthRejected, 0);
}
```

Create `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One live Rust+ socket to a single server, driven by one player credential.</summary>
internal interface IRustServerConnection : IAsyncDisposable
{
    /// <summary>Connects within <paramref name="timeout"/>.</summary>
    Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Sends a lightweight info request as a heartbeat (and auth probe), within <paramref name="timeout"/>.</summary>
    Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
```

Create `src/RustPlusBot.Features.Connections/Listening/IRustSocketSource.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Creates <see cref="IRustServerConnection"/>s (the real RustPlusApi adapter in production, a fake in tests).</summary>
internal interface IRustSocketSource
{
    /// <summary>Creates a connection to <paramref name="ip"/>:<paramref name="port"/> as the given player.</summary>
    IRustServerConnection Create(string ip, int port, ulong steamId, string playerToken);
}
```

- [ ] **Step 3: Create `ConnectionOptions`**

Create `src/RustPlusBot.Features.Connections/ConnectionOptions.cs`:

```csharp
namespace RustPlusBot.Features.Connections;

/// <summary>Connection feature configuration, bound from the "Connections" config section.</summary>
public sealed class ConnectionOptions
{
    /// <summary>How long to wait for a socket to connect before treating the server as unreachable.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>First delay before retrying after an unreachable result.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Cap on the exponential backoff between reconnect attempts.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How often to send a heartbeat on a connected socket.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a single heartbeat may take before the socket is considered unreachable.</summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
```

- [ ] **Step 4: Create the test project**

Create `tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj` (mirror an existing `*.Tests.csproj`; this repo uses central package versions and a shared test SDK set — copy the package list from `tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj` exactly, changing only the `ProjectReference`):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\RustPlusBot.Features.Connections\RustPlusBot.Features.Connections.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="NSubstitute" />
  </ItemGroup>

</Project>
```

> NOTE: open `tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj` and match its `PropertyGroup`/`Using`/`PackageReference` set verbatim (e.g. implicit usings, `Using Include="Xunit"`, analyzers) so this project compiles identically.

- [ ] **Step 5: Create `FakeRustSocketSource`**

Create `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`:

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests.Fakes;

/// <summary>
/// Scripted <see cref="IRustSocketSource"/>. Each <see cref="Create"/> dequeues the next connect outcome;
/// each created connection dequeues heartbeat results (default Ok(0) when the queue empties).
/// </summary>
internal sealed class FakeRustSocketSource : IRustSocketSource
{
    private readonly ConcurrentQueue<SocketConnectOutcome> _connectOutcomes = new();
    private readonly ConcurrentQueue<HeartbeatResult> _heartbeats = new();

    private int _createCount;

    public int CreateCount => Volatile.Read(ref _createCount);

    public string? LastIp { get; private set; }

    public ulong LastSteamId { get; private set; }

    public void EnqueueConnect(SocketConnectOutcome outcome) => _connectOutcomes.Enqueue(outcome);

    public void EnqueueHeartbeat(HeartbeatResult result) => _heartbeats.Enqueue(result);

    public IRustServerConnection Create(string ip, int port, ulong steamId, string playerToken)
    {
        Interlocked.Increment(ref _createCount);
        LastIp = ip;
        LastSteamId = steamId;
        var outcome = _connectOutcomes.TryDequeue(out var next) ? next : SocketConnectOutcome.Connected;
        return new FakeConnection(outcome, _heartbeats);
    }

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
```

- [ ] **Step 6: Write a smoke test and register both projects in the solution**

Create `tests/RustPlusBot.Features.Connections.Tests/SeamSmokeTests.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Connections.Tests.Fakes;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class SeamSmokeTests
{
    [Fact]
    public async Task FakeSource_CreatesConnection_AndDefaultsToConnectedOkZero()
    {
        var source = new FakeRustSocketSource();

        var connection = source.Create("1.2.3.4", 28015, 100UL, "tok");
        await using var _ = connection;

        Assert.Equal(SocketConnectOutcome.Connected, await connection.ConnectAsync(TimeSpan.Zero, default));
        var beat = await connection.GetInfoAsync(TimeSpan.Zero, default);
        Assert.Equal(HeartbeatKind.Ok, beat.Kind);
        Assert.Equal(1, source.CreateCount);
    }
}
```

Add both projects to the solution:

```bash
dotnet sln RustPlusBot.slnx add src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj
```

- [ ] **Step 7: Build and test**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests`
Expected: PASS (1 test). `dotnet build RustPlusBot.slnx` → 0/0.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Connections/ tests/RustPlusBot.Features.Connections.Tests/ RustPlusBot.slnx
git commit -m "feat(connections): scaffold Features.Connections project and socket seam"
```

---

## Task 6: `RustPlusSocketSource` — real RustPlusApi adapter (integration shim)

**Files:**

- Modify: `Directory.Packages.props`
- Modify: `src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj`
- Create: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/RustPlusSocketSourceTests.cs`

> **Integration shim — verify against `RustPlusApi` 2.0.0-beta.1 during execution.** The reference code below is grounded in the cached package API (`RustPlusApi.RustPlus(server, port, playerId, playerToken, useFacepunchProxy)`; `ConnectAsync()`; `DisconnectAsync()`; events `Connected`/`Disconnected`/`ErrorOccurred`; `GetInfoAsync()` → an info model exposing `Players`). Confirm exact type names, the `GetInfoAsync` return shape, the event-args types, and the constructor's `playerToken` parameter type (it is numeric — `PlayerCredential.ProtectedPlayerToken` is the numeric token stored as a string, so parse it). Adjust the three-way outcome mapping to the real error surface. This class is intentionally not unit-tested beyond construction (same policy as `RustPlusFcmPairingSource`).

- [ ] **Step 1: Add the package version**

In `Directory.Packages.props`, add beside `RustPlusApi.Fcm`:

```xml
<PackageVersion Include="RustPlusApi" Version="2.0.0-beta.1" />
```

- [ ] **Step 2: Reference it in the project**

In `src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj`, add to the package `ItemGroup`:

```xml
<PackageReference Include="RustPlusApi" />
```

- [ ] **Step 3: Write the (minimal) shim test**

Create `tests/RustPlusBot.Features.Connections.Tests/RustPlusSocketSourceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class RustPlusSocketSourceTests
{
    [Fact]
    public async Task Create_ReturnsADisposableConnection()
    {
        var source = new RustPlusSocketSource(NullLogger<RustPlusSocketSource>.Instance);

        var connection = source.Create("127.0.0.1", 28015, 100UL, "12345");
        await using var _ = connection;

        Assert.NotNull(connection);
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter RustPlusSocketSourceTests`
Expected: FAIL — `RustPlusSocketSource` not defined.

- [ ] **Step 5: Implement the adapter**

Create `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`:

```csharp
using System.Globalization;
using Microsoft.Extensions.Logging;
using RustPlusApi;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Real <see cref="IRustSocketSource"/> backed by RustPlusApi. Untested integration shim.</summary>
/// <param name="logger">The logger.</param>
internal sealed partial class RustPlusSocketSource(ILogger<RustPlusSocketSource> logger) : IRustSocketSource
{
    /// <inheritdoc />
    public IRustServerConnection Create(string ip, int port, ulong steamId, string playerToken)
    {
        // PlayerToken is numeric; PlayerCredential stores it as a string. Parse to the numeric form RustPlus expects.
        var token = int.Parse(playerToken, CultureInfo.InvariantCulture);
        return new RustPlusServerConnection(ip, port, steamId, token, logger);
    }

    private sealed partial class RustPlusServerConnection : IRustServerConnection
    {
        private readonly RustPlus _rustPlus;
        private readonly ILogger _logger;

        public RustPlusServerConnection(string ip, int port, ulong steamId, int playerToken, ILogger logger)
        {
            _logger = logger;
            // VERIFY: constructor signature (server, port, playerId, playerToken, useFacepunchProxy).
            _rustPlus = new RustPlus(ip, port, steamId, playerToken, useFacepunchProxy: false);
        }

        public async Task<SocketConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            void OnConnected(object? sender, EventArgs e) => connected.TrySetResult();

            // VERIFY: event names/args. The socket "Connected" fires on a successful connection.
            _rustPlus.Connected += OnConnected;
            try
            {
                await _rustPlus.ConnectAsync().ConfigureAwait(false);
                await connected.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                return SocketConnectOutcome.Connected;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Timed out waiting to connect — server unreachable, not the caller's cancellation.
                return SocketConnectOutcome.Unreachable;
            }
#pragma warning disable CA1031 // Broad catch: any non-cancellation connect failure maps to Unreachable; auth is probed by the first heartbeat.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogConnectFailed(_logger, ex);
                return SocketConnectOutcome.Unreachable;
            }
            finally
            {
                _rustPlus.Connected -= OnConnected;
            }
        }

        public async Task<HeartbeatResult> GetInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // VERIFY: GetInfoAsync return type + the player-count member (Players). Some overloads take a CT.
                var info = await _rustPlus.GetInfoAsync().WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                return HeartbeatResult.Ok(info.Players);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return HeartbeatResult.Unreachable;
            }
#pragma warning disable CA1031 // Broad catch: classify an info failure. VERIFY: detect the auth/token-rejected error and return AuthRejected.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogHeartbeatFailed(_logger, ex);
                // Conservative default: treat as unreachable. Map the specific "not authorized / invalid token"
                // RustPlusApi error to HeartbeatResult.AuthRejected once its shape is confirmed.
                return HeartbeatResult.Unreachable;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                // VERIFY: DisposeAsync vs DisconnectAsync/CloseAsync. Prefer the async teardown that closes the socket.
                await _rustPlus.DisconnectAsync().ConfigureAwait(false);
            }
#pragma warning disable CA1031 // Broad catch: teardown failures must not throw from disposal.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDisposeFailed(_logger, ex);
            }
        }

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ socket connect failed.")]
        private static partial void LogConnectFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ heartbeat (GetInfo) failed.")]
        private static partial void LogHeartbeatFailed(ILogger logger, Exception ex);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rust+ socket teardown failed.")]
        private static partial void LogDisposeFailed(ILogger logger, Exception ex);
    }
}
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter RustPlusSocketSourceTests`
Expected: PASS. If `RustPlus`/`GetInfoAsync`/`Players`/`DisconnectAsync` names differ in 2.0.0-beta.1, fix per the VERIFY notes until it compiles and the test passes.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/RustPlusBot.Features.Connections/ tests/RustPlusBot.Features.Connections.Tests/RustPlusSocketSourceTests.cs
git commit -m "feat(connections): add RustPlusApi socket adapter shim"
```

---

## Task 7: `IConnectionSupervisor` + `ConnectionSupervisor`

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Supervisor/IConnectionSupervisor.cs`
- Create: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs`
- Test helper: `tests/RustPlusBot.Features.Connections.Tests/TestDb.cs`

- [ ] **Step 1: Add the test DB helper**

Create `tests/RustPlusBot.Features.Connections.Tests/TestDb.cs` (copy of the Pairing tests' helper):

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Persistence;

namespace RustPlusBot.Features.Connections.Tests;

/// <summary>Creates a BotDbContext over a private in-memory SQLite connection kept open for the test.</summary>
internal static class TestDb
{
    public static (BotDbContext Context, SqliteConnection Connection) Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connection).Options;
        var context = new BotDbContext(options);
        context.Database.Migrate();
        return (context, connection);
    }
}
```

> The Pairing test project references `Microsoft.Data.Sqlite`/`Microsoft.EntityFrameworkCore.Sqlite` transitively through `RustPlusBot.Persistence`. If `SqliteConnection`/`UseSqlite` don't resolve, add `<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />` to the test csproj (it's already a central package version).

- [ ] **Step 2: Write the failing supervisor tests**

Create `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
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

public sealed class ConnectionSupervisorTests
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

        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using (var seed = new BotDbContext(
                   new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connection).Options))
        {
            seed.Database.Migrate();
        }

        services.AddSingleton(connection);
        services.AddScoped(sp => new BotDbContext(
            new DbContextOptionsBuilder<BotDbContext>().UseSqlite(sp.GetRequiredService<SqliteConnection>()).Options));
        services.AddScoped<IConnectionStore, ConnectionStore>();
        services.AddScoped<RustPlusBot.Persistence.Servers.IServerService, RustPlusBot.Persistence.Servers.ServerService>();

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
        return new Harness { Provider = provider, Dm = dm, Supervisor = provider.GetRequiredService<ConnectionSupervisor>() };
    }

    private static async Task<(Guid ServerId, Guid CredA, Guid CredB)> SeedAsync(
        ServiceProvider provider, CredentialStatus bStatus = CredentialStatus.Standby)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        context.RustServers.Add(server);
        var a = new PlayerCredential
        {
            GuildId = 10UL, RustServerId = server.Id, OwnerUserId = 1UL, SteamId = 100UL,
            ProtectedPlayerToken = "111", Status = CredentialStatus.Active,
        };
        var b = new PlayerCredential
        {
            GuildId = 10UL, RustServerId = server.Id, OwnerUserId = 2UL, SteamId = 200UL,
            ProtectedPlayerToken = "222", Status = bStatus,
        };
        context.PlayerCredentials.AddRange(a, b);
        await context.SaveChangesAsync();
        return (server.Id, a.Id, b.Id);
    }

    private static async Task<ConnectionState?> WaitForStateAsync(
        ServiceProvider provider, Guid serverId, Func<ConnectionState, bool> predicate)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var scope = provider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await store.GetStateAsync(10UL, serverId);
            if (state is not null && predicate(state))
            {
                return state;
            }

            await Task.Delay(15);
        }

        return null;
    }

    private static async Task<CredentialStatus> CredStatusAsync(ServiceProvider provider, Guid credId)
    {
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BotDbContext>();
        return (await context.PlayerCredentials.SingleAsync(c => c.Id == credId)).Status;
    }

    [Fact]
    public async Task Connect_Healthy_BecomesConnectedWithPlayerCount()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(7));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        Assert.Equal(7, state!.PlayerCount);
    }

    [Fact]
    public async Task Connect_AuthRejected_FailsOverToNextStandbyAndDmsOwner()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.AuthRejected); // credential A
        source.EnqueueConnect(SocketConnectOutcome.Connected);    // credential B
        source.EnqueueHeartbeat(HeartbeatResult.Ok(3));
        await using var h = CreateHarness(source);
        var (serverId, credA, credB) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        Assert.Equal(CredentialStatus.Invalid, await CredStatusAsync(h.Provider, credA));
        Assert.Equal(CredentialStatus.Active, await CredStatusAsync(h.Provider, credB));
        await h.Dm.Received(1).SendAsync(1UL, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connect_AllCredentialsRejected_BecomesNoCredentials()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.AuthRejected); // only credential, A
        await using var h = CreateHarness(source);
        // Seed with B already Invalid so A is the only eligible credential.
        var (serverId, credA, _) = await SeedAsync(h.Provider, bStatus: CredentialStatus.Invalid);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.NoCredentials);
        Assert.NotNull(state);
        Assert.Equal(CredentialStatus.Invalid, await CredStatusAsync(h.Provider, credA));
    }

    [Fact]
    public async Task Connect_UnreachableThenConnected_RetriesAndRecovers()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Unreachable);
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        Assert.True(source.CreateCount >= 2);
    }

    [Fact]
    public async Task Heartbeat_Unreachable_ReconnectsAndRecovers()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(2));        // first heartbeat → Connected
        source.EnqueueHeartbeat(HeartbeatResult.Unreachable);  // next heartbeat → drop
        source.EnqueueConnect(SocketConnectOutcome.Connected); // reconnect
        source.EnqueueHeartbeat(HeartbeatResult.Ok(4));
        await using var h = CreateHarness(source);
        var (serverId, _, _) = await SeedAsync(h.Provider);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId);

        var recovered = await WaitForStateAsync(
            h.Provider, serverId, s => s.Status == ConnectionStatus.Connected && s.PlayerCount == 4);
        Assert.NotNull(recovered);
        Assert.True(source.CreateCount >= 2);
    }

    [Fact]
    public async Task StartAll_StartsAConnectionPerConnectableServer()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedAsync(h.Provider);

        await h.Supervisor.StartAllAsync();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
        while (source.CreateCount < 1 && DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(15);
        }

        Assert.True(source.CreateCount >= 1);
    }

    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required IUserDmSender Dm { get; init; }
        public required ConnectionSupervisor Supervisor { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAllAsync();
            await Provider.DisposeAsync();
        }
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionSupervisorTests`
Expected: FAIL — `IConnectionSupervisor`/`ConnectionSupervisor` not defined.

- [ ] **Step 4: Create the interface**

Create `src/RustPlusBot.Features.Connections/Supervisor/IConnectionSupervisor.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Supervisor;

/// <summary>Owns the live Rust+ sockets — one per (guild, server).</summary>
internal interface IConnectionSupervisor
{
    /// <summary>Starts a connection for every server that has a non-Invalid credential (called once at startup).</summary>
    Task StartAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts (or restarts) the connection for one server (after a swap or on server registration).</summary>
    Task EnsureConnectionAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Stops the connection for one server, if running.</summary>
    Task StopAsync(ulong guildId, Guid serverId);

    /// <summary>Cancels and disposes every connection (called on shutdown).</summary>
    Task StopAllAsync();
}
```

- [ ] **Step 5: Implement the supervisor**

Create `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Connections.Supervisor;

/// <summary>Default <see cref="IConnectionSupervisor"/>: one connect→heartbeat→failover loop per (guild, server).</summary>
/// <param name="source">Creates sockets (RustPlusApi in production, a fake in tests).</param>
/// <param name="scopeFactory">Opens scopes for the scoped stores.</param>
/// <param name="dmSender">DMs an owner when their credential is rejected.</param>
/// <param name="protector">Unprotects stored tokens before connecting.</param>
/// <param name="eventBus">Publishes ConnectionStatusChangedEvent on state changes.</param>
/// <param name="options">Timeouts/backoff/heartbeat settings.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ConnectionSupervisor(
    IRustSocketSource source,
    IServiceScopeFactory scopeFactory,
    IUserDmSender dmSender,
    ICredentialProtector protector,
    IEventBus eventBus,
    IOptions<ConnectionOptions> options,
    ILogger<ConnectionSupervisor> logger) : IConnectionSupervisor, IAsyncDisposable
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), Handle> _connections = new();
    private readonly ConnectionOptions _options = options.Value;
    private readonly CancellationTokenSource _shutdown = new();

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await StopAllAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }

    /// <inheritdoc />
    public async Task StartAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<(ulong GuildId, Guid ServerId)> servers;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            servers = await store.ListConnectableServersAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var (guildId, serverId) in servers)
        {
            await EnsureConnectionAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task EnsureConnectionAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        var key = (guildId, serverId);
        await StopConnectionAsync(key).ConfigureAwait(false);
        if (_shutdown.IsCancellationRequested)
        {
            return;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        _connections[key] = new Handle(cts, Task.Run(() => RunAsync(key, cts.Token), CancellationToken.None));
    }

    /// <inheritdoc />
    public Task StopAsync(ulong guildId, Guid serverId) => StopConnectionAsync((guildId, serverId));

    /// <inheritdoc />
    public async Task StopAllAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        foreach (var key in _connections.Keys.ToList())
        {
            await StopConnectionAsync(key).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Connection loop for server {ServerId} faulted.")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception, Guid serverId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Stored token for credential {CredentialId} is unreadable.")]
    private static partial void LogUnreadableToken(ILogger logger, Exception exception, Guid credentialId);

    private async Task StopConnectionAsync((ulong Guild, Guid Server) key)
    {
        if (!_connections.TryRemove(key, out var handle))
        {
            return;
        }

        await handle.StopAsync().ConfigureAwait(false);
        await handle.DisposeAsync().ConfigureAwait(false);
    }

    private async Task RunAsync((ulong Guild, Guid Server) key, CancellationToken ct)
    {
        var delay = _options.InitialRetryDelay;
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var prepared = await PrepareAsync(key, ct).ConfigureAwait(false);
                if (prepared is null)
                {
                    await PublishStatusAsync(key, ConnectionStatus.NoCredentials, null, null, ct).ConfigureAwait(false);
                    return;
                }

                var p = prepared.Value;
                await PublishStatusAsync(key, ConnectionStatus.Connecting, null, p.CredentialId, ct)
                    .ConfigureAwait(false);

                var connection = source.Create(p.Ip, p.Port, p.SteamId, p.PlayerToken);
                SocketConnectOutcome outcome;
                try
                {
                    outcome = await connection.ConnectAsync(_options.ConnectTimeout, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                if (outcome == SocketConnectOutcome.AuthRejected)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    await FailoverAsync(key, p.CredentialId, p.OwnerUserId, p.ServerName).ConfigureAwait(false);
                    delay = _options.InitialRetryDelay;
                    continue;
                }

                if (outcome == SocketConnectOutcome.Unreachable)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                    await PublishStatusAsync(key, ConnectionStatus.Unreachable, null, p.CredentialId, ct)
                        .ConfigureAwait(false);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = NextDelay(delay);
                    continue;
                }

                // Connected: run the heartbeat loop until it signals a reason to reconnect.
                delay = _options.InitialRetryDelay;
                ReconnectReason reason;
                try
                {
                    reason = await RunConnectedAsync(key, connection, p.CredentialId, ct).ConfigureAwait(false);
                }
                finally
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }

                if (reason == ReconnectReason.AuthRejected)
                {
                    await FailoverAsync(key, p.CredentialId, p.OwnerUserId, p.ServerName).ConfigureAwait(false);
                }
                else if (reason == ReconnectReason.Unreachable)
                {
                    await PublishStatusAsync(key, ConnectionStatus.Unreachable, null, p.CredentialId, ct)
                        .ConfigureAwait(false);
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                    delay = NextDelay(delay);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
#pragma warning disable CA1031 // Broad catch is intentional: a faulting loop must not crash the host or other servers.
        catch (Exception ex)
        {
            LogLoopFaulted(logger, ex, key.Server);
        }
#pragma warning restore CA1031
    }

    private async Task<ReconnectReason> RunConnectedAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        Guid credentialId,
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

    // Returns the connection inputs, auto-promoting the oldest Standby when there is no Active credential and
    // marking unreadable tokens Invalid (skipping to the next candidate). Null = nothing eligible (NoCredentials).
    private async Task<Prepared?> PrepareAsync((ulong Guild, Guid Server) key, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();

            var server = await servers.GetAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
            if (server is null)
            {
                return null;
            }

            while (!ct.IsCancellationRequested)
            {
                var active = await store.GetActiveCredentialAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
                if (active is null)
                {
                    var pool = await store.ListPoolAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
                    var next = pool.FirstOrDefault(c => c.Status == CredentialStatus.Standby);
                    if (next is null)
                    {
                        return null;
                    }

                    await store.PromoteAsync(key.Guild, key.Server, next.Id, ct).ConfigureAwait(false);
                    active = next;
                }

                string token;
                try
                {
                    token = protector.Unprotect(active.ProtectedPlayerToken);
                }
                catch (CryptographicException ex)
                {
                    LogUnreadableToken(logger, ex, active.Id);
                    await store.MarkInvalidAsync(active.Id, ct).ConfigureAwait(false);
                    await dmSender.SendAsync(
                            active.OwnerUserId,
                            $"Your Rust+ credential for **{server.Name}** could not be read — reconnect in #setup.")
                        .ConfigureAwait(false);
                    continue;
                }

                return new Prepared(server.Ip, server.Port, server.Name, active.Id, active.OwnerUserId, active.SteamId,
                    token);
            }

            return null;
        }
    }

    private async Task FailoverAsync((ulong Guild, Guid Server) key, Guid credentialId, ulong ownerUserId,
        string serverName)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            await store.MarkInvalidAsync(credentialId).ConfigureAwait(false);
        }

        await dmSender.SendAsync(
                ownerUserId,
                $"Your Rust+ credential for **{serverName}** was rejected — reconnect in #setup to keep it in the pool.")
            .ConfigureAwait(false);
    }

    private async Task PublishStatusAsync(
        (ulong Guild, Guid Server) key,
        ConnectionStatus status,
        int? playerCount,
        Guid? activeCredentialId,
        CancellationToken ct)
    {
        bool changed;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            changed = await store
                .UpsertStatusAsync(key.Guild, key.Server, status, playerCount, activeCredentialId, ct)
                .ConfigureAwait(false);
        }

        if (changed)
        {
            await eventBus.PublishAsync(new ConnectionStatusChangedEvent(key.Guild, key.Server), ct)
                .ConfigureAwait(false);
        }
    }

    private TimeSpan NextDelay(TimeSpan delay) =>
        delay < _options.MaxRetryDelay
            ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxRetryDelay.Ticks))
            : _options.MaxRetryDelay;

    private enum ReconnectReason
    {
        Stopped,
        Unreachable,
        AuthRejected,
    }

    private readonly record struct Prepared(
        string Ip,
        int Port,
        string ServerName,
        Guid CredentialId,
        ulong OwnerUserId,
        ulong SteamId,
        string PlayerToken);

    private sealed class Handle(CancellationTokenSource cts, Task runTask) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            cts.Dispose();
            return ValueTask.CompletedTask;
        }

        public async Task StopAsync()
        {
            await cts.CancelAsync().ConfigureAwait(false);
            try
            {
#pragma warning disable VSTHRD003 // Suppress: task is owned by this Handle and explicitly joined on stop.
                await runTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on stop.
            }
        }
    }
}
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionSupervisorTests`
Expected: PASS (6 tests). If a heartbeat-timing test is flaky, confirm the `ConnectionOptions` in the harness use the millisecond values above.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Supervisor/ tests/RustPlusBot.Features.Connections.Tests/
git commit -m "feat(connections): add ConnectionSupervisor (connect, heartbeat, failover, restart-survival)"
```

---

## Task 8: `ConnectionHostedService`

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Hosting/ConnectionHostedService.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionHostedServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.Connections.Tests/ConnectionHostedServiceTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Hosting;
using RustPlusBot.Features.Connections.Supervisor;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ConnectionHostedServiceTests
{
    [Fact]
    public async Task ServerRegistered_EnsuresAConnection()
    {
        var supervisor = Substitute.For<IConnectionSupervisor>();
        var bus = new InMemoryEventBus();
        var service = new ConnectionHostedService(supervisor, bus,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConnectionHostedService>.Instance);

        await service.StartAsync(default);
        await bus.PublishAsync(new ServerRegisteredEvent(10UL, Guid.NewGuid()));

        await WaitAsync(async () =>
            (await supervisor.ReceivedCalls().ToAsyncEnumerable().CountAsync()) >= 0); // settle scheduler
        await Task.Delay(100);
        await supervisor.Received().EnsureConnectionAsync(10UL, Arg.Any<Guid>(), Arg.Any<CancellationToken>());

        await service.StopAsync(default);
        await supervisor.Received(1).StopAllAsync();
    }

    private static async Task WaitAsync(Func<Task<bool>> _)
    {
        await Task.Delay(50);
    }
}
```

> NOTE: the consumer runs on a background loop, so the test gives it a short settle window (`Task.Delay`). Keep the delay; it is the same approach used to test event-bus consumers elsewhere.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionHostedServiceTests`
Expected: FAIL — `ConnectionHostedService` not defined.

- [ ] **Step 3: Implement the hosted service**

Create `src/RustPlusBot.Features.Connections/Hosting/ConnectionHostedService.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Supervisor;

namespace RustPlusBot.Features.Connections.Hosting;

/// <summary>Drives the connection supervisor: start all on startup, react to server registration, stop on shutdown.</summary>
/// <param name="supervisor">The connection supervisor.</param>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ConnectionHostedService(
    IConnectionSupervisor supervisor,
    IEventBus eventBus,
    ILogger<ConnectionHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _eventLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _eventLoop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_eventLoop is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // Suppress: this is our own loop task, joined on stop.
                await _eventLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        await supervisor.StopAllAsync().ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await supervisor.StartAllAsync(cancellationToken).ConfigureAwait(false);

            await foreach (var registered in eventBus.SubscribeAsync<ServerRegisteredEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await supervisor.EnsureConnectionAsync(registered.GuildId, registered.ServerId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch is intentional: a faulting consumer must not crash the host.
        catch (Exception ex)
        {
            LogLoopFaulted(logger, ex);
        }
#pragma warning restore CA1031
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Connection hosted-service loop faulted.")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
```

> NOTE: unlike Pairing/Workspace, this service does not need the Discord `Ready` event — it has no Discord dependency. `StartAllAsync` runs immediately; sockets that need a still-empty DB simply find nothing to start. Subscribing to `ServerRegisteredEvent` *before* `StartAllAsync` finishes is fine (the bus registers the subscriber eagerly).

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionHostedServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Hosting/ tests/RustPlusBot.Features.Connections.Tests/ConnectionHostedServiceTests.cs
git commit -m "feat(connections): add ConnectionHostedService"
```

---

## Task 9: `#info` live status + swap select (Workspace renderer)

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceComponentIds.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`
- Modify: `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`

- [ ] **Step 1: Add the swap custom-id prefix**

In `src/RustPlusBot.Features.Workspace/WorkspaceComponentIds.cs`, add inside the class:

```csharp
/// <summary>Prefix for the #info "swap active player" select; the server id is appended. Handled by Connections.</summary>
public const string ServerInfoSwapPrefix = "workspace:info:swap:";
```

- [ ] **Step 2: Add localization strings**

In `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`, add these keys to BOTH the `["en"]` and `["fr"]` dictionaries (English shown; French equivalents in parentheses — use the parenthesized text as the `fr` value):

```csharp
["server.info.status.label"] = "Status",                       // fr: "État"
["server.info.status.connecting"] = "Connecting…",             // fr: "Connexion…"
["server.info.status.connected"] = "Connected",                // fr: "Connecté"
["server.info.status.unreachable"] = "Server unreachable",     // fr: "Serveur injoignable"
["server.info.status.nocredentials"] = "No working credentials", // fr: "Aucun identifiant valide"
["server.info.player.label"] = "Active player",                // fr: "Joueur actif"
["server.info.players.label"] = "Players online",              // fr: "Joueurs en ligne"
["server.info.swap.placeholder"] = "Switch active player",     // fr: "Changer le joueur actif"
["server.info.none"] = "—",                                    // fr: "—"
```

Concretely, in the `["en"]` block add the nine pairs above; in the `["fr"]` block add the same nine keys with the French values.

- [ ] **Step 3: Update the renderer tests first (TDD)**

Replace the `ServerInfo_TitleIsServerName_AndEndpointShown` test in `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs` and add two more. Add these usings at the top of the file:

```csharp
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Persistence.Connections;
```

Replace the old ServerInfo test with:

```csharp
[Fact]
public async Task ServerInfo_Connected_ShowsStatusActivePlayerAndCount_AndSwapSelect()
{
    var serverId = Guid.NewGuid();
    var credId = Guid.NewGuid();
    var servers = Substitute.For<IServerService>();
    servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "Rustopia EU", Ip = "1.2.3.4", Port = 28015 });

    var connections = Substitute.For<IConnectionStore>();
    connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new ConnectionState
        {
            RustServerId = serverId, GuildId = 1, ActiveCredentialId = credId,
            Status = ConnectionStatus.Connected, PlayerCount = 12,
        });
    connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new List<PlayerCredential>
        {
            new() { Id = credId, GuildId = 1, RustServerId = serverId, OwnerUserId = 7, SteamId = 76561198000000000UL, Status = CredentialStatus.Active },
        });

    var renderer = new ServerInfoMessageRenderer(servers, connections, Loc);

    var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

    Assert.Contains("Rustopia EU", payload.Embed!.Title, StringComparison.Ordinal);
    Assert.Contains("12", string.Concat(payload.Embed.Fields.Select(f => f.Value)), StringComparison.Ordinal);
    Assert.Contains("76561198000000000", string.Concat(payload.Embed.Fields.Select(f => f.Value)), StringComparison.Ordinal);
    var selects = payload.Components!.Components.OfType<ActionRowComponent>()
        .SelectMany(r => r.Components).OfType<SelectMenuComponent>();
    Assert.Contains(selects, s => s.CustomId == $"workspace:info:swap:{serverId}");
}

[Fact]
public async Task ServerInfo_NoCredentials_ShowsNoCredentialsAndNoCount()
{
    var serverId = Guid.NewGuid();
    var servers = Substitute.For<IServerService>();
    servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "S", Ip = "1.2.3.4", Port = 28015 });
    var connections = Substitute.For<IConnectionStore>();
    connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new ConnectionState { RustServerId = serverId, GuildId = 1, Status = ConnectionStatus.NoCredentials });
    connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new List<PlayerCredential>());

    var renderer = new ServerInfoMessageRenderer(servers, connections, Loc);

    var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

    Assert.Contains("No working credentials",
        string.Concat(payload.Embed!.Fields.Select(f => f.Value)), StringComparison.Ordinal);
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter RendererTests`
Expected: FAIL — `ServerInfoMessageRenderer` has no 3-arg constructor.

- [ ] **Step 5: Rewrite the renderer**

Replace `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`:

```csharp
using System.Globalization;
using Discord;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders a server's #info embed with live connection status and the ManageGuild swap select.</summary>
/// <param name="servers">Server lookup.</param>
/// <param name="connections">Live connection state + pool.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class ServerInfoMessageRenderer(
    IServerService servers,
    IConnectionStore connections,
    ILocalizer localizer) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.ServerInfo;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var server = await servers.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            return new MessagePayload(null, null, null);
        }

        var state = await connections.GetStateAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var pool = await connections.ListPoolAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        var status = state?.Status ?? ConnectionStatus.NoCredentials;

        var active = pool.FirstOrDefault(c => state?.ActiveCredentialId is Guid id && c.Id == id)
                     ?? pool.FirstOrDefault(c => c.Status == CredentialStatus.Active);
        var none = localizer.Get("server.info.none", context.Culture);
        var port = server.Port.ToString(CultureInfo.InvariantCulture);

        var embed = new EmbedBuilder()
            .WithTitle($"{Glyph(status)} {server.Name}")
            .WithDescription(localizer.Get("server.info.endpoint", context.Culture, server.Ip, port))
            .WithColor(ColorFor(status))
            .AddField(localizer.Get("server.info.status.label", context.Culture), StatusText(status, context.Culture))
            .AddField(localizer.Get("server.info.player.label", context.Culture),
                active is null ? none : active.SteamId.ToString(CultureInfo.InvariantCulture));

        if (status == ConnectionStatus.Connected && state?.PlayerCount is int count)
        {
            embed.AddField(localizer.Get("server.info.players.label", context.Culture),
                count.ToString(CultureInfo.InvariantCulture));
        }

        var eligible = pool.Where(c => c.Status != CredentialStatus.Invalid).ToList();
        MessageComponent? components = null;
        if (eligible.Count > 0)
        {
            var select = new SelectMenuBuilder()
                .WithCustomId($"{WorkspaceComponentIds.ServerInfoSwapPrefix}{serverId}")
                .WithPlaceholder(localizer.Get("server.info.swap.placeholder", context.Culture));
            foreach (var credential in eligible)
            {
                var label = credential.SteamId.ToString(CultureInfo.InvariantCulture);
                select.AddOption(label, credential.Id.ToString(), isDefault: credential.Id == active?.Id);
            }

            components = new ComponentBuilder().WithSelectMenu(select).Build();
        }

        return new MessagePayload(null, embed.Build(), components);
    }

    private static string Glyph(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => "🟢",
        ConnectionStatus.Connecting or ConnectionStatus.Unreachable => "🟡",
        _ => "🔴",
    };

    private static Color ColorFor(ConnectionStatus status) => status switch
    {
        ConnectionStatus.Connected => Color.Green,
        ConnectionStatus.Connecting or ConnectionStatus.Unreachable => Color.Gold,
        _ => Color.Red,
    };

    private string StatusText(ConnectionStatus status, string culture) => status switch
    {
        ConnectionStatus.Connecting => localizer.Get("server.info.status.connecting", culture),
        ConnectionStatus.Connected => localizer.Get("server.info.status.connected", culture),
        ConnectionStatus.Unreachable => localizer.Get("server.info.status.unreachable", culture),
        _ => localizer.Get("server.info.status.nocredentials", culture),
    };
}
```

- [ ] **Step 6: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter RendererTests`
Expected: PASS. Also run the full Workspace test project to catch the `WorkspaceRegistrationTests` DI resolve (the renderer now needs `IConnectionStore`, which `AddBotPersistence` registers — confirm that test calls `AddBotPersistence`).

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Workspace/ tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs
git commit -m "feat(workspace): live status and swap select on #info"
```

---

## Task 10: Workspace consumes `ConnectionStatusChangedEvent`

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/Hosting/WorkspaceHostedService.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Hosting/WorkspaceConnectionStatusTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Hosting/WorkspaceConnectionStatusTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Hosting;
using RustPlusBot.Features.Workspace.Reconciler;

namespace RustPlusBot.Features.Workspace.Tests.Hosting;

public sealed class WorkspaceConnectionStatusTests
{
    [Fact]
    public async Task ConnectionStatusChanged_ReconcilesThatServer()
    {
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var services = new ServiceCollection();
        services.AddScoped(_ => reconciler);
        await using var provider = services.BuildServiceProvider();

        var bus = new InMemoryEventBus();
        var client = new Discord.WebSocket.DiscordSocketClient();
        var service = new WorkspaceHostedService(client, bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkspaceHostedService>.Instance);

        await service.StartAsync(default);
        var serverId = Guid.NewGuid();
        await bus.PublishAsync(new ConnectionStatusChangedEvent(10UL, serverId));
        await Task.Delay(100);

        await reconciler.Received().ReconcileServerAsync(10UL, serverId, Arg.Any<CancellationToken>());

        await service.StopAsync(default);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter WorkspaceConnectionStatusTests`
Expected: FAIL — the service does not yet consume `ConnectionStatusChangedEvent`.

- [ ] **Step 3: Add a second subscription**

In `src/RustPlusBot.Features.Workspace/Hosting/WorkspaceHostedService.cs`:

Add a second loop field and start it in `StartAsync`. Replace the single `_eventLoop` with two:

```csharp
private Task? _serverRegisteredLoop;
private Task? _connectionStatusLoop;
```

In `StartAsync`, replace the `_eventLoop = ...` line with:

```csharp
_serverRegisteredLoop = Task.Run(() => ConsumeServerRegisteredAsync(_cts.Token), CancellationToken.None);
_connectionStatusLoop = Task.Run(() => ConsumeConnectionStatusAsync(_cts.Token), CancellationToken.None);
```

In `StopAsync`, replace the single-loop await with awaiting both (guard each for null, swallow `OperationCanceledException`), e.g.:

```csharp
await _cts.CancelAsync().ConfigureAwait(false);
foreach (var loop in new[] { _serverRegisteredLoop, _connectionStatusLoop })
{
    if (loop is null)
    {
        continue;
    }

    try
    {
#pragma warning disable VSTHRD003
        await loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }
    catch (OperationCanceledException)
    {
        // Expected on shutdown.
    }
}
```

Add the new consumer method (mirrors `ConsumeServerRegisteredAsync`), and add `using RustPlusBot.Abstractions.Events;` if not already present:

```csharp
private async Task ConsumeConnectionStatusAsync(CancellationToken cancellationToken)
{
    try
    {
        await foreach (var changed in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                           .ConfigureAwait(false))
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
                await reconciler.ReconcileServerAsync(changed.GuildId, changed.ServerId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
    catch (OperationCanceledException)
    {
        // Shutting down.
    }
    catch (Exception ex) // Broad catch is intentional: a faulting consumer must not crash the host.
    {
        logger.LogError(ex, "ConnectionStatusChanged consumer faulted.");
    }
}
```

> Keep the existing `ConsumeServerRegisteredAsync` method as-is.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter WorkspaceConnectionStatusTests`
Expected: PASS. Run the whole Workspace test project too: `dotnet test tests/RustPlusBot.Features.Workspace.Tests` → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Workspace/Hosting/WorkspaceHostedService.cs tests/RustPlusBot.Features.Workspace.Tests/Hosting/
git commit -m "feat(workspace): re-render #info on ConnectionStatusChangedEvent"
```

---

## Task 11: Swap module + `AddConnections` + Host wiring

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Modules/ConnectionComponentModule.cs`
- Create: `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Host/Program.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionRegistrationTests.cs`

- [ ] **Step 1: Write the failing DI registration test**

Create `tests/RustPlusBot.Features.Connections.Tests/ConnectionRegistrationTests.cs`:

```csharp
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord.Notifications;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ConnectionRegistrationTests
{
    [Fact]
    public async Task Services_Resolve()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(Substitute.For<ICredentialProtector>());
        services.AddSingleton(Substitute.For<IUserDmSender>());
        services.AddLogging();
        services.AddBotPersistence("DataSource=:memory:");
        services.AddOptions<ConnectionOptions>();
        services.AddConnections();

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<IConnectionSupervisor>());
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IConnectionStore>());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionRegistrationTests`
Expected: FAIL — `AddConnections` not defined.

- [ ] **Step 3: Create the swap module**

Create `src/RustPlusBot.Features.Connections/Modules/ConnectionComponentModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Connections.Modules;

/// <summary>Handles the #info "switch active player" select (ManageGuild).</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class ConnectionComponentModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Promotes the chosen pooled credential to active and restarts the socket on it.</summary>
    /// <param name="serverIdRaw">The server id captured from the custom id.</param>
    /// <param name="selectedValues">The selected credential id (expects exactly one).</param>
    [ComponentInteraction($"{WorkspaceComponentIds.ServerInfoSwapPrefix}*")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task SwapAsync(string serverIdRaw, string[] selectedValues)
    {
        ArgumentNullException.ThrowIfNull(selectedValues);
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(serverIdRaw, out var serverId)
            || selectedValues.Length == 0
            || !Guid.TryParse(selectedValues[0], out var credentialId))
        {
            await RespondAsync("That selection wasn't valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            if (!await store.PromoteAsync(Context.Guild.Id, serverId, credentialId).ConfigureAwait(false))
            {
                await FollowupAsync("That player isn't available to activate.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var supervisor = scope.ServiceProvider.GetRequiredService<IConnectionSupervisor>();
            await supervisor.EnsureConnectionAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            await FollowupAsync("Switching active player…", ephemeral: true).ConfigureAwait(false);
        }
    }
}
```

> NOTE: like `CredentialModule`/`SettingsComponentModule`, this interaction module is not unit-tested directly (it derives from `InteractionModuleBase`). Its branch logic is covered indirectly: `PromoteAsync` rejecting an invalid/unknown credential is tested in Task 3; the swap → restart path is exercised by the supervisor tests in Task 7.

- [ ] **Step 4: Create `AddConnections`**

Create `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Connections.Hosting;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Connections.Supervisor;

namespace RustPlusBot.Features.Connections;

/// <summary>DI registration for the live-connection feature.</summary>
public static class ConnectionServiceCollectionExtensions
{
    /// <summary>Registers the socket source, supervisor, module seam, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddConnections(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IRustSocketSource, RustPlusSocketSource>();
        services.AddSingleton<IConnectionSupervisor, ConnectionSupervisor>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(ConnectionServiceCollectionExtensions).Assembly));

        services.AddHostedService<ConnectionHostedService>();

        return services;
    }
}
```

> `IConnectionStore` is registered by `AddBotPersistence` (Task 3), not here.

- [ ] **Step 5: Wire into the Host**

In `src/RustPlusBot.Host/Program.cs`, add `using RustPlusBot.Features.Connections;` and, after the `AddPairing()` block, add:

```csharp
builder.Services.AddOptions<ConnectionOptions>()
    .Bind(builder.Configuration.GetSection("Connections"))
    .Validate(static o => o.ConnectTimeout > TimeSpan.Zero, "Connections:ConnectTimeout must be positive.")
    .Validate(static o => o.InitialRetryDelay > TimeSpan.Zero, "Connections:InitialRetryDelay must be positive.")
    .Validate(static o => o.MaxRetryDelay >= o.InitialRetryDelay,
        "Connections:MaxRetryDelay must be at least InitialRetryDelay.")
    .Validate(static o => o.HeartbeatInterval > TimeSpan.Zero, "Connections:HeartbeatInterval must be positive.")
    .Validate(static o => o.HeartbeatTimeout > TimeSpan.Zero, "Connections:HeartbeatTimeout must be positive.")
    .ValidateOnStart();
builder.Services.AddConnections();
```

- [ ] **Step 6: Run to verify pass + full suite**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionRegistrationTests`
Expected: PASS.

Run the whole solution: `dotnet build RustPlusBot.slnx` (0/0) and `dotnet test` (all green).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections/ src/RustPlusBot.Host/Program.cs tests/RustPlusBot.Features.Connections.Tests/ConnectionRegistrationTests.cs
git commit -m "feat(connections): swap module, AddConnections wiring, host registration"
```

---

## Task 12: Final verification + format gate

**Files:** none (verification only).

- [ ] **Step 1: Full build + test**

Run: `dotnet build RustPlusBot.slnx` → 0 warnings / 0 errors. `dotnet test` → all projects green.

- [ ] **Step 2: ReSharper format gate (CI parity)**

Run:

```bash
dotnet tool restore
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
```

Then `git diff` — review and commit any reordering the tool applied:

```bash
git add -A
git commit -m "style: apply ReSharper ReformatAndReorder"
```

- [ ] **Step 3: Confirm no EF model drift**

Run:

```bash
dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host
```

Expected: "No changes have been made" (the `LiveConnections` migration covers the model). If it reports drift, add a follow-up migration and re-run.

---

## Self-Review

**Spec coverage (spec §→task):**

- §1–2 context / RustPlusApi flow → grounded in Tasks 6–7 (adapter + supervisor).
- §3 decisions: new project (T5), hybrid health (T7 heartbeat), swap-select-on-#info (T9 + T11), ConnectionState source of truth (T1/T3), deterministic failover (T7 `PrepareAsync`), DM-on-token-death (T7 `FailoverAsync`/`PrepareAsync`). ✓
- §5 data model + `LiveConnections` migration → T1. ✓
- §6 `IConnectionStore` (Get, UpsertStatus+changed, GetActiveCredential, ListPool, Promote, MarkInvalid, ListConnectableServers) → T3. ✓
- §7 socket adapter (three-way mapping, untested shim) → T6. ✓
- §8 supervisor (connect/heartbeat/failover/restart-survival) + hosted service → T7, T8. ✓
- §9 `#info` embed + swap select + localization + cross-feature refresh → T9 + T10. ✓
- §10 owner DM via `IUserDmSender` → T4 + T7. ✓
- §11 options → T5 + T11. ✓
- §12 error-handling table → covered across T6/T7 branches + store. ✓
- §13 tests → store (T3), supervisor (T7), hosted service (T8), renderer (T9), workspace consumer (T10), DI (T11), adapter shim (T6), schema (T1). ✓
- §14 deferrals (IP-change, RustServer-removal cascade, disconnect/remove) → intentionally NOT implemented. ✓

**Placeholder scan:** No `TBD`/`TODO`/"add error handling"/"similar to Task N". The RustPlusApi member names in T6 carry explicit `VERIFY` notes (external package surface, by design — the integration shim).

**Type consistency:** `IConnectionStore` method names (`GetStateAsync`, `UpsertStatusAsync`, `GetActiveCredentialAsync`, `ListPoolAsync`, `PromoteAsync`, `MarkInvalidAsync`, `ListConnectableServersAsync`) are identical across T3 (definition), T7 (supervisor calls), T9 (renderer calls), T11 (module call). `ConnectionStatus` members (`Connecting`/`Connected`/`Unreachable`/`NoCredentials`) consistent T1↔T7↔T9. `SocketConnectOutcome`/`HeartbeatKind` consistent T5↔T6↔T7. `IUserDmSender.SendAsync(ulong, string, CancellationToken)` consistent T4↔T7. `WorkspaceComponentIds.ServerInfoSwapPrefix` consistent T9↔T11.
