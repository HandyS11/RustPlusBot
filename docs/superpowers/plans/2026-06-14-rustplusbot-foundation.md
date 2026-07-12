# RustPlusBot Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a bootable, multi-guild, per-guild-isolated Discord bot skeleton — Generic Host + Discord.Net + Persistord-backed SQLite persistence + an in-process event bus + credential-storage seams + the `/server` and `/bind` slash-command surface — with no live Rust+ connection yet.

**Architecture:** Modular monolith, single .NET 10 Generic Host process. Feature logic lives in small, interface-bounded services (`ServerService`, `BindingService`, `IEventBus`, `IClock`, `ICredentialStore`, `ICredentialProtector`) that are unit-testable without Discord or a real Rust server. Discord.Net interaction modules are thin shells that delegate to those services. Persistence derives from Persistord's `DiscordDbContext` (which supplies the global `ulong`↔`long` snowflake conversion); all Rust-domain rows carry a `GuildId` and every query is guild-scoped.

**Tech Stack:** .NET 10, C# latest, Discord.Net 3.20.x (`DiscordSocketClient` + `InteractionService`), EF Core 10.0.9 (SQLite), Persistord.Core (prerelease NuGet), `Microsoft.Extensions.Hosting`, `Microsoft.AspNetCore.DataProtection`, xUnit + NSubstitute. `TreatWarningsAsErrors=true` with NetAnalyzers/Roslynator/Sonar is already enabled in `Directory.Build.props`, so all code must be analyzer-clean.

**Scope boundary (deferred to subsystem 1):** live `RustPlusConnectionManager`, `/setup` credential *validation* by connecting, FCM pairing listener, hot-swap/failover, and the `RustPlusBot.RustPlus` project. This plan creates the storage + seams those will use, but does not pull the `RustPlusApi` packages.

---

## File structure

| Project | Responsibility |
| --- | --- |
| `src/RustPlusBot.Abstractions` | Pure contracts + value types: `IClock`, `IEventBus`, `ICredentialProtector`, `ICredentialStore`, `IBotLocalizer`. No EF, no Discord. |
| `src/RustPlusBot.Domain` | Entities + enums: `RustServer`, `PlayerCredential`, `CredentialStatus`, `ConnectionState`, `GuildSettings`, `ChannelBinding`, `BoundFeature`, `PairedEntity`, `PairedEntityKind`, `EventSubscription`. POCOs only. |
| `src/RustPlusBot.Persistence` | `BotDbContext : DiscordDbContext`, EF entity configurations, migrations, `ServerService`, `BindingService`, `CredentialStore`. |
| `src/RustPlusBot.Discord` | `DiscordOptions`, `DiscordBotService` (hosted service: login + InteractionService), `ServerModule`, `BindModule`. |
| `src/RustPlusBot.Host` | `Program.cs` Generic Host wiring, configuration, `DataProtectionCredentialProtector`, `ResxBotLocalizer`. |
| `tests/RustPlusBot.Abstractions.Tests` | Tests for the in-memory bus, clock. |
| `tests/RustPlusBot.Persistence.Tests` | Tests for context round-trips, services, credential store. |

---

## Task 1: Scaffold solution and projects

**Files:**
- Create: the six projects above + two test projects
- Modify: `RustPlusBot.slnx`

- [ ] **Step 1: Create the source projects**

Run from repo root (`/home/handys11/Dev/RustPlusBot`):

```bash
dotnet new classlib -n RustPlusBot.Abstractions -o src/RustPlusBot.Abstractions
dotnet new classlib -n RustPlusBot.Domain        -o src/RustPlusBot.Domain
dotnet new classlib -n RustPlusBot.Persistence    -o src/RustPlusBot.Persistence
dotnet new classlib -n RustPlusBot.Discord        -o src/RustPlusBot.Discord
dotnet new worker   -n RustPlusBot.Host           -o src/RustPlusBot.Host
rm src/RustPlusBot.Abstractions/Class1.cs src/RustPlusBot.Domain/Class1.cs src/RustPlusBot.Persistence/Class1.cs src/RustPlusBot.Discord/Class1.cs
```

- [ ] **Step 2: Create the test projects**

```bash
dotnet new xunit -n RustPlusBot.Abstractions.Tests -o tests/RustPlusBot.Abstractions.Tests
dotnet new xunit -n RustPlusBot.Persistence.Tests   -o tests/RustPlusBot.Persistence.Tests
rm tests/RustPlusBot.Abstractions.Tests/UnitTest1.cs tests/RustPlusBot.Persistence.Tests/UnitTest1.cs
```

- [ ] **Step 3: Wire project references**

```bash
dotnet add src/RustPlusBot.Domain reference src/RustPlusBot.Abstractions
dotnet add src/RustPlusBot.Persistence reference src/RustPlusBot.Domain src/RustPlusBot.Abstractions
dotnet add src/RustPlusBot.Discord reference src/RustPlusBot.Persistence src/RustPlusBot.Domain src/RustPlusBot.Abstractions
dotnet add src/RustPlusBot.Host reference src/RustPlusBot.Discord src/RustPlusBot.Persistence src/RustPlusBot.Domain src/RustPlusBot.Abstractions
dotnet add tests/RustPlusBot.Abstractions.Tests reference src/RustPlusBot.Abstractions
dotnet add tests/RustPlusBot.Persistence.Tests reference src/RustPlusBot.Persistence src/RustPlusBot.Domain src/RustPlusBot.Abstractions
```

- [ ] **Step 4: Set the TFM on every project**

Each `.csproj` under `src/` and `tests/` must target net10.0. Edit each `<TargetFramework>` to `net10.0` (the worker template may already use it; the classlib/xunit templates may default lower). Verify:

```bash
grep -rL "net10.0" src tests --include=*.csproj
```

Expected: prints nothing (every csproj contains `net10.0`).

- [ ] **Step 5: Add all projects to the solution**

```bash
dotnet sln RustPlusBot.slnx add $(find src tests -name *.csproj)
```

- [ ] **Step 6: Verify the empty solution builds**

```bash
dotnet build RustPlusBot.slnx
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "chore: scaffold foundation solution and projects"
```

---

## Task 2: Pin third-party packages

**Files:**
- Modify: `Directory.Packages.props`
- Modify: project `.csproj` files (PackageReference entries)

- [ ] **Step 1: Add package versions to central management**

Edit `Directory.Packages.props`, adding these inside the first `<ItemGroup>` (the `<!-- Packages -->` group):

```xml
<PackageVersion Include="Discord.Net" Version="3.20.0" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.9" />
<PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.9" />
<PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
<PackageVersion Include="Microsoft.AspNetCore.DataProtection" Version="10.0.0" />
```

- [ ] **Step 2: Add the prerelease Persistord package**

Persistord is a prerelease NuGet package; let the SDK resolve and record the exact version (do not guess the version string):

```bash
dotnet add src/RustPlusBot.Persistence package Persistord.Core --prerelease
```

This writes a `<PackageReference Include="Persistord.Core" />` to the csproj and a `<PackageVersion Include="Persistord.Core" Version="..." />` to `Directory.Packages.props`. Confirm both exist:

```bash
grep Persistord.Core Directory.Packages.props src/RustPlusBot.Persistence/RustPlusBot.Persistence.csproj
```

- [ ] **Step 3: Reference packages from the projects that need them**

```bash
dotnet add src/RustPlusBot.Persistence package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/RustPlusBot.Persistence package Microsoft.EntityFrameworkCore.Design
dotnet add src/RustPlusBot.Discord package Discord.Net
dotnet add src/RustPlusBot.Host package Microsoft.Extensions.Hosting
dotnet add src/RustPlusBot.Host package Microsoft.AspNetCore.DataProtection
dotnet add tests/RustPlusBot.Persistence.Tests package Microsoft.EntityFrameworkCore.Sqlite
```

(With central package management, `dotnet add package` omits the version in the csproj and uses the pinned `PackageVersion`.)

- [ ] **Step 4: Verify restore + build**

```bash
dotnet build RustPlusBot.slnx
```

Expected: Build succeeded, 0 errors. If Persistord.Core fails to restore, confirm `nuget.org` is the configured feed (`NuGet.config`) and retry with `--prerelease`.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "chore: pin Discord.Net, EF Core, Persistord, hosting packages"
```

---

## Task 3: IClock abstraction (establishes the TDD rhythm)

**Files:**
- Create: `src/RustPlusBot.Abstractions/Time/IClock.cs`
- Create: `src/RustPlusBot.Abstractions/Time/SystemClock.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/Time/SystemClockTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Abstractions.Tests.Time;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_ReturnsCurrentInstant_WithinTolerance()
    {
        IClock clock = new SystemClock();

        var before = DateTimeOffset.UtcNow;
        var value = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(value, before, after);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter SystemClockTests`
Expected: FAIL — `IClock`/`SystemClock` do not exist (compile error).

- [ ] **Step 3: Write the implementation**

`src/RustPlusBot.Abstractions/Time/IClock.cs`:

```csharp
namespace RustPlusBot.Abstractions.Time;

/// <summary>Abstracts the system clock so time-dependent logic is testable.</summary>
public interface IClock
{
    /// <summary>The current UTC instant.</summary>
    DateTimeOffset UtcNow { get; }
}
```

`src/RustPlusBot.Abstractions/Time/SystemClock.cs`:

```csharp
namespace RustPlusBot.Abstractions.Time;

/// <summary>An <see cref="IClock"/> backed by the wall clock.</summary>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter SystemClockTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add IClock abstraction"
```

---

## Task 4: In-process event bus

**Files:**
- Create: `src/RustPlusBot.Abstractions/Events/IEventBus.cs`
- Create: `src/RustPlusBot.Abstractions/Events/InMemoryEventBus.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/Events/InMemoryEventBusTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests.Events;

public sealed class InMemoryEventBusTests
{
    private sealed record Ping(string Text);

    [Fact]
    public async Task Subscribe_ReceivesEventPublishedAfterSubscribing()
    {
        var bus = new InMemoryEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var stream = bus.Subscribe<Ping>(cts.Token);
        await bus.PublishAsync(new Ping("hello"), cts.Token);

        await foreach (var ping in stream.WithCancellation(cts.Token))
        {
            Assert.Equal("hello", ping.Text);
            return;
        }

        Assert.Fail("No event was received.");
    }

    [Fact]
    public async Task PublishAsync_WithNoSubscribers_DoesNotThrow()
    {
        var bus = new InMemoryEventBus();
        await bus.PublishAsync(new Ping("nobody-listening"));
    }

    [Fact]
    public async Task Subscribe_OnlyReceivesItsOwnEventType()
    {
        var bus = new InMemoryEventBus();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var stream = bus.Subscribe<Ping>(cts.Token);
        await bus.PublishAsync("a string, not a Ping", cts.Token);
        await bus.PublishAsync(new Ping("the-real-one"), cts.Token);

        await foreach (var ping in stream.WithCancellation(cts.Token))
        {
            Assert.Equal("the-real-one", ping.Text);
            return;
        }

        Assert.Fail("No Ping event was received.");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter InMemoryEventBusTests`
Expected: FAIL — `IEventBus`/`InMemoryEventBus` do not exist.

- [ ] **Step 3: Write the contract**

`src/RustPlusBot.Abstractions/Events/IEventBus.cs`:

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// A lightweight in-process publish/subscribe backbone. Producers (the connection
/// manager, FCM listener) publish; feature modules subscribe. Swappable for an
/// external broker later without touching producers or consumers.
/// </summary>
public interface IEventBus
{
    /// <summary>Publishes an event to every live subscriber of <typeparamref name="TEvent"/>.</summary>
    ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : notnull;

    /// <summary>
    /// Subscribes to <typeparamref name="TEvent"/>. The returned stream yields every event
    /// published after this call until <paramref name="cancellationToken"/> is cancelled or
    /// enumeration stops.
    /// </summary>
    IAsyncEnumerable<TEvent> Subscribe<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull;
}
```

- [ ] **Step 4: Write the implementation**

`src/RustPlusBot.Abstractions/Events/InMemoryEventBus.cs`:

```csharp
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace RustPlusBot.Abstractions.Events;

/// <summary>An unbounded, in-process <see cref="IEventBus"/> backed by channels per subscriber.</summary>
public sealed class InMemoryEventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, Subscribers> _byType = new();

    /// <inheritdoc />
    public ValueTask PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(@event);
        cancellationToken.ThrowIfCancellationRequested();

        if (_byType.TryGetValue(typeof(TEvent), out var subscribers))
        {
            subscribers.Publish(@event);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TEvent> Subscribe<TEvent>(CancellationToken cancellationToken = default)
        where TEvent : notnull
    {
        var subscribers = _byType.GetOrAdd(typeof(TEvent), static _ => new Subscribers());
        return subscribers.Subscribe<TEvent>(cancellationToken);
    }

    private sealed class Subscribers
    {
        private readonly ConcurrentDictionary<Guid, Channel<object>> _channels = new();

        public void Publish(object @event)
        {
            foreach (var channel in _channels.Values)
            {
                channel.Writer.TryWrite(@event);
            }
        }

        // Registers the channel eagerly (before the iterator runs) so events published
        // immediately after Subscribe() are not missed.
        public IAsyncEnumerable<TEvent> Subscribe<TEvent>(CancellationToken cancellationToken)
        {
            var id = Guid.NewGuid();
            var channel = Channel.CreateUnbounded<object>();
            _channels[id] = channel;
            return Iterate<TEvent>(id, channel, cancellationToken);
        }

        private async IAsyncEnumerable<TEvent> Iterate<TEvent>(
            Guid id,
            Channel<object> channel,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    yield return (TEvent)item;
                }
            }
            finally
            {
                _channels.TryRemove(id, out _);
            }
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter InMemoryEventBusTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add in-process event bus"
```

---

## Task 5: Domain entities and enums

**Files:**
- Create: `src/RustPlusBot.Domain/Servers/RustServer.cs`
- Create: `src/RustPlusBot.Domain/Credentials/PlayerCredential.cs`
- Create: `src/RustPlusBot.Domain/Credentials/CredentialStatus.cs`
- Create: `src/RustPlusBot.Domain/Connections/ConnectionState.cs`
- Create: `src/RustPlusBot.Domain/Guilds/GuildSettings.cs`
- Create: `src/RustPlusBot.Domain/Guilds/ChannelBinding.cs`
- Create: `src/RustPlusBot.Domain/Guilds/BoundFeature.cs`
- Create: `src/RustPlusBot.Domain/Entities/PairedEntity.cs`
- Create: `src/RustPlusBot.Domain/Entities/PairedEntityKind.cs`
- Create: `src/RustPlusBot.Domain/Events/EventSubscription.cs`

This task is plain POCOs/enums — no behavior, so no unit test (verification is the build in Task 6). Create each file exactly:

- [ ] **Step 1: Enums**

`CredentialStatus.cs`:

```csharp
namespace RustPlusBot.Domain.Credentials;

/// <summary>Lifecycle state of a stored player credential within a server's pool.</summary>
public enum CredentialStatus
{
    /// <summary>Eligible to drive the live connection, currently inactive.</summary>
    Standby = 0,

    /// <summary>Currently driving the live connection.</summary>
    Active = 1,

    /// <summary>Rejected by the server (expired/invalid token); excluded from failover.</summary>
    Invalid = 2,
}
```

`BoundFeature.cs`:

```csharp
namespace RustPlusBot.Domain.Guilds;

/// <summary>The bot feature a Discord channel is bound to.</summary>
public enum BoundFeature
{
    /// <summary>Team-chat bridge and in-game commands.</summary>
    Chat = 0,

    /// <summary>Map render and live event notifications.</summary>
    Events = 1,

    /// <summary>Smart switches / alarms / storage monitors.</summary>
    Devices = 2,

    /// <summary>Camera stills and control.</summary>
    Cameras = 3,
}
```

`PairedEntityKind.cs`:

```csharp
namespace RustPlusBot.Domain.Entities;

/// <summary>The kind of paired in-game smart device.</summary>
public enum PairedEntityKind
{
    /// <summary>A smart switch.</summary>
    SmartSwitch = 0,

    /// <summary>A smart alarm.</summary>
    SmartAlarm = 1,

    /// <summary>A storage monitor.</summary>
    StorageMonitor = 2,
}
```

- [ ] **Step 2: RustServer**

`RustServer.cs`:

```csharp
namespace RustPlusBot.Domain.Servers;

/// <summary>A Rust+ server target bound to a Discord guild. Guild-scoped.</summary>
public sealed class RustServer
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Display name shown in Discord.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Server host (ip or dns).</summary>
    public string Ip { get; set; } = string.Empty;

    /// <summary>Rust+ app port.</summary>
    public int Port { get; set; }

    /// <summary>The Discord user who added this server.</summary>
    public ulong AddedByUserId { get; set; }
}
```

- [ ] **Step 3: PlayerCredential**

`PlayerCredential.cs`:

```csharp
namespace RustPlusBot.Domain.Credentials;

/// <summary>
/// One player's Rust+ credentials within a server's pool. Many per (GuildId, RustServerId).
/// The token fields are stored protected at rest (see ICredentialProtector).
/// </summary>
public sealed class PlayerCredential
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this credential can connect to.</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The Discord user who registered this credential.</summary>
    public ulong OwnerUserId { get; set; }

    /// <summary>The player's Steam64 id.</summary>
    public ulong SteamId { get; set; }

    /// <summary>Protected Rust+ player token.</summary>
    public string ProtectedPlayerToken { get; set; } = string.Empty;

    /// <summary>Protected FCM/Expo credential blob (JSON), used by the pairing listener later.</summary>
    public string ProtectedFcmCredentials { get; set; } = string.Empty;

    /// <summary>Pool lifecycle state.</summary>
    public CredentialStatus Status { get; set; } = CredentialStatus.Standby;
}
```

- [ ] **Step 4: ConnectionState, GuildSettings, ChannelBinding, PairedEntity, EventSubscription**

`ConnectionState.cs`:

```csharp
namespace RustPlusBot.Domain.Connections;

/// <summary>Persisted last-known connection state per server, so the active identity survives restarts.</summary>
public sealed class ConnectionState
{
    /// <summary>The server this state belongs to (primary key, one row per server).</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The credential currently selected as active, if any.</summary>
    public Guid? ActiveCredentialId { get; set; }

    /// <summary>Whether the connection was healthy at last check.</summary>
    public bool IsHealthy { get; set; }

    /// <summary>When the state was last updated (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
```

`GuildSettings.cs`:

```csharp
namespace RustPlusBot.Domain.Guilds;

/// <summary>Per-guild configuration. Primary key is the guild snowflake.</summary>
public sealed class GuildSettings
{
    /// <summary>The Discord guild snowflake (primary key).</summary>
    public ulong GuildId { get; set; }

    /// <summary>BCP-47 culture for localized output (e.g. "en", "fr").</summary>
    public string Culture { get; set; } = "en";
}
```

`ChannelBinding.cs`:

```csharp
namespace RustPlusBot.Domain.Guilds;

/// <summary>Maps a Discord channel to a bot feature within a guild.</summary>
public sealed class ChannelBinding
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The bound Discord channel snowflake.</summary>
    public ulong ChannelId { get; set; }

    /// <summary>The feature this channel serves.</summary>
    public BoundFeature Feature { get; set; }
}
```

`PairedEntity.cs`:

```csharp
namespace RustPlusBot.Domain.Entities;

/// <summary>A paired in-game smart device, discovered via FCM pairing (populated in subsystem 1).</summary>
public sealed class PairedEntity
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this entity belongs to.</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The in-game entity id.</summary>
    public ulong EntityId { get; set; }

    /// <summary>The device kind.</summary>
    public PairedEntityKind Kind { get; set; }

    /// <summary>User-facing label.</summary>
    public string Name { get; set; } = string.Empty;
}
```

`EventSubscription.cs`:

```csharp
namespace RustPlusBot.Domain.Events;

/// <summary>A guild's opt-in to a named map/live event (e.g. "CargoShip"). Consumed in subsystem 2.</summary>
public sealed class EventSubscription
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this subscription applies to.</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The event key the guild subscribed to.</summary>
    public string EventKey { get; set; } = string.Empty;
}
```

- [ ] **Step 5: Build the Domain project**

Run: `dotnet build src/RustPlusBot.Domain`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add Rust-domain entities and enums"
```

---

## Task 6: BotDbContext and entity configurations

**Files:**
- Create: `src/RustPlusBot.Persistence/BotDbContext.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/RustServerConfiguration.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/PlayerCredentialConfiguration.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/ChannelBindingConfiguration.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/BotDbContextTests.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/SqliteContextFixture.cs`

- [ ] **Step 1: Write a SQLite test fixture**

`tests/RustPlusBot.Persistence.Tests/SqliteContextFixture.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Persistence;

namespace RustPlusBot.Persistence.Tests;

/// <summary>Creates a BotDbContext over a private in-memory SQLite connection kept open for the test.</summary>
public static class SqliteContextFixture
{
    public static (BotDbContext Context, SqliteConnection Connection) Create()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new BotDbContext(options);
        context.Database.EnsureCreated();
        return (context, connection);
    }
}
```

- [ ] **Step 2: Write the failing test**

`tests/RustPlusBot.Persistence.Tests/BotDbContextTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests;

public sealed class BotDbContextTests
{
    [Fact]
    public async Task RustServer_RoundTrips_WithSnowflakeGuildId()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        using var __ = connection;

        var guildId = 1234567890123456789UL; // larger than long.MaxValue/2; exercises ulong<->long
        context.RustServers.Add(new RustServer
        {
            GuildId = guildId,
            Name = "Main",
            Ip = "127.0.0.1",
            Port = 28082,
            AddedByUserId = 42UL,
        });
        await context.SaveChangesAsync();

        var loaded = await context.RustServers.SingleAsync();
        Assert.Equal(guildId, loaded.GuildId);
        Assert.Equal("Main", loaded.Name);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter BotDbContextTests`
Expected: FAIL — `BotDbContext`/`RustServers` do not exist.

- [ ] **Step 4: Write the context**

`src/RustPlusBot.Persistence/BotDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Persistord.Core;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Entities;
using RustPlusBot.Domain.Events;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence;

/// <summary>
/// The bot's EF Core context. Inherits Persistord's DiscordDbContext for the Discord skeleton and
/// the global ulong&lt;-&gt;long snowflake conversion, and adds the Rust-domain sets.
/// </summary>
public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DiscordDbContext(options)
{
    /// <summary>Configured Rust+ servers.</summary>
    public DbSet<RustServer> RustServers => Set<RustServer>();

    /// <summary>Stored player credentials.</summary>
    public DbSet<PlayerCredential> PlayerCredentials => Set<PlayerCredential>();

    /// <summary>Per-server connection state.</summary>
    public DbSet<ConnectionState> ConnectionStates => Set<ConnectionState>();

    /// <summary>Per-guild settings.</summary>
    public DbSet<GuildSettings> GuildSettings => Set<GuildSettings>();

    /// <summary>Channel-to-feature bindings.</summary>
    public DbSet<ChannelBinding> ChannelBindings => Set<ChannelBinding>();

    /// <summary>Paired smart devices.</summary>
    public DbSet<PairedEntity> PairedEntities => Set<PairedEntity>();

    /// <summary>Per-guild event subscriptions.</summary>
    public DbSet<EventSubscription> EventSubscriptions => Set<EventSubscription>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder); // core skeleton + snowflake convention
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BotDbContext).Assembly);
    }
}
```

- [ ] **Step 5: Write the configurations**

`src/RustPlusBot.Persistence/Configurations/RustServerConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class RustServerConfiguration : IEntityTypeConfiguration<RustServer>
{
    public void Configure(EntityTypeBuilder<RustServer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(128);
        builder.Property(s => s.Ip).IsRequired().HasMaxLength(255);
        builder.HasIndex(s => s.GuildId);
    }
}
```

`src/RustPlusBot.Persistence/Configurations/PlayerCredentialConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class PlayerCredentialConfiguration : IEntityTypeConfiguration<PlayerCredential>
{
    public void Configure(EntityTypeBuilder<PlayerCredential> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.GuildId, c.RustServerId });
        builder.Property(c => c.ProtectedPlayerToken).IsRequired();
    }
}
```

`src/RustPlusBot.Persistence/Configurations/ChannelBindingConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Guilds;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ChannelBindingConfiguration : IEntityTypeConfiguration<ChannelBinding>
{
    public void Configure(EntityTypeBuilder<ChannelBinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(b => b.Id);
        // One channel per feature per guild.
        builder.HasIndex(b => new { b.GuildId, b.Feature }).IsUnique();
    }
}
```

Also set the keyless-but-keyed simple entities by convention: `ConnectionState` needs an explicit key (it has no `Id`). Add `src/RustPlusBot.Persistence/Configurations/ConnectionStateConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Connections;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ConnectionStateConfiguration : IEntityTypeConfiguration<ConnectionState>
{
    public void Configure(EntityTypeBuilder<ConnectionState> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.RustServerId);
    }
}
```

And `GuildSettings` (key is `GuildId`, not `Id`) — add `GuildSettingsConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Guilds;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class GuildSettingsConfiguration : IEntityTypeConfiguration<GuildSettings>
{
    public void Configure(EntityTypeBuilder<GuildSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.GuildId);
        builder.Property(s => s.Culture).IsRequired().HasMaxLength(16);
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter BotDbContextTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add BotDbContext and entity configurations"
```

---

## Task 7: Initial EF migration

**Files:**
- Create: `src/RustPlusBot.Persistence/Migrations/*` (generated)
- Create: `src/RustPlusBot.Persistence/DesignTimeDbContextFactory.cs`

- [ ] **Step 1: Add a design-time factory so `dotnet-ef` can build the context**

`src/RustPlusBot.Persistence/DesignTimeDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RustPlusBot.Persistence;

/// <summary>Lets `dotnet-ef` instantiate the context at design time (migrations only).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BotDbContext>()
            .UseSqlite("DataSource=design-time.db")
            .Options;
        return new BotDbContext(options);
    }
}
```

- [ ] **Step 2: Generate the migration**

```bash
dotnet tool restore
dotnet ef migrations add InitialCreate --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Persistence
```

Expected: a `Migrations/` folder with `*_InitialCreate.cs` + `BotDbContextModelSnapshot.cs`.

- [ ] **Step 3: Verify the migration applies cleanly**

```bash
dotnet ef database update --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Persistence
```

Expected: "Done." and a `design-time.db` file created. Then remove the throwaway db:

```bash
rm src/RustPlusBot.Persistence/design-time.db
```

- [ ] **Step 4: Verify build + tests still pass**

```bash
dotnet build RustPlusBot.slnx && dotnet test tests/RustPlusBot.Persistence.Tests
```

Expected: Build + tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add initial EF migration"
```

---

## Task 8: Credential protector seam

**Files:**
- Create: `src/RustPlusBot.Abstractions/Credentials/ICredentialProtector.cs`
- Create: `src/RustPlusBot.Host/Credentials/DataProtectionCredentialProtector.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Credentials/CredentialProtectorContractTests.cs` (against an inline fake; the DataProtection impl is exercised in Host integration)

- [ ] **Step 1: Write the contract**

`src/RustPlusBot.Abstractions/Credentials/ICredentialProtector.cs`:

```csharp
namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Protects secret credential material at rest (round-trippable on the same machine/key).</summary>
public interface ICredentialProtector
{
    /// <summary>Protects plaintext into an opaque, storable string.</summary>
    string Protect(string plaintext);

    /// <summary>Reverses <see cref="Protect"/>.</summary>
    string Unprotect(string protectedText);
}
```

- [ ] **Step 2: Write the failing test (round-trip contract via a DataProtection-backed protector)**

`tests/RustPlusBot.Persistence.Tests/Credentials/CredentialProtectorContractTests.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Host.Credentials;

namespace RustPlusBot.Persistence.Tests.Credentials;

public sealed class CredentialProtectorContractTests
{
    [Fact]
    public void ProtectThenUnprotect_ReturnsOriginal()
    {
        var provider = DataProtectionProvider.Create(nameof(CredentialProtectorContractTests));
        ICredentialProtector protector = new DataProtectionCredentialProtector(provider);

        const string secret = "player-token-12345";
        var protectedText = protector.Protect(secret);

        Assert.NotEqual(secret, protectedText);
        Assert.Equal(secret, protector.Unprotect(protectedText));
    }
}
```

Add the reference so the test can see the Host type:

```bash
dotnet add tests/RustPlusBot.Persistence.Tests reference src/RustPlusBot.Host
dotnet add tests/RustPlusBot.Persistence.Tests package Microsoft.AspNetCore.DataProtection
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter CredentialProtectorContractTests`
Expected: FAIL — `DataProtectionCredentialProtector` does not exist.

- [ ] **Step 4: Write the implementation**

`src/RustPlusBot.Host/Credentials/DataProtectionCredentialProtector.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using RustPlusBot.Abstractions.Credentials;

namespace RustPlusBot.Host.Credentials;

/// <summary>An <see cref="ICredentialProtector"/> backed by ASP.NET Core Data Protection.</summary>
public sealed class DataProtectionCredentialProtector : ICredentialProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionCredentialProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("RustPlusBot.Credentials.v1");
    }

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        return _protector.Unprotect(protectedText);
    }
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter CredentialProtectorContractTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add Data Protection credential protector"
```

---

## Task 9: Credential store

**Files:**
- Create: `src/RustPlusBot.Abstractions/Credentials/ICredentialStore.cs`
- Create: `src/RustPlusBot.Abstractions/Credentials/StoreCredentialRequest.cs`
- Create: `src/RustPlusBot.Persistence/Credentials/CredentialStore.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Credentials/CredentialStoreTests.cs`

- [ ] **Step 1: Write the contract + request type**

`src/RustPlusBot.Abstractions/Credentials/StoreCredentialRequest.cs`:

```csharp
namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Plaintext inputs to register a credential; tokens are protected by the store before persistence.</summary>
public sealed record StoreCredentialRequest(
    ulong GuildId,
    Guid RustServerId,
    ulong OwnerUserId,
    ulong SteamId,
    string PlayerToken,
    string FcmCredentialsJson);
```

`src/RustPlusBot.Abstractions/Credentials/ICredentialStore.cs`:

```csharp
namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Persists and counts player credentials. Tokens are protected at rest.</summary>
public interface ICredentialStore
{
    /// <summary>Stores a credential (as Standby) and returns its new id.</summary>
    Task<Guid> StoreAsync(StoreCredentialRequest request, CancellationToken cancellationToken = default);

    /// <summary>Counts stored credentials for a server within a guild.</summary>
    Task<int> CountForServerAsync(ulong guildId, Guid rustServerId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing test**

`tests/RustPlusBot.Persistence.Tests/Credentials/CredentialStoreTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;
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

    [Fact]
    public async Task StoreAsync_ProtectsTokenAndPersistsAsStandby()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        using var __ = connection;

        var store = new CredentialStore(context, PassThroughProtector());
        var serverId = Guid.NewGuid();

        var id = await store.StoreAsync(new StoreCredentialRequest(
            GuildId: 10UL,
            RustServerId: serverId,
            OwnerUserId: 99UL,
            SteamId: 76561198000000000UL,
            PlayerToken: "raw-token",
            FcmCredentialsJson: "{}"));

        var saved = await context.PlayerCredentials.SingleAsync();
        Assert.Equal(id, saved.Id);
        Assert.Equal("enc:raw-token", saved.ProtectedPlayerToken);
        Assert.Equal(CredentialStatus.Standby, saved.Status);
    }

    [Fact]
    public async Task CountForServerAsync_CountsOnlyMatchingGuildAndServer()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        using var __ = connection;

        var store = new CredentialStore(context, PassThroughProtector());
        var serverA = Guid.NewGuid();
        var serverB = Guid.NewGuid();

        await store.StoreAsync(new StoreCredentialRequest(10UL, serverA, 1UL, 1UL, "t1", "{}"));
        await store.StoreAsync(new StoreCredentialRequest(10UL, serverA, 2UL, 2UL, "t2", "{}"));
        await store.StoreAsync(new StoreCredentialRequest(10UL, serverB, 3UL, 3UL, "t3", "{}"));
        await store.StoreAsync(new StoreCredentialRequest(20UL, serverA, 4UL, 4UL, "t4", "{}"));

        Assert.Equal(2, await store.CountForServerAsync(10UL, serverA));
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter CredentialStoreTests`
Expected: FAIL — `CredentialStore` does not exist.

- [ ] **Step 4: Write the implementation**

`src/RustPlusBot.Persistence/Credentials/CredentialStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Domain.Credentials;

namespace RustPlusBot.Persistence.Credentials;

/// <summary>EF-backed <see cref="ICredentialStore"/> that protects tokens before persisting.</summary>
public sealed class CredentialStore(BotDbContext context, ICredentialProtector protector) : ICredentialStore
{
    /// <inheritdoc />
    public async Task<Guid> StoreAsync(StoreCredentialRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var credential = new PlayerCredential
        {
            GuildId = request.GuildId,
            RustServerId = request.RustServerId,
            OwnerUserId = request.OwnerUserId,
            SteamId = request.SteamId,
            ProtectedPlayerToken = protector.Protect(request.PlayerToken),
            ProtectedFcmCredentials = protector.Protect(request.FcmCredentialsJson),
            Status = CredentialStatus.Standby,
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

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter CredentialStoreTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add EF-backed credential store"
```

---

## Task 10: Server service (guild-scoped add/list/remove)

**Files:**
- Create: `src/RustPlusBot.Persistence/Servers/ServerService.cs`
- Create: `src/RustPlusBot.Persistence/Servers/IServerService.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Servers/ServerServiceTests.cs`

- [ ] **Step 1: Write the contract**

`src/RustPlusBot.Persistence/Servers/IServerService.cs`:

```csharp
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Servers;

/// <summary>Guild-scoped management of Rust+ server targets.</summary>
public interface IServerService
{
    /// <summary>Adds a server to a guild and returns it.</summary>
    Task<RustServer> AddAsync(ulong guildId, ulong addedByUserId, string name, string ip, int port,
        CancellationToken cancellationToken = default);

    /// <summary>Lists a guild's servers, ordered by name.</summary>
    Task<IReadOnlyList<RustServer>> ListAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Removes a server by id within a guild. Returns true if a row was removed.</summary>
    Task<bool> RemoveAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing tests**

`tests/RustPlusBot.Persistence.Tests/Servers/ServerServiceTests.cs`:

```csharp
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Persistence.Tests.Servers;

public sealed class ServerServiceTests
{
    [Fact]
    public async Task AddAsync_PersistsServerScopedToGuild()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        using var __ = connection;
        var service = new ServerService(context);

        var server = await service.AddAsync(10UL, 99UL, "Main", "127.0.0.1", 28082);

        Assert.NotEqual(Guid.Empty, server.Id);
        var listed = await service.ListAsync(10UL);
        Assert.Single(listed);
        Assert.Equal("Main", listed[0].Name);
    }

    [Fact]
    public async Task ListAsync_DoesNotLeakAcrossGuilds()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        using var __ = connection;
        var service = new ServerService(context);

        await service.AddAsync(10UL, 1UL, "A", "1.1.1.1", 1);
        await service.AddAsync(20UL, 1UL, "B", "2.2.2.2", 2);

        var guild10 = await service.ListAsync(10UL);
        Assert.Single(guild10);
        Assert.Equal("A", guild10[0].Name);
    }

    [Fact]
    public async Task RemoveAsync_OnlyRemovesWithinGuild()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        using var __ = connection;
        var service = new ServerService(context);

        var server = await service.AddAsync(10UL, 1UL, "A", "1.1.1.1", 1);

        Assert.False(await service.RemoveAsync(20UL, server.Id)); // wrong guild
        Assert.True(await service.RemoveAsync(10UL, server.Id));
        Assert.Empty(await service.ListAsync(10UL));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ServerServiceTests`
Expected: FAIL — `ServerService` does not exist.

- [ ] **Step 4: Write the implementation**

`src/RustPlusBot.Persistence/Servers/ServerService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Servers;

/// <inheritdoc />
public sealed class ServerService(BotDbContext context) : IServerService
{
    /// <inheritdoc />
    public async Task<RustServer> AddAsync(
        ulong guildId,
        ulong addedByUserId,
        string name,
        string ip,
        int port,
        CancellationToken cancellationToken = default)
    {
        var server = new RustServer
        {
            GuildId = guildId,
            AddedByUserId = addedByUserId,
            Name = name,
            Ip = ip,
            Port = port,
        };

        context.RustServers.Add(server);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return server;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RustServer>> ListAsync(
        ulong guildId,
        CancellationToken cancellationToken = default) =>
        await context.RustServers
            .Where(s => s.GuildId == guildId)
            .OrderBy(s => s.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var server = await context.RustServers
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.Id == serverId, cancellationToken)
            .ConfigureAwait(false);

        if (server is null)
        {
            return false;
        }

        context.RustServers.Remove(server);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ServerServiceTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add guild-scoped server service"
```

---

## Task 11: Binding service (channel ↔ feature)

**Files:**
- Create: `src/RustPlusBot.Persistence/Bindings/IBindingService.cs`
- Create: `src/RustPlusBot.Persistence/Bindings/BindingService.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Bindings/BindingServiceTests.cs`

- [ ] **Step 1: Write the contract**

`src/RustPlusBot.Persistence/Bindings/IBindingService.cs`:

```csharp
using RustPlusBot.Domain.Guilds;

namespace RustPlusBot.Persistence.Bindings;

/// <summary>Guild-scoped management of channel-to-feature bindings (one channel per feature per guild).</summary>
public interface IBindingService
{
    /// <summary>Binds (or re-binds) a feature to a channel within a guild.</summary>
    Task BindAsync(ulong guildId, BoundFeature feature, ulong channelId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the channel bound to a feature, or null if unbound.</summary>
    Task<ulong?> GetBoundChannelAsync(ulong guildId, BoundFeature feature,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 2: Write the failing tests**

`tests/RustPlusBot.Persistence.Tests/Bindings/BindingServiceTests.cs`:

```csharp
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Persistence.Bindings;

namespace RustPlusBot.Persistence.Tests.Bindings;

public sealed class BindingServiceTests
{
    [Fact]
    public async Task BindAsync_StoresChannelForFeature()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        using var __ = connection;
        var service = new BindingService(context);

        await service.BindAsync(10UL, BoundFeature.Chat, 555UL);

        Assert.Equal(555UL, await service.GetBoundChannelAsync(10UL, BoundFeature.Chat));
        Assert.Null(await service.GetBoundChannelAsync(10UL, BoundFeature.Events));
    }

    [Fact]
    public async Task BindAsync_RebindingFeature_ReplacesChannel()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        using var __ = connection;
        var service = new BindingService(context);

        await service.BindAsync(10UL, BoundFeature.Chat, 555UL);
        await service.BindAsync(10UL, BoundFeature.Chat, 777UL);

        Assert.Equal(777UL, await service.GetBoundChannelAsync(10UL, BoundFeature.Chat));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter BindingServiceTests`
Expected: FAIL — `BindingService` does not exist.

- [ ] **Step 4: Write the implementation**

`src/RustPlusBot.Persistence/Bindings/BindingService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Guilds;

namespace RustPlusBot.Persistence.Bindings;

/// <inheritdoc />
public sealed class BindingService(BotDbContext context) : IBindingService
{
    /// <inheritdoc />
    public async Task BindAsync(
        ulong guildId,
        BoundFeature feature,
        ulong channelId,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.ChannelBindings
            .SingleOrDefaultAsync(b => b.GuildId == guildId && b.Feature == feature, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            context.ChannelBindings.Add(new ChannelBinding
            {
                GuildId = guildId,
                Feature = feature,
                ChannelId = channelId,
            });
        }
        else
        {
            existing.ChannelId = channelId;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ulong?> GetBoundChannelAsync(
        ulong guildId,
        BoundFeature feature,
        CancellationToken cancellationToken = default)
    {
        var binding = await context.ChannelBindings
            .SingleOrDefaultAsync(b => b.GuildId == guildId && b.Feature == feature, cancellationToken)
            .ConfigureAwait(false);

        return binding?.ChannelId;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter BindingServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add guild-scoped channel binding service"
```

---

## Task 12: Persistence DI registration

**Files:**
- Create: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/PersistenceRegistrationTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/RustPlusBot.Persistence.Tests/PersistenceRegistrationTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Persistence.Tests;

public sealed class PersistenceRegistrationTests
{
    [Fact]
    public void AddBotPersistence_RegistersDbContextFactoryAndServices()
    {
        var services = new ServiceCollection();
        services.AddBotPersistence("DataSource=:memory:");

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IServerService>());
        Assert.NotNull(provider.GetService<IDbContextFactory<BotDbContext>>());
    }
}
```

(Add `using Microsoft.EntityFrameworkCore;` for `IDbContextFactory`.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter PersistenceRegistrationTests`
Expected: FAIL — `AddBotPersistence` does not exist.

- [ ] **Step 3: Write the implementation**

`src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Persistence.Bindings;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Persistence;

/// <summary>DI registration for the persistence layer.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>Registers the BotDbContext factory and the guild-scoped services over SQLite.</summary>
    public static IServiceCollection AddBotPersistence(this IServiceCollection services, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDbContextFactory<BotDbContext>(options => options.UseSqlite(connectionString));

        // AddDbContextFactory registers only the singleton factory, not a scoped context.
        // Register a scoped BotDbContext sourced from the factory so the scoped services below
        // (which take BotDbContext directly) resolve, while the factory remains available for
        // the startup migration in Program.cs.
        services.AddScoped<BotDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<BotDbContext>>().CreateDbContext());

        services.AddScoped<IServerService, ServerService>();
        services.AddScoped<IBindingService, BindingService>();
        services.AddScoped<ICredentialStore, CredentialStore>();

        return services;
    }
}
```

The interaction modules (Task 14) create an `AsyncScope` per command, so the scoped `BotDbContext` and services are created and disposed per unit of work — matching Persistord's "short-lived contexts" guidance.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter PersistenceRegistrationTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add persistence DI registration"
```

---

## Task 13: Discord options and bot host service

**Files:**
- Create: `src/RustPlusBot.Discord/DiscordOptions.cs`
- Create: `src/RustPlusBot.Discord/DiscordBotService.cs`
- Create: `src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs`

No unit test (it drives the live Discord gateway; verified by running the Host in Task 15). Keep it minimal and correct.

- [ ] **Step 1: Options type**

`src/RustPlusBot.Discord/DiscordOptions.cs`:

```csharp
namespace RustPlusBot.Discord;

/// <summary>Discord connection configuration, bound from the "Discord" config section.</summary>
public sealed class DiscordOptions
{
    /// <summary>The bot token.</summary>
    public string Token { get; set; } = string.Empty;
}
```

- [ ] **Step 2: The hosted service**

`src/RustPlusBot.Discord/DiscordBotService.cs`:

```csharp
using System.Reflection;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace RustPlusBot.Discord;

/// <summary>
/// Hosted service that logs the bot in, loads interaction modules, and registers slash commands
/// per guild on join/ready. Commands register per-guild (instant) rather than globally.
/// </summary>
public sealed class DiscordBotService(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    IOptions<DiscordOptions> options,
    ILogger<DiscordBotService> logger) : IHostedService
{
    private readonly DiscordOptions _options = options.Value;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        client.Log += OnLog;
        interactions.Log += OnLog;
        client.Ready += OnReadyAsync;
        client.InteractionCreated += OnInteractionCreatedAsync;
        client.JoinedGuild += OnJoinedGuildAsync;

        await interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services).ConfigureAwait(false);
        await client.LoginAsync(TokenType.Bot, _options.Token).ConfigureAwait(false);
        await client.StartAsync().ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.LogoutAsync().ConfigureAwait(false);
        await client.StopAsync().ConfigureAwait(false);
    }

    private async Task OnReadyAsync()
    {
        foreach (var guild in client.Guilds)
        {
            await interactions.RegisterCommandsToGuildAsync(guild.Id).ConfigureAwait(false);
        }

        logger.LogInformation("Registered commands to {GuildCount} guild(s).", client.Guilds.Count);
    }

    private Task OnJoinedGuildAsync(SocketGuild guild) =>
        interactions.RegisterCommandsToGuildAsync(guild.Id);

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        var context = new SocketInteractionContext(client, interaction);
        await interactions.ExecuteCommandAsync(context, services).ConfigureAwait(false);
    }

    private Task OnLog(LogMessage message)
    {
        logger.Log(ToLogLevel(message.Severity), message.Exception, "[{Source}] {Message}", message.Source, message.Message);
        return Task.CompletedTask;
    }

    private static LogLevel ToLogLevel(LogSeverity severity) => severity switch
    {
        LogSeverity.Critical => LogLevel.Critical,
        LogSeverity.Error => LogLevel.Error,
        LogSeverity.Warning => LogLevel.Warning,
        LogSeverity.Info => LogLevel.Information,
        LogSeverity.Verbose => LogLevel.Debug,
        LogSeverity.Debug => LogLevel.Trace,
        _ => LogLevel.Information,
    };
}
```

- [ ] **Step 3: The DI registration**

`src/RustPlusBot.Discord/DiscordServiceCollectionExtensions.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RustPlusBot.Discord;

/// <summary>DI registration for the Discord layer.</summary>
public static class DiscordServiceCollectionExtensions
{
    /// <summary>Registers the socket client, interaction service, and the hosted bot service.</summary>
    public static IServiceCollection AddDiscordBot(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        var socketConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
            AlwaysDownloadUsers = false,
        };

        services.AddSingleton(socketConfig);
        services.AddSingleton<DiscordSocketClient>();
        services.AddSingleton(sp => new InteractionService(
            sp.GetRequiredService<DiscordSocketClient>(),
            new InteractionServiceConfig { DefaultRunMode = RunMode.Async }));
        services.AddHostedService<DiscordBotService>();

        return services;
    }
}
```

- [ ] **Step 4: Build the Discord project**

Run: `dotnet build src/RustPlusBot.Discord`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add Discord bot hosted service and registration"
```

---

## Task 14: `/server` and `/bind` interaction modules

**Files:**
- Create: `src/RustPlusBot.Discord/Modules/ServerModule.cs`
- Create: `src/RustPlusBot.Discord/Modules/BindModule.cs`

These are thin shells delegating to the services. They run against the live gateway, so they aren't unit-tested here; the service logic they call is already covered by Tasks 10–11. Create a scope per interaction so the scoped `BotDbContext`/services resolve.

- [ ] **Step 1: ServerModule**

`src/RustPlusBot.Discord/Modules/ServerModule.cs`:

```csharp
using System.Globalization;
using System.Text;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Discord.Modules;

/// <summary>Guild-scoped management of Rust+ servers.</summary>
[Group("server", "Manage this guild's Rust+ servers")]
public sealed class ServerModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("add", "Add a Rust+ server to this guild")]
    public async Task AddAsync(
        [Summary("name", "Display name")] string name,
        [Summary("ip", "Server host or ip")] string ip,
        [Summary("port", "Rust+ app port")] int port)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IServerService>();
        var server = await service.AddAsync(Context.Guild.Id, Context.User.Id, name, ip, port).ConfigureAwait(false);

        await RespondAsync($"Added **{server.Name}** (`{server.Id}`).", ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("list", "List this guild's Rust+ servers")]
    public async Task ListAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IServerService>();
        var servers = await service.ListAsync(Context.Guild.Id).ConfigureAwait(false);

        if (servers.Count == 0)
        {
            await RespondAsync("No servers configured.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var builder = new StringBuilder();
        foreach (var server in servers)
        {
            builder.AppendLine(CultureInfo.InvariantCulture, $"• **{server.Name}** — `{server.Ip}:{server.Port}` (`{server.Id}`)");
        }

        await RespondAsync(builder.ToString(), ephemeral: true).ConfigureAwait(false);
    }

    [SlashCommand("remove", "Remove a Rust+ server by id")]
    public async Task RemoveAsync([Summary("id", "The server id from /server list")] string id)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(id, out var serverId))
        {
            await RespondAsync("That is not a valid server id.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IServerService>();
        var removed = await service.RemoveAsync(Context.Guild.Id, serverId).ConfigureAwait(false);

        await RespondAsync(removed ? "Removed." : "No matching server found.", ephemeral: true).ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: BindModule**

`src/RustPlusBot.Discord/Modules/BindModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Persistence.Bindings;

namespace RustPlusBot.Discord.Modules;

/// <summary>Binds a Discord channel to a bot feature.</summary>
public sealed class BindModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("bind", "Bind a channel to a bot feature")]
    public async Task BindAsync(
        [Summary("feature", "Which feature this channel serves")] BoundFeature feature,
        [Summary("channel", "Target text channel")] ITextChannel channel)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IBindingService>();
        await service.BindAsync(Context.Guild.Id, feature, channel.Id).ConfigureAwait(false);

        await RespondAsync($"Bound **{feature}** to <#{channel.Id}>.", ephemeral: true).ConfigureAwait(false);
    }
}
```

(`BoundFeature` is an enum, so Discord.Net renders it as a choice list automatically.)

- [ ] **Step 3: Build the Discord project**

Run: `dotnet build src/RustPlusBot.Discord`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: add /server and /bind interaction modules"
```

---

## Task 15: Host wiring and configuration

**Files:**
- Modify: `src/RustPlusBot.Host/Program.cs`
- Create: `src/RustPlusBot.Host/appsettings.json`
- Delete: `src/RustPlusBot.Host/Worker.cs` (from the worker template, if present)

- [ ] **Step 1: Remove the template worker**

```bash
rm -f src/RustPlusBot.Host/Worker.cs
```

- [ ] **Step 2: Configuration file**

`src/RustPlusBot.Host/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Discord": {
    "Token": ""
  },
  "Database": {
    "ConnectionString": "DataSource=rustplusbot.db"
  }
}
```

- [ ] **Step 3: Program.cs**

`src/RustPlusBot.Host/Program.cs`:

```csharp
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Discord;
using RustPlusBot.Host.Credentials;
using RustPlusBot.Persistence;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<DiscordOptions>(builder.Configuration.GetSection("Discord"));

var connectionString = builder.Configuration.GetSection("Database")["ConnectionString"]
    ?? "DataSource=rustplusbot.db";

builder.Services.AddDataProtection();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddSingleton<ICredentialProtector, DataProtectionCredentialProtector>();
builder.Services.AddBotPersistence(connectionString);
builder.Services.AddDiscordBot();

var host = builder.Build();

// Apply migrations at startup.
using (var scope = host.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BotDbContext>>();
    await using var db = await factory.CreateDbContextAsync();
    await db.Database.MigrateAsync();
}

await host.RunAsync();
```

- [ ] **Step 4: Build the whole solution**

Run: `dotnet build RustPlusBot.slnx`
Expected: Build succeeded, 0 errors, 0 warnings (warnings are errors).

- [ ] **Step 5: Run the host without a token to confirm clean startup/shutdown wiring**

```bash
DOTNET_ENVIRONMENT=Development dotnet run --project src/RustPlusBot.Host &
sleep 8
kill %1
```

Expected: the host starts, applies migrations (creates `rustplusbot.db`), then Discord login fails clearly because the token is empty (a logged Discord error, not an unhandled crash of the host process). Confirm `src/RustPlusBot.Host/rustplusbot.db` (or repo-root `rustplusbot.db`, depending on working dir) was created. Then:

```bash
rm -f rustplusbot.db src/RustPlusBot.Host/rustplusbot.db
```

- [ ] **Step 6: Run the full test suite**

```bash
dotnet test RustPlusBot.slnx
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: wire Generic Host, config, DI, and startup migration"
```

---

## Task 16: README and manual smoke test

**Files:**
- Create: `README.md` (or update if present)
- Create: `docs/development/running-locally.md`

- [ ] **Step 1: Document local run**

`docs/development/running-locally.md`:

```markdown
# Running locally

1. Create a Discord application + bot, copy the token.
2. Set the token without committing it:

   ```bash
   dotnet user-secrets init --project src/RustPlusBot.Host
   dotnet user-secrets set "Discord:Token" "<your-bot-token>" --project src/RustPlusBot.Host
   ```

3. Invite the bot to a test guild with the `applications.commands` and `bot` scopes.
4. Run:

   ```bash
   dotnet run --project src/RustPlusBot.Host
   ```

5. In the guild, use `/server add`, `/server list`, `/server remove`, and `/bind`.
   The SQLite database is created at `rustplusbot.db` in the working directory.
```

- [ ] **Step 2: Enable user-secrets in the Host csproj**

Add a `<UserSecretsId>` to `src/RustPlusBot.Host/RustPlusBot.Host.csproj` inside the main `<PropertyGroup>`:

```xml
<UserSecretsId>rustplusbot-host</UserSecretsId>
```

`Host.CreateApplicationBuilder` loads user-secrets automatically in the Development environment, so no code change is needed.

- [ ] **Step 3: Manual smoke test (human-run, not automatable)**

With a real token set via user-secrets and the bot invited to a test guild:

1. `dotnet run --project src/RustPlusBot.Host`
2. Confirm the log says "Registered commands to N guild(s)."
3. In Discord: `/server add name:Main ip:127.0.0.1 port:28082` → expect an ephemeral "Added **Main**".
4. `/server list` → shows the server.
5. `/bind feature:Chat channel:#some-channel` → expect "Bound **Chat**".
6. `/server remove id:<the id>` → expect "Removed."

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "docs: add local run guide and enable user-secrets"
```

---

## Self-review notes (coverage against the spec)

- **§4.1 project layout** → Tasks 1, 5, 6, 13 (the `RustPlusBot.RustPlus` project is intentionally deferred to subsystem 1; recorded in the scope boundary).
- **§4.2 schema** → Tasks 5–7 (all entities, configurations, migration). `PairedEntity`/`EventSubscription`/`ConnectionState` are created now as schema even though populated later.
- **§4.3 connection core** → deferred to subsystem 1 (scope boundary). The `ConnectionState` table + `CredentialStatus`/`PlayerCredential` schema it needs are created here.
- **§4.4 Discord interaction layer** → Tasks 13–14. `/setup` and `/identity` are deferred to subsystem 1 (they require a live connection to validate); `/server` and `/bind` ship now.
- **§4.5 event bus** → Task 4.
- **§4.6 cross-cutting**: secrets → Tasks 8–9; logging → Task 13; i18n → `GuildSettings.Culture` (Task 5) is seeded; the resx localizer is deliberately deferred (YAGNI until a feature emits user-facing strings — noted as a gap to revisit in subsystem 3). Testing → MockServer applies only once the connection layer exists (subsystem 1).

**Known intentional deferrals (not gaps):** live connection, `/setup`, FCM, RustPlusApi package reference, resx localizer.
