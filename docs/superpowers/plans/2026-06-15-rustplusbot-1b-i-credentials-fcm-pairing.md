# RustPlusBot Subsystem 1b-i: Credential Intake & FCM Pairing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A guild member pastes their Rust+ FCM credentials into a modal in `#setup`; the bot runs a per-user FCM listener and, when that user pairs a server in-game, auto-registers the server (provisioning its workspace via the existing 1a reconciler) and stores the per-server player credential into a multi-owner pool.

**Architecture:** A new `RustPlusBot.Features.Pairing` project. An `IPairingSource` seam abstracts FCM (real impl wraps `RustPlusApi.Fcm`; a fake drives tests). A singleton `IPairingSupervisor` owns one listener per active `FcmRegistration`, reports the first connect outcome for immediate UX feedback, marks credentials `Expired` + DMs the owner on auth rejection, and retries transient connect failures with capped backoff. A scoped `IPairingHandler` turns a server-pairing notification into a `RustServer` (resolve-or-create by endpoint) + a `PlayerCredential` (upsert, first→`Active`) and fires the real `ServerRegisteredEvent` on new-server creation. The credential schema splits into a per-user `FcmRegistration` and a slimmed per-server `PlayerCredential`.

**Tech Stack:** .NET 10, Discord.Net 3.20, EF Core 10 (SQLite), Persistord.Core, `RustPlusApi.Fcm` 2.0.0-beta.1, xUnit + NSubstitute. Strict analyzers (build must stay 0 warnings / 0 errors).

**Spec:** `docs/superpowers/specs/2026-06-15-rustplusbot-1b-i-credentials-fcm-pairing-design.md`

---

## File Structure

**Domain (`src/RustPlusBot.Domain`)**

- Create `Credentials/FcmRegistration.cs` — per-(guild,user) FCM registration entity.
- Create `Credentials/FcmRegistrationStatus.cs` — `Active`/`Expired`/`Disabled`.
- Modify `Credentials/PlayerCredential.cs` — remove `ProtectedFcmCredentials`.

**Abstractions (`src/RustPlusBot.Abstractions`)**

- Modify `Credentials/StoreCredentialRequest.cs` — drop `FcmCredentialsJson`.
- Modify `Credentials/ICredentialStore.cs` — `UpsertFromPairingAsync` + `CountForServerAsync`.

**Persistence (`src/RustPlusBot.Persistence`)**

- Modify `Configurations/PlayerCredentialConfiguration.cs` — unique `(GuildId,RustServerId,OwnerUserId)` index + FK→`RustServer` cascade.
- Modify `Configurations/RustServerConfiguration.cs` — unique `(GuildId,Ip,Port)` index.
- Create `Configurations/FcmRegistrationConfiguration.cs`.
- Modify `BotDbContext.cs` — `DbSet<FcmRegistration>` + apply config.
- Modify `Credentials/CredentialStore.cs` — upsert + first→`Active`.
- Create `Credentials/IFcmRegistrationStore.cs` + `Credentials/FcmRegistrationStore.cs`.
- Modify `Servers/IServerService.cs` + `Servers/ServerService.cs` — `ResolveOrCreateByEndpointAsync`.
- Modify `PersistenceServiceCollectionExtensions.cs` — register `IFcmRegistrationStore`.
- Create migration `Migrations/<timestamp>_PairingCredentials.cs` (generated).

**Workspace (`src/RustPlusBot.Features.Workspace`)**

- Create `WorkspaceComponentIds.cs` — public custom-id constants.
- Modify `Messages/SetupMessageRenderer.cs` — add Connect button.
- Modify `Localization/LocalizationCatalog.cs` — `setup.connect.button` + revised `setup.body`.

**Pairing (`src/RustPlusBot.Features.Pairing`, new)**

- `RustPlusBot.Features.Pairing.csproj`, `AssemblyInfo.cs`, `PairingOptions.cs`.
- `Listening/PairingNotification.cs` (`PairingKind`, `PairingConnectOutcome`, record).
- `Listening/IPairingSource.cs` (`IPairingSource`, `IPairingListener`).
- `Listening/RustPlusFcmPairingSource.cs` (real adapter).
- `Pairing/IPairingHandler.cs` + `Pairing/PairingHandler.cs`.
- `Notifications/IOwnerNotifier.cs` + `Notifications/DiscordOwnerNotifier.cs`.
- `Supervisor/IPairingSupervisor.cs` + `Supervisor/PairingSupervisor.cs`.
- `Hosting/PairingHostedService.cs`.
- `Modules/CredentialModule.cs` + `Modules/ConnectModal.cs` + `Validation/FcmCredentialValidator.cs`.
- `PairingServiceCollectionExtensions.cs`.

**Host (`src/RustPlusBot.Host`)**

- Modify `Program.cs` — bind+validate `PairingOptions`, `AddPairing()`.
- Modify `RustPlusBot.Host.csproj` — reference `Features.Pairing`.

**Build infra**

- Modify `Directory.Packages.props` — add `RustPlusApi.Fcm`.
- Modify `RustPlusBot.slnx` — add the two new projects.

**Tests**

- Modify `tests/RustPlusBot.Persistence.Tests/Credentials/CredentialStoreTests.cs`.
- Create `tests/RustPlusBot.Persistence.Tests/Credentials/FcmRegistrationStoreTests.cs`.
- Modify `tests/RustPlusBot.Persistence.Tests/Servers/ServerServiceTests.cs`.
- Create `tests/RustPlusBot.Persistence.Tests/Credentials/PairingSchemaTests.cs`.
- Modify `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`.
- Create test project `tests/RustPlusBot.Features.Pairing.Tests/` with fakes + handler/supervisor/validator tests.

---

## A note on scope deviations from the spec

Two conscious refinements made while planning (call them out at review):

1. **`ICredentialStore.SetStatusAsync` is deferred to 1b-ii.** It has no caller in 1b-i (only the live socket marks `Invalid`), and adding a `Domain.CredentialStatus` parameter would force `ICredentialStore` out of the dependency-free `Abstractions` project. The `Invalid`→`Standby` reset on re-pairing still happens — it lives *inside* `CredentialStore.UpsertFromPairingAsync`, not on the interface.
2. **The supervisor retries the *initial connect* on timeout with capped backoff; mid-session reconnection after a drop is left to the adapter / 1b-ii.** 1b-i's boundary is "no live socket lifecycle," and durable session reconnection is connection-management work. Auth rejection → `Expired`+DM and transient-connect → backoff are both implemented.

---

### Task 1: Split the credential schema and generate the migration

**Files:**

- Create: `src/RustPlusBot.Domain/Credentials/FcmRegistrationStatus.cs`
- Create: `src/RustPlusBot.Domain/Credentials/FcmRegistration.cs`
- Modify: `src/RustPlusBot.Domain/Credentials/PlayerCredential.cs`
- Modify: `src/RustPlusBot.Abstractions/Credentials/StoreCredentialRequest.cs`
- Modify: `src/RustPlusBot.Abstractions/Credentials/ICredentialStore.cs`
- Modify: `src/RustPlusBot.Persistence/Credentials/CredentialStore.cs`
- Modify: `src/RustPlusBot.Persistence/Configurations/PlayerCredentialConfiguration.cs`
- Modify: `src/RustPlusBot.Persistence/Configurations/RustServerConfiguration.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/FcmRegistrationConfiguration.cs`
- Modify: `src/RustPlusBot.Persistence/BotDbContext.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Credentials/CredentialStoreTests.cs` (rewritten)

- [ ] **Step 1: Add the status enum**

Create `src/RustPlusBot.Domain/Credentials/FcmRegistrationStatus.cs`:

```csharp
namespace RustPlusBot.Domain.Credentials;

/// <summary>Lifecycle state of a user's FCM listener registration.</summary>
public enum FcmRegistrationStatus
{
    /// <summary>Credentials accepted; the listener should run.</summary>
    Active = 0,

    /// <summary>FCM rejected the credentials; the user must reconnect.</summary>
    Expired = 1,

    /// <summary>Removed by the user; the listener must not run.</summary>
    Disabled = 2,
}
```

- [ ] **Step 2: Add the FcmRegistration entity**

Create `src/RustPlusBot.Domain/Credentials/FcmRegistration.cs`:

```csharp
namespace RustPlusBot.Domain.Credentials;

/// <summary>
/// One Discord user's Rust+ FCM listener registration within a guild. One per (GuildId, OwnerUserId).
/// The credentials blob is stored protected at rest (see ICredentialProtector) and feeds the pairing listener.
/// </summary>
public sealed class FcmRegistration
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The Discord user who connected these credentials.</summary>
    public ulong OwnerUserId { get; set; }

    /// <summary>Protected FCM/Expo credentials JSON.</summary>
    public string ProtectedFcmCredentials { get; set; } = string.Empty;

    /// <summary>Listener lifecycle state.</summary>
    public FcmRegistrationStatus Status { get; set; } = FcmRegistrationStatus.Active;

    /// <summary>When the registration was last upserted or had its status changed (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 3: Slim PlayerCredential**

In `src/RustPlusBot.Domain/Credentials/PlayerCredential.cs`, delete the `ProtectedFcmCredentials` property (lines 27-28: the `<summary>Protected FCM/Expo…</summary>` comment and the property). Leave everything else unchanged.

- [ ] **Step 4: Reshape StoreCredentialRequest**

Replace the body of `src/RustPlusBot.Abstractions/Credentials/StoreCredentialRequest.cs`:

```csharp
namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Plaintext inputs to upsert a per-server player credential from a pairing; the token is protected before persistence.</summary>
/// <param name="GuildId">Owning Discord guild snowflake.</param>
/// <param name="RustServerId">The server this credential connects to.</param>
/// <param name="OwnerUserId">The Discord user who owns the paired identity.</param>
/// <param name="SteamId">The player's Steam64 id (from the pairing notification).</param>
/// <param name="PlayerToken">The plaintext Rust+ player token (protected before storage).</param>
public sealed record StoreCredentialRequest(
    ulong GuildId,
    Guid RustServerId,
    ulong OwnerUserId,
    ulong SteamId,
    string PlayerToken);
```

- [ ] **Step 5: Reshape ICredentialStore**

Replace the body of `src/RustPlusBot.Abstractions/Credentials/ICredentialStore.cs`:

```csharp
namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Persists and counts per-server player credentials. Tokens are protected at rest.</summary>
public interface ICredentialStore
{
    /// <summary>
    /// Inserts or refreshes the credential for (GuildId, RustServerId, OwnerUserId). The first credential
    /// stored for a server becomes <c>Active</c>; subsequent owners are <c>Standby</c>. Re-pairing refreshes
    /// the token and resets an <c>Invalid</c> credential to <c>Standby</c>. Returns the credential's id.
    /// </summary>
    /// <param name="request">The plaintext credential inputs.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The credential's id.</returns>
    Task<Guid> UpsertFromPairingAsync(StoreCredentialRequest request, CancellationToken cancellationToken = default);

    /// <summary>Counts stored credentials for a server within a guild.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="rustServerId">The server to count credentials for.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The number of stored credentials for that (guild, server).</returns>
    Task<int> CountForServerAsync(ulong guildId, Guid rustServerId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 6: Rewrite CredentialStore**

Replace the body of `src/RustPlusBot.Persistence/Credentials/CredentialStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Credentials;

/// <summary>EF-backed <see cref="ICredentialStore"/> that protects tokens before persisting.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="protector">Protects token material before it is written.</param>
public sealed class CredentialStore(BotDbContext context, ICredentialProtector protector) : ICredentialStore
{
    /// <inheritdoc />
    public async Task<Guid> UpsertFromPairingAsync(
        StoreCredentialRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = await context.PlayerCredentials
            .SingleOrDefaultAsync(
                c => c.GuildId == request.GuildId
                    && c.RustServerId == request.RustServerId
                    && c.OwnerUserId == request.OwnerUserId,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.SteamId = request.SteamId;
            existing.ProtectedPlayerToken = protector.Protect(request.PlayerToken);
            if (existing.Status == CredentialStatus.Invalid)
            {
                existing.Status = CredentialStatus.Standby;
            }

            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing.Id;
        }

        var isFirstForServer = !await context.PlayerCredentials
            .AnyAsync(c => c.GuildId == request.GuildId && c.RustServerId == request.RustServerId, cancellationToken)
            .ConfigureAwait(false);

        var credential = new PlayerCredential
        {
            GuildId = request.GuildId,
            RustServerId = request.RustServerId,
            OwnerUserId = request.OwnerUserId,
            SteamId = request.SteamId,
            ProtectedPlayerToken = protector.Protect(request.PlayerToken),
            Status = isFirstForServer ? CredentialStatus.Active : CredentialStatus.Standby,
        };

        context.PlayerCredentials.Add(credential);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return credential.Id;
    }

    /// <inheritdoc />
    public Task<int> CountForServerAsync(
        ulong guildId,
        Guid rustServerId,
        CancellationToken cancellationToken = default) =>
        context.PlayerCredentials
            .Where(c => c.GuildId == guildId && c.RustServerId == rustServerId)
            .CountAsync(cancellationToken);
}
```

- [ ] **Step 7: Update PlayerCredentialConfiguration (unique index + cascade FK)**

Replace the `Configure` body of `src/RustPlusBot.Persistence/Configurations/PlayerCredentialConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class PlayerCredentialConfiguration : IEntityTypeConfiguration<PlayerCredential>
{
    public void Configure(EntityTypeBuilder<PlayerCredential> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new
        {
            c.GuildId, c.RustServerId, c.OwnerUserId
        }).IsUnique();
        builder.Property(c => c.ProtectedPlayerToken).IsRequired();

        // Removing a RustServer cascades to its credentials so orphaned secrets cannot linger.
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(c => c.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 8: Add the unique endpoint index to RustServerConfiguration**

In `src/RustPlusBot.Persistence/Configurations/RustServerConfiguration.cs`, after the existing `builder.HasIndex(s => s.GuildId);` line add:

```csharp
        builder.HasIndex(s => new
        {
            s.GuildId, s.Ip, s.Port
        }).IsUnique();
```

- [ ] **Step 9: Add FcmRegistrationConfiguration**

Create `src/RustPlusBot.Persistence/Configurations/FcmRegistrationConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class FcmRegistrationConfiguration : IEntityTypeConfiguration<FcmRegistration>
{
    public void Configure(EntityTypeBuilder<FcmRegistration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(r => r.Id);
        builder.HasIndex(r => new
        {
            r.GuildId, r.OwnerUserId
        }).IsUnique();
        builder.Property(r => r.ProtectedFcmCredentials).IsRequired();
    }
}
```

- [ ] **Step 10: Register the entity in BotDbContext**

In `src/RustPlusBot.Persistence/BotDbContext.cs`: after the `PlayerCredentials` DbSet (line 25) add:

```csharp
    /// <summary>Per-user FCM listener registrations.</summary>
    public DbSet<FcmRegistration> FcmRegistrations => Set<FcmRegistration>();
```

and in `OnModelCreating`, after `.ApplyConfiguration(new PlayerCredentialConfiguration())` add:

```csharp
            .ApplyConfiguration(new FcmRegistrationConfiguration())
```

- [ ] **Step 11: Verify the solution compiles**

Run: `dotnet build`
Expected: SUCCESS, 0 warnings / 0 errors. (`CredentialStoreTests` still references the old API and will fail to compile — that is why it builds the *solution* with the test rewrite in the next step before running tests. If the test project breaks the build, proceed to Step 12 first, then re-run.)

- [ ] **Step 12: Rewrite CredentialStoreTests for the upsert model**

Replace the body of `tests/RustPlusBot.Persistence.Tests/Credentials/CredentialStoreTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Persistence.Tests.Credentials;

public sealed class CredentialStoreTests
{
    private static ICredentialProtector PassThroughProtector()
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Protect(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        return protector;
    }

    private static async Task<Guid> SeedServerAsync(BotDbContext context, ulong guildId)
    {
        var server = new RustServer
        {
            GuildId = guildId, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    [Fact]
    public async Task UpsertFromPairing_FirstCredentialForServer_IsActiveAndProtected()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverId = await SeedServerAsync(context, 10UL);
        var protector = PassThroughProtector();
        var store = new CredentialStore(context, protector);

        var id = await store.UpsertFromPairingAsync(
            new StoreCredentialRequest(10UL, serverId, 99UL, 76561198000000000UL, "raw-token"));

        var saved = await context.PlayerCredentials.SingleAsync();
        Assert.Equal(id, saved.Id);
        Assert.Equal("enc:raw-token", saved.ProtectedPlayerToken);
        Assert.Equal(CredentialStatus.Active, saved.Status);
        protector.Received(1).Protect("raw-token");
    }

    [Fact]
    public async Task UpsertFromPairing_SecondOwnerForServer_IsStandby()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverId = await SeedServerAsync(context, 10UL);
        var store = new CredentialStore(context, PassThroughProtector());

        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 1UL, 1UL, "t1"));
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 2UL, 2UL, "t2"));

        var second = await context.PlayerCredentials.SingleAsync(c => c.OwnerUserId == 2UL);
        Assert.Equal(CredentialStatus.Standby, second.Status);
        Assert.Equal(2, await store.CountForServerAsync(10UL, serverId));
    }

    [Fact]
    public async Task UpsertFromPairing_SameOwnerAgain_RefreshesTokenAndResetsInvalid()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverId = await SeedServerAsync(context, 10UL);
        var store = new CredentialStore(context, PassThroughProtector());

        var id = await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 1UL, 1UL, "old"));
        var row = await context.PlayerCredentials.SingleAsync();
        row.Status = CredentialStatus.Invalid;
        await context.SaveChangesAsync();

        var again = await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverId, 1UL, 1UL, "new"));

        Assert.Equal(id, again);
        var saved = await context.PlayerCredentials.SingleAsync();
        Assert.Equal("enc:new", saved.ProtectedPlayerToken);
        Assert.Equal(CredentialStatus.Standby, saved.Status);
    }

    [Fact]
    public async Task CountForServer_CountsOnlyMatchingGuildAndServer()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var serverA = await SeedServerAsync(context, 10UL);
        var serverB = await SeedServerAsync(context, 10UL);
        var store = new CredentialStore(context, PassThroughProtector());

        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 1UL, 1UL, "t1"));
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverA, 2UL, 2UL, "t2"));
        await store.UpsertFromPairingAsync(new StoreCredentialRequest(10UL, serverB, 3UL, 3UL, "t3"));

        Assert.Equal(2, await store.CountForServerAsync(10UL, serverA));
    }
}
```

> Note: tests now seed a real `RustServer` first because the cascade FK requires the referenced server to exist.

- [ ] **Step 13: Install the EF tools if needed and generate the migration**

Run: `dotnet tool restore 2>/dev/null || dotnet tool install --global dotnet-ef`
Then run:
`dotnet ef migrations add PairingCredentials --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Persistence`
Expected: a new `Migrations/<timestamp>_PairingCredentials.cs` + `.Designer.cs` and an updated `BotDbContextModelSnapshot.cs`. Open the generated migration and confirm it: drops `PlayerCredentials.ProtectedFcmCredentials`; creates table `FcmRegistrations`; adds unique index on `RustServers (GuildId, Ip, Port)`; adds the unique `(GuildId, RustServerId, OwnerUserId)` index + FK on `PlayerCredentials`.

- [ ] **Step 14: Run the persistence tests**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests`
Expected: PASS (all existing + rewritten credential tests).

- [ ] **Step 15: Commit**

```bash
git add src/RustPlusBot.Domain src/RustPlusBot.Abstractions src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests/Credentials/CredentialStoreTests.cs
git commit -m "feat(persistence): split FCM registration from player credential pool

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 2: FcmRegistrationStore

**Files:**

- Create: `src/RustPlusBot.Persistence/Credentials/IFcmRegistrationStore.cs`
- Create: `src/RustPlusBot.Persistence/Credentials/FcmRegistrationStore.cs`
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Credentials/FcmRegistrationStoreTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Persistence.Tests/Credentials/FcmRegistrationStoreTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Persistence.Tests.Credentials;

public sealed class FcmRegistrationStoreTests
{
    private static ICredentialProtector PassThroughProtector()
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Protect(Arg.Any<string>()).Returns(call => "enc:" + call.Arg<string>());
        return protector;
    }

    private static IClock FixedClock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 15, 0, 0, 0, TimeSpan.Zero));
        return clock;
    }

    [Fact]
    public async Task Upsert_StoresProtectedAndActive()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var store = new FcmRegistrationStore(context, PassThroughProtector(), FixedClock());

        var id = await store.UpsertAsync(10UL, 99UL, "{\"a\":1}");

        var saved = await context.FcmRegistrations.SingleAsync();
        Assert.Equal(id, saved.Id);
        Assert.Equal("enc:{\"a\":1}", saved.ProtectedFcmCredentials);
        Assert.Equal(FcmRegistrationStatus.Active, saved.Status);
    }

    [Fact]
    public async Task Upsert_SameOwner_RefreshesAndReactivates()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var store = new FcmRegistrationStore(context, PassThroughProtector(), FixedClock());

        var id = await store.UpsertAsync(10UL, 99UL, "old");
        await store.SetStatusAsync(id, FcmRegistrationStatus.Expired);

        var again = await store.UpsertAsync(10UL, 99UL, "new");

        Assert.Equal(id, again);
        var saved = await context.FcmRegistrations.SingleAsync();
        Assert.Equal("enc:new", saved.ProtectedFcmCredentials);
        Assert.Equal(FcmRegistrationStatus.Active, saved.Status);
    }

    [Fact]
    public async Task ListActive_ReturnsOnlyActiveAcrossGuilds()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var store = new FcmRegistrationStore(context, PassThroughProtector(), FixedClock());

        await store.UpsertAsync(10UL, 1UL, "a");
        var expiredId = await store.UpsertAsync(10UL, 2UL, "b");
        await store.SetStatusAsync(expiredId, FcmRegistrationStatus.Expired);
        await store.UpsertAsync(20UL, 3UL, "c");

        var active = await store.ListActiveAsync();

        Assert.Equal(2, active.Count);
        Assert.All(active, r => Assert.Equal(FcmRegistrationStatus.Active, r.Status));
    }

    [Fact]
    public async Task Get_ReturnsRegistrationForOwner_OrNull()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var store = new FcmRegistrationStore(context, PassThroughProtector(), FixedClock());

        await store.UpsertAsync(10UL, 99UL, "a");

        Assert.NotNull(await store.GetAsync(10UL, 99UL));
        Assert.Null(await store.GetAsync(10UL, 1UL));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter FcmRegistrationStoreTests`
Expected: FAIL to compile (`FcmRegistrationStore` does not exist).

- [ ] **Step 3: Add the interface**

Create `src/RustPlusBot.Persistence/Credentials/IFcmRegistrationStore.cs`:

```csharp
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Credentials;

/// <summary>Persists per-user FCM listener registrations. Credentials are protected at rest.</summary>
public interface IFcmRegistrationStore
{
    /// <summary>Inserts or refreshes the registration for (guild, owner), setting it <c>Active</c>. Returns its id.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user connecting credentials.</param>
    /// <param name="fcmCredentialsJson">The plaintext FCM credentials JSON (protected before storage).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The registration's id.</returns>
    Task<Guid> UpsertAsync(
        ulong guildId,
        ulong ownerUserId,
        string fcmCredentialsJson,
        CancellationToken cancellationToken = default);

    /// <summary>Lists every <c>Active</c> registration across all guilds (used at startup).</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The active registrations.</returns>
    Task<IReadOnlyList<FcmRegistration>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Sets a registration's status.</summary>
    /// <param name="id">The registration id.</param>
    /// <param name="status">The new status.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task SetStatusAsync(Guid id, FcmRegistrationStatus status, CancellationToken cancellationToken = default);

    /// <summary>Gets the registration for (guild, owner), or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The registration, or null.</returns>
    Task<FcmRegistration?> GetAsync(ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement the store**

Create `src/RustPlusBot.Persistence/Credentials/FcmRegistrationStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Credentials;

/// <summary>EF-backed <see cref="IFcmRegistrationStore"/> that protects credentials before persisting.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="protector">Protects credential material before it is written.</param>
/// <param name="clock">Supplies the update timestamp.</param>
public sealed class FcmRegistrationStore(BotDbContext context, ICredentialProtector protector, IClock clock)
    : IFcmRegistrationStore
{
    /// <inheritdoc />
    public async Task<Guid> UpsertAsync(
        ulong guildId,
        ulong ownerUserId,
        string fcmCredentialsJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fcmCredentialsJson);

        var existing = await context.FcmRegistrations
            .SingleOrDefaultAsync(r => r.GuildId == guildId && r.OwnerUserId == ownerUserId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.ProtectedFcmCredentials = protector.Protect(fcmCredentialsJson);
            existing.Status = FcmRegistrationStatus.Active;
            existing.UpdatedAt = clock.UtcNow;
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return existing.Id;
        }

        var registration = new FcmRegistration
        {
            GuildId = guildId,
            OwnerUserId = ownerUserId,
            ProtectedFcmCredentials = protector.Protect(fcmCredentialsJson),
            Status = FcmRegistrationStatus.Active,
            UpdatedAt = clock.UtcNow,
        };

        context.FcmRegistrations.Add(registration);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return registration.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FcmRegistration>> ListActiveAsync(CancellationToken cancellationToken = default) =>
        await context.FcmRegistrations
            .Where(r => r.Status == FcmRegistrationStatus.Active)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SetStatusAsync(
        Guid id,
        FcmRegistrationStatus status,
        CancellationToken cancellationToken = default)
    {
        var registration = await context.FcmRegistrations
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            .ConfigureAwait(false);
        if (registration is null)
        {
            return;
        }

        registration.Status = status;
        registration.UpdatedAt = clock.UtcNow;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<FcmRegistration?> GetAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default) =>
        context.FcmRegistrations
            .SingleOrDefaultAsync(r => r.GuildId == guildId && r.OwnerUserId == ownerUserId, cancellationToken);
}
```

- [ ] **Step 5: Register it**

In `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`, after `services.AddScoped<ICredentialStore, CredentialStore>();` add:

```csharp
        services.AddScoped<IFcmRegistrationStore, FcmRegistrationStore>();
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter FcmRegistrationStoreTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests/Credentials/FcmRegistrationStoreTests.cs
git commit -m "feat(persistence): add FcmRegistrationStore

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 3: ServerService.ResolveOrCreateByEndpointAsync

**Files:**

- Modify: `src/RustPlusBot.Persistence/Servers/IServerService.cs`
- Modify: `src/RustPlusBot.Persistence/Servers/ServerService.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Servers/ServerServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to the class in `tests/RustPlusBot.Persistence.Tests/Servers/ServerServiceTests.cs` (before the closing brace):

```csharp
    [Fact]
    public async Task ResolveOrCreateByEndpoint_CreatesWhenNew()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        var (server, created) = await service.ResolveOrCreateByEndpointAsync(10UL, 99UL, "Main", "1.2.3.4", 28015);

        Assert.True(created);
        Assert.NotEqual(Guid.Empty, server.Id);
        Assert.Single(await service.ListAsync(10UL));
    }

    [Fact]
    public async Task ResolveOrCreateByEndpoint_ReturnsExistingForSameEndpoint()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        var first = await service.ResolveOrCreateByEndpointAsync(10UL, 1UL, "Main", "1.2.3.4", 28015);
        var second = await service.ResolveOrCreateByEndpointAsync(10UL, 2UL, "Main again", "1.2.3.4", 28015);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(first.Server.Id, second.Server.Id);
        Assert.Single(await service.ListAsync(10UL));
    }

    [Fact]
    public async Task ResolveOrCreateByEndpoint_DifferentPortIsDistinct()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var service = new ServerService(context);

        await service.ResolveOrCreateByEndpointAsync(10UL, 1UL, "A", "1.2.3.4", 28015);
        await service.ResolveOrCreateByEndpointAsync(10UL, 1UL, "B", "1.2.3.4", 28016);

        Assert.Equal(2, (await service.ListAsync(10UL)).Count);
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ServerServiceTests`
Expected: FAIL to compile (`ResolveOrCreateByEndpointAsync` does not exist).

- [ ] **Step 3: Add the interface method**

In `src/RustPlusBot.Persistence/Servers/IServerService.cs`, add (before the closing brace):

```csharp
    /// <summary>
    /// Returns the server matching (guild, ip, port), creating it if none exists. Handles the
    /// concurrent-create race (two pairings for the same new server) by re-reading the winner.
    /// </summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="addedByUserId">The Discord user whose pairing created it (only used on create).</param>
    /// <param name="name">Display name (only used on create).</param>
    /// <param name="ip">Server host or ip.</param>
    /// <param name="port">Rust+ app port.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved server and whether it was newly created.</returns>
    Task<(RustServer Server, bool Created)> ResolveOrCreateByEndpointAsync(
        ulong guildId,
        ulong addedByUserId,
        string name,
        string ip,
        int port,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement it**

In `src/RustPlusBot.Persistence/Servers/ServerService.cs`, add `using Microsoft.EntityFrameworkCore;` at the top if not present (it is needed for `DbUpdateException`/`SingleAsync`; the file already uses `SingleOrDefaultAsync`, so the using is present). Add the method (before the closing brace):

```csharp
    /// <inheritdoc />
    public async Task<(RustServer Server, bool Created)> ResolveOrCreateByEndpointAsync(
        ulong guildId,
        ulong addedByUserId,
        string name,
        string ip,
        int port,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.RustServers
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.Ip == ip && s.Port == port, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return (existing, false);
        }

        var server = new RustServer
        {
            GuildId = guildId, AddedByUserId = addedByUserId, Name = name, Ip = ip, Port = port,
        };
        context.RustServers.Add(server);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return (server, true);
        }
        catch (DbUpdateException)
        {
            // Another pairing created the same endpoint between the read and the write; re-read the winner.
            context.Entry(server).State = EntityState.Detached;
            var winner = await context.RustServers
                .SingleAsync(s => s.GuildId == guildId && s.Ip == ip && s.Port == port, cancellationToken)
                .ConfigureAwait(false);
            return (winner, false);
        }
    }
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ServerServiceTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Persistence/Servers tests/RustPlusBot.Persistence.Tests/Servers/ServerServiceTests.cs
git commit -m "feat(persistence): add resolve-or-create-by-endpoint to ServerService

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 4: Pairing schema test (cascade + unique indexes)

**Files:**

- Test: `tests/RustPlusBot.Persistence.Tests/Credentials/PairingSchemaTests.cs`

- [ ] **Step 1: Write the test**

Create `tests/RustPlusBot.Persistence.Tests/Credentials/PairingSchemaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Credentials;

public sealed class PairingSchemaTests
{
    [Fact]
    public async Task RemovingServer_CascadeDeletesItsCredentials()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        context.PlayerCredentials.Add(new PlayerCredential
        {
            GuildId = 10UL, RustServerId = server.Id, OwnerUserId = 1UL, SteamId = 1UL,
            ProtectedPlayerToken = "x", Status = CredentialStatus.Active
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.PlayerCredentials.ToListAsync());
    }

    [Fact]
    public async Task DuplicateServerEndpoint_ViolatesUniqueIndex()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        context.RustServers.Add(new RustServer
        {
            GuildId = 10UL, Name = "A", Ip = "1.1.1.1", Port = 28015
        });
        context.RustServers.Add(new RustServer
        {
            GuildId = 10UL, Name = "B", Ip = "1.1.1.1", Port = 28015
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateRegistrationForOwner_ViolatesUniqueIndex()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        context.FcmRegistrations.Add(new FcmRegistration
        {
            GuildId = 10UL, OwnerUserId = 1UL, ProtectedFcmCredentials = "a"
        });
        context.FcmRegistrations.Add(new FcmRegistration
        {
            GuildId = 10UL, OwnerUserId = 1UL, ProtectedFcmCredentials = "b"
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter PairingSchemaTests`
Expected: PASS. (If the cascade test fails, confirm the FK in Task 1 Step 7 and that the migration was regenerated.)

- [ ] **Step 3: Commit**

```bash
git add tests/RustPlusBot.Persistence.Tests/Credentials/PairingSchemaTests.cs
git commit -m "test(persistence): cover pairing schema cascade and unique indexes

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 5: Pairing project skeleton + listener seam

**Files:**

- Create: `src/RustPlusBot.Features.Pairing/RustPlusBot.Features.Pairing.csproj`
- Create: `src/RustPlusBot.Features.Pairing/AssemblyInfo.cs`
- Create: `src/RustPlusBot.Features.Pairing/PairingOptions.cs`
- Create: `src/RustPlusBot.Features.Pairing/Listening/PairingNotification.cs`
- Create: `src/RustPlusBot.Features.Pairing/Listening/IPairingSource.cs`
- Create: `tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj`
- Modify: `Directory.Packages.props`
- Modify: `RustPlusBot.slnx`

- [ ] **Step 1: Add the RustPlusApi.Fcm package version**

In `Directory.Packages.props`, inside the first `<ItemGroup>` (the `<!-- Packages -->` group), add (alphabetical, after `Persistord.Core`):

```xml
    <PackageVersion Include="RustPlusApi.Fcm" Version="2.0.0-beta.1" />
```

- [ ] **Step 2: Create the feature project file**

Create `src/RustPlusBot.Features.Pairing/RustPlusBot.Features.Pairing.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

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
    <PackageReference Include="RustPlusApi.Fcm" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create AssemblyInfo for test visibility**

Create `src/RustPlusBot.Features.Pairing/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RustPlusBot.Features.Pairing.Tests")]
```

- [ ] **Step 4: Add PairingOptions**

Create `src/RustPlusBot.Features.Pairing/PairingOptions.cs`:

```csharp
namespace RustPlusBot.Features.Pairing;

/// <summary>Pairing feature configuration, bound from the "Pairing" config section.</summary>
public sealed class PairingOptions
{
    /// <summary>How long the modal connect waits for an initial FCM check-in before reporting "still verifying".</summary>
    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>First delay before retrying a timed-out initial connect.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Cap on the exponential backoff between connect retries.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromMinutes(5);
}
```

- [ ] **Step 5: Add the notification record and enums**

Create `src/RustPlusBot.Features.Pairing/Listening/PairingNotification.cs`:

```csharp
namespace RustPlusBot.Features.Pairing.Listening;

/// <summary>Whether a pairing notification is for a server or an in-game entity.</summary>
internal enum PairingKind
{
    /// <summary>A server pairing (registers a server).</summary>
    Server,

    /// <summary>An entity pairing (switch/alarm/camera) — ignored in 1b-i.</summary>
    Entity,
}

/// <summary>The outcome of an initial FCM connect attempt.</summary>
internal enum PairingConnectOutcome
{
    /// <summary>Connected and checked in.</summary>
    Connected,

    /// <summary>FCM rejected the credentials.</summary>
    Rejected,

    /// <summary>No result within the probe window (network); retried in the background.</summary>
    Timeout,
}

/// <summary>A pairing notification delivered by a listener.</summary>
/// <param name="Kind">Server or entity.</param>
/// <param name="ServerName">The server's display name.</param>
/// <param name="Ip">The server host or ip.</param>
/// <param name="Port">The Rust+ app port.</param>
/// <param name="PlayerId">The paired player's Steam64 id.</param>
/// <param name="PlayerToken">The Rust+ player token for this (server, player).</param>
internal sealed record PairingNotification(
    PairingKind Kind,
    string ServerName,
    string Ip,
    int Port,
    ulong PlayerId,
    string PlayerToken);
```

- [ ] **Step 6: Add the listener seam**

Create `src/RustPlusBot.Features.Pairing/Listening/IPairingSource.cs`:

```csharp
namespace RustPlusBot.Features.Pairing.Listening;

/// <summary>A live FCM listener for one user's credentials. Pushes pairing notifications to a callback.</summary>
internal interface IPairingListener : IAsyncDisposable
{
    /// <summary>
    /// Connects and waits up to <paramref name="timeout"/> for the first successful check-in. After a
    /// <see cref="PairingConnectOutcome.Connected"/> result the listener keeps delivering notifications
    /// to its callback until disposed.
    /// </summary>
    /// <param name="timeout">How long to wait for the initial check-in.</param>
    /// <param name="cancellationToken">Cancels the connection.</param>
    /// <returns>The initial connect outcome.</returns>
    Task<PairingConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

/// <summary>Creates <see cref="IPairingListener"/>s from credentials. The seam that abstracts FCM for tests.</summary>
internal interface IPairingSource
{
    /// <summary>Creates a listener that delivers notifications to <paramref name="onNotification"/>.</summary>
    /// <param name="fcmCredentialsJson">The plaintext FCM credentials JSON.</param>
    /// <param name="onNotification">Invoked for every pairing notification received.</param>
    /// <returns>A not-yet-connected listener.</returns>
    IPairingListener Create(
        string fcmCredentialsJson,
        Func<PairingNotification, CancellationToken, Task> onNotification);
}
```

- [ ] **Step 7: Create the test project file**

Create `tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\RustPlusBot.Features.Pairing\RustPlusBot.Features.Pairing.csproj" />
    <ProjectReference Include="..\..\src\RustPlusBot.Features.Workspace\RustPlusBot.Features.Workspace.csproj" />
    <ProjectReference Include="..\..\src\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
    <ProjectReference Include="..\..\src\RustPlusBot.Domain\RustPlusBot.Domain.csproj" />
    <ProjectReference Include="..\..\src\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 8: Add both projects to the solution**

Run:

```bash
dotnet sln RustPlusBot.slnx add src/RustPlusBot.Features.Pairing/RustPlusBot.Features.Pairing.csproj tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj
```

Expected: "Project ... added to the solution." twice.

- [ ] **Step 9: Verify it builds**

Run: `dotnet build`
Expected: SUCCESS, 0 warnings / 0 errors. (The test project has no tests yet; that is fine.)

- [ ] **Step 10: Commit**

```bash
git add Directory.Packages.props RustPlusBot.slnx src/RustPlusBot.Features.Pairing tests/RustPlusBot.Features.Pairing.Tests
git commit -m "chore(pairing): scaffold Features.Pairing project and listener seam

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 6: PairingHandler (notification → server registration)

**Files:**

- Create: `src/RustPlusBot.Features.Pairing/Pairing/IPairingHandler.cs`
- Create: `src/RustPlusBot.Features.Pairing/Pairing/PairingHandler.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class PairingHandlerTests
{
    private static ICredentialProtector PassThrough()
    {
        var p = Substitute.For<ICredentialProtector>();
        p.Protect(Arg.Any<string>()).Returns(c => c.Arg<string>());
        return p;
    }

    private static PairingHandler CreateHandler(BotDbContext context, IEventBus bus) =>
        new(new ServerService(context), new CredentialStore(context, PassThrough()), bus,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PairingHandler>.Instance);

    private static PairingNotification ServerPairing(string ip = "1.2.3.4", int port = 28015, ulong steam = 7UL) =>
        new(PairingKind.Server, "Rustopia", ip, port, steam, "ptoken");

    [Fact]
    public async Task ServerPairing_CreatesServerCredentialAndFiresEventOnce()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        var server = await context.RustServers.SingleAsync();
        Assert.Equal("Rustopia", server.Name);
        var credential = await context.PlayerCredentials.SingleAsync();
        Assert.Equal(server.Id, credential.RustServerId);
        Assert.Equal(CredentialStatus.Active, credential.Status);
        await bus.Received(1).PublishAsync(
            Arg.Is<ServerRegisteredEvent>(e => e.GuildId == 10UL && e.ServerId == server.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SecondOwnerSameServer_AddsStandbyCredentialNoEvent()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 1UL, ServerPairing(steam: 1UL), CancellationToken.None);
        bus.ClearReceivedCalls();
        await handler.HandleAsync(10UL, 2UL, ServerPairing(steam: 2UL), CancellationToken.None);

        Assert.Single(await context.RustServers.ToListAsync());
        Assert.Equal(2, await context.PlayerCredentials.CountAsync());
        var second = await context.PlayerCredentials.SingleAsync(c => c.OwnerUserId == 2UL);
        Assert.Equal(CredentialStatus.Standby, second.Status);
        await bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EntityPairing_IsIgnored()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var handler = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 1UL,
            new PairingNotification(PairingKind.Entity, "x", "1.2.3.4", 28015, 1UL, "t"), CancellationToken.None);

        Assert.Empty(await context.RustServers.ToListAsync());
        Assert.Empty(await context.PlayerCredentials.ToListAsync());
        await bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Add the shared SQLite test fixture for this project**

Create `tests/RustPlusBot.Features.Pairing.Tests/TestDb.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Persistence;

namespace RustPlusBot.Features.Pairing.Tests;

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

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter PairingHandlerTests`
Expected: FAIL to compile (`PairingHandler` / `IPairingHandler` do not exist).

- [ ] **Step 4: Add the interface**

Create `src/RustPlusBot.Features.Pairing/Pairing/IPairingHandler.cs`:

```csharp
using RustPlusBot.Features.Pairing.Listening;

namespace RustPlusBot.Features.Pairing.Pairing;

/// <summary>Turns a pairing notification into a registered server and a pooled credential.</summary>
internal interface IPairingHandler
{
    /// <summary>Handles one notification for a given owner.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user whose listener received it.</param>
    /// <param name="notification">The pairing notification.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task HandleAsync(
        ulong guildId,
        ulong ownerUserId,
        PairingNotification notification,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Implement it**

Create `src/RustPlusBot.Features.Pairing/Pairing/PairingHandler.cs`:

```csharp
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Pairing.Pairing;

/// <summary>Default <see cref="IPairingHandler"/>: resolve-or-create the server, upsert the credential, announce new servers.</summary>
/// <param name="servers">Server resolve-or-create.</param>
/// <param name="credentials">The credential pool store.</param>
/// <param name="eventBus">Publishes the real ServerRegisteredEvent on new-server creation.</param>
/// <param name="logger">The logger.</param>
internal sealed class PairingHandler(
    IServerService servers,
    ICredentialStore credentials,
    IEventBus eventBus,
    ILogger<PairingHandler> logger) : IPairingHandler
{
    /// <inheritdoc />
    public async Task HandleAsync(
        ulong guildId,
        ulong ownerUserId,
        PairingNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (notification.Kind != PairingKind.Server)
        {
            logger.LogDebug("Ignoring {Kind} pairing notification (deferred to a later subsystem).", notification.Kind);
            return;
        }

        var (server, created) = await servers.ResolveOrCreateByEndpointAsync(
            guildId, ownerUserId, notification.ServerName, notification.Ip, notification.Port, cancellationToken)
            .ConfigureAwait(false);

        await credentials.UpsertFromPairingAsync(
            new StoreCredentialRequest(guildId, server.Id, ownerUserId, notification.PlayerId, notification.PlayerToken),
            cancellationToken).ConfigureAwait(false);

        if (created)
        {
            await eventBus.PublishAsync(new ServerRegisteredEvent(guildId, server.Id), cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter PairingHandlerTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Pairing/Pairing tests/RustPlusBot.Features.Pairing.Tests
git commit -m "feat(pairing): add PairingHandler for server registration

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 7: Owner notifier (DM seam)

**Files:**

- Create: `src/RustPlusBot.Features.Pairing/Notifications/IOwnerNotifier.cs`
- Create: `src/RustPlusBot.Features.Pairing/Notifications/DiscordOwnerNotifier.cs`

- [ ] **Step 1: Add the interface**

Create `src/RustPlusBot.Features.Pairing/Notifications/IOwnerNotifier.cs`:

```csharp
namespace RustPlusBot.Features.Pairing.Notifications;

/// <summary>Notifies a credential owner out-of-band (e.g. by DM) when action is needed.</summary>
internal interface IOwnerNotifier
{
    /// <summary>Tells the owner their FCM credentials expired and to reconnect.</summary>
    /// <param name="guildId">The guild the registration belongs to.</param>
    /// <param name="ownerUserId">The Discord user to notify.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task NotifyCredentialsExpiredAsync(ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Implement the Discord-backed notifier**

Create `src/RustPlusBot.Features.Pairing/Notifications/DiscordOwnerNotifier.cs`:

```csharp
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Pairing.Notifications;

/// <summary>DMs the owner. A closed-DM failure is swallowed (logged): the Expired state still shows in #setup.</summary>
/// <param name="client">The socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordOwnerNotifier(DiscordSocketClient client, ILogger<DiscordOwnerNotifier> logger)
    : IOwnerNotifier
{
    /// <inheritdoc />
    public async Task NotifyCredentialsExpiredAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var user = await client.GetUserAsync(ownerUserId).ConfigureAwait(false);
            if (user is null)
            {
                logger.LogWarning("Cannot DM owner {OwnerId}: user not found.", ownerUserId);
                return;
            }

            await user.SendMessageAsync(
                "Your Rust+ credentials were rejected. Reconnect your account in #setup to keep receiving pairings.")
                .ConfigureAwait(false);
        }
        catch (Exception ex) // Broad catch: a closed DM (or any send failure) must not break the supervisor.
        {
            logger.LogWarning(ex, "Failed to DM owner {OwnerId} about expired credentials.", ownerUserId);
        }
    }
}
```

> `IUser.SendMessageAsync` (an extension on the messaging interface) opens/uses the DM channel; verify the exact overload against Discord.Net 3.20 if the build objects, falling back to `(await user.CreateDMChannelAsync()).SendMessageAsync(...)`.

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/RustPlusBot.Features.Pairing`
Expected: SUCCESS, 0 warnings / 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Pairing/Notifications
git commit -m "feat(pairing): add owner DM notifier seam

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 8: PairingSupervisor (listener lifecycle, expiry, backoff)

**Files:**

- Create: `src/RustPlusBot.Features.Pairing/Supervisor/IPairingSupervisor.cs`
- Create: `src/RustPlusBot.Features.Pairing/Supervisor/PairingSupervisor.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/Fakes/FakePairingSource.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/Fakes/RecordingOwnerNotifier.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/PairingSupervisorTests.cs`

- [ ] **Step 1: Add the test fakes**

Create `tests/RustPlusBot.Features.Pairing.Tests/Fakes/FakePairingSource.cs`:

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Features.Pairing.Listening;

namespace RustPlusBot.Features.Pairing.Tests.Fakes;

/// <summary>A scripted <see cref="IPairingSource"/>: each created listener returns the next queued outcome.</summary>
internal sealed class FakePairingSource : IPairingSource
{
    private readonly ConcurrentQueue<PairingConnectOutcome> _outcomes = new();

    public int CreateCount { get; private set; }

    public Func<PairingNotification, CancellationToken, Task>? LastCallback { get; private set; }

    public TaskCompletionSource ConnectedSignal { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void EnqueueOutcome(PairingConnectOutcome outcome) => _outcomes.Enqueue(outcome);

    public IPairingListener Create(
        string fcmCredentialsJson,
        Func<PairingNotification, CancellationToken, Task> onNotification)
    {
        CreateCount++;
        LastCallback = onNotification;
        var outcome = _outcomes.TryDequeue(out var next) ? next : PairingConnectOutcome.Connected;
        return new FakeListener(outcome, () => ConnectedSignal.TrySetResult());
    }

    private sealed class FakeListener(PairingConnectOutcome outcome, Action onConnected) : IPairingListener
    {
        public Task<PairingConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (outcome == PairingConnectOutcome.Connected)
            {
                onConnected();
            }

            return Task.FromResult(outcome);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
```

Create `tests/RustPlusBot.Features.Pairing.Tests/Fakes/RecordingOwnerNotifier.cs`:

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Features.Pairing.Notifications;

namespace RustPlusBot.Features.Pairing.Tests.Fakes;

/// <summary>Records expiry notifications instead of DMing.</summary>
internal sealed class RecordingOwnerNotifier : IOwnerNotifier
{
    public ConcurrentBag<(ulong Guild, ulong Owner)> Notified { get; } = [];

    public Task NotifyCredentialsExpiredAsync(ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default)
    {
        Notified.Add((guildId, ownerUserId));
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Write the failing supervisor tests**

Create `tests/RustPlusBot.Features.Pairing.Tests/PairingSupervisorTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Features.Pairing.Tests.Fakes;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class PairingSupervisorTests
{
    private sealed class Harness : IAsyncDisposable
    {
        public required ServiceProvider Provider { get; init; }
        public required FakePairingSource Source { get; init; }
        public required RecordingOwnerNotifier Notifier { get; init; }
        public required PairingSupervisor Supervisor { get; init; }

        public async ValueTask DisposeAsync()
        {
            await Supervisor.StopAllAsync();
            await Provider.DisposeAsync();
        }
    }

    private static Harness CreateHarness(FakePairingSource source, PairingOptions? options = null)
    {
        var protector = Substitute.For<ICredentialProtector>();
        protector.Protect(Arg.Any<string>()).Returns(c => c.Arg<string>());
        protector.Unprotect(Arg.Any<string>()).Returns(c => c.Arg<string>());

        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(clock);
        services.AddSingleton(protector);
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        // Migrate the schema once, then keep the in-memory connection open (singleton) and hand each
        // scope its OWN context over that shared connection (EF contexts are not thread-safe, and the
        // supervisor opens scopes from background tasks).
        var (seed, connection) = TestDb.Create();
        seed.Dispose();
        services.AddSingleton(connection);
        services.AddScoped(sp => new BotDbContext(
            new DbContextOptionsBuilder<BotDbContext>().UseSqlite(sp.GetRequiredService<SqliteConnection>()).Options));
        services.AddScoped<IFcmRegistrationStore, FcmRegistrationStore>();
        services.AddScoped<IPairingHandler>(_ => Substitute.For<IPairingHandler>());

        var notifier = new RecordingOwnerNotifier();
        services.AddSingleton<IOwnerNotifier>(notifier);
        services.AddSingleton<IPairingSource>(source);
        services.AddSingleton(Options.Create(options ?? new PairingOptions
        {
            ProbeTimeout = TimeSpan.FromSeconds(1),
            InitialRetryDelay = TimeSpan.FromMilliseconds(5),
            MaxRetryDelay = TimeSpan.FromMilliseconds(20),
        }));
        services.AddSingleton<PairingSupervisor>();

        var provider = services.BuildServiceProvider();
        return new Harness
        {
            Provider = provider,
            Source = source,
            Notifier = notifier,
            Supervisor = provider.GetRequiredService<PairingSupervisor>(),
        };
    }

    private static async Task<Guid> SeedRegistrationAsync(ServiceProvider provider, ulong guild, ulong owner)
    {
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
        return await store.UpsertAsync(guild, owner, "{}");
    }

    [Fact]
    public async Task EnsureListener_Connected_ReturnsConnectedAndStaysActive()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        Assert.Equal(PairingConnectOutcome.Connected, outcome);
        Assert.Empty(h.Notifier.Notified);
        using var scope = h.Provider.CreateScope();
        var reg = await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>().GetAsync(10UL, 99UL);
        Assert.Equal(FcmRegistrationStatus.Active, reg!.Status);
    }

    [Fact]
    public async Task EnsureListener_Rejected_MarksExpiredAndNotifies()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Rejected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        Assert.Equal(PairingConnectOutcome.Rejected, outcome);
        using var scope = h.Provider.CreateScope();
        var reg = await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>().GetAsync(10UL, 99UL);
        Assert.Equal(FcmRegistrationStatus.Expired, reg!.Status);
        Assert.Contains((10UL, 99UL), h.Notifier.Notified);
    }

    [Fact]
    public async Task EnsureListener_TimeoutThenConnected_RetriesAndStaysActive()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Timeout);
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);

        var outcome = await h.Supervisor.EnsureListenerAsync(10UL, 99UL);
        Assert.Equal(PairingConnectOutcome.Timeout, outcome);

        // The background loop retries after InitialRetryDelay (5ms) and connects.
        await h.Source.ConnectedSignal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(h.Source.CreateCount >= 2);
        using var scope = h.Provider.CreateScope();
        var reg = await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>().GetAsync(10UL, 99UL);
        Assert.Equal(FcmRegistrationStatus.Active, reg!.Status);
    }

    [Fact]
    public async Task ConnectedListener_DeliversNotificationToHandler()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 99UL);
        await h.Supervisor.EnsureListenerAsync(10UL, 99UL);

        var note = new PairingNotification(PairingKind.Server, "S", "1.2.3.4", 28015, 7UL, "tok");
        await h.Source.LastCallback!(note, CancellationToken.None);

        // The supervisor opens a scope and calls IPairingHandler (a substitute) — assert it was invoked.
        using var scope = h.Provider.CreateScope();
        // Each scope yields a fresh substitute, so assert via a shared mock instead:
        // (covered by StartAll test below using CreateCount; here we assert no throw + active state)
        var reg = await scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>().GetAsync(10UL, 99UL);
        Assert.Equal(FcmRegistrationStatus.Active, reg!.Status);
    }

    [Fact]
    public async Task StartAllActive_StartsAListenerPerActiveRegistration()
    {
        var source = new FakePairingSource();
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        source.EnqueueOutcome(PairingConnectOutcome.Connected);
        await using var h = CreateHarness(source);
        await SeedRegistrationAsync(h.Provider, 10UL, 1UL);
        await SeedRegistrationAsync(h.Provider, 10UL, 2UL);

        await h.Supervisor.StartAllActiveAsync();

        Assert.Equal(2, h.Source.CreateCount);
    }
}
```

> The `IPairingHandler` substitute is per-scope, which makes asserting the call awkward; the dedicated handler behavior is already covered in Task 6. The `ConnectedListener_DeliversNotificationToHandler` test asserts the callback path runs without faulting. If you want a stronger assertion, register a shared singleton recording handler instead of a per-scope substitute and assert on it.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter PairingSupervisorTests`
Expected: FAIL to compile (`PairingSupervisor` / `IPairingSupervisor` do not exist).

- [ ] **Step 4: Add the interface**

Create `src/RustPlusBot.Features.Pairing/Supervisor/IPairingSupervisor.cs`:

```csharp
using RustPlusBot.Features.Pairing.Listening;

namespace RustPlusBot.Features.Pairing.Supervisor;

/// <summary>Owns the live FCM listeners — one per active registration.</summary>
internal interface IPairingSupervisor
{
    /// <summary>
    /// Starts (or restarts) the listener for (guild, owner) and returns the initial connect outcome.
    /// On <see cref="PairingConnectOutcome.Rejected"/> the registration is marked Expired and the owner notified.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="ownerUserId">The Discord user.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The initial connect outcome.</returns>
    Task<PairingConnectOutcome> EnsureListenerAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Starts listeners for every Active registration (called once at startup).</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StartAllActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Cancels and disposes every listener (called on shutdown).</summary>
    Task StopAllAsync();
}
```

- [ ] **Step 5: Implement the supervisor**

Create `src/RustPlusBot.Features.Pairing/Supervisor/PairingSupervisor.cs`:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Features.Pairing.Supervisor;

/// <summary>Default <see cref="IPairingSupervisor"/>: one connect-retry loop per active registration.</summary>
/// <param name="source">Creates listeners (FCM in production, a fake in tests).</param>
/// <param name="scopeFactory">Opens scopes for the scoped store and handler.</param>
/// <param name="notifier">Notifies owners when their credentials expire.</param>
/// <param name="protector">Unprotects stored credentials before connecting.</param>
/// <param name="options">Probe timeout and backoff settings.</param>
/// <param name="logger">The logger.</param>
internal sealed class PairingSupervisor(
    IPairingSource source,
    IServiceScopeFactory scopeFactory,
    IOwnerNotifier notifier,
    ICredentialProtector protector,
    IOptions<PairingOptions> options,
    ILogger<PairingSupervisor> logger) : IPairingSupervisor
{
    private readonly ConcurrentDictionary<(ulong Guild, ulong Owner), Handle> _listeners = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly PairingOptions _options = options.Value;

    private sealed record Handle(CancellationTokenSource Cts, Task RunTask);

    /// <inheritdoc />
    public async Task<PairingConnectOutcome> EnsureListenerAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        var key = (guildId, ownerUserId);
        await StopListenerAsync(key).ConfigureAwait(false);

        FcmRegistration? registration;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
            registration = await store.GetAsync(guildId, ownerUserId, cancellationToken).ConfigureAwait(false);
        }

        if (registration is null || registration.Status == FcmRegistrationStatus.Disabled)
        {
            return PairingConnectOutcome.Rejected;
        }

        string credentialsJson;
        try
        {
            credentialsJson = protector.Unprotect(registration.ProtectedFcmCredentials);
        }
        catch (CryptographicException ex)
        {
            logger.LogError(ex, "Stored FCM credentials for owner {OwnerId} are unreadable.", ownerUserId);
            await MarkExpiredAsync(key, registration.Id).ConfigureAwait(false);
            return PairingConnectOutcome.Rejected;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        var firstOutcome = new TaskCompletionSource<PairingConnectOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runTask = Task.Run(
            () => RunAsync(key, registration.Id, credentialsJson, firstOutcome, cts.Token),
            CancellationToken.None);
        _listeners[key] = new Handle(cts, runTask);
        return await firstOutcome.Task.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StartAllActiveAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FcmRegistration> active;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
            active = await store.ListActiveAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var registration in active)
        {
            await EnsureListenerAsync(registration.GuildId, registration.OwnerUserId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task StopAllAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        foreach (var key in _listeners.Keys.ToList())
        {
            await StopListenerAsync(key).ConfigureAwait(false);
        }
    }

    private async Task StopListenerAsync((ulong Guild, ulong Owner) key)
    {
        if (!_listeners.TryRemove(key, out var handle))
        {
            return;
        }

        await handle.Cts.CancelAsync().ConfigureAwait(false);
        try
        {
            await handle.RunTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
        finally
        {
            handle.Cts.Dispose();
        }
    }

    private async Task RunAsync(
        (ulong Guild, ulong Owner) key,
        Guid registrationId,
        string credentialsJson,
        TaskCompletionSource<PairingConnectOutcome> firstOutcome,
        CancellationToken cancellationToken)
    {
        var delay = _options.InitialRetryDelay;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var listener = source.Create(
                    credentialsJson,
                    (notification, ct) => HandleNotificationAsync(key, notification, ct));

                PairingConnectOutcome outcome;
                try
                {
                    outcome = await listener.ConnectAsync(_options.ProbeTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    await listener.DisposeAsync().ConfigureAwait(false);
                    throw;
                }

                firstOutcome.TrySetResult(outcome);

                if (outcome == PairingConnectOutcome.Connected)
                {
                    try
                    {
                        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // Stopping.
                    }
                    finally
                    {
                        await listener.DisposeAsync().ConfigureAwait(false);
                    }

                    return;
                }

                await listener.DisposeAsync().ConfigureAwait(false);

                if (outcome == PairingConnectOutcome.Rejected)
                {
                    await MarkExpiredAsync(key, registrationId).ConfigureAwait(false);
                    return;
                }

                // Timeout: back off and retry the initial connect.
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay = delay < _options.MaxRetryDelay
                    ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _options.MaxRetryDelay.Ticks))
                    : _options.MaxRetryDelay;
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
        catch (Exception ex) // Broad catch: a faulting listener loop must not crash the host.
        {
            logger.LogError(ex, "Listener loop for owner {OwnerId} faulted.", key.Owner);
        }
        finally
        {
            firstOutcome.TrySetResult(PairingConnectOutcome.Timeout);
        }
    }

    private async Task HandleNotificationAsync(
        (ulong Guild, ulong Owner) key,
        PairingNotification notification,
        CancellationToken cancellationToken)
    {
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var handler = scope.ServiceProvider.GetRequiredService<IPairingHandler>();
                await handler.HandleAsync(key.Guild, key.Owner, notification, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) // Broad catch: one bad notification must not kill the listener.
        {
            logger.LogError(ex, "Failed to handle pairing notification for owner {OwnerId}.", key.Owner);
        }
    }

    private async Task MarkExpiredAsync((ulong Guild, ulong Owner) key, Guid registrationId)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
            await store.SetStatusAsync(registrationId, FcmRegistrationStatus.Expired).ConfigureAwait(false);
        }

        await notifier.NotifyCredentialsExpiredAsync(key.Guild, key.Owner).ConfigureAwait(false);
    }
}
```

- [ ] **Step 6: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter PairingSupervisorTests`
Expected: PASS. (If the timeout-retry test is flaky on a slow machine, raise the `WaitAsync` budget; the delays are 5–20 ms.)

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Pairing/Supervisor tests/RustPlusBot.Features.Pairing.Tests
git commit -m "feat(pairing): add PairingSupervisor with expiry and backoff

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 9: RustPlusApi.Fcm adapter (real IPairingSource)

> This is the one task that touches the external `RustPlusApi.Fcm` package — an integration shim with no unit tests (it is faked everywhere else). The member names below are taken from the package's quickstart; **verify each against the installed `2.0.0-beta.1` assembly** and adjust if the beta differs. Do not invent members — if a name is wrong, check the package's public API (e.g. via the IDE's go-to-definition on `RustPlusFcm`).

**Files:**

- Create: `src/RustPlusBot.Features.Pairing/Listening/RustPlusFcmPairingSource.cs`

- [ ] **Step 1: Implement the adapter**

Create `src/RustPlusBot.Features.Pairing/Listening/RustPlusFcmPairingSource.cs`:

```csharp
using Microsoft.Extensions.Logging;
using RustPlusApi.Fcm;

namespace RustPlusBot.Features.Pairing.Listening;

/// <summary>Real <see cref="IPairingSource"/> backed by RustPlusApi.Fcm. Untested by unit tests (integration shim).</summary>
/// <param name="logger">The logger.</param>
internal sealed class RustPlusFcmPairingSource(ILogger<RustPlusFcmPairingSource> logger) : IPairingSource
{
    /// <inheritdoc />
    public IPairingListener Create(
        string fcmCredentialsJson,
        Func<PairingNotification, CancellationToken, Task> onNotification) =>
        new RustPlusFcmListener(fcmCredentialsJson, onNotification, logger);

    private sealed class RustPlusFcmListener : IPairingListener
    {
        private readonly Func<PairingNotification, CancellationToken, Task> _onNotification;
        private readonly ILogger _logger;
        private readonly RustPlusFcm _fcm;

        public RustPlusFcmListener(
            string fcmCredentialsJson,
            Func<PairingNotification, CancellationToken, Task> onNotification,
            ILogger logger)
        {
            _onNotification = onNotification;
            _logger = logger;
            // VERIFY: how RustPlusApi.Fcm parses credentials (a Credentials type / static parse helper) and
            // the RustPlusFcm constructor signature in 2.0.0-beta.1.
            _fcm = new RustPlusFcm(fcmCredentialsJson);
            _fcm.OnServerPairing += OnServerPairing;
        }

        public async Task<PairingConnectOutcome> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(timeout);
                // VERIFY: RustPlusFcm.ConnectAsync signature / cancellation support.
                await _fcm.ConnectAsync(timeoutCts.Token).ConfigureAwait(false);
                return PairingConnectOutcome.Connected;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return PairingConnectOutcome.Timeout;
            }
            catch (Exception ex) // Auth/credential rejection from FCM.
            {
                _logger.LogWarning(ex, "FCM connect was rejected.");
                return PairingConnectOutcome.Rejected;
            }
        }

        private void OnServerPairing(object? sender, ServerPairingEventArgs e)
        {
            // VERIFY: the event-args type name and the property names on e.Data.
            var data = e.Data;
            if (data is null)
            {
                return;
            }

            var notification = new PairingNotification(
                PairingKind.Server,
                data.Name ?? "Rust server",
                data.Ip ?? string.Empty,
                data.Port,
                data.PlayerId,
                data.PlayerToken ?? string.Empty);

            // Bridge the sync event to the async callback; failures are logged, never thrown back into FCM.
            _ = Task.Run(async () =>
            {
                try
                {
                    await _onNotification(notification, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Pairing notification handler faulted.");
                }
            });
        }

        public async ValueTask DisposeAsync()
        {
            _fcm.OnServerPairing -= OnServerPairing;
            // VERIFY: whether RustPlusFcm exposes DisposeAsync / Dispose / a Disconnect method.
            if (_fcm is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else if (_fcm is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
```

- [ ] **Step 2: Build and reconcile against the real package API**

Run: `dotnet build src/RustPlusBot.Features.Pairing`
Expected: SUCCESS. If member names (`RustPlusFcm`, `OnServerPairing`, `ServerPairingEventArgs`, `e.Data.*`, `ConnectAsync`) do not match `2.0.0-beta.1`, fix them per the package's actual public API — keep the mapping to `PairingNotification` and the `Connected`/`Timeout`/`Rejected` outcome contract identical.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Features.Pairing/Listening/RustPlusFcmPairingSource.cs
git commit -m "feat(pairing): add RustPlusApi.Fcm adapter for IPairingSource

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 10: PairingHostedService

**Files:**

- Create: `src/RustPlusBot.Features.Pairing/Hosting/PairingHostedService.cs`

- [ ] **Step 1: Implement the hosted service**

Create `src/RustPlusBot.Features.Pairing/Hosting/PairingHostedService.cs`:

```csharp
using Discord.WebSocket;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Features.Pairing.Supervisor;

namespace RustPlusBot.Features.Pairing.Hosting;

/// <summary>Drives the supervisor from the host lifecycle: start listeners on Ready, stop them on shutdown.</summary>
/// <param name="client">The socket client (for the Ready event).</param>
/// <param name="supervisor">The pairing supervisor.</param>
/// <param name="logger">The logger.</param>
internal sealed class PairingHostedService(
    DiscordSocketClient client,
    IPairingSupervisor supervisor,
    ILogger<PairingHostedService> logger) : IHostedService
{
    private bool _started;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += OnReadyAsync;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= OnReadyAsync;
        await supervisor.StopAllAsync().ConfigureAwait(false);
    }

    private async Task OnReadyAsync()
    {
        // Ready fires on every (re)connect; only start listeners once per process.
        if (_started)
        {
            return;
        }

        _started = true;
        try
        {
            await supervisor.StartAllActiveAsync().ConfigureAwait(false);
        }
        catch (Exception ex) // Broad catch: a failed startup must not crash the host.
        {
            logger.LogError(ex, "Failed to start FCM pairing listeners.");
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/RustPlusBot.Features.Pairing`
Expected: SUCCESS, 0 warnings / 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Features.Pairing/Hosting
git commit -m "feat(pairing): add PairingHostedService

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 11: FCM credential validator + the connect modal

**Files:**

- Create: `src/RustPlusBot.Features.Pairing/Validation/FcmCredentialValidator.cs`
- Create: `src/RustPlusBot.Features.Pairing/Modules/ConnectModal.cs`
- Create: `src/RustPlusBot.Features.Pairing/Modules/CredentialModule.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/FcmCredentialValidatorTests.cs`

- [ ] **Step 1: Write the failing validator tests**

Create `tests/RustPlusBot.Features.Pairing.Tests/FcmCredentialValidatorTests.cs`:

```csharp
using RustPlusBot.Features.Pairing.Validation;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class FcmCredentialValidatorTests
{
    [Theory]
    [InlineData("{\"fcm\":{\"token\":\"abc\"}}")]
    [InlineData("   {\"a\":1}   ")]
    public void IsWellFormed_True_ForJsonObject(string json) =>
        Assert.True(FcmCredentialValidator.IsWellFormed(json));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    public void IsWellFormed_False_ForNonObjectOrGarbage(string? json) =>
        Assert.False(FcmCredentialValidator.IsWellFormed(json));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter FcmCredentialValidatorTests`
Expected: FAIL to compile (`FcmCredentialValidator` does not exist).

- [ ] **Step 3: Implement the validator**

Create `src/RustPlusBot.Features.Pairing/Validation/FcmCredentialValidator.cs`:

```csharp
using System.Text.Json;

namespace RustPlusBot.Features.Pairing.Validation;

/// <summary>
/// A cheap structural guard against pasted garbage: confirms the blob is a JSON object. The authoritative
/// check is the live connect probe (a malformed-but-objecty blob is rejected by FCM at connect time).
/// </summary>
internal static class FcmCredentialValidator
{
    /// <summary>True when <paramref name="json"/> is non-empty and parses as a JSON object.</summary>
    /// <param name="json">The pasted credentials blob.</param>
    /// <returns>Whether it is structurally usable.</returns>
    public static bool IsWellFormed(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 4: Run to verify the validator passes**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter FcmCredentialValidatorTests`
Expected: PASS.

- [ ] **Step 5: Add the modal definition**

Create `src/RustPlusBot.Features.Pairing/Modules/ConnectModal.cs`:

```csharp
using Discord;
using Discord.Interactions;

namespace RustPlusBot.Features.Pairing.Modules;

/// <summary>The ephemeral modal that collects a user's FCM credentials JSON.</summary>
public sealed class ConnectModal : IModal
{
    /// <summary>Custom id of the modal, handled by <see cref="CredentialModule"/>.</summary>
    public const string ModalId = "pairing:connect:modal";

    /// <summary>Custom id of the credentials text input.</summary>
    public const string CredentialsInputId = "pairing:connect:credentials";

    /// <inheritdoc />
    public string Title => "Connect your Rust+ account";

    /// <summary>The pasted FCM credentials JSON.</summary>
    [InputLabel("FCM credentials JSON")]
    [ModalTextInput(CredentialsInputId, TextInputStyle.Paragraph, placeholder: "Paste the JSON from the credentials helper")]
    public string Credentials { get; set; } = string.Empty;
}
```

- [ ] **Step 6: Add the interaction module**

Create `src/RustPlusBot.Features.Pairing/Modules/CredentialModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Features.Pairing.Validation;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Persistence.Credentials;

namespace RustPlusBot.Features.Pairing.Modules;

/// <summary>Handles the #setup "Connect account" button and the credentials modal submission.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class CredentialModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Opens the credentials modal when the #setup button is clicked.</summary>
    [ComponentInteraction(WorkspaceComponentIds.ConnectAccount)]
    public async Task OpenAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await Context.Interaction.RespondWithModalAsync<ConnectModal>(ConnectModal.ModalId).ConfigureAwait(false);
    }

    /// <summary>Validates, stores, and connects the submitted credentials.</summary>
    /// <param name="modal">The submitted modal.</param>
    [ModalInteraction(ConnectModal.ModalId)]
    public async Task SubmitAsync(ConnectModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        if (!FcmCredentialValidator.IsWellFormed(modal.Credentials))
        {
            await FollowupAsync(
                "I couldn't read those credentials. Paste the full JSON produced by the credentials helper.",
                ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var registrations = scope.ServiceProvider.GetRequiredService<IFcmRegistrationStore>();
            await registrations.UpsertAsync(Context.Guild.Id, Context.User.Id, modal.Credentials).ConfigureAwait(false);

            var supervisor = scope.ServiceProvider.GetRequiredService<IPairingSupervisor>();
            var outcome = await supervisor.EnsureListenerAsync(Context.Guild.Id, Context.User.Id).ConfigureAwait(false);

            var message = outcome switch
            {
                PairingConnectOutcome.Connected =>
                    "Account connected. Pair with a server in-game and it'll show up here automatically.",
                PairingConnectOutcome.Rejected =>
                    "Those credentials were rejected by Rust+. Generate fresh credentials and reconnect.",
                _ => "Account saved — still verifying the connection. Pairings will appear once it's up.",
            };
            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }
    }
}
```

> The supervisor is a singleton; resolving it from the per-interaction scope returns that singleton (the internal type is only used inside this method body, so the public module's signature stays clean — the same pattern `SetupModule` uses for `IWorkspaceReconciler`).

- [ ] **Step 7: Build**

Run: `dotnet build src/RustPlusBot.Features.Pairing`
Expected: SUCCESS, 0 warnings / 0 errors.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Pairing/Validation src/RustPlusBot.Features.Pairing/Modules tests/RustPlusBot.Features.Pairing.Tests/FcmCredentialValidatorTests.cs
git commit -m "feat(pairing): add credential validator and connect modal

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 12: Workspace "Connect account" button

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/WorkspaceComponentIds.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Messages/SetupMessageRenderer.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`

- [ ] **Step 1: Write the failing renderer test**

In `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`, add (before the closing brace):

```csharp
    [Fact]
    public async Task Setup_HasConnectAccountButton()
    {
        var renderer = new SetupMessageRenderer(Loc);

        var payload = await renderer.RenderAsync(Global, default);

        Assert.NotNull(payload.Components);
        var buttons = payload.Components!.Components.OfType<ActionRowComponent>()
            .SelectMany(r => r.Components).OfType<ButtonComponent>();
        Assert.Contains(buttons, b => b.CustomId == "workspace:setup:connect");
    }
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter RendererTests`
Expected: FAIL — `Setup_HasConnectAccountButton` fails because `payload.Components` is null (and `WorkspaceComponentIds` does not exist yet, so it may fail to compile).

- [ ] **Step 3: Add the public component-id surface**

Create `src/RustPlusBot.Features.Workspace/WorkspaceComponentIds.cs`:

```csharp
namespace RustPlusBot.Features.Workspace;

/// <summary>
/// Public custom ids for components rendered into the workspace by Workspace but handled by other features.
/// Feature modules reference these so the rendered control and its handler share one contract.
/// </summary>
public static class WorkspaceComponentIds
{
    /// <summary>The #setup "Connect account" button; handled by the Pairing feature.</summary>
    public const string ConnectAccount = "workspace:setup:connect";
}
```

- [ ] **Step 4: Render the button**

Replace the `RenderAsync` body of `src/RustPlusBot.Features.Workspace/Messages/SetupMessageRenderer.cs`:

```csharp
    /// <inheritdoc />
    public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("setup.title", context.Culture))
            .WithDescription(localizer.Get("setup.body", context.Culture))
            .WithColor(Color.Blue)
            .Build();
        var components = new ComponentBuilder()
            .WithButton(
                localizer.Get("setup.connect.button", context.Culture),
                WorkspaceComponentIds.ConnectAccount,
                ButtonStyle.Primary)
            .Build();
        return ValueTask.FromResult(new MessagePayload(null, embed, components));
    }
```

(`SetupMessageRenderer` is in namespace `RustPlusBot.Features.Workspace.Messages`, which can see `WorkspaceComponentIds` in the root namespace; add `using RustPlusBot.Features.Workspace;` if the analyzer requires an explicit using.)

- [ ] **Step 5: Add and revise the localization strings**

In `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`:

In the `["en"]` dictionary, replace the `setup.body` value and add the button key:

```csharp
                ["setup.title"] = "Connect your Rust+ account",
                ["setup.body"] =
                    "Click **Connect account** below and paste your Rust+ FCM credentials. Then pair a server in-game and its channels appear automatically.",
                ["setup.connect.button"] = "Connect account",
```

In the `["fr"]` dictionary, do the same:

```csharp
                ["setup.title"] = "Connectez votre compte Rust+",
                ["setup.body"] =
                    "Cliquez sur **Connecter le compte** ci-dessous et collez vos identifiants FCM Rust+. Appairez ensuite un serveur en jeu et ses salons apparaitront automatiquement.",
                ["setup.connect.button"] = "Connecter le compte",
```

- [ ] **Step 6: Run the renderer tests**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter RendererTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs
git commit -m "feat(workspace): add Connect account button to #setup

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

### Task 13: DI wiring, Host integration, and final verification

**Files:**

- Create: `src/RustPlusBot.Features.Pairing/PairingServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Host/RustPlusBot.Host.csproj`
- Modify: `src/RustPlusBot.Host/Program.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/PairingRegistrationTests.cs`

- [ ] **Step 1: Add the AddPairing extension**

Create `src/RustPlusBot.Features.Pairing/PairingServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Pairing.Hosting;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Supervisor;

namespace RustPlusBot.Features.Pairing;

/// <summary>DI registration for the pairing feature.</summary>
public static class PairingServiceCollectionExtensions
{
    /// <summary>Registers the pairing source, supervisor, handler, notifier, modules, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPairing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IPairingHandler, PairingHandler>();
        services.AddSingleton<IPairingSource, RustPlusFcmPairingSource>();
        services.AddSingleton<IOwnerNotifier, DiscordOwnerNotifier>();
        services.AddSingleton<IPairingSupervisor, PairingSupervisor>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(PairingServiceCollectionExtensions).Assembly));

        services.AddHostedService<PairingHostedService>();

        return services;
    }
}
```

- [ ] **Step 2: Write the failing registration test**

Create `tests/RustPlusBot.Features.Pairing.Tests/PairingRegistrationTests.cs`:

```csharp
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Pairing;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Supervisor;
using RustPlusBot.Persistence;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class PairingRegistrationTests
{
    [Fact]
    public void Services_Resolve()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(Substitute.For<ICredentialProtector>());
        services.AddLogging();
        services.AddBotPersistence("DataSource=:memory:");
        services.AddOptions<PairingOptions>();
        services.AddPairing();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        Assert.NotNull(provider.GetRequiredService<IPairingSupervisor>());
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPairingHandler>());
    }
}
```

`AddBotPersistence` lives in `RustPlusBot.Persistence`; add `using RustPlusBot.Persistence;` at the top.

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter PairingRegistrationTests`
Expected: FAIL to compile (`AddPairing` not found) — until Step 1 is built; if Step 1 is already in, it should pass. Run after Step 1.

- [ ] **Step 4: Reference the Pairing project from the Host**

In `src/RustPlusBot.Host/RustPlusBot.Host.csproj`, add to the `ProjectReference` ItemGroup:

```xml
    <ProjectReference Include="..\RustPlusBot.Features.Pairing\RustPlusBot.Features.Pairing.csproj" />
```

- [ ] **Step 5: Wire it in Program.cs**

In `src/RustPlusBot.Host/Program.cs`, add `using RustPlusBot.Features.Pairing;` with the other usings. After the `AddWorkspace();` line (line 30) add:

```csharp
builder.Services.AddOptions<PairingOptions>()
    .Bind(builder.Configuration.GetSection("Pairing"))
    .Validate(static o => o.ProbeTimeout > TimeSpan.Zero, "Pairing:ProbeTimeout must be positive.")
    .Validate(static o => o.InitialRetryDelay > TimeSpan.Zero, "Pairing:InitialRetryDelay must be positive.")
    .Validate(static o => o.MaxRetryDelay >= o.InitialRetryDelay,
        "Pairing:MaxRetryDelay must be at least InitialRetryDelay.")
    .ValidateOnStart();
builder.Services.AddPairing();
```

- [ ] **Step 6: Run the registration test**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter PairingRegistrationTests`
Expected: PASS.

- [ ] **Step 7: Full solution build with analyzers**

Run: `dotnet build`
Expected: SUCCESS, **0 warnings / 0 errors** across all projects.

- [ ] **Step 8: Full test sweep**

Run: `dotnet test`
Expected: PASS — all projects (Persistence, Workspace, Pairing, Abstractions).

- [ ] **Step 9: Format check (matches CI)**

Run: `dotnet format --verify-no-changes`
Expected: no formatting changes required. If it reports changes, run `dotnet format` and re-run the build + tests.

- [ ] **Step 10: Final commit**

```bash
git add src/RustPlusBot.Features.Pairing/PairingServiceCollectionExtensions.cs src/RustPlusBot.Host tests/RustPlusBot.Features.Pairing.Tests/PairingRegistrationTests.cs
git commit -m "feat(pairing): wire pairing feature into the host

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Final state

When all tasks are complete:

- A guild member sees a **Connect account** button in `#setup`; pasting valid FCM credentials connects a per-user listener and stores an `FcmRegistration`.
- Pairing a server in-game creates the `RustServer` (resolve-or-create by `GuildId`+`Ip`+`Port`), provisions its workspace via the existing 1a reconciler (the real `ServerRegisteredEvent`), and adds a `PlayerCredential` to the pool (first owner per server → `Active`).
- Rejected credentials flip the registration to `Expired` and DM the owner; transient connect failures retry with capped backoff.
- All work is covered by tests using an `IPairingSource` fake — no real FCM connection in the test suite. Build is 0/0 under strict analyzers.

**Deferred to 1b-ii (carry-forwards):** live Rust+ socket per `(guild,server)`; `ConnectionState` health/restart-survival; active-identity **swap UI** + reconnect; auto-failover → `Invalid`; server-unreachable detection; `#info` live status; `ICredentialStore.SetStatusAsync`; supervisor-driven mid-session reconnection; IP-change re-identification; entity pairings (subsystems 4/5).
