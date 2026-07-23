# Player Events Channel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route team-player presence transitions (login, logout, death, AFK) to a new per-server `#player-events` Discord channel, leaving map events and wipe announcements in `#events` and in-game team chat untouched.

**Architecture:** Channels in this codebase are declarative — `ServerWorkspaceSpecProvider` returns `ChannelSpec` records and `WorkspaceReconciler` creates/orders whatever is declared. Each consumer resolves its channel through a singleton locator subclassing `CachingChannelLocator`. We add one channel key, one `ChannelSpec`, two localized names, one locator interface + implementation, and swap the single constructor dependency in `PlayerEventRelay`.

**Tech Stack:** C# / .NET 10, Discord.Net, EF Core (SQLite), xUnit + NSubstitute, `dtk` (DotnetTokenKiller) as the `dotnet` wrapper.

**Spec:** `docs/superpowers/specs/2026-07-22-player-events-channel-design.md`

## Global Constraints

- `TreatWarningsAsErrors=true` and `GenerateDocumentationFile=true` (`Directory.Build.props`) — **every** public and internal type/member you add needs an XML doc comment, including `<param>` for every primary-constructor parameter, or the build fails.
- Always build/test with `dtk dotnet ...`, never raw `dotnet ...`.
- Channel key string is exactly `playerevents` (lowercase, no separator) — matches the `storagemonitors` convention.
- Localized channel names: English `player-events`, French `evenements-joueurs`.
- New channel position is `5`; `map`, `switches`, `alarms`, `storagemonitors` shift to `6`, `7`, `8`, `9`. `events` stays at `4`.
- Do **not** modify `EventRelay`, `WipeAnnouncer`, or any in-game team-chat send path.

## File Structure

| File | Responsibility | Task |
|---|---|---|
| `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs` (modify) | Add `ServerPlayerEvents` constant | 1 |
| `src/RustPlusBot.Localization/Strings.resx` (modify) | English channel name | 1 |
| `src/RustPlusBot.Localization/Strings.fr.resx` (modify) | French channel name | 1 |
| `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs` (modify) | Declare the channel at position 5 | 1 |
| `src/RustPlusBot.Features.Workspace/Locating/IPlayerEventChannelLocator.cs` (create) | Locator seam for `#player-events` | 2 |
| `src/RustPlusBot.Features.Workspace/Locating/PlayerEventChannelLocator.cs` (create) | TTL-cached resolution of the channel id | 2 |
| `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs` (modify) | DI registration | 2 |
| `src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs` (modify) | Route embeds to the new locator | 3 |
| `tests/RustPlusBot.Features.Workspace.Tests/Specs/ServerChannelSpecTests.cs` (create) | Assert spec content + ordering | 1 |
| `tests/RustPlusBot.Features.Workspace.Tests/Locating/PlayerEventChannelLocatorTests.cs` (create) | Locator reads the `playerevents` key | 2 |
| `tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs` (modify) | Assert DI registration | 2 |
| `tests/RustPlusBot.Features.Players.Tests/PlayerEventRelayTests.cs` (modify) | Relay uses the player-events locator | 3 |
| `tests/RustPlusBot.Features.Players.Tests/Hosting/PlayersHostedServiceTests.cs` (modify) | Compile against the new type | 3 |
| `tests/RustPlusBot.Features.Players.Tests/PlayerEventRegistrationTests.cs` (modify) | DI graph resolves | 3 |
| `README.md` (modify) | Document the new channel | 4 |

Task order matters: Task 1 declares the channel, Task 2 builds the way to find it, Task 3 points the relay at it. Each task ends green and committable.

---

### Task 1: Declare the #player-events channel

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`
- Modify: `src/RustPlusBot.Localization/Strings.fr.resx`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Specs/ServerChannelSpecTests.cs` (create)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `WorkspaceChannelKeys.ServerPlayerEvents` (`internal const string` = `"playerevents"`), used by Task 2. Resource key `channel.playerevents.name`.

**Background:** `ChannelSpec` is an `internal sealed record` with positional members
`(WorkspaceScope Scope, string Key, string NameKey, ChannelPermissionProfile Permissions, int Order, string? Capability = null)`. Note the property names are `Key`, `NameKey` and `Order` — not `ChannelKey`/`NameResourceKey`/`Position`. `ChannelSpec`, `WorkspaceChannelKeys` and `ServerWorkspaceSpecProvider` are all `internal`; the Workspace test project reaches them via `InternalsVisibleTo` (declared in `RustPlusBot.Features.Workspace.csproj`), so the test can name them directly.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Specs/ServerChannelSpecTests.cs`:

```csharp
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Specs;

namespace RustPlusBot.Features.Workspace.Tests.Specs;

/// <summary>Locks the per-server channel set and its on-screen ordering.</summary>
public sealed class ServerChannelSpecTests
{
    private static readonly ChannelSpec[] Specs = [.. new ServerWorkspaceSpecProvider().GetChannelSpecs()];

    [Fact]
    public void Declares_player_events_read_only_right_after_events()
    {
        var playerEvents = Assert.Single(Specs, s => s.Key == WorkspaceChannelKeys.ServerPlayerEvents);

        Assert.Equal("playerevents", WorkspaceChannelKeys.ServerPlayerEvents);
        Assert.Equal("channel.playerevents.name", playerEvents.NameKey);
        Assert.Equal(ChannelPermissionProfile.ReadOnly, playerEvents.Permissions);
        Assert.Equal(WorkspaceScope.PerServer, playerEvents.Scope);
        Assert.Null(playerEvents.Capability);
    }

    [Fact]
    public void Orders_player_events_between_events_and_map()
    {
        var keysInOrder = Specs.OrderBy(s => s.Order).Select(s => s.Key).ToArray();

        Assert.Equal(
        [
            WorkspaceChannelKeys.ServerInfo,
            WorkspaceChannelKeys.ServerTeamChat,
            WorkspaceChannelKeys.ServerClanChat,
            WorkspaceChannelKeys.ServerClanInfo,
            WorkspaceChannelKeys.ServerEvents,
            WorkspaceChannelKeys.ServerPlayerEvents,
            WorkspaceChannelKeys.ServerMap,
            WorkspaceChannelKeys.ServerSwitches,
            WorkspaceChannelKeys.ServerAlarms,
            WorkspaceChannelKeys.ServerStorageMonitors,
        ], keysInOrder);
    }

    [Fact]
    public void Assigns_a_unique_order_to_every_channel()
    {
        Assert.Equal(Specs.Length, Specs.Select(s => s.Order).Distinct().Count());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dtk dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile with `'WorkspaceChannelKeys' does not contain a definition for 'ServerPlayerEvents'`.

- [ ] **Step 3: Add the channel key**

In `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`, immediately after the `ServerEvents` constant:

```csharp
    /// <summary>Key for the per-server #player-events channel (team presence: join, leave, death, AFK).</summary>
    public const string ServerPlayerEvents = "playerevents";
```

- [ ] **Step 4: Declare the spec and shift positions**

In `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`, replace the tail of `GetChannelSpecs()` from the `ServerEvents` entry onwards with:

```csharp
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerEvents, "channel.events.name",
            ChannelPermissionProfile.ReadOnly, 4),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerPlayerEvents, "channel.playerevents.name",
            ChannelPermissionProfile.ReadOnly, 5),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerMap, "channel.map.name",
            ChannelPermissionProfile.ReadOnly, 6),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerSwitches, "channel.switches.name",
            ChannelPermissionProfile.Interactive, 7),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerAlarms, "channel.alarms.name",
            ChannelPermissionProfile.Interactive, 8),
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerStorageMonitors, "channel.storagemonitors.name",
            ChannelPermissionProfile.Interactive, 9),
    ];
```

- [ ] **Step 5: Add the localized channel names**

In `src/RustPlusBot.Localization/Strings.resx`, next to the `channel.events.name` entry (around line 93):

```xml
  <data name="channel.playerevents.name" xml:space="preserve">
    <value>player-events</value>
  </data>
```

In `src/RustPlusBot.Localization/Strings.fr.resx`, at the matching place:

```xml
  <data name="channel.playerevents.name" xml:space="preserve">
    <value>evenements-joueurs</value>
  </data>
```

Keep `.resx` entries in the same relative position in both files so diffs stay readable.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: PASS, all tests green (the three new ones included).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs \
        src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs \
        src/RustPlusBot.Localization/Strings.resx \
        src/RustPlusBot.Localization/Strings.fr.resx \
        tests/RustPlusBot.Features.Workspace.Tests/Specs/ServerChannelSpecTests.cs
git commit -m "feat(workspace): declare the per-server #player-events channel"
```

---

### Task 2: Add the player-events channel locator

**Files:**
- Create: `src/RustPlusBot.Features.Workspace/Locating/IPlayerEventChannelLocator.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/PlayerEventChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Locating/PlayerEventChannelLocatorTests.cs` (create)
- Test: `tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs` (modify)

**Interfaces:**
- Consumes: `WorkspaceChannelKeys.ServerPlayerEvents` from Task 1.
- Produces: `public interface IPlayerEventChannelLocator` with
  `Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)`, registered as a singleton. Task 3 injects it.

**Background:** `CachingChannelLocator` is an `internal abstract` class taking `(IServiceScopeFactory scopeFactory, IClock clock, string channelKey)`; it already implements `GetChannelIdAsync` with a 30-second TTL over `IWorkspaceStore.GetChannelsByKeyAsync`. The concrete locator is therefore a one-line subclass — do not reimplement caching. The interface must be `public` (consumed from other feature assemblies); the class is `internal sealed`.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Locating/PlayerEventChannelLocatorTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Locating;

/// <summary>Covers that the locator resolves the "playerevents" key and not the "events" key.</summary>
public sealed class PlayerEventChannelLocatorTests
{
    private static (PlayerEventChannelLocator Locator, ServiceProvider Provider, string ConnectionString)
        CreateLocator()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var cs = $"DataSource=player-event-locator-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(cs);
        keepAlive.Open();
        using (var seed = new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options))
        {
            seed.Database.Migrate();
        }

        var services = new ServiceCollection();
        services.AddSingleton(keepAlive);
        services.AddSingleton(clock);
        services.AddScoped(_ => new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options));
        services.AddScoped<IWorkspaceStore, WorkspaceStore>();
        var provider = services.BuildServiceProvider();

        var locator = new PlayerEventChannelLocator(provider.GetRequiredService<IServiceScopeFactory>(), clock);
        return (locator, provider, cs);
    }

    private static async Task<Guid> SeedAsync(string connectionString)
    {
        await using var context =
            new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options);

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            ChannelKey = WorkspaceChannelKeys.ServerPlayerEvents,
            DiscordChannelId = 777UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            ChannelKey = WorkspaceChannelKeys.ServerEvents,
            DiscordChannelId = 888UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        return server.Id;
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_the_player_events_channel_not_the_events_channel()
    {
        var (locator, provider, cs) = CreateLocator();
        await using var _p = provider;
        var serverId = await SeedAsync(cs);

        var channelId = await locator.GetChannelIdAsync(10UL, serverId, CancellationToken.None);

        Assert.Equal(777UL, channelId);
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_null_when_not_provisioned()
    {
        var (locator, provider, _) = CreateLocator();
        await using var _p = provider;

        Assert.Null(await locator.GetChannelIdAsync(10UL, Guid.NewGuid(), CancellationToken.None));
    }
}
```

The TTL/refresh behaviour is already covered by `CachingChannelLocatorTests` and `EventChannelLocatorTests` — do not duplicate it here.

- [ ] **Step 2: Add the DI registration assertion**

In `tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs`, next to the existing
`Assert.Contains(services, d => d.ServiceType == typeof(IEventChannelLocator));` (around line 53), add:

```csharp
        Assert.Contains(services, d => d.ServiceType == typeof(IPlayerEventChannelLocator));
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile — `The type or namespace name 'PlayerEventChannelLocator' could not be found` and the same for `IPlayerEventChannelLocator`.

- [ ] **Step 4: Create the interface**

`src/RustPlusBot.Features.Workspace/Locating/IPlayerEventChannelLocator.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the per-server #player-events channel (game-to-Discord direction only).</summary>
public interface IPlayerEventChannelLocator
{
    /// <summary>Gets the Discord channel id of #player-events for (<paramref name="guildId"/>, <paramref name="serverId"/>), or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

- [ ] **Step 5: Create the locator**

`src/RustPlusBot.Features.Workspace/Locating/PlayerEventChannelLocator.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the #player-events channel id for a (guild, server).</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class PlayerEventChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : CachingChannelLocator(scopeFactory, clock, WorkspaceChannelKeys.ServerPlayerEvents),
        IPlayerEventChannelLocator;
```

- [ ] **Step 6: Register it in DI**

In `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`, in the channel-locator block, directly under the `IEventChannelLocator` line:

```csharp
        services.AddSingleton<IPlayerEventChannelLocator, PlayerEventChannelLocator>();
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: PASS, all green.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Workspace/Locating/IPlayerEventChannelLocator.cs \
        src/RustPlusBot.Features.Workspace/Locating/PlayerEventChannelLocator.cs \
        src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs \
        tests/RustPlusBot.Features.Workspace.Tests/Locating/PlayerEventChannelLocatorTests.cs \
        tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs
git commit -m "feat(workspace): add the #player-events channel locator"
```

---

### Task 3: Route player events to #player-events

**Files:**
- Modify: `src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs`
- Test: `tests/RustPlusBot.Features.Players.Tests/PlayerEventRelayTests.cs`
- Test: `tests/RustPlusBot.Features.Players.Tests/Hosting/PlayersHostedServiceTests.cs`
- Test: `tests/RustPlusBot.Features.Players.Tests/PlayerEventRegistrationTests.cs`

**Interfaces:**
- Consumes: `IPlayerEventChannelLocator.GetChannelIdAsync(ulong, Guid, CancellationToken)` from Task 2.
- Produces: nothing new. `PlayerEventRelay`'s constructor arity is unchanged — only the second parameter's type changes from `IEventChannelLocator` to `IPlayerEventChannelLocator`.

**Background:** `PlayerEventRelay.RelayAsync` loops over `evt.Transitions` and, per transition, calls `teamChatSender.SendAsync(...)` unconditionally and `poster.PostAsync(...)` only when the locator returned a channel id. **Only the injected locator type changes.** The loop body, the in-game send, and the renderer calls stay byte-identical — that is what keeps in-game messaging unchanged.

- [ ] **Step 1: Point the relay tests at the new locator**

In `tests/RustPlusBot.Features.Players.Tests/PlayerEventRelayTests.cs`, change the field declaration (line 18) from:

```csharp
    private readonly IEventChannelLocator _locator = Substitute.For<IEventChannelLocator>();
```

to:

```csharp
    private readonly IPlayerEventChannelLocator _locator = Substitute.For<IPlayerEventChannelLocator>();
```

Then add this test at the end of the class, which proves player embeds no longer reach `#events`:

```csharp
    [Fact]
    public async Task Never_posts_to_the_events_channel()
    {
        var eventsLocator = Substitute.For<IEventChannelLocator>();
        eventsLocator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)888);
        var relay = BuildRelay();
        _locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)555);

        await relay.RelayAsync(
            Evt(new PlayerTransition(PlayerTransitionKind.Connect, 1, "Bob", null)), CancellationToken.None);

        await _poster.Received(1).PostAsync(555UL, Arg.Any<global::Discord.Embed>(), Arg.Any<CancellationToken>());
        await _poster.DidNotReceive()
            .PostAsync(888UL, Arg.Any<global::Discord.Embed>(), Arg.Any<CancellationToken>());
        await eventsLocator.DidNotReceiveWithAnyArgs().GetChannelIdAsync(default, default, default);
    }
```

- [ ] **Step 2: Point the hosted-service test at the new locator**

In `tests/RustPlusBot.Features.Players.Tests/Hosting/PlayersHostedServiceTests.cs`, in `Create()` (around line 34), change:

```csharp
        var locator = Substitute.For<IEventChannelLocator>();
```

to:

```csharp
        var locator = Substitute.For<IPlayerEventChannelLocator>();
```

and in the `Harness` record (around line 125), change:

```csharp
        IEventChannelLocator Locator,
```

to:

```csharp
        IPlayerEventChannelLocator Locator,
```

- [ ] **Step 3: Point the registration test at the new locator**

In `tests/RustPlusBot.Features.Players.Tests/PlayerEventRegistrationTests.cs`, in `BuildProvider()` (around line 34), replace:

```csharp
        services.AddSingleton<IEventChannelLocator>(Substitute.For<IEventChannelLocator>());
```

with:

```csharp
        services.AddSingleton<IPlayerEventChannelLocator>(Substitute.For<IPlayerEventChannelLocator>());
```

`AddPlayers()` no longer needs `IEventChannelLocator`, so leaving it registered would hide a missing dependency.

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Players.Tests/RustPlusBot.Features.Players.Tests.csproj`
Expected: FAIL — `cannot convert from 'IPlayerEventChannelLocator' to 'IEventChannelLocator'` at the `new PlayerEventRelay(...)` call sites.

- [ ] **Step 5: Swap the relay's dependency**

In `src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs`, update the summary, the `<param>` doc, and the constructor parameter:

```csharp
/// <summary>Posts every player transition to #player-events AND in-game team chat.</summary>
/// <param name="renderer">Renders transitions as embeds and in-game lines.</param>
/// <param name="locator">Resolves the #player-events Discord channel id.</param>
/// <param name="poster">Posts embeds to the Discord channel.</param>
/// <param name="teamChatSender">Broadcasts the in-game team-chat line.</param>
/// <param name="scopeFactory">Opens scopes to read guild culture.</param>
internal sealed class PlayerEventRelay(
    PlayerEventRenderer renderer,
    IPlayerEventChannelLocator locator,
    IPlayerChannelPoster poster,
    IBotTeamChatSender teamChatSender,
    IServiceScopeFactory scopeFactory)
```

The `using RustPlusBot.Features.Workspace.Locating;` directive already covers the new type. Change nothing inside `RelayAsync` or `GetRenderSettingsAsync`.

- [ ] **Step 6: Run the player tests to verify they pass**

Run: `dtk dotnet test tests/RustPlusBot.Features.Players.Tests/RustPlusBot.Features.Players.Tests.csproj`
Expected: PASS, all green (23 tests — the 22 existing plus the new one).

- [ ] **Step 7: Run the whole suite to prove nothing else regressed**

Run: `dtk dotnet test RustPlusBot.slnx`
Expected: PASS. In particular `EventRelayTests` and `WipeAnnouncerTests` must still be green and untouched — they are the regression proof that map events and wipe announcements still go to `#events`.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs \
        tests/RustPlusBot.Features.Players.Tests/PlayerEventRelayTests.cs \
        tests/RustPlusBot.Features.Players.Tests/Hosting/PlayersHostedServiceTests.cs \
        tests/RustPlusBot.Features.Players.Tests/PlayerEventRegistrationTests.cs
git commit -m "feat(players): route presence events to #player-events"
```

---

### Task 4: Document the new channel

**Files:**
- Modify: `README.md`

**Interfaces:**
- Consumes: the behaviour shipped in Tasks 1–3.
- Produces: nothing consumed by code.

- [ ] **Step 1: Find the channel documentation**

Run: `grep -n "events" README.md`

Locate the section listing the per-server channels (it enumerates `#info`, `#teamchat`, `#events`, `#map`, `#switches`, `#alarms`, `#storagemonitors`).

- [ ] **Step 2: Add the #player-events entry**

Insert an entry immediately after the `#events` one, matching the surrounding formatting exactly (bullet, table row, or heading — whichever the file uses). Content:

> **#player-events** — team presence: joins, disconnects, deaths, respawns and AFK transitions. Read-only. The same lines are still broadcast to in-game team chat.

If the `#events` description currently mentions player/presence events, remove that mention so the two descriptions do not overlap.

- [ ] **Step 3: Verify the docs lint passes**

Run: `npx markdownlint-cli README.md` — if `markdownlint` is not installed, skip this step; the repo's `.markdownlint.json` is enforced in CI.

- [ ] **Step 4: Commit**

```bash
git add README.md
git commit -m "docs: document the #player-events channel"
```

---

## Verification

After Task 4, confirm the full picture:

```bash
dtk dotnet build RustPlusBot.slnx
dtk dotnet test RustPlusBot.slnx
git log --oneline -4
```

Expected: build clean (warnings are errors here), all tests pass, four commits present.

Manual check on a live guild (optional, requires a bot token and a paired server): start the bot, wait for the workspace reconcile, confirm `#player-events` appears between `#events` and `#map`, then trigger a teammate connect and confirm the embed lands in `#player-events` while the in-game team-chat line is unchanged.
