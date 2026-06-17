# 3b-ii Team-intel Commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add six in-game team-intelligence `!commands` (`!online`, `!offline`, `!team`, `!alive`, `!prox`, `!steamid`) that read a single live `GetTeamInfoAsync` snapshot from the connected Rust+ socket.

**Architecture:** Extend the existing `IRustServerQuery` seam (the path `!pop`/`!time`/`!wipe` already use) with `GetTeamInfoAsync` returning a new boundary DTO `TeamInfoSnapshot`, implemented on `ConnectionSupervisor` over its `_liveSockets` registry and on the untested `RustPlusSocketSource` shim. Six new `ICommandHandler` classes consume it, each producing one compact comma-joined localized line. No new entities, migrations, events, options, background services, or projects.

**Tech Stack:** .NET 10, C#, Discord.Net, RustPlusApi 2.0.0-beta.1, xUnit + NSubstitute, EF Core / SQLite (read-only here — no schema change). Strict Roslynator analyzers; jb `ReformatAndReorder` is the format gate.

**Spec:** `docs/superpowers/specs/2026-06-17-rustplusbot-3b-ii-team-intel-design.md`

**Branch:** `feat/team-intel` (already created off `develop`; the spec is already committed there).

---

## File Structure

**Connections (the seam):**

- Create `src/RustPlusBot.Features.Connections/Listening/TeamInfoSnapshot.cs` — boundary DTOs `TeamInfoSnapshot` + `TeamMemberSnapshot`.
- Modify `src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs` — add `GetTeamInfoAsync` (public).
- Modify `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs` — add `GetTeamInfoAsync` (internal).
- Modify `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` — implement on both `RejectedConnection` and `RustPlusServerConnection`.
- Modify `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` — implement `IRustServerQuery.GetTeamInfoAsync`.

**Commands (helpers + handlers):**

- Create `src/RustPlusBot.Features.Commands/Formatting/Distance.cs` — pure distance helper.
- Create `src/RustPlusBot.Features.Commands/Formatting/TeamMemberFilter.cs` — shared partial name match.
- Create `src/RustPlusBot.Features.Commands/Handlers/OnlineCommandHandler.cs`
- Create `src/RustPlusBot.Features.Commands/Handlers/OfflineCommandHandler.cs`
- Create `src/RustPlusBot.Features.Commands/Handlers/TeamCommandHandler.cs`
- Create `src/RustPlusBot.Features.Commands/Handlers/SteamIdCommandHandler.cs`
- Create `src/RustPlusBot.Features.Commands/Handlers/AliveCommandHandler.cs`
- Create `src/RustPlusBot.Features.Commands/Handlers/ProxCommandHandler.cs`
- Modify `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs` — add EN/FR keys.
- Modify `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs` — register the six handlers.

**Tests / fakes:**

- Modify `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` — add `TeamResult` + `GetTeamInfoAsync`.
- Modify `tests/RustPlusBot.Features.Connections.Tests/ServerQueryTests.cs` — supervisor `GetTeamInfoAsync` coverage.
- Create `tests/RustPlusBot.Features.Commands.Tests/Formatting/DistanceTests.cs`
- Create `tests/RustPlusBot.Features.Commands.Tests/Formatting/TeamMemberFilterTests.cs`
- Create `tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs` — all six handlers.

---

## Task 1: Boundary DTOs (`TeamInfoSnapshot`, `TeamMemberSnapshot`)

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Listening/TeamInfoSnapshot.cs`

- [ ] **Step 1: Create the DTO file**

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A point-in-time view of a team, decoupled from RustPlusApi types.</summary>
/// <param name="LeaderSteamId">Steam64 id of the current team leader.</param>
/// <param name="Members">Status snapshots for all team members.</param>
public sealed record TeamInfoSnapshot(ulong LeaderSteamId, IReadOnlyList<TeamMemberSnapshot> Members);

/// <summary>A point-in-time view of one team member.</summary>
/// <param name="SteamId">Steam64 id of the member.</param>
/// <param name="Name">In-game display name (empty when the game reports none).</param>
/// <param name="X">Horizontal map coordinate (west to east).</param>
/// <param name="Y">Vertical map coordinate (south to north).</param>
/// <param name="IsOnline">Whether the member is currently connected.</param>
/// <param name="IsAlive">Whether the member is currently alive.</param>
/// <param name="LastSpawnTimeUtc">UTC time of the member's last spawn.</param>
/// <param name="LastDeathTimeUtc">UTC time of the member's last death.</param>
public sealed record TeamMemberSnapshot(
    ulong SteamId,
    string Name,
    float X,
    float Y,
    bool IsOnline,
    bool IsAlive,
    DateTimeOffset LastSpawnTimeUtc,
    DateTimeOffset LastDeathTimeUtc);
```

- [ ] **Step 2: Build the Connections project**

Run: `dotnet build src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj`
Expected: build succeeds, 0 warnings / 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Listening/TeamInfoSnapshot.cs
git commit -m "feat(connections): add TeamInfoSnapshot/TeamMemberSnapshot DTOs"
```

---

## Task 2: Extend the seam interfaces

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`

- [ ] **Step 1: Add `GetTeamInfoAsync` to the public `IRustServerQuery`**

In `IRustServerQuery.cs`, after the existing `GetTimeAsync` method (before the closing brace), add:

```csharp
    /// <summary>Gets a team snapshot, or null when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A team snapshot, or null when there is no live socket.</returns>
    Task<TeamInfoSnapshot?> GetTeamInfoAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
```

- [ ] **Step 2: Add `GetTeamInfoAsync` to the internal `IRustServerConnection`**

In `IRustServerConnection.cs`, after the existing `GetTimeAsync` method (the one returning `Task<ServerTimeSnapshot?>`), add:

```csharp
    /// <summary>Gets a team snapshot, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A team snapshot, or null on failure/timeout.</returns>
    Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);
```

- [ ] **Step 3: Build the Connections project (expect it to FAIL)**

Run: `dotnet build src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj`
Expected: FAIL — `RustPlusSocketSource` (`RejectedConnection` + `RustPlusServerConnection`) and `ConnectionSupervisor` do not yet implement the new members. This confirms the interface change is wired in. Tasks 3 and 4 fix the build.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs
git commit -m "feat(connections): add GetTeamInfoAsync to query/connection seams"
```

---

## Task 3: Implement `GetTeamInfoAsync` on `RustPlusSocketSource` (untested shim)

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`

This is the integration shim — by design it has no unit tests. Mapping verified against the real 2.0.0-beta.1 DLL: `RustPlus.GetTeamInfoAsync(CancellationToken)` returns `Task<Response<TeamInfo?>>`; `TeamInfo.Members` is `IEnumerable<MemberInfo>?`; each `MemberInfo` has `SteamId`/`Name?`/`X`/`Y`/`IsOnline`/`IsAlive`/`LastSpawnTime` (DateTime, UTC)/`LastDeathTime` (DateTime, UTC).

- [ ] **Step 1: Add the no-op override on `RejectedConnection`**

In `RustPlusSocketSource.cs`, inside the `RejectedConnection` class, after its `GetTimeAsync` method, add:

```csharp
        public Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult<TeamInfoSnapshot?>(null);
```

- [ ] **Step 2: Add the real mapping on `RustPlusServerConnection`**

In `RustPlusSocketSource.cs`, inside the `RustPlusServerConnection` class, immediately after the existing `GetTimeAsync` method (the one ending at the `LogQueryFailed`-returning catch), add:

```csharp
        public async Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.1): GetTeamInfoAsync returns Task<Response<TeamInfo?>>; Response.IsSuccess/.Data.
                // TeamInfo.LeaderSteamId (ulong), TeamInfo.Members (IEnumerable<MemberInfo>?).
                // MemberInfo: SteamId/Name?/X/Y/IsOnline/IsAlive/LastSpawnTime(DateTime,UTC)/LastDeathTime(DateTime,UTC).
                var response = await _rustPlus.GetTeamInfoAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                {
                    return null;
                }

                var members = (response.Data.Members ?? [])
                    .Select(m => new TeamMemberSnapshot(
                        m.SteamId,
                        m.Name ?? string.Empty,
                        m.X,
                        m.Y,
                        m.IsOnline,
                        m.IsAlive,
                        new DateTimeOffset(DateTime.SpecifyKind(m.LastSpawnTime, DateTimeKind.Utc)),
                        new DateTimeOffset(DateTime.SpecifyKind(m.LastDeathTime, DateTimeKind.Utc))))
                    .ToList();
                return new TeamInfoSnapshot(response.Data.LeaderSteamId, members);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
#pragma warning disable CA1031 // Broad catch: any team-query failure maps to null; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return null;
            }
        }
```

Note: this requires the using `RustPlusApi.Data` types to be reachable. `MemberInfo`/`TeamInfo` live in namespace `RustPlusApi.Data`; the file already has `using RustPlusApi;`. The `m =>` lambda references only `m`'s members so no extra `using` is needed (the element type is inferred). If the compiler complains it cannot find the member type, add `using RustPlusApi.Data;` at the top of the file.

- [ ] **Step 3: Build the Connections project**

Run: `dotnet build src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj`
Expected: still FAILS, but now only on `ConnectionSupervisor` (missing `IRustServerQuery.GetTeamInfoAsync`). `RustPlusSocketSource` errors are gone. (Task 4 finishes the build.)

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs
git commit -m "feat(connections): map GetTeamInfoAsync in RustPlusSocketSource shim"
```

---

## Task 4: Implement `GetTeamInfoAsync` on `ConnectionSupervisor`

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ServerQueryTests.cs`

- [ ] **Step 1: Add `TeamResult` + `GetTeamInfoAsync` to the fake connection**

In `FakeRustSocketSource.cs`, inside `FakeConnection`, after the `TimeResult` property add a `TeamResult` property (default a non-null empty snapshot, mirroring the other two defaults):

```csharp
        /// <summary>The snapshot returned by <see cref="GetTeamInfoAsync"/>. Defaults to a non-null empty snapshot.</summary>
        public TeamInfoSnapshot? TeamResult { get; set; } = new(0UL, []);
```

Then, after the existing `GetTimeAsync` method in `FakeConnection`, add:

```csharp
        public Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(TeamResult);
```

- [ ] **Step 2: Write the failing supervisor tests**

In `ServerQueryTests.cs`, add two tests (after `GetTime_ReturnsNull_WhenNoLiveSocket`, before the private `WaitUntilAsync`):

```csharp
    [Fact]
    public async Task GetTeamInfo_ReturnsSnapshot_WhenConnected()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;
        var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

        source.LastConnection!.TeamResult = new TeamInfoSnapshot(
            555UL,
            [new TeamMemberSnapshot(555UL, "alice", 1f, 2f, true, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch)]);
        var snapshot = await supervisor.GetTeamInfoAsync(10UL, serverId, cts.Token);

        Assert.NotNull(snapshot);
        Assert.Equal(555UL, snapshot.LeaderSteamId);
        Assert.Single(snapshot.Members);
        Assert.Equal("alice", snapshot.Members[0].Name);
        await supervisor.StopAllAsync();
    }

    [Fact]
    public async Task GetTeamInfo_ReturnsNull_WhenNoLiveSocket()
    {
        var source = new FakeRustSocketSource();
        var (provider, supervisor) = CreateHarness(source);
        await using var _ = provider;

        var snapshot = await supervisor.GetTeamInfoAsync(10UL, Guid.NewGuid(), CancellationToken.None);

        Assert.Null(snapshot);
    }
```

- [ ] **Step 3: Run the tests to verify they fail to compile**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj`
Expected: build FAIL — `ConnectionSupervisor` has no `GetTeamInfoAsync`.

- [ ] **Step 4: Implement it on the supervisor**

In `ConnectionSupervisor.cs`, immediately after the existing `GetTimeAsync` method (ends at line ~154), add:

```csharp
    /// <inheritdoc />
    public async Task<TeamInfoSnapshot?> GetTeamInfoAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        return await live.Connection.GetTeamInfoAsync(_options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj`
Expected: PASS — all Connections tests green (read the count; should be the prior count + 2).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs tests/RustPlusBot.Features.Connections.Tests/ServerQueryTests.cs
git commit -m "feat(connections): implement supervisor GetTeamInfoAsync over live sockets"
```

---

## Task 5: `Distance` helper

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Formatting/Distance.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Formatting/DistanceTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.Features.Commands.Formatting;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class DistanceTests
{
    [Fact]
    public void Between_ReturnsEuclideanDistance_Rounded()
    {
        Assert.Equal(5, Distance.Between(0f, 0f, 3f, 4f)); // 3-4-5 triangle
    }

    [Fact]
    public void Between_ReturnsZero_ForSamePoint()
    {
        Assert.Equal(0, Distance.Between(10f, 10f, 10f, 10f));
    }

    [Fact]
    public void Between_RoundsToNearestInteger()
    {
        Assert.Equal(1, Distance.Between(0f, 0f, 1f, 0f));
        Assert.Equal(141, Distance.Between(0f, 0f, 100f, 100f)); // 141.42 -> 141
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~DistanceTests"`
Expected: build FAIL — `Distance` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Computes planar distances between map coordinates for in-game replies.</summary>
internal static class Distance
{
    /// <summary>Euclidean distance between two map points, rounded to whole metres.</summary>
    /// <param name="x1">First point's horizontal coordinate.</param>
    /// <param name="y1">First point's vertical coordinate.</param>
    /// <param name="x2">Second point's horizontal coordinate.</param>
    /// <param name="y2">Second point's vertical coordinate.</param>
    /// <returns>The distance rounded to the nearest whole number.</returns>
    public static int Between(float x1, float y1, float x2, float y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return (int)Math.Round(Math.Sqrt((dx * dx) + (dy * dy)), MidpointRounding.AwayFromZero);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~DistanceTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Formatting/Distance.cs tests/RustPlusBot.Features.Commands.Tests/Formatting/DistanceTests.cs
git commit -m "feat(commands): add Distance helper for !prox"
```

---

## Task 6: `TeamMemberFilter` helper (shared partial name match)

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Formatting/TeamMemberFilter.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Formatting/TeamMemberFilterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class TeamMemberFilterTests
{
    private static readonly IReadOnlyList<TeamMemberSnapshot> Members =
    [
        Member(1UL, "Alice"),
        Member(2UL, "Bob"),
        Member(3UL, "Bobby"),
    ];

    private static TeamMemberSnapshot Member(ulong id, string name) =>
        new(id, name, 0f, 0f, true, true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    [Fact]
    public void ByName_NullArg_ReturnsAll()
    {
        Assert.Equal(3, TeamMemberFilter.ByName(Members, null).Count);
    }

    [Fact]
    public void ByName_EmptyArg_ReturnsAll()
    {
        Assert.Equal(3, TeamMemberFilter.ByName(Members, "  ").Count);
    }

    [Fact]
    public void ByName_PartialCaseInsensitive_MatchesSubstring()
    {
        var result = TeamMemberFilter.ByName(Members, "bob");
        Assert.Equal(2, result.Count); // Bob + Bobby
        Assert.Contains(result, m => m.Name == "Bob");
        Assert.Contains(result, m => m.Name == "Bobby");
    }

    [Fact]
    public void ByName_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(TeamMemberFilter.ByName(Members, "zed"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamMemberFilterTests"`
Expected: build FAIL — `TeamMemberFilter` does not exist.

- [ ] **Step 3: Write the implementation**

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Filters team members by an optional name argument for !prox and !steamid.</summary>
internal static class TeamMemberFilter
{
    /// <summary>Returns all members when <paramref name="nameArg"/> is null/whitespace,
    /// otherwise those whose name contains it (case-insensitive).</summary>
    /// <param name="members">The members to filter.</param>
    /// <param name="nameArg">The optional partial name filter.</param>
    /// <returns>The matching members (all when no filter is given).</returns>
    public static IReadOnlyList<TeamMemberSnapshot> ByName(
        IReadOnlyList<TeamMemberSnapshot> members,
        string? nameArg)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (string.IsNullOrWhiteSpace(nameArg))
        {
            return members;
        }

        return members
            .Where(m => m.Name.Contains(nameArg, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamMemberFilterTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Formatting/TeamMemberFilter.cs tests/RustPlusBot.Features.Commands.Tests/Formatting/TeamMemberFilterTests.cs
git commit -m "feat(commands): add TeamMemberFilter for partial name matching"
```

---

## Task 7: Localization keys

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs`

All six handlers depend on these keys. Add them now so handler tests can assert exact strings.

- [ ] **Step 1: Add the EN keys**

In `CommandLocalizationCatalog.cs`, inside the `["en"]` dictionary, after the `["command.wipe.unknown"]` line, add:

```csharp
                ["command.online.ok"] = "Online ({0}): {1}",
                ["command.online.none"] = "No one is online.",
                ["command.offline.ok"] = "Offline ({0}): {1}",
                ["command.offline.none"] = "Everyone is online.",
                ["command.team.ok"] = "Team ({0}): {1}",
                ["command.team.none"] = "No team members.",
                ["command.team.nomatch"] = "No teammate matches '{0}'.",
                ["command.steamid.ok"] = "{0}",
                ["command.alive.ok"] = "Alive: {0}",
                ["command.alive.dead"] = "{0} dead",
                ["command.alive.member"] = "{0} {1}",
                ["command.prox.ok"] = "Prox: {0}",
                ["command.prox.member"] = "{0} {1}m",
                ["command.prox.selfunknown"] = "Can't locate you.",
```

- [ ] **Step 2: Add the FR keys**

Inside the `["fr"]` dictionary, after the `["command.wipe.unknown"]` line, add:

```csharp
                ["command.online.ok"] = "En ligne ({0}) : {1}",
                ["command.online.none"] = "Personne n'est en ligne.",
                ["command.offline.ok"] = "Hors ligne ({0}) : {1}",
                ["command.offline.none"] = "Tout le monde est en ligne.",
                ["command.team.ok"] = "Équipe ({0}) : {1}",
                ["command.team.none"] = "Aucun membre d'équipe.",
                ["command.team.nomatch"] = "Aucun coéquipier ne correspond à « {0} ».",
                ["command.steamid.ok"] = "{0}",
                ["command.alive.ok"] = "En vie : {0}",
                ["command.alive.dead"] = "{0} mort",
                ["command.alive.member"] = "{0} {1}",
                ["command.prox.ok"] = "Prox : {0}",
                ["command.prox.member"] = "{0} {1}m",
                ["command.prox.selfunknown"] = "Impossible de vous localiser.",
```

- [ ] **Step 3: Build the Commands project**

Run: `dotnet build src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj`
Expected: build succeeds, 0/0.

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs
git commit -m "feat(commands): add EN/FR strings for team-intel commands"
```

---

## Task 8: `!online` and `!offline` handlers

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/OnlineCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/OfflineCommandHandler.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs`

- [ ] **Step 1: Create the shared handler test file with the first tests**

Create `tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class TeamIntelHandlersTests
{
    private static readonly ICommandLocalizer Loc = new CommandLocalizer(CommandLocalizationCatalog.Default);

    // Caller alice = steamId 7, present in every snapshot at (0,0).
    private static CommandContext Ctx(params string[] args) => new(1, ServerId, "en", 7UL, "alice", args);
    private static readonly Guid ServerId = Guid.NewGuid();

    private static TeamMemberSnapshot Member(
        ulong id, string name, bool online = true, bool alive = true,
        float x = 0f, float y = 0f, DateTimeOffset? spawn = null) =>
        new(id, name, x, y, online, alive, spawn ?? DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static IRustServerQuery QueryReturning(TeamInfoSnapshot? snapshot)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(snapshot);
        return query;
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    [Fact]
    public async Task Online_ListsOnlineMembers()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice", online: true),
            Member(8UL, "bob", online: true),
            Member(9UL, "carl", online: false),
        ]));
        var reply = await new OnlineCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Online (2): alice, bob", reply);
    }

    [Fact]
    public async Task Online_None_WhenNobodyOnline()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL, [Member(8UL, "bob", online: false)]));
        var reply = await new OnlineCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No one is online.", reply);
    }

    [Fact]
    public async Task Online_NotConnected_WhenNull()
    {
        var reply = await new OnlineCommandHandler(QueryReturning(null), Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Not connected to the server.", reply);
    }

    [Fact]
    public async Task Offline_ListsOfflineMembers()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice", online: true),
            Member(9UL, "carl", online: false),
        ]));
        var reply = await new OfflineCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Offline (1): carl", reply);
    }

    [Fact]
    public async Task Offline_None_WhenEveryoneOnline()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL, [Member(7UL, "alice", online: true)]));
        var reply = await new OfflineCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Everyone is online.", reply);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamIntelHandlersTests"`
Expected: build FAIL — `OnlineCommandHandler`/`OfflineCommandHandler` do not exist.

- [ ] **Step 3: Create `OnlineCommandHandler`**

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!online — lists currently-connected teammates.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class OnlineCommandHandler(IRustServerQuery query, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "online";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var team = await query.GetTeamInfoAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        var names = team.Members.Where(m => m.IsOnline).Select(m => m.Name).ToList();
        if (names.Count == 0)
        {
            return localizer.Get("command.online.none", context.Culture);
        }

        return localizer.Get("command.online.ok", context.Culture, names.Count, string.Join(", ", names));
    }
}
```

- [ ] **Step 4: Create `OfflineCommandHandler`**

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!offline — lists teammates not currently connected.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class OfflineCommandHandler(IRustServerQuery query, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "offline";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var team = await query.GetTeamInfoAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        var names = team.Members.Where(m => !m.IsOnline).Select(m => m.Name).ToList();
        if (names.Count == 0)
        {
            return localizer.Get("command.offline.none", context.Culture);
        }

        return localizer.Get("command.offline.ok", context.Culture, names.Count, string.Join(", ", names));
    }
}
```

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamIntelHandlersTests"`
Expected: PASS (5 tests so far).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers/OnlineCommandHandler.cs src/RustPlusBot.Features.Commands/Handlers/OfflineCommandHandler.cs tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs
git commit -m "feat(commands): add !online and !offline handlers"
```

---

## Task 9: `!team` and `!steamid` handlers

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/TeamCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/SteamIdCommandHandler.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs`

- [ ] **Step 1: Add the failing tests**

Append these tests inside the `TeamIntelHandlersTests` class:

```csharp
    [Fact]
    public async Task Team_ListsAllMembers()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice"),
            Member(8UL, "bob", online: false),
        ]));
        var reply = await new TeamCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Team (2): alice, bob", reply);
    }

    [Fact]
    public async Task Team_None_WhenNoMembers()
    {
        var reply = await new TeamCommandHandler(QueryReturning(new TeamInfoSnapshot(0UL, [])), Loc)
            .ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No team members.", reply);
    }

    [Fact]
    public async Task SteamId_NoArg_ListsAll()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice"),
            Member(8UL, "bob"),
        ]));
        var reply = await new SteamIdCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("alice 7, bob 8", reply);
    }

    [Fact]
    public async Task SteamId_NameArg_FiltersToOne()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice"),
            Member(8UL, "bob"),
        ]));
        var reply = await new SteamIdCommandHandler(query, Loc).ExecuteAsync(Ctx("bob"), CancellationToken.None);
        Assert.Equal("bob 8", reply);
    }

    [Fact]
    public async Task SteamId_NameArg_NoMatch_ReportsNoMatch()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL, [Member(7UL, "alice")]));
        var reply = await new SteamIdCommandHandler(query, Loc).ExecuteAsync(Ctx("zed"), CancellationToken.None);
        Assert.Equal("No teammate matches 'zed'.", reply);
    }

    [Fact]
    public async Task SteamId_None_WhenNoMembers()
    {
        var reply = await new SteamIdCommandHandler(QueryReturning(new TeamInfoSnapshot(0UL, [])), Loc)
            .ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No team members.", reply);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamIntelHandlersTests"`
Expected: build FAIL — handlers do not exist.

- [ ] **Step 3: Create `TeamCommandHandler`**

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!team — lists every team member's name.</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class TeamCommandHandler(IRustServerQuery query, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "team";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var team = await query.GetTeamInfoAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        if (team.Members.Count == 0)
        {
            return localizer.Get("command.team.none", context.Culture);
        }

        var names = team.Members.Select(m => m.Name).ToList();
        return localizer.Get("command.team.ok", context.Culture, names.Count, string.Join(", ", names));
    }
}
```

- [ ] **Step 4: Create `SteamIdCommandHandler`**

```csharp
using System.Globalization;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!steamid [name] — reports teammates' Steam ids (all, or filtered by partial name).</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class SteamIdCommandHandler(IRustServerQuery query, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "steamid";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var team = await query.GetTeamInfoAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        if (team.Members.Count == 0)
        {
            return localizer.Get("command.team.none", context.Culture);
        }

        var nameArg = context.Args.Count > 0 ? context.Args[0] : null;
        var matches = TeamMemberFilter.ByName(team.Members, nameArg);
        if (matches.Count == 0)
        {
            return localizer.Get("command.team.nomatch", context.Culture, nameArg ?? string.Empty);
        }

        var pairs = matches.Select(m =>
            string.Create(CultureInfo.InvariantCulture, $"{m.Name} {m.SteamId}"));
        return localizer.Get("command.steamid.ok", context.Culture, string.Join(", ", pairs));
    }
}
```

- [ ] **Step 5: Run to verify the tests pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamIntelHandlersTests"`
Expected: PASS (11 tests so far).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers/TeamCommandHandler.cs src/RustPlusBot.Features.Commands/Handlers/SteamIdCommandHandler.cs tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs
git commit -m "feat(commands): add !team and !steamid handlers"
```

---

## Task 10: `!alive` handler

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/AliveCommandHandler.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs`

`!alive` shows alive members sorted by survival (now − LastSpawnTimeUtc) descending, each as `"<name> <duration>"` via `DurationFormat.Compact`, then dead members as `"<name> dead"`. Uses `IClock`.

- [ ] **Step 1: Add the failing tests**

Append inside `TeamIntelHandlersTests`:

```csharp
    [Fact]
    public async Task Alive_SortsBySurvivalDesc_ThenDeadLast()
    {
        var clock = new TestClock { UtcNow = DateTimeOffset.UnixEpoch.AddHours(3) };
        // carl spawned at epoch -> 3h survival; alice spawned at +2h -> 1h survival; bob dead.
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice", alive: true, spawn: DateTimeOffset.UnixEpoch.AddHours(2)),
            Member(8UL, "bob", alive: false),
            Member(9UL, "carl", alive: true, spawn: DateTimeOffset.UnixEpoch),
        ]));
        var reply = await new AliveCommandHandler(query, Loc, clock).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Alive: carl 3h 0m, alice 1h 0m, bob dead", reply);
    }

    [Fact]
    public async Task Alive_None_WhenNoMembers()
    {
        var reply = await new AliveCommandHandler(QueryReturning(new TeamInfoSnapshot(0UL, [])), Loc, new TestClock())
            .ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No team members.", reply);
    }

    [Fact]
    public async Task Alive_NotConnected_WhenNull()
    {
        var reply = await new AliveCommandHandler(QueryReturning(null), Loc, new TestClock())
            .ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Not connected to the server.", reply);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamIntelHandlersTests"`
Expected: build FAIL — `AliveCommandHandler` does not exist.

- [ ] **Step 3: Create `AliveCommandHandler`**

```csharp
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!alive — reports per-member survival times (longest first); dead members shown as "dead".</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
/// <param name="clock">The clock used to compute survival durations.</param>
internal sealed class AliveCommandHandler(IRustServerQuery query, ICommandLocalizer localizer, IClock clock)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "alive";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var team = await query.GetTeamInfoAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        if (team.Members.Count == 0)
        {
            return localizer.Get("command.team.none", context.Culture);
        }

        var now = clock.UtcNow;
        var alive = team.Members
            .Where(m => m.IsAlive)
            .OrderByDescending(m => now - m.LastSpawnTimeUtc)
            .Select(m => localizer.Get(
                "command.alive.member", context.Culture,
                m.Name, DurationFormat.Compact(now - m.LastSpawnTimeUtc)));
        var dead = team.Members
            .Where(m => !m.IsAlive)
            .Select(m => localizer.Get("command.alive.dead", context.Culture, m.Name));

        var parts = alive.Concat(dead).ToList();
        return localizer.Get("command.alive.ok", context.Culture, string.Join(", ", parts));
    }
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamIntelHandlersTests"`
Expected: PASS (14 tests so far).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers/AliveCommandHandler.cs tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs
git commit -m "feat(commands): add !alive handler with per-member survival times"
```

---

## Task 11: `!prox` handler

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/ProxCommandHandler.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs`

`!prox [name]` finds the caller's own position in the snapshot (match `SenderSteamId`), then reports the distance to each OTHER member (`"<name> <metres>m"`), optionally filtered to one by partial name. If the caller is not in the snapshot → `command.prox.selfunknown`. If a name arg matches nobody → `command.team.nomatch`.

- [ ] **Step 1: Add the failing tests**

Append inside `TeamIntelHandlersTests`:

```csharp
    [Fact]
    public async Task Prox_ListsDistancesToOtherMembers()
    {
        // caller alice (steamId 7) at (0,0); bob at (3,4) -> 5m; carl at (0,10) -> 10m.
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice", x: 0f, y: 0f),
            Member(8UL, "bob", x: 3f, y: 4f),
            Member(9UL, "carl", x: 0f, y: 10f),
        ]));
        var reply = await new ProxCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Prox: bob 5m, carl 10m", reply);
    }

    [Fact]
    public async Task Prox_NameArg_FiltersToOne()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice", x: 0f, y: 0f),
            Member(8UL, "bob", x: 3f, y: 4f),
        ]));
        var reply = await new ProxCommandHandler(query, Loc).ExecuteAsync(Ctx("bob"), CancellationToken.None);
        Assert.Equal("Prox: bob 5m", reply);
    }

    [Fact]
    public async Task Prox_NameArg_NoMatch_ReportsNoMatch()
    {
        var query = QueryReturning(new TeamInfoSnapshot(7UL,
        [
            Member(7UL, "alice", x: 0f, y: 0f),
            Member(8UL, "bob", x: 3f, y: 4f),
        ]));
        var reply = await new ProxCommandHandler(query, Loc).ExecuteAsync(Ctx("zed"), CancellationToken.None);
        Assert.Equal("No teammate matches 'zed'.", reply);
    }

    [Fact]
    public async Task Prox_SelfUnknown_WhenCallerNotInSnapshot()
    {
        var query = QueryReturning(new TeamInfoSnapshot(8UL, [Member(8UL, "bob", x: 3f, y: 4f)]));
        var reply = await new ProxCommandHandler(query, Loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("Can't locate you.", reply);
    }

    [Fact]
    public async Task Prox_None_WhenNoMembers()
    {
        var reply = await new ProxCommandHandler(QueryReturning(new TeamInfoSnapshot(0UL, [])), Loc)
            .ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No team members.", reply);
    }
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamIntelHandlersTests"`
Expected: build FAIL — `ProxCommandHandler` does not exist.

- [ ] **Step 3: Create `ProxCommandHandler`**

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!prox [name] — distance from the caller to each other teammate (optionally one).</summary>
/// <param name="query">The live server query.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class ProxCommandHandler(IRustServerQuery query, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "prox";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var team = await query.GetTeamInfoAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (team is null)
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        if (team.Members.Count == 0)
        {
            return localizer.Get("command.team.none", context.Culture);
        }

        var self = team.Members.FirstOrDefault(m => m.SteamId == context.SenderSteamId);
        if (self is null)
        {
            return localizer.Get("command.prox.selfunknown", context.Culture);
        }

        var others = team.Members.Where(m => m.SteamId != context.SenderSteamId).ToList();
        var nameArg = context.Args.Count > 0 ? context.Args[0] : null;
        var matches = TeamMemberFilter.ByName(others, nameArg);
        if (matches.Count == 0)
        {
            return localizer.Get("command.team.nomatch", context.Culture, nameArg ?? string.Empty);
        }

        var parts = matches.Select(m => localizer.Get(
            "command.prox.member", context.Culture, m.Name, Distance.Between(self.X, self.Y, m.X, m.Y)));
        return localizer.Get("command.prox.ok", context.Culture, string.Join(", ", parts));
    }
}
```

- [ ] **Step 4: Run to verify the tests pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~TeamIntelHandlersTests"`
Expected: PASS (19 tests so far).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers/ProxCommandHandler.cs tests/RustPlusBot.Features.Commands.Tests/Handlers/TeamIntelHandlersTests.cs
git commit -m "feat(commands): add !prox handler with caller-relative distances"
```

---

## Task 12: Register the six handlers

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs` (verify, may already assert resolvability)

- [ ] **Step 1: Register the handlers**

In `AddCommands`, after the existing `services.AddScoped<ICommandHandler, TimeCommandHandler>();` line, add:

```csharp
        services.AddScoped<ICommandHandler, OnlineCommandHandler>();
        services.AddScoped<ICommandHandler, OfflineCommandHandler>();
        services.AddScoped<ICommandHandler, TeamCommandHandler>();
        services.AddScoped<ICommandHandler, SteamIdCommandHandler>();
        services.AddScoped<ICommandHandler, AliveCommandHandler>();
        services.AddScoped<ICommandHandler, ProxCommandHandler>();
```

- [ ] **Step 2: Update the registration test count**

`CommandRegistrationTests.Dispatcher_and_handlers_resolve` asserts the exact handler count, which WILL break when six handlers are added. In `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`, change:

```csharp
        Assert.Equal(6, handlers.Count);
```

to:

```csharp
        Assert.Equal(12, handlers.Count);
```

and add assertions for the new names after the existing `Assert.Contains(handlers, h => h.Name == "time");` line:

```csharp
        Assert.Contains(handlers, h => h.Name == "online");
        Assert.Contains(handlers, h => h.Name == "offline");
        Assert.Contains(handlers, h => h.Name == "team");
        Assert.Contains(handlers, h => h.Name == "steamid");
        Assert.Contains(handlers, h => h.Name == "alive");
        Assert.Contains(handlers, h => h.Name == "prox");
```

Note: this test already registers a substitute `IRustServerQuery` (line 25), so the new handlers' constructor dependency resolves without further wiring.

- [ ] **Step 3: Run the Commands test suite**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`
Expected: PASS — all Commands tests (prior 43 + the new ~26 = ~69; read the count).

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs
git commit -m "feat(commands): register team-intel handlers in DI"
```

---

## Task 13: Full-suite verification + format gate

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution strict**

Run: `dotnet build RustPlusBot.slnx`
Expected: 0 warnings / 0 errors.

- [ ] **Step 2: Run the FULL test suite and read per-assembly counts**

Run: `dotnet test RustPlusBot.slnx`
Expected: all assemblies green. **Read each assembly's count** — a sudden drop means a fake/interface broke that assembly's build (the 3a/3b lesson). Confirm Connections gained 2 and Commands gained the team-intel tests; no other assembly's count fell.

- [ ] **Step 3: Run the format gate**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Expected: completes. If it reorders/reformats files, review the diff (it commonly reorders members and wraps collection initializers that Roslynator does not flag).

- [ ] **Step 4: Re-build and re-test after cleanup**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: still 0/0 and all green.

- [ ] **Step 5: Confirm no EF model drift**

Run: `dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence/RustPlusBot.Persistence.csproj --startup-project src/RustPlusBot/RustPlusBot.csproj`
Expected: "No changes have been made..." (no migration needed — team info is read-only live data). If the project uses a different EF invocation, fall back to confirming no new `Migrations/` files and that the build's model-drift check (if any) passes.

- [ ] **Step 6: Commit any jb-cleanup changes**

```bash
git add -A
git commit -m "style: apply jb ReformatAndReorder for team-intel changes"
```

(Skip this commit if Step 3 produced no changes.)

---

## Task 14: Update the feature catalog

**Files:**

- Modify: `docs/product/feature-catalog.md` (TRACKED — normal `git add`, no `-f`).

- [ ] **Step 1: Flip the shipped rows to Done**

In `docs/product/feature-catalog.md`, change the decision marker on the `!online`/`!offline`/`!team`/`!alive`/`!prox`/`!steamid` rows from `✅ Adopt` to `✔️ Done`. Update the `3b-ii` cluster row's status to reflect it is shipped (e.g. `⏸ Planned` → `✔️ Done`). For `!afk`, note it is deferred pending a position-tracking poller.

- [ ] **Step 2: Commit**

```bash
git add docs/product/feature-catalog.md
git commit -m "docs: mark 3b-ii team-intel commands done in feature catalog"
```

---

## Self-Review Notes

- **Spec coverage:** all six commands (Tasks 8–11), the seam extension + DTO (Tasks 1–4), the untested shim mapping (Task 3), both helpers (Tasks 5–6), EN/FR localization with every edge-case key (Task 7), registration (Task 12), the full verification gate incl. per-assembly counts + jb + no-drift (Task 13), and catalog update (Task 14). `!afk` is explicitly deferred (mentioned in Task 14). No new entities/migrations/events/options — consistent with the spec's non-goals.
- **Type consistency:** `TeamInfoSnapshot(ulong LeaderSteamId, IReadOnlyList<TeamMemberSnapshot> Members)` and `TeamMemberSnapshot(SteamId, Name, X, Y, IsOnline, IsAlive, LastSpawnTimeUtc, LastDeathTimeUtc)` are used identically across the shim, fake, supervisor test, and every handler/test. `IRustServerQuery.GetTeamInfoAsync(ulong, Guid, CancellationToken)` and `IRustServerConnection.GetTeamInfoAsync(TimeSpan, CancellationToken)` match their consumers. Localization keys defined in Task 7 are exactly the keys read in Tasks 8–11.
- **Ordering note:** `!alive` sorts by `now - LastSpawnTimeUtc` descending; the test fixes the clock so durations are deterministic and `DurationFormat.Compact` renders `"3h 0m"`/`"1h 0m"` (matches the existing `Uptime`/`Wipe` format).
- **Distance rounding:** `Distance.Between` rounds away-from-zero; the test's `141` (from 141.42) and `5` (3-4-5) confirm the contract the handlers depend on.
