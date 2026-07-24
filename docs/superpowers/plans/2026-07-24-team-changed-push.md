# Push-driven team state via `team_changed` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move team-state detection (connect/disconnect/death/respawn/AFK) from the 2–5 s poll inside `PollMarkersAsync` onto RustPlusApi beta.6's `OnTeamChanged` push event, keeping a low-frequency team poll as an AFK safety tick. Marker/rig polling is unchanged.

**Architecture:** RustPlusApi beta.6 now dispatches the `team_changed` broadcast as an `OnTeamChanged` event whose `TeamInfo` payload is the same model the polled `GetTeamInfoAsync` already maps. `RustPlusSocketSource` surfaces it as a new `IRustServerConnection.TeamChanged` event. `ConnectionSupervisor` runs `TeamStateTracker.Diff` on each pushed snapshot (instant transitions) and also on a slow `PollTeamAsync` loop (guarantees `Diff` runs so "BecameAfk" can't stall during broadcast silence). The team block is removed from the fast marker poll.

**Tech Stack:** C# / .NET 10, xUnit, NSubstitute, RustPlusApi 2.0.0-beta.6. Build/test via `dtk` (DotnetTokenKiller).

## Global Constraints

- RustPlusApi and RustPlusApi.Fcm are pinned to `2.0.0-beta.6` in `Directory.Packages.props` (central package management — versions live there, `PackageReference` entries carry no `Version`).
- `TeamStateTracker` logic MUST NOT change — it is already lock-guarded and its AFK model is correct; this work only changes *how often* and *from where* `Diff` is called.
- Fire-and-forget publish handlers use `_shutdown.Token`, guard on `_disposed`, and swallow all exceptions into a `LoggerMessage` (mirror `PublishClanStateAsync`, `ConnectionSupervisor.cs:1189`).
- Broad `catch (Exception)` blocks in background loops keep the existing `#pragma warning disable CA1031` + justifying comment convention.
- Build: `dtk build`. Test one project with a filter: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "<expr>"`.

---

### Task 1: Upgrade RustPlusApi to beta.6

**Files:**
- Modify: `Directory.Packages.props:18-19`

**Interfaces:**
- Produces: the `RustPlus.OnTeamChanged` event (`EventHandler<RustPlusApi.Data.Events.TeamChangedEventArg>`) and the `RustPlusApi.Data.Events.TeamChangedEventArg { ulong PlayerId; RustPlusApi.Data.TeamInfo TeamInfo }` record, available to later tasks.

- [ ] **Step 1: Bump both package versions**

In `Directory.Packages.props`, change the two lines:

```xml
    <PackageVersion Include="RustPlusApi" Version="2.0.0-beta.6" />
    <PackageVersion Include="RustPlusApi.Fcm" Version="2.0.0-beta.6" />
```

- [ ] **Step 2: Restore + build the whole solution to confirm beta.6 is source-compatible**

Run: `dtk build`
Expected: build succeeds. beta.6 keeps the same public surface for the methods the bot already uses (`GetMapMarkersAsync`, `GetTeamInfoAsync`, `ProcessRequestAsync`, `ParseNotification`); it only *adds* `OnTeamChanged`. If any pre-existing call site fails to compile, fix it minimally in this task before proceeding.

- [ ] **Step 3: Run the full connections test project to confirm no runtime regression from the bump**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj`
Expected: PASS (same green set as before the bump).

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props
git commit -m "build(deps): bump RustPlusApi to 2.0.0-beta.6 (team_changed dispatch)"
```

---

### Task 2: Extract `TeamInfoMapping.ToSnapshot` and unit-test it

Extract the inline `TeamInfo → TeamInfoSnapshot` mapping (currently in `RustPlusServerConnection.GetTeamInfoAsync`, `RustPlusSocketSource.cs:339-353`) into a reusable, directly-testable helper so both the poll and the new push handler share one mapping.

**Files:**
- Create: `src/RustPlusBot.Features.Connections/Listening/TeamInfoMapping.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs:339-353`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj`
- Create: `tests/RustPlusBot.Features.Connections.Tests/TeamInfoMappingTests.cs`

**Interfaces:**
- Produces: `internal static class TeamInfoMapping` with `public static TeamInfoSnapshot ToSnapshot(RustPlusApi.Data.TeamInfo teamInfo)`.
- Consumes: `RustPlusApi.Data.TeamInfo { ulong LeaderSteamId; IEnumerable<RustPlusApi.Data.MemberInfo>? Members; RustPlusApi.Data.Notes.DeathNote? DeathNote }`; `RustPlusApi.Data.MemberInfo { ulong SteamId; string? Name; float X; float Y; bool IsOnline; bool IsAlive; DateTime LastSpawnTime; DateTime LastDeathTime }`; `RustPlusApi.Data.Notes.DeathNote { float X; float Y }`.

- [ ] **Step 1: Give the test project access to `RustPlusApi.Data` types**

In `tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj`, add inside the first `<ItemGroup>` (the `PackageReference` group):

```xml
    <PackageReference Include="RustPlusApi" />
```

- [ ] **Step 2: Write the failing test**

Create `tests/RustPlusBot.Features.Connections.Tests/TeamInfoMappingTests.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class TeamInfoMappingTests
{
    [Fact]
    public void ToSnapshot_MapsMembersLeaderAndDeathNote()
    {
        var teamInfo = new RustPlusApi.Data.TeamInfo
        {
            LeaderSteamId = 100UL,
            DeathNote = new RustPlusApi.Data.Notes.DeathNote { X = 12f, Y = 34f },
            Members =
            [
                new RustPlusApi.Data.MemberInfo
                {
                    SteamId = 100UL,
                    Name = "Alice",
                    X = 1f,
                    Y = 2f,
                    IsOnline = true,
                    IsAlive = false,
                    LastSpawnTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
                    LastDeathTime = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
                },
            ],
        };

        var snapshot = TeamInfoMapping.ToSnapshot(teamInfo);

        Assert.Equal(100UL, snapshot.LeaderSteamId);
        Assert.Equal((12f, 34f), snapshot.DeathNote);
        var m = Assert.Single(snapshot.Members);
        Assert.Equal(100UL, m.SteamId);
        Assert.Equal("Alice", m.Name);
        Assert.True(m.IsOnline);
        Assert.False(m.IsAlive);
        // Unspecified game timestamps are read as UTC.
        Assert.Equal(DateTimeKind.Utc, m.LastDeathTimeUtc.UtcDateTime.Kind);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), m.LastDeathTimeUtc);
    }

    [Fact]
    public void ToSnapshot_NullMembersAndNoDeathNote_YieldEmptyMembersAndNullNote()
    {
        var teamInfo = new RustPlusApi.Data.TeamInfo { LeaderSteamId = 7UL, Members = null, DeathNote = null };

        var snapshot = TeamInfoMapping.ToSnapshot(teamInfo);

        Assert.Equal(7UL, snapshot.LeaderSteamId);
        Assert.Empty(snapshot.Members);
        Assert.Null(snapshot.DeathNote);
    }
}
```

- [ ] **Step 3: Run the test to verify it fails**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~TeamInfoMappingTests"`
Expected: FAIL to compile — `TeamInfoMapping` does not exist.

- [ ] **Step 4: Create the mapping helper**

Create `src/RustPlusBot.Features.Connections/Listening/TeamInfoMapping.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Maps a RustPlusApi <see cref="RustPlusApi.Data.TeamInfo"/> to the bot's decoupled
/// <see cref="TeamInfoSnapshot"/>. Shared by the polled read and the pushed <c>team_changed</c> event so
/// the two paths can never drift.</summary>
internal static class TeamInfoMapping
{
    /// <summary>Projects a RustPlusApi team-info model onto a <see cref="TeamInfoSnapshot"/>.</summary>
    /// <param name="teamInfo">The RustPlusApi team info (from a poll response or a team_changed broadcast).</param>
    /// <returns>The decoupled snapshot the tracker and events consume.</returns>
    public static TeamInfoSnapshot ToSnapshot(RustPlusApi.Data.TeamInfo teamInfo)
    {
        // CONFIRMED (2.0.0-beta.6): MemberInfo exposes SteamId/Name?/X/Y/IsOnline/IsAlive plus
        // LastSpawnTime/LastDeathTime as Unspecified-kind DateTimes that are actually UTC.
        var members = (teamInfo.Members ?? [])
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
        var deathNote = teamInfo.DeathNote is { } dn
            ? ((float X, float Y)?)(dn.X, dn.Y)
            : null;
        return new TeamInfoSnapshot(teamInfo.LeaderSteamId, members, deathNote);
    }
}
```

- [ ] **Step 5: Point `GetTeamInfoAsync` at the shared helper**

In `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`, replace the mapping block (lines 339-353) — everything from `var members = (response.Data.Members ?? [])` through `return new TeamInfoSnapshot(...)` — with:

```csharp
                return TeamInfoMapping.ToSnapshot(response.Data);
```

Leave the `if (!response.IsSuccess || response.Data is null) { return null; }` guard above it untouched.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~TeamInfoMappingTests"`
Expected: PASS (both tests).

- [ ] **Step 7: Run the full connections project to confirm the refactor broke nothing**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Listening/TeamInfoMapping.cs \
        src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs \
        tests/RustPlusBot.Features.Connections.Tests/TeamInfoMappingTests.cs \
        tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj
git commit -m "refactor(connections): extract shared TeamInfoMapping.ToSnapshot"
```

---

### Task 3: Add the `TeamChanged` event to the connection abstraction and wire it up

**Files:**
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs:172` (end of interface)
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` (ctor ~172, `DisposeAsync` ~768, event declaration, new handler)
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`

**Interfaces:**
- Produces: `event EventHandler<TeamInfoSnapshot>? IRustServerConnection.TeamChanged`; on the fake, `FakeConnection.RaiseTeamChanged(TeamInfoSnapshot snapshot)` and `int FakeConnection.TeamInfoCallCount`.
- Consumes: `TeamInfoMapping.ToSnapshot` (Task 2); `RustPlusApi.Data.Events.TeamChangedEventArg` (Task 1).

- [ ] **Step 1: Declare the event on the interface**

In `IRustServerConnection.cs`, add after the `StorageMonitorTriggered` event (line 171), before the closing brace:

```csharp

    /// <summary>
    /// Raised when the server pushes a <c>team_changed</c> broadcast (member join/leave, online/offline,
    /// death/respawn, movement, leader change). Carries the full team snapshot — the same shape a
    /// <see cref="GetTeamInfoAsync"/> poll returns.
    /// </summary>
    event EventHandler<TeamInfoSnapshot>? TeamChanged;
```

- [ ] **Step 2: Declare + raise the event in the real connection**

In `RustPlusSocketSource.cs`, inside the `RustPlusServerConnection` class, add the event declaration next to the other public events (near the `SmartDeviceTriggered`/`StorageMonitorTriggered` declarations — search for `public event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered;` and add below it):

```csharp
        public event EventHandler<TeamInfoSnapshot>? TeamChanged;
```

Add the private handler next to `OnClanChanged` (near line 834):

```csharp
        private void OnTeamChanged(object? sender, RustPlusApi.Data.Events.TeamChangedEventArg e) =>
            TeamChanged?.Invoke(this, TeamInfoMapping.ToSnapshot(e.TeamInfo));
```

Subscribe in the constructor event block (after `_rustPlus.OnClanChanged += OnClanChanged;`, line 172):

```csharp
            _rustPlus.OnTeamChanged += OnTeamChanged;
```

Unsubscribe in `DisposeAsync` (after `_rustPlus.OnClanChanged -= OnClanChanged;`, line 768):

```csharp
            _rustPlus.OnTeamChanged -= OnTeamChanged;
```

- [ ] **Step 3: Write the failing fake-connection test**

Add to `tests/RustPlusBot.Features.Connections.Tests/RustPlusSocketSourceTests.cs` (new `[Fact]` inside the existing class):

```csharp
    [Fact]
    public void FakeConnection_RaiseTeamChanged_InvokesSubscribers()
    {
        var source = new Fakes.FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        var connection = (Fakes.FakeRustSocketSource.FakeConnection)source.Create("127.0.0.1", 28015, 1UL, "1");

        TeamInfoSnapshot? received = null;
        connection.TeamChanged += (_, s) => received = s;
        var snapshot = new TeamInfoSnapshot(5UL, []);
        connection.RaiseTeamChanged(snapshot);

        Assert.Same(snapshot, received);
    }
```

Add `using RustPlusBot.Abstractions.Connections;` to the file's usings if not present.

- [ ] **Step 4: Run it to verify it fails**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~FakeConnection_RaiseTeamChanged"`
Expected: FAIL to compile — `FakeConnection` has no `TeamChanged` / `RaiseTeamChanged`.

- [ ] **Step 5: Extend the fake connection**

In `tests/.../Fakes/FakeRustSocketSource.cs`, inside `FakeConnection`:

Add the event next to the other event declarations (after `StorageMonitorTriggered`, line 296):

```csharp

        /// <summary>Raised by <see cref="RaiseTeamChanged"/> to simulate a pushed team_changed broadcast.</summary>
        public event EventHandler<TeamInfoSnapshot>? TeamChanged;
```

Add a call counter property next to `TeamResult` (line 208):

```csharp

        /// <summary>Number of times <see cref="GetTeamInfoAsync"/> has been called (to assert team info is
        /// no longer polled on the fast marker cadence).</summary>
        public int TeamInfoCallCount { get; private set; }
```

Change `GetTeamInfoAsync` (line 310-311) to count calls:

```csharp
        public Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            TeamInfoCallCount++;
            return Task.FromResult(TeamResult);
        }
```

Add the raise helper next to `RaiseClanChanged` (line 454):

```csharp

        /// <summary>Raises <see cref="TeamChanged"/> to simulate a pushed team_changed broadcast.</summary>
        /// <param name="snapshot">The team snapshot to deliver.</param>
        public void RaiseTeamChanged(TeamInfoSnapshot snapshot) => TeamChanged?.Invoke(this, snapshot);
```

Finally, add a pre-`Create` setup hook on the OUTER `FakeRustSocketSource` (not the connection) so a test can configure a connection's `TeamResult` before the supervisor's poll loop starts — this eliminates the race between the test assigning `TeamResult` and the immediate priming poll. Add the property next to `LastConnection` (line 44):

```csharp
    /// <summary>Optional hook invoked on each new <see cref="FakeConnection"/> at creation time, before it is
    /// returned and the poll loops start. Lets a test stage <see cref="FakeConnection.TeamResult"/> (or null)
    /// without racing the immediate priming team poll.</summary>
    internal Action<FakeConnection>? LastConnectionSetup { get; set; }
```

and invoke it inside `Create`, immediately before `LastConnection = connection;` (line 101):

```csharp
        LastConnectionSetup?.Invoke(connection);
```

- [ ] **Step 6: Run it to verify it passes**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~FakeConnection_RaiseTeamChanged"`
Expected: PASS.

- [ ] **Step 7: Build the solution (the new interface member must be satisfied everywhere)**

Run: `dtk build`
Expected: build succeeds — `RejectedConnection` and any other `IRustServerConnection` implementor compiles. If `RejectedConnection` (`RustPlusSocketSource.cs:35`) needs the member, add `public event EventHandler<TeamInfoSnapshot>? TeamChanged;` to it (auto-implemented, never raised).

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs \
        src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs \
        tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs \
        tests/RustPlusBot.Features.Connections.Tests/RustPlusSocketSourceTests.cs
git commit -m "feat(connections): surface team_changed as IRustServerConnection.TeamChanged"
```

---

### Task 4: Add the `TeamPollInterval` option

**Files:**
- Modify: `src/RustPlusBot.Features.Connections/ConnectionOptions.cs:43` (after `AfkEpsilon`)
- Modify: `tests/RustPlusBot.Features.Connections.Tests/ConnectionOptionsTests.cs`

**Interfaces:**
- Produces: `ConnectionOptions.TeamPollInterval` (`TimeSpan`, default 30 s).

- [ ] **Step 1: Write the failing default-value test**

In `ConnectionOptionsTests.cs`, add an assertion (place it beside the existing marker-interval assertions around line 12-13):

```csharp
        Assert.Equal(TimeSpan.FromSeconds(30), o.TeamPollInterval);
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~ConnectionOptionsTests"`
Expected: FAIL to compile — `TeamPollInterval` does not exist.

- [ ] **Step 3: Add the option**

In `ConnectionOptions.cs`, add after the `AfkEpsilon` property (line 43):

```csharp

    /// <summary>
    /// How often to poll team info as an AFK safety tick and self-heal, now that live team changes arrive
    /// via the pushed <c>team_changed</c> event. Presence/death events are instant; this only bounds how long
    /// a still player in a broadcast-silent team can take to be flagged AFK (worst case AfkThreshold + this).
    /// Default 30s.
    /// </summary>
    public TimeSpan TeamPollInterval { get; set; } = TimeSpan.FromSeconds(30);
```

- [ ] **Step 4: Run it to verify it passes**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~ConnectionOptionsTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Connections/ConnectionOptions.cs \
        tests/RustPlusBot.Features.Connections.Tests/ConnectionOptionsTests.cs
git commit -m "feat(connections): add TeamPollInterval (AFK safety-tick cadence)"
```

---

### Task 5: Drive team state from the push event + slow tick; remove it from the marker poll

This is the core wiring. `ConnectionSupervisor.cs` gets: a `DimensionsHolder`, a shared `PublishTeamStateAsync` helper, an `OnTeamChanged` subscription, a new `PollTeamAsync` loop, and the removal of the team block from `PollMarkersAsync`.

**Files:**
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (RunAsync ~608-660; `PollMarkersAsync` 725-786; add helpers + logger messages)
- Modify: `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs` (harness option + new tests + a `WaitUntilAsync` helper)

**Interfaces:**
- Consumes: `IRustServerConnection.TeamChanged` (Task 3); `ConnectionOptions.TeamPollInterval` (Task 4); `TeamStateTracker.Diff` (unchanged); `FakeConnection.RaiseTeamChanged` / `TeamInfoCallCount` / `TeamResult` (Task 3); the harness `CreateHarness`, `SeedAsync`, `WaitForStateAsync` (`ConnectionSupervisorTests.cs`).
- Produces: no new public API; behaviour change only.

- [ ] **Step 1: Add an optional `teamPollInterval` to the test harness**

In `ConnectionSupervisorTests.cs`, change the harness signature (line 24) and its options block. Replace:

```csharp
    private static Harness CreateHarness(FakeRustSocketSource source)
    {
```

with:

```csharp
    private static Harness CreateHarness(FakeRustSocketSource source, TimeSpan? teamPollInterval = null)
    {
```

and in the `Options.Create(new ConnectionOptions { ... })` block (lines 61-71) add one line after `LivenessPollInterval = TimeSpan.FromMilliseconds(20),`:

```csharp
            TeamPollInterval = teamPollInterval ?? TimeSpan.FromMilliseconds(20),
```

- [ ] **Step 2: Write the failing push-event test**

Add to `ConnectionSupervisorTests.cs`. It connects, raises a `TeamChanged` prime snapshot, then a second snapshot where a member has gone offline, and asserts a `Disconnect` transition is published. (First `Diff` primes silently; the second yields the transition.)

```csharp
    [Fact]
    public async Task TeamChanged_push_publishes_player_state_transition()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        // Make the slow poll inert: TeamResult = null means Diff(null) is a no-op, so the tracker's baseline
        // is driven ONLY by the pushed snapshots below — no race between the priming poll and the pushes.
        source.LastConnectionSetup = c => c.TeamResult = null;
        // Large team-poll interval too, belt-and-suspenders.
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromSeconds(30));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<PlayerStateChangedEvent>();
        var stream = h.Bus.SubscribeAsync<PlayerStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in stream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        var conn = source.LastConnection!;

        var online = new TeamMemberSnapshot(
            100UL, "Alice", 1f, 1f, IsOnline: true, IsAlive: true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        var offline = online with { IsOnline = false };

        conn.RaiseTeamChanged(new TeamInfoSnapshot(100UL, [online]));   // prime (silent)
        conn.RaiseTeamChanged(new TeamInfoSnapshot(100UL, [offline]));  // -> Disconnect

        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);

        Assert.True(captured.TryDequeue(out var evt));
        var transition = Assert.Single(evt!.Transitions);
        Assert.Equal(PlayerTransitionKind.Disconnect, transition.Kind);
        Assert.Equal(100UL, transition.SteamId);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try { await subTask; } catch (OperationCanceledException) { /* expected */ }
    }
```

If the file has no `WaitUntilAsync`, add this private static helper to the class:

```csharp
    private static async Task WaitUntilAsync(Func<bool> predicate, CancellationToken ct)
    {
        while (!predicate())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(15, ct);
        }
    }
```

Ensure these usings are present at the top of the file: `using RustPlusBot.Abstractions.Events;` (for `PlayerStateChangedEvent`, `PlayerTransitionKind`).

- [ ] **Step 3: Run it to verify it fails**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~TeamChanged_push_publishes"`
Expected: FAIL — no `TeamChanged` subscription exists yet, so nothing is published and `WaitUntilAsync` runs to the 30 s timeout (`OperationCanceledException`).

- [ ] **Step 4: Add the `DimensionsHolder` type**

In `ConnectionSupervisor.cs`, add near the `LiveSocket` record (line 1579):

```csharp
    /// <summary>Mutable, thread-visible holder for the per-connected-window map dimensions. The marker poll
    /// resolves these once off the critical path; the team push handler and team poll read them (possibly
    /// null before resolution — PlayerStateChangedEvent tolerates a null and renders without a grid ref).</summary>
    private sealed class DimensionsHolder
    {
        private volatile MapDimensions? _value;

        public MapDimensions? Value
        {
            get => _value;
            set => _value = value;
        }
    }
```

- [ ] **Step 5: Add the shared publish helper**

In `ConnectionSupervisor.cs`, add next to `PublishClanStateAsync` (after line ~1210). This is the single Diff-and-publish path used by both the push handler and the slow poll:

```csharp
    private async Task PublishTeamStateAsync(
        (ulong Guild, Guid Server) key,
        TeamStateTracker tracker,
        DimensionsHolder dims,
        TeamInfoSnapshot? snapshot)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var transitions = tracker.Diff(snapshot, clock.UtcNow, _options.AfkThreshold, _options.AfkEpsilon);
            if (transitions.Count == 0)
            {
                return;
            }

            var evt = new PlayerStateChangedEvent(key.Guild, key.Server, dims.Value, transitions);
            // Supervisor-wide shutdown token, not a per-connection ct: a pushed team change should publish
            // regardless of one connection's reconnect cycle (mirrors the chat/clan handlers).
            await eventBus.PublishAsync(evt, _shutdown.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a publish failure must not crash the socket callback or poll.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPublishTeamStateFailed(logger, ex, key.Server);
        }
    }
```

- [ ] **Step 6: Add the two `LoggerMessage` declarations**

In `ConnectionSupervisor.cs`, next to the existing `LogMarkerPollFailed` declaration (search for `LogMarkerPollFailed`), add:

```csharp
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team poll for server {ServerId} failed.")]
    private static partial void LogTeamPollFailed(ILogger logger, Exception ex, Guid serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing team state for server {ServerId} failed.")]
    private static partial void LogPublishTeamStateFailed(ILogger logger, Exception ex, Guid serverId);
```

- [ ] **Step 7: Add the `PollTeamAsync` loop**

In `ConnectionSupervisor.cs`, add after `PollMarkersAsync` (after line 786):

```csharp
    /// <summary>
    /// Low-frequency team poll that runs the same <see cref="TeamStateTracker.Diff"/> as the pushed
    /// team_changed handler. Live changes arrive via the push event; this loop exists solely to (a) prime the
    /// baseline on connect and (b) guarantee <c>Diff</c> runs periodically so a still player in a
    /// broadcast-silent team is still flagged AFK. Its first iteration runs immediately (prime), then it waits
    /// <see cref="ConnectionOptions.TeamPollInterval"/> between iterations. Degrades safely: a failed poll is
    /// logged and skipped, never ending the loop.
    /// </summary>
    private async Task PollTeamAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        TeamStateTracker tracker,
        DimensionsHolder dims,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var team = await connection.GetTeamInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
                await PublishTeamStateAsync(key, tracker, dims, team).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return; // stopping
            }
#pragma warning disable CA1031 // Broad catch: a failed team poll is logged and skipped; the loop survives.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogTeamPollFailed(logger, ex, key.Server);
            }

            await Task.Delay(_options.TeamPollInterval, ct).ConfigureAwait(false);
        }
    }
```

- [ ] **Step 8: Remove the team block from `PollMarkersAsync` and thread the dims holder**

In `ConnectionSupervisor.cs`, change the `PollMarkersAsync` signature (line 725-729). Replace the `TeamStateTracker tracker` parameter with `DimensionsHolder dims`:

```csharp
    private async Task PollMarkersAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        DimensionsHolder dims,
        CancellationToken ct)
    {
```

Immediately after `var dims = await connection.GetMapDimensionsAsync(...)` becomes a name clash — the parameter is now named `dims`. Rename the local dimension variable. Replace line 736:

```csharp
        var localDims = await connection.GetMapDimensionsAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
        dims.Value = localDims;
```

Then update the two uses of the old `dims` local inside `PollMarkersAsync` — in the `PublishMarkerDeltaAsync(key, dims, ...)` call (line 755) and the `DetectRigActivationsAsync(key, current, rigs, dims, ...)` call (line 759) — to pass `localDims`:

```csharp
                    await PublishMarkerDeltaAsync(key, localDims, previous, current, ct).ConfigureAwait(false);
```

```csharp
                await DetectRigActivationsAsync(key, current, rigs, localDims, rigsInRadius, ct).ConfigureAwait(false);
```

Delete the team block (lines 761-768) entirely — the `var team = ...` through the `PlayerStateChangedEvent` publish:

```csharp
                var team = await connection.GetTeamInfoAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
                var transitions = tracker.Diff(team, clock.UtcNow, _options.AfkThreshold, _options.AfkEpsilon);
                if (transitions.Count > 0)
                {
                    await eventBus.PublishAsync(
                            new PlayerStateChangedEvent(key.Guild, key.Server, dims, transitions), ct)
                        .ConfigureAwait(false);
                }
```

(The `catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }` and the marker `catch` below it stay.)

- [ ] **Step 9: Wire the holder, the push handler, and the team poll into `RunAsync`**

In `ConnectionSupervisor.cs` `RunAsync`, after `var tracker = new TeamStateTracker();` (line 608), add:

```csharp
        var dims = new DimensionsHolder();

        void OnTeamChanged(object? sender, TeamInfoSnapshot snapshot)
        {
            // Fire-and-forget: PublishTeamStateAsync catches everything internally.
            _ = PublishTeamStateAsync(key, tracker, dims, snapshot);
        }
```

Subscribe alongside the other handlers (after `connection.ClanChanged += OnClanChanged;`, line 613):

```csharp
        connection.TeamChanged += OnTeamChanged;
```

Change the marker-poll launch (line 624) to pass `dims` instead of `tracker`:

```csharp
        var markerPoll = Task.Run(() => PollMarkersAsync(key, connection, dims, pollCts.Token),
            CancellationToken.None);
```

Add the team-poll launch right after the reachability-poll launch (after line 627):

```csharp
        var teamPoll = Task.Run(() => PollTeamAsync(key, connection, tracker, dims, pollCts.Token),
            CancellationToken.None);
```

Add `teamPoll` to the `Task.WhenAll` join (line 646):

```csharp
                await Task.WhenAll(markerPoll, reachabilityPoll, teamPoll, heartbeat, liveness).ConfigureAwait(false);
```

Unsubscribe in the `finally` alongside the others (after `connection.ClanChanged -= OnClanChanged;`, line 659):

```csharp
            connection.TeamChanged -= OnTeamChanged;
```

- [ ] **Step 10: Run the push test — it should now pass**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~TeamChanged_push_publishes"`
Expected: PASS.

- [ ] **Step 11: Add + run the slow-poll-tick test**

Add to `ConnectionSupervisorTests.cs`. No `TeamChanged` event is raised; the slow poll must drive the transition purely from `TeamResult` changing between polls.

```csharp
    [Fact]
    public async Task TeamPoll_tick_publishes_transition_without_any_push()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        // Fast team poll so the tick drives the transition quickly.
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromMilliseconds(20));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        var online = new TeamMemberSnapshot(
            100UL, "Alice", 1f, 1f, IsOnline: true, IsAlive: true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);
        source.LastConnectionSetup = c => c.TeamResult = new TeamInfoSnapshot(100UL, [online]);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var captured = new System.Collections.Concurrent.ConcurrentQueue<PlayerStateChangedEvent>();
        var stream = h.Bus.SubscribeAsync<PlayerStateChangedEvent>(cts.Token);
        var subTask = Task.Run(async () =>
        {
            await foreach (var e in stream)
            {
                captured.Enqueue(e);
            }
        }, CancellationToken.None);

        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        var conn = source.LastConnection!;

        // Wait for at least one poll to prime the baseline with the online snapshot, then flip to offline.
        await WaitUntilAsync(() => conn.TeamInfoCallCount >= 1, cts.Token);
        conn.TeamResult = new TeamInfoSnapshot(100UL, [online with { IsOnline = false }]);

        await WaitUntilAsync(() => !captured.IsEmpty, cts.Token);
        Assert.True(captured.TryDequeue(out var evt));
        Assert.Contains(evt!.Transitions, t => t.Kind == PlayerTransitionKind.Disconnect && t.SteamId == 100UL);

        await h.Supervisor.StopAllAsync();
        await cts.CancelAsync();
        try { await subTask; } catch (OperationCanceledException) { /* expected */ }
    }
```

This test uses the `LastConnectionSetup` hook (added to `FakeRustSocketSource` in Task 3, Step 5) so the initial online `TeamResult` is staged before the poll starts, then flips it to offline after the baseline is primed.

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~TeamPoll_tick_publishes"`
Expected: PASS.

- [ ] **Step 12: Add + run the efficiency test (team info is not on the marker cadence)**

```csharp
    [Fact]
    public async Task TeamInfo_is_not_polled_on_the_fast_marker_cadence()
    {
        var source = new FakeRustSocketSource();
        source.EnqueueConnect(SocketConnectOutcome.Connected);
        source.EnqueueHeartbeat(HeartbeatResult.Ok(1));
        // Marker cadence stays fast (20ms from the harness); team poll is slow.
        await using var h = CreateHarness(source, teamPollInterval: TimeSpan.FromSeconds(10));
        var (serverId, _, _) = await SeedAsync(h.Provider);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await h.Supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
        var state = await WaitForStateAsync(h.Provider, serverId, s => s.Status == ConnectionStatus.Connected);
        Assert.NotNull(state);
        var conn = source.LastConnection!;

        // Wait for the team poll's immediate first (priming) call, then let ~10 marker intervals elapse.
        await WaitUntilAsync(() => conn.TeamInfoCallCount >= 1, cts.Token);
        await Task.Delay(200, cts.Token);

        // Only the single priming poll ran; the 10s team interval hasn't elapsed, so the fast marker
        // cadence did NOT trigger any further team-info reads.
        Assert.Equal(1, conn.TeamInfoCallCount);

        await h.Supervisor.StopAllAsync();
    }
```

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj --filter "FullyQualifiedName~TeamInfo_is_not_polled"`
Expected: PASS.

- [ ] **Step 13: Run the whole connections project (nothing else regressed)**

Run: `dtk test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj`
Expected: PASS (all existing marker/rig/AFK/status-loop tests plus the four new ones).

- [ ] **Step 14: Run the full solution test suite**

Run: `dtk test`
Expected: PASS across all projects (the downstream Players tests that consume `PlayerStateChangedEvent` are unaffected — the event contract is unchanged).

- [ ] **Step 15: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs \
        tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs \
        tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs
git commit -m "feat(connections): drive team state from team_changed push + slow AFK tick"
```

---

## Notes for the implementer

- **Concurrency:** `PublishTeamStateAsync` is now called from two threads — the RustPlusApi dispatch thread (push) and the `PollTeamAsync` thread. `TeamStateTracker.Diff` is lock-guarded and swaps its baseline atomically, so this is safe and cannot double-publish a transition (the second caller diffs against the first's committed baseline).
- **Do not** reintroduce a `GetTeamInfoAsync` call inside `PollMarkersAsync`; team state is now exclusively push + `PollTeamAsync`.
- **Line numbers** in this plan are from the pre-change tree; after early tasks they will drift. Anchor edits on the quoted code, not the numbers.
- **Out of scope:** the `GetMapMarkers returned no data` diagnostic-message gap noted in the design's §7 is a separate change — do not bundle it here.
