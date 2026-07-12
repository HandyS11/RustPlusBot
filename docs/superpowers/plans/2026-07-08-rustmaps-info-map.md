# RustMaps #info Static Map + Auto-Generate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Revert RustMaps from the #map layered base (back to the Rust+ tile), and post a static RustMaps map image to the per-server #info channel — RustMaps' render when available, else a bot-rendered grid+monuments fallback — with credit-safe background auto-generation.

**Architecture:** The #map base chain drops to `RustPlusBaseMapSource` only. A new background `InfoMapHostedService` owns the #info map surface: it tracks connected servers, drives a credit-safe generation state machine (`RustMapsMapCoordinator` + `RustMapsGenerationDriver`), and posts an image attachment to #info (`IInfoMapPoster`, mirroring the #map poster) — the RustMaps `ImageUrl` bytes when ready, otherwise `MapComposer.ComposeStaticAsync` (grid + monuments only). Gated behind the RustMaps API key.

**Tech Stack:** .NET 10, xUnit + NSubstitute, SixLabors.ImageSharp 3.1.12 / Drawing 2.1.7, RustPlusApi 2.0.0-beta.4, RustMapsApi 1.0.0-beta.2, Discord.Net 3.20.1.

**Spec:** `docs/superpowers/specs/2026-07-08-rustmaps-info-map-design.md`

## Global Constraints

- Solution is `RustPlusBot.slnx` (no `.sln`). **`-maxcpucount:1` is MANDATORY on EVERY `dotnet build` AND `dotnet test`** — a ConfigureGitHooks race under parallel MSBuild silently DROPS an assembly's tests (they report 0, look "passing"). Never omit it.
- Build `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` — zero warnings/errors.
- `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` is a hard CI gate — run before the final commit; must produce no diff.
- Tests: plain xUnit `Assert.*` + NSubstitute — NO FluentAssertions; `using Xunit` is global in test projects.
- Public types/members need XML doc comments. `CultureInfo.InvariantCulture` / `StringComparison.Ordinal` on string ops.
- ImageSharp stays `3.1.12`, ImageSharp.Drawing stays `2.1.7`.
- User-facing strings live in the shared `src/RustPlusBot.Localization/Strings.resx` (+ `Strings.fr.resx`), resolved via `ILocalizer.Get(key, culture[, args])`.
- `docs/superpowers/` is gitignored — never `git add` it.
- Branch: `feat/map-render-rework` (already checked out; do NOT create/switch branches). Continues the map rework; base for this slice is the current branch HEAD.
- Baseline before this slice: 879 tests + 1 skipped / 17 assemblies, build 0/0.

---

### Task 1: Revert RustMaps from the base-map chain

**Files:**

- Delete: `src/RustPlusBot.Features.Map/Composing/RustMapsBaseMapSource.cs`
- Delete: `tests/RustPlusBot.Features.Map.Tests/RustMapsBaseMapSourceTests.cs`
- Modify: `src/RustPlusBot.Features.Map/MapServiceCollectionExtensions.cs`
- Modify: `tests/RustPlusBot.Features.Map.Tests/MapRegistrationTests.cs`

**Interfaces:**

- Produces: after this task, `IBaseMapSource` resolves to exactly `RustPlusBaseMapSource`. `AddRustMapsClientV4` still runs when the key is present (later tasks consume `IRustMapsClient`); the RustMaps image-download `HttpClient` and the RustMaps `IBaseMapSource` registration are gone (re-added in Task 7 for the #info poster).

- [ ] **Step 1: Delete the source + its test**

```bash
git rm src/RustPlusBot.Features.Map/Composing/RustMapsBaseMapSource.cs \
       tests/RustPlusBot.Features.Map.Tests/RustMapsBaseMapSourceTests.cs
```

- [ ] **Step 2: Update `AddMap`**

In `MapServiceCollectionExtensions.AddMap`, replace the RustMaps block so it keeps the client but stops registering the base source + download client:

```csharp
services.AddSingleton<MapRenderer>();

// RustMaps is NOT a base-map source (the #map render draws its own layers on the Rust+ tile).
// The client is kept for the #info static-map surface (Task 7 wires the coordinator/service/poster).
var rustMapsKey = configuration["Map:RustMaps:ApiKey"];
if (!string.IsNullOrWhiteSpace(rustMapsKey))
{
    services.AddRustMapsClientV4(o => o.ApiKey = rustMapsKey);
}

services.AddSingleton<IBaseMapSource, RustPlusBaseMapSource>();
services.AddSingleton<BaseMapCache>();
services.AddSingleton<MapComposer>();
services.AddSingleton<IMapChannelPoster, DiscordMapChannelPoster>();
services.AddSingleton<MapPipeline>();
services.AddHostedService<MapHostedService>();

return services;
```

- [ ] **Step 3: Update `MapRegistrationTests`**

Delete the test `AddMap_with_RustMaps_key_registers_RustMaps_source_first` (RustMaps is no longer a base source). Change `AddMap_without_RustMaps_key_registers_only_the_RustPlus_source` into an unconditional single-source assertion, and add a with-key case asserting the base chain is STILL only RustPlus:

```csharp
[Fact]
public void AddMap_registers_only_the_RustPlus_base_source_without_key()
{
    using var provider = BuildProvider(EmptyConfiguration());
    var source = Assert.Single(provider.GetServices<IBaseMapSource>());
    Assert.IsType<RustPlusBaseMapSource>(source);
}

[Fact]
public void AddMap_with_RustMaps_key_still_uses_only_the_RustPlus_base_source()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Map:RustMaps:ApiKey", "test-key")])
        .Build();
    using var provider = BuildProvider(configuration);
    var source = Assert.Single(provider.GetServices<IBaseMapSource>());
    Assert.IsType<RustPlusBaseMapSource>(source);
}
```

Remove the now-unused `using` for anything only the deleted test referenced (the compiler will flag). `BuildProvider` is unchanged.

- [ ] **Step 4: Build + test**

Run:

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1
```

Expected: clean build; Map.Tests green (down by the deleted RustMapsBaseMapSource tests, up by none yet).

- [ ] **Step 5: Commit**

```bash
git add -A src tests
git commit -m "revert(map): drop RustMaps as a base-map source; keep client for #info"
```

---

### Task 2: `MapComposer.ComposeStaticAsync` (grid + monuments only)

**Files:**

- Modify: `src/RustPlusBot.Features.Map/Composing/MapComposer.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MapComposerTests.cs`

**Interfaces:**

- Produces: `Task<byte[]?> MapComposer.ComposeStaticAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)` — composes the Rust+ base with a fixed `MapLayerSet(Grid, Monuments)` (players/markers/vendor/rigs/trails off), ignoring per-server #map toggle settings. Null when no base map. Consumed by Task 7.

- [ ] **Step 1: Write the failing test**

Add to `MapComposerTests.cs` (mirror the file's existing arrange helpers for cache/query/events stubs; the key assertions are the two below):

```csharp
[Fact]
public async Task ComposeStaticAsync_ignores_markers_and_players()
{
    // Static compose must be invariant to live marker/player state (dynamic layers are OFF),
    // so the bytes are identical whether or not the stores hold markers/players.
    var withDynamic = BuildComposer(events: EventsWithCargo(), team: TeamWithPlayer());
    var withoutDynamic = BuildComposer(events: EmptyEvents(), team: null);

    var a = await withDynamic.ComposeStaticAsync(Guild, Server, CancellationToken.None);
    var b = await withoutDynamic.ComposeStaticAsync(Guild, Server, CancellationToken.None);

    Assert.NotNull(a);
    Assert.True(a!.AsSpan().SequenceEqual(b));
}

[Fact]
public async Task ComposeStaticAsync_draws_monuments_over_the_base()
{
    var composer = BuildComposer(monuments: [new MonumentSnapshot("launchsite", 2000f, 2000f)]);
    var withMonument = await composer.ComposeStaticAsync(Guild, Server, CancellationToken.None);

    var bare = BuildComposer(monuments: []);
    var withoutMonument = await bare.ComposeStaticAsync(Guild, Server, CancellationToken.None);

    Assert.NotNull(withMonument);
    Assert.False(withMonument!.AsSpan().SequenceEqual(withoutMonument!)); // monument icon changed pixels
}
```

If the file lacks `BuildComposer`/`EventsWithCargo`/`TeamWithPlayer`/`EmptyEvents` helpers, add thin ones over the existing NSubstitute stubs the other composer tests already build (the file already stubs `BaseMapCache` via a fake `IBaseMapSource`, `IRustServerQuery.GetMapDimensionsAsync` → `new MapDimensions(2000,2000,100,4000)`, `GetMonumentsAsync`, `IEventState.GetActiveMarkers`, `GetTeamInfoAsync`). Match those.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1 --filter ComposeStaticAsync`
Expected: FAIL — `ComposeStaticAsync` does not exist.

- [ ] **Step 3: Refactor `ComposeAsync` to share a core, add `ComposeStaticAsync`**

In `MapComposer`, add the fixed layer set and split the body. Keep `ComposeAsync`'s public signature; move everything from the `baseImage` fetch onward into a private core that takes an explicit `MapLayerSet`:

```csharp
/// <summary>The static #info layer set: terrain base + grid + monuments only (no dynamic overlays).</summary>
private static readonly MapLayerSet StaticLayers =
    new(Grid: true, Markers: false, Monuments: true, Vendor: false, Players: false, Rigs: false);

/// <summary>Composes the map PNG for a server using its saved layer toggles, or null when no base map.</summary>
/// <param name="guildId">The owning guild snowflake.</param>
/// <param name="serverId">The target server id.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>PNG bytes, or null.</returns>
public async Task<byte[]?> ComposeAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
{
    MapLayerSettings settings;
    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var store = scope.ServiceProvider.GetRequiredService<IMapSettingsStore>();
        settings = await store.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    }

    var layers = new MapLayerSet(settings.Grid, settings.Markers, settings.Monuments,
        settings.Vendor, settings.Players, settings.Rigs);
    return await ComposeWithLayersAsync(guildId, serverId, layers, cancellationToken).ConfigureAwait(false);
}

/// <summary>Composes a static #info map: terrain base + grid + monuments only, ignoring saved toggles.</summary>
/// <param name="guildId">The owning guild snowflake.</param>
/// <param name="serverId">The target server id.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>PNG bytes, or null when no base map is available.</returns>
public Task<byte[]?> ComposeStaticAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken) =>
    ComposeWithLayersAsync(guildId, serverId, StaticLayers, cancellationToken);

private async Task<byte[]?> ComposeWithLayersAsync(ulong guildId, Guid serverId, MapLayerSet layers,
    CancellationToken cancellationToken)
{
    var baseImage = await cache.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    if (baseImage is null)
    {
        return null;
    }

    var dims = await query.GetMapDimensionsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    if (dims is null || dims.WorldSize == 0)
    {
        return renderer.Render(baseImage.Bytes, new MapProjection(0, 1, 1, 0, MapRenderer.OutputSize),
            markers: [], monuments: [], players: [], rigs: [],
            new MapLayerSet(Grid: false, Markers: false, Monuments: false, Vendor: false, Players: false,
                Rigs: false));
    }

    var projection = new MapProjection(dims.WorldSize, baseImage.PixelWidth, baseImage.PixelHeight,
        baseImage.OceanMarginPx, MapRenderer.OutputSize);

    var markers = GatherMarkers(guildId, serverId, projection, layers);
    IReadOnlyList<MonumentSnapshot> serverMonuments = [];
    if (layers.Monuments || layers.Rigs)
    {
        serverMonuments = await query.GetMonumentsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    }

    var monuments = GatherMonuments(serverMonuments, projection, layers);
    var players = await GatherPlayersAsync(guildId, serverId, projection, layers, cancellationToken)
        .ConfigureAwait(false);
    var rigPlacements = GatherRigs(guildId, serverId, serverMonuments, projection, layers);

    return renderer.Render(baseImage.Bytes, projection, markers, monuments, players, rigPlacements, layers);
}
```

(The `GatherMarkers`/`GatherMonuments`/`GatherPlayersAsync`/`GatherRigs`/`ProjectTrail` methods are unchanged — they already gate on the passed `layers`.)

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1`
Expected: PASS (new + existing composer tests).

- [ ] **Step 5: Commit**

```bash
git add src tests
git commit -m "feat(map): MapComposer.ComposeStaticAsync (grid+monuments static render)"
```

---

### Task 3: RustMaps generation state + coordinator

**Files:**

- Create: `src/RustPlusBot.Features.Map/RustMaps/RustMapsGenerationState.cs`
- Create: `src/RustPlusBot.Features.Map/RustMaps/RustMapsMapKey.cs`
- Create: `src/RustPlusBot.Features.Map/RustMaps/RustMapsReadyMap.cs`
- Create: `src/RustPlusBot.Features.Map/RustMaps/RustMapsMapSnapshot.cs`
- Create: `src/RustPlusBot.Features.Map/RustMaps/IRustMapsMapCoordinator.cs`
- Create: `src/RustPlusBot.Features.Map/RustMaps/RustMapsMapCoordinator.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/RustMaps/RustMapsMapCoordinatorTests.cs`

**Interfaces:**

- Produces (consumed by Tasks 5, 7):
  - `enum RustMapsGenerationState { Idle, Generating, Ready, Failed, LimitReached }`
  - `sealed record RustMapsMapKey(int Size, int Seed)`
  - `sealed record RustMapsReadyMap(byte[] ImageBytes, string? RustMapsUrl)`
  - `sealed record RustMapsMapSnapshot(RustMapsGenerationState State, string? MapId, RustMapsReadyMap? Ready)`
  - `interface IRustMapsMapCoordinator`:
    - `void Register(RustMapsMapKey key, ulong guildId, Guid serverId)`
    - `RustMapsMapSnapshot Snapshot(RustMapsMapKey key)` (returns `Idle` snapshot for an unseen key)
    - `bool TrySetGenerating(RustMapsMapKey key, string? mapId)`
    - `void SetReady(RustMapsMapKey key, RustMapsReadyMap ready)`
    - `void SetFailed(RustMapsMapKey key)`
    - `void SetLimitReached(RustMapsMapKey key)`
    - `IReadOnlyList<RustMapsMapKey> PendingKeys()` (keys with requesters in `Idle` or `Generating`)
    - `IReadOnlyList<(ulong Guild, Guid Server)> Requesters(RustMapsMapKey key)`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/RustPlusBot.Features.Map.Tests/RustMaps/RustMapsMapCoordinatorTests.cs
using RustPlusBot.Features.Map.RustMaps;

namespace RustPlusBot.Features.Map.Tests.RustMaps;

public sealed class RustMapsMapCoordinatorTests
{
    private static readonly RustMapsMapKey Key = new(4000, 12345);
    private static readonly Guid Server = Guid.NewGuid();

    [Fact]
    public void Unseen_key_snapshots_as_idle()
    {
        var c = new RustMapsMapCoordinator();
        Assert.Equal(RustMapsGenerationState.Idle, c.Snapshot(Key).State);
    }

    [Fact]
    public void Register_is_idempotent_and_accumulates_requesters()
    {
        var c = new RustMapsMapCoordinator();
        var s2 = Guid.NewGuid();
        c.Register(Key, 1UL, Server);
        c.Register(Key, 1UL, Server);   // same requester again
        c.Register(Key, 1UL, s2);       // second server, same (size,seed)

        Assert.Equal(2, c.Requesters(Key).Count);
        Assert.Contains(Key, c.PendingKeys());
    }

    [Fact]
    public void Lifecycle_transitions_and_ready_caches_the_image()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        Assert.True(c.TrySetGenerating(Key, "map-1"));
        Assert.Equal(RustMapsGenerationState.Generating, c.Snapshot(Key).State);
        Assert.Equal("map-1", c.Snapshot(Key).MapId);

        c.SetReady(Key, new RustMapsReadyMap([1, 2, 3], "https://rustmaps/x"));
        var snap = c.Snapshot(Key);
        Assert.Equal(RustMapsGenerationState.Ready, snap.State);
        Assert.Equal([1, 2, 3], snap.Ready!.ImageBytes);
        Assert.Equal("https://rustmaps/x", snap.Ready.RustMapsUrl);
    }

    [Fact]
    public void Terminal_states_are_not_pending_and_are_not_reset_by_register()
    {
        var c = new RustMapsMapCoordinator();
        c.Register(Key, 1UL, Server);
        c.SetFailed(Key);
        c.Register(Key, 1UL, Server);   // must not revive

        Assert.Equal(RustMapsGenerationState.Failed, c.Snapshot(Key).State);
        Assert.DoesNotContain(Key, c.PendingKeys());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1 --filter RustMapsMapCoordinatorTests`
Expected: FAIL — types don't exist.

- [ ] **Step 3: Implement the types**

```csharp
// RustMapsGenerationState.cs
namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>Lifecycle of a RustMaps map for one (size, seed). Ready/Failed/LimitReached are terminal for the wipe.</summary>
public enum RustMapsGenerationState
{
    /// <summary>Not yet checked/started.</summary>
    Idle,

    /// <summary>Generation requested; polling for completion.</summary>
    Generating,

    /// <summary>The RustMaps render is available and cached.</summary>
    Ready,

    /// <summary>Generation failed; no retry this wipe.</summary>
    Failed,

    /// <summary>Generation skipped because the RustMaps credit limit is reached.</summary>
    LimitReached,
}
```

```csharp
// RustMapsMapKey.cs
namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>Identity of a RustMaps procedural map, shared by every server on a wipe.</summary>
/// <param name="Size">The world size.</param>
/// <param name="Seed">The map seed.</param>
public sealed record RustMapsMapKey(int Size, int Seed);
```

```csharp
// RustMapsReadyMap.cs
namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>A generated RustMaps map ready to post: the downloaded image plus its RustMaps page link.</summary>
/// <param name="ImageBytes">The downloaded RustMaps render bytes.</param>
/// <param name="RustMapsUrl">The RustMaps page URL, when available.</param>
public sealed record RustMapsReadyMap(byte[] ImageBytes, string? RustMapsUrl);
```

```csharp
// RustMapsMapSnapshot.cs
namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>An immutable view of a map key's generation progress.</summary>
/// <param name="State">The current lifecycle state.</param>
/// <param name="MapId">The RustMaps map id once assigned, else null.</param>
/// <param name="Ready">The ready image when <see cref="State"/> is <see cref="RustMapsGenerationState.Ready"/>.</param>
public sealed record RustMapsMapSnapshot(RustMapsGenerationState State, string? MapId, RustMapsReadyMap? Ready);
```

```csharp
// IRustMapsMapCoordinator.cs
namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>Tracks RustMaps generation state per (size, seed) and the servers awaiting each map. Thread-safe singleton.</summary>
public interface IRustMapsMapCoordinator
{
    /// <summary>Registers a server's interest in a map key (idempotent; never revives a terminal state).</summary>
    void Register(RustMapsMapKey key, ulong guildId, Guid serverId);

    /// <summary>Gets an immutable snapshot of a key (an Idle snapshot for an unseen key).</summary>
    RustMapsMapSnapshot Snapshot(RustMapsMapKey key);

    /// <summary>Moves a key to Generating with its map id; returns false if it was already terminal.</summary>
    bool TrySetGenerating(RustMapsMapKey key, string? mapId);

    /// <summary>Marks a key Ready and caches its image.</summary>
    void SetReady(RustMapsMapKey key, RustMapsReadyMap ready);

    /// <summary>Marks a key Failed (no retry this wipe).</summary>
    void SetFailed(RustMapsMapKey key);

    /// <summary>Marks a key LimitReached (credits exhausted; no spend).</summary>
    void SetLimitReached(RustMapsMapKey key);

    /// <summary>Keys with at least one requester still in Idle or Generating.</summary>
    IReadOnlyList<RustMapsMapKey> PendingKeys();

    /// <summary>The (guild, server) pairs awaiting a key.</summary>
    IReadOnlyList<(ulong Guild, Guid Server)> Requesters(RustMapsMapKey key);
}
```

```csharp
// RustMapsMapCoordinator.cs
using System.Collections.Concurrent;

namespace RustPlusBot.Features.Map.RustMaps;

/// <inheritdoc />
public sealed class RustMapsMapCoordinator : IRustMapsMapCoordinator
{
    private readonly ConcurrentDictionary<RustMapsMapKey, Entry> _byKey = new();

    /// <inheritdoc />
    public void Register(RustMapsMapKey key, ulong guildId, Guid serverId)
    {
        var entry = _byKey.GetOrAdd(key, static _ => new Entry());
        lock (entry.Gate)
        {
            entry.Requesters.Add((guildId, serverId));
        }
    }

    /// <inheritdoc />
    public RustMapsMapSnapshot Snapshot(RustMapsMapKey key)
    {
        if (!_byKey.TryGetValue(key, out var entry))
        {
            return new RustMapsMapSnapshot(RustMapsGenerationState.Idle, null, null);
        }

        lock (entry.Gate)
        {
            return new RustMapsMapSnapshot(entry.State, entry.MapId, entry.Ready);
        }
    }

    /// <inheritdoc />
    public bool TrySetGenerating(RustMapsMapKey key, string? mapId)
    {
        var entry = _byKey.GetOrAdd(key, static _ => new Entry());
        lock (entry.Gate)
        {
            if (entry.State is RustMapsGenerationState.Ready or RustMapsGenerationState.Failed
                or RustMapsGenerationState.LimitReached)
            {
                return false;
            }

            entry.State = RustMapsGenerationState.Generating;
            entry.MapId = mapId;
            return true;
        }
    }

    /// <inheritdoc />
    public void SetReady(RustMapsMapKey key, RustMapsReadyMap ready)
    {
        ArgumentNullException.ThrowIfNull(ready);
        var entry = _byKey.GetOrAdd(key, static _ => new Entry());
        lock (entry.Gate)
        {
            entry.State = RustMapsGenerationState.Ready;
            entry.Ready = ready;
        }
    }

    /// <inheritdoc />
    public void SetFailed(RustMapsMapKey key) => SetTerminal(key, RustMapsGenerationState.Failed);

    /// <inheritdoc />
    public void SetLimitReached(RustMapsMapKey key) => SetTerminal(key, RustMapsGenerationState.LimitReached);

    /// <inheritdoc />
    public IReadOnlyList<RustMapsMapKey> PendingKeys()
    {
        var pending = new List<RustMapsMapKey>();
        foreach (var (key, entry) in _byKey)
        {
            lock (entry.Gate)
            {
                if (entry.Requesters.Count > 0
                    && entry.State is RustMapsGenerationState.Idle or RustMapsGenerationState.Generating)
                {
                    pending.Add(key);
                }
            }
        }

        return pending;
    }

    /// <inheritdoc />
    public IReadOnlyList<(ulong Guild, Guid Server)> Requesters(RustMapsMapKey key)
    {
        if (!_byKey.TryGetValue(key, out var entry))
        {
            return [];
        }

        lock (entry.Gate)
        {
            return [.. entry.Requesters];
        }
    }

    private void SetTerminal(RustMapsMapKey key, RustMapsGenerationState state)
    {
        var entry = _byKey.GetOrAdd(key, static _ => new Entry());
        lock (entry.Gate)
        {
            entry.State = state;
        }
    }

    private sealed class Entry
    {
        public object Gate { get; } = new();
        public RustMapsGenerationState State { get; set; } = RustMapsGenerationState.Idle;
        public string? MapId { get; set; }
        public RustMapsReadyMap? Ready { get; set; }
        public HashSet<(ulong Guild, Guid Server)> Requesters { get; } = [];
    }
}
```

- [ ] **Step 4: Run tests + commit**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1 --filter RustMapsMapCoordinatorTests`
Expected: PASS.

```bash
git add src tests
git commit -m "feat(map): RustMaps generation coordinator (per size+seed state machine)"
```

---

### Task 4: #info channel locator + info-map poster

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Locating/IInfoChannelLocator.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/InfoChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs` (register locator)
- Create: `src/RustPlusBot.Features.Map/Posting/IInfoMapPoster.cs`
- Create: `src/RustPlusBot.Features.Map/Posting/DiscordInfoMapPoster.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Locating/InfoChannelLocatorTests.cs` (only if the file's sibling locators have tests to mirror; otherwise skip — see Step 1)

**Interfaces:**

- Consumes: `CachingChannelLocator(IServiceScopeFactory, IClock, string channelKey)`, `WorkspaceChannelKeys.ServerInfo`.
- Produces (consumed by Task 7):
  - `interface IInfoChannelLocator { Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken); }`
  - `interface IInfoMapPoster { Task PostAsync(ulong channelId, Discord.Embed embed, byte[] pngBytes, CancellationToken cancellationToken); }`

- [ ] **Step 1: Locator (mirror `MapChannelLocator` exactly)**

```csharp
// IInfoChannelLocator.cs
namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the per-server #info channel id (for posting the static map image).</summary>
public interface IInfoChannelLocator
{
    /// <summary>Gets the #info Discord channel id for (guild, server), or null if not provisioned.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The channel id, or null.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

```csharp
// InfoChannelLocator.cs
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the #info channel id for a (guild, server).</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class InfoChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : CachingChannelLocator(scopeFactory, clock, WorkspaceChannelKeys.ServerInfo), IInfoChannelLocator;
```

Register in `WorkspaceServiceCollectionExtensions` beside the other locators (line ~65):

```csharp
services.AddSingleton<IInfoChannelLocator, InfoChannelLocator>();
```

Test only if `tests/RustPlusBot.Features.Workspace.Tests/Locating/` already tests a sibling locator — if so, mirror it for `InfoChannelLocator` (resolve the "info" channel, null when unprovisioned). If sibling locators have NO tests (they're thin `CachingChannelLocator` subclasses), skip the locator test and note it in the report — do not invent a test pattern the codebase doesn't use.

- [ ] **Step 2: Poster (mirror `DiscordMapChannelPoster`, add an embed param)**

```csharp
// IInfoMapPoster.cs
using Discord;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Posts the static map image + embed to #info, replacing the bot's prior image post.</summary>
internal interface IInfoMapPoster
{
    /// <summary>Deletes the bot's prior #info image post (if any) and posts the new embed + PNG.</summary>
    /// <param name="channelId">The #info channel id.</param>
    /// <param name="embed">The map embed (title, size/seed, RustMaps link).</param>
    /// <param name="pngBytes">The map image PNG.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the post is issued.</returns>
    Task PostAsync(ulong channelId, Embed embed, byte[] pngBytes, CancellationToken cancellationToken);
}
```

```csharp
// DiscordInfoMapPoster.cs — mirror DiscordMapChannelPoster verbatim, but attach an embed and
// key the image name "map.png" so DeletePriorBotMessagesAsync's "own attachment posts" scan
// leaves the reconciled #info status embed (no attachment) intact.
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Map.Posting;

/// <summary>Posts the static map image to #info by deleting the bot's prior image post and reposting. Untested integration shim.</summary>
/// <param name="client">The Discord socket client.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordInfoMapPoster(
    DiscordSocketClient client,
    ILogger<DiscordInfoMapPoster> logger) : IInfoMapPoster
{
    private const int RecentMessageScan = 10;

    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, Embed embed, byte[] pngBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(embed);
        ArgumentNullException.ThrowIfNull(pngBytes);
        try
        {
            var options = new RequestOptions { CancelToken = cancellationToken };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false) is not ITextChannel channel)
            {
                return;
            }

            try
            {
                await DeletePriorBotMessagesAsync(channel, options).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Broad catch: a failed delete must not abort the repost (best-effort).
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDeleteFailed(logger, ex, channelId);
            }

            var stream = new MemoryStream(pngBytes);
            await using (stream.ConfigureAwait(false))
            {
                await channel.SendFileAsync(stream, "map.png", embed: embed, options: options,
                    allowedMentions: AllowedMentions.None).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the info-map loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, channelId);
        }
    }

    private async Task DeletePriorBotMessagesAsync(ITextChannel channel, RequestOptions options)
    {
        var batch = await channel.GetMessagesAsync(RecentMessageScan, options: options).FlattenAsync()
            .ConfigureAwait(false);
        // Delete only our prior IMAGE posts (they carry a file attachment); leave the reconciled
        // #info status embed (no attachment) for the workspace reconciler.
        foreach (var message in batch.Where(m => m.Author.Id == client.CurrentUser.Id && m.Attachments.Count > 0))
        {
            await message.DeleteAsync(options).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Posting the #info map image to channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Deleting prior #info map messages in channel {ChannelId} failed; reposting anyway.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, ulong channelId);
}
```

- [ ] **Step 3: Verify the reconciler does not prune the image post (COEXISTENCE GATE)**

Read `src/RustPlusBot.Features.Workspace/Reconciler/WorkspaceReconciler.cs`. Confirm it edits/manages only messages tracked by `ProvisionedMessage.DiscordMessageId` and does NOT delete "unknown" messages in a channel (this is how the #map control message coexists with the #map image post). If it DOES prune unknown messages, STOP and report — the design assumed the #map coexistence guarantee holds for #info too. Record the finding in the report either way.

- [ ] **Step 4: Build + test + commit**

Run:

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
dotnet test tests/RustPlusBot.Features.Workspace.Tests -maxcpucount:1
```

Expected: clean build; Workspace tests green.

```bash
git add -A src tests
git commit -m "feat(map): #info channel locator + info-map attachment poster"
```

---

### Task 5: RustMaps generation driver (credit-safe advance)

**Files:**

- Create: `src/RustPlusBot.Features.Map/RustMaps/RustMapsGenerationDriver.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/RustMaps/RustMapsGenerationDriverTests.cs`

**Interfaces:**

- Consumes: `IRustMapsMapCoordinator` (Task 3); `RustMapsApi.V4.IRustMapsClient` (`GetMapBySeedAndSizeAsync(int,int,bool,CancellationToken)`, `GetMapByIdAsync(string,CancellationToken)`, `GetLimitsAsync(string?,CancellationToken)`, `CreateMapAsync(MapGenerationRequest,CancellationToken)`); `RustMapsApi.Results.Result<T>` (`IsSuccess`, `Data`, `Error`); `RustMapsApi.Results.RustMapsError.Kind`; `RustMapsApi.Results.RustMapsErrorKind.Queued`; `RustMapsApi.V4.Models.MapInfo` (`ImageUrl`, `Url`); `RustMapsApi.V4.Models.MapGenerationStatus.MapId`; `RustMapsApi.V4.Models.MapGenLimits` (`Concurrent`, `Monthly` → `MapGenerationStat(Current, Allowed)`); `RustMapsApi.V4.Requests.MapGenerationRequest { Size, Seed, Staging }`.
- Produces (consumed by Task 7): `RustMapsGenerationDriver` with `const string HttpClientName = "RustMapsImages";` and `Task AdvanceAsync(RustMapsMapKey key, CancellationToken cancellationToken)`.

**Design note:** "ready" is signalled by a successful GET whose `MapInfo.ImageUrl` is non-null (a map still generating returns failure or a null image URL). We never inspect `MapState` (it lives on `MapGenerationStatus`, not `MapInfo`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/RustPlusBot.Features.Map.Tests/RustMaps/RustMapsGenerationDriverTests.cs
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustMapsApi.Results;
using RustMapsApi.V4;
using RustMapsApi.V4.Models;
using RustMapsApi.V4.Requests;
using RustPlusBot.Features.Map.RustMaps;

namespace RustPlusBot.Features.Map.Tests.RustMaps;

public sealed class RustMapsGenerationDriverTests
{
    private static readonly RustMapsMapKey Key = new(4000, 12345);
    private static readonly Guid Server = Guid.NewGuid();

    private sealed class StubHandler(HttpStatusCode status, byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static RustMapsError Error(RustMapsErrorKind kind) => new(kind, null, null, null);

    private static (RustMapsGenerationDriver Driver, IRustMapsClient Client, RustMapsMapCoordinator Coord) Build(
        HttpStatusCode imageStatus = HttpStatusCode.OK)
    {
        var client = Substitute.For<IRustMapsClient>();
        var coord = new RustMapsMapCoordinator();
        var factory = new StubFactory(new StubHandler(imageStatus, [7, 7, 7]));
        var driver = new RustMapsGenerationDriver(client, coord, factory,
            NullLogger<RustMapsGenerationDriver>.Instance);
        return (driver, client, coord);
    }

    [Fact]
    public async Task Existing_map_goes_straight_to_ready_without_generating()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        client.GetMapBySeedAndSizeAsync(4000, 12345, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(
                new MapInfo { ImageUrl = "https://img/x.png", Url = "https://rustmaps/x" }, 200));

        await driver.AdvanceAsync(Key, CancellationToken.None);

        Assert.Equal(RustMapsGenerationState.Ready, coord.Snapshot(Key).State);
        Assert.Equal([7, 7, 7], coord.Snapshot(Key).Ready!.ImageBytes);
        await client.DidNotReceiveWithAnyArgs().CreateMapAsync(default!, default);
    }

    [Fact]
    public async Task NotFound_with_budget_generates_exactly_once_across_ticks()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        client.GetMapBySeedAndSizeAsync(4000, 12345, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Failure(Error(RustMapsErrorKind.NotFound), 404));
        client.GetLimitsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<MapGenLimits>.Success(
                new MapGenLimits { Concurrent = new(0, 5), Monthly = new(3, 100) }, 200));
        client.CreateMapAsync(Arg.Any<MapGenerationRequest>(), Arg.Any<CancellationToken>())
            .Returns(Result<MapGenerationStatus>.Success(new MapGenerationStatus { MapId = "map-1" }, 200));

        await driver.AdvanceAsync(Key, CancellationToken.None);   // Idle → Generating
        await driver.AdvanceAsync(Key, CancellationToken.None);   // Generating → poll (still not ready)

        Assert.Equal(RustMapsGenerationState.Generating, coord.Snapshot(Key).State);
        await client.Received(1).CreateMapAsync(
            Arg.Is<MapGenerationRequest>(r => r.Size == 4000 && r.Seed == 12345 && !r.Staging),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Limit_reached_never_generates()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        client.GetMapBySeedAndSizeAsync(4000, 12345, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Failure(Error(RustMapsErrorKind.NotFound), 404));
        client.GetLimitsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<MapGenLimits>.Success(
                new MapGenLimits { Concurrent = new(5, 5), Monthly = new(3, 100) }, 200)); // concurrent exhausted

        await driver.AdvanceAsync(Key, CancellationToken.None);

        Assert.Equal(RustMapsGenerationState.LimitReached, coord.Snapshot(Key).State);
        await client.DidNotReceiveWithAnyArgs().CreateMapAsync(default!, default);
    }

    [Fact]
    public async Task Limits_call_failing_fails_closed_no_generate()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        client.GetMapBySeedAndSizeAsync(4000, 12345, false, Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Failure(Error(RustMapsErrorKind.NotFound), 404));
        client.GetLimitsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Result<MapGenLimits>.Failure(Error(RustMapsErrorKind.Transport), 503));

        await driver.AdvanceAsync(Key, CancellationToken.None);

        Assert.Equal(RustMapsGenerationState.Failed, coord.Snapshot(Key).State);
        await client.DidNotReceiveWithAnyArgs().CreateMapAsync(default!, default);
    }

    [Fact]
    public async Task Poll_reaches_ready_and_downloads_the_image()
    {
        var (driver, client, coord) = Build();
        coord.Register(Key, 1UL, Server);
        coord.TrySetGenerating(Key, "map-1");
        client.GetMapByIdAsync("map-1", Arg.Any<CancellationToken>())
            .Returns(Result<MapInfo>.Success(
                new MapInfo { ImageUrl = "https://img/x.png", Url = "https://rustmaps/x" }, 200));

        await driver.AdvanceAsync(Key, CancellationToken.None);

        Assert.Equal(RustMapsGenerationState.Ready, coord.Snapshot(Key).State);
        Assert.Equal([7, 7, 7], coord.Snapshot(Key).Ready!.ImageBytes);
    }
}
```

(When writing, verify `MapGenLimits`/`MapGenerationStatus`/`MapInfo` are settable-init records as used here; the RustMapsApi models are `record`s with `init` setters — confirmed for `MapInfo`/`MapGenerationStatus`/`MapGenLimits`. `MapGenerationStat` is a positional record `new(Current, Allowed)`.)

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1 --filter RustMapsGenerationDriverTests`
Expected: FAIL — `RustMapsGenerationDriver` does not exist.

- [ ] **Step 3: Implement**

```csharp
// RustMapsGenerationDriver.cs
using Microsoft.Extensions.Logging;
using RustMapsApi.Results;
using RustMapsApi.V4;
using RustMapsApi.V4.Models;
using RustMapsApi.V4.Requests;

namespace RustPlusBot.Features.Map.RustMaps;

/// <summary>
/// Advances one RustMaps map key's generation one step: GET → (limits-gated) CreateMap → poll → download.
/// Credit-safe: CreateMap only on a genuine NotFound with confirmed budget, once per key, no retry.
/// </summary>
/// <param name="client">The RustMaps API client.</param>
/// <param name="coordinator">The shared generation state.</param>
/// <param name="httpClientFactory">Creates the image-download client.</param>
/// <param name="logger">The logger.</param>
public sealed partial class RustMapsGenerationDriver(
    IRustMapsClient client,
    IRustMapsMapCoordinator coordinator,
    IHttpClientFactory httpClientFactory,
    ILogger<RustMapsGenerationDriver> logger)
{
    /// <summary>Named HTTP client used to download the RustMaps render.</summary>
    public const string HttpClientName = "RustMapsImages";

    /// <summary>Advances the key one step based on its current state.</summary>
    /// <param name="key">The map key.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the step is done.</returns>
    public async Task AdvanceAsync(RustMapsMapKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        try
        {
            var snapshot = coordinator.Snapshot(key);
            switch (snapshot.State)
            {
                case RustMapsGenerationState.Idle:
                    await StartAsync(key, cancellationToken).ConfigureAwait(false);
                    break;
                case RustMapsGenerationState.Generating:
                    await PollAsync(key, snapshot.MapId, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    break; // Ready / Failed / LimitReached — terminal.
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: any RustMaps failure marks the key Failed; the #info fallback still renders.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogAdvanceFailed(logger, ex, key.Size, key.Seed);
            coordinator.SetFailed(key);
        }
    }

    private async Task StartAsync(RustMapsMapKey key, CancellationToken cancellationToken)
    {
        var get = await client.GetMapBySeedAndSizeAsync(key.Size, key.Seed, staging: false, cancellationToken)
            .ConfigureAwait(false);
        if (get.IsSuccess && get.Data?.ImageUrl is not null)
        {
            await SetReadyAsync(key, get.Data, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (get.Error?.Kind == RustMapsErrorKind.Queued)
        {
            // Already generating (elsewhere/earlier) — poll without spending.
            coordinator.TrySetGenerating(key, mapId: null);
            return;
        }

        // Genuine miss → pre-check limits (fail closed) before spending a credit.
        var limits = await client.GetLimitsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!limits.IsSuccess || limits.Data is null)
        {
            LogLimitsUnavailable(logger, key.Size, key.Seed);
            coordinator.SetFailed(key);
            return;
        }

        if (IsExhausted(limits.Data.Concurrent) || IsExhausted(limits.Data.Monthly))
        {
            LogLimitReached(logger, key.Size, key.Seed);
            coordinator.SetLimitReached(key);
            return;
        }

        var create = await client.CreateMapAsync(
                new MapGenerationRequest { Size = key.Size, Seed = key.Seed, Staging = false }, cancellationToken)
            .ConfigureAwait(false);
        if (!create.IsSuccess)
        {
            LogCreateFailed(logger, key.Size, key.Seed, create.StatusCode);
            coordinator.SetFailed(key);
            return;
        }

        coordinator.TrySetGenerating(key, create.Data?.MapId);
    }

    private async Task PollAsync(RustMapsMapKey key, string? mapId, CancellationToken cancellationToken)
    {
        var get = mapId is { } id
            ? await client.GetMapByIdAsync(id, cancellationToken).ConfigureAwait(false)
            : await client.GetMapBySeedAndSizeAsync(key.Size, key.Seed, staging: false, cancellationToken)
                .ConfigureAwait(false);
        if (get.IsSuccess && get.Data?.ImageUrl is not null)
        {
            await SetReadyAsync(key, get.Data, cancellationToken).ConfigureAwait(false);
        }
        // else: still generating — leave Generating, poll again next tick.
    }

    private async Task SetReadyAsync(RustMapsMapKey key, MapInfo info, CancellationToken cancellationToken)
    {
        var http = httpClientFactory.CreateClient(HttpClientName);
        var bytes = await http.GetByteArrayAsync(new Uri(info.ImageUrl!), cancellationToken).ConfigureAwait(false);
        coordinator.SetReady(key, new RustMapsReadyMap(bytes, info.Url));
    }

    private static bool IsExhausted(MapGenerationStat? stat) => stat is { } s && s.Current >= s.Allowed;

    [LoggerMessage(Level = LogLevel.Warning, Message = "RustMaps advance failed for size {Size} seed {Seed}.")]
    private static partial void LogAdvanceFailed(ILogger logger, Exception exception, int size, int seed);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RustMaps limits unavailable for size {Size} seed {Seed}; skipping generation (fail closed).")]
    private static partial void LogLimitsUnavailable(ILogger logger, int size, int seed);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "RustMaps credit limit reached; skipping generation for size {Size} seed {Seed}.")]
    private static partial void LogLimitReached(ILogger logger, int size, int seed);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RustMaps CreateMap failed for size {Size} seed {Seed} (status {StatusCode}).")]
    private static partial void LogCreateFailed(ILogger logger, int size, int seed, int statusCode);
}
```

- [ ] **Step 4: Run tests + commit**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1 --filter RustMapsGenerationDriverTests`
Expected: PASS.

```bash
git add src tests
git commit -m "feat(map): credit-safe RustMaps generation driver (limits fail-closed, one-shot)"
```

---

### Task 6: `MapOptions.RustMaps.GenerationPollInterval` + config

**Files:**

- Modify: `src/RustPlusBot.Features.Map/MapOptions.cs`
- Modify: `src/RustPlusBot.Host/appsettings.json`
- Modify: `src/RustPlusBot.Host/Program.cs` (options validation — locate the existing `AddOptions<MapOptions>()` block)
- Test: `tests/RustPlusBot.Features.Map.Tests/MapOptionsTests.cs` (create if absent)

**Interfaces:**

- Produces (consumed by Task 7): `MapOptions.RustMaps` of type `RustMapsOptions { TimeSpan GenerationPollInterval }` (default `00:00:20`).

- [ ] **Step 1: Implement options**

```csharp
// MapOptions.cs
namespace RustPlusBot.Features.Map;

/// <summary>Map feature configuration, bound from the "Map" config section.</summary>
public sealed class MapOptions
{
    /// <summary>Minimum time between #map image re-renders per server (coalesces rapid marker changes). Default 30s.</summary>
    public TimeSpan MapRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>RustMaps integration settings.</summary>
    public RustMapsOptions RustMaps { get; set; } = new();
}

/// <summary>RustMaps settings. The integration is inactive when no API key is configured.</summary>
public sealed class RustMapsOptions
{
    /// <summary>How often the background service checks generation progress. Default 20s.</summary>
    public TimeSpan GenerationPollInterval { get; set; } = TimeSpan.FromSeconds(20);
}
```

(Note: the API key is read directly via `configuration["Map:RustMaps:ApiKey"]` in `AddMap`; it is intentionally NOT a bound property. `RustMapsOptions` carries only the poll interval.)

- [ ] **Step 2: appsettings + validation**

`appsettings.json` "Map" section:

```json
"Map": {
  "MapRefreshInterval": "00:00:30",
  "RustMaps": {
    "ApiKey": "",
    "GenerationPollInterval": "00:00:20"
  }
}
```

In `Program.cs`, extend the existing `AddOptions<MapOptions>()` validation chain with:

```csharp
.Validate(static o => o.RustMaps.GenerationPollInterval > TimeSpan.Zero,
    "Map:RustMaps:GenerationPollInterval must be positive.")
```

- [ ] **Step 3: Test**

```csharp
// MapOptionsTests.cs
using RustPlusBot.Features.Map;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MapOptionsTests
{
    [Fact]
    public void Defaults_are_sane()
    {
        var options = new MapOptions();
        Assert.Equal(TimeSpan.FromSeconds(30), options.MapRefreshInterval);
        Assert.NotNull(options.RustMaps);
        Assert.Equal(TimeSpan.FromSeconds(20), options.RustMaps.GenerationPollInterval);
    }
}
```

- [ ] **Step 4: Build + test + commit**

Run: `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1 && dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1 --filter MapOptionsTests`
Expected: clean; PASS.

```bash
git add src tests
git commit -m "feat(map): RustMaps generation poll-interval option + config"
```

---

### Task 7: `InfoMapHostedService` + wiring + localization

**Files:**

- Create: `src/RustPlusBot.Features.Map/Hosting/InfoMapHostedService.cs`
- Modify: `src/RustPlusBot.Features.Map/MapServiceCollectionExtensions.cs` (register RustMaps components when key present)
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `src/RustPlusBot.Localization/Strings.fr.resx` (embed strings)
- Test: `tests/RustPlusBot.Features.Map.Tests/Hosting/InfoMapServiceTests.cs`
- Modify: `tests/RustPlusBot.Features.Map.Tests/MapRegistrationTests.cs` (RustMaps components register iff key)

**Interfaces:**

- Consumes: `IRustMapsMapCoordinator` (T3), `RustMapsGenerationDriver` (+ `HttpClientName`) (T5), `MapComposer.ComposeStaticAsync` (T2), `IInfoMapPoster` (T4), `IInfoChannelLocator` (T4), `MapOptions.RustMaps.GenerationPollInterval` (T6), `IRustServerQuery.GetWorldAsync` (returns `WorldSnapshot(uint WorldSize, uint Seed)`), `IEventBus.SubscribeAsync<ConnectionStatusChangedEvent>`, `IConnectionStore.GetStateAsync`, `ILocalizer.Get`, `IWorkspaceStore.GetCultureAsync`.

**Design:** Mirror `MapHostedService`'s shape (a `_connected` set built from `ConnectionStatusChangedEvent`, a status loop, and a periodic loop; `IClock` not needed — no throttle). The service owns a per-server "last posted variant" so it posts on state change only (no per-tick churn).

- [ ] **Step 1: Write the failing test (state → post-variant orchestration)**

Test the orchestration seam via a fake poster + coordinator + a stub `MapComposer` seam. Because `MapComposer` is a concrete class, inject a small `IStaticMapComposer` wrapper? No — keep it simple: the service depends on `MapComposer` directly, and the test builds a real `MapComposer` over NSubstitute stubs (as `MapComposerTests` does) OR the test targets the extracted `EnsureInfoMapAsync` logic. To keep the service testable without Discord, extract the decision into a public method `Task EnsureInfoMapAsync(ulong guildId, Guid serverId, CancellationToken ct)` and test THAT:

```csharp
// tests/RustPlusBot.Features.Map.Tests/Hosting/InfoMapServiceTests.cs
// Arrange a coordinator pre-seeded Ready for the server's (size,seed); a fake IInfoMapPoster
// capturing (channelId, embed, bytes); a fake IInfoChannelLocator → 123UL; IRustServerQuery
// .GetWorldAsync → new WorldSnapshot(4000, 12345). Call EnsureInfoMapAsync twice.
// Assert: poster.PostAsync received ONCE (Ready posted; second call is a no-op — variant unchanged)
// with the coordinator's cached RustMaps bytes.
//
// Second test: coordinator Idle/Failed for the key + ComposeStaticAsync (real composer over stubs)
// returns non-null → poster receives the FALLBACK bytes once; a repeat call does not repost.
//
// Third test: locator returns null → poster never called.
```

Write these concretely against the fakes (mirror `MapComposerTests` for the composer stubs and `RustMapsGenerationDriverTests` for the coordinator). Assert `PostAsync` call counts and which byte[] was posted (Ready→coordinator bytes; else→ComposeStatic bytes).

**Culture-scope faking:** `EnsureInfoMapAsync` resolves the guild culture via `scopeFactory.CreateAsyncScope() → GetRequiredService<IWorkspaceStore>().GetCultureAsync(...)` — the SAME pattern as `EventRelay.GetCultureAsync`. Reuse `EventRelayTests`' fake for the `IServiceScopeFactory → scope → IWorkspaceStore.GetCultureAsync → "en"` chain (read `tests/RustPlusBot.Features.Events.Tests/Relaying/EventRelayTests.cs` for the exact NSubstitute wiring and copy it). Build the service under test with `NullLogger<InfoMapHostedService>.Instance`, `Options.Create(new MapOptions())`, an `ILocalizer` stub returning the key (or a real `ResxLocalizer`), a real `MapComposer` over the `MapComposerTests` stubs (for the fallback path), and NSubstitute fakes for `IInfoMapPoster`/`IInfoChannelLocator`/`IRustServerQuery`/`IEventBus`. Call `EnsureInfoMapAsync` directly (do NOT start the hosted loops).

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1 --filter InfoMapServiceTests`
Expected: FAIL — `InfoMapHostedService` does not exist.

- [ ] **Step 3: Implement the service**

```csharp
// InfoMapHostedService.cs
using System.Collections.Concurrent;
using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Features.Map.Composing;
using RustPlusBot.Features.Map.Posting;
using RustPlusBot.Features.Map.RustMaps;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Map.Hosting;

/// <summary>Posts the static RustMaps map to #info: a bot-rendered grid+monuments fallback until the
/// RustMaps render is generated, then the RustMaps image. Credit-safe generation via the driver.</summary>
internal sealed partial class InfoMapHostedService(
    IEventBus eventBus,
    IRustMapsMapCoordinator coordinator,
    RustMapsGenerationDriver driver,
    MapComposer composer,
    IInfoMapPoster poster,
    IInfoChannelLocator locator,
    IRustServerQuery query,
    ILocalizer localizer,
    IOptions<MapOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<InfoMapHostedService> logger) : IHostedService, IDisposable
{
    private enum Posted { None, Fallback, Ready }

    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), Posted> _posted = new();
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), byte> _connected = new();
    private readonly CancellationTokenSource _cts = new();
    private Task? _statusLoop;
    private Task? _tickLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _statusLoop = Task.Run(() => ConsumeConnectionStatusAsync(_cts.Token), CancellationToken.None);
        _tickLoop = Task.Run(() => RunTickAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[] { _statusLoop, _tickLoop }.Where(t => t is not null))
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

    /// <summary>Ensures #info shows the correct map image for the server's current generation state.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the check (and any post) is done.</returns>
    public async Task EnsureInfoMapAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var world = await query.GetWorldAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (world is null)
        {
            return;
        }

        var key = new RustMapsMapKey((int)world.WorldSize, (int)world.Seed);
        coordinator.Register(key, guildId, serverId);

        var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (channelId is not { } id)
        {
            return;
        }

        var snapshot = coordinator.Snapshot(key);
        var mapKey = (guildId, serverId);

        if (snapshot is { State: RustMapsGenerationState.Ready, Ready: { } ready })
        {
            if (_posted.TryGetValue(mapKey, out var v) && v == Posted.Ready)
            {
                return;
            }

            var culture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
            await poster.PostAsync(id, BuildEmbed(key, ready.RustMapsUrl, culture), ready.ImageBytes, cancellationToken)
                .ConfigureAwait(false);
            _posted[mapKey] = Posted.Ready;
            return;
        }

        if (_posted.TryGetValue(mapKey, out var posted) && posted != Posted.None)
        {
            return; // fallback already up; nothing better to show yet.
        }

        var png = await composer.ComposeStaticAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (png is null)
        {
            return; // base map not ready yet; retry next tick.
        }

        var fallbackCulture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        await poster.PostAsync(id, BuildEmbed(key, rustMapsUrl: null, fallbackCulture), png, cancellationToken)
            .ConfigureAwait(false);
        _posted[mapKey] = Posted.Fallback;
    }

    private Embed BuildEmbed(RustMapsMapKey key, string? rustMapsUrl, string culture)
    {
        var builder = new EmbedBuilder()
            .WithTitle(localizer.Get("map.info.title", culture))
            .WithColor(Color.DarkGreen)
            .AddField(localizer.Get("map.info.size", culture),
                key.Size.ToString(System.Globalization.CultureInfo.InvariantCulture), inline: true)
            .AddField(localizer.Get("map.info.seed", culture),
                key.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), inline: true)
            .WithImageUrl("attachment://map.png");
        if (rustMapsUrl is not null)
        {
            builder.WithUrl(rustMapsUrl);
        }
        else
        {
            builder.WithFooter(localizer.Get("map.info.generating", culture));
        }

        return builder.Build();
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

    private async Task RunTickAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(options.Value.RustMaps.GenerationPollInterval, cancellationToken).ConfigureAwait(false);
                foreach (var key in coordinator.PendingKeys())
                {
                    await driver.AdvanceAsync(key, cancellationToken).ConfigureAwait(false);
                }

                foreach (var (guild, server) in _connected.Keys)
                {
                    await EnsureInfoMapAsync(guild, server, cancellationToken).ConfigureAwait(false);
                }
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
            LogTickFaulted(logger, ex);
        }
    }

    private async Task ConsumeConnectionStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await OnConnectionStatusAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogStatusFaulted(logger, ex);
        }
    }

    private async Task OnConnectionStatusAsync(ConnectionStatusChangedEvent evt, CancellationToken cancellationToken)
    {
        var key = (evt.GuildId, evt.ServerId);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IConnectionStore>();
            var state = await store.GetStateAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
            if (state is null || state.Status != ConnectionStatus.Connected)
            {
                _connected.TryRemove(key, out _);
                return;
            }

            _connected[key] = 0;
        }

        await EnsureInfoMapAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Info-map tick loop faulted.")]
    private static partial void LogTickFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Info-map connection-status loop faulted.")]
    private static partial void LogStatusFaulted(ILogger logger, Exception exception);
}
```

(Verify during implementation: `IWorkspaceStore.GetCultureAsync(guildId, ct)` signature — used exactly as `EventRelay.GetCultureAsync` does; copy that call. `IConnectionStore.GetStateAsync` + `ConnectionStatus.Connected` — copy from `MapHostedService.OnConnectionStatusAsync`. If `EnsureInfoMapAsync` being public on an internal class trips an analyzer, keep it `internal`-visible for the test via the existing `InternalsVisibleTo` the Map.Tests project already uses.)

- [ ] **Step 4: Register the RustMaps components (key-gated)**

In `MapServiceCollectionExtensions.AddMap`, expand the key block:

```csharp
var rustMapsKey = configuration["Map:RustMaps:ApiKey"];
if (!string.IsNullOrWhiteSpace(rustMapsKey))
{
    services.AddRustMapsClientV4(o => o.ApiKey = rustMapsKey);
    services.AddHttpClient(RustMapsGenerationDriver.HttpClientName);
    services.AddSingleton<IRustMapsMapCoordinator, RustMapsMapCoordinator>();
    services.AddSingleton<RustMapsGenerationDriver>();
    services.AddSingleton<IInfoMapPoster, DiscordInfoMapPoster>();
    services.AddHostedService<InfoMapHostedService>();
}
```

(`IInfoChannelLocator` is registered by Workspace's `AddWorkspace`, Task 4.)

- [ ] **Step 5: Localization strings**

Add to `Strings.resx` (English):

- `map.info.title` = `Server Map`
- `map.info.size` = `Size`
- `map.info.seed` = `Seed`
- `map.info.generating` = `Higher-quality RustMaps render generating…`

Add to `Strings.fr.resx` (French):

- `map.info.title` = `Carte du serveur`
- `map.info.size` = `Taille`
- `map.info.seed` = `Seed`
- `map.info.generating` = `Rendu RustMaps haute qualité en cours de génération…`

Match the existing `<data name=… xml:space="preserve"><value>…</value></data>` structure. Keep keys sorted where the file is sorted.

- [ ] **Step 6: Registration test**

Add to `MapRegistrationTests`. Resolve only the Discord-free components (coordinator, driver — the driver needs `IHttpClientFactory` from `AddHttpClient` and `IRustMapsClient` from `AddRustMapsClientV4`, both registered by the key block, so it constructs). Assert the Discord-dependent poster + hosted service by **descriptor presence** — do NOT resolve them, so no `DiscordSocketClient` is constructed (the existing `BuildProvider` registers none, and this must stay true).

```csharp
[Fact]
public void AddMap_with_RustMaps_key_registers_the_generation_components()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton(Substitute.For<IRustServerQuery>());
    services.AddSingleton(Substitute.For<IEventState>());
    services.AddSingleton(Substitute.For<IRigState>());
    services.AddScoped(_ => Substitute.For<IMapSettingsStore>());
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection([new KeyValuePair<string, string?>("Map:RustMaps:ApiKey", "test-key")])
        .Build();
    services.AddMap(configuration);

    using var provider = services.BuildServiceProvider(validateScopes: true);
    Assert.NotNull(provider.GetService<IRustMapsMapCoordinator>());
    Assert.NotNull(provider.GetService<RustMapsGenerationDriver>());
    // Discord-dependent — assert registration without constructing DiscordSocketClient.
    Assert.Contains(services, d => d.ServiceType == typeof(IInfoMapPoster));
    Assert.Contains(services, d => d.ImplementationType == typeof(InfoMapHostedService));
}

[Fact]
public void AddMap_without_RustMaps_key_registers_no_generation_components()
{
    using var provider = BuildProvider(EmptyConfiguration());
    Assert.Null(provider.GetService<IRustMapsMapCoordinator>());
    Assert.Null(provider.GetService<IInfoMapPoster>());
}
```

(`InfoMapHostedService` is registered via `AddHostedService`, whose descriptor has `ServiceType == typeof(IHostedService)` and `ImplementationType == typeof(InfoMapHostedService)`.)

- [ ] **Step 7: Build + test + jb + commit**

Run:

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
dotnet test tests/RustPlusBot.Features.Map.Tests -maxcpucount:1
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git diff --stat
```

Expected: clean build; Map.Tests green; commit any jb reformat with the change.

```bash
git add -A src tests
git commit -m "feat(map): #info static RustMaps map + credit-safe auto-generate service"
```

---

### Task 8: Final verification sweep

**Files:** none new — whole-branch gates.

- [ ] **Step 1: Full sequential test run**

```bash
for p in tests/*/; do dotnet test "$p" -maxcpucount:1 --nologo 2>&1 | grep -E "^(Passed|Failed)!"; done
```

Expected: every assembly green; record totals (Map.Tests up by the new coordinator/driver/composer/options/registration tests).

- [ ] **Step 2: Strict build + EF drift**

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Persistence
```

Expected: clean build; no pending model changes (this slice adds no entities).

- [ ] **Step 3: Format gate**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git diff --stat
```

Expected: no diff (commit any reformat if present).

- [ ] **Step 4: Commit + wrap**

```bash
git add -A
git commit -m "chore(map): verification sweep for RustMaps #info map"   # only if anything changed
```

Then use superpowers:finishing-a-development-branch (the whole map-render-rework branch → PR to develop).

---

## Deferred (recorded, not planned)

- Wipe / seed-size-change detection (dropped — wider cross-cutting effort; a mid-session wipe still serves a stale base tile until reconnect, unchanged by this slice).
- Persistence of generation state across restarts (re-derived from GET).
- Showing the #info static map when no RustMaps key is configured.
