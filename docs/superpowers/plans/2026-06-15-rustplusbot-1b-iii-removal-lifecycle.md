# Subsystem 1b-iii — Server removal & account disconnect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an admin permanently remove a registered Rust+ server and let a user fully disconnect their account, with correct cascade cleanup and prompt failover/UI refresh.

**Architecture:** Server removal is a **synchronous orchestrator** in Connections (`stop socket → delete row (cascades creds + connection-state) → tear down Discord`). Account disconnect runs in Pairing and signals its cross-feature effects through a **new `ServerCredentialsChangedEvent`** consumed by Connections (failover) and Workspace (#info refresh). Adds the missing `ConnectionState → RustServer` cascade FK.

**Tech Stack:** .NET 10, EF Core 10 (+ SQLite, `dotnet ef` local tool), Discord.Net 3.20, xUnit + NSubstitute, ReSharper `jb` formatter (CI gate).

**Spec:** `docs/superpowers/specs/2026-06-15-rustplusbot-1b-iii-removal-lifecycle-design.md`

**Branch:** `feat/removal-lifecycle` off `develop` (create before Task 1 if not already on it).

**Conventions (all tasks):**

- Build/test the whole solution: `dotnet build RustPlusBot.slnx` and `dotnet test RustPlusBot.slnx`. Run a single project's tests with `dotnet test tests/<Project>/<Project>.csproj`.
- Strict analyzers: the build must stay **0 warnings / 0 errors**.
- Mocking an **internal** interface with NSubstitute requires `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in that source project's csproj (Connections & Workspace already have it; Pairing gains it in Task 5).
- Before any push, run `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` and commit any reorders (the CI format gate is `jb`, not `dotnet format`).

---

### Task 1: `ConnectionState → RustServer` cascade FK + migration

**Files:**

- Modify: `src/RustPlusBot.Persistence/Configurations/ConnectionStateConfiguration.cs`
- Modify (fix under new FK): `tests/RustPlusBot.Persistence.Tests/Connections/ConnectionStateSchemaTests.cs`
- Modify (fix under new FK): `tests/RustPlusBot.Persistence.Tests/Connections/ConnectionStoreTests.cs`
- Create (generated): `src/RustPlusBot.Persistence/Migrations/<timestamp>_ServerRemovalCascade.cs` (+ `.Designer.cs`, snapshot update)

- [ ] **Step 1: Make the three existing tests seed a real server (keeps them valid once the FK exists)**

In `ConnectionStateSchemaTests.cs` add `using RustPlusBot.Domain.Servers;` and, in **both** test methods, replace `var serverId = Guid.NewGuid();` with a seeded server:

```csharp
var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
context.RustServers.Add(server);
await context.SaveChangesAsync();
var serverId = server.Id;
```

In `ConnectionStoreTests.cs`, in `UpsertStatus_InsertsThenReportsChangeOnlyWhenDifferent`, replace `var serverId = Guid.NewGuid();` with the same seed block (the file already has `using RustPlusBot.Domain.Servers;`).

- [ ] **Step 2: Run the Persistence tests to confirm they still pass on the current schema**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj`
Expected: PASS (seeding an extra server is harmless without the FK).

- [ ] **Step 3: Write the failing cascade test**

Append to `ConnectionStateSchemaTests.cs`:

```csharp
[Fact]
public async Task RemovingServer_CascadeDeletesItsConnectionState()
{
    var (context, connection) = SqliteContextFixture.Create();
    await using var _ = context;
    await using var __ = connection;

    var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
    context.RustServers.Add(server);
    context.ConnectionStates.Add(new ConnectionState
    {
        RustServerId = server.Id,
        GuildId = 10UL,
        Status = ConnectionStatus.Connected,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    });
    await context.SaveChangesAsync();

    context.RustServers.Remove(server);
    await context.SaveChangesAsync();

    Assert.Empty(await context.ConnectionStates.ToListAsync());
}
```

- [ ] **Step 4: Run it to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj --filter "FullyQualifiedName~RemovingServer_CascadeDeletesItsConnectionState"`
Expected: FAIL — `Assert.Empty()` fails (the connection-state row lingers; no cascade yet).

- [ ] **Step 5: Add the cascade FK to the configuration**

Replace the body of `ConnectionStateConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ConnectionStateConfiguration : IEntityTypeConfiguration<ConnectionState>
{
    public void Configure(EntityTypeBuilder<ConnectionState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.RustServerId);

        // Removing a RustServer cascades to its single connection-state row so no orphaned status lingers.
        builder.HasOne<RustServer>()
            .WithOne()
            .HasForeignKey<ConnectionState>(s => s.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 6: Generate the migration**

Run:

```bash
dotnet tool restore
dotnet ef migrations add ServerRemovalCascade \
  --project src/RustPlusBot.Persistence \
  --startup-project src/RustPlusBot.Persistence
```

Expected: a new `Migrations/<timestamp>_ServerRemovalCascade.cs` is created and `BotDbContextModelSnapshot.cs` updated. Open the migration and confirm it adds `FK_ConnectionStates_RustServers_RustServerId` with `onDelete: ReferentialAction.Cascade` (EF rebuilds the SQLite table to add it — that is expected).

- [ ] **Step 7: Run all Persistence tests**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj`
Expected: PASS (cascade test green; the three edited tests green because they seed a server).

- [ ] **Step 8: Verify no model drift**

Run:

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/RustPlusBot.Persistence \
  --startup-project src/RustPlusBot.Persistence
```

Expected: "No changes have been made to the model since the last migration." (exit 0)

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests
git commit -m "feat(persistence): cascade ConnectionState on RustServer delete"
```

---

### Task 2: `ICredentialStore` owner-removal methods

**Files:**

- Modify: `src/RustPlusBot.Abstractions/Credentials/ICredentialStore.cs`
- Modify: `src/RustPlusBot.Persistence/Credentials/CredentialStore.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Credentials/CredentialStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `CredentialStoreTests.cs`:

```csharp
[Fact]
public async Task RemoveForOwner_RemovesOwnersCredsAcrossServers_ReturnsDistinctServerIds_LeavesOthers()
{
    var (context, connection) = SqliteContextFixture.Create();
    await using var _ = context;
    await using var __ = connection;
    var serverA = await SeedServerAsync(context, 10UL);
    var serverB = await SeedServerAsync(context, 10UL, port: 28016);
    var store = new CredentialStore(context, PassThroughProtector());

    await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 1UL, 1UL, "t1"), markActive: true);
    await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverB, 1UL, 1UL, "t2"), markActive: true);
    await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 2UL, 2UL, "t3"), markActive: false);

    var affected = await store.RemoveForOwnerAsync(10UL, 1UL);

    Assert.Equal(2, affected.Count);
    Assert.Contains(serverA, affected);
    Assert.Contains(serverB, affected);
    var remaining = await context.PlayerCredentials.ToListAsync();
    Assert.Single(remaining);
    Assert.Equal(2UL, remaining[0].OwnerUserId);
}

[Fact]
public async Task RemoveForOwner_WhenNothingOwned_ReturnsEmpty()
{
    var (context, connection) = SqliteContextFixture.Create();
    await using var _ = context;
    await using var __ = connection;
    await SeedServerAsync(context, 10UL);
    var store = new CredentialStore(context, PassThroughProtector());

    var affected = await store.RemoveForOwnerAsync(10UL, 999UL);

    Assert.Empty(affected);
}

[Fact]
public async Task ListServerIdsForOwner_ReturnsDistinctServers()
{
    var (context, connection) = SqliteContextFixture.Create();
    await using var _ = context;
    await using var __ = connection;
    var serverA = await SeedServerAsync(context, 10UL);
    var serverB = await SeedServerAsync(context, 10UL, port: 28016);
    var store = new CredentialStore(context, PassThroughProtector());
    await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 1UL, 1UL, "t1"), markActive: true);
    await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverB, 1UL, 1UL, "t2"), markActive: true);

    var ids = await store.ListServerIdsForOwnerAsync(10UL, 1UL);

    Assert.Equal(2, ids.Count);
    Assert.Contains(serverA, ids);
    Assert.Contains(serverB, ids);
}
```

- [ ] **Step 2: Run to verify they fail to compile/fail**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj --filter "FullyQualifiedName~ForOwner"`
Expected: FAIL — `ICredentialStore` has no `RemoveForOwnerAsync`/`ListServerIdsForOwnerAsync` (compile error).

- [ ] **Step 3: Add the interface members**

In `ICredentialStore.cs`, add inside the interface:

```csharp
    /// <summary>
    /// Removes every player-credential the owner holds across all servers in the guild (full account
    /// removal). Returns the distinct server ids whose pool was affected, so callers can re-evaluate
    /// those connections.
    /// </summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user whose credentials are removed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The distinct server ids whose pool changed.</returns>
    Task<IReadOnlyList<Guid>> RemoveForOwnerAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>Lists the distinct server ids the owner currently holds a credential for in the guild.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The distinct server ids the owner has a credential for.</returns>
    Task<IReadOnlyList<Guid>> ListServerIdsForOwnerAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement them in `CredentialStore.cs`**

Add these methods to the class:

```csharp
    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> RemoveForOwnerAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default)
    {
        var owned = await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.OwnerUserId == ownerUserId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        if (owned.Count == 0)
        {
            return [];
        }

        var serverIds = owned.Select(c => c.RustServerId).Distinct().ToList();
        context.PlayerCredentials.RemoveRange(owned);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return serverIds;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> ListServerIdsForOwnerAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default) =>
        await context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.OwnerUserId == ownerUserId)
            .Select(c => c.RustServerId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj --filter "FullyQualifiedName~ForOwner"`
Expected: PASS (all three).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Abstractions src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests
git commit -m "feat(persistence): remove/list a user's credentials across a guild"
```

---

### Task 3: `ServerCredentialsChangedEvent`

**Files:**

- Create: `src/RustPlusBot.Abstractions/Events/ServerCredentialsChangedEvent.cs`

- [ ] **Step 1: Create the event record**

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// Raised when a server's credential pool changed out-of-band (e.g. a user disconnected their account),
/// so the live connection should be re-evaluated and the #info view refreshed.
/// </summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The affected server.</param>
public sealed record ServerCredentialsChangedEvent(ulong GuildId, Guid ServerId);
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/RustPlusBot.Abstractions/RustPlusBot.Abstractions.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Abstractions
git commit -m "feat(abstractions): add ServerCredentialsChangedEvent"
```

---

### Task 4: `IPairingSupervisor.StopListenerAsync`

**Files:**

- Modify: `src/RustPlusBot.Features.Pairing/Supervisor/IPairingSupervisor.cs`
- Modify: `src/RustPlusBot.Features.Pairing/Supervisor/PairingSupervisor.cs`
- Modify (track disposal): `tests/RustPlusBot.Features.Pairing.Tests/Fakes/FakePairingSource.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/PairingSupervisorTests.cs`

- [ ] **Step 1: Let the fake report disposals**

In `FakePairingSource.cs`, add a dispose counter and pass a callback into the listener. Add the field + property:

```csharp
    private int _disposeCount;

    /// <summary>How many created listeners have been disposed.</summary>
    public int DisposeCount => Volatile.Read(ref _disposeCount);
```

Change the `Create` return line to pass the callback:

```csharp
        return new FakeListener(outcome, () => ConnectedSignal.TrySetResult(),
            () => Interlocked.Increment(ref _disposeCount));
```

Replace `FakeListener` with:

```csharp
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
```

- [ ] **Step 2: Write the failing tests**

Append to `PairingSupervisorTests.cs`:

```csharp
[Fact]
public async Task StopListener_DisposesTheRunningListener()
{
    var source = new FakePairingSource();
    source.EnqueueOutcome(PairingConnectOutcome.Connected);
    await using var h = CreateHarness(source);
    await SeedRegistrationAsync(h.Provider, 10UL, 99UL);
    await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

    await h.Supervisor.StopListenerAsync(10UL, 99UL);

    var deadline = DateTimeOffset.UtcNow.AddSeconds(5);
    while (h.Source.DisposeCount < 1 && DateTimeOffset.UtcNow < deadline)
    {
        await Task.Delay(20);
    }

    Assert.True(h.Source.DisposeCount >= 1);
}

[Fact]
public async Task StopListener_WhenNotRunning_DoesNotThrow()
{
    var source = new FakePairingSource();
    await using var h = CreateHarness(source);

    await h.Supervisor.StopListenerAsync(10UL, 12345UL);
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj --filter "FullyQualifiedName~StopListener"`
Expected: FAIL — `IPairingSupervisor` has no public `StopListenerAsync(ulong, ulong)` (compile error).

- [ ] **Step 4: Add the interface member**

In `IPairingSupervisor.cs`, add:

```csharp
    /// <summary>Stops the listener for (guild, owner), if running. Idempotent.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <returns>A task that completes once the listener has stopped.</returns>
    Task StopListenerAsync(ulong guildId, ulong ownerUserId);
```

- [ ] **Step 5: Implement the public forwarder in `PairingSupervisor.cs`**

Add this method (it forwards to the existing private tuple overload):

```csharp
    /// <inheritdoc />
    public Task StopListenerAsync(ulong guildId, ulong ownerUserId) =>
        StopListenerAsync((guildId, ownerUserId));
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj`
Expected: PASS (all, including the existing supervisor tests).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Pairing tests/RustPlusBot.Features.Pairing.Tests
git commit -m "feat(pairing): expose StopListenerAsync on the supervisor"
```

---

### Task 5: `AccountDisconnectService` (Pairing)

**Files:**

- Modify: `src/RustPlusBot.Features.Pairing/RustPlusBot.Features.Pairing.csproj` (add DynamicProxyGenAssembly2)
- Create: `src/RustPlusBot.Features.Pairing/Accounts/IAccountDisconnectService.cs`
- Create: `src/RustPlusBot.Features.Pairing/Accounts/AccountDisconnectService.cs`
- Modify: `src/RustPlusBot.Features.Pairing/PairingServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/AccountDisconnectServiceTests.cs`

- [ ] **Step 1: Allow NSubstitute to proxy internal interfaces in Pairing**

In `RustPlusBot.Features.Pairing.csproj`, in the `<ItemGroup>` that has `<InternalsVisibleTo Include="RustPlusBot.Features.Pairing.Tests" />`, add:

```xml
    <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
```

- [ ] **Step 2: Create the service contract**

`src/RustPlusBot.Features.Pairing/Accounts/IAccountDisconnectService.cs`:

```csharp
namespace RustPlusBot.Features.Pairing.Accounts;

/// <summary>Full account disconnect: stop the listener, disable the registration, drop the user's pool credentials.</summary>
internal interface IAccountDisconnectService
{
    /// <summary>Reads what a disconnect would affect, for the confirmation dialog.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>Whether the user is connected and the names of servers they would be removed from.</returns>
    Task<AccountDisconnectPreview> PreviewAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the user's FCM listener, marks their registration Disabled, removes all their pool credentials
    /// in the guild, and publishes a <c>ServerCredentialsChangedEvent</c> per affected server.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of servers the user was removed from.</returns>
    Task<int> DisconnectAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);
}

/// <summary>What an account disconnect would affect.</summary>
/// <param name="IsConnected">True if the user has a non-Disabled registration or any pool credential.</param>
/// <param name="AffectedServerNames">Names of servers the user holds a credential for.</param>
internal sealed record AccountDisconnectPreview(bool IsConnected, IReadOnlyList<string> AffectedServerNames);
```

- [ ] **Step 3: Implement the service**

`src/RustPlusBot.Features.Pairing/Accounts/AccountDisconnectService.cs`:

```csharp
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Accounts;

/// <summary>Default <see cref="IAccountDisconnectService"/>.</summary>
/// <param name="supervisor">Stops the user's FCM listener.</param>
/// <param name="registrations">The FCM registration store.</param>
/// <param name="credentials">The player-credential pool store.</param>
/// <param name="servers">Resolves server names for the preview.</param>
/// <param name="eventBus">Publishes ServerCredentialsChangedEvent per affected server.</param>
internal sealed class AccountDisconnectService(
    IPairingSupervisor supervisor,
    IFcmRegistrationStore registrations,
    ICredentialStore credentials,
    IServerService servers,
    IEventBus eventBus) : IAccountDisconnectService
{
    /// <inheritdoc />
    public async Task<AccountDisconnectPreview> PreviewAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default)
    {
        var registration = await registrations.GetAsync(guildId, ownerUserId, cancellationToken).ConfigureAwait(false);
        var serverIds = await credentials.ListServerIdsForOwnerAsync(guildId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);

        var names = new List<string>(serverIds.Count);
        foreach (var serverId in serverIds)
        {
            var server = await servers.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (server is not null)
            {
                names.Add(server.Name);
            }
        }

        var isConnected = (registration is not null && registration.Status != FcmRegistrationStatus.Disabled)
                          || serverIds.Count > 0;
        return new AccountDisconnectPreview(isConnected, names);
    }

    /// <inheritdoc />
    public async Task<int> DisconnectAsync(
        ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default)
    {
        await supervisor.StopListenerAsync(guildId, ownerUserId).ConfigureAwait(false);

        var registration = await registrations.GetAsync(guildId, ownerUserId, cancellationToken).ConfigureAwait(false);
        if (registration is not null)
        {
            await registrations.SetStatusAsync(registration.Id, FcmRegistrationStatus.Disabled, cancellationToken)
                .ConfigureAwait(false);
        }

        var affected = await credentials.RemoveForOwnerAsync(guildId, ownerUserId, cancellationToken)
            .ConfigureAwait(false);
        foreach (var serverId in affected)
        {
            await eventBus.PublishAsync(new ServerCredentialsChangedEvent(guildId, serverId), cancellationToken)
                .ConfigureAwait(false);
        }

        return affected.Count;
    }
}
```

- [ ] **Step 4: Register the service**

In `PairingServiceCollectionExtensions.cs`, add `using RustPlusBot.Features.Pairing.Accounts;` and, after the supervisor registration:

```csharp
        services.AddScoped<IAccountDisconnectService, AccountDisconnectService>();
```

- [ ] **Step 5: Write the failing tests**

`tests/RustPlusBot.Features.Pairing.Tests/AccountDisconnectServiceTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Pairing.Accounts;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class AccountDisconnectServiceTests
{
    private static (AccountDisconnectService Sut, IPairingSupervisor Sup, IFcmRegistrationStore Regs,
        ICredentialStore Creds, IServerService Servers, IEventBus Bus) Create()
    {
        var sup = Substitute.For<IPairingSupervisor>();
        var regs = Substitute.For<IFcmRegistrationStore>();
        var creds = Substitute.For<ICredentialStore>();
        var servers = Substitute.For<IServerService>();
        var bus = Substitute.For<IEventBus>();
        return (new AccountDisconnectService(sup, regs, creds, servers, bus), sup, regs, creds, servers, bus);
    }

    [Fact]
    public async Task Disconnect_StopsListener_DisablesRegistration_RemovesCreds_PublishesPerServer()
    {
        var (sut, sup, regs, creds, _, bus) = Create();
        var regId = Guid.NewGuid();
        var s1 = Guid.NewGuid();
        var s2 = Guid.NewGuid();
        regs.GetAsync(10UL, 99UL, Arg.Any<CancellationToken>())
            .Returns(new FcmRegistration { Id = regId, GuildId = 10UL, OwnerUserId = 99UL });
        creds.RemoveForOwnerAsync(10UL, 99UL, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { s1, s2 });

        var count = await sut.DisconnectAsync(10UL, 99UL);

        Assert.Equal(2, count);
        await sup.Received(1).StopListenerAsync(10UL, 99UL);
        await regs.Received(1).SetStatusAsync(regId, FcmRegistrationStatus.Disabled, Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(
            Arg.Is<ServerCredentialsChangedEvent>(e => e.GuildId == 10UL && e.ServerId == s1),
            Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(
            Arg.Is<ServerCredentialsChangedEvent>(e => e.ServerId == s2), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disconnect_WhenNothingStored_StopsListener_NoEvents_ReturnsZero()
    {
        var (sut, sup, regs, creds, _, bus) = Create();
        regs.GetAsync(10UL, 99UL, Arg.Any<CancellationToken>()).Returns((FcmRegistration?)null);
        creds.RemoveForOwnerAsync(10UL, 99UL, Arg.Any<CancellationToken>()).Returns(new List<Guid>());

        var count = await sut.DisconnectAsync(10UL, 99UL);

        Assert.Equal(0, count);
        await sup.Received(1).StopListenerAsync(10UL, 99UL);
        await regs.DidNotReceive().SetStatusAsync(Arg.Any<Guid>(), Arg.Any<FcmRegistrationStatus>(),
            Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<ServerCredentialsChangedEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Preview_ReportsConnectedAndServerNames()
    {
        var (sut, _, regs, creds, servers, _) = Create();
        var s1 = Guid.NewGuid();
        regs.GetAsync(10UL, 99UL, Arg.Any<CancellationToken>())
            .Returns(new FcmRegistration { Id = Guid.NewGuid(), GuildId = 10UL, OwnerUserId = 99UL });
        creds.ListServerIdsForOwnerAsync(10UL, 99UL, Arg.Any<CancellationToken>())
            .Returns(new List<Guid> { s1 });
        servers.GetAsync(10UL, s1, Arg.Any<CancellationToken>())
            .Returns(new RustServer { Id = s1, GuildId = 10UL, Name = "Rustopia", Ip = "1.1.1.1", Port = 28015 });

        var preview = await sut.PreviewAsync(10UL, 99UL);

        Assert.True(preview.IsConnected);
        Assert.Equal(new[] { "Rustopia" }, preview.AffectedServerNames);
    }

    [Fact]
    public async Task Preview_WhenNothing_ReportsNotConnected()
    {
        var (sut, _, regs, creds, _, _) = Create();
        regs.GetAsync(10UL, 99UL, Arg.Any<CancellationToken>()).Returns((FcmRegistration?)null);
        creds.ListServerIdsForOwnerAsync(10UL, 99UL, Arg.Any<CancellationToken>()).Returns(new List<Guid>());

        var preview = await sut.PreviewAsync(10UL, 99UL);

        Assert.False(preview.IsConnected);
        Assert.Empty(preview.AffectedServerNames);
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj --filter "FullyQualifiedName~AccountDisconnect"`
Expected: PASS (all four).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Pairing tests/RustPlusBot.Features.Pairing.Tests
git commit -m "feat(pairing): account disconnect service (disable + drop creds + notify)"
```

---

### Task 6: `IServerWorkspaceRemover` public seam (Workspace)

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Teardown/IServerWorkspaceRemover.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Teardown/WorkspaceTeardownService.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs`

- [ ] **Step 1: Write the failing resolution test**

In `WorkspaceRegistrationTests.cs`, add to the `ReconcilerAndTeardown_ResolveFromScope` test, after the existing asserts:

```csharp
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IServerWorkspaceRemover>());
```

(The `using RustPlusBot.Features.Workspace.Teardown;` import is already present.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj --filter "FullyQualifiedName~ReconcilerAndTeardown_ResolveFromScope"`
Expected: FAIL — `IServerWorkspaceRemover` does not exist (compile error).

- [ ] **Step 3: Create the public interface**

`src/RustPlusBot.Features.Workspace/Teardown/IServerWorkspaceRemover.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Public seam to remove one server's provisioned Discord resources (used by the Connections removal flow).</summary>
public interface IServerWorkspaceRemover
{
    /// <summary>Deletes one server's category, channels, and provisioning records.</summary>
    /// <param name="guildId">The guild.</param>
    /// <param name="serverId">The server to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task.</returns>
    Task RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement it on the teardown service**

In `WorkspaceTeardownService.cs`, change the class declaration to also implement the new interface:

```csharp
internal sealed class WorkspaceTeardownService(
    IWorkspaceGateway gateway,
    IWorkspaceStore store,
    IProvisioningLock provisioningLock) : IWorkspaceTeardownService, IServerWorkspaceRemover
```

(The existing `RemoveServerAsync(ulong, Guid, CancellationToken)` method already satisfies `IServerWorkspaceRemover`.)

- [ ] **Step 5: Register the seam**

In `WorkspaceServiceCollectionExtensions.cs`, after `services.AddScoped<IWorkspaceTeardownService, WorkspaceTeardownService>();` add:

```csharp
        services.AddScoped<IServerWorkspaceRemover, WorkspaceTeardownService>();
```

- [ ] **Step 6: Run the workspace tests**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: PASS (resolution test green; teardown tests unaffected).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests
git commit -m "feat(workspace): public IServerWorkspaceRemover seam"
```

---

### Task 7: `ServerRemovalService` (Connections)

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Removal/IServerRemovalService.cs`
- Create: `src/RustPlusBot.Features.Connections/Removal/ServerRemovalService.cs`
- Modify: `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ServerRemovalServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Connections.Tests/ServerRemovalServiceTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Features.Connections.Removal;
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ServerRemovalServiceTests
{
    [Fact]
    public async Task RemoveServer_Stops_Deletes_TearsDown_InOrder()
    {
        var serverId = Guid.NewGuid();
        var supervisor = Substitute.For<IConnectionSupervisor>();
        var servers = Substitute.For<IServerService>();
        servers.RemoveAsync(10UL, serverId, Arg.Any<CancellationToken>()).Returns(true);
        var workspace = Substitute.For<IServerWorkspaceRemover>();
        var sut = new ServerRemovalService(supervisor, servers, workspace);

        var removed = await sut.RemoveServerAsync(10UL, serverId);

        Assert.True(removed);
        Received.InOrder(() =>
        {
            supervisor.StopAsync(10UL, serverId);
            servers.RemoveAsync(10UL, serverId, Arg.Any<CancellationToken>());
            workspace.RemoveServerAsync(10UL, serverId, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task RemoveServer_WhenServerAbsent_ReturnsFalse_StillTearsDown()
    {
        var serverId = Guid.NewGuid();
        var supervisor = Substitute.For<IConnectionSupervisor>();
        var servers = Substitute.For<IServerService>();
        servers.RemoveAsync(10UL, serverId, Arg.Any<CancellationToken>()).Returns(false);
        var workspace = Substitute.For<IServerWorkspaceRemover>();
        var sut = new ServerRemovalService(supervisor, servers, workspace);

        var removed = await sut.RemoveServerAsync(10UL, serverId);

        Assert.False(removed);
        await workspace.Received(1).RemoveServerAsync(10UL, serverId, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~ServerRemovalService"`
Expected: FAIL — `ServerRemovalService`/`IServerRemovalService` do not exist (compile error).

- [ ] **Step 3: Create the contract**

`src/RustPlusBot.Features.Connections/Removal/IServerRemovalService.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Removal;

/// <summary>Orchestrates removing a server: stop the socket, delete the row (cascades), tear down the workspace.</summary>
internal interface IServerRemovalService
{
    /// <summary>Removes a server end-to-end. Returns true if a server row was deleted.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server to remove.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True if a matching server row was removed.</returns>
    Task<bool> RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement it**

`src/RustPlusBot.Features.Connections/Removal/ServerRemovalService.cs`:

```csharp
using RustPlusBot.Features.Connections.Supervisor;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Connections.Removal;

/// <summary>Default <see cref="IServerRemovalService"/>. Order matters: stop the socket before deleting the row,
/// so no late status write re-inserts a connection-state row against a deleted server (FK violation).</summary>
/// <param name="supervisor">Stops the live socket.</param>
/// <param name="servers">Deletes the RustServer (cascades credentials + connection state).</param>
/// <param name="workspace">Tears down the server's Discord category/channels/records.</param>
internal sealed class ServerRemovalService(
    IConnectionSupervisor supervisor,
    IServerService servers,
    IServerWorkspaceRemover workspace) : IServerRemovalService
{
    /// <inheritdoc />
    public async Task<bool> RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        await supervisor.StopAsync(guildId, serverId).ConfigureAwait(false);
        var removed = await servers.RemoveAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        await workspace.RemoveServerAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        return removed;
    }
}
```

- [ ] **Step 5: Register the service**

In `ConnectionServiceCollectionExtensions.cs`, add `using RustPlusBot.Features.Connections.Removal;` and, after the supervisor registration:

```csharp
        services.AddScoped<IServerRemovalService, ServerRemovalService>();
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~ServerRemovalService"`
Expected: PASS (both).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(connections): server removal orchestrator"
```

---

### Task 8: Connections consumes `ServerCredentialsChangedEvent`

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Hosting/ConnectionHostedService.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionHostedServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `ConnectionHostedServiceTests.cs`:

```csharp
    [Fact]
    public async Task ServerCredentialsChanged_EnsuresAConnection()
    {
        var supervisor = Substitute.For<IConnectionSupervisor>();
        var bus = new InMemoryEventBus();
        var service = new ConnectionHostedService(supervisor, bus, NullLogger<ConnectionHostedService>.Instance);

        await service.StartAsync(default);

        var serverId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !supervisor.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IConnectionSupervisor.EnsureConnectionAsync)))
        {
            await bus.PublishAsync(new ServerCredentialsChangedEvent(10UL, serverId));
            await Task.Delay(20);
        }

        await supervisor.Received().EnsureConnectionAsync(10UL, serverId, Arg.Any<CancellationToken>());

        await service.StopAsync(default);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~ServerCredentialsChanged_EnsuresAConnection"`
Expected: FAIL — the service never reacts to `ServerCredentialsChangedEvent` (deadline elapses, `EnsureConnectionAsync` not received).

- [ ] **Step 3: Add a second consumer loop**

Replace the `RunAsync` method in `ConnectionHostedService.cs` with two consumer loops fanned by `Task.WhenAll`:

```csharp
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        // Subscriptions register when each loop first awaits the bus, after StartAllAsync completes.
        // The in-process bus does not replay, so events published before this point are not delivered. Safe:
        // both events are only raised at runtime, long after startup.
        try
        {
            await supervisor.StartAllAsync(cancellationToken).ConfigureAwait(false);

            await Task.WhenAll(
                    ConsumeRegisteredAsync(cancellationToken),
                    ConsumeCredentialsChangedAsync(cancellationToken))
                .ConfigureAwait(false);
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

    private async Task ConsumeRegisteredAsync(CancellationToken cancellationToken)
    {
        await foreach (var registered in eventBus.SubscribeAsync<ServerRegisteredEvent>(cancellationToken)
                           .ConfigureAwait(false))
        {
            await supervisor.EnsureConnectionAsync(registered.GuildId, registered.ServerId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task ConsumeCredentialsChangedAsync(CancellationToken cancellationToken)
    {
        await foreach (var changed in eventBus.SubscribeAsync<ServerCredentialsChangedEvent>(cancellationToken)
                           .ConfigureAwait(false))
        {
            await supervisor.EnsureConnectionAsync(changed.GuildId, changed.ServerId, cancellationToken)
                .ConfigureAwait(false);
        }
    }
```

- [ ] **Step 4: Run the hosted-service tests**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~ConnectionHostedService"`
Expected: PASS (the existing `Startup_CallsStartAll_And_ServerRegistered_EnsuresAConnection` and the new test).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(connections): failover on ServerCredentialsChangedEvent"
```

---

### Task 9: Workspace consumes `ServerCredentialsChangedEvent`

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/Hosting/WorkspaceHostedService.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Hosting/WorkspaceConnectionStatusTests.cs`

- [ ] **Step 1: Write the failing test**

Append to `WorkspaceConnectionStatusTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task ServerCredentialsChanged_ReconcilesThatServer()
    {
        var reconciler = Substitute.For<IWorkspaceReconciler>();
        var services = new ServiceCollection();
        services.AddScoped(_ => reconciler);
        await using var provider = services.BuildServiceProvider();

        var bus = new InMemoryEventBus();
        var client = new DiscordSocketClient();
        var service = new WorkspaceHostedService(client, bus,
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkspaceHostedService>.Instance);

        await service.StartAsync(default);
        var serverId = Guid.NewGuid();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !reconciler.ReceivedCalls().Any(c =>
                   c.GetMethodInfo().Name == nameof(IWorkspaceReconciler.ReconcileServerAsync)))
        {
            await bus.PublishAsync(new ServerCredentialsChangedEvent(10UL, serverId));
            await Task.Delay(20);
        }

        await reconciler.Received().ReconcileServerAsync(10UL, serverId, Arg.Any<CancellationToken>());

        await service.StopAsync(default);
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj --filter "FullyQualifiedName~ServerCredentialsChanged_ReconcilesThatServer"`
Expected: FAIL — the service never reconciles on `ServerCredentialsChangedEvent`.

- [ ] **Step 3: Add the third consumer loop**

In `WorkspaceHostedService.cs`:

a) Add a field next to the other loop fields:

```csharp
    private Task? _serverCredentialsLoop;
```

b) In `StartAsync`, after `_connectionStatusLoop = ...`, add:

```csharp
        _serverCredentialsLoop = Task.Run(() => ConsumeServerCredentialsAsync(_cts.Token), CancellationToken.None);
```

c) In `StopAsync`, add `_serverCredentialsLoop` to the joined-loops array:

```csharp
        foreach (var loop in new[]
                 {
                     _serverRegisteredLoop, _connectionStatusLoop, _serverCredentialsLoop
                 })
```

d) Add the consumer method (mirrors `ConsumeConnectionStatusAsync`):

```csharp
    private async Task ConsumeServerCredentialsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var changed in eventBus.SubscribeAsync<ServerCredentialsChangedEvent>(cancellationToken)
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
            logger.LogError(ex, "ServerCredentialsChanged consumer faulted.");
        }
    }
```

- [ ] **Step 4: Run the workspace hosting tests**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj --filter "FullyQualifiedName~WorkspaceConnectionStatusTests"`
Expected: PASS (existing ConnectionStatusChanged test + the new one).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests
git commit -m "feat(workspace): refresh #info on ServerCredentialsChangedEvent"
```

---

### Task 10: Component ids, localization keys, and the two rendered buttons

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceComponentIds.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Messages/SetupMessageRenderer.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`

- [ ] **Step 1: Write the failing renderer tests**

Append to `RendererTests.cs` (inside the class):

```csharp
    [Fact]
    public async Task Setup_HasDisconnectAccountButton()
    {
        var renderer = new SetupMessageRenderer(Loc);

        var payload = await renderer.RenderAsync(Global, default);

        var buttons = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>();
        Assert.Contains(buttons, b => b.CustomId == "workspace:setup:disconnect");
    }

    [Fact]
    public async Task ServerInfo_HasRemoveServerButton()
    {
        var serverId = Guid.NewGuid();
        var servers = Substitute.For<IServerService>();
        servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "S", Ip = "1.2.3.4", Port = 28015 });
        var connections = Substitute.For<IConnectionStore>();
        connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new DomainConnectionState
            {
                RustServerId = serverId, GuildId = 1, Status = ConnectionStatus.NoCredentials
            });
        connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new List<PlayerCredential>());
        var renderer = new ServerInfoMessageRenderer(servers, connections, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        var buttons = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>();
        Assert.Contains(buttons, b => b.CustomId == $"workspace:info:remove:{serverId}");
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj --filter "FullyQualifiedName~HasDisconnectAccountButton|FullyQualifiedName~HasRemoveServerButton"`
Expected: FAIL — buttons not rendered (and `ServerInfo_HasRemoveServerButton` may throw on `Components!` being null in the NoCredentials case).

- [ ] **Step 3: Add the component ids**

In `WorkspaceComponentIds.cs`, add inside the class:

```csharp
    /// <summary>The #setup "Disconnect account" button; handled by the Pairing feature.</summary>
    public const string DisconnectAccount = "workspace:setup:disconnect";

    /// <summary>Prefix for the #info "remove server" button; the server id is appended. Handled by Connections.</summary>
    public const string ServerInfoRemovePrefix = "workspace:info:remove:";
```

- [ ] **Step 4: Add localization keys (EN + FR)**

In `LocalizationCatalog.cs`, add to the `["en"]` dictionary (e.g. after `["setup.connect.button"]`):

```csharp
                ["setup.disconnect.button"] = "Disconnect account",
                ["server.info.remove.button"] = "Remove server",
```

And to the `["fr"]` dictionary:

```csharp
                ["setup.disconnect.button"] = "Déconnecter le compte",
                ["server.info.remove.button"] = "Supprimer le serveur",
```

- [ ] **Step 5: Render the Disconnect button on #setup**

In `SetupMessageRenderer.cs`, replace the `components` builder so it adds the second (Danger) button:

```csharp
        var components = new ComponentBuilder()
            .WithButton(
                localizer.Get("setup.connect.button", context.Culture),
                WorkspaceComponentIds.ConnectAccount,
                ButtonStyle.Primary)
            .WithButton(
                localizer.Get("setup.disconnect.button", context.Culture),
                WorkspaceComponentIds.DisconnectAccount,
                ButtonStyle.Danger)
            .Build();
```

- [ ] **Step 6: Render the Remove-server button on #info (always present)**

In `ServerInfoMessageRenderer.cs`, replace the components block (from `MessageComponent? components = null;` through the `return`) with a builder that always adds the Danger remove button and conditionally adds the swap select:

```csharp
        var builder = new ComponentBuilder();
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

            builder.WithSelectMenu(select);
        }

        builder.WithButton(
            localizer.Get("server.info.remove.button", context.Culture),
            $"{WorkspaceComponentIds.ServerInfoRemovePrefix}{serverId}",
            ButtonStyle.Danger);

        return new MessagePayload(null, embed.Build(), builder.Build());
```

- [ ] **Step 7: Run the renderer tests**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj --filter "FullyQualifiedName~RendererTests"`
Expected: PASS (new button tests + all existing renderer tests, including the swap-select and connect-button tests).

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests
git commit -m "feat(workspace): render disconnect + remove-server buttons (EN/FR)"
```

---

### Task 11: Pairing disconnect module (button + confirm)

**Files:**

- Modify: `src/RustPlusBot.Features.Pairing/Modules/CredentialModule.cs`

No unit test: interaction modules (`InteractionModuleBase`) are not unit-tested in this repo; the logic lives in `AccountDisconnectService` (Task 5). The module is auto-registered via the existing `InteractionModuleAssembly` seam. Verification is a clean build.

- [ ] **Step 1: Add the disconnect handlers to `CredentialModule`**

Add these `using`s at the top (if missing): `using Discord;`, `using RustPlusBot.Features.Pairing.Accounts;`. Then add inside the class:

```csharp
    private const string DisconnectConfirmId = "pairing:account:disconnect:confirm";
    private const string DisconnectCancelId = "pairing:account:disconnect:cancel";

    /// <summary>Opens an ephemeral confirmation listing the servers a disconnect would affect.</summary>
    [ComponentInteraction(WorkspaceComponentIds.DisconnectAccount)]
    public async Task DisconnectPromptAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var disconnect = scope.ServiceProvider.GetRequiredService<IAccountDisconnectService>();
            var preview = await disconnect.PreviewAsync(Context.Guild.Id, Context.User.Id).ConfigureAwait(false);
            if (!preview.IsConnected)
            {
                await FollowupAsync("You're not connected.", ephemeral: true).ConfigureAwait(false);
                return;
            }

            var servers = preview.AffectedServerNames.Count > 0
                ? string.Join(", ", preview.AffectedServerNames)
                : "no servers yet";
            var components = new ComponentBuilder()
                .WithButton("Disconnect", DisconnectConfirmId, ButtonStyle.Danger)
                .WithButton("Cancel", DisconnectCancelId, ButtonStyle.Secondary)
                .Build();
            await FollowupAsync(
                    $"This disconnects your account and removes your credentials from: {servers}. Continue?",
                    components: components, ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Performs the disconnect after confirmation.</summary>
    [ComponentInteraction(DisconnectConfirmId)]
    public async Task DisconnectConfirmAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var disconnect = scope.ServiceProvider.GetRequiredService<IAccountDisconnectService>();
            var count = await disconnect.DisconnectAsync(Context.Guild.Id, Context.User.Id).ConfigureAwait(false);
            await FollowupAsync($"Account disconnected. Removed from {count} server(s).", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Cancels the disconnect.</summary>
    [ComponentInteraction(DisconnectCancelId)]
    public async Task DisconnectCancelAsync() =>
        await RespondAsync("Cancelled.", ephemeral: true).ConfigureAwait(false);
```

- [ ] **Step 2: Build the Pairing project**

Run: `dotnet build src/RustPlusBot.Features.Pairing/RustPlusBot.Features.Pairing.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Features.Pairing
git commit -m "feat(pairing): #setup disconnect-account button with confirm"
```

---

### Task 12: Connections remove-server module (button + confirm)

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Modules/ServerRemovalModule.cs`

No unit test (interaction module; logic is in `ServerRemovalService`, Task 7). Auto-registered via the existing `InteractionModuleAssembly` seam.

- [ ] **Step 1: Create the module**

`src/RustPlusBot.Features.Connections/Modules/ServerRemovalModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Connections.Removal;
using RustPlusBot.Features.Workspace;

namespace RustPlusBot.Features.Connections.Modules;

/// <summary>Handles the #info "Remove server" button and its confirmation (ManageGuild).</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class ServerRemovalModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string RemoveConfirmPrefix = "connections:server:remove:confirm:";
    private const string RemoveCancelId = "connections:server:remove:cancel";

    /// <summary>Opens an ephemeral confirmation for removing the server.</summary>
    /// <param name="serverIdRaw">The server id captured from the custom id.</param>
    [ComponentInteraction(WorkspaceComponentIds.ServerInfoRemovePrefix + "*")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task RemovePromptAsync(string serverIdRaw)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(serverIdRaw, out var serverId))
        {
            await RespondAsync("That selection wasn't valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var components = new ComponentBuilder()
            .WithButton("Remove server", $"{RemoveConfirmPrefix}{serverId}", ButtonStyle.Danger)
            .WithButton("Cancel", RemoveCancelId, ButtonStyle.Secondary)
            .Build();
        await RespondAsync(
                "This permanently deletes the category, all channels, and stored credentials for this server. Continue?",
                components: components, ephemeral: true)
            .ConfigureAwait(false);
    }

    /// <summary>Removes the server after confirmation.</summary>
    /// <param name="serverIdRaw">The server id captured from the custom id.</param>
    [ComponentInteraction(RemoveConfirmPrefix + "*")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task RemoveConfirmAsync(string serverIdRaw)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(serverIdRaw, out var serverId))
        {
            await RespondAsync("That selection wasn't valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var removal = scope.ServiceProvider.GetRequiredService<IServerRemovalService>();
            var removed = await removal.RemoveServerAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            await FollowupAsync(removed ? "Server removed." : "That server no longer exists.", ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Cancels the removal.</summary>
    [ComponentInteraction(RemoveCancelId)]
    public async Task RemoveCancelAsync() =>
        await RespondAsync("Cancelled.", ephemeral: true).ConfigureAwait(false);
}
```

- [ ] **Step 2: Build the Connections project**

Run: `dotnet build src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Features.Connections
git commit -m "feat(connections): #info remove-server button with confirm"
```

---

### Task 13: Whole-feature verification

**Files:** none (verification only).

- [ ] **Step 1: Clean build (strict analyzers)**

Run: `dotnet build RustPlusBot.slnx`
Expected: Build succeeded, **0 Warning(s), 0 Error(s)**.

- [ ] **Step 2: Full test suite**

Run: `dotnet test RustPlusBot.slnx`
Expected: all tests pass (no failures, no skips beyond pre-existing).

- [ ] **Step 3: No EF model drift**

Run:

```bash
dotnet ef migrations has-pending-model-changes \
  --project src/RustPlusBot.Persistence \
  --startup-project src/RustPlusBot.Persistence
```

Expected: "No changes have been made to the model since the last migration."

- [ ] **Step 4: Formatter gate (ReSharper) — matches CI**

Run:

```bash
dotnet tool restore
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git diff --stat
```

If `jb` reordered anything, review and stage it, then re-run Steps 1–2 to confirm still green.

- [ ] **Step 5: Commit any formatting changes**

```bash
git add -A
git commit -m "style: jb reformat for 1b-iii" --allow-empty
```

---

## Self-review notes (author)

- **Spec coverage:** §4 cascade → Task 1. §5 removal (seam/service/UI) → Tasks 6, 7, 12. §6 disconnect (store/supervisor/service/UI) → Tasks 2, 4, 5, 11. §7 event + wiring → Tasks 3, 8, 9. §8 ids/localization/renderers → Task 10. §9 testing → per-task tests + Task 13.
- **Breaking-change handling:** the new FK breaks three existing tests that persist orphan `ConnectionState` rows; Task 1 Step 1 fixes all three before the FK lands.
- **Type consistency:** `RemoveForOwnerAsync`/`ListServerIdsForOwnerAsync` (Task 2) are consumed by `AccountDisconnectService` (Task 5); `ServerCredentialsChangedEvent` (Task 3) is published in Task 5 and consumed in Tasks 8–9; `IServerWorkspaceRemover` (Task 6) is consumed in Task 7; `IServerRemovalService` (Task 7) is consumed in Task 12; `IAccountDisconnectService` (Task 5) is consumed in Task 11; component ids (Task 10) are referenced by the modules (Tasks 11–12). All names match across tasks.
- **Conscious limitation (per spec §8):** ephemeral confirm/result strings in the modules are English; rendered button labels are localized EN/FR.
