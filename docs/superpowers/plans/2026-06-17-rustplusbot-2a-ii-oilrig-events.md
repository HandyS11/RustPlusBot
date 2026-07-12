# Oil Rig Activation Detection (2a-ii) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect small/large oil-rig activation from CH47 (Chinook) movement, run a timed three-state machine per rig, fire `#events` + in-game alerts at all three rig boundaries, generalize the in-game broadcast to ALL live events, and expose `!small`/`!large` + `/small`/`/large`.

**Architecture:** Extends the merged 2a live-event pipeline. The Connections marker poll (now 5s base / ~2s adaptive while a CH47 is live) gains rig-radius detection that publishes a `RigStateChangedEvent(Kind=Activated)`. A new `RigStateStore` + a background tick in `EventsHostedService` drive the two timed boundaries (`CrateLootable`, `Respawned`). `EventRelay` posts every event to `#events` AND in-game team chat. `!small`/`!large` are in-game `ICommandHandler`s reading `IRigState`; `/small`/`/large` reuse them by name via the existing `ServerQueryService` (3c-ii pattern). All state is in-memory; no entities/migrations/persistence/new project.

**Tech Stack:** .NET 10, C#, Discord.Net 3.20.1, RustPlusApi 2.0.0-beta.1, xUnit + NSubstitute, EF Core (untouched here).

## Global Constraints

- Per-guild isolation: every rig key is `(ulong GuildId, Guid ServerId, RigKind)`.
- In-memory only — NO new entity, migration, persistence, options project, gateway intent, or feature project.
- Strict analyzers (Roslynator + Roslyn): no `// TODO` comments (`S1135` = error); XML `<param>`/`<summary>` docs required on public members (`RCS1141`); named args in declaration order (`RCS1205`); prefer concrete `Dictionary<>` over interface for `CA1859`; no secret in exception messages.
- Format gate is `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (run `dotnet tool restore` first). The pre-push hook enforces it. `dotnet format` disagrees — use `jb`.
- NSubstitute on `internal` interfaces needs `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in the project's csproj (already present in Connections/Workspace/Commands; add to Events only if a new internal interface is substituted there).
- EN + FR localization for every user-visible string; English is the fallback.
- Run the FULL test suite and read per-assembly counts after interface changes — a new interface member silently drops a whole assembly's tests if a fake/double isn't updated (the recurring 3a/3b lesson).
- `docs/superpowers/` specs & plans are LOCAL-ONLY — never `git add` them.
- Verified RustPlusApi facts (2.0.0-beta.1): `GetMapAsync()` → `Task<Response<RustPlusApi.Data.ServerMap>>`; `ServerMap.Monuments` is `List<RustPlusApi.Data.ServerMapMonument>` with `string Name` (= protobuf token, e.g. `oilrig_1`), `Nullable<float> X`, `Nullable<float> Y`. Rig tokens: small = `oilrig_1`, large = `large_oil_rig`.
- Branch: `feat/oilrig-events` off `develop`.

---

## File Structure

**Abstractions** (`src/RustPlusBot.Abstractions/`):

- Create `Connections/MonumentSnapshot.cs` — `record MonumentSnapshot(string Token, float X, float Y)` in ns `RustPlusBot.Features.Connections.Listening`.
- Create `Events/RigKind.cs`, `Events/RigEventKind.cs`, `Events/RigStateChangedEvent.cs` in ns `RustPlusBot.Abstractions.Events` (RigKind/RigEventKind enums kept in the Events ns for cohesion with the event).

**Connections** (`src/RustPlusBot.Features.Connections/`):

- Modify `ConnectionOptions.cs` — add `MarkerPollFastInterval`, `RigRadius`, `RigActiveWindow`, `RigOfflineWindow`, `RigTickInterval`; lower `MarkerPollInterval` default to 5s.
- Modify `Listening/IRustServerConnection.cs` — add `GetMonumentsAsync`.
- Modify `Listening/RustPlusSocketSource.cs` — implement `GetMonumentsAsync` (untested shim).
- Modify `Supervisor/ConnectionSupervisor.cs` — fetch monuments on connect, filter rigs, rig-radius detection + adaptive cadence in `PollMarkersAsync`.
- Modify `Program.cs` validation (in the Host project) — validate the new options.

**Events** (`src/RustPlusBot.Features.Events/`):

- Create `State/RigStatus.cs`, `State/RigState.cs`, `State/IRigState.cs`, `State/RigStateStore.cs`.
- Modify `Rendering/EventLocalizationCatalog.cs` — add rig + in-game-line keys.
- Modify `Rendering/EventEmbedRenderer.cs` — render rig events.
- Modify `Relaying/EventRelay.cs` — consume `RigStateChangedEvent`, broadcast in-game for ALL events.
- Modify `Hosting/EventsHostedService.cs` — rig event bus loop + background rig-timer tick.
- Modify `EventServiceCollectionExtensions.cs` — register `RigStateStore`/`IRigState`.

**Commands** (`src/RustPlusBot.Features.Commands/`):

- Create `Handlers/SmallCommandHandler.cs`, `Handlers/LargeCommandHandler.cs`.
- Modify `Localization/CommandLocalizationCatalog.cs` — add `command.small.*`/`command.large.*` keys.
- Modify `CommandServiceCollectionExtensions.cs` — register the two handlers.
- Modify `Modules/ServerCommandModule.cs` — add `/small` `/large`.

**Test doubles:**

- Modify `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` — implement `GetMonumentsAsync`.

---

## Task 1: Abstractions — MonumentSnapshot, RigKind, RigEventKind, RigStateChangedEvent

**Files:**

- Create: `src/RustPlusBot.Abstractions/Connections/MonumentSnapshot.cs`
- Create: `src/RustPlusBot.Abstractions/Events/RigKind.cs`
- Create: `src/RustPlusBot.Abstractions/Events/RigEventKind.cs`
- Create: `src/RustPlusBot.Abstractions/Events/RigStateChangedEvent.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/Events/RigStateChangedEventTests.cs`

**Interfaces:**

- Produces:
  - `record MonumentSnapshot(string Token, float X, float Y)` — ns `RustPlusBot.Features.Connections.Listening`.
  - `enum RigKind { Small, Large }` — ns `RustPlusBot.Abstractions.Events`.
  - `enum RigEventKind { Activated, CrateLootable, Respawned }` — ns `RustPlusBot.Abstractions.Events`.
  - `record RigStateChangedEvent(ulong GuildId, Guid ServerId, RigKind Rig, RigEventKind Kind, float X, float Y, MapDimensions? Dimensions)` — ns `RustPlusBot.Abstractions.Events`.

- [ ] **Step 1: Write the failing test**

`tests/RustPlusBot.Abstractions.Tests/Events/RigStateChangedEventTests.cs`:

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Abstractions.Tests.Events;

public sealed class RigStateChangedEventTests
{
    [Fact]
    public void Carries_rig_kind_event_kind_position_and_dimensions()
    {
        var server = Guid.NewGuid();
        var dims = new MapDimensions(4000u, 4000u, 500);

        var evt = new RigStateChangedEvent(7UL, server, RigKind.Large, RigEventKind.CrateLootable, 1f, 2f, dims);

        Assert.Equal(7UL, evt.GuildId);
        Assert.Equal(server, evt.ServerId);
        Assert.Equal(RigKind.Large, evt.Rig);
        Assert.Equal(RigEventKind.CrateLootable, evt.Kind);
        Assert.Equal(1f, evt.X);
        Assert.Equal(2f, evt.Y);
        Assert.Equal(dims, evt.Dimensions);
    }

    [Fact]
    public void Monument_snapshot_carries_token_and_position()
    {
        var m = new MonumentSnapshot("oilrig_1", 10f, 20f);
        Assert.Equal("oilrig_1", m.Token);
        Assert.Equal(10f, m.X);
        Assert.Equal(20f, m.Y);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter RigStateChangedEventTests`
Expected: FAIL — compile error (types not defined). If `RustPlusBot.Abstractions.Tests` does not exist yet, check `ls tests/` — if absent, create it mirroring another `*.Tests` csproj (reference `RustPlusBot.Abstractions`, the xUnit/NSubstitute package set used elsewhere) and add it to `RustPlusBot.slnx`. (2a added `Features.Events.Tests`; an Abstractions test project may already exist — `ls tests/` first.)

- [ ] **Step 3: Create the types**

`src/RustPlusBot.Abstractions/Connections/MonumentSnapshot.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One named monument observed in a <c>GetMap</c> response.</summary>
/// <param name="Token">The monument token (e.g. <c>oilrig_1</c>, <c>large_oil_rig</c>).</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
public sealed record MonumentSnapshot(string Token, float X, float Y);
```

`src/RustPlusBot.Abstractions/Events/RigKind.cs`:

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>Which oil rig a rig event refers to.</summary>
public enum RigKind
{
    /// <summary>The small oil rig (monument token <c>oilrig_1</c>).</summary>
    Small = 0,

    /// <summary>The large oil rig (monument token <c>large_oil_rig</c>).</summary>
    Large = 1,
}
```

`src/RustPlusBot.Abstractions/Events/RigEventKind.cs`:

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>The rig lifecycle boundary an event marks.</summary>
public enum RigEventKind
{
    /// <summary>A CH47 reached the rig — the combat (unlocking) phase began.</summary>
    Activated = 0,

    /// <summary>The combat phase ended — the crate is now lootable.</summary>
    CrateLootable = 1,

    /// <summary>The dormant window ended — the crate respawned and the rig is armed again.</summary>
    Respawned = 2,
}
```

`src/RustPlusBot.Abstractions/Events/RigStateChangedEvent.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when an oil rig crosses a lifecycle boundary (activated / crate lootable / respawned).</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="Rig">Which rig.</param>
/// <param name="Kind">The boundary that was crossed.</param>
/// <param name="X">The rig monument's world X coordinate.</param>
/// <param name="Y">The rig monument's world Y coordinate.</param>
/// <param name="Dimensions">Map dimensions for grid-reference rendering, or null if unavailable.</param>
public sealed record RigStateChangedEvent(
    ulong GuildId,
    Guid ServerId,
    RigKind Rig,
    RigEventKind Kind,
    float X,
    float Y,
    MapDimensions? Dimensions);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter RigStateChangedEventTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Abstractions tests/RustPlusBot.Abstractions.Tests
git commit -m "feat(events): add MonumentSnapshot, RigKind, RigEventKind, RigStateChangedEvent"
```

---

## Task 2: Connections options — adaptive cadence + rig windows

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/ConnectionOptions.cs`
- Modify: `src/RustPlusBot.RustPlusBot/Program.cs` (the Host — find where `ConnectionOptions` is validated; add `.Validate(...)` clauses)
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionOptionsTests.cs` (create if absent; otherwise extend the existing options test)

**Interfaces:**

- Produces: `ConnectionOptions.MarkerPollFastInterval` (TimeSpan), `.RigRadius` (float), `.RigActiveWindow` (TimeSpan), `.RigOfflineWindow` (TimeSpan), `.RigTickInterval` (TimeSpan); `.MarkerPollInterval` default now 5s.

- [ ] **Step 1: Write the failing test**

Add to `tests/RustPlusBot.Features.Connections.Tests/ConnectionOptionsTests.cs` (create the class if it doesn't exist; `using RustPlusBot.Features.Connections;`):

```csharp
[Fact]
public void Defaults_match_the_2a_ii_design()
{
    var o = new ConnectionOptions();

    Assert.Equal(TimeSpan.FromSeconds(5), o.MarkerPollInterval);
    Assert.Equal(TimeSpan.FromSeconds(2), o.MarkerPollFastInterval);
    Assert.Equal(150f, o.RigRadius);
    Assert.Equal(TimeSpan.FromMinutes(15), o.RigActiveWindow);
    Assert.Equal(TimeSpan.FromMinutes(15), o.RigOfflineWindow);
    Assert.Equal(TimeSpan.FromSeconds(30), o.RigTickInterval);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionOptionsTests`
Expected: FAIL — `MarkerPollFastInterval`/`RigRadius`/etc. not defined; `MarkerPollInterval` is 10s.

- [ ] **Step 3: Add the options**

In `src/RustPlusBot.Features.Connections/ConnectionOptions.cs`, change `MarkerPollInterval`'s default and append the new properties:

```csharp
    /// <summary>How often to poll map markers for live-event detection. Default 5s.</summary>
    public TimeSpan MarkerPollInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Faster poll cadence used while a CH47 marker is live (to catch its brief oil-rig visit). Default 2s.</summary>
    public TimeSpan MarkerPollFastInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Distance (world units) within which a CH47 is considered to be "at" an oil rig. Default 150.</summary>
    public float RigRadius { get; set; } = 150f;

    /// <summary>How long an oil rig stays in the combat (Active) phase before the crate becomes lootable. Default 15m.</summary>
    public TimeSpan RigActiveWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long an oil rig stays dormant (Offline) before the crate respawns (Online). Default 15m.</summary>
    public TimeSpan RigOfflineWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How often the rig-timer tick advances rig phases and emits timed boundary events. Default 30s.</summary>
    public TimeSpan RigTickInterval { get; set; } = TimeSpan.FromSeconds(30);
```

- [ ] **Step 4: Add validation in the Host**

Find the `AddOptions<ConnectionOptions>()...ValidateOnStart()` registration in `Program.cs` (search: `grep -rn "ConnectionOptions" src/RustPlusBot.RustPlusBot/Program.cs`). Extend its `.Validate(...)` to cover the new members. Match the existing validation style (the file already validates `MarkerPollInterval > Zero` from 2a). Add clauses such as:

```csharp
            .Validate(o => o.MarkerPollFastInterval > TimeSpan.Zero, "MarkerPollFastInterval must be positive.")
            .Validate(o => o.RigRadius > 0f, "RigRadius must be positive.")
            .Validate(o => o.RigActiveWindow > TimeSpan.Zero, "RigActiveWindow must be positive.")
            .Validate(o => o.RigOfflineWindow > TimeSpan.Zero, "RigOfflineWindow must be positive.")
            .Validate(o => o.RigTickInterval > TimeSpan.Zero, "RigTickInterval must be positive.")
```

(If the existing 2a registration used a single combined `.Validate`, follow that shape instead — read the surrounding lines first and stay consistent.)

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionOptionsTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Connections/ConnectionOptions.cs src/RustPlusBot.RustPlusBot/Program.cs tests/RustPlusBot.Features.Connections.Tests/ConnectionOptionsTests.cs
git commit -m "feat(connections): add adaptive-poll + rig-window options"
```

---

## Task 3: Connections seam — GetMonumentsAsync (interface + shim + fake)

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`

**Interfaces:**

- Produces: `IRustServerConnection.GetMonumentsAsync(TimeSpan timeout, CancellationToken)` → `Task<IReadOnlyList<MonumentSnapshot>>` (throws on failure, like `GetMapMarkersAsync`).
- Consumes (Task 1): `MonumentSnapshot`.

> The real `RustPlusSocketSource` implementation is the one **untested integration shim** (by repo convention, like every other RustPlusApi mapping). The test coverage is via the fake in the supervisor test (Task 4).

- [ ] **Step 1: Add the interface member**

In `IRustServerConnection.cs`, add after `GetMapDimensionsAsync`:

```csharp
    /// <summary>Gets the map monuments (for locating oil rigs). Throws on failure.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The map monuments (token + position).</returns>
    Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(TimeSpan timeout,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Build to see the compile failures**

Run: `dotnet build src/RustPlusBot.Features.Connections`
Expected: FAIL — `RustPlusSocketSource` and any test fake do not implement the new member. (This is the guardrail; implement next.)

- [ ] **Step 3: Implement on the real shim**

In `RustPlusSocketSource.cs`, after `GetMapDimensionsAsync`, add (mirrors its structure — returns data on success, throws on failure to match `GetMapMarkersAsync`'s contract):

```csharp
        public async Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            // CONFIRMED (2.0.0-beta.1): GetMapAsync returns Task<Response<RustPlusApi.Data.ServerMap>>.
            // ServerMap.Monuments is List<ServerMapMonument> with Name (= protobuf token, e.g. "oilrig_1"),
            // Nullable<float> X/Y. We surface (token, x, y) and skip monuments with incomplete coordinates.
            var response = await _rustPlus.GetMapAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccess || response.Data is null)
            {
                throw new InvalidOperationException("GetMap returned no data.");
            }

            var monuments = new List<MonumentSnapshot>();
            foreach (var m in response.Data.Monuments)
            {
                if (m.Name is null || m.X is not { } x || m.Y is not { } y)
                {
                    continue;
                }

                monuments.Add(new MonumentSnapshot(m.Name, x, y));
            }

            return monuments;
        }
```

> Note: `GetMapDimensionsAsync` and `GetMonumentsAsync` both call `_rustPlus.GetMapAsync` separately — acceptable (monuments are fetched once on connect; dims once on connect). If the reviewer flags the double call as wasteful, a follow-up can cache one `GetMapAsync` response; do NOT over-engineer it now (YAGNI).

- [ ] **Step 4: Implement on the fake**

In `FakeRustSocketSource.cs` inside `FakeConnection`, add a settable result and the method (place near `DimensionsResult`):

```csharp
        /// <summary>The monuments returned by <see cref="GetMonumentsAsync"/>. Defaults to empty.</summary>
        public IReadOnlyList<MonumentSnapshot> MonumentsResult { get; set; } = [];
```

and the method (near `GetMapDimensionsAsync`):

```csharp
        public Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MonumentsResult);
```

- [ ] **Step 5: Build to verify it compiles**

Run: `dotnet build RustPlusBot.slnx`
Expected: BUILD SUCCEEDED, 0 warnings.

- [ ] **Step 6: Run the Connections test suite (no regressions)**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests`
Expected: PASS (existing count unchanged — this task adds no test of its own; the shim is covered in Task 4).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs
git commit -m "feat(connections): add GetMonumentsAsync to the socket seam"
```

---

## Task 4: Connections — rig detection + adaptive cadence in the marker poll

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/Supervisor/ConnectionSupervisorMarkerTests.cs` (find the existing 2a marker test file — `grep -rln "EnqueueMarkers" tests/RustPlusBot.Features.Connections.Tests` — and add to it; if none, create this file mirroring the existing supervisor-test harness).

**Interfaces:**

- Consumes (Task 1): `RigStateChangedEvent`, `RigKind`, `RigEventKind`; (Task 2) the new `ConnectionOptions` members; (Task 3) `GetMonumentsAsync`.
- Produces: the supervisor publishes `RigStateChangedEvent(Kind=Activated)` when a CH47 marker enters a rig radius (once per continuous visit).

**Design notes for the implementer (read before coding):**

- `RunConnectedAsync` already fetches `dims` on connect and starts `PollMarkersAsync` via `Task.Run` (see `ConnectionSupervisor.cs:353-358`). Add a one-shot monuments fetch next to `dims`, filter to the two rig tokens, and pass an `IReadOnlyList<RigPosition>` into `PollMarkersAsync`.
- Define a small private readonly record struct in the supervisor:
  `private readonly record struct RigPosition(RigKind Kind, float X, float Y);`
- Map tokens: `"oilrig_1" => RigKind.Small`, `"large_oil_rig" => RigKind.Large`, anything else skipped.
- Distance: squared-distance compare against `RigRadius * RigRadius` (avoid `Math.Sqrt`).
- Debounce: keep a `HashSet<RigKind> _rigsCurrentlyInRadius` local to the poll loop. On each poll, compute the set of rigs that have ANY CH47 within radius this poll. For a rig newly in the set (in `current` but not in the previous poll's set), publish `Activated`. Replace the previous set with the current. This fires once when a CH47 enters and not again until it leaves and returns.
- Adaptive cadence: after each poll, `var anyCh47 = current.Any(m => m.Kind == MarkerKind.Chinook); var delay = anyCh47 ? _options.MarkerPollFastInterval : _options.MarkerPollInterval;` then `await Task.Delay(delay, ct)`.
- The existing add/removed diff + `MapMarkersChangedEvent` publish stays exactly as-is. Rig detection runs against `current` (all live CH47 markers, not just `added`), because a CH47 spawns away from the rig and only later enters the radius.

- [ ] **Step 1: Write the failing test — CH47 entering a rig radius fires Activated**

In the supervisor marker test, add (adapt names to the existing harness — it already builds a supervisor with a `FakeRustSocketSource`, an `IEventBus` capture, options, and an in-memory store; reuse that setup helper):

```csharp
[Fact]
public async Task Ch47_entering_rig_radius_publishes_activated_once_per_visit()
{
    // Rig at (1000, 1000). Poll 1: CH47 far away (no event). Poll 2: CH47 within radius (Activated).
    // Poll 3: CH47 still within radius (no re-fire). Poll 4: CH47 gone (no event).
    var rig = new MonumentSnapshot("oilrig_1", 1000f, 1000f);
    fakeConnection.MonumentsResult = [rig];
    fake.EnqueueMarkers([new MapMarkerSnapshot(1, MarkerKind.Chinook, 0f, 0f, null)]);          // far
    fake.EnqueueMarkers([new MapMarkerSnapshot(1, MarkerKind.Chinook, 1010f, 1010f, null)]);    // in radius
    fake.EnqueueMarkers([new MapMarkerSnapshot(1, MarkerKind.Chinook, 1005f, 1005f, null)]);    // still in
    fake.EnqueueMarkers([]);                                                                     // gone

    // ... run the supervisor connected window long enough for >=4 polls (use the harness's
    // existing "run until N events captured / drain" mechanism with a short fast/base interval) ...

    var rigEvents = capturedEvents.OfType<RigStateChangedEvent>().ToList();
    Assert.Single(rigEvents);
    Assert.Equal(RigKind.Small, rigEvents[0].Rig);
    Assert.Equal(RigEventKind.Activated, rigEvents[0].Kind);
    Assert.Equal(1010f, rigEvents[0].X); // rig monument position is what the event carries
    // NOTE: the event carries the RIG monument position, not the CH47 position:
    Assert.Equal(1000f, rigEvents[0].X);
}
```

> Fix the contradictory assert before running: the event carries the **rig monument** position (1000f, 1000f), NOT the CH47 position. Keep only `Assert.Equal(1000f, rigEvents[0].X)` and `Assert.Equal(1000f, rigEvents[0].Y)`.

Set the test's options to tiny intervals (e.g. `MarkerPollInterval = MarkerPollFastInterval = TimeSpan.FromMilliseconds(20)`) so polls flow quickly. Reuse the existing harness pattern that drains markers and disconnects deterministically (look at how the 2a test asserts on `MapMarkersChangedEvent` — copy its run/settle approach; the supervisor uses a shared-cache in-memory DB + keep-alive connection, see the foundation-status note).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter Ch47_entering_rig_radius`
Expected: FAIL — no `RigStateChangedEvent` is ever published.

- [ ] **Step 3: Implement rig detection + adaptive cadence**

In `ConnectionSupervisor.cs`:

a) Add the private record struct (near the top of the class, by other private types):

```csharp
    private readonly record struct RigPosition(RigKind Kind, float X, float Y);
```

b) In `RunConnectedAsync`, where `dims` is fetched (line ~353), add a monuments fetch + rig filter and pass it to `PollMarkersAsync`:

```csharp
        var dims = await connection.GetMapDimensionsAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
        var rigs = await GetRigPositionsAsync(connection, ct).ConfigureAwait(false);
        // ...
        var markerPoll = Task.Run(() => PollMarkersAsync(key, connection, dims, rigs, pollCts.Token), CancellationToken.None);
```

c) Add the rig-position fetch helper (broad-catch → empty so a map-query failure just disables rig detection for this window, never crashes the loop):

```csharp
    private async Task<IReadOnlyList<RigPosition>> GetRigPositionsAsync(
        IRustServerConnection connection,
        CancellationToken ct)
    {
        try
        {
            var monuments = await connection.GetMonumentsAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
            var rigs = new List<RigPosition>();
            foreach (var m in monuments)
            {
                RigKind? kind = m.Token switch
                {
                    "oilrig_1" => RigKind.Small,
                    "large_oil_rig" => RigKind.Large,
                    _ => null,
                };
                if (kind is { } k)
                {
                    rigs.Add(new RigPosition(k, m.X, m.Y));
                }
            }

            return rigs;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a monuments-fetch failure just disables rig detection this window.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogMarkerPollFailed(logger, ex, default); // reuse the marker-poll-failed log; rig detection degrades gracefully
            return [];
        }
    }
```

d) Change `PollMarkersAsync`'s signature to accept `IReadOnlyList<RigPosition> rigs`, and inside the loop add rig detection + adaptive delay. The full revised method:

```csharp
    private async Task PollMarkersAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        MapDimensions? dims,
        IReadOnlyList<RigPosition> rigs,
        CancellationToken ct)
    {
        IReadOnlyList<MapMarkerSnapshot>? previous = null;
        var rigsInRadius = new HashSet<RigKind>();
        while (!ct.IsCancellationRequested)
        {
            var anyCh47 = false;
            try
            {
                var current = await connection.GetMapMarkersAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
                anyCh47 = current.Any(m => m.Kind == MarkerKind.Chinook);

                if (previous is null)
                {
                    previous = current; // first poll: silent baseline
                }
                else
                {
                    var added = current.Where(c => previous.All(p => p.Id != c.Id)).ToList();
                    var removed = previous.Where(p => current.All(c => c.Id != p.Id)).ToList();
                    previous = current;
                    if (added.Count > 0 || removed.Count > 0)
                    {
                        await eventBus.PublishAsync(
                                new MapMarkersChangedEvent(key.Guild, key.Server, dims, added, removed), ct)
                            .ConfigureAwait(false);
                    }
                }

                await DetectRigActivationsAsync(key, current, rigs, dims, rigsInRadius, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // stopping
            }
#pragma warning disable CA1031 // Broad catch: a failed poll is logged and skipped; the previous snapshot is retained.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogMarkerPollFailed(logger, ex, key.Server);
            }

            var delay = anyCh47 ? _options.MarkerPollFastInterval : _options.MarkerPollInterval;
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }

    private async Task DetectRigActivationsAsync(
        (ulong Guild, Guid Server) key,
        IReadOnlyList<MapMarkerSnapshot> current,
        IReadOnlyList<RigPosition> rigs,
        MapDimensions? dims,
        HashSet<RigKind> rigsInRadius,
        CancellationToken ct)
    {
        if (rigs.Count == 0)
        {
            return;
        }

        var radiusSquared = _options.RigRadius * _options.RigRadius;
        var nowInRadius = new HashSet<RigKind>();
        foreach (var rig in rigs)
        {
            foreach (var m in current)
            {
                if (m.Kind != MarkerKind.Chinook)
                {
                    continue;
                }

                var dx = m.X - rig.X;
                var dy = m.Y - rig.Y;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    nowInRadius.Add(rig.Kind);
                    if (!rigsInRadius.Contains(rig.Kind))
                    {
                        await eventBus.PublishAsync(
                                new RigStateChangedEvent(key.Guild, key.Server, rig.Kind, RigEventKind.Activated,
                                    rig.X, rig.Y, dims), ct)
                            .ConfigureAwait(false);
                    }

                    break; // one CH47 in radius is enough for this rig
                }
            }
        }

        rigsInRadius.Clear();
        rigsInRadius.UnionWith(nowInRadius);
    }
```

Add the needed `using RustPlusBot.Abstractions.Events;` if not present.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter Ch47_entering_rig_radius`
Expected: PASS.

- [ ] **Step 5: Add a guard test — generic Chinook event still fires + no rig event when out of radius**

```csharp
[Fact]
public async Task Ch47_far_from_rig_publishes_chinook_event_but_no_rig_event()
{
    fakeConnection.MonumentsResult = [new MonumentSnapshot("oilrig_1", 1000f, 1000f)];
    fake.EnqueueMarkers([]);                                                                 // baseline
    fake.EnqueueMarkers([new MapMarkerSnapshot(1, MarkerKind.Chinook, 0f, 0f, null)]);       // spawned far
    fake.EnqueueMarkers([]);                                                                 // gone

    // ... run window ...

    Assert.Contains(capturedEvents.OfType<MapMarkersChangedEvent>(), e => e.Added.Any(m => m.Kind == MarkerKind.Chinook));
    Assert.Empty(capturedEvents.OfType<RigStateChangedEvent>());
}
```

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter Ch47_far_from_rig`
Expected: PASS.

- [ ] **Step 6: Run the full Connections suite + jb format**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests` then `dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Expected: all green; commit any jb reordering.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(connections): detect oil-rig activation from CH47 radius + adaptive poll cadence"
```

---

## Task 5: Events — RigStateStore + IRigState (state machine, no I/O)

**Files:**

- Create: `src/RustPlusBot.Features.Events/State/RigStatus.cs`
- Create: `src/RustPlusBot.Features.Events/State/RigState.cs`
- Create: `src/RustPlusBot.Features.Events/State/IRigState.cs`
- Create: `src/RustPlusBot.Features.Events/State/RigStateStore.cs`
- Modify: `src/RustPlusBot.Features.Events/EventServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/State/RigStateStoreTests.cs`

**Interfaces:**

- Consumes (Task 1): `RigKind`, `RigEventKind`, `RigStateChangedEvent`; `MapDimensions`; (Task 2) `ConnectionOptions`.
- Produces:
  - `enum RigStatus { Online, Active, Offline }` (ns `RustPlusBot.Features.Events.State`).
  - `record RigState(RigStatus Status, TimeSpan? Remaining)` — the public read result (ns `…State`).
  - `interface IRigState { RigState Get(ulong guildId, Guid serverId, RigKind rig); }`.
  - `sealed class RigStateStore(IClock clock, IOptions<ConnectionOptions> options) : IRigState` with:
    - `RigState Get(...)` — untracked ⇒ `Online`/`null`; settles overdue transitions on read.
    - `void Apply(RigStateChangedEvent activated)` — sets that rig Active@now; stores X/Y/dims (only called with `Kind=Activated`).
    - `IReadOnlyList<RigCrossing> Advance(DateTimeOffset now)` — advances every tracked rig's due timer, returns crossings to publish, and drops a rig that returns to Online.
    - `void Clear(ulong guildId, Guid serverId)`.
  - `readonly record struct RigCrossing(ulong GuildId, Guid ServerId, RigKind Rig, RigEventKind Kind, float X, float Y, MapDimensions? Dimensions)`.

**State-machine semantics (implement exactly):**

- Untracked rig ⇒ `Get` returns `new RigState(RigStatus.Online, null)`.
- `Apply` (Activated): set `Active`, `PhaseStart = now`, store `X/Y/Dimensions`.
- `Active` for ≥ `RigActiveWindow` ⇒ → `Offline`, `PhaseStart` advances by `RigActiveWindow`, crossing `CrateLootable`.
- `Offline` for ≥ `RigOfflineWindow` ⇒ → `Online`, crossing `Respawned`, then **remove the rig from the dict** (back to untracked = Online default).
- `Get` Remaining: `Active` ⇒ `RigActiveWindow - (now - PhaseStart)`; `Offline` ⇒ `RigOfflineWindow - (now - PhaseStart)`; clamp at `TimeSpan.Zero`; `Online` ⇒ `null`.
- `Get` settles an overdue transition before returning (so a read between ticks is current) — but `Get` does NOT emit crossings (only `Advance` does, from the tick). Settling on read and `Advance` must be idempotent: whichever runs first transitions; the other sees the already-advanced state.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Events.Tests/State/RigStateStoreTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Events.Tests.State;

public sealed class RigStateStoreTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    }

    private static (RigStateStore Store, TestClock Clock) Create()
    {
        var clock = new TestClock();
        var options = Options.Create(new ConnectionOptions
        {
            RigActiveWindow = TimeSpan.FromMinutes(15),
            RigOfflineWindow = TimeSpan.FromMinutes(15),
        });
        return (new RigStateStore(clock, options), clock);
    }

    private static RigStateChangedEvent Activated(RigKind rig) =>
        new(Guild, Server, rig, RigEventKind.Activated, 100f, 200f, null);

    [Fact]
    public void Untracked_rig_reads_online_with_no_timer()
    {
        var (store, _) = Create();
        var state = store.Get(Guild, Server, RigKind.Small);
        Assert.Equal(RigStatus.Online, state.Status);
        Assert.Null(state.Remaining);
    }

    [Fact]
    public void Apply_activated_sets_active_with_remaining()
    {
        var (store, _) = Create();
        store.Apply(Activated(RigKind.Small));
        var state = store.Get(Guild, Server, RigKind.Small);
        Assert.Equal(RigStatus.Active, state.Status);
        Assert.Equal(TimeSpan.FromMinutes(15), state.Remaining);
    }

    [Fact]
    public void Advance_active_past_window_yields_crate_lootable_and_goes_offline()
    {
        var (store, clock) = Create();
        store.Apply(Activated(RigKind.Small));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);

        var crossings = store.Advance(clock.UtcNow);

        Assert.Single(crossings);
        Assert.Equal(RigEventKind.CrateLootable, crossings[0].Kind);
        Assert.Equal(RigKind.Small, crossings[0].Rig);
        Assert.Equal(100f, crossings[0].X);
        Assert.Equal(RigStatus.Offline, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Advance_offline_past_window_yields_respawned_and_returns_to_online_untracked()
    {
        var (store, clock) = Create();
        store.Apply(Activated(RigKind.Small));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        store.Advance(clock.UtcNow); // -> Offline
        clock.UtcNow = clock.UtcNow.AddMinutes(15);

        var crossings = store.Advance(clock.UtcNow); // -> Online (Respawned), then dropped

        Assert.Single(crossings);
        Assert.Equal(RigEventKind.Respawned, crossings[0].Kind);
        Assert.Equal(RigStatus.Online, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Get_settles_overdue_transition_on_read()
    {
        var (store, clock) = Create();
        store.Apply(Activated(RigKind.Small));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);

        // No Advance call; reading must settle Active -> Offline.
        Assert.Equal(RigStatus.Offline, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Activated_in_any_state_resets_to_active()
    {
        var (store, clock) = Create();
        store.Apply(Activated(RigKind.Small));
        clock.UtcNow = clock.UtcNow.AddMinutes(15);
        store.Advance(clock.UtcNow); // Offline
        store.Apply(Activated(RigKind.Small)); // ground-truth override
        Assert.Equal(RigStatus.Active, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Clear_drops_tracked_rigs()
    {
        var (store, _) = Create();
        store.Apply(Activated(RigKind.Small));
        store.Clear(Guild, Server);
        Assert.Equal(RigStatus.Online, store.Get(Guild, Server, RigKind.Small).Status);
    }

    [Fact]
    public void Advance_returns_nothing_for_untracked_or_within_window()
    {
        var (store, clock) = Create();
        Assert.Empty(store.Advance(clock.UtcNow));
        store.Apply(Activated(RigKind.Small));
        Assert.Empty(store.Advance(clock.UtcNow)); // still within active window
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter RigStateStoreTests`
Expected: FAIL — types not defined.

- [ ] **Step 3: Create the types**

`src/RustPlusBot.Features.Events/State/RigStatus.cs`:

```csharp
namespace RustPlusBot.Features.Events.State;

/// <summary>The inferred lifecycle status of an oil rig.</summary>
public enum RigStatus
{
    /// <summary>Crate present and armed; the resting state (also assumed before any activation is seen).</summary>
    Online = 0,

    /// <summary>Combat phase — scientists deployed, crate unlocking (becomes lootable at the end).</summary>
    Active = 1,

    /// <summary>Crate became lootable and (in practice) was looted; the rig is dormant until respawn.</summary>
    Offline = 2,
}
```

`src/RustPlusBot.Features.Events/State/RigState.cs`:

```csharp
namespace RustPlusBot.Features.Events.State;

/// <summary>A point-in-time read of an oil rig's status and time remaining in the current phase.</summary>
/// <param name="Status">The current status.</param>
/// <param name="Remaining">Time left in the current timed phase, or null when Online (no timer).</param>
public sealed record RigState(RigStatus Status, TimeSpan? Remaining);
```

`src/RustPlusBot.Features.Events/State/IRigState.cs`:

```csharp
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Events.State;

/// <summary>Reads the current inferred oil-rig status for a server.</summary>
public interface IRigState
{
    /// <summary>Gets the current status + time remaining for one rig. An untracked rig reads as Online.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="rig">Which rig.</param>
    /// <returns>The current rig state.</returns>
    RigState Get(ulong guildId, Guid serverId, RigKind rig);
}
```

`src/RustPlusBot.Features.Events/State/RigStateStore.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Events.State;

/// <summary>One timed boundary an oil rig crossed, ready to publish as a <see cref="RigStateChangedEvent"/>.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="Rig">Which rig.</param>
/// <param name="Kind">The boundary crossed (CrateLootable or Respawned).</param>
/// <param name="X">The rig monument's world X coordinate.</param>
/// <param name="Y">The rig monument's world Y coordinate.</param>
/// <param name="Dimensions">Map dimensions for grid rendering, or null.</param>
public readonly record struct RigCrossing(
    ulong GuildId,
    Guid ServerId,
    RigKind Rig,
    RigEventKind Kind,
    float X,
    float Y,
    MapDimensions? Dimensions);

/// <summary>
/// In-memory per-(guild, server, rig) oil-rig state machine. An untracked rig is Online by default;
/// a rig enters the map on its first <see cref="Apply"/> (Activated) and is dropped again once it
/// respawns back to Online. Timed transitions are driven by <see cref="Advance"/> (the tick) and
/// also settled lazily on <see cref="Get"/>.
/// </summary>
/// <param name="clock">Supplies the current time.</param>
/// <param name="options">Supplies the rig phase windows.</param>
public sealed class RigStateStore(IClock clock, IOptions<ConnectionOptions> options) : IRigState
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server, RigKind Rig), Entry> _rigs = new();

    /// <inheritdoc />
    public RigState Get(ulong guildId, Guid serverId, RigKind rig)
    {
        var now = clock.UtcNow;
        var key = (guildId, serverId, rig);
        lock (Gate)
        {
            if (!_rigs.TryGetValue(key, out var entry))
            {
                return new RigState(RigStatus.Online, null);
            }

            // Settle any overdue transitions (without emitting crossings — Advance does that).
            while (TrySettle(key, entry, now, out var next))
            {
                if (next is null)
                {
                    return new RigState(RigStatus.Online, null); // respawned -> dropped
                }

                entry = next;
            }

            var remaining = Remaining(entry, now);
            return new RigState(entry.Status, remaining);
        }
    }

    /// <summary>Sets a rig Active (the CH47 ground-truth override from any state).</summary>
    /// <param name="activated">The activation event (must be <see cref="RigEventKind.Activated"/>).</param>
    public void Apply(RigStateChangedEvent activated)
    {
        ArgumentNullException.ThrowIfNull(activated);
        var key = (activated.GuildId, activated.ServerId, activated.Rig);
        lock (Gate)
        {
            _rigs[key] = new Entry(RigStatus.Active, clock.UtcNow, activated.X, activated.Y, activated.Dimensions);
        }
    }

    /// <summary>Advances every tracked rig whose timed window has elapsed, returning the crossings to publish.</summary>
    /// <param name="now">The current time.</param>
    /// <returns>The boundary crossings (CrateLootable / Respawned) since the last advance.</returns>
    public IReadOnlyList<RigCrossing> Advance(DateTimeOffset now)
    {
        var crossings = new List<RigCrossing>();
        lock (Gate)
        {
            foreach (var key in _rigs.Keys.ToList())
            {
                if (!_rigs.TryGetValue(key, out var entry))
                {
                    continue;
                }

                while (TrySettle(key, entry, now, out var next, recordInto: crossings))
                {
                    if (next is null)
                    {
                        break; // dropped
                    }

                    entry = next;
                }
            }
        }

        return crossings;
    }

    /// <summary>Drops all rig state for a server (on disconnect).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId)
    {
        lock (Gate)
        {
            foreach (var key in _rigs.Keys.Where(k => k.Guild == guildId && k.Server == serverId).ToList())
            {
                _rigs.TryRemove(key, out _);
            }
        }
    }

    private object Gate { get; } = new();

    /// <summary>Performs one due transition. Returns true if a transition happened; sets <paramref name="next"/>
    /// to the new entry, or null if the rig respawned (and was dropped). Optionally records the crossing.</summary>
    private bool TrySettle(
        (ulong Guild, Guid Server, RigKind Rig) key,
        Entry entry,
        DateTimeOffset now,
        out Entry? next,
        List<RigCrossing>? recordInto = null)
    {
        var opts = options.Value;
        switch (entry.Status)
        {
            case RigStatus.Active when now - entry.PhaseStart >= opts.RigActiveWindow:
                next = entry with { Status = RigStatus.Offline, PhaseStart = entry.PhaseStart + opts.RigActiveWindow };
                _rigs[key] = next;
                recordInto?.Add(new RigCrossing(key.Guild, key.Server, key.Rig, RigEventKind.CrateLootable,
                    entry.X, entry.Y, entry.Dimensions));
                return true;
            case RigStatus.Offline when now - entry.PhaseStart >= opts.RigOfflineWindow:
                _rigs.TryRemove(key, out _); // respawn -> back to untracked Online
                recordInto?.Add(new RigCrossing(key.Guild, key.Server, key.Rig, RigEventKind.Respawned,
                    entry.X, entry.Y, entry.Dimensions));
                next = null;
                return true;
            default:
                next = entry;
                return false;
        }
    }

    private TimeSpan? Remaining(Entry entry, DateTimeOffset now)
    {
        var opts = options.Value;
        var window = entry.Status switch
        {
            RigStatus.Active => opts.RigActiveWindow,
            RigStatus.Offline => opts.RigOfflineWindow,
            _ => (TimeSpan?)null,
        };
        if (window is not { } w)
        {
            return null;
        }

        var remaining = w - (now - entry.PhaseStart);
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private sealed record Entry(RigStatus Status, DateTimeOffset PhaseStart, float X, float Y, MapDimensions? Dimensions);
}
```

> Implementer note on `TrySettle` returning `next` that equals `entry` in the `default` case: the `default` arm returns `false`, so the `while` loops in `Get`/`Advance` stop. The `case … when …` only fires when a window has elapsed, so a single elapsed Active window settles to Offline, and a *further* elapsed Offline window can settle to Respawned in the same loop pass (handles a tick that was very late). This is intended.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter RigStateStoreTests`
Expected: PASS (8 tests).

- [ ] **Step 5: Register in DI**

In `EventServiceCollectionExtensions.AddEvents`, add (next to `EventStateStore`):

```csharp
        services.AddSingleton<RigStateStore>();
        services.AddSingleton<IRigState>(sp => sp.GetRequiredService<RigStateStore>());
```

- [ ] **Step 6: Build + commit**

Run: `dotnet build RustPlusBot.slnx` (expect 0/0).

```bash
git add src/RustPlusBot.Features.Events tests/RustPlusBot.Features.Events.Tests/State/RigStateStoreTests.cs
git commit -m "feat(events): add RigStateStore timed three-state machine"
```

---

## Task 6: Events — localization + rig embed rendering + in-game lines

**Files:**

- Modify: `src/RustPlusBot.Features.Events/Rendering/EventLocalizationCatalog.cs`
- Modify: `src/RustPlusBot.Features.Events/Rendering/EventEmbedRenderer.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/Rendering/` (add rig-render + in-game-line tests; mirror the existing renderer test if present, else create `RigRenderingTests.cs`)

**Interfaces:**

- Consumes: `RigStateChangedEvent`, `RigKind`, `RigEventKind`; `GridReference`; `IEventLocalizer`.
- Produces on `EventEmbedRenderer`:
  - `Embed RenderRig(RigStateChangedEvent evt, string culture)`
  - `string RenderRigLine(RigStateChangedEvent evt, string culture)` (the in-game team-chat line)
  - `string RenderLine(RustMapEvent evt, string culture)` (in-game line for cargo/heli/chinook)

**Localization keys to add (EN + FR), both catalogs:**

- Embeds (description, `{0}` = grid): `event.rig.small.activated`, `event.rig.small.lootable`, `event.rig.small.respawned`, and the three `event.rig.large.*` — plus reuse `event.title` author.
- In-game lines (plain text, `{0}` = grid): same 6 keys with a `.line` suffix → `event.rig.small.activated.line` … `event.rig.large.respawned.line`.
- In-game lines for existing events (plain text, `{0}` = grid): `event.cargo.entered.line`, `event.cargo.left.line`, `event.heli.entered.line`, `event.heli.left.line`, `event.chinook.spawned.line`.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Events.Tests/Rendering/RigRenderingTests.cs`:

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Rendering;

namespace RustPlusBot.Features.Events.Tests.Rendering;

public sealed class RigRenderingTests
{
    private static EventEmbedRenderer Renderer() =>
        new(new EventLocalizer(EventLocalizationCatalog.Default));

    [Theory]
    [InlineData(RigKind.Small, RigEventKind.Activated, "en")]
    [InlineData(RigKind.Small, RigEventKind.CrateLootable, "fr")]
    [InlineData(RigKind.Large, RigEventKind.Respawned, "en")]
    public void RenderRig_produces_a_nonempty_embed(RigKind rig, RigEventKind kind, string culture)
    {
        var evt = new RigStateChangedEvent(1UL, Guid.NewGuid(), rig, kind, 100f, 200f, null);
        var embed = Renderer().RenderRig(evt, culture);
        Assert.False(string.IsNullOrWhiteSpace(embed.Description));
    }

    [Fact]
    public void RenderRigLine_includes_a_grid_reference()
    {
        var evt = new RigStateChangedEvent(1UL, Guid.NewGuid(), RigKind.Small, RigEventKind.Activated, 100f, 200f, null);
        var line = Renderer().RenderRigLine(evt, "en");
        Assert.False(string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public void RenderLine_for_cargo_is_nonempty()
    {
        var evt = new RustMapEvent(MapEventKind.CargoEntered, 0f, 0f, null, DateTimeOffset.UtcNow);
        var line = Renderer().RenderLine(evt, "fr");
        Assert.False(string.IsNullOrWhiteSpace(line));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter RigRenderingTests`
Expected: FAIL — `RenderRig`/`RenderRigLine`/`RenderLine` not defined.

- [ ] **Step 3: Add localization keys**

In `EventLocalizationCatalog.cs`, add to BOTH the `["en"]` and `["fr"]` dictionaries. EN:

```csharp
                ["event.cargo.entered.line"] = "Cargo Ship entered at {0}",
                ["event.cargo.left.line"] = "Cargo Ship left ({0})",
                ["event.heli.entered.line"] = "Patrol Helicopter entered at {0}",
                ["event.heli.left.line"] = "Patrol Helicopter left ({0})",
                ["event.chinook.spawned.line"] = "Chinook spawned at {0}",
                ["event.rig.small.activated"] = "🛢️ Small Oil Rig activated — combat phase, crate lootable soon ({0})",
                ["event.rig.small.lootable"] = "🛢️ Small Oil Rig — crate is now LOOTABLE ({0})",
                ["event.rig.small.respawned"] = "🛢️ Small Oil Rig — crate respawned, armed again ({0})",
                ["event.rig.large.activated"] = "🛢️ Large Oil Rig activated — combat phase, crate lootable soon ({0})",
                ["event.rig.large.lootable"] = "🛢️ Large Oil Rig — crate is now LOOTABLE ({0})",
                ["event.rig.large.respawned"] = "🛢️ Large Oil Rig — crate respawned, armed again ({0})",
                ["event.rig.small.activated.line"] = "Small Oil Rig activated — combat phase ({0})",
                ["event.rig.small.lootable.line"] = "Small Oil Rig — crate is now lootable ({0})",
                ["event.rig.small.respawned.line"] = "Small Oil Rig — crate respawned ({0})",
                ["event.rig.large.activated.line"] = "Large Oil Rig activated — combat phase ({0})",
                ["event.rig.large.lootable.line"] = "Large Oil Rig — crate is now lootable ({0})",
                ["event.rig.large.respawned.line"] = "Large Oil Rig — crate respawned ({0})",
```

FR (translate; e.g.):

```csharp
                ["event.cargo.entered.line"] = "Cargo Ship arrivé en {0}",
                ["event.cargo.left.line"] = "Cargo Ship parti ({0})",
                ["event.heli.entered.line"] = "Hélicoptère de patrouille arrivé en {0}",
                ["event.heli.left.line"] = "Hélicoptère de patrouille parti ({0})",
                ["event.chinook.spawned.line"] = "Chinook apparu en {0}",
                ["event.rig.small.activated"] = "🛢️ Petite plateforme pétrolière activée — phase de combat, caisse bientôt lootable ({0})",
                ["event.rig.small.lootable"] = "🛢️ Petite plateforme pétrolière — caisse LOOTABLE ({0})",
                ["event.rig.small.respawned"] = "🛢️ Petite plateforme pétrolière — caisse réapparue, réarmée ({0})",
                ["event.rig.large.activated"] = "🛢️ Grande plateforme pétrolière activée — phase de combat, caisse bientôt lootable ({0})",
                ["event.rig.large.lootable"] = "🛢️ Grande plateforme pétrolière — caisse LOOTABLE ({0})",
                ["event.rig.large.respawned"] = "🛢️ Grande plateforme pétrolière — caisse réapparue, réarmée ({0})",
                ["event.rig.small.activated.line"] = "Petite plateforme activée — phase de combat ({0})",
                ["event.rig.small.lootable.line"] = "Petite plateforme — caisse lootable ({0})",
                ["event.rig.small.respawned.line"] = "Petite plateforme — caisse réapparue ({0})",
                ["event.rig.large.activated.line"] = "Grande plateforme activée — phase de combat ({0})",
                ["event.rig.large.lootable.line"] = "Grande plateforme — caisse lootable ({0})",
                ["event.rig.large.respawned.line"] = "Grande plateforme — caisse réapparue ({0})",
```

- [ ] **Step 4: Add the render methods**

In `EventEmbedRenderer.cs`, add a rig-key helper and the three methods. Add `using RustPlusBot.Abstractions.Events;`:

```csharp
    /// <summary>Renders a rig boundary event as a Discord embed.</summary>
    /// <param name="evt">The rig event.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The built embed.</returns>
    public Embed RenderRig(RigStateChangedEvent evt, string culture)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var grid = GridReference.From(evt.X, evt.Y, evt.Dimensions);
        return new EmbedBuilder()
            .WithAuthor(localizer.Get("event.title", culture))
            .WithDescription(localizer.Get(RigKey(evt), culture, grid))
            .Build();
    }

    /// <summary>Renders the in-game team-chat line for a rig boundary event.</summary>
    /// <param name="evt">The rig event.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The line text.</returns>
    public string RenderRigLine(RigStateChangedEvent evt, string culture)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var grid = GridReference.From(evt.X, evt.Y, evt.Dimensions);
        return localizer.Get(RigKey(evt) + ".line", culture, grid);
    }

    /// <summary>Renders the in-game team-chat line for a cargo/heli/chinook event.</summary>
    /// <param name="evt">The map event.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The line text.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The event kind is not a supported <see cref="MapEventKind"/>.</exception>
    public string RenderLine(RustMapEvent evt, string culture)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var grid = GridReference.From(evt.X, evt.Y, evt.Dimensions);
        var key = evt.Kind switch
        {
            MapEventKind.CargoEntered => "event.cargo.entered.line",
            MapEventKind.CargoLeft => "event.cargo.left.line",
            MapEventKind.HeliEntered => "event.heli.entered.line",
            MapEventKind.HeliLeft => "event.heli.left.line",
            MapEventKind.ChinookSpawned => "event.chinook.spawned.line",
            _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.Kind, "Unsupported map event kind."),
        };
        return localizer.Get(key, culture, grid);
    }

    private static string RigKey(RigStateChangedEvent evt)
    {
        var rig = evt.Rig == RigKind.Small ? "small" : "large";
        var phase = evt.Kind switch
        {
            RigEventKind.Activated => "activated",
            RigEventKind.CrateLootable => "lootable",
            RigEventKind.Respawned => "respawned",
            _ => throw new ArgumentOutOfRangeException(nameof(evt), evt.Kind, "Unsupported rig event kind."),
        };
        return $"event.rig.{rig}.{phase}";
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter RigRenderingTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Events/Rendering tests/RustPlusBot.Features.Events.Tests/Rendering/RigRenderingTests.cs
git commit -m "feat(events): rig embed + in-game-line rendering + EN/FR strings"
```

---

## Task 7: Events — EventRelay broadcasts to #events AND in-game, handles rig events

**Files:**

- Modify: `src/RustPlusBot.Features.Events/Relaying/EventRelay.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/Relaying/EventRelayTests.cs`

**Interfaces:**

- Consumes: `ITeamChatSender` (from Connections.Listening), `RigStateChangedEvent`, the Task-6 render methods, `RigStateStore`.
- Produces:
  - `EventRelay` ctor gains `ITeamChatSender teamChatSender` and `RigStateStore rigStore`.
  - `Task RelayRigAsync(RigStateChangedEvent evt, CancellationToken)` — Apply (if Activated) → embed to #events + line to in-game.
  - Existing `RelayAsync(MapMarkersChangedEvent, …)` now ALSO sends an in-game line per classified event.

**Design notes:**

- `ITeamChatSender.SendAsync(guildId, serverId, message, ct)` returns a `TeamChatSendResult`; ignore the result (best-effort broadcast). If there's no live socket it returns `NotConnected` — fine.
- In-game broadcast happens **regardless of whether the #events channel exists** (the two surfaces are independent). So send the in-game line first (or independently), then attempt the Discord post if a channel is resolved.
- For `RelayRigAsync`: call `rigStore.Apply(evt)` only when `evt.Kind == RigEventKind.Activated` (the timed kinds were already advanced by the tick). Always render + post + broadcast.

- [ ] **Step 1: Write the failing tests**

Extend `EventRelayTests.cs`. First update the `CreateRelay` helper to construct the new ctor (add an `ITeamChatSender` substitute and a `RigStateStore`), and return the sender so tests can assert on it. Then add:

```csharp
[Fact]
public async Task Relay_broadcasts_each_event_in_game_in_addition_to_discord()
{
    var (relay, _, poster, sender, _) = CreateRelay();

    await relay.RelayAsync(
        new MapMarkersChangedEvent(Guild, Server, null,
            [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null)],
            []),
        CancellationToken.None);

    await poster.Received(1).PostAsync(999UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
    await sender.Received(1).SendAsync(Guild, Server, Arg.Is<string>(s => !string.IsNullOrWhiteSpace(s)),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task RelayRig_activated_applies_state_posts_embed_and_broadcasts()
{
    var (relay, _, poster, sender, rigStore) = CreateRelay();
    var evt = new RigStateChangedEvent(Guild, Server, RigKind.Small, RigEventKind.Activated, 100f, 200f, null);

    await relay.RelayRigAsync(evt, CancellationToken.None);

    Assert.Equal(RigStatus.Active, rigStore.Get(Guild, Server, RigKind.Small).Status);
    await poster.Received(1).PostAsync(999UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
    await sender.Received(1).SendAsync(Guild, Server, Arg.Any<string>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task RelayRig_lootable_does_not_apply_but_still_alerts()
{
    var (relay, _, poster, sender, rigStore) = CreateRelay();
    var evt = new RigStateChangedEvent(Guild, Server, RigKind.Small, RigEventKind.CrateLootable, 100f, 200f, null);

    await relay.RelayRigAsync(evt, CancellationToken.None);

    // Not Active — the tick already advanced state; relay must not flip it back to Active.
    Assert.Equal(RigStatus.Online, rigStore.Get(Guild, Server, RigKind.Small).Status);
    await poster.Received(1).PostAsync(999UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
    await sender.Received(1).SendAsync(Guild, Server, Arg.Any<string>(), Arg.Any<CancellationToken>());
}
```

Update the `CreateRelay` helper signature/return. For the `RigStateStore`, construct it with the same `clock` and `Options.Create(new ConnectionOptions())`. Example helper body additions:

```csharp
    private static (EventRelay Relay, EventStateStore Store, IEventChannelPoster Poster,
        ITeamChatSender Sender, RigStateStore RigStore) CreateRelay(ulong? channelId = 999UL)
    {
        // ... existing clock/locator/poster/store/workspaceStore/provider ...
        var sender = Substitute.For<ITeamChatSender>();
        var rigStore = new RigStateStore(clock, Options.Create(new ConnectionOptions()));
        var renderer = new EventEmbedRenderer(new EventLocalizer(EventLocalizationCatalog.Default));

        var relay = new EventRelay(
            new MarkerEventClassifier(clock),
            store,
            renderer,
            locator,
            poster,
            sender,
            rigStore,
            provider.GetRequiredService<IServiceScopeFactory>());

        return (relay, store, poster, sender, rigStore);
    }
```

(Add `using Microsoft.Extensions.Options; using RustPlusBot.Features.Connections; using RustPlusBot.Features.Events.State;` to the test file.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter EventRelayTests`
Expected: FAIL — ctor arity mismatch / `RelayRigAsync` missing.

- [ ] **Step 3: Implement the relay changes**

Rewrite `EventRelay.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Posting;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Events.Relaying;

/// <summary>Posts every live event to #events AND in-game team chat; tracks rig state.</summary>
/// <param name="classifier">Classifies raw marker deltas into domain events.</param>
/// <param name="state">Tracks active markers and recent events per server.</param>
/// <param name="renderer">Renders events as embeds and in-game lines.</param>
/// <param name="locator">Resolves the #events Discord channel id.</param>
/// <param name="poster">Posts embeds to the Discord channel.</param>
/// <param name="teamChatSender">Broadcasts the in-game team-chat line.</param>
/// <param name="rigStore">Tracks oil-rig state (Apply on Activated).</param>
/// <param name="scopeFactory">Opens scopes to read guild culture.</param>
internal sealed class EventRelay(
    MarkerEventClassifier classifier,
    EventStateStore state,
    EventEmbedRenderer renderer,
    IEventChannelLocator locator,
    IEventChannelPoster poster,
    ITeamChatSender teamChatSender,
    RigStateStore rigStore,
    IServiceScopeFactory scopeFactory)
{
    /// <summary>Handles one <see cref="MapMarkersChangedEvent"/>: updates state, posts embeds, broadcasts in-game.</summary>
    /// <param name="evt">The marker delta.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the delta has been processed.</returns>
    public async Task RelayAsync(MapMarkersChangedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var events = classifier.Classify(evt);
        state.Apply(evt, events);
        if (events.Count == 0)
        {
            return;
        }

        var culture = await GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var e in events)
        {
            await teamChatSender.SendAsync(evt.GuildId, evt.ServerId, renderer.RenderLine(e, culture), cancellationToken)
                .ConfigureAwait(false);
            if (channelId is { } id)
            {
                await poster.PostAsync(id, renderer.Render(e, culture), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Handles one <see cref="RigStateChangedEvent"/>: applies activation, posts an embed, broadcasts in-game.</summary>
    /// <param name="evt">The rig boundary event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the rig event has been processed.</returns>
    public async Task RelayRigAsync(RigStateChangedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        if (evt.Kind == RigEventKind.Activated)
        {
            rigStore.Apply(evt);
        }

        var culture = await GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        await teamChatSender.SendAsync(evt.GuildId, evt.ServerId, renderer.RenderRigLine(evt, culture), cancellationToken)
            .ConfigureAwait(false);

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is { } id)
        {
            await poster.PostAsync(id, renderer.RenderRig(evt, culture), cancellationToken).ConfigureAwait(false);
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

> Behavior change: the existing 2a tests `When_channel_missing_updates_state_but_does_not_post` and `Empty_classification_posts_nothing` still hold — the in-game send happens only when there are classified events, and the embed post still requires a channel. The `Posts_one_embed_per_classified_event` test will now also see an in-game send; it only asserts on the poster, so it stays green.

- [ ] **Step 4: Run the full Events suite**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests`
Expected: PASS (existing 2a relay tests + new ones).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Events/Relaying/EventRelay.cs tests/RustPlusBot.Features.Events.Tests/Relaying/EventRelayTests.cs
git commit -m "feat(events): broadcast all events in-game + relay rig boundary events"
```

---

## Task 8: Events — EventsHostedService rig event loop + background tick

**Files:**

- Modify: `src/RustPlusBot.Features.Events/Hosting/EventsHostedService.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/Hosting/EventsHostedServiceTickTests.cs` (create)

**Interfaces:**

- Consumes: `IEventBus.SubscribeAsync<RigStateChangedEvent>`, `IEventBus.PublishAsync`, `RigStateStore.Advance`, `ConnectionOptions.RigTickInterval`, `IClock`.
- Produces: the host now (a) consumes `RigStateChangedEvent` → `EventRelay.RelayRigAsync`, and (b) runs a tick loop that calls `RigStateStore.Advance(clock.UtcNow)` every `RigTickInterval` and publishes one `RigStateChangedEvent` per returned crossing.

**Design notes:**

- The hosted service ctor gains `RigStateStore rigStore`, `IClock clock`, and `IOptions<ConnectionOptions> options`. `EventRelay` is already injected.
- Add a third loop task `_rigLoop` and a fourth `_tickLoop`. Mirror the existing `ConsumeMarkerEventsAsync` shape for the rig consumer (broad-catch around the `await foreach`).
- The tick loop: `while (!ct.IsCancellationRequested) { var crossings = rigStore.Advance(clock.UtcNow); foreach (var c in crossings) await eventBus.PublishAsync(new RigStateChangedEvent(c.GuildId, c.ServerId, c.Rig, c.Kind, c.X, c.Y, c.Dimensions), ct); await Task.Delay(options.Value.RigTickInterval, ct); }` with `OperationCanceledException` swallowed and a broad-catch log.
- Because the tick PUBLISHES `RigStateChangedEvent` and the rig consumer CONSUMES it, the `CrateLootable`/`Respawned` events flow through `RelayRigAsync` (which does NOT re-Apply for non-Activated kinds) → embed + in-game. Good: one code path for all three kinds.
- Extend `ClearIfDisconnectedAsync` to also call `rigStore.Clear(evt.GuildId, evt.ServerId)`.

- [ ] **Step 1: Write the failing test (tick publishes timed crossings)**

`tests/RustPlusBot.Features.Events.Tests/Hosting/EventsHostedServiceTickTests.cs` — test the tick logic via a small extracted method or by driving the store + a fake bus. Simplest: test the **publish-from-Advance** behavior directly against an in-memory bus capture. Since the loop is timing-driven, extract the tick body into an internal method `Task TickOnceAsync(CancellationToken)` on the service and test that:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Events.Hosting;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Events.Tests.Hosting;

public sealed class EventsHostedServiceTickTests
{
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public async Task TickOnce_publishes_a_crate_lootable_event_when_active_window_elapsed()
    {
        var clock = new TestClock();
        var options = Options.Create(new ConnectionOptions());
        var rigStore = new RigStateStore(clock, options);
        var bus = Substitute.For<IEventBus>();
        var server = Guid.NewGuid();

        rigStore.Apply(new RigStateChangedEvent(1UL, server, RigKind.Small, RigEventKind.Activated, 1f, 2f, null));
        clock.UtcNow = clock.UtcNow.Add(options.Value.RigActiveWindow);

        var service = EventsHostedServiceTestAccess.Create(bus, rigStore, clock, options);
        await service.TickOnceAsync(CancellationToken.None);

        await bus.Received(1).PublishAsync(
            Arg.Is<RigStateChangedEvent>(e => e.Kind == RigEventKind.CrateLootable && e.Rig == RigKind.Small),
            Arg.Any<CancellationToken>());
    }
}
```

> Implementer: provide a tiny test-accessor or make `TickOnceAsync` internal + `InternalsVisibleTo` the Events.Tests project (the csproj likely already exposes internals to its test project — check `RustPlusBot.Features.Events.csproj` for an existing `InternalsVisibleTo`; 2a's `EventStateStore` is internal and tested, so it does). If a factory helper is cleaner than a static accessor, write one in the test project that `new`s the service with the other (substituted) dependencies (`EventRelay` can be `null!` since `TickOnceAsync` doesn't touch it — or pass a real one built from substitutes). Prefer the minimal real construction.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter EventsHostedServiceTickTests`
Expected: FAIL — `TickOnceAsync` not defined.

- [ ] **Step 3: Implement the rig loop + tick**

Rewrite `EventsHostedService.cs` adding the dependencies, the two new loops, the extracted `TickOnceAsync`, the rig consumer, and the clear extension. Full file:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Connections;
using RustPlusBot.Features.Events.Relaying;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Persistence.Connections;

namespace RustPlusBot.Features.Events.Hosting;

/// <summary>Runs the marker relay loop, the rig-event relay loop, the rig-timer tick, and the disconnect-clear loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="relay">Relays marker + rig events into Discord #events and in-game chat.</param>
/// <param name="store">The marker state store, cleared on disconnect.</param>
/// <param name="rigStore">The rig state store, advanced by the tick and cleared on disconnect.</param>
/// <param name="clock">Supplies the current time for the tick.</param>
/// <param name="options">Supplies the rig-tick interval.</param>
/// <param name="scopeFactory">Opens scopes to read connection state.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class EventsHostedService(
    IEventBus eventBus,
    EventRelay relay,
    EventStateStore store,
    RigStateStore rigStore,
    IClock clock,
    IOptions<ConnectionOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<EventsHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _disconnectLoop;
    private Task? _relayLoop;
    private Task? _rigLoop;
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _relayLoop = Task.Run(() => ConsumeMarkerEventsAsync(_cts.Token), CancellationToken.None);
        _rigLoop = Task.Run(() => ConsumeRigEventsAsync(_cts.Token), CancellationToken.None);
        _tickLoop = Task.Run(() => RunRigTickAsync(_cts.Token), CancellationToken.None);
        _disconnectLoop = Task.Run(() => ConsumeConnectionStatusEventsAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _relayLoop, _rigLoop, _tickLoop, _disconnectLoop
                 }.Where(t => t is not null))
        {
            try
            {
#pragma warning disable VSTHRD003 // Our own loop tasks, joined on stop.
                await loop!.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    /// <summary>Runs one rig-timer tick: advances rig phases and publishes any timed boundary crossings.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the tick has published all crossings.</returns>
    internal async Task TickOnceAsync(CancellationToken cancellationToken)
    {
        var crossings = rigStore.Advance(clock.UtcNow);
        foreach (var c in crossings)
        {
            await eventBus.PublishAsync(
                    new RigStateChangedEvent(c.GuildId, c.ServerId, c.Rig, c.Kind, c.X, c.Y, c.Dimensions),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task RunRigTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await TickOnceAsync(cancellationToken).ConfigureAwait(false);
                await Task.Delay(options.Value.RigTickInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a faulting tick must not crash the host.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRigTickFaulted(logger, ex);
        }
    }

    private async Task ConsumeMarkerEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<MapMarkersChangedEvent>(cancellationToken)
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

    private async Task ConsumeRigEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<RigStateChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await relay.RelayRigAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogRigLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeConnectionStatusEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await ClearIfDisconnectedAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogDisconnectLoopFaulted(logger, ex);
        }
    }

    private async Task ClearIfDisconnectedAsync(ConnectionStatusChangedEvent evt, CancellationToken cancellationToken)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var connectionStore = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await connectionStore.GetStateAsync(evt.GuildId, evt.ServerId, cancellationToken)
                .ConfigureAwait(false);
            if (state is null || state.Status != ConnectionStatus.Connected)
            {
                store.Clear(evt.GuildId, evt.ServerId);
                rigStore.Clear(evt.GuildId, evt.ServerId);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Event relay loop faulted.")]
    private static partial void LogRelayLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rig relay loop faulted.")]
    private static partial void LogRigLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Rig tick loop faulted.")]
    private static partial void LogRigTickFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Disconnect-clear loop faulted.")]
    private static partial void LogDisconnectLoopFaulted(ILogger logger, Exception exception);
}
```

For the test accessor, add a small factory in the test project (since the ctor takes `EventRelay` which is non-trivial): construct `EventRelay` from substitutes, or — simpler — add an internal static test factory. Cleanest: in the test file, build the service via a helper that supplies a real `EventRelay` (its deps all substitutable) OR mark `TickOnceAsync` independent of `relay`. Given `TickOnceAsync` only uses `rigStore`, `clock`, `eventBus`, write the helper `EventsHostedServiceTestAccess.Create(bus, rigStore, clock, options)` in the test project that constructs the service passing substitutes/`null!` for the unused `relay`, `store`, `scopeFactory`, `logger` (use `NullLogger<EventsHostedService>.Instance`, `new EventStateStore(clock)`, `Substitute.For<IServiceScopeFactory>()`, and a real `EventRelay` is NOT needed for the tick). Verify `InternalsVisibleTo` exposes the internal service + `TickOnceAsync` to the test assembly.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter EventsHostedServiceTickTests`
Expected: PASS.

- [ ] **Step 5: Run the full Events suite + the registration test**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests`
Expected: PASS, including `EventRegistrationTests` (the host now resolves more deps — `RigStateStore`, `IClock`, `IOptions<ConnectionOptions>`; if `EventRegistrationTests` builds a provider that lacks `IOptions<ConnectionOptions>` or `IClock`, add those substitutes/registrations to the test — read it and fix).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Events/Hosting/EventsHostedService.cs tests/RustPlusBot.Features.Events.Tests/Hosting
git commit -m "feat(events): rig-event relay loop + background rig-timer tick"
```

---

## Task 9: Commands — !small / !large in-game handlers

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/SmallCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/LargeCommandHandler.cs`
- Modify: `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/RigCommandHandlersTests.cs`

**Interfaces:**

- Consumes: `IRigState`, `ICommandLocalizer`, `ICommandHandler`, `CommandContext`; `RigKind`, `RigStatus`; `DurationFormat.Compact`.
- Produces: two `internal sealed ICommandHandler` named `"small"` and `"large"`.

**Design notes:**

- Look at an existing handler that formats a duration (e.g. `!alive` / the team-intel handlers use `DurationFormat.Compact`) for the exact helper name/namespace. `grep -rn "DurationFormat" src/RustPlusBot.Features.Commands`.
- Reply logic per status:
  - `Online` → `localizer.Get("command.{rig}.online", culture)`
  - `Active` → `localizer.Get("command.{rig}.active", culture, DurationFormat.Compact(remaining))`
  - `Offline` → `localizer.Get("command.{rig}.offline", culture, DurationFormat.Compact(remaining))`
- `remaining` is non-null for Active/Offline by construction; guard with `?? TimeSpan.Zero`.
- Both handlers share logic — extract a private static helper or a shared base is overkill; just write two small classes calling a shared private method. To stay DRY without a base class, put the formatting in a tiny internal static `RigReply.For(IRigState, ctx, RigKind, ICommandLocalizer)` helper in `Handlers/` (or `Formatting/`). Keep it simple.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Commands.Tests/Handlers/RigCommandHandlersTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class RigCommandHandlersTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    private static CommandContext Ctx() => new(Guild, Server, "en", 0UL, string.Empty, []);

    private static ICommandLocalizer RealLocalizer() =>
        new CommandLocalizer(CommandLocalizationCatalog.Default);

    [Fact]
    public async Task Small_reports_online_when_untracked()
    {
        var rig = Substitute.For<IRigState>();
        rig.Get(Guild, Server, RigKind.Small).Returns(new RigState(RigStatus.Online, null));
        var handler = new SmallCommandHandler(rig, RealLocalizer());

        var reply = await handler.ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.Equal("small", handler.Name);
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    [Fact]
    public async Task Large_reports_active_with_remaining()
    {
        var rig = Substitute.For<IRigState>();
        rig.Get(Guild, Server, RigKind.Large)
            .Returns(new RigState(RigStatus.Active, TimeSpan.FromMinutes(8)));
        var handler = new LargeCommandHandler(rig, RealLocalizer());

        var reply = await handler.ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.Equal("large", handler.Name);
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }

    [Fact]
    public async Task Small_reports_offline_with_remaining()
    {
        var rig = Substitute.For<IRigState>();
        rig.Get(Guild, Server, RigKind.Small)
            .Returns(new RigState(RigStatus.Offline, TimeSpan.FromMinutes(6)));
        var handler = new SmallCommandHandler(rig, RealLocalizer());

        var reply = await handler.ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.False(string.IsNullOrWhiteSpace(reply));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter RigCommandHandlersTests`
Expected: FAIL — handler types not defined.

- [ ] **Step 3: Add localization keys**

In `CommandLocalizationCatalog.cs`, add to BOTH cultures (EN shown; add FR equivalents):

```csharp
                ["command.small.online"] = "Small Oil Rig: crate ready, waiting for activation.",
                ["command.small.active"] = "Small Oil Rig: combat phase — crate lootable in {0}.",
                ["command.small.offline"] = "Small Oil Rig: looted / dormant — respawns in {0}.",
                ["command.large.online"] = "Large Oil Rig: crate ready, waiting for activation.",
                ["command.large.active"] = "Large Oil Rig: combat phase — crate lootable in {0}.",
                ["command.large.offline"] = "Large Oil Rig: looted / dormant — respawns in {0}.",
```

FR:

```csharp
                ["command.small.online"] = "Petite plateforme : caisse prête, en attente d'activation.",
                ["command.small.active"] = "Petite plateforme : phase de combat — caisse lootable dans {0}.",
                ["command.small.offline"] = "Petite plateforme : pillée / dormante — réapparaît dans {0}.",
                ["command.large.online"] = "Grande plateforme : caisse prête, en attente d'activation.",
                ["command.large.active"] = "Grande plateforme : phase de combat — caisse lootable dans {0}.",
                ["command.large.offline"] = "Grande plateforme : pillée / dormante — réapparaît dans {0}.",
```

- [ ] **Step 4: Create the shared formatter + the two handlers**

`src/RustPlusBot.Features.Commands/Handlers/RigReply.cs` (verify `DurationFormat` namespace first — adjust the `using`):

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>Shared rig-status reply formatting for the !small/!large handlers.</summary>
internal static class RigReply
{
    /// <summary>Formats the localized rig-status reply for a rig.</summary>
    /// <param name="rigState">The rig state reader.</param>
    /// <param name="context">The command context.</param>
    /// <param name="rig">Which rig.</param>
    /// <param name="prefix">The localization key prefix ("command.small" / "command.large").</param>
    /// <param name="localizer">The reply localizer.</param>
    /// <returns>The localized reply.</returns>
    public static string For(
        IRigState rigState,
        CommandContext context,
        RigKind rig,
        string prefix,
        ICommandLocalizer localizer)
    {
        var state = rigState.Get(context.GuildId, context.ServerId, rig);
        return state.Status switch
        {
            RigStatus.Online => localizer.Get($"{prefix}.online", context.Culture),
            RigStatus.Active => localizer.Get($"{prefix}.active", context.Culture,
                DurationFormat.Compact(state.Remaining ?? TimeSpan.Zero)),
            RigStatus.Offline => localizer.Get($"{prefix}.offline", context.Culture,
                DurationFormat.Compact(state.Remaining ?? TimeSpan.Zero)),
            _ => localizer.Get($"{prefix}.online", context.Culture),
        };
    }
}
```

`src/RustPlusBot.Features.Commands/Handlers/SmallCommandHandler.cs`:

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!small — reports the small oil rig's status.</summary>
/// <param name="rigState">The rig state reader.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class SmallCommandHandler(IRigState rigState, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "small";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult<string?>(RigReply.For(rigState, context, RigKind.Small, "command.small", localizer));
    }
}
```

`src/RustPlusBot.Features.Commands/Handlers/LargeCommandHandler.cs`:

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!large — reports the large oil rig's status.</summary>
/// <param name="rigState">The rig state reader.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class LargeCommandHandler(IRigState rigState, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "large";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Task.FromResult<string?>(RigReply.For(rigState, context, RigKind.Large, "command.large", localizer));
    }
}
```

- [ ] **Step 5: Register the handlers**

In `CommandServiceCollectionExtensions.cs`, find where the other `ICommandHandler`s are registered (`AddScoped<ICommandHandler, …>`) and add:

```csharp
        services.AddScoped<ICommandHandler, SmallCommandHandler>();
        services.AddScoped<ICommandHandler, LargeCommandHandler>();
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter RigCommandHandlersTests`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers tests/RustPlusBot.Features.Commands.Tests/Handlers/RigCommandHandlersTests.cs src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs
git commit -m "feat(commands): add !small / !large rig-status handlers"
```

---

## Task 10: Commands — /small /large slash commands + registration count bump

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Modules/ServerCommandModule.cs`
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`

**Interfaces:**

- Consumes: the `small`/`large` handlers (registered Task 9), reused by name via `ServerQueryService.RunAsync` — the slash module needs no new dependency.

**Design note (refinement over the spec):** the spec floated reading `IRigState` directly in the module; the cleaner, fully-consistent path (matching the other 7 slash commands) is to route `/small`/`/large` through the existing `ServerQueryService` by the handler names `"small"`/`"large"`. The handlers read only `GuildId`/`ServerId`/`Culture` from the synthetic context (never SenderSteamId/Args), so the synthetic-context reuse is safe — exactly like `/pop` etc.

- [ ] **Step 1: Update the registration test (handler count 16 → 18 + new names)**

In `CommandRegistrationTests.cs`, change the assertion in BOTH test methods' shared count and add name pins. In `Dispatcher_and_handlers_resolve`:

```csharp
        Assert.Equal(18, handlers.Count);
```

and add:

```csharp
        Assert.Contains(handlers, h => h.Name == "small");
        Assert.Contains(handlers, h => h.Name == "large");
```

> The registration test now requires `IRigState` to resolve (the new handlers depend on it). The test's `services` already registers `IEventState` via a substitute; add `services.AddSingleton<IRigState>(_ => Substitute.For<IRigState>());` to BOTH test methods so `AddCommands` handlers can be constructed.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandRegistrationTests`
Expected: FAIL — count is 18 but only 16 registered until the `using` + DI is in place; OR a resolution error for `IRigState`. (Handlers were registered in Task 9, so the count should already be 18 once `IRigState` is provided — if Task 9 is merged, this mainly validates the count + adds the `IRigState` substitute.)

- [ ] **Step 3: Add the slash commands**

In `ServerCommandModule.cs`, add two methods following the exact `/pop` shape (note `RCS1141` requires the `<param>` doc):

```csharp
    /// <summary>Shows the small oil rig's status.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("small", "Show the small oil rig status")]
    public Task SmallAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("small", server);

    /// <summary>Shows the large oil rig's status.</summary>
    /// <param name="server">The target server (only needed if more than one is registered).</param>
    [SlashCommand("large", "Show the large oil rig status")]
    public Task LargeAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("large", server);
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandRegistrationTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Modules/ServerCommandModule.cs tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs
git commit -m "feat(commands): add /small and /large slash commands"
```

---

## Task 11: Full-suite verification, jb format, no-EF-drift, final review

**Files:** none (verification only).

- [ ] **Step 1: Full build**

Run: `dotnet build RustPlusBot.slnx`
Expected: BUILD SUCCEEDED, 0 warnings, 0 errors (strict analyzers).

- [ ] **Step 2: Full test suite — read per-assembly counts**

Run: `dotnet test RustPlusBot.slnx`
Expected: ALL green. Confirm per-assembly counts moved as expected: Abstractions +2, Connections +new marker tests, Events += (RigStateStore 8, RigRendering 4, tick 1, relay 3), Commands += (rig handlers 3) and registration count 18. If ANY assembly's total dropped, a fake/double wasn't updated — fix before proceeding (the 3a/3b lesson).

- [ ] **Step 3: Format gate**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Then: `git status` — review reordering; re-run the build/tests if anything changed.

- [ ] **Step 4: Confirm no EF model drift**

Run: `git diff --name-only develop... | grep -iE "Migrations|ModelSnapshot|DbContext|Configurations/" || echo "no EF files touched"`
Expected: "no EF files touched" (this slice adds no entity/migration). If the tooling allows, also run `dotnet ef migrations has-pending-model-changes` from the Host project; if it errors with "Unable to retrieve project metadata" (a known tooling quirk), rely on the file-diff check instead.

- [ ] **Step 5: Commit any format changes**

```bash
git add -A
git commit -m "style: jb cleanupcode reorder for 2a-ii"
```

- [ ] **Step 6: Final whole-branch review**

Dispatch a final review (per subagent-driven-development) covering: state-machine correctness (the three transitions + assume-Online), the in-game-broadcast generalization (all events, both surfaces independent), the adaptive cadence + rig-radius debounce, and that `/small`/`/large` reuse the handlers safely. Confirm the untested `RustPlusSocketSource.GetMonumentsAsync` shim is the only untested integration path.

---

## Self-Review (completed by plan author)

**Spec coverage:**

- State model (Online/Active/Offline, assume-Online, two timers) → Tasks 2, 5. ✓
- CH47-radius detection + adaptive cadence → Task 4. ✓
- Rig locations from monuments (`oilrig_1`/`large_oil_rig`) → Tasks 3, 4. ✓
- Generic Chinook event unchanged + fires alongside rig → Task 4 (guard test Step 5). ✓
- Three boundary alerts (Activated/CrateLootable/Respawned) → Abstractions Task 1, tick Task 8, relay Task 7. ✓
- #events embeds + in-game broadcast for ALL events 1:1 → Tasks 6, 7. ✓
- !small/!large → Task 9; /small//large → Task 10. ✓
- No entity/migration/persistence/project → enforced by Global Constraints + Task 11 Step 4. ✓
- EN/FR everywhere → Tasks 6, 9. ✓

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to". The one deliberate ambiguity (the test harness drive-loop in Task 4) points the implementer to the existing 2a supervisor-test pattern with a concrete approach — acceptable since that harness already exists and varies by exact helper names. The Task-4 Step-1 test has an intentional self-correcting note (the rig-position-vs-CH47-position assert) that the implementer must resolve before running — flagged explicitly.

**Type consistency:** `RigStateChangedEvent`/`RigKind`/`RigEventKind`/`RigStatus`/`RigState`/`RigCrossing`/`MonumentSnapshot`/`IRigState.Get`/`RigStateStore.Apply|Advance|Clear`/`EventEmbedRenderer.RenderRig|RenderRigLine|RenderLine`/`EventRelay.RelayRigAsync`/`EventsHostedService.TickOnceAsync` are used identically across tasks. `ConnectionOptions` member names match Task 2 throughout. Handler names `"small"`/`"large"` match Tasks 9–10.

```
