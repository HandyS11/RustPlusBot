# Team Presence Events (3d) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fire Discord `#events` + in-game alerts when a team member connects, disconnects, dies, respawns, or goes/returns from AFK — plus an in-game `!afk` command — by diffing `GetTeamInfoAsync` snapshots in the connection poll loop.

**Architecture:** The `ConnectionSupervisor` already runs a per-connection poll loop. We add a pure, unit-testable `TeamStateTracker` that diffs successive team snapshots into a list of `PlayerTransition`s (connect/disconnect/death/respawn, then AFK), which the supervisor publishes as `PlayerStateChangedEvent`. A new `RustPlusBot.Features.Players` slice (mirroring `Features.Events`) consumes the event and relays each transition to `#events` (embed) and in-game team chat (line). AFK adds two transition kinds, a hysteresis tracker inside `TeamStateTracker`, an `IAfkState` read seam on the supervisor, and an `!afk` command handler.

**Tech Stack:** C# / .NET 10, xUnit + NSubstitute, Discord.Net, RustPlusApi 2.0.0-beta.1, Microsoft.Extensions DI/Hosting/Options.

## Global Constraints

- Target framework `net10.0`; nullable reference types enabled (match sibling projects).
- New feature project name: `RustPlusBot.Features.Players`; test project `RustPlusBot.Features.Players.Tests`.
- Branch: `feat/team-presence-events` off `develop` (already created).
- `docs/product/` and `docs/superpowers/` are **gitignored** — never `git add` them.
- Mirror existing patterns exactly: primary-constructor DI, `internal sealed` types, `[LoggerMessage]` source-gen logging, broad-catch-and-log in loops (`#pragma warning disable CA1031`), `ConfigureAwait(false)` everywhere.
- Localization keys live in catalogs with EN + FR; English is the fallback culture.
- Spec: `docs/superpowers/specs/2026-06-18-rustplusbot-3d-team-presence-events-design.md`.
- Tasks 1–8 are the shippable core (connect/disconnect/death/respawn). Tasks 9–12 add AFK and may be split to a 3d-ii PR if needed.

---

### Task 1: Extend `TeamInfoSnapshot` facade with leader `DeathNote`

**Files:**

- Modify: `src/RustPlusBot.Abstractions/Connections/TeamInfoSnapshot.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs:256-267`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ServerQueryTests.cs` (add case) — verify via the existing fake; if no direct mapping test exists, add a focused test file `tests/RustPlusBot.Features.Connections.Tests/TeamInfoSnapshotTests.cs`.

**Interfaces:**

- Produces: `TeamInfoSnapshot.DeathNote` of type `(float X, float Y)?` (nullable tuple). Consumed by Task 4 (tracker death-location resolution).

- [ ] **Step 1: Add the `DeathNote` property to the record**

In `TeamInfoSnapshot.cs`, extend the record:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A point-in-time view of a team, decoupled from RustPlusApi types.</summary>
/// <param name="LeaderSteamId">Steam64 id of the current team leader.</param>
/// <param name="Members">Status snapshots for all team members.</param>
/// <param name="DeathNote">The leader's death-note map coordinate, if one is set; null otherwise.</param>
public sealed record TeamInfoSnapshot(
    ulong LeaderSteamId,
    IReadOnlyList<TeamMemberSnapshot> Members,
    (float X, float Y)? DeathNote = null);
```

(The trailing default keeps every existing `new TeamInfoSnapshot(leader, members)` call compiling.)

- [ ] **Step 2: Map the DeathNote in `GetTeamInfoAsync`**

In `RustPlusSocketSource.cs`, change the return at the end of the try block (currently `return new TeamInfoSnapshot(response.Data.LeaderSteamId, members);`):

```csharp
                var deathNote = response.Data.DeathNote is { } dn
                    ? ((float X, float Y)?)(dn.X, dn.Y)
                    : null;
                return new TeamInfoSnapshot(response.Data.LeaderSteamId, members, deathNote);
```

- [ ] **Step 3: Write the failing test**

Add to `tests/RustPlusBot.Features.Connections.Tests/TeamInfoSnapshotTests.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamInfoSnapshotTests
{
    [Fact]
    public void DeathNote_defaults_to_null()
    {
        var snap = new TeamInfoSnapshot(123UL, []);
        Assert.Null(snap.DeathNote);
    }

    [Fact]
    public void DeathNote_carries_coordinate_when_set()
    {
        var snap = new TeamInfoSnapshot(123UL, [], (100f, 200f));
        Assert.Equal((100f, 200f), snap.DeathNote);
    }
}
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter TeamInfoSnapshotTests`
Expected: PASS (and `dotnet build` of the connections project succeeds, confirming the `DeathNote` upstream property name is correct).

> If the build fails because `response.Data.DeathNote` is not the exact upstream member name, inspect `~/.nuget/packages/rustplusapi/2.0.0-beta.1/lib/net10.0/RustPlusApi.xml` for `P:RustPlusApi.Data.TeamInfo.DeathNote` and its property type, and adjust the accessor (it is documented as the leader's death note carrying `X`/`Y`).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Abstractions/Connections/TeamInfoSnapshot.cs \
        src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs \
        tests/RustPlusBot.Features.Connections.Tests/TeamInfoSnapshotTests.cs
git commit -m "feat(connections): surface leader DeathNote on TeamInfoSnapshot"
```

---

### Task 2: `PlayerTransition` domain types + `PlayerStateChangedEvent`

**Files:**

- Create: `src/RustPlusBot.Abstractions/Events/PlayerTransitionKind.cs`
- Create: `src/RustPlusBot.Abstractions/Events/PlayerTransition.cs`
- Create: `src/RustPlusBot.Abstractions/Events/PlayerStateChangedEvent.cs`
- Test: `tests/RustPlusBot.Features.Players.Tests/PlayerTransitionTests.cs` (project created in Task 5; for now add to `RustPlusBot.Features.Connections.Tests` and move later — OR create the test project first). **Decision:** create these types now; test them in Task 4 alongside the tracker (the types are trivial data records).

**Interfaces:**

- Produces:
  - `enum PlayerTransitionKind { Connect, Disconnect, Death, Respawn, BecameAfk, ReturnedFromAfk }`
  - `record PlayerTransition(PlayerTransitionKind Kind, ulong SteamId, string Name, (float X, float Y)? Location)`
  - `record PlayerStateChangedEvent(ulong GuildId, Guid ServerId, MapDimensions? Dimensions, IReadOnlyList<PlayerTransition> Transitions)`
- Consumed by: Task 4 (tracker produces transitions), Task 6 (relay), Task 7 (supervisor publishes the event).

- [ ] **Step 1: Create the enum**

`PlayerTransitionKind.cs`:

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>The kind of team-member presence transition detected between two team snapshots.</summary>
public enum PlayerTransitionKind
{
    /// <summary>Member came online.</summary>
    Connect,

    /// <summary>Member went offline.</summary>
    Disconnect,

    /// <summary>Member died.</summary>
    Death,

    /// <summary>Member spawned/respawned.</summary>
    Respawn,

    /// <summary>Member crossed the AFK stillness threshold.</summary>
    BecameAfk,

    /// <summary>Member moved again after being AFK.</summary>
    ReturnedFromAfk,
}
```

- [ ] **Step 2: Create `PlayerTransition`**

`PlayerTransition.cs`:

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>One team-member presence transition, with a pre-resolved optional map location.</summary>
/// <param name="Kind">The transition kind.</param>
/// <param name="SteamId">Steam64 id of the member.</param>
/// <param name="Name">In-game display name (empty when the game reports none).</param>
/// <param name="Location">Resolved map coordinate to show, or null when no location applies.</param>
public sealed record PlayerTransition(
    PlayerTransitionKind Kind,
    ulong SteamId,
    string Name,
    (float X, float Y)? Location);
```

- [ ] **Step 3: Create `PlayerStateChangedEvent`**

`PlayerStateChangedEvent.cs` (note: `MapDimensions` lives in `RustPlusBot.Features.Connections.Listening`, same as `MapMarkersChangedEvent` uses it):

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a team-info poll detects presence transitions since the previous poll.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="Dimensions">Map dimensions for grid rendering, or null if unavailable.</param>
/// <param name="Transitions">The transitions detected this poll (never empty when published).</param>
public sealed record PlayerStateChangedEvent(
    ulong GuildId,
    Guid ServerId,
    MapDimensions? Dimensions,
    IReadOnlyList<PlayerTransition> Transitions);
```

- [ ] **Step 4: Build to verify it compiles**

Run: `dotnet build src/RustPlusBot.Abstractions`
Expected: SUCCESS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Abstractions/Events/PlayerTransitionKind.cs \
        src/RustPlusBot.Abstractions/Events/PlayerTransition.cs \
        src/RustPlusBot.Abstractions/Events/PlayerStateChangedEvent.cs
git commit -m "feat(abstractions): add PlayerTransition + PlayerStateChangedEvent"
```

---

### Task 3: Create the `Features.Players` test project + empty slice project

**Files:**

- Create: `src/RustPlusBot.Features.Players/RustPlusBot.Features.Players.csproj`
- Create: `tests/RustPlusBot.Features.Players.Tests/RustPlusBot.Features.Players.Tests.csproj`
- Modify: the solution file `RustPlusBot.sln` (add both projects)

**Interfaces:**

- Produces: the project skeleton later tasks add types to.

- [ ] **Step 1: Create the slice csproj** (mirror `Features.Events.csproj`)

`src/RustPlusBot.Features.Players/RustPlusBot.Features.Players.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="RustPlusBot.Features.Players.Tests" />
    <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
    <ProjectReference Include="..\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
    <ProjectReference Include="..\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
    <ProjectReference Include="..\RustPlusBot.Domain\RustPlusBot.Domain.csproj" />
    <ProjectReference Include="..\RustPlusBot.Features.Workspace\RustPlusBot.Features.Workspace.csproj" />
    <ProjectReference Include="..\RustPlusBot.Features.Connections\RustPlusBot.Features.Connections.csproj" />
    <ProjectReference Include="..\RustPlusBot.Features.Events\RustPlusBot.Features.Events.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Discord.Net" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
  </ItemGroup>

</Project>
```

(The `Features.Events` reference is to reuse `GridReference`; see Task 6 note.)

- [ ] **Step 2: Create the test csproj** (mirror an existing `*.Tests.csproj`)

Read `tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj` and copy it verbatim, changing only the `<ProjectReference>` to point at `..\..\src\RustPlusBot.Features.Players\RustPlusBot.Features.Players.csproj`. Test projects in this repo inherit packages via `Directory.Build.props`/`Directory.Packages.props`, so only the project reference differs.

- [ ] **Step 3: Add both projects to the solution**

Run:

```bash
dotnet sln add src/RustPlusBot.Features.Players/RustPlusBot.Features.Players.csproj
dotnet sln add tests/RustPlusBot.Features.Players.Tests/RustPlusBot.Features.Players.Tests.csproj
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build`
Expected: SUCCESS (empty projects build clean).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Players/ tests/RustPlusBot.Features.Players.Tests/ RustPlusBot.sln
git commit -m "chore(players): scaffold Features.Players slice + test project"
```

---

### Task 4: `TeamStateTracker` — pure diff for the four core transitions (TDD)

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Listening/TeamStateTracker.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/TeamStateTrackerTests.cs`

**Interfaces:**

- Consumes: `TeamInfoSnapshot`, `TeamMemberSnapshot` (Task 1); `PlayerTransition`, `PlayerTransitionKind` (Task 2).
- Produces: `internal sealed class TeamStateTracker` with `IReadOnlyList<PlayerTransition> Diff(TeamInfoSnapshot? snapshot)`. First non-null snapshot primes silently (returns empty). Null snapshot returns empty and keeps baseline. Used by Task 7 (supervisor owns one tracker per connection) and extended by Task 9 (AFK).

> **Why a class, not inline in the poll loop:** the diff is pure logic and the spec requires unit tests without a socket. The supervisor owns one instance per connected window (fresh baseline per reconnect, matching the marker poller's `previous = null` reset).

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Connections.Tests/TeamStateTrackerTests.cs`:

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamStateTrackerTests
{
    private static TeamMemberSnapshot Member(
        ulong id, bool online = true, bool alive = true,
        float x = 0, float y = 0, DateTimeOffset spawn = default, DateTimeOffset death = default)
        => new(id, $"P{id}", x, y, online, alive, spawn, death);

    private static TeamInfoSnapshot Team(ulong leader, params TeamMemberSnapshot[] members)
        => new(leader, members);

    [Fact]
    public void First_snapshot_primes_silently()
    {
        var tracker = new TeamStateTracker();
        var result = tracker.Diff(Team(1, Member(1)));
        Assert.Empty(result);
    }

    [Fact]
    public void Null_snapshot_emits_nothing_and_keeps_baseline()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, online: true)));
        Assert.Empty(tracker.Diff(null));
        // After the null, an offline flip is still detected against the original baseline.
        var result = tracker.Diff(Team(1, Member(1, online: false)));
        Assert.Single(result, t => t.Kind == PlayerTransitionKind.Disconnect);
    }

    [Fact]
    public void Brand_new_member_is_primed_silently()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1)));
        var result = tracker.Diff(Team(1, Member(1), Member(2, online: true)));
        Assert.DoesNotContain(result, t => t.SteamId == 2);
    }

    [Fact]
    public void Connect_detected_on_offline_to_online()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, online: false)));
        var result = tracker.Diff(Team(1, Member(1, online: true)));
        Assert.Single(result, t => t.Kind == PlayerTransitionKind.Connect && t.SteamId == 1);
    }

    [Fact]
    public void Disconnect_detected_on_online_to_offline()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, online: true)));
        var result = tracker.Diff(Team(1, Member(1, online: false)));
        Assert.Single(result, t => t.Kind == PlayerTransitionKind.Disconnect && t.SteamId == 1);
    }

    [Fact]
    public void Death_detected_on_deathtime_advance_even_if_alive_again()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, alive: true, death: t0)));
        // Died and already respawned: IsAlive true both times, but LastDeathTime advanced.
        var result = tracker.Diff(Team(1, Member(1, alive: true, death: t0.AddMinutes(1))));
        Assert.Contains(result, t => t.Kind == PlayerTransitionKind.Death && t.SteamId == 1);
    }

    [Fact]
    public void Respawn_detected_on_spawntime_advance_with_current_location()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, spawn: t0)));
        var result = tracker.Diff(Team(1, Member(1, x: 50, y: 60, spawn: t0.AddMinutes(1))));
        var respawn = Assert.Single(result, t => t.Kind == PlayerTransitionKind.Respawn);
        Assert.Equal((50f, 60f), respawn.Location);
    }

    [Fact]
    public void Unchanged_snapshot_emits_nothing()
    {
        var tracker = new TeamStateTracker();
        var m = Member(1, spawn: DateTimeOffset.UnixEpoch, death: DateTimeOffset.UnixEpoch);
        tracker.Diff(new TeamInfoSnapshot(1, [m]));
        Assert.Empty(tracker.Diff(new TeamInfoSnapshot(1, [m])));
    }

    [Fact]
    public void Connect_and_disconnect_have_no_location()
    {
        var tracker = new TeamStateTracker();
        tracker.Diff(Team(1, Member(1, online: false)));
        var result = tracker.Diff(Team(1, Member(1, x: 9, y: 9, online: true)));
        Assert.Null(Assert.Single(result).Location);
    }

    [Fact]
    public void Leader_death_uses_deathnote_location()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        tracker.Diff(new TeamInfoSnapshot(1, [Member(1, death: t0)]));
        var snap = new TeamInfoSnapshot(1, [Member(1, x: 1, y: 1, death: t0.AddMinutes(1))], (700f, 800f));
        var death = Assert.Single(tracker.Diff(snap), t => t.Kind == PlayerTransitionKind.Death);
        Assert.Equal((700f, 800f), death.Location);
    }

    [Fact]
    public void Nonleader_death_uses_previous_poll_position_not_current()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        // Member 2 is alive at (10,10) on the baseline poll...
        tracker.Diff(new TeamInfoSnapshot(1, [Member(1), Member(2, x: 10, y: 10, death: t0)]));
        // ...then dies and respawns at (999,999); death must report the PRE-death (10,10).
        var snap = new TeamInfoSnapshot(1, [Member(1), Member(2, x: 999, y: 999, death: t0.AddMinutes(1))], (5f, 5f));
        var death = Assert.Single(tracker.Diff(snap), t => t.Kind == PlayerTransitionKind.Death);
        Assert.Equal((10f, 10f), death.Location); // leader DeathNote (5,5) must NOT apply to a non-leader
    }

    [Fact]
    public void Death_with_no_prior_position_and_no_deathnote_has_null_location()
    {
        var t0 = DateTimeOffset.UnixEpoch;
        var tracker = new TeamStateTracker();
        // Prime member 1 only.
        tracker.Diff(new TeamInfoSnapshot(1, [Member(1)]));
        // Member 2 appears already-dead-advanced in the same poll it is first seen → primed silently, no death.
        var first = tracker.Diff(new TeamInfoSnapshot(1, [Member(1), Member(2, death: t0)]));
        Assert.DoesNotContain(first, t => t.SteamId == 2);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter TeamStateTrackerTests`
Expected: FAIL — `TeamStateTracker` does not exist.

- [ ] **Step 3: Implement `TeamStateTracker`**

`src/RustPlusBot.Features.Connections/Listening/TeamStateTracker.cs`:

```csharp
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Diffs successive team snapshots into presence transitions. One instance per connected window.</summary>
/// <remarks>Not thread-safe: the supervisor calls <see cref="Diff"/> from a single poll loop.</remarks>
internal sealed class TeamStateTracker
{
    private Dictionary<ulong, TeamMemberSnapshot>? _baseline;

    /// <summary>Diffs <paramref name="snapshot"/> against the previous one. First non-null call primes silently.</summary>
    /// <param name="snapshot">The latest team snapshot, or null when the poll returned no data.</param>
    /// <returns>The transitions since the previous snapshot; empty on prime, null input, or no change.</returns>
    public IReadOnlyList<PlayerTransition> Diff(TeamInfoSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return [];
        }

        var current = snapshot.Members.ToDictionary(m => m.SteamId);

        if (_baseline is null)
        {
            _baseline = current; // first poll: silent baseline
            return [];
        }

        var transitions = new List<PlayerTransition>();
        foreach (var (id, now) in current)
        {
            if (!_baseline.TryGetValue(id, out var was))
            {
                continue; // brand-new member: prime silently this poll
            }

            if (now.IsOnline && !was.IsOnline)
            {
                transitions.Add(new PlayerTransition(PlayerTransitionKind.Connect, id, now.Name, null));
            }
            else if (!now.IsOnline && was.IsOnline)
            {
                transitions.Add(new PlayerTransition(PlayerTransitionKind.Disconnect, id, now.Name, null));
            }

            if (now.LastDeathTimeUtc > was.LastDeathTimeUtc)
            {
                transitions.Add(new PlayerTransition(
                    PlayerTransitionKind.Death, id, now.Name, ResolveDeathLocation(id, snapshot, was)));
            }

            if (now.LastSpawnTimeUtc > was.LastSpawnTimeUtc)
            {
                transitions.Add(new PlayerTransition(
                    PlayerTransitionKind.Respawn, id, now.Name, (now.X, now.Y)));
            }
        }

        _baseline = current;
        return transitions;
    }

    private static (float X, float Y)? ResolveDeathLocation(
        ulong steamId, TeamInfoSnapshot snapshot, TeamMemberSnapshot previous)
    {
        // Leader: the single DeathNote is the true death spot (player respawns elsewhere).
        if (steamId == snapshot.LeaderSteamId && snapshot.DeathNote is { } note)
        {
            return note;
        }

        // Everyone else: the previous poll's position (captured while alive) ≈ where they died.
        return (previous.X, previous.Y);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter TeamStateTrackerTests`
Expected: PASS (all 12).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Listening/TeamStateTracker.cs \
        tests/RustPlusBot.Features.Connections.Tests/TeamStateTrackerTests.cs
git commit -m "feat(connections): TeamStateTracker diffs team snapshots into transitions"
```

---

### Task 5: `PlayerLocalizationCatalog` + `IPlayerLocalizer`/`PlayerLocalizer` (TDD)

**Files:**

- Create: `src/RustPlusBot.Features.Players/Rendering/IPlayerLocalizer.cs`
- Create: `src/RustPlusBot.Features.Players/Rendering/PlayerLocalizer.cs`
- Create: `src/RustPlusBot.Features.Players/Rendering/PlayerLocalizationCatalog.cs`
- Test: `tests/RustPlusBot.Features.Players.Tests/PlayerLocalizationCatalogTests.cs`

**Interfaces:**

- Produces: `internal interface IPlayerLocalizer { string Get(string key, string culture); string Get(string key, string culture, params object[] args); }` and `PlayerLocalizationCatalog.Default`. Consumed by Task 6 (renderer/relay).

> Copy the `EventLocalizer` implementation verbatim (it is already marked "duplicated; consolidate later") — same English-fallback + region-normalize logic.

- [ ] **Step 1: Write the failing test**

`tests/RustPlusBot.Features.Players.Tests/PlayerLocalizationCatalogTests.cs`:

```csharp
using RustPlusBot.Features.Players.Rendering;

namespace RustPlusBot.Features.Players.Tests;

public sealed class PlayerLocalizationCatalogTests
{
    private static readonly string[] Keys =
    [
        "player.connect", "player.connect.line",
        "player.disconnect", "player.disconnect.line",
        "player.death", "player.death.line",
        "player.death.unknown", "player.death.unknown.line",
        "player.respawn", "player.respawn.line",
        "player.afk", "player.afk.line",
        "player.afk.back", "player.afk.back.line",
    ];

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    public void Every_key_present_for_culture(string culture)
    {
        var catalog = PlayerLocalizationCatalog.Default;
        Assert.True(catalog.Strings.TryGetValue(culture, out var map));
        foreach (var key in Keys)
        {
            Assert.True(map!.ContainsKey(key), $"missing {key} for {culture}");
        }
    }

    [Fact]
    public void Localizer_formats_with_args_and_falls_back_to_english()
    {
        var localizer = new PlayerLocalizer(PlayerLocalizationCatalog.Default);
        Assert.Contains("Bob", localizer.Get("player.connect", "en", "Bob"));
        // Unknown culture falls back to English string (not the raw key).
        Assert.Contains("Bob", localizer.Get("player.connect", "de", "Bob"));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Players.Tests --filter PlayerLocalizationCatalogTests`
Expected: FAIL — types do not exist.

- [ ] **Step 3: Create `IPlayerLocalizer`**

`src/RustPlusBot.Features.Players/Rendering/IPlayerLocalizer.cs`:

```csharp
namespace RustPlusBot.Features.Players.Rendering;

/// <summary>Resolves localized player-event strings by key and culture, falling back to English.</summary>
internal interface IPlayerLocalizer
{
    /// <summary>Gets the localized string for a key, or the key itself if not found.</summary>
    string Get(string key, string culture);

    /// <summary>Gets the localized, format-applied string.</summary>
    string Get(string key, string culture, params object[] args);
}
```

- [ ] **Step 4: Create `PlayerLocalizer`** (copy `EventLocalizer` body, rename type + catalog field type)

`src/RustPlusBot.Features.Players/Rendering/PlayerLocalizer.cs`:

```csharp
using System.Globalization;

namespace RustPlusBot.Features.Players.Rendering;

/// <summary>Dictionary-backed <see cref="IPlayerLocalizer"/> with English fallback and region normalization.</summary>
/// <param name="catalog">The string catalog.</param>
internal sealed class PlayerLocalizer(PlayerLocalizationCatalog catalog) : IPlayerLocalizer
{
    private const string FallbackCulture = "en";

    /// <inheritdoc />
    public string Get(string key, string culture)
    {
        var normalized = Normalize(culture);
        if (catalog.Strings.TryGetValue(normalized, out var map) && map.TryGetValue(key, out var value))
        {
            return value;
        }

        if (catalog.Strings.TryGetValue(FallbackCulture, out var fallback) &&
            fallback.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    /// <inheritdoc />
    public string Get(string key, string culture, params object[] args)
    {
        var format = Get(key, culture);
        var provider = ResolveFormatProvider(Normalize(culture));
        return string.Format(provider, format, args);
    }

    private static string Normalize(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return FallbackCulture;
        }

        var dash = culture.IndexOf('-', StringComparison.Ordinal);
        var primary = dash >= 0 ? culture[..dash] : culture;
        return primary.ToLowerInvariant();
    }

    private static CultureInfo ResolveFormatProvider(string culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
```

- [ ] **Step 5: Create `PlayerLocalizationCatalog`** with all keys

`src/RustPlusBot.Features.Players/Rendering/PlayerLocalizationCatalog.cs`:

```csharp
namespace RustPlusBot.Features.Players.Rendering;

/// <summary>The in-memory string catalog for player events: culture -> (key -> value). English is the fallback.</summary>
internal sealed class PlayerLocalizationCatalog
{
    /// <summary>culture -> key -> value.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings { get; init; }

    /// <summary>The built-in EN/FR catalog.</summary>
    public static PlayerLocalizationCatalog Default { get; } = new()
    {
        Strings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["player.title"] = "Team event",
                ["player.connect"] = "🟢 {0} connected",
                ["player.connect.line"] = "{0} connected",
                ["player.disconnect"] = "🔴 {0} disconnected",
                ["player.disconnect.line"] = "{0} disconnected",
                ["player.death"] = "💀 {0} died at {1}",
                ["player.death.line"] = "{0} died at {1}",
                ["player.death.unknown"] = "💀 {0} died",
                ["player.death.unknown.line"] = "{0} died",
                ["player.respawn"] = "✨ {0} respawned at {1}",
                ["player.respawn.line"] = "{0} respawned at {1}",
                ["player.afk"] = "💤 {0} is AFK ({1})",
                ["player.afk.line"] = "{0} is AFK ({1})",
                ["player.afk.back"] = "👋 {0} is back",
                ["player.afk.back.line"] = "{0} is back",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["player.title"] = "Événement d'équipe",
                ["player.connect"] = "🟢 {0} s'est connecté",
                ["player.connect.line"] = "{0} s'est connecté",
                ["player.disconnect"] = "🔴 {0} s'est déconnecté",
                ["player.disconnect.line"] = "{0} s'est déconnecté",
                ["player.death"] = "💀 {0} est mort en {1}",
                ["player.death.line"] = "{0} est mort en {1}",
                ["player.death.unknown"] = "💀 {0} est mort",
                ["player.death.unknown.line"] = "{0} est mort",
                ["player.respawn"] = "✨ {0} a réapparu en {1}",
                ["player.respawn.line"] = "{0} a réapparu en {1}",
                ["player.afk"] = "💤 {0} est AFK ({1})",
                ["player.afk.line"] = "{0} est AFK ({1})",
                ["player.afk.back"] = "👋 {0} est de retour",
                ["player.afk.back.line"] = "{0} est de retour",
            },
        },
    };
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Players.Tests --filter PlayerLocalizationCatalogTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Players/Rendering/ tests/RustPlusBot.Features.Players.Tests/PlayerLocalizationCatalogTests.cs
git commit -m "feat(players): EN/FR player-event localization catalog"
```

---

### Task 6: `PlayerEventRenderer` + `PlayerEventRelay` (TDD)

**Files:**

- Create: `src/RustPlusBot.Features.Players/Rendering/PlayerEventRenderer.cs`
- Create: `src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs`
- Test: `tests/RustPlusBot.Features.Players.Tests/PlayerEventRendererTests.cs`
- Test: `tests/RustPlusBot.Features.Players.Tests/PlayerEventRelayTests.cs`

**Interfaces:**

- Consumes: `IPlayerLocalizer` (Task 5); `GridReference.From(float, float, MapDimensions?)` from `RustPlusBot.Features.Events.Formatting` (Task 3 added the project ref); `IEventChannelLocator` (`RustPlusBot.Features.Workspace.Locating`); `IEventChannelPoster` (`RustPlusBot.Features.Events.Posting`); `ITeamChatSender` (`RustPlusBot.Features.Connections.Listening`); `IWorkspaceStore` (`RustPlusBot.Persistence.Workspace`); `PlayerStateChangedEvent`, `PlayerTransition` (Task 2).
- Produces: `internal sealed class PlayerEventRenderer` with `Embed Render(PlayerTransition, MapDimensions?, string culture)` and `string RenderLine(PlayerTransition, MapDimensions?, string culture)`; `internal sealed class PlayerEventRelay` with `Task RelayAsync(PlayerStateChangedEvent, CancellationToken)`. Consumed by Task 7 (hosted service) and Task 8 (DI).

> `IEventChannelPoster` is `internal` to `Features.Events`. Task 3 references `Features.Events`, but `internal` types are not visible across the assembly boundary. **Resolution:** the relay depends on the *public* `IEventChannelLocator` (in `Features.Workspace`) and on the existing `DiscordEventChannelPoster` is internal — so instead of reusing the poster, post via the shared `DiscordSocketClient` directly (the poster is a thin wrapper). Define a tiny internal `IPlayerChannelPoster` in this slice with a `DiscordSocketClient`-backed implementation, mirroring `DiscordEventChannelPoster`. Read `src/RustPlusBot.Features.Events/Posting/DiscordEventChannelPoster.cs` and copy its body.

- [ ] **Step 1: Read the existing poster to copy**

Run: `cat src/RustPlusBot.Features.Events/Posting/DiscordEventChannelPoster.cs`
(Use its exact channel-fetch + `SendMessageAsync` logic in the new `DiscordPlayerChannelPoster`.)

- [ ] **Step 2: Write the failing renderer test**

`tests/RustPlusBot.Features.Players.Tests/PlayerEventRendererTests.cs`:

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Rendering;

namespace RustPlusBot.Features.Players.Tests;

public sealed class PlayerEventRendererTests
{
    private static readonly PlayerEventRenderer Renderer = new(new PlayerLocalizer(PlayerLocalizationCatalog.Default));
    private static readonly MapDimensions Dims = new(3000, 3000, 0);

    [Fact]
    public void Connect_line_names_member_without_location()
    {
        var line = Renderer.RenderLine(
            new PlayerTransition(PlayerTransitionKind.Connect, 1, "Bob", null), Dims, "en");
        Assert.Equal("Bob connected", line);
    }

    [Fact]
    public void Death_with_location_includes_grid()
    {
        var line = Renderer.RenderLine(
            new PlayerTransition(PlayerTransitionKind.Death, 1, "Bob", (0f, 2999f)), Dims, "en");
        Assert.StartsWith("Bob died at A", line); // top-left ≈ A0/A1
    }

    [Fact]
    public void Death_without_location_uses_unknown_string()
    {
        var line = Renderer.RenderLine(
            new PlayerTransition(PlayerTransitionKind.Death, 1, "Bob", null), Dims, "en");
        Assert.Equal("Bob died", line);
    }

    [Fact]
    public void Afk_back_has_no_location()
    {
        var line = Renderer.RenderLine(
            new PlayerTransition(PlayerTransitionKind.ReturnedFromAfk, 1, "Bob", null), Dims, "en");
        Assert.Equal("Bob is back", line);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Players.Tests --filter PlayerEventRendererTests`
Expected: FAIL — `PlayerEventRenderer` does not exist.

- [ ] **Step 4: Implement `PlayerEventRenderer`**

`src/RustPlusBot.Features.Players/Rendering/PlayerEventRenderer.cs`:

```csharp
using Discord;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Formatting;

namespace RustPlusBot.Features.Players.Rendering;

/// <summary>Renders one <see cref="PlayerTransition"/> as a Discord embed or an in-game line.</summary>
/// <param name="localizer">The reply localizer.</param>
internal sealed class PlayerEventRenderer(IPlayerLocalizer localizer)
{
    /// <summary>Renders the transition for a guild culture as a Discord embed.</summary>
    public Embed Render(PlayerTransition transition, MapDimensions? dims, string culture)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return new EmbedBuilder()
            .WithAuthor(localizer.Get("player.title", culture))
            .WithDescription(Describe(transition, dims, culture, suffix: string.Empty))
            .WithCurrentTimestamp()
            .Build();
    }

    /// <summary>Renders the in-game team-chat line for the transition.</summary>
    public string RenderLine(PlayerTransition transition, MapDimensions? dims, string culture)
    {
        ArgumentNullException.ThrowIfNull(transition);
        return Describe(transition, dims, culture, suffix: ".line");
    }

    private string Describe(PlayerTransition t, MapDimensions? dims, string culture, string suffix)
    {
        switch (t.Kind)
        {
            case PlayerTransitionKind.Connect:
                return localizer.Get("player.connect" + suffix, culture, t.Name);
            case PlayerTransitionKind.Disconnect:
                return localizer.Get("player.disconnect" + suffix, culture, t.Name);
            case PlayerTransitionKind.Respawn:
                return localizer.Get("player.respawn" + suffix, culture, t.Name, Grid(t, dims));
            case PlayerTransitionKind.ReturnedFromAfk:
                return localizer.Get("player.afk.back" + suffix, culture, t.Name);
            case PlayerTransitionKind.BecameAfk:
                return localizer.Get("player.afk" + suffix, culture, t.Name, Grid(t, dims));
            case PlayerTransitionKind.Death:
                return t.Location is null
                    ? localizer.Get("player.death.unknown" + suffix, culture, t.Name)
                    : localizer.Get("player.death" + suffix, culture, t.Name, Grid(t, dims));
            default:
                throw new ArgumentOutOfRangeException(nameof(t), t.Kind, "Unsupported transition kind.");
        }
    }

    private static string Grid(PlayerTransition t, MapDimensions? dims)
        => t.Location is { } loc ? GridReference.From(loc.X, loc.Y, dims) : string.Empty;
}
```

- [ ] **Step 5: Run renderer test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Players.Tests --filter PlayerEventRendererTests`
Expected: PASS.

> `MapDimensions` is `record MapDimensions(uint Width, uint Height, int OceanMargin)` — confirmed; the `, 0` is `OceanMargin`.

- [ ] **Step 6: Create the slice poster** `src/RustPlusBot.Features.Players/Posting/IPlayerChannelPoster.cs` + `DiscordPlayerChannelPoster.cs`

`IPlayerChannelPoster.cs`:

```csharp
using Discord;

namespace RustPlusBot.Features.Players.Posting;

/// <summary>Posts a player-event embed to a Discord channel.</summary>
internal interface IPlayerChannelPoster
{
    /// <summary>Posts <paramref name="embed"/> to the channel.</summary>
    Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken);
}
```

`DiscordPlayerChannelPoster.cs` — copy the body of `DiscordEventChannelPoster` verbatim, renaming the type and interface. (It fetches the channel from `DiscordSocketClient` and calls `SendMessageAsync(embed: ...)`.)

- [ ] **Step 7: Write the failing relay test**

`tests/RustPlusBot.Features.Players.Tests/PlayerEventRelayTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Posting;
using RustPlusBot.Features.Players.Relaying;
using RustPlusBot.Features.Players.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Players.Tests;

public sealed class PlayerEventRelayTests
{
    private readonly IEventChannelLocator _locator = Substitute.For<IEventChannelLocator>();
    private readonly IPlayerChannelPoster _poster = Substitute.For<IPlayerChannelPoster>();
    private readonly ITeamChatSender _sender = Substitute.For<ITeamChatSender>();
    private readonly IWorkspaceStore _workspace = Substitute.For<IWorkspaceStore>();

    private PlayerEventRelay BuildRelay()
    {
        var scopeFactory = BuildScopeFactory(_workspace);
        _workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");
        return new PlayerEventRelay(
            new PlayerEventRenderer(new PlayerLocalizer(PlayerLocalizationCatalog.Default)),
            _locator, _poster, _sender, scopeFactory);
    }

    private static IServiceScopeFactory BuildScopeFactory(IWorkspaceStore workspace)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => workspace);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static PlayerStateChangedEvent Evt(params PlayerTransition[] ts)
        => new(1UL, Guid.NewGuid(), new MapDimensions(3000, 3000, 0), ts);

    [Fact]
    public async Task Sends_ingame_line_for_each_transition()
    {
        var relay = BuildRelay();
        _locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);

        await relay.RelayAsync(
            Evt(new PlayerTransition(PlayerTransitionKind.Connect, 1, "Bob", null)), CancellationToken.None);

        await _sender.Received(1).SendAsync(
            Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Is<string>(s => s.Contains("Bob")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Skips_discord_post_when_no_channel_but_still_sends_ingame()
    {
        var relay = BuildRelay();
        _locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)null);

        await relay.RelayAsync(
            Evt(new PlayerTransition(PlayerTransitionKind.Disconnect, 1, "Bob", null)), CancellationToken.None);

        await _poster.DidNotReceive().PostAsync(Arg.Any<ulong>(), Arg.Any<Discord.Embed>(), Arg.Any<CancellationToken>());
        await _sender.ReceivedWithAnyArgs(1).SendAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Posts_embed_when_channel_resolves()
    {
        var relay = BuildRelay();
        _locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ulong?)555);

        await relay.RelayAsync(
            Evt(new PlayerTransition(PlayerTransitionKind.Death, 1, "Bob", (10f, 10f))), CancellationToken.None);

        await _poster.Received(1).PostAsync(555UL, Arg.Any<Discord.Embed>(), Arg.Any<CancellationToken>());
    }
}
```

> Verify `IWorkspaceStore.GetCultureAsync` is the exact method name (used by `EventRelay.GetCultureAsync`). If different, match the real signature.

- [ ] **Step 8: Run relay test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Players.Tests --filter PlayerEventRelayTests`
Expected: FAIL — `PlayerEventRelay` does not exist.

- [ ] **Step 9: Implement `PlayerEventRelay`** (mirror `EventRelay`)

`src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players.Posting;
using RustPlusBot.Features.Players.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Players.Relaying;

/// <summary>Posts every player transition to #events AND in-game team chat.</summary>
/// <param name="renderer">Renders transitions as embeds and in-game lines.</param>
/// <param name="locator">Resolves the #events Discord channel id.</param>
/// <param name="poster">Posts embeds to the Discord channel.</param>
/// <param name="teamChatSender">Broadcasts the in-game team-chat line.</param>
/// <param name="scopeFactory">Opens scopes to read guild culture.</param>
internal sealed class PlayerEventRelay(
    PlayerEventRenderer renderer,
    IEventChannelLocator locator,
    IPlayerChannelPoster poster,
    ITeamChatSender teamChatSender,
    IServiceScopeFactory scopeFactory)
{
    /// <summary>Handles one <see cref="PlayerStateChangedEvent"/>: posts embeds and broadcasts in-game.</summary>
    public async Task RelayAsync(PlayerStateChangedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.Transitions.Count == 0)
        {
            return;
        }

        var culture = await GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var t in evt.Transitions)
        {
            await teamChatSender
                .SendAsync(evt.GuildId, evt.ServerId, renderer.RenderLine(t, evt.Dimensions, culture), cancellationToken)
                .ConfigureAwait(false);
            if (channelId is { } id)
            {
                await poster.PostAsync(id, renderer.Render(t, evt.Dimensions, culture), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task<string> GetCultureAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            return await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 10: Run all Players tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Players.Tests`
Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add src/RustPlusBot.Features.Players/Rendering/PlayerEventRenderer.cs \
        src/RustPlusBot.Features.Players/Posting/ \
        src/RustPlusBot.Features.Players/Relaying/PlayerEventRelay.cs \
        tests/RustPlusBot.Features.Players.Tests/PlayerEventRendererTests.cs \
        tests/RustPlusBot.Features.Players.Tests/PlayerEventRelayTests.cs
git commit -m "feat(players): renderer + relay to #events and in-game chat"
```

---

### Task 7: `PlayersHostedService` + DI + supervisor publishes `PlayerStateChangedEvent`

**Files:**

- Create: `src/RustPlusBot.Features.Players/Hosting/PlayersHostedService.cs`
- Create: `src/RustPlusBot.Features.Players/PlayerEventServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (poll loop + tracker)
- Modify: `src/RustPlusBot.Host/Program.cs:70` (add `AddPlayers()`)
- Test: `tests/RustPlusBot.Features.Players.Tests/PlayerEventRegistrationTests.cs`

**Interfaces:**

- Consumes: `IEventBus.SubscribeAsync<PlayerStateChangedEvent>` (Task 2), `PlayerEventRelay` (Task 6), `TeamStateTracker` (Task 4).
- Produces: `IServiceCollection AddPlayers(this IServiceCollection)`; supervisor now publishes `PlayerStateChangedEvent` each poll with transitions.

- [ ] **Step 1: Implement `PlayersHostedService`** (mirror `EventsHostedService`'s consumer loop)

`src/RustPlusBot.Features.Players/Hosting/PlayersHostedService.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Players.Relaying;

namespace RustPlusBot.Features.Players.Hosting;

/// <summary>Consumes <see cref="PlayerStateChangedEvent"/> and relays each to #events + in-game chat.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Relays player transitions.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class PlayersHostedService(
    IEventBus eventBus,
    PlayerEventRelay relay,
    ILogger<PlayersHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loop = Task.Run(() => ConsumeAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
#pragma warning disable VSTHRD003 // Our own loop task, joined on stop.
                await _loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<PlayerStateChangedEvent>(cancellationToken)
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Player relay loop faulted.")]
    private static partial void LogRelayLoopFaulted(ILogger logger, Exception exception);
}
```

- [ ] **Step 2: Implement `AddPlayers`**

`src/RustPlusBot.Features.Players/PlayerEventServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Players.Hosting;
using RustPlusBot.Features.Players.Posting;
using RustPlusBot.Features.Players.Relaying;
using RustPlusBot.Features.Players.Rendering;

namespace RustPlusBot.Features.Players;

/// <summary>DI registration for the team-presence-events feature.</summary>
public static class PlayerEventServiceCollectionExtensions
{
    /// <summary>Registers the localizer, renderer, poster, relay, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPlayers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(PlayerLocalizationCatalog.Default);
        services.AddSingleton<IPlayerLocalizer, PlayerLocalizer>();
        services.AddSingleton<PlayerEventRenderer>();
        services.AddSingleton<IPlayerChannelPoster, DiscordPlayerChannelPoster>();
        services.AddSingleton<PlayerEventRelay>();
        services.AddHostedService<PlayersHostedService>();

        return services;
    }
}
```

- [ ] **Step 3: Wire the tracker into the supervisor poll loop**

In `ConnectionSupervisor.cs`, the `PollMarkersAsync` method owns the per-window state. Add a `TeamStateTracker` and poll team info inside the same loop. After the existing marker-diff block (after the `await DetectRigActivationsAsync(...)` call, still inside the `try`), insert:

```csharp
                var team = await connection.GetTeamInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
                var transitions = tracker.Diff(team);
                if (transitions.Count > 0)
                {
                    await eventBus.PublishAsync(
                            new PlayerStateChangedEvent(key.Guild, key.Server, dims, transitions), ct)
                        .ConfigureAwait(false);
                }
```

And declare the tracker at the top of `PollMarkersAsync` next to `previous`/`rigsInRadius`:

```csharp
        var tracker = new TeamStateTracker();
```

Add `using RustPlusBot.Abstractions.Events;` (already present) and ensure `PlayerStateChangedEvent` resolves (Abstractions is referenced). No new method parameters needed — `dims` and `eventBus` are already in scope.

- [ ] **Step 4: Register `AddPlayers()` in `Program.cs`**

After line `builder.Services.AddEvents();`, add:

```csharp
builder.Services.AddPlayers();
```

(Add `using RustPlusBot.Features.Players;` to the usings.)

- [ ] **Step 5: Write the registration test**

`tests/RustPlusBot.Features.Players.Tests/PlayerEventRegistrationTests.cs` (mirror `EventRegistrationTests`):

```csharp
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Players;
using RustPlusBot.Features.Players.Hosting;
using RustPlusBot.Features.Players.Relaying;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Players.Tests;

public sealed class PlayerEventRegistrationTests
{
    [Fact]
    public void Services_resolve()
    {
        using var provider = BuildProvider();
        Assert.Contains(provider.GetServices<IHostedService>(), h => h is PlayersHostedService);
        Assert.NotNull(provider.GetRequiredService<PlayerEventRelay>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IEventChannelLocator>(Substitute.For<IEventChannelLocator>());
        services.AddScoped<IWorkspaceStore>(_ => Substitute.For<IWorkspaceStore>());
        services.AddSingleton<ITeamChatSender>(Substitute.For<ITeamChatSender>());
        services.AddPlayers();
        return services.BuildServiceProvider(validateScopes: true);
    }
}
```

- [ ] **Step 6: Build + run tests**

Run: `dotnet build && dotnet test tests/RustPlusBot.Features.Players.Tests`
Expected: PASS. Also run `dotnet test tests/RustPlusBot.Features.Connections.Tests` to confirm the supervisor change didn't break existing tests.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Players/Hosting/ \
        src/RustPlusBot.Features.Players/PlayerEventServiceCollectionExtensions.cs \
        src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs \
        src/RustPlusBot.Host/Program.cs \
        tests/RustPlusBot.Features.Players.Tests/PlayerEventRegistrationTests.cs
git commit -m "feat(players): publish + relay PlayerStateChangedEvent from poll loop"
```

---

### Task 8: End-to-end smoke + core milestone verification

**Files:**

- Test: `tests/RustPlusBot.Features.Players.Tests/PlayerEventEndToEndTests.cs`

**Interfaces:**

- Consumes everything from Tasks 1–7. No new production code (verification only).

- [ ] **Step 1: Write an in-memory bus round-trip test**

`tests/RustPlusBot.Features.Players.Tests/PlayerEventEndToEndTests.cs` — publish a `PlayerStateChangedEvent` on a real `InMemoryEventBus`, run the relay manually (or assert the hosted-service consumer path), and verify the team-chat sender received the expected line. (Reuse the substitutes from `PlayerEventRelayTests`; this test asserts the *full* transition set — one of each kind — renders without throwing.)

```csharp
[Fact]
public async Task All_transition_kinds_render_without_throwing()
{
    var renderer = new PlayerEventRenderer(new PlayerLocalizer(PlayerLocalizationCatalog.Default));
    var dims = new MapDimensions(3000, 3000, 0);
    foreach (PlayerTransitionKind kind in Enum.GetValues<PlayerTransitionKind>())
    {
        var loc = kind is PlayerTransitionKind.Death or PlayerTransitionKind.Respawn or PlayerTransitionKind.BecameAfk
            ? ((float, float)?)(10f, 10f) : null;
        var t = new PlayerTransition(kind, 1, "Bob", loc);
        Assert.NotNull(renderer.Render(t, dims, "en"));
        Assert.NotEmpty(renderer.RenderLine(t, dims, "fr"));
    }
}
```

- [ ] **Step 2: Run the full suite**

Run: `dotnet test`
Expected: PASS (entire solution).

- [ ] **Step 3: Commit**

```bash
git add tests/RustPlusBot.Features.Players.Tests/PlayerEventEndToEndTests.cs
git commit -m "test(players): all transition kinds render end-to-end"
```

> **Core milestone (Tasks 1–8) is shippable here.** Connect/disconnect/death/respawn alerts work. AFK (Tasks 9–12) can ship in the same PR or split to 3d-ii.

---

### Task 9: AFK config + extend `TeamStateTracker` with hysteresis (TDD)

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/ConnectionOptions.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/TeamStateTracker.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (inject `IClock`, pass `now` + options to `Diff`)
- Test: `tests/RustPlusBot.Features.Connections.Tests/TeamStateTrackerAfkTests.cs`

**Interfaces:**

- Consumes: `IClock` (`RustPlusBot.Abstractions.Time`).
- Produces: `ConnectionOptions.AfkThreshold` (`TimeSpan`, default 5m) + `AfkEpsilon` (`float`, default 1f); `TeamStateTracker.Diff` gains parameters `(TeamInfoSnapshot?, DateTimeOffset now, TimeSpan afkThreshold, float afkEpsilon)`. **This changes the Task 4 signature** — update Task 4's call site (supervisor) and existing tracker tests to pass the new args (use `DateTimeOffset.UnixEpoch`, `TimeSpan.FromMinutes(5)`, `1f` where AFK is irrelevant).

- [ ] **Step 1: Add config options**

In `ConnectionOptions.cs`, append:

```csharp
    /// <summary>How long a member must be still (and online + alive) before being flagged AFK. Default 5m.</summary>
    public TimeSpan AfkThreshold { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Movement tolerance (world units) below which a member is considered still. Default 1.</summary>
    public float AfkEpsilon { get; set; } = 1f;
```

- [ ] **Step 2: Write the failing AFK tests**

`tests/RustPlusBot.Features.Connections.Tests/TeamStateTrackerAfkTests.cs`:

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamStateTrackerAfkTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);
    private const float Eps = 1f;

    private static TeamMemberSnapshot Member(ulong id, float x, float y, bool online = true, bool alive = true)
        => new(id, $"P{id}", x, y, online, alive, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static TeamInfoSnapshot Team(params TeamMemberSnapshot[] m) => new(1, m);

    [Fact]
    public void Still_for_threshold_emits_one_BecameAfk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);                       // prime
        Assert.Empty(t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(2), Threshold, Eps)); // still, < threshold
        var afk = t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps);     // crossed
        Assert.Single(afk, x => x.Kind == PlayerTransitionKind.BecameAfk && x.Location == (0f, 0f));
        Assert.Empty(t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(9), Threshold, Eps)); // latched, no repeat
    }

    [Fact]
    public void Moving_after_afk_emits_ReturnedFromAfk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps);          // BecameAfk
        var back = t.Diff(Team(Member(1, 50, 50)), t0.AddMinutes(7), Threshold, Eps);
        Assert.Single(back, x => x.Kind == PlayerTransitionKind.ReturnedFromAfk && x.Location == null);
    }

    [Fact]
    public void Going_offline_while_afk_emits_ReturnedFromAfk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        t.Diff(Team(Member(1, 0, 0)), t0.AddMinutes(6), Threshold, Eps);          // BecameAfk
        var off = t.Diff(Team(Member(1, 0, 0, online: false)), t0.AddMinutes(7), Threshold, Eps);
        Assert.Contains(off, x => x.Kind == PlayerTransitionKind.ReturnedFromAfk);
    }

    [Fact]
    public void Dead_member_is_never_afk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0, alive: false)), t0, Threshold, Eps);
        Assert.Empty(t.Diff(Team(Member(1, 0, 0, alive: false)), t0.AddMinutes(10), Threshold, Eps));
    }

    [Fact]
    public void Small_jitter_below_epsilon_still_counts_as_still()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        t.Diff(Team(Member(1, 0, 0)), t0, Threshold, Eps);
        var afk = t.Diff(Team(Member(1, 0.5f, 0.5f)), t0.AddMinutes(6), Threshold, Eps); // < 1 unit move
        Assert.Single(afk, x => x.Kind == PlayerTransitionKind.BecameAfk);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter TeamStateTrackerAfkTests`
Expected: FAIL (signature mismatch / no AFK behavior).

- [ ] **Step 4: Extend `TeamStateTracker`** — change the `Diff` signature and add AFK tracking

Replace the `Diff` signature and add AFK fields + logic. The full updated method:

```csharp
    private Dictionary<ulong, TeamMemberSnapshot>? _baseline;
    private readonly Dictionary<ulong, DateTimeOffset> _stillSince = new();
    private readonly HashSet<ulong> _afk = new();

    public IReadOnlyList<PlayerTransition> Diff(
        TeamInfoSnapshot? snapshot, DateTimeOffset now, TimeSpan afkThreshold, float afkEpsilon)
    {
        if (snapshot is null)
        {
            return [];
        }

        var current = snapshot.Members.ToDictionary(m => m.SteamId);

        if (_baseline is null)
        {
            _baseline = current;
            foreach (var m in current.Values)
            {
                _stillSince[m.SteamId] = now;
            }

            return [];
        }

        var transitions = new List<PlayerTransition>();
        foreach (var (id, nowMember) in current)
        {
            if (!_baseline.TryGetValue(id, out var was))
            {
                _stillSince[id] = now; // prime new member's stillness clock
                continue;
            }

            AddPresenceTransitions(transitions, id, was, nowMember, snapshot);
            UpdateAfk(transitions, id, was, nowMember, now, afkThreshold, afkEpsilon);
        }

        _baseline = current;
        return transitions;
    }

    private static void AddPresenceTransitions(
        List<PlayerTransition> transitions, ulong id,
        TeamMemberSnapshot was, TeamMemberSnapshot now, TeamInfoSnapshot snapshot)
    {
        if (now.IsOnline && !was.IsOnline)
        {
            transitions.Add(new PlayerTransition(PlayerTransitionKind.Connect, id, now.Name, null));
        }
        else if (!now.IsOnline && was.IsOnline)
        {
            transitions.Add(new PlayerTransition(PlayerTransitionKind.Disconnect, id, now.Name, null));
        }

        if (now.LastDeathTimeUtc > was.LastDeathTimeUtc)
        {
            transitions.Add(new PlayerTransition(
                PlayerTransitionKind.Death, id, now.Name, ResolveDeathLocation(id, snapshot, was)));
        }

        if (now.LastSpawnTimeUtc > was.LastSpawnTimeUtc)
        {
            transitions.Add(new PlayerTransition(PlayerTransitionKind.Respawn, id, now.Name, (now.X, now.Y)));
        }
    }

    private void UpdateAfk(
        List<PlayerTransition> transitions, ulong id,
        TeamMemberSnapshot was, TeamMemberSnapshot now, DateTimeOffset clock, TimeSpan threshold, float epsilon)
    {
        var eligible = now.IsOnline && now.IsAlive;
        if (!eligible)
        {
            if (_afk.Remove(id))
            {
                transitions.Add(new PlayerTransition(PlayerTransitionKind.ReturnedFromAfk, id, now.Name, null));
            }

            _stillSince[id] = clock;
            return;
        }

        var moved = Math.Abs(now.X - was.X) > epsilon || Math.Abs(now.Y - was.Y) > epsilon;
        if (moved)
        {
            _stillSince[id] = clock;
            if (_afk.Remove(id))
            {
                transitions.Add(new PlayerTransition(PlayerTransitionKind.ReturnedFromAfk, id, now.Name, null));
            }

            return;
        }

        var since = _stillSince.TryGetValue(id, out var s) ? s : clock;
        _stillSince.TryAdd(id, since);
        if (clock - since >= threshold && _afk.Add(id))
        {
            transitions.Add(new PlayerTransition(PlayerTransitionKind.BecameAfk, id, now.Name, (now.X, now.Y)));
        }
    }
```

(`ResolveDeathLocation` is unchanged from Task 4.)

- [ ] **Step 5: Update the Task 4 tests + supervisor call site for the new signature**

In `TeamStateTrackerTests.cs`, change every `tracker.Diff(x)` to `tracker.Diff(x, DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), 1f)`. (The presence assertions are unaffected because AFK needs time to advance, which these tests don't do.)

In `ConnectionSupervisor.cs`, change the Task 7 publish block to:

```csharp
                var team = await connection.GetTeamInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
                var transitions = tracker.Diff(team, clock.UtcNow, _options.AfkThreshold, _options.AfkEpsilon);
```

Add `IClock clock` to the supervisor primary constructor parameter list (after `eventBus`), and the `using RustPlusBot.Abstractions.Time;`.

- [ ] **Step 6: Update supervisor DI + tests that construct it**

`Program.cs` already registers `IClock` (line 29). Check `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs` and any test that `new ConnectionSupervisor(...)` — add an `IClock` substitute argument (`Substitute.For<IClock>()` returning a fixed `UtcNow`). Run the connections suite to find every call site.

- [ ] **Step 7: Run tests**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests`
Expected: PASS (existing + new AFK tests).

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Connections/ tests/RustPlusBot.Features.Connections.Tests/
git commit -m "feat(connections): AFK hysteresis tracking in TeamStateTracker"
```

---

### Task 10: `IAfkState` seam on the supervisor (TDD)

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Listening/IAfkState.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/TeamStateTracker.cs` (expose current AFK members)
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (implement `IAfkState`)
- Modify: `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs` (register `IAfkState` → supervisor singleton)
- Test: `tests/RustPlusBot.Features.Connections.Tests/AfkStateTests.cs`

**Interfaces:**

- Produces:
  - `record AfkMember(ulong SteamId, string Name, TimeSpan StillFor)`
  - `interface IAfkState { Task<IReadOnlyList<AfkMember>?> GetAfkMembersAsync(ulong guildId, Guid serverId, CancellationToken ct); }`
- Consumed by Task 11 (`AfkCommandHandler`).

> The supervisor keeps one `TeamStateTracker` per connected window. To answer `IAfkState`, the supervisor must hold a reference to the *current* tracker per `(guild, server)`. Store it in the existing `LiveSocket` record (or a parallel `ConcurrentDictionary`). Read `LiveSocket`'s definition in `ConnectionSupervisor.cs` and add a `TeamStateTracker Tracker` field, set when the poll loop starts.

- [ ] **Step 1: Expose AFK members from the tracker**

Add to `TeamStateTracker`:

```csharp
    /// <summary>The members currently flagged AFK and how long each has been still, as of <paramref name="now"/>.</summary>
    public IReadOnlyList<AfkMember> CurrentAfk(DateTimeOffset now)
    {
        var result = new List<AfkMember>();
        foreach (var id in _afk)
        {
            if (_baseline is not null && _baseline.TryGetValue(id, out var m))
            {
                var since = _stillSince.TryGetValue(id, out var s) ? s : now;
                result.Add(new AfkMember(id, m.Name, now - since));
            }
        }

        return result;
    }
```

- [ ] **Step 2: Create `IAfkState` + `AfkMember`**

`src/RustPlusBot.Features.Connections/Listening/IAfkState.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One currently-AFK team member.</summary>
/// <param name="SteamId">Steam64 id.</param>
/// <param name="Name">In-game display name.</param>
/// <param name="StillFor">How long the member has been continuously still.</param>
public sealed record AfkMember(ulong SteamId, string Name, TimeSpan StillFor);

/// <summary>Reads the live AFK state computed by the connection poll loop.</summary>
public interface IAfkState
{
    /// <summary>Gets the currently-AFK members, or null when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The currently-AFK members, or null when there is no live socket.</returns>
    Task<IReadOnlyList<AfkMember>?> GetAfkMembersAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Implement `IAfkState` on the supervisor**

Add `IAfkState` to the supervisor's interface list:

```csharp
    : IConnectionSupervisor, ITeamChatSender, IRustServerQuery, IAfkState, IAsyncDisposable
```

Hold the active tracker per connection. In `LiveSocket`, add a `TeamStateTracker Tracker` member; set it where the tracker is created. Then implement:

```csharp
    /// <inheritdoc />
    public Task<IReadOnlyList<AfkMember>?> GetAfkMembersAsync(
        ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        if (_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return Task.FromResult<IReadOnlyList<AfkMember>?>(live.Tracker.CurrentAfk(clock.UtcNow));
        }

        return Task.FromResult<IReadOnlyList<AfkMember>?>(null);
    }
```

> The tracker is created in `PollMarkersAsync` (Task 7). Move its construction up to where `LiveSocket` is created (just before `_liveSockets[key] = new LiveSocket(...)`) and pass it both into `LiveSocket` and into `PollMarkersAsync` as a parameter, so the same instance is shared.

- [ ] **Step 4: Register `IAfkState`**

In `ConnectionServiceCollectionExtensions.cs`, the supervisor is already a singleton backing multiple interfaces. Add the forwarding registration next to the others (find `IRustServerQuery`):

```csharp
        services.AddSingleton<IAfkState>(sp => (ConnectionSupervisor)sp.GetRequiredService<IConnectionSupervisor>());
```

(Match the exact existing forwarding style — it may use the concrete type or `IConnectionSupervisor`.)

- [ ] **Step 5: Write the AFK-state test**

`tests/RustPlusBot.Features.Connections.Tests/AfkStateTests.cs` — unit-test the tracker's `CurrentAfk` directly (the supervisor wiring is covered by registration + the harder integration path):

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class AfkStateTests
{
    [Fact]
    public void CurrentAfk_lists_member_with_still_duration()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        var m = new TeamMemberSnapshot(1, "Bob", 0, 0, true, true, t0, t0);
        t.Diff(new TeamInfoSnapshot(1, [m]), t0, TimeSpan.FromMinutes(5), 1f);
        t.Diff(new TeamInfoSnapshot(1, [m]), t0.AddMinutes(6), TimeSpan.FromMinutes(5), 1f); // BecameAfk

        var afk = t.CurrentAfk(t0.AddMinutes(6));
        var bob = Assert.Single(afk);
        Assert.Equal(1UL, bob.SteamId);
        Assert.Equal(TimeSpan.FromMinutes(6), bob.StillFor);
    }

    [Fact]
    public void CurrentAfk_empty_when_nobody_afk()
    {
        var t = new TeamStateTracker();
        var t0 = DateTimeOffset.UnixEpoch;
        var m = new TeamMemberSnapshot(1, "Bob", 0, 0, true, true, t0, t0);
        t.Diff(new TeamInfoSnapshot(1, [m]), t0, TimeSpan.FromMinutes(5), 1f);
        Assert.Empty(t.CurrentAfk(t0.AddMinutes(1)));
    }
}
```

- [ ] **Step 6: Build + test**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections/ tests/RustPlusBot.Features.Connections.Tests/AfkStateTests.cs
git commit -m "feat(connections): IAfkState read seam over the AFK tracker"
```

---

### Task 11: `!afk` command handler (TDD)

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/AfkCommandHandler.cs`
- Modify: `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs` (add keys)
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs` (register handler)
- Modify: `src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj` (ensure it references `Features.Connections` — it already does, since handlers inject `IRustServerQuery`)
- Test: `tests/RustPlusBot.Features.Commands.Tests/AfkCommandHandlerTests.cs`

**Interfaces:**

- Consumes: `IAfkState` (Task 10), `ICommandLocalizer`, `CommandContext`, `ICommandHandler`, `DurationFormat.Compact` (used by `AliveCommandHandler`).
- Produces: `AfkCommandHandler : ICommandHandler` with `Name => "afk"`.

- [ ] **Step 1: Add EN/FR command keys**

In `CommandLocalizationCatalog.cs`, add to the `["en"]` map:

```csharp
                ["command.afk.ok"] = "AFK: {0}",
                ["command.afk.none"] = "Nobody is AFK.",
                ["command.afk.member"] = "{0} ({1})",
```

and to `["fr"]`:

```csharp
                ["command.afk.ok"] = "AFK : {0}",
                ["command.afk.none"] = "Personne n'est AFK.",
                ["command.afk.member"] = "{0} ({1})",
```

> Reuse the existing `command.notconnected` key for the no-socket case (already present, used by `AliveCommandHandler`).

- [ ] **Step 2: Write the failing test**

`tests/RustPlusBot.Features.Commands.Tests/AfkCommandHandlerTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Tests;

public sealed class AfkCommandHandlerTests
{
    private readonly IAfkState _afk = Substitute.For<IAfkState>();
    private readonly ICommandLocalizer _localizer = new CommandLocalizer(CommandLocalizationCatalog.Default);

    private CommandContext Ctx() => new(1, Guid.NewGuid(), "en", 99, "Caller", []);

    [Fact]
    public async Task Name_is_afk() => Assert.Equal("afk", new AfkCommandHandler(_afk, _localizer).Name);

    [Fact]
    public async Task Reports_not_connected_when_state_null()
    {
        _afk.GetAfkMembersAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<AfkMember>?)null);
        var reply = await new AfkCommandHandler(_afk, _localizer).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal(_localizer.Get("command.notconnected", "en"), reply);
    }

    [Fact]
    public async Task Reports_none_when_empty()
    {
        _afk.GetAfkMembersAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns([]);
        var reply = await new AfkCommandHandler(_afk, _localizer).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal(_localizer.Get("command.afk.none", "en"), reply);
    }

    [Fact]
    public async Task Lists_afk_members_with_durations()
    {
        _afk.GetAfkMembersAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new List<AfkMember> { new(1, "Bob", TimeSpan.FromMinutes(6)) });
        var reply = await new AfkCommandHandler(_afk, _localizer).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Contains("Bob", reply);
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter AfkCommandHandlerTests`
Expected: FAIL — `AfkCommandHandler` does not exist.

- [ ] **Step 4: Implement `AfkCommandHandler`** (mirror `AliveCommandHandler`)

`src/RustPlusBot.Features.Commands/Handlers/AfkCommandHandler.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!afk — lists team members who have been still (online + alive) past the AFK threshold.</summary>
/// <param name="afk">The live AFK state.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class AfkCommandHandler(IAfkState afk, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "afk";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var members = await afk.GetAfkMembersAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (members is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        if (members.Count == 0)
        {
            return localizer.Get("command.afk.none", context.Culture);
        }

        var parts = members
            .OrderByDescending(m => m.StillFor)
            .Select(m => localizer.Get(
                "command.afk.member", context.Culture, m.Name, DurationFormat.Compact(m.StillFor)));
        return localizer.Get("command.afk.ok", context.Culture, string.Join(", ", parts));
    }
}
```

- [ ] **Step 5: Register the handler**

In `CommandServiceCollectionExtensions.cs`, after `services.AddScoped<ICommandHandler, AliveCommandHandler>();` add:

```csharp
        services.AddScoped<ICommandHandler, AfkCommandHandler>();
```

- [ ] **Step 6: Run tests**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter AfkCommandHandlerTests`
Expected: PASS.

> The command-tests DI provider must supply `IAfkState`. The handler tests construct the handler directly (no DI), so they pass as-is. If a `CommandRegistrationTests` builds a provider and resolves all handlers, register an `IAfkState` substitute there.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers/AfkCommandHandler.cs \
        src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs \
        src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs \
        tests/RustPlusBot.Features.Commands.Tests/AfkCommandHandlerTests.cs
git commit -m "feat(commands): in-game !afk command over IAfkState"
```

---

### Task 12: Full-suite verification + help/catalog wiring

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs` (add `!afk` help line if other commands are listed there)
- Modify: `docs/product/feature-catalog.md` (already edited in the spec phase — confirm `!afk` row + 3d row reflect Done; **do not git add**, it is gitignored)

**Interfaces:**

- Consumes everything. Verification task.

- [ ] **Step 1: Add `!afk` to the in-game help catalog**

Read `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`. If it enumerates commands (for `!help`/`/help`), add an `afk` entry with an EN/FR description matching the existing style. If `!afk` should appear, add it; otherwise skip this step.

- [ ] **Step 2: Run the entire test suite**

Run: `dotnet test`
Expected: PASS (all projects).

- [ ] **Step 3: Run the format/lint check used by CI**

Run: `dotnet build /p:TreatWarningsAsErrors=true` (or the repo's ReSharper/format check command referenced in CI).
Expected: clean. Fix any analyzer warnings (e.g. missing XML docs on public members — `IAfkState`, `AfkMember`, `PlayerTransition*` are public and need `///` docs, which the code above includes).

- [ ] **Step 4: Manual smoke (optional, if a live server is available)**

Connect a server, have a teammate connect/disconnect/die/respawn and stand still for the threshold; confirm `#events` embeds + in-game lines, and `!afk` output. Lower `AfkThreshold` via config for the test.

- [ ] **Step 5: Final commit**

```bash
git add src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs
git commit -m "docs(commands): list !afk in the help catalog"
```

---

## Self-Review

**Spec coverage:**

- §1 scope (team-only, always-on) → Tasks 4, 7 (no toggle). ✓
- §2 detection (prime, brand-new silent, null-skip, 4 transitions) → Task 4. ✓
- §2 death-location resolution (leader DeathNote → prev position → null) → Task 1 (facade) + Task 4 (`ResolveDeathLocation`). ✓
- §2a AFK (definition, hysteresis, two kinds, `IAfkState`, config) → Tasks 9, 10. ✓
- §3 slice (enum, transition, event, relay, renderer, catalog, hosted, DI) → Tasks 2, 5, 6, 7. ✓
- §3 location-only-on-death/respawn/became-afk → Task 6 renderer `Describe`. ✓
- §3 `!afk` command → Task 11. ✓
- §4 error handling (poll skip, consumer broad-catch, null channel) → Task 4 (null→empty), Task 7 (hosted catch), Task 6 (channel null guard). ✓
- §5 testing (all enumerated cases) → Tasks 4, 5, 6, 8, 9, 10, 11. ✓
- §6 catalog update → spec phase (gitignored) + Task 12 confirm. ✓

**Placeholder scan:** No TBD/TODO. Every code step has full code. The two "read the existing file and copy" steps (poster in Task 6, `LiveSocket` in Task 10) name the exact file and what to copy. ✓

**Type consistency:** `Diff` signature changes in Task 9 — flagged explicitly with the call-site + test-update steps (Task 9 Steps 5–6). `PlayerTransition.Location` is `(float X, float Y)?` throughout. `IAfkState.GetAfkMembersAsync` returns `IReadOnlyList<AfkMember>?` in Tasks 10 and 11. `AfkMember(ulong, string, TimeSpan)` consistent. ✓

One known signature risk: upstream `TeamInfo.DeathNote` property name/shape (Task 1 Step 4 has a verification fallback). `IWorkspaceStore.GetCultureAsync` and `MapDimensions` constructor are flagged for verification in Task 6.
