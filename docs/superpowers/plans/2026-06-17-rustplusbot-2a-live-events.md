# Subsystem 2a — Live Events Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect live Rust map events (Cargo Ship, Patrol Heli, Chinook, Locked Crate) by polling `GetMapMarkers`, alert them to a per-server `#events` Discord channel, and answer `!cargo`/`!heli`/`!chinook`/`!events` in-game commands — with no image rendering.

**Architecture:** The existing `ConnectionSupervisor` connected-loop gains a marker poll alongside its heartbeat: it fetches map dimensions once on connect, polls `GetMapMarkers` every `EventOptions.PollInterval`, diffs against the per-socket previous snapshot, and publishes a `MapMarkersChangedEvent` on the bus (first poll is a silent baseline). A new `RustPlusBot.Features.Events` project consumes that event: a pure `MarkerEventClassifier` maps deltas to domain events, a pure `GridReference` formats coordinates, an in-memory `EventStateStore` holds current markers + a recent-event ring, `EventRelay` posts one embed per event, and four `ICommandHandler`s in `Features.Commands` read the store via a public `IEventState` seam. The `#events` channel spec + a public `IEventChannelLocator` live in Workspace (mirroring `#teamchat`).

**Tech Stack:** .NET 10, C# 13, Discord.Net 3.20.x, RustPlusApi 2.0.0-beta.1, EF Core 9 / SQLite (no new tables), xUnit + NSubstitute, in-process `IEventBus`.

## Global Constraints

- Target framework `net10.0`; nullable enabled; strict analyzers (Roslynator + CA rules) treat warnings as errors.
- No `// TODO` comments (Roslynator `S1135` = error — use XML `<remarks>` instead).
- Interface implementations must repeat `= default` on `CancellationToken` parameters (`S1006`).
- Named arguments must follow parameter-declaration order (`RCS1205`); XML `<param>` docs required where the analyzer demands them (`RCS1141`).
- NSubstitute on an **internal** interface requires `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in that project's csproj.
- The real format gate is `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (ReSharper), enforced by the pre-push hook — NOT `dotnet format`. Run `dotnet tool restore` first.
- Per-guild culture is `"en"`/`"fr"`; all player-facing rendered copy (embeds, channel names, command replies) is localized EN/FR. Ephemeral/diagnostic strings may stay English.
- Background-loop service + DB-poll tests must use the shared-cache in-memory SQLite harness (per-scope own connection + a kept-open keep-alive), never a single shared `:memory:` connection.
- `docs/superpowers/` is gitignored and local-only — never commit or force-add specs/plans.
- New marker-poll socket calls must never put a token/secret in an exception or log message.
- Branch: `feat/live-events` off `develop`.
- After every task: run the FULL test suite and read per-assembly counts (a missing fake/double member silently drops a whole assembly — the 3a/3b lesson). Run `dotnet jb` before any push.

## Deviation from spec (locator placement)

The spec (§3) placed `IEventChannelLocator` + `DiscordEventChannelPoster` in `Features.Events`. During codebase study this was corrected: the channel **key**, **ChannelSpec**, and the public **`IEventChannelLocator`** live in **Workspace** (exactly where `WorkspaceChannelKeys.ServerTeamChat`, the `#teamchat` `ChannelSpec`, and the public `ITeamChatChannelLocator` live). `Features.Events` consumes the public locator interface — the same Chat↔Workspace relationship. The poster (a thin Discord wrapper) stays in `Features.Events`. Everything else in the spec stands.

## File structure

**New project `src/RustPlusBot.Features.Events/`** (csproj refs: Abstractions, Persistence, Discord, Features.Workspace, Features.Connections):

- `Classifying/MarkerEventClassifier.cs` — pure: `MapMarkersChangedEvent` → `IReadOnlyList<RustMapEvent>`.
- `Classifying/RustMapEvent.cs` — record `(MapEventKind Kind, float X, float Y, MapDimensions? Dimensions, DateTimeOffset AtUtc)`.
- `Classifying/MapEventKind.cs` — enum `CargoEntered, CargoLeft, HeliEntered, HeliLeft, ChinookSpawned, CrateSpawned`.
- `Formatting/GridReference.cs` — pure: `From(float x, float y, MapDimensions? dims)` → string.
- `State/IEventState.cs` — **public** read-seam consumed by Commands.
- `State/EventStateStore.cs` — singleton, implements `IEventState`; per-(guild,server) active markers + recent ring + `Apply`/`Clear`.
- `State/ActiveMarker.cs` — record `(ulong Id, MarkerKind Kind, float X, float Y, DateTimeOffset SeenAtUtc)`.
- `Relaying/EventRelay.cs` — bus consumer body: classify → `EventStateStore.Apply` → render → post.
- `Rendering/EventEmbedRenderer.cs` — pure: `RustMapEvent` → `Discord.Embed`, EN/FR.
- `Rendering/EventLocalizationCatalog.cs` — EN/FR strings for the feature.
- `Rendering/IEventLocalizer.cs` + `Rendering/EventLocalizer.cs` — culture lookup (copy Commands' shape).
- `Posting/IEventChannelPoster.cs` + `Posting/DiscordEventChannelPoster.cs` — posts an embed to a channel id.
- `Hosting/EventsHostedService.cs` — subscribes `MapMarkersChangedEvent`, drives `EventRelay`; also subscribes `ConnectionStatusChangedEvent`? No — see Task 8 for disconnect-clear wiring.
- `EventOptions.cs` — `PollInterval`.
- `EventServiceCollectionExtensions.cs` — `AddEvents()`.

**Abstractions (`src/RustPlusBot.Abstractions/`):**

- `Events/MapMarkersChangedEvent.cs` (in existing `Abstractions/Events/`).
- `Connections/MapMarkerSnapshot.cs`, `Connections/MarkerKind.cs`, `Connections/MapDimensions.cs` (namespace `RustPlusBot.Features.Connections.Listening`, matching where the other snapshots live — see Task 2).

**Connections (modify):**

- `Listening/IRustServerConnection.cs` — add `GetMapMarkersAsync` + `GetMapDimensionsAsync`.
- `Listening/IRustServerQuery.cs` (Abstractions) — add `GetMapMarkersAsync` + `GetMapDimensionsAsync` query methods (used by nothing in 2a directly, but keeps the public seam complete for 2b; **DEFERRED — see Task 3 note**, only the internal connection methods are needed for 2a).
- `Listening/RustPlusSocketSource.cs` — implement both on `RustPlusServerConnection` and `RejectedConnection`.
- `Supervisor/ConnectionSupervisor.cs` — fetch dims on connect, poll+diff in `RunConnectedAsync`, publish event.
- `ConnectionOptions.cs` — NO change (poll interval is `EventOptions`, injected into the supervisor — see Task 7 note).

**Commands (modify):**

- `Handlers/CargoCommandHandler.cs`, `HeliCommandHandler.cs`, `ChinookCommandHandler.cs`, `EventsCommandHandler.cs`.
- `Localization/CommandLocalizationCatalog.cs` — add EN/FR keys.
- `CommandServiceCollectionExtensions.cs` — register the four handlers.

**Workspace (modify):**

- `WorkspaceKeys.cs` — add `ServerEvents = "events"`.
- `Specs/ServerWorkspaceSpecProvider.cs` — add the `#events` `ChannelSpec`.
- `Localization/LocalizationCatalog.cs` — add `channel.events.name` EN/FR.
- `Locating/IEventChannelLocator.cs` + `Locating/EventChannelLocator.cs` — public locator (copy `TeamChatChannelLocator`).
- `WorkspaceServiceCollectionExtensions.cs` — register `IEventChannelLocator`.

**Test doubles (modify):**

- `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` — add the two new members.

**Host (modify):**

- `src/RustPlusBot.Host/Program.cs` — `AddOptions<EventOptions>()...ValidateOnStart()` + `AddEvents()`.

---

### Task 1: Abstractions — marker snapshot, dimensions, marker kind, bus event

**Files:**

- Create: `src/RustPlusBot.Abstractions/Connections/MarkerKind.cs`
- Create: `src/RustPlusBot.Abstractions/Connections/MapMarkerSnapshot.cs`
- Create: `src/RustPlusBot.Abstractions/Connections/MapDimensions.cs`
- Create: `src/RustPlusBot.Abstractions/Events/MapMarkersChangedEvent.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/Connections/MapMarkerSnapshotTests.cs`

**Interfaces:**

- Produces: `MarkerKind` enum; `MapMarkerSnapshot(ulong Id, MarkerKind Kind, float X, float Y, string? Name)`; `MapDimensions(uint Width, uint Height, int OceanMargin)`; `MapMarkersChangedEvent(ulong GuildId, Guid ServerId, MapDimensions? Dimensions, IReadOnlyList<MapMarkerSnapshot> Added, IReadOnlyList<MapMarkerSnapshot> Removed)`.

> **Namespace note:** the other snapshots (`ServerInfoSnapshot`, etc.) physically live in `Abstractions/Connections/` but declare `namespace RustPlusBot.Features.Connections.Listening` (a deliberate 3c carry-over so ~31 consumers compiled unchanged). Match that: these three types ALSO declare `namespace RustPlusBot.Features.Connections.Listening`. The bus event declares `namespace RustPlusBot.Abstractions.Events` (matching its siblings like `TeamMessageReceivedEvent`).

> **`ServerId` type:** the bus events in this repo use `Guid ServerId` (see `TeamMessageReceivedEvent`/`ConnectionStatusChangedEvent`), NOT `int`. The spec said `int ServerId` — that was wrong; use `Guid`.

- [ ] **Step 1: Confirm the Abstractions.Tests project exists and how events look**

Run: `ls tests/RustPlusBot.Abstractions.Tests/ && sed -n '1,40p' src/RustPlusBot.Abstractions/Events/TeamMessageReceivedEvent.cs`
Expected: the test project exists (3c added it); the event is a `public sealed record` in `namespace RustPlusBot.Abstractions.Events`.

- [ ] **Step 2: Write the failing test**

Create `tests/RustPlusBot.Abstractions.Tests/Connections/MapMarkerSnapshotTests.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Abstractions.Tests.Connections;

public sealed class MapMarkerSnapshotTests
{
    [Fact]
    public void Marker_carries_kind_and_coordinates()
    {
        var marker = new MapMarkerSnapshot(42UL, MarkerKind.CargoShip, 1234f, 5678f, "Cargo");

        Assert.Equal(42UL, marker.Id);
        Assert.Equal(MarkerKind.CargoShip, marker.Kind);
        Assert.Equal(1234f, marker.X);
        Assert.Equal(5678f, marker.Y);
        Assert.Equal("Cargo", marker.Name);
    }

    [Fact]
    public void Dimensions_carry_size_and_margin()
    {
        var dims = new MapDimensions(4000u, 4000u, 500);

        Assert.Equal(4000u, dims.Width);
        Assert.Equal(500, dims.OceanMargin);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter MapMarkerSnapshotTests`
Expected: FAIL — `MapMarkerSnapshot`/`MapDimensions`/`MarkerKind` do not exist (compile error).

- [ ] **Step 4: Create the types**

`src/RustPlusBot.Abstractions/Connections/MarkerKind.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>The subset of Rust map-marker types this bot reasons about; everything else is <see cref="Other"/>.</summary>
public enum MarkerKind
{
    /// <summary>A marker type the bot does not classify (player, vending, explosion, generic radius, vendor).</summary>
    Other = 0,

    /// <summary>A cargo ship.</summary>
    CargoShip = 1,

    /// <summary>A patrol helicopter.</summary>
    PatrolHelicopter = 2,

    /// <summary>A Chinook (CH-47).</summary>
    Chinook = 3,

    /// <summary>A locked crate.</summary>
    Crate = 4,
}
```

`src/RustPlusBot.Abstractions/Connections/MapMarkerSnapshot.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One map marker observed in a <c>GetMapMarkers</c> poll.</summary>
/// <param name="Id">The stable marker id (used to diff polls).</param>
/// <param name="Kind">The classified marker kind.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Name">The marker name, if any.</param>
public sealed record MapMarkerSnapshot(ulong Id, MarkerKind Kind, float X, float Y, string? Name);
```

`src/RustPlusBot.Abstractions/Connections/MapDimensions.cs`:

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>The static-per-wipe map size needed to convert world coordinates to a grid reference.</summary>
/// <param name="Width">Map image width.</param>
/// <param name="Height">Map image height.</param>
/// <param name="OceanMargin">The ocean margin around the playable area.</param>
public sealed record MapDimensions(uint Width, uint Height, int OceanMargin);
```

`src/RustPlusBot.Abstractions/Events/MapMarkersChangedEvent.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a marker poll detects markers that appeared or disappeared since the previous poll.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="Dimensions">Map dimensions for grid-reference rendering, or null if unavailable.</param>
/// <param name="Added">Markers present now that were absent in the previous poll.</param>
/// <param name="Removed">Markers absent now that were present in the previous poll.</param>
public sealed record MapMarkersChangedEvent(
    ulong GuildId,
    Guid ServerId,
    MapDimensions? Dimensions,
    IReadOnlyList<MapMarkerSnapshot> Added,
    IReadOnlyList<MapMarkerSnapshot> Removed);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter MapMarkerSnapshotTests`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Abstractions tests/RustPlusBot.Abstractions.Tests
git commit -m "feat(2a): add map-marker snapshot, dimensions, and MapMarkersChangedEvent"
```

---

### Task 2: Connections seam — GetMapMarkers + GetMapDimensions on the connection

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` (both `RejectedConnection` and `RustPlusServerConnection`)
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` is the double; assert via the supervisor test in Task 6.

**Interfaces:**

- Consumes: `MapMarkerSnapshot`, `MapDimensions`, `MarkerKind` from Task 1.
- Produces (on internal `IRustServerConnection`):
  - `Task<IReadOnlyList<MapMarkerSnapshot>> GetMapMarkersAsync(TimeSpan timeout, CancellationToken cancellationToken = default)` — returns all markers (every kind, `Other` for unclassified) so the diff tracks by id; empty list on failure.
  - `Task<MapDimensions?> GetMapDimensionsAsync(TimeSpan timeout, CancellationToken cancellationToken = default)` — null on failure.
- Produces (on `FakeConnection`): settable `MarkersResult` (default empty) and `DimensionsResult` (default `new(4000u,4000u,500)`), plus the two method impls returning them.

> **Why empty-list (not null) for markers:** an empty poll is a legitimate "nothing on the map" state that must diff correctly (everything removed). A failure is also surfaced as empty here, and Task 6's supervisor logic retains the previous snapshot on a thrown/failed poll — so the source returns empty on failure and the supervisor distinguishes "successful empty" from "failed" by catching the exception. To make that distinction clean, **`GetMapMarkersAsync` THROWS on failure** (like the connect path) and returns the marker list on success; the supervisor catches. `GetMapDimensionsAsync` returns null on failure (non-critical).

- [ ] **Step 1: Add the two methods to the internal interface**

In `IRustServerConnection.cs`, after `PromoteToLeaderAsync` (before the `event` line), add:

```csharp
    /// <summary>Polls the current map markers (all kinds; the caller diffs by id). Throws on failure.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The current markers (every kind, unclassified ones as <see cref="MarkerKind.Other"/>).</returns>
    Task<IReadOnlyList<MapMarkerSnapshot>> GetMapMarkersAsync(TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>Gets the static map dimensions for grid-reference rendering, or null on failure/timeout.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The map dimensions, or null on failure/timeout.</returns>
    Task<MapDimensions?> GetMapDimensionsAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
```

- [ ] **Step 2: Implement on `RejectedConnection`**

In `RustPlusSocketSource.cs`, inside `RejectedConnection` (after `PromoteToLeaderAsync`), add:

```csharp
        public Task<IReadOnlyList<MapMarkerSnapshot>> GetMapMarkersAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<MapMarkerSnapshot>>([]);

        public Task<MapDimensions?> GetMapDimensionsAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult<MapDimensions?>(null);
```

- [ ] **Step 3: Implement on `RustPlusServerConnection`**

> **CORRECTED (verified by reflection):** the facade returns the MAPPED DTO `RustPlusApi.Data.MapMarkers`, NOT the raw `RustPlusContracts.AppMapMarkers`. `MapMarkers` has **typed per-category dictionaries** `CargoShipMarkers` / `PatrolHelicopterMarkers` / `Ch47Markers` (each `Dictionary<ulong, XMarker>`), NOT a flat `Markers` list and NO `Type` field. Each marker (all derive from `RustPlusApi.Data.Markers.Marker`) exposes only `Nullable<ulong> Id`, `Nullable<float> X`, `Nullable<float> Y`. **There is NO crate bucket — the game no longer sends crate markers, so 2a is CORE-3 (cargo/heli/chinook).** `Name` is not available on the mapped marker → always pass `null`. `ServerMap.Width/Height/OceanMargin` are `Nullable<uint>`/`Nullable<int>`.

In `RustPlusSocketSource.cs`, inside `RustPlusServerConnection` (after `PromoteToLeaderAsync`), add (no `RustPlusContracts` using needed — everything is `RustPlusApi.Data.*`):

```csharp
        public async Task<IReadOnlyList<MapMarkerSnapshot>> GetMapMarkersAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            // CONFIRMED (2.0.0-beta.1): GetMapMarkersAsync returns Task<Response<RustPlusApi.Data.MapMarkers>>.
            // MapMarkers has typed dictionaries CargoShipMarkers/PatrolHelicopterMarkers/Ch47Markers
            // (Dictionary<ulong, XMarker>); each marker exposes Nullable<ulong> Id, Nullable<float> X/Y.
            // No flat list, no Type field, NO crate bucket (the game stopped sending crate markers) → core-3.
            var response = await _rustPlus.GetMapMarkersAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccess || response.Data is null)
            {
                throw new InvalidOperationException("GetMapMarkers returned no data.");
            }

            var data = response.Data;
            var markers = new List<MapMarkerSnapshot>();
            AddMarkers(markers, data.CargoShipMarkers, MarkerKind.CargoShip);
            AddMarkers(markers, data.PatrolHelicopterMarkers, MarkerKind.PatrolHelicopter);
            AddMarkers(markers, data.Ch47Markers, MarkerKind.Chinook);
            return markers;
        }

        private static void AddMarkers<TMarker>(
            List<MapMarkerSnapshot> into,
            IReadOnlyDictionary<ulong, TMarker> source,
            MarkerKind kind)
            where TMarker : RustPlusApi.Data.Markers.Marker
        {
            foreach (var (id, marker) in source)
            {
                into.Add(new MapMarkerSnapshot(id, kind, marker.X ?? 0f, marker.Y ?? 0f, Name: null));
            }
        }

        public async Task<MapDimensions?> GetMapDimensionsAsync(
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.1): GetMapAsync returns Task<Response<RustPlusApi.Data.ServerMap>>;
                // ServerMap { Nullable<uint> Width/Height, Nullable<int> OceanMargin, JpgImage, Monuments }.
                // 2a uses dims only; if any dim is null, treat the whole thing as unavailable (return null).
                var response = await _rustPlus.GetMapAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token)
                    .ConfigureAwait(false);
                if (!response.IsSuccess || response.Data is null)
                {
                    return null;
                }

                var map = response.Data;
                if (map.Width is not { } width || map.Height is not { } height || map.OceanMargin is not { } margin)
                {
                    return null;
                }

                return new MapDimensions(width, height, margin);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
#pragma warning disable CA1031 // Broad catch: any map-query failure maps to null; never surface a token/secret.
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
                LogQueryFailed(_logger, ex);
                return null;
            }
        }
```

> Verify the dictionary property type exposes `IReadOnlyDictionary` or `Dictionary` — the reflection showed `Dictionary<ulong, XMarker>`; if the generic helper's `IReadOnlyDictionary<ulong, TMarker>` parameter doesn't bind, change it to `Dictionary<ulong, TMarker>`. Verify `RustPlusApi.Data.Markers.Marker` is the public base type with `X`/`Y` (it is, per reflection).

- [ ] **Step 4: Add the two members to `FakeConnection`**

In `FakeRustSocketSource.cs`, inside `FakeConnection`, add the settable results near the other `*Result` properties:

```csharp
        /// <summary>The markers returned by <see cref="GetMapMarkersAsync"/>. Defaults to empty (nothing on the map).</summary>
        public IReadOnlyList<MapMarkerSnapshot> MarkersResult { get; set; } = [];

        /// <summary>When true, <see cref="GetMapMarkersAsync"/> throws (simulates a failed poll).</summary>
        public bool MarkersThrow { get; set; }

        /// <summary>The dimensions returned by <see cref="GetMapDimensionsAsync"/>. Defaults to a non-null snapshot.</summary>
        public MapDimensions? DimensionsResult { get; set; } = new(4000u, 4000u, 500);
```

and the method impls near the other `Get*` methods:

```csharp
        public Task<IReadOnlyList<MapMarkerSnapshot>> GetMapMarkersAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            MarkersThrow
                ? Task.FromException<IReadOnlyList<MapMarkerSnapshot>>(new InvalidOperationException("poll failed"))
                : Task.FromResult(MarkersResult);

        public Task<MapDimensions?> GetMapDimensionsAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
            Task.FromResult(DimensionsResult);
```

- [ ] **Step 5: Build (no behavior change yet) to verify the seam compiles**

Run: `dotnet build src/RustPlusBot.Features.Connections && dotnet build tests/RustPlusBot.Features.Connections.Tests`
Expected: build succeeds 0 warnings/0 errors. All marker types are `RustPlusApi.Data.*` (the mapped facade) — no `RustPlusContracts` reference. If the typed-dictionary property names differ from `CargoShipMarkers`/`PatrolHelicopterMarkers`/`Ch47Markers`, correct them (reflection confirmed these exact names on `RustPlusApi.Data.MapMarkers`).

- [ ] **Step 6: Run full suite (ensure no assembly silently dropped)**

Run: `dotnet test`
Expected: all prior tests still green; per-assembly counts unchanged from baseline + Task 1's 2 new Abstractions tests.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(2a): add GetMapMarkers/GetMapDimensions to the connection seam + fake"
```

---

### Task 3: Supervisor — fetch dims on connect, poll+diff markers, publish event

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Create: `src/RustPlusBot.Features.Connections/EventPollOptions.cs` (see note) — OR inject `EventOptions`. **Decision below.**
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs` (add cases)

**Interfaces:**

- Consumes: `IRustServerConnection.GetMapMarkersAsync`/`GetMapDimensionsAsync` (Task 2); `IEventBus.PublishAsync`; `MapMarkersChangedEvent` (Task 1).
- Produces: the supervisor now publishes `MapMarkersChangedEvent` on the bus during the connected window.

> **Options ownership decision:** `EventOptions` (with `PollInterval`) lives in the NEW `Features.Events` project (Task 7). But the supervisor is in `Connections`, which must NOT reference `Features.Events` (wrong dependency direction — Events references Connections). Resolution: the supervisor reads the poll interval from its OWN `ConnectionOptions` — **add `MarkerPollInterval` to `ConnectionOptions`** (default 10s). `EventOptions` in Task 7 then holds only Events-side knobs (none needed in 2a beyond the interval, so Task 7's `EventOptions` is dropped — the interval lives on `ConnectionOptions`). This keeps the dependency arrow correct and matches how `HeartbeatInterval` already lives on `ConnectionOptions`.

> This supersedes the spec's "EventOptions.PollInterval" — the interval is `ConnectionOptions.MarkerPollInterval`. Note it in the status memory.

- [ ] **Step 1: Add `MarkerPollInterval` to `ConnectionOptions`**

Read `src/RustPlusBot.Features.Connections/ConnectionOptions.cs`, then add a property mirroring `HeartbeatInterval`:

```csharp
    /// <summary>How often to poll map markers for live-event detection. Default 10s.</summary>
    public TimeSpan MarkerPollInterval { get; init; } = TimeSpan.FromSeconds(10);
```

- [ ] **Step 2: Write the failing supervisor tests**

In `ConnectionSupervisorTests.cs`, add (mirror the existing connected-window test setup — find an existing test that drives `EnsureConnectionAsync` with the shared-cache SQLite harness and an `IEventBus` substitute, and copy its arrangement). Add three tests:

```csharp
[Fact]
public async Task First_marker_poll_is_a_silent_baseline()
{
    // Arrange: fake connection returns one CargoShip marker; connect succeeds; heartbeat Ok.
    // (Reuse the harness helper that builds the supervisor with a captured IEventBus substitute.)
    var bus = Substitute.For<IEventBus>();
    var source = new FakeRustSocketSource();
    // ... build supervisor (see existing tests) with bus + source ...
    source /* next connection */; // configure LastConnection.MarkersResult after Create — see existing pattern

    // Act: start the connection, let it run one poll cycle.
    // Assert: NO MapMarkersChangedEvent was published on the first poll.
    await bus.DidNotReceive().PublishAsync(Arg.Any<MapMarkersChangedEvent>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task Marker_added_on_a_later_poll_publishes_changed_event()
{
    // Arrange: first poll empty (baseline), second poll has a CargoShip.
    // Act: run two poll cycles.
    // Assert: exactly one MapMarkersChangedEvent with Added containing the CargoShip, Removed empty.
    await bus.Received().PublishAsync(
        Arg.Is<MapMarkersChangedEvent>(e => e.Added.Count == 1 && e.Added[0].Kind == MarkerKind.CargoShip),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task Failed_marker_poll_retains_previous_snapshot()
{
    // Arrange: baseline has a CargoShip; next poll throws (MarkersThrow=true); the poll after returns the same CargoShip.
    // Assert: the throw produced NO event and did NOT cause a spurious "removed then added" on recovery.
}
```

> The exact harness wiring (shared-cache SQLite, seeded RustServer + active credential so `PrepareAsync` returns non-null, the `IEventBus` substitute, the poll-cycle pump) must be copied from the existing connected-window tests in this file. Because the supervisor's poll cadence is `MarkerPollInterval`, set it to a small value (e.g. `TimeSpan.FromMilliseconds(50)`) in the test options and use the file's existing "wait until condition" helper rather than a fixed sleep. If the existing tests use a 30s CTS for background-loop stability, match that.

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionSupervisorTests`
Expected: the three new tests FAIL (no marker poll exists yet).

- [ ] **Step 4: Implement the poll in `RunConnectedAsync`**

Modify `RunConnectedAsync` in `ConnectionSupervisor.cs`. After the first successful `GetInfoAsync` and `PublishStatusAsync(Connected...)`, before the heartbeat loop:

1. Fetch dims once: `var dims = await connection.GetMapDimensionsAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);`
2. Maintain a local `IReadOnlyList<MapMarkerSnapshot>? previousMarkers = null;` (null = no baseline yet).
3. The connected loop currently delays `HeartbeatInterval` then beats. Add a marker poll on its own cadence. **Simplest correct approach given one loop:** poll markers every heartbeat tick IF `MarkerPollInterval <= HeartbeatInterval`, else track elapsed time. To avoid a second timer, drive the marker poll from the SAME loop iteration but gate it by elapsed wall-clock using the injected clock-free approach already in the file (the loop already `Task.Delay(HeartbeatInterval)`s). **Chosen design:** run a SEPARATE marker-poll task for the connected window, started before the heartbeat loop and cancelled in the `finally`. This keeps the heartbeat cadence independent of the poll cadence:

```csharp
        connection.TeamMessageReceived += OnTeamMessage;
        _liveSockets[key] = new LiveSocket(connection, activeSteamId);
        using var pollCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var markerPoll = Task.Run(() => PollMarkersAsync(key, connection, dims, pollCts.Token), CancellationToken.None);
        try
        {
            // ... existing heartbeat while-loop unchanged ...
        }
        finally
        {
            await pollCts.CancelAsync().ConfigureAwait(false);
            try { await markerPoll.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected on stop */ }
            _liveSockets.TryRemove(key, out _);
            connection.TeamMessageReceived -= OnTeamMessage;
        }
```

Add the poll method:

```csharp
    private async Task PollMarkersAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        MapDimensions? dims,
        CancellationToken ct)
    {
        IReadOnlyList<MapMarkerSnapshot>? previous = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var current = await connection.GetMapMarkersAsync(_options.HeartbeatTimeout, ct).ConfigureAwait(false);
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
                // previous is retained, so a transient failure does not produce a spurious despawn/respawn diff.
            }

            await Task.Delay(_options.MarkerPollInterval, ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Marker poll for server {ServerId} failed.")]
    private static partial void LogMarkerPollFailed(ILogger logger, Exception exception, Guid serverId);
```

> Note: `Task.Delay` AFTER the work so the first poll fires immediately on connect (establishing the baseline promptly). The `OperationCanceledException` from the trailing `Task.Delay` is caught by the surrounding `catch (OperationCanceledException) => return` only if inside the try; since the delay is outside the try, wrap the whole `while` body OR move the delay-cancellation handling: simplest is to let the delay throw and have the method's caller-join swallow it — but cleaner is to also catch it. Implement so a cancel during the delay exits cleanly (the outer `try/await markerPoll` in `RunConnectedAsync` swallows `OperationCanceledException`).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ConnectionSupervisorTests`
Expected: PASS including the three new tests.

- [ ] **Step 6: Add the `MarkerPollInterval` validation to be safe (optional, only if other ConnectionOptions are validated in Program.cs)**

No code change in this task for Program.cs; Task 9 adds the validation line. Skip here.

- [ ] **Step 7: Run full suite + jb**

Run: `dotnet test && dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Expected: all green; jb reorders only this branch's files (review `git diff` after jb).

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(2a): poll map markers in the connected window and publish diffs"
```

---

### Task 4: Features.Events project scaffold + GridReference (pure)

**Files:**

- Create: `src/RustPlusBot.Features.Events/RustPlusBot.Features.Events.csproj`
- Create: `src/RustPlusBot.Features.Events/Formatting/GridReference.cs`
- Create: `tests/RustPlusBot.Features.Events.Tests/RustPlusBot.Features.Events.Tests.csproj`
- Test: `tests/RustPlusBot.Features.Events.Tests/Formatting/GridReferenceTests.cs`

**Interfaces:**

- Consumes: `MapDimensions` (Task 1).
- Produces: `static string GridReference.From(float x, float y, MapDimensions? dims)`.

> **Grid math (rustplusplus-compatible):** Rust map grids are lettered columns (A, B, …, then AA, AB… past Z) and numbered rows from the top. The playable area excludes the ocean margin on each side. Cell size is fixed at **146.25** world units (rustplusplus's `gridDiameter`). Column index = `floor((x - oceanMargin?) ...)` — but rustplusplus computes it as: `mapSize = width` (the JpgImage is square-ish; use `Width`), and column = `floor(x / gridDiameter)`, row = `numberOfRows - floor(y / gridDiameter) - 1` where `numberOfRows = ceil(mapSize / gridDiameter)`. **For 2a, replicate rustplusplus's exact formula** (verified known-good): see Step 4. The `OceanMargin` is NOT subtracted in rustplusplus's grid calc (markers are already in playable coords); keep `dims` only for `Width`. If `dims is null`, return `$"({x:0},{y:0})"`.

- [ ] **Step 1: Create the project files**

`src/RustPlusBot.Features.Events/RustPlusBot.Features.Events.csproj` (copy `Features.Chat`'s csproj as the template — same TFM/analyzers/InternalsVisibleTo block), with project references:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="RustPlusBot.Features.Events.Tests" />
  <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
</ItemGroup>
<ItemGroup>
  <ProjectReference Include="..\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
  <ProjectReference Include="..\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
  <ProjectReference Include="..\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
  <ProjectReference Include="..\RustPlusBot.Features.Workspace\RustPlusBot.Features.Workspace.csproj" />
  <ProjectReference Include="..\RustPlusBot.Features.Connections\RustPlusBot.Features.Connections.csproj" />
</ItemGroup>
```

Test csproj: copy `tests/RustPlusBot.Features.Chat.Tests`'s csproj; reference the new Events project (and Connections for the snapshot types).

Add both projects to the solution: `dotnet sln RustPlusBot.slnx add src/RustPlusBot.Features.Events tests/RustPlusBot.Features.Events.Tests`.

- [ ] **Step 2: Write the failing test**

`tests/RustPlusBot.Features.Events.Tests/Formatting/GridReferenceTests.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Formatting;

namespace RustPlusBot.Features.Events.Tests.Formatting;

public sealed class GridReferenceTests
{
    [Fact]
    public void Origin_is_top_left_cell()
    {
        // x near 0, y near top => column A, top row.
        var dims = new MapDimensions(4000u, 4000u, 500);
        var grid = GridReference.From(10f, 3990f, dims);

        Assert.StartsWith("A", grid, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_dimensions_fall_back_to_raw_coordinates()
    {
        var grid = GridReference.From(1234.6f, 5678.4f, dims: null);

        Assert.Equal("(1235, 5678)", grid);
    }

    [Theory]
    [InlineData(0f, 4000f, "A")]
    [InlineData(150f, 4000f, "B")]
    public void Column_letter_advances_every_cell(float x, float y, string expectedColumnPrefix)
    {
        var dims = new MapDimensions(4000u, 4000u, 500);
        var grid = GridReference.From(x, y, dims);
        Assert.StartsWith(expectedColumnPrefix, grid, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter GridReferenceTests`
Expected: FAIL — `GridReference` does not exist.

- [ ] **Step 4: Implement `GridReference`**

`src/RustPlusBot.Features.Events/Formatting/GridReference.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Events.Formatting;

/// <summary>Converts world coordinates to a Rust map grid reference (e.g. "D7"), rustplusplus-compatible.</summary>
public static class GridReference
{
    private const float GridDiameter = 146.25f;

    /// <summary>Formats a grid reference, or raw rounded coordinates when <paramref name="dims"/> is null.</summary>
    /// <param name="x">World X coordinate.</param>
    /// <param name="y">World Y coordinate.</param>
    /// <param name="dims">Map dimensions, or null when unavailable.</param>
    /// <returns>A grid reference like "D7", or "(x, y)" when dimensions are unavailable.</returns>
    public static string From(float x, float y, MapDimensions? dims)
    {
        if (dims is null)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"({Math.Round(x)}, {Math.Round(y)})");
        }

        var mapSize = dims.Width;
        var columns = (int)Math.Ceiling(mapSize / GridDiameter);
        var col = (int)Math.Floor(Math.Clamp(x, 0f, mapSize - 1) / GridDiameter);
        // Rows are numbered from the TOP; world Y increases upward, so invert.
        var rowFromBottom = (int)Math.Floor(Math.Clamp(y, 0f, mapSize - 1) / GridDiameter);
        var row = Math.Max(0, columns - rowFromBottom - 1);

        return string.Create(CultureInfo.InvariantCulture, $"{ColumnLetters(col)}{row}");
    }

    private static string ColumnLetters(int index)
    {
        // 0->A .. 25->Z, 26->AA .. (spreadsheet-style, matching rustplusplus past Z).
        var letters = string.Empty;
        var n = index;
        do
        {
            letters = (char)('A' + (n % 26)) + letters;
            n = (n / 26) - 1;
        }
        while (n >= 0);

        return letters;
    }
}
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter GridReferenceTests`
Expected: PASS. If the column/row expectations are off, adjust the test's expected values to the formula's output (the formula is the source of truth; the spec only requires *a* deterministic grid ref) — but keep the null-fallback assertion exact.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Events tests/RustPlusBot.Features.Events.Tests RustPlusBot.slnx
git commit -m "feat(2a): scaffold Features.Events project + GridReference helper"
```

---

### Task 5: MarkerEventClassifier + RustMapEvent + MapEventKind (pure)

**Files:**

- Create: `src/RustPlusBot.Features.Events/Classifying/MapEventKind.cs`
- Create: `src/RustPlusBot.Features.Events/Classifying/RustMapEvent.cs`
- Create: `src/RustPlusBot.Features.Events/Classifying/MarkerEventClassifier.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/Classifying/MarkerEventClassifierTests.cs`

**Interfaces:**

- Consumes: `MapMarkersChangedEvent`, `MapMarkerSnapshot`, `MarkerKind`, `MapDimensions` (Task 1); `IClock` (`RustPlusBot.Abstractions.Time`) for the event timestamp.
- Produces:
  - `enum MapEventKind { CargoEntered, CargoLeft, HeliEntered, HeliLeft, ChinookSpawned }`.
  - `sealed record RustMapEvent(MapEventKind Kind, float X, float Y, MapDimensions? Dimensions, DateTimeOffset AtUtc)`.
  - `sealed class MarkerEventClassifier(IClock clock)` with `IReadOnlyList<RustMapEvent> Classify(MapMarkersChangedEvent evt)`.

> **CORE-3 (crate dropped):** the game no longer sends crate markers, so there is NO `CrateSpawned` event. `MarkerKind.Crate` remains a (currently unused) enum member for forward-compat, but the classifier maps it to nothing (falls in the `_ => null` bucket).
>
> **Classification rules:** CargoShip added→`CargoEntered`, removed→`CargoLeft`; PatrolHelicopter added→`HeliEntered`, removed→`HeliLeft`; Ch47/Chinook added→`ChinookSpawned` (removal → nothing); everything else (`Crate`, `Other`) → nothing. Every produced event carries `evt.Dimensions` and `clock.UtcNow`.

- [ ] **Step 1: Write the failing test**

`tests/RustPlusBot.Features.Events.Tests/Classifying/MarkerEventClassifierTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;

namespace RustPlusBot.Features.Events.Tests.Classifying;

public sealed class MarkerEventClassifierTests
{
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private static MarkerEventClassifier Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new MarkerEventClassifier(clock);
    }

    private static MapMarkersChangedEvent Evt(
        IReadOnlyList<MapMarkerSnapshot> added,
        IReadOnlyList<MapMarkerSnapshot> removed) =>
        new(1UL, Server, new MapDimensions(4000u, 4000u, 500), added, removed);

    [Fact]
    public void Cargo_added_is_CargoEntered()
    {
        var result = Build().Classify(Evt(
            [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 10f, 20f, null)], []));

        var e = Assert.Single(result);
        Assert.Equal(MapEventKind.CargoEntered, e.Kind);
        Assert.Equal(Now, e.AtUtc);
        Assert.NotNull(e.Dimensions);
    }

    [Fact]
    public void Cargo_removed_is_CargoLeft()
    {
        var result = Build().Classify(Evt([],
            [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 10f, 20f, null)]));
        Assert.Equal(MapEventKind.CargoLeft, Assert.Single(result).Kind);
    }

    [Fact]
    public void Heli_added_and_removed_map_to_entered_and_left()
    {
        Assert.Equal(MapEventKind.HeliEntered, Assert.Single(Build().Classify(
            Evt([new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 0f, 0f, null)], []))).Kind);
        Assert.Equal(MapEventKind.HeliLeft, Assert.Single(Build().Classify(
            Evt([], [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 0f, 0f, null)]))).Kind);
    }

    [Fact]
    public void Chinook_added_is_spawned_but_removal_is_silent()
    {
        Assert.Equal(MapEventKind.ChinookSpawned, Assert.Single(Build().Classify(
            Evt([new MapMarkerSnapshot(3, MarkerKind.Chinook, 0f, 0f, null)], []))).Kind);
        Assert.Empty(Build().Classify(
            Evt([], [new MapMarkerSnapshot(3, MarkerKind.Chinook, 0f, 0f, null)])));
    }

    [Fact]
    public void Crate_and_other_markers_produce_nothing()
    {
        // The game no longer sends crate markers; MarkerKind.Crate (and Other) classify to nothing.
        Assert.Empty(Build().Classify(Evt(
            [new MapMarkerSnapshot(4, MarkerKind.Crate, 0f, 0f, null),
             new MapMarkerSnapshot(5, MarkerKind.Other, 0f, 0f, null)],
            [new MapMarkerSnapshot(6, MarkerKind.Crate, 0f, 0f, null),
             new MapMarkerSnapshot(7, MarkerKind.Other, 0f, 0f, null)])));
    }

    [Fact]
    public void Multiple_deltas_produce_multiple_events()
    {
        var result = Build().Classify(Evt(
            [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null),
             new MapMarkerSnapshot(3, MarkerKind.Chinook, 0f, 0f, null)],
            [new MapMarkerSnapshot(2, MarkerKind.PatrolHelicopter, 0f, 0f, null)]));
        Assert.Equal(3, result.Count);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter MarkerEventClassifierTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement the types**

`MapEventKind.cs`:

```csharp
namespace RustPlusBot.Features.Events.Classifying;

/// <summary>A classified live map event.</summary>
public enum MapEventKind
{
    /// <summary>A cargo ship entered the map.</summary>
    CargoEntered,

    /// <summary>A cargo ship left the map.</summary>
    CargoLeft,

    /// <summary>A patrol helicopter entered the map.</summary>
    HeliEntered,

    /// <summary>A patrol helicopter left the map.</summary>
    HeliLeft,

    /// <summary>A Chinook spawned.</summary>
    ChinookSpawned,
}
```

`RustMapEvent.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Events.Classifying;

/// <summary>A classified live map event with its location and time.</summary>
/// <param name="Kind">The event kind.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Dimensions">Map dimensions for grid rendering, or null.</param>
/// <param name="AtUtc">When the event was observed.</param>
public sealed record RustMapEvent(MapEventKind Kind, float X, float Y, MapDimensions? Dimensions, DateTimeOffset AtUtc);
```

`MarkerEventClassifier.cs`:

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Events.Classifying;

/// <summary>Maps raw marker add/remove deltas to domain map events.</summary>
/// <param name="clock">Stamps each produced event.</param>
internal sealed class MarkerEventClassifier(IClock clock)
{
    /// <summary>Classifies one marker-change delta into zero or more domain events.</summary>
    /// <param name="evt">The marker-change delta.</param>
    /// <returns>The classified events, in delta order (added first, then removed).</returns>
    public IReadOnlyList<RustMapEvent> Classify(MapMarkersChangedEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var now = clock.UtcNow;
        var events = new List<RustMapEvent>();

        foreach (var m in evt.Added)
        {
            MapEventKind? kind = m.Kind switch
            {
                MarkerKind.CargoShip => MapEventKind.CargoEntered,
                MarkerKind.PatrolHelicopter => MapEventKind.HeliEntered,
                MarkerKind.Chinook => MapEventKind.ChinookSpawned,
                _ => null, // Crate/Other → no event (the game no longer sends crate markers).
            };
            if (kind is { } k)
            {
                events.Add(new RustMapEvent(k, m.X, m.Y, evt.Dimensions, now));
            }
        }

        foreach (var m in evt.Removed)
        {
            MapEventKind? kind = m.Kind switch
            {
                MarkerKind.CargoShip => MapEventKind.CargoLeft,
                MarkerKind.PatrolHelicopter => MapEventKind.HeliLeft,
                _ => null, // Chinook/Crate removal is silent.
            };
            if (kind is { } k)
            {
                events.Add(new RustMapEvent(k, m.X, m.Y, evt.Dimensions, now));
            }
        }

        return events;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter MarkerEventClassifierTests`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Events tests/RustPlusBot.Features.Events.Tests
git commit -m "feat(2a): add MarkerEventClassifier mapping deltas to domain events"
```

---

### Task 6: EventStateStore + IEventState (in-memory)

**Files:**

- Create: `src/RustPlusBot.Features.Events/State/ActiveMarker.cs`
- Create: `src/RustPlusBot.Features.Events/State/IEventState.cs` (**public**)
- Create: `src/RustPlusBot.Features.Events/State/EventStateStore.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/State/EventStateStoreTests.cs`

**Interfaces:**

- Consumes: `MapMarkersChangedEvent`, `MapMarkerSnapshot`, `MarkerKind` (Task 1); `RustMapEvent` (Task 5).
- Produces:
  - `public sealed record ActiveMarker(ulong Id, MarkerKind Kind, float X, float Y, MapDimensions? Dimensions, DateTimeOffset SeenAtUtc)`.
  - `public interface IEventState` with:
    - `IReadOnlyList<ActiveMarker> GetActiveMarkers(ulong guildId, Guid serverId, MarkerKind kind)`
    - `IReadOnlyList<RustMapEvent> GetRecentEvents(ulong guildId, Guid serverId)`
  - `internal sealed class EventStateStore : IEventState` with:
    - `void Apply(MapMarkersChangedEvent delta, IReadOnlyList<RustMapEvent> events)` — updates active set from the full delta (add enters, remove leaves) and pushes the classified events onto the ring (bounded to 10, newest-first).
    - `void Clear(ulong guildId, Guid serverId)`.

> **`ActiveMarker` carries `MapDimensions? Dimensions`** (folded in here, not deferred): stamped from `delta.Dimensions` on each added marker, so Task 10's command handlers can render a grid ref from the active marker. (The spec's §4-step-5 note about deferring this is superseded.)
>
> **Active-marker maintenance is from the full delta, independent of alerting** (spec §4 step 5, tightened): every `delta.Added` enters the active set (stamped `SeenAtUtc = clock.UtcNow`, `Dimensions = delta.Dimensions`), every `delta.Removed` leaves it — regardless of whether the marker produced an alert (so an un-alerted removal, e.g. a crate, still clears state). Separately, each *alerted* `RustMapEvent` is pushed onto the bounded ring. **Inject `IClock`** for `SeenAtUtc`.

- [ ] **Step 1: Write the failing test**

`tests/RustPlusBot.Features.Events.Tests/State/EventStateStoreTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Events.Tests.State;

public sealed class EventStateStoreTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private static EventStateStore Build()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return new EventStateStore(clock);
    }

    private static MapMarkersChangedEvent Delta(
        IReadOnlyList<MapMarkerSnapshot> added,
        IReadOnlyList<MapMarkerSnapshot> removed) =>
        new(Guild, Server, null, added, removed);

    private static MapMarkersChangedEvent DeltaWithDims(
        IReadOnlyList<MapMarkerSnapshot> added,
        IReadOnlyList<MapMarkerSnapshot> removed,
        MapDimensions? dims) =>
        new(Guild, Server, dims, added, removed);

    [Fact]
    public void Added_marker_becomes_active_and_carries_dimensions()
    {
        var store = Build();
        var dims = new MapDimensions(4000u, 4000u, 500);
        store.Apply(
            DeltaWithDims([new MapMarkerSnapshot(1, MarkerKind.CargoShip, 10f, 20f, null)], [], dims),
            [new RustMapEvent(MapEventKind.CargoEntered, 10f, 20f, dims, Now)]);

        var active = store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip);
        Assert.Equal(1, active.Count);
        Assert.Equal(10f, active[0].X);
        Assert.Equal(dims, active[0].Dimensions);
        Assert.Equal(Now, active[0].SeenAtUtc);
    }

    [Fact]
    public void Removed_marker_leaves_active_even_when_unalerted()
    {
        var store = Build();
        // A Crate marker produces NO classified event (core-3), but is still tracked in the active set...
        store.Apply(Delta([new MapMarkerSnapshot(4, MarkerKind.Crate, 0f, 0f, null)], []), []);
        Assert.Equal(1, store.GetActiveMarkers(Guild, Server, MarkerKind.Crate).Count);
        // ...and its (un-alerted) removal must still clear it.
        store.Apply(Delta([], [new MapMarkerSnapshot(4, MarkerKind.Crate, 0f, 0f, null)]), []);

        Assert.Empty(store.GetActiveMarkers(Guild, Server, MarkerKind.Crate));
    }

    [Fact]
    public void Recent_events_are_newest_first_and_bounded_to_ten()
    {
        var store = Build();
        for (var i = 0; i < 12; i++)
        {
            store.Apply(
                Delta([new MapMarkerSnapshot((ulong)i, MarkerKind.CargoShip, i, 0f, null)], []),
                [new RustMapEvent(MapEventKind.CargoEntered, i, 0f, null, Now.AddMinutes(i))]);
        }

        var recent = store.GetRecentEvents(Guild, Server);
        Assert.Equal(10, recent.Count);
        Assert.Equal(Now.AddMinutes(11), recent[0].AtUtc); // newest first
    }

    [Fact]
    public void Clear_drops_active_and_recent_for_that_server()
    {
        var store = Build();
        store.Apply(Delta([new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null)], []),
            [new RustMapEvent(MapEventKind.CargoEntered, 0f, 0f, null, Now)]);

        store.Clear(Guild, Server);

        Assert.Empty(store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip));
        Assert.Empty(store.GetRecentEvents(Guild, Server));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter EventStateStoreTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement the types**

`ActiveMarker.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Events.State;

/// <summary>A marker currently present on a server's map.</summary>
/// <param name="Id">The marker id.</param>
/// <param name="Kind">The marker kind.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Dimensions">Map dimensions for grid rendering, or null.</param>
/// <param name="SeenAtUtc">When the marker was first seen.</param>
public sealed record ActiveMarker(
    ulong Id,
    MarkerKind Kind,
    float X,
    float Y,
    MapDimensions? Dimensions,
    DateTimeOffset SeenAtUtc);
```

`IEventState.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;

namespace RustPlusBot.Features.Events.State;

/// <summary>Read access to current map markers and recent events (consumed by in-game command handlers).</summary>
public interface IEventState
{
    /// <summary>Gets the currently-active markers of a kind for a server (empty if none/unknown).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="kind">The marker kind to filter by.</param>
    /// <returns>The active markers of that kind, newest-first.</returns>
    IReadOnlyList<ActiveMarker> GetActiveMarkers(ulong guildId, Guid serverId, MarkerKind kind);

    /// <summary>Gets the recent events for a server, newest-first (empty if none/unknown).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <returns>The recent events, newest-first.</returns>
    IReadOnlyList<RustMapEvent> GetRecentEvents(ulong guildId, Guid serverId);
}
```

`EventStateStore.cs`:

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;

namespace RustPlusBot.Features.Events.State;

/// <summary>In-memory per-(guild, server) active markers and a bounded recent-event ring. Cleared on disconnect.</summary>
/// <param name="clock">Stamps when markers become active.</param>
internal sealed class EventStateStore(IClock clock) : IEventState
{
    private const int RecentCapacity = 10;
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), ServerState> _byServer = new();

    /// <inheritdoc />
    public IReadOnlyList<ActiveMarker> GetActiveMarkers(ulong guildId, Guid serverId, MarkerKind kind)
    {
        if (!_byServer.TryGetValue((guildId, serverId), out var state))
        {
            return [];
        }

        lock (state.Gate)
        {
            return state.Active.Values
                .Where(m => m.Kind == kind)
                .OrderByDescending(m => m.SeenAtUtc)
                .ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RustMapEvent> GetRecentEvents(ulong guildId, Guid serverId)
    {
        if (!_byServer.TryGetValue((guildId, serverId), out var state))
        {
            return [];
        }

        lock (state.Gate)
        {
            return state.Recent.ToList(); // already newest-first
        }
    }

    /// <summary>Applies one marker-change delta and its classified events.</summary>
    /// <param name="delta">The raw marker delta (drives the active set).</param>
    /// <param name="events">The classified events (pushed onto the recent ring).</param>
    public void Apply(MapMarkersChangedEvent delta, IReadOnlyList<RustMapEvent> events)
    {
        ArgumentNullException.ThrowIfNull(delta);
        ArgumentNullException.ThrowIfNull(events);
        var state = _byServer.GetOrAdd((delta.GuildId, delta.ServerId), static _ => new ServerState());
        var now = clock.UtcNow;
        lock (state.Gate)
        {
            foreach (var m in delta.Added)
            {
                state.Active[m.Id] = new ActiveMarker(m.Id, m.Kind, m.X, m.Y, delta.Dimensions, now);
            }

            foreach (var m in delta.Removed)
            {
                state.Active.Remove(m.Id);
            }

            foreach (var e in events)
            {
                state.Recent.Insert(0, e);
            }

            if (state.Recent.Count > RecentCapacity)
            {
                state.Recent.RemoveRange(RecentCapacity, state.Recent.Count - RecentCapacity);
            }
        }
    }

    /// <summary>Clears all state for a server (called when its connection drops).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId) => _byServer.TryRemove((guildId, serverId), out _);

    private sealed class ServerState
    {
        public object Gate { get; } = new();

        public Dictionary<ulong, ActiveMarker> Active { get; } = [];

        public List<RustMapEvent> Recent { get; } = [];
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter EventStateStoreTests`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Events tests/RustPlusBot.Features.Events.Tests
git commit -m "feat(2a): add in-memory EventStateStore + IEventState read-seam"
```

---

### Task 7: EventEmbedRenderer + localization (pure)

**Files:**

- Create: `src/RustPlusBot.Features.Events/Rendering/EventLocalizationCatalog.cs`
- Create: `src/RustPlusBot.Features.Events/Rendering/IEventLocalizer.cs`
- Create: `src/RustPlusBot.Features.Events/Rendering/EventLocalizer.cs`
- Create: `src/RustPlusBot.Features.Events/Rendering/EventEmbedRenderer.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/Rendering/EventEmbedRendererTests.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/Rendering/EventLocalizerTests.cs`

**Interfaces:**

- Consumes: `RustMapEvent`, `MapEventKind` (Task 5); `GridReference` (Task 4).
- Produces:
  - `internal sealed class EventLocalizationCatalog` with `static Default` (copy `CommandLocalizationCatalog`'s shape: `Strings : culture -> key -> value`).
  - `internal interface IEventLocalizer { string Get(string key, string culture, params object[] args); }` + `EventLocalizer` impl (copy `CommandLocalizer`).
  - `internal sealed class EventEmbedRenderer(IEventLocalizer localizer)` with `Discord.Embed Render(RustMapEvent evt, string culture)`.

> **Localization keys** (EN / FR):
>
> - `event.cargo.entered` = "🚢 Cargo Ship entered at {0}" / "🚢 Cargo Ship arrivé en {0}"
> - `event.cargo.left` = "🚢 Cargo Ship left ({0})" / "🚢 Cargo Ship parti ({0})"
> - `event.heli.entered` = "🚁 Patrol Helicopter entered at {0}" / "🚁 Hélicoptère de patrouille arrivé en {0}"
> - `event.heli.left` = "🚁 Patrol Helicopter left ({0})" / "🚁 Hélicoptère de patrouille parti ({0})"
> - `event.chinook.spawned` = "🚁 Chinook spawned at {0}" / "🚁 Chinook apparu en {0}"
> - `event.title` = "Live event" / "Événement"
>
> (CORE-3 — there is NO `event.crate.*` key; the game no longer sends crate markers and the classifier never emits a crate event.)
>
> `{0}` is the grid reference (or raw coords). The renderer builds an `EmbedBuilder` with the localized line as the description, a UTC timestamp, and `event.title` as the author/title. For `*.left`, `{0}` is still the grid where it was last seen.

- [ ] **Step 1: Write the failing renderer test**

`tests/RustPlusBot.Features.Events.Tests/Rendering/EventEmbedRendererTests.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Rendering;

namespace RustPlusBot.Features.Events.Tests.Rendering;

public sealed class EventEmbedRendererTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 0, 0, TimeSpan.Zero);

    private static EventEmbedRenderer Build() =>
        new(new EventLocalizer(EventLocalizationCatalog.Default));

    [Fact]
    public void Cargo_entered_renders_english_with_grid()
    {
        var dims = new MapDimensions(4000u, 4000u, 500);
        var embed = Build().Render(new RustMapEvent(MapEventKind.CargoEntered, 10f, 3990f, dims, Now), "en");

        Assert.NotNull(embed.Description);
        Assert.Contains("Cargo Ship entered", embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Chinook_spawned_renders_french()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.ChinookSpawned, 0f, 0f, null, Now), "fr");
        Assert.Contains("Chinook apparu", embed.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_dimensions_render_raw_coordinates()
    {
        var embed = Build().Render(new RustMapEvent(MapEventKind.HeliEntered, 1234f, 5678f, null, Now), "en");
        Assert.Contains("(1234, 5678)", embed.Description, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter EventEmbedRendererTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement catalog, localizer, renderer**

`EventLocalizationCatalog.cs` — copy `CommandLocalizationCatalog`'s structure (a `public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings` and a `static Default`), with the EN/FR keys listed above.

`IEventLocalizer.cs` + `EventLocalizer.cs` — copy `ICommandLocalizer`/`CommandLocalizer` verbatim (rename namespace to `RustPlusBot.Features.Events.Rendering`, type names to `IEventLocalizer`/`EventLocalizer`, catalog type to `EventLocalizationCatalog`). Leave an XML `<remarks>consolidate localizers</remarks>` note (NOT a `// TODO`).

`EventEmbedRenderer.cs`:

```csharp
using Discord;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Formatting;

namespace RustPlusBot.Features.Events.Rendering;

/// <summary>Renders one <see cref="RustMapEvent"/> as a Discord embed.</summary>
/// <param name="localizer">The reply localizer.</param>
internal sealed class EventEmbedRenderer(IEventLocalizer localizer)
{
    /// <summary>Renders the event for a guild culture.</summary>
    /// <param name="evt">The event to render.</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <returns>The built embed.</returns>
    public Embed Render(RustMapEvent evt, string culture)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var grid = GridReference.From(evt.X, evt.Y, evt.Dimensions);
        var key = evt.Kind switch
        {
            MapEventKind.CargoEntered => "event.cargo.entered",
            MapEventKind.CargoLeft => "event.cargo.left",
            MapEventKind.HeliEntered => "event.heli.entered",
            MapEventKind.HeliLeft => "event.heli.left",
            MapEventKind.ChinookSpawned => "event.chinook.spawned",
            _ => "event.chinook.spawned",
        };

        return new EmbedBuilder()
            .WithAuthor(localizer.Get("event.title", culture))
            .WithDescription(localizer.Get(key, culture, grid))
            .WithTimestamp(evt.AtUtc)
            .Build();
    }
}
```

- [ ] **Step 4: Write + run the localizer test**

`EventLocalizerTests.cs` (copy `CommandLocalizerTests` shape): assert EN and FR lookups, fallback to EN for an unknown culture, and that the raw key is returned for an unknown key. Run:

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter "EventEmbedRendererTests|EventLocalizerTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Events tests/RustPlusBot.Features.Events.Tests
git commit -m "feat(2a): add EventEmbedRenderer + EN/FR event localization"
```

---

### Task 8: EventRelay + poster + locator (Workspace) + hosted service + DI

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Locating/IEventChannelLocator.cs` (**public**)
- Create: `src/RustPlusBot.Features.Workspace/Locating/EventChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs` (+`ServerEvents`)
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs` (+`#events` spec)
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs` (+`channel.events.name`)
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs` (register locator)
- Create: `src/RustPlusBot.Features.Events/Posting/IEventChannelPoster.cs` + `Posting/DiscordEventChannelPoster.cs`
- Create: `src/RustPlusBot.Features.Events/Relaying/EventRelay.cs`
- Create: `src/RustPlusBot.Features.Events/Hosting/EventsHostedService.cs`
- Create: `src/RustPlusBot.Features.Events/EventServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/Relaying/EventRelayTests.cs`
- Test: `tests/RustPlusBot.Features.Events.Tests/EventRegistrationTests.cs`
- Test (Workspace): `tests/RustPlusBot.Features.Workspace.Tests/Locating/EventChannelLocatorTests.cs`

**Interfaces:**

- Consumes: `MapMarkersChangedEvent` (Task 1), `MarkerEventClassifier` (Task 5), `EventStateStore` (Task 6), `EventEmbedRenderer` (Task 7), `IWorkspaceStore.GetChannelsByKeyAsync` + culture, `IEventBus`.
- Produces:
  - `public interface IEventChannelLocator { Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken); }` (in Workspace; the `#events` direction only — no reverse `Resolve` needed since there's no Discord→game path).
  - `internal interface IEventChannelPoster { Task PostAsync(ulong channelId, Discord.Embed embed, CancellationToken cancellationToken); }`.
  - `internal sealed class EventRelay(...)` with `Task RelayAsync(MapMarkersChangedEvent evt, CancellationToken ct)`.
  - `public static IServiceCollection AddEvents(this IServiceCollection services)`.

> **Disconnect-clear wiring (CONFIRMED branch b):** the spec requires clearing `EventStateStore` on disconnect. The supervisor publishes `ConnectionStatusChangedEvent` on every status transition. **Confirmed shape: `ConnectionStatusChangedEvent(ulong GuildId, Guid ServerId)` — it does NOT carry the status.** So `EventsHostedService` subscribes to `ConnectionStatusChangedEvent` as a SECOND subscription and, per event, opens a DI scope, reads the current status via **`IConnectionStore.GetStateAsync(guildId, serverId, ct)`** (returns `ConnectionState?` with `.Status` of type `RustPlusBot.Domain.Connections.ConnectionStatus`), and calls `EventStateStore.Clear(guildId, serverId)` when the state is null OR `state.Status != ConnectionStatus.Connected`. (`ConnectionStatus.Connected = 1`; the other members are `Connecting`/`Unreachable`/`NoCredentials`.) The store is scoped, so use `scopeFactory.CreateAsyncScope()` + `GetRequiredService<IConnectionStore>()` (mirror the supervisor's `PublishStatusAsync` scope pattern).

- [ ] **Step 1: Verify ConnectionStatusChangedEvent shape and IConnectionStore.GetAsync**

Run: `sed -n '1,40p' src/RustPlusBot.Abstractions/Events/ConnectionStatusChangedEvent.cs && grep -n "GetAsync\|ConnectionStatus" src/RustPlusBot.Persistence/Connections/IConnectionStore.cs`
Expected: learn whether the event carries the status. Record which branch (a/b) applies; the relay/hosted-service code below uses branch (b) (scope-read) — if (a) applies, simplify to read `evt.Status`.

- [ ] **Step 2: Add the Workspace channel key, spec, name, locator**

`WorkspaceKeys.cs` — add to `WorkspaceChannelKeys`:

```csharp
    /// <summary>Key for the per-server #events channel.</summary>
    public const string ServerEvents = "events";
```

`ServerWorkspaceSpecProvider.cs` — add a third channel spec (Order 2):

```csharp
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerEvents, "channel.events.name",
            ChannelPermissionProfile.ReadOnly, 2),
```

`LocalizationCatalog.cs` — add `channel.events.name` to EN ("events") and FR ("evenements") mirroring `channel.teamchat.name` (find that key and copy its EN/FR entries' style).

`Locating/IEventChannelLocator.cs` + `Locating/EventChannelLocator.cs` — copy `ITeamChatChannelLocator`/`TeamChatChannelLocator`, keeping ONLY the `GetChannelIdAsync` direction (drop `ResolveAsync` and `_byChannelId`), and query `WorkspaceChannelKeys.ServerEvents` instead of `ServerTeamChat`. Keep the `IDisposable`/`SemaphoreSlim`/30s TTL/`IClock` pattern.

`WorkspaceServiceCollectionExtensions.cs` — register it next to the teamchat locator:

```csharp
        services.AddSingleton<IEventChannelLocator, EventChannelLocator>();
```

(find the `ITeamChatChannelLocator` registration and mirror it).

- [ ] **Step 3: Write the failing EventRelay test**

`tests/RustPlusBot.Features.Events.Tests/Relaying/EventRelayTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Posting;
using RustPlusBot.Features.Events.Relaying;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Locating;

namespace RustPlusBot.Features.Events.Tests.Relaying;

public sealed class EventRelayTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();

    [Fact]
    public async Task Posts_one_embed_per_classified_event_and_updates_state()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(new DateTimeOffset(2026, 6, 17, 12, 0, 0, TimeSpan.Zero));
        var locator = Substitute.For<IEventChannelLocator>();
        locator.GetChannelIdAsync(Guild, Server, Arg.Any<CancellationToken>()).Returns(999UL);
        var poster = Substitute.For<IEventChannelPoster>();
        var store = new EventStateStore(clock);
        var workspaceStore = Substitute.For<RustPlusBot.Persistence.Workspace.IWorkspaceStore>();
        workspaceStore.GetCultureAsync(Guild, Arg.Any<CancellationToken>()).Returns("en");
        var relay = new EventRelay(
            new MarkerEventClassifier(clock), store,
            new EventEmbedRenderer(new EventLocalizer(EventLocalizationCatalog.Default)),
            locator, poster, /* scope-factory for culture — see note */ default!);

        await relay.RelayAsync(
            new MapMarkersChangedEvent(Guild, Server, null,
                [new MapMarkerSnapshot(1, MarkerKind.CargoShip, 0f, 0f, null)], []),
            CancellationToken.None);

        await poster.Received(1).PostAsync(999UL, Arg.Any<Discord.Embed>(), Arg.Any<CancellationToken>());
        Assert.Equal(1, store.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip).Count);
    }

    [Fact]
    public async Task When_channel_missing_updates_state_but_does_not_post()
    {
        // locator returns null => no post, but state still updated (so !cargo works even without #events).
    }

    [Fact]
    public async Task Empty_classification_posts_nothing()
    {
        // delta of only Other markers => classifier yields nothing => no post; state-active still reflects the delta?
        // Other markers are not tracked by command handlers; assert poster never called.
    }
}
```

> **Culture source:** `EventRelay` needs the guild culture. `TeamChatRelay` doesn't localize, but `CommandsHostedService`/handlers read culture via `IWorkspaceStore.GetCultureAsync` inside a DI scope. Since `EventRelay` is invoked from the hosted service's bus loop, give `EventRelay` an `IServiceScopeFactory` and open a scope to read `IWorkspaceStore.GetCultureAsync` per event (mirror `CommandsHostedService`'s scope pattern). Adjust the test to pass a real `IServiceScopeFactory` substitute that yields a scope whose `IWorkspaceStore` returns "en" — OR inject `IWorkspaceStore` resolution differently. **Simpler for testability:** inject a small `IGuildCultureProvider` seam? No — avoid new seams. **Chosen:** `EventRelay` takes `IServiceScopeFactory`; the test builds a tiny `ServiceCollection` with a substitute `IWorkspaceStore` registered scoped, builds a provider, and passes its `IServiceScopeFactory`. Rewrite the test arrangement accordingly (see `CommandsHostedService` tests if any, else `TeamChatInboundProcessorTests` for the scope-factory pattern).

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests --filter EventRelayTests`
Expected: FAIL — `EventRelay`/poster/locator types missing.

- [ ] **Step 5: Implement poster, relay, hosted service, DI**

`Posting/IEventChannelPoster.cs`:

```csharp
using Discord;

namespace RustPlusBot.Features.Events.Posting;

/// <summary>Posts an event embed to a Discord channel.</summary>
internal interface IEventChannelPoster
{
    /// <summary>Posts <paramref name="embed"/> to the channel.</summary>
    /// <param name="channelId">The target Discord channel id.</param>
    /// <param name="embed">The embed to post.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the embed has been posted.</returns>
    Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken);
}
```

`Posting/DiscordEventChannelPoster.cs` — copy the channel-fetch idiom from `DiscordTeamChatWebhookPoster` BUT post a normal message embed (no webhook): resolve the channel via `DiscordSocketClient.GetChannelAsync` (async — `CA1849`/`S6966` forbid the sync `GetChannel`), cast to `IMessageChannel`, `await channel.SendMessageAsync(embed: embed)`. Wrap in the broad-catch + logger pattern.

`Relaying/EventRelay.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Posting;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Events.Relaying;

/// <summary>Classifies one marker delta, updates state, and posts one embed per event to #events.</summary>
internal sealed class EventRelay(
    MarkerEventClassifier classifier,
    EventStateStore state,
    EventEmbedRenderer renderer,
    IEventChannelLocator locator,
    IEventChannelPoster poster,
    IServiceScopeFactory scopeFactory)
{
    /// <summary>Handles one <see cref="MapMarkersChangedEvent"/>.</summary>
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

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is null)
        {
            return;
        }

        var culture = await GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        foreach (var e in events)
        {
            var embed = renderer.Render(e, culture);
            await poster.PostAsync(channelId.Value, embed, cancellationToken).ConfigureAwait(false);
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

> CONFIRMED: `IWorkspaceStore.GetCultureAsync(ulong, CancellationToken = default)` returns a NON-nullable `Task<string>` (it defaults to "en" internally), so no `?? DefaultCulture` fallback is needed — drop the `DefaultCulture` const from `EventRelay` (it is unused; keep the class clean to satisfy analyzers).

`Hosting/EventsHostedService.cs` — copy `ChatHostedService`'s two-subscription shape: one loop over `eventBus.SubscribeAsync<MapMarkersChangedEvent>` → `relay.RelayAsync`; a second loop over `eventBus.SubscribeAsync<ConnectionStatusChangedEvent>` → clear state when not connected (per Step 1's branch). The store is a singleton, injected directly. Use the broad-catch + `LoggerMessage` pattern.

`EventServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Hosting;
using RustPlusBot.Features.Events.Posting;
using RustPlusBot.Features.Events.Relaying;
using RustPlusBot.Features.Events.Rendering;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Events;

/// <summary>DI registration for the live-events feature.</summary>
public static class EventServiceCollectionExtensions
{
    /// <summary>Registers the classifier, state store, renderer, relay, poster, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddEvents(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<EventStateStore>();
        services.AddSingleton<IEventState>(sp => sp.GetRequiredService<EventStateStore>());
        services.AddSingleton(EventLocalizationCatalog.Default);
        services.AddSingleton<IEventLocalizer, EventLocalizer>();
        services.AddSingleton<MarkerEventClassifier>();
        services.AddSingleton<EventEmbedRenderer>();
        services.AddSingleton<IEventChannelPoster, DiscordEventChannelPoster>();
        services.AddSingleton<EventRelay>();
        services.AddHostedService<EventsHostedService>();

        return services;
    }
}
```

> `EventStateStore` is registered ONCE and exposed both as itself (for `EventRelay`/hosted service) and as `IEventState` (for command handlers) — the same factory-aliasing the supervisor uses for its three roles. `MarkerEventClassifier` injects `IClock` (already registered by the host).

- [ ] **Step 6: Write the registration test**

`tests/RustPlusBot.Features.Events.Tests/EventRegistrationTests.cs` — mirror `CommandRegistrationTests`: build a `ServiceCollection`, add logging + substitutes for `IClock`, `IEventBus`, `DiscordSocketClient` (or whatever `DiscordEventChannelPoster` needs — register a substitute), `IEventChannelLocator`, `IWorkspaceStore` (scoped), call `AddEvents()`, `BuildServiceProvider(validateScopes:true)`, and assert: `EventsHostedService` is registered, `IEventState` resolves to the same instance as `EventStateStore`, `EventRelay` resolves.

- [ ] **Step 7: Write the EventChannelLocator test (Workspace)**

`tests/RustPlusBot.Features.Workspace.Tests/Locating/EventChannelLocatorTests.cs` — copy `TeamChatChannelLocatorTests`, asserting `GetChannelIdAsync` returns the provisioned `#events` channel id and null when absent, querying `WorkspaceChannelKeys.ServerEvents`.

- [ ] **Step 8: Run all new tests**

Run: `dotnet test tests/RustPlusBot.Features.Events.Tests && dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter EventChannelLocator`
Expected: PASS.

- [ ] **Step 9: Run FULL suite (Workspace spec count changed!)**

Run: `dotnet test`
Expected: all green. **The new `#events` ChannelSpec changes Workspace reconciler test expectations** (any test asserting the per-server channel COUNT or the exact spec list will now see 3 channels, not 2). Find and update those assertions (search `ServerWorkspaceSpecProvider` tests / reconciler tests asserting channel counts). Read per-assembly counts.

- [ ] **Step 10: jb + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add -A
git commit -m "feat(2a): EventRelay, #events poster/locator, hosted service, DI + Workspace spec"
```

---

### Task 9: Wire into the Host + register options

**Files:**

- Modify: `src/RustPlusBot.Host/Program.cs`
- Test: `tests/RustPlusBot.Host.Tests/` (if a host composition/registration test exists — verify)

**Interfaces:**

- Consumes: `AddEvents()` (Task 8); `ConnectionOptions.MarkerPollInterval` (Task 3).

- [ ] **Step 1: Add the marker-poll validation to ConnectionOptions binding**

In `Program.cs`, in the existing `AddOptions<ConnectionOptions>()` chain (lines ~43-53), add before `.ValidateOnStart()`:

```csharp
    .Validate(static o => o.MarkerPollInterval > TimeSpan.Zero, "Connections:MarkerPollInterval must be positive.")
```

- [ ] **Step 2: Register the Events feature**

In `Program.cs`, after `builder.Services.AddChat();` (line ~55) or after `AddCommands()` — pick a spot consistent with feature ordering; `AddEvents()` depends on Workspace + Connections being registered (they are, earlier). Add:

```csharp
builder.Services.AddEvents();
```

and the `using RustPlusBot.Features.Events;` import at the top (match existing feature usings).

- [ ] **Step 3: Build the host**

Run: `dotnet build src/RustPlusBot.Host`
Expected: 0/0. Confirms the full DI graph composes (Events resolves all its deps from the host's registrations).

- [ ] **Step 4: Run full suite**

Run: `dotnet test`
Expected: all green; if a Host composition test exists it still passes.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Host
git commit -m "feat(2a): register Events feature + MarkerPollInterval option in the host"
```

---

### Task 10: The four in-game command handlers (!cargo / !heli / !chinook / !events)

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/CargoCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/HeliCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/ChinookCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/EventsCommandHandler.cs`
- Modify: `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/EventHandlersTests.cs`

**Interfaces:**

- Consumes: `IEventState` (Task 6) — `GetActiveMarkers`/`GetRecentEvents`; `ICommandLocalizer`; `GridReference` (Task 4) for formatting locations; `DurationFormat.Compact` (existing, for "how long ago"); `IClock` for "ago".
- Produces: four `ICommandHandler`s with `Name` = `"cargo"`/`"heli"`/`"chinook"`/`"events"`.

> **Commands project must reference Features.Events** for `IEventState` + `GridReference`. Add the project reference. Check there's no cycle: Events → Connections, Events → Workspace; Commands → Connections, Commands → Workspace, Commands → Discord. Commands → Events is NEW and fine (Events does NOT reference Commands). Confirm with `dotnet build` after adding the ref.

> **Handler behavior:**
>
> - `!cargo`: `GetActiveMarkers(.., CargoShip)`. If empty → `command.cargo.none`. Else → `command.cargo.ok` with the grid of the first (newest) marker + how-long-ago (`DurationFormat.Compact(clock.UtcNow - marker.SeenAtUtc)`).
> - `!heli`: same with `PatrolHelicopter` → `command.heli.ok` / `command.heli.none`.
> - `!chinook`: same with `Chinook` → `command.chinook.ok` / `command.chinook.none`.
> - `!events`: `GetRecentEvents`. If empty → `command.events.none`. Else join the recent events (each via a short per-kind label + grid) into `command.events.ok`.

- [ ] **Step 1: Add the project reference + localization keys**

Add to `RustPlusBot.Features.Commands.csproj`:

```xml
<ProjectReference Include="..\RustPlusBot.Features.Events\RustPlusBot.Features.Events.csproj" />
```

In `CommandLocalizationCatalog.cs`, add to BOTH `en` and `fr` dictionaries (EN shown; FR mirror):

```csharp
                ["command.cargo.ok"] = "Cargo Ship at {0} ({1} ago)",
                ["command.cargo.none"] = "No cargo ship on the map.",
                ["command.heli.ok"] = "Patrol Helicopter at {0} ({1} ago)",
                ["command.heli.none"] = "No patrol helicopter on the map.",
                ["command.chinook.ok"] = "Chinook at {0} ({1} ago)",
                ["command.chinook.none"] = "No chinook on the map.",
                ["command.events.ok"] = "Recent: {0}",
                ["command.events.none"] = "No recent events.",
                ["command.event.cargoentered"] = "cargo in {0}",
                ["command.event.cargoleft"] = "cargo left {0}",
                ["command.event.helientered"] = "heli in {0}",
                ["command.event.helileft"] = "heli left {0}",
                ["command.event.chinookspawned"] = "chinook in {0}",
```

(CORE-3 — there is NO `command.event.cratespawned` key.) FR equivalents (e.g. `["command.cargo.none"] = "Aucun cargo sur la carte.",` etc.) — keep the `{0}`/`{1}` placeholders.

- [ ] **Step 2: Write the failing handler tests**

`tests/RustPlusBot.Features.Commands.Tests/Handlers/EventHandlersTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class EventHandlersTests
{
    private const ulong Guild = 1UL;
    private static readonly Guid Server = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 17, 12, 5, 0, TimeSpan.Zero);

    private static (IClock Clock, ICommandLocalizer Loc) Deps()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return (clock, new CommandLocalizer(CommandLocalizationCatalog.Default));
    }

    private static CommandContext Ctx() => new(Guild, Server, "en", 0UL, string.Empty, []);

    [Fact]
    public async Task Cargo_with_active_marker_reports_grid()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        var dims = new MapDimensions(4000u, 4000u, 500);
        state.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip).Returns(
            [new ActiveMarker(1, MarkerKind.CargoShip, 10f, 3990f, dims, Now.AddMinutes(-5))]);
        var reply = await new CargoCommandHandler(state, loc, clock).ExecuteAsync(Ctx(), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("Cargo Ship at", reply, StringComparison.Ordinal);
        // Dimensions present → a grid ref (e.g. "A0"), NOT raw "(10, 3990)" coords.
        Assert.DoesNotContain("(10, 3990)", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cargo_without_marker_reports_none()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetActiveMarkers(Guild, Server, MarkerKind.CargoShip).Returns([]);
        var reply = await new CargoCommandHandler(state, loc, clock).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Equal("No cargo ship on the map.", reply);
    }

    [Fact]
    public async Task Events_lists_recent_or_reports_none()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        state.GetRecentEvents(Guild, Server).Returns([]);
        Assert.Equal("No recent events.", await new EventsCommandHandler(state, loc).ExecuteAsync(Ctx(), CancellationToken.None));

        state.GetRecentEvents(Guild, Server).Returns(
            [new RustMapEvent(MapEventKind.CargoEntered, 10f, 3990f, new MapDimensions(4000u, 4000u, 500), Now)]);
        var reply = await new EventsCommandHandler(state, loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.Contains("Recent:", reply!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Handlers_expose_expected_names()
    {
        var (clock, loc) = Deps();
        var state = Substitute.For<IEventState>();
        Assert.Equal("cargo", new CargoCommandHandler(state, loc, clock).Name);
        Assert.Equal("heli", new HeliCommandHandler(state, loc, clock).Name);
        Assert.Equal("chinook", new ChinookCommandHandler(state, loc, clock).Name);
        Assert.Equal("events", new EventsCommandHandler(state, loc).Name);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter EventHandlersTests`
Expected: FAIL — handlers don't exist.

- [ ] **Step 4: Implement the four handlers**

`CargoCommandHandler.cs` (the others mirror it with their kind + keys; `EventsCommandHandler` differs):

```csharp
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Commands.Formatting;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!cargo — reports the cargo ship's current grid position, if any.</summary>
/// <param name="state">The live event state.</param>
/// <param name="localizer">The reply localizer.</param>
/// <param name="clock">For "how long ago".</param>
internal sealed class CargoCommandHandler(IEventState state, ICommandLocalizer localizer, IClock clock)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "cargo";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var markers = state.GetActiveMarkers(context.GuildId, context.ServerId, MarkerKind.CargoShip);
        if (markers.Count == 0)
        {
            return Task.FromResult<string?>(localizer.Get("command.cargo.none", context.Culture));
        }

        var m = markers[0];
        var grid = GridReference.From(m.X, m.Y, m.Dimensions);
        var ago = DurationFormat.Compact(clock.UtcNow - m.SeenAtUtc);
        return Task.FromResult<string?>(localizer.Get("command.cargo.ok", context.Culture, grid, ago));
    }
}
```

> **`ActiveMarker.Dimensions` already exists** (folded into Task 6): the handler passes `m.Dimensions` to `GridReference.From`, so alert grids and command grids are consistent. (The earlier "dims gap" is resolved.)
>
> `HeliCommandHandler` / `ChinookCommandHandler` mirror `CargoCommandHandler` with their `MarkerKind` (`PatrolHelicopter` / `Chinook`) and key prefix (`command.heli.*` / `command.chinook.*`). `Name` = `"heli"` / `"chinook"`.

`EventsCommandHandler.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Events.Classifying;
using RustPlusBot.Features.Events.Formatting;
using RustPlusBot.Features.Events.State;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!events — lists the most recent live events.</summary>
/// <param name="state">The live event state.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class EventsCommandHandler(IEventState state, ICommandLocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "events";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var events = state.GetRecentEvents(context.GuildId, context.ServerId);
        if (events.Count == 0)
        {
            return Task.FromResult<string?>(localizer.Get("command.events.none", context.Culture));
        }

        var parts = events.Select(e =>
        {
            var grid = GridReference.From(e.X, e.Y, e.Dimensions);
            var key = e.Kind switch
            {
                MapEventKind.CargoEntered => "command.event.cargoentered",
                MapEventKind.CargoLeft => "command.event.cargoleft",
                MapEventKind.HeliEntered => "command.event.helientered",
                MapEventKind.HeliLeft => "command.event.helileft",
                MapEventKind.ChinookSpawned => "command.event.chinookspawned",
                _ => "command.event.chinookspawned",
            };
            return localizer.Get(key, context.Culture, grid);
        });

        return Task.FromResult<string?>(
            localizer.Get("command.events.ok", context.Culture, string.Join(", ", parts)));
    }
}
```

> Verify `DurationFormat.Compact`'s exact signature (`grep -n "Compact" src/RustPlusBot.Features.Commands/Formatting/DurationFormat.cs`) and that it takes a `TimeSpan`.

- [ ] **Step 5: Register the four handlers**

In `CommandServiceCollectionExtensions.cs`, after the `ProxCommandHandler` line:

```csharp
        services.AddScoped<ICommandHandler, CargoCommandHandler>();
        services.AddScoped<ICommandHandler, HeliCommandHandler>();
        services.AddScoped<ICommandHandler, ChinookCommandHandler>();
        services.AddScoped<ICommandHandler, EventsCommandHandler>();
```

- [ ] **Step 6: Update the registration test (count 12 → 16)**

In `CommandRegistrationTests.cs` `Dispatcher_and_handlers_resolve`:

- change `Assert.Equal(12, handlers.Count);` to `Assert.Equal(16, handlers.Count);`
- add `Assert.Contains(handlers, h => h.Name == "cargo");` (and heli/chinook/events).
- **Add the `IEventState` registration** to BOTH test methods' service setup (they `BuildServiceProvider(validateScopes:true)`, and the new handlers depend on `IEventState`): `services.AddSingleton<IEventState>(_ => Substitute.For<IEventState>());`. Also ensure `IClock` is already registered (it is).

- [ ] **Step 7: Run the handler + registration tests**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "EventHandlersTests|CommandRegistrationTests"`
Expected: PASS.

- [ ] **Step 8: Run FULL suite + jb**

Run: `dotnet test && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Expected: all assemblies green; Commands count bumped. Review jb diff (branch files only).

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(2a): add !cargo/!heli/!chinook/!events in-game command handlers"
```

---

### Task 11: Documentation + final verification

**Files:**

- Modify: `docs/running-locally.md` (or wherever gateway intents / channel list are documented — verify) — note the new `#events` channel.
- Verify only (no code): the running-locally privileged-intents note (Events adds NO new intent — markers come over the Rust socket, not the gateway; `#events` posting needs only the existing Send Messages perm).

- [ ] **Step 1: Document the #events channel**

The channel-list / provisioning doc is `docs/development/running-locally.md`. Add a short note that the per-server `#events` channel carries live event alerts (cargo ship, patrol helicopter, chinook) — and explicitly that it needs **no new gateway intent** (markers arrive over the Rust+ socket, not the Discord gateway; only the existing **Send Messages** / **Embed Links** perms on the provisioned channel are used). CORE-3 — do NOT mention crate/oil-rig (the game no longer sends crate markers; deferred).

- [ ] **Step 2: Full clean build + test + jb (final gate)**

Run:

```bash
dotnet build RustPlusBot.slnx -warnaserror
dotnet test
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder && git diff --exit-code
```

Expected: 0 warnings / 0 errors; ALL tests green (read per-assembly counts: Abstractions +2, Connections + supervisor cases, NEW Events ~ all green, Commands +~6, Workspace +1 locator and updated spec-count); `git diff --exit-code` after jb shows no further reformatting.

- [ ] **Step 3: Verify no EF model drift**

Run: `git diff --name-only develop... | grep -iE "Migrations|ModelSnapshot|DbContext" || echo "no EF changes"`
Expected: "no EF changes" (2a adds no entity/migration).

- [ ] **Step 4: Commit docs**

```bash
git add docs
git commit -m "docs(2a): document the #events channel"
```

---

## Self-Review

**1. Spec coverage:**

- Marker poll loop in the supervisor's connected window, 10s configurable → Task 3 (interval on `ConnectionOptions.MarkerPollInterval`, deviation noted). ✓
- `GetMap` dims-only fetch on connect → Tasks 2 (seam) + 3 (fetch). ✓
- New `Features.Events` project → Tasks 4–8. ✓
- Core-4 classification → Task 5. ✓
- One embed per event to per-server `#events` → Tasks 7 (render) + 8 (relay/post/spec). ✓
- In-memory active markers + recent ring; first poll silent baseline → Tasks 3 (baseline) + 6 (store). ✓
- Four `!commands` reading `IEventState` → Task 10. ✓
- Workspace `#events` ChannelSpec + EN/FR name → Task 8. ✓
- Disconnect clears state → Task 8 (Step 1 verifies event shape, hosted service subscribes). ✓
- Error handling (poll failure retains snapshot; post wrapped; channel-missing skip) → Tasks 3, 8. ✓
- No new entity/migration → Task 11 Step 3 verifies. ✓
- Options validated ValidateOnStart → Task 9. ✓

**2. Placeholder scan:** No "TBD"/"implement later". Two places defer a concrete decision to a verification step (Task 8 Step 1 `ConnectionStatusChangedEvent` shape; Task 10 `ActiveMarker` dims gap) — each gives the exact resolution to apply, so they are decisions-with-instructions, not placeholders.

**3. Type consistency:**

- `MapMarkersChangedEvent(ulong, Guid, MapDimensions?, IReadOnlyList<MapMarkerSnapshot>, IReadOnlyList<MapMarkerSnapshot>)` — consistent across Tasks 1, 3, 5, 6, 8. ✓
- `MarkerKind` (CargoShip/PatrolHelicopter/Chinook/Crate/Other) — consistent Tasks 1, 2, 5, 6, 10. ✓
- `RustMapEvent(MapEventKind, float, float, MapDimensions?, DateTimeOffset)` — consistent Tasks 5, 6, 7, 10. ✓
- `IEventState.GetActiveMarkers(ulong, Guid, MarkerKind)` / `GetRecentEvents(ulong, Guid)` — consistent Tasks 6, 10. ✓
- **Fix applied during review:** `ActiveMarker` needs `MapDimensions? Dimensions` for the command handlers to render grids — folded into Task 10 Step 4 (extend the record + update Task 6's tests). Noted as the chosen resolution. ✓
- `ConnectionOptions.MarkerPollInterval` — Tasks 3, 9. ✓
- Locator placement corrected to Workspace (deviation section) — Task 8 consistent. ✓
