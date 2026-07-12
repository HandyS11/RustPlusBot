# Subsystem 2b-ii — Map Layers & Per-Server Toggles — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add per-server map layer settings with a ManageGuild toggle UI in `#map`, plus four new render layers (monuments, travelling vendor, players, oil-rig styling), replacing 2b's drawn glyphs with bundled icons.

**Architecture:** Extends `RustPlusBot.Features.Map` (2b) and `RustPlusBot.Features.Workspace`. A new `ServerMapSettings` entity + `IMapSettingsStore` (Persistence) drive a `MapLayerSet` the `MapComposer` reads per-render. New layers gather from existing seams (`IRustServerQuery.GetTeamInfoAsync`, 2a-ii `IRigState`) plus a new `IRustServerQuery.GetMonumentsAsync`. The toggle UI is a persistent Workspace `MessageSpec` control message + a wildcard ManageGuild component module; toggles publish a `MapSettingsChangedEvent` the Map host consumes for an immediate throttled repaint. The image post stays a pure delete+repost (poster now skips attachmentless messages).

**Tech Stack:** .NET 10, C#, EF Core + SQLite (Persistence), Discord.Net 3.20, SixLabors.ImageSharp + ImageSharp.Drawing, xUnit + NSubstitute. Strict analyzers (Roslynator) + ReSharper `jb cleanupcode --profile=ReformatAndReorder` format gate.

## Global Constraints

- Target framework `net10.0`; all packages centrally versioned in `Directory.Packages.props` (no `Version=` attributes on `<PackageReference>`).
- Entities live in `RustPlusBot.Domain`; EF configs in `RustPlusBot.Persistence/Configurations`; stores in `RustPlusBot.Persistence/<Area>`; the global ulong↔long snowflake conversion is applied by `OnModelCreating` base.
- All async public/interface methods take a trailing `CancellationToken cancellationToken = default` (interface impls must repeat `= default` — Roslynator `S1006`).
- All public types/members need XML doc comments (Roslynator `RCS1141` requires `<param>` for every parameter; `S1135` treats `// TODO` as an error — use `<remarks>`).
- Per-guild culture is EN/FR; localized strings come from the feature's catalog (Workspace `LocalizationCatalog` for channel/message copy). Ephemeral interaction acks may stay English (repo convention).
- NSubstitute on `internal` interfaces requires `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` (already present in Map; add to any new project that needs it — none new here).
- Run the **full** test suite and read per-assembly counts after each task (a changed interface/fake that fails to compile silently drops an assembly's tests). Run `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (via `dotnet tool restore`) before any push.
- New FK (`ServerMapSettings` → `RustServer` cascade): any test persisting a `ServerMapSettings` row must seed a `RustServer` first.
- Branch: `feat/map-layers` off `develop` (plain branch, no worktree).
- Bundled icons are Facepunch/Rust game art (companion-app fair use); record in `NOTICE`. Source: `/home/handys11/Dev/rustplus-desktop/RustPlusDesktop/Assets/icons/`.

---

## File Structure

**Create:**

- `src/RustPlusBot.Domain/Map/ServerMapSettings.cs` — the per-(guild,server) settings entity.
- `src/RustPlusBot.Persistence/Configurations/ServerMapSettingsConfiguration.cs` — EF mapping + cascade FK.
- `src/RustPlusBot.Persistence/Map/MapLayer.cs` — the layer enum (Persistence-side, store API).
- `src/RustPlusBot.Persistence/Map/IMapSettingsStore.cs` — store seam.
- `src/RustPlusBot.Persistence/Map/MapSettingsStore.cs` — EF-backed store.
- `src/RustPlusBot.Persistence/Migrations/<ts>_MapSettings.cs` (+ `.Designer.cs`) — generated migration.
- `src/RustPlusBot.Abstractions/Events/MapSettingsChangedEvent.cs` — toggle→repaint bus event.
- `src/RustPlusBot.Features.Map/Assets/icons/*.png` — vendored icon assets (embedded).
- `src/RustPlusBot.Features.Map/Assets/MapIcons.cs` — keyed icon registry.
- `src/RustPlusBot.Features.Map/Assets/MonumentIconMap.cs` — monument token → icon key.
- `src/RustPlusBot.Features.Map/Rendering/PlayerPlacement.cs` — a projected player to draw.
- `src/RustPlusBot.Features.Workspace/Messages/MapControlMessageRenderer.cs` — control message renderer.
- `src/RustPlusBot.Features.Workspace/Modules/MapComponentModule.cs` — toggle component module.
- Tests under `tests/RustPlusBot.Persistence.Tests/Map/`, `tests/RustPlusBot.Features.Map.Tests/` (FLAT — no subfolders; existing files: `MapRendererTests`, `MapComposerTests`, `MapLayerSetTests`, `MarkerGlyphsTests`, `MapRegistrationTests`, `WorldToPixelTests`, `BaseMapCacheTests`, `MapRefreshThrottleTests`), `tests/RustPlusBot.Features.Workspace.Tests/`.

**Existing tests this slice BREAKS (must be updated, called out per task):**

- `MapLayerSetTests` — asserts `MapLayerSet.Default2b` (removed in Task 6) → update to `AllOn`.
- `MapRendererTests` — calls the 4-arg `Render(...)` + 5-field `MapLayerSet(...)` + `Default2b` (Task 6 changes both).
- `MarkerGlyphsTests` — `MarkerGlyphs` is removed in Task 6 (replaced by `MapIcons`) → delete this test file.
- `MapComposerTests` — constructs `new MapComposer(cache, events, query, renderer)` (4 args); Task 7 changes the ctor to 6 args (`+ IRigState + IServiceScopeFactory`).
- `MapRegistrationTests` — resolves `MapComposer`; Task 7's new ctor deps (`IRigState`, `IMapSettingsStore` scoped) must be registered in the test's service collection.

**Modify:**

- `src/RustPlusBot.Persistence/BotDbContext.cs` — add `DbSet<ServerMapSettings>` + `ApplyConfiguration`.
- `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs` — register `IMapSettingsStore`.
- `src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs` — add `GetMonumentsAsync`.
- `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` — implement `GetMonumentsAsync`.
- `tests/.../FakeRustSocketSource.cs` (or the test double implementing `IRustServerQuery`) — add `GetMonumentsAsync`.
- `src/RustPlusBot.Features.Connections/Listening/MarkerKind.cs` — add `TravellingVendor`.
- `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` — surface `TravellingVendorMarkers`.
- `src/RustPlusBot.Features.Map/Rendering/MapLayerSet.cs` — add `Players` field + `AllOn`.
- `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs` — icon draws + new layers.
- `src/RustPlusBot.Features.Map/Composing/MapComposer.cs` — read settings, gather all layers.
- `src/RustPlusBot.Features.Map/Hosting/MapHostedService.cs` — consume `MapSettingsChangedEvent`.
- `src/RustPlusBot.Features.Map/Posting/DiscordMapChannelPoster.cs` — skip attachmentless messages.
- `src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj` — embed icon resources.
- `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs` — add `WorkspaceMessageKeys.ServerMap`.
- `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs` — add the map `MessageSpec`.
- `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs` — map control EN/FR keys.
- Workspace renderer/module registration site (where `ServerInfoMessageRenderer` is registered).
- `NOTICE` — icon attribution.

---

### Task 1: `ServerMapSettings` entity + EF config + DbContext + migration

**Files:**

- Create: `src/RustPlusBot.Domain/Map/ServerMapSettings.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/ServerMapSettingsConfiguration.cs`
- Modify: `src/RustPlusBot.Persistence/BotDbContext.cs` (DbSet + ApplyConfiguration)
- Test: `tests/RustPlusBot.Persistence.Tests/Map/ServerMapSettingsSchemaTests.cs`

**Interfaces:**

- Produces: `ServerMapSettings { ulong GuildId; Guid ServerId; bool ShowGrid; bool ShowMarkers; bool ShowMonuments; bool ShowVendor; bool ShowPlayers; bool ShowRigs; }` (all bools default `true`), `BotDbContext.ServerMapSettings` DbSet.

- [ ] **Step 1: Write the entity**

Create `src/RustPlusBot.Domain/Map/ServerMapSettings.cs`:

```csharp
namespace RustPlusBot.Domain.Map;

/// <summary>Per-(guild, server) rendered-map layer settings; one row per server. Layers default on.</summary>
public sealed class ServerMapSettings
{
    /// <summary>The owning guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server id (FK to RustServer; primary key, one row per server).</summary>
    public Guid ServerId { get; set; }

    /// <summary>Whether the grid layer is drawn.</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>Whether live cargo/heli/chinook markers are drawn.</summary>
    public bool ShowMarkers { get; set; } = true;

    /// <summary>Whether monument icons are drawn.</summary>
    public bool ShowMonuments { get; set; } = true;

    /// <summary>Whether the travelling-vendor marker is drawn.</summary>
    public bool ShowVendor { get; set; } = true;

    /// <summary>Whether teammate position markers are drawn.</summary>
    public bool ShowPlayers { get; set; } = true;

    /// <summary>Whether oil rigs are styled by activation state.</summary>
    public bool ShowRigs { get; set; } = true;
}
```

- [ ] **Step 2: Write the EF configuration**

Create `src/RustPlusBot.Persistence/Configurations/ServerMapSettingsConfiguration.cs` (mirrors `ServerCommandSettingsConfiguration`):

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Map;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ServerMapSettingsConfiguration : IEntityTypeConfiguration<ServerMapSettings>
{
    public void Configure(EntityTypeBuilder<ServerMapSettings> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(s => s.ServerId);

        // Removing a RustServer cascades to its single map-settings row so no orphaned config lingers.
        builder.HasOne<RustServer>()
            .WithOne()
            .HasForeignKey<ServerMapSettings>(s => s.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 3: Wire into `BotDbContext`**

In `src/RustPlusBot.Persistence/BotDbContext.cs`, after the `ServerCommandSettings` DbSet (line ~35) add:

```csharp
    /// <summary>Per-(guild, server) rendered-map layer settings.</summary>
    public DbSet<ServerMapSettings> ServerMapSettings => Set<ServerMapSettings>();
```

Add `using RustPlusBot.Domain.Map;` to the usings, and in `OnModelCreating` add `.ApplyConfiguration(new ServerMapSettingsConfiguration())` to the chain (after `ServerCommandSettingsConfiguration`).

- [ ] **Step 4: Write the failing schema test**

Create `tests/RustPlusBot.Persistence.Tests/Map/ServerMapSettingsSchemaTests.cs`, matching `ServerCommandSettingsSchemaTests` **verbatim** in harness shape: `SqliteContextFixture.Create()` returns `(context, connection)` and runs `Database.Migrate()` (so this test only passes once Step 6 creates the migration — that is the intended red→green). Seed a `RustServer { GuildId, Name, Ip, Port }`, save (its `Id` is generated), then add the settings row.

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Map;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Map;

public sealed class ServerMapSettingsSchemaTests
{
    [Fact]
    public async Task ServerMapSettings_persists_with_all_layers_on_by_default()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer { GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        context.ServerMapSettings.Add(new ServerMapSettings { ServerId = server.Id, GuildId = 1UL });
        await context.SaveChangesAsync();

        var read = await context.ServerMapSettings.SingleAsync(s => s.ServerId == server.Id);
        Assert.True(read.ShowGrid);
        Assert.True(read.ShowMarkers);
        Assert.True(read.ShowMonuments);
        Assert.True(read.ShowVendor);
        Assert.True(read.ShowPlayers);
        Assert.True(read.ShowRigs);
    }

    [Fact]
    public async Task RemovingServer_CascadeDeletesItsMapSettings()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer { GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        context.RustServers.Add(server);
        context.ServerMapSettings.Add(new ServerMapSettings { ServerId = server.Id, GuildId = 1UL });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.ServerMapSettings.ToListAsync());
    }
}
```

- [ ] **Step 5: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ServerMapSettingsSchemaTests`
Expected: FAIL — migration not created yet (model differs from snapshot) or DbSet missing.

- [ ] **Step 6: Create the migration**

Run: `dotnet tool restore` then
`dotnet ef migrations add MapSettings --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host`
Expected: creates `Migrations/<ts>_MapSettings.cs` + `.Designer.cs`, updates `BotDbContextModelSnapshot.cs`. Inspect the migration: a `ServerMapSettings` table, `ServerId` PK, FK to `RustServers` cascade, six bool columns.

> If `dotnet ef` errors with "Unable to retrieve project metadata" (the 2a-ii tooling quirk), hand-write the migration mirroring `20260616175850_CommandSettings.cs` and update the snapshot, then verify by building.

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter ServerMapSettingsSchemaTests`
Expected: PASS.

- [ ] **Step 8: Run full suite + commit**

Run: `dotnet test` — confirm no assembly dropped (per-assembly counts ≥ prior).

```bash
git add src/RustPlusBot.Domain/Map src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests/Map
git commit -m "feat(map): ServerMapSettings entity + EF config + MapSettings migration"
```

---

### Task 2: `MapLayer` enum + `IMapSettingsStore` + `MapSettingsStore`

**Files:**

- Create: `src/RustPlusBot.Persistence/Map/MapLayer.cs`
- Create: `src/RustPlusBot.Persistence/Map/IMapSettingsStore.cs`
- Create: `src/RustPlusBot.Persistence/Map/MapSettingsStore.cs`
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Map/MapSettingsStoreTests.cs`

**Interfaces:**

- Consumes: `ServerMapSettings`, `BotDbContext.ServerMapSettings` (Task 1); `RustPlusBot.Features.Map.Rendering.MapLayerSet` (Task 3 adds `Players` — but `MapLayerSet` already exists with 5 fields; **Task 3 runs before this in dependency order is NOT required** — to avoid a forward dep, the store returns its OWN result type). **Decision:** to keep Persistence free of a `Features.Map` reference, `IMapSettingsStore.GetAsync` returns a Persistence-owned record `MapLayerSettings(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Players, bool Rigs)` defined in `MapLayer.cs`. The composer (Task 6) maps `MapLayerSettings` → `MapLayerSet`.
- Produces: `enum MapLayer { Grid, Markers, Monuments, Vendor, Players, Rigs }`; `record MapLayerSettings(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Players, bool Rigs)` with `static MapLayerSettings AllOn`; `IMapSettingsStore.GetAsync(ulong, Guid, CancellationToken) -> Task<MapLayerSettings>`, `IMapSettingsStore.SetLayerAsync(ulong, Guid, MapLayer, bool, CancellationToken) -> Task`.

> **Why a Persistence-owned record, not `MapLayerSet`:** `RustPlusBot.Persistence` does not (and must not) reference `RustPlusBot.Features.Map`. Returning `MapLayerSettings` keeps the dependency arrow correct; the composer does the trivial 1:1 map.

- [ ] **Step 1: Write the enum + result record**

Create `src/RustPlusBot.Persistence/Map/MapLayer.cs`:

```csharp
namespace RustPlusBot.Persistence.Map;

/// <summary>A single toggleable map render layer.</summary>
public enum MapLayer
{
    /// <summary>The grid lines layer.</summary>
    Grid,

    /// <summary>The live cargo/heli/chinook markers layer.</summary>
    Markers,

    /// <summary>The monument icons layer.</summary>
    Monuments,

    /// <summary>The travelling-vendor layer.</summary>
    Vendor,

    /// <summary>The teammate-positions layer.</summary>
    Players,

    /// <summary>The oil-rig activation-styling layer.</summary>
    Rigs,
}

/// <summary>The resolved per-server layer toggles (all-on when no row is stored).</summary>
/// <param name="Grid">Grid lines.</param>
/// <param name="Markers">Live cargo/heli/chinook markers.</param>
/// <param name="Monuments">Monument icons.</param>
/// <param name="Vendor">Travelling vendor.</param>
/// <param name="Players">Teammate positions.</param>
/// <param name="Rigs">Oil-rig activation styling.</param>
public sealed record MapLayerSettings(
    bool Grid,
    bool Markers,
    bool Monuments,
    bool Vendor,
    bool Players,
    bool Rigs)
{
    /// <summary>All layers enabled — the default when no settings row exists.</summary>
    public static MapLayerSettings AllOn { get; } = new(true, true, true, true, true, true);
}
```

- [ ] **Step 2: Write the store interface**

Create `src/RustPlusBot.Persistence/Map/IMapSettingsStore.cs`:

```csharp
namespace RustPlusBot.Persistence.Map;

/// <summary>Reads/writes per-(guild, server) map layer settings.</summary>
public interface IMapSettingsStore
{
    /// <summary>Gets the layer toggles, returning all-on defaults when no row exists.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved layer toggles.</returns>
    Task<MapLayerSettings> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Sets one layer's enabled state, creating the row (all-on) if needed.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="layer">Which layer to change.</param>
    /// <param name="enabled">The new enabled state.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
    Task SetLayerAsync(ulong guildId,
        Guid serverId,
        MapLayer layer,
        bool enabled,
        CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the failing store tests**

Create `tests/RustPlusBot.Persistence.Tests/Map/MapSettingsStoreTests.cs`, matching `MuteStoreTests`' harness use (`SqliteContextFixture.Create()` → one `(context, connection)`; the store takes that `BotDbContext`). A seeded `RustServer` is required before writing a settings row (FK). Tests:

```csharp
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Persistence.Tests.Map;

public sealed class MapSettingsStoreTests
{
    private static RustServer SeedServer(BotDbContext context)
    {
        var server = new RustServer { GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 28015 };
        context.RustServers.Add(server);
        context.SaveChanges();
        return server;
    }

    [Fact]
    public async Task GetAsync_returns_all_on_when_no_row()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var result = await new MapSettingsStore(context).GetAsync(1UL, Guid.NewGuid());

        Assert.Equal(MapLayerSettings.AllOn, result);
    }

    [Fact]
    public async Task SetLayerAsync_creates_row_and_disables_one_layer_only()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var server = SeedServer(context);

        await new MapSettingsStore(context).SetLayerAsync(1UL, server.Id, MapLayer.Monuments, enabled: false);
        var result = await new MapSettingsStore(context).GetAsync(1UL, server.Id);

        Assert.False(result.Monuments);
        Assert.True(result.Grid);
        Assert.True(result.Markers);
        Assert.True(result.Vendor);
        Assert.True(result.Players);
        Assert.True(result.Rigs);
    }

    [Fact]
    public async Task SetLayerAsync_updates_existing_row()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;
        var server = SeedServer(context);
        var store = new MapSettingsStore(context);

        await store.SetLayerAsync(1UL, server.Id, MapLayer.Players, enabled: false);
        await store.SetLayerAsync(1UL, server.Id, MapLayer.Players, enabled: true);
        var result = await store.GetAsync(1UL, server.Id);

        Assert.True(result.Players);
    }
}
```

> One `BotDbContext` per test (the fixture binds it to one open in-memory connection); reusing the same `context` across store instances is the `MuteStoreTests` pattern. EF change-tracking means re-reading after a write sees the change within the same context.

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter MapSettingsStoreTests`
Expected: FAIL — `MapSettingsStore` not defined.

- [ ] **Step 5: Write the store implementation**

Create `src/RustPlusBot.Persistence/Map/MapSettingsStore.cs` (mirrors `MuteStore`):

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Map;

namespace RustPlusBot.Persistence.Map;

/// <summary>EF-backed <see cref="IMapSettingsStore"/>.</summary>
/// <param name="context">The bot database context.</param>
public sealed class MapSettingsStore(BotDbContext context) : IMapSettingsStore
{
    /// <inheritdoc />
    public async Task<MapLayerSettings> GetAsync(ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        var row = await context.ServerMapSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);
        return row is null
            ? MapLayerSettings.AllOn
            : new MapLayerSettings(row.ShowGrid, row.ShowMarkers, row.ShowMonuments, row.ShowVendor,
                row.ShowPlayers, row.ShowRigs);
    }

    /// <inheritdoc />
    public async Task SetLayerAsync(ulong guildId,
        Guid serverId,
        MapLayer layer,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        var existing = await context.ServerMapSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);

        var row = existing ?? new ServerMapSettings { GuildId = guildId, ServerId = serverId };
        switch (layer)
        {
            case MapLayer.Grid: row.ShowGrid = enabled; break;
            case MapLayer.Markers: row.ShowMarkers = enabled; break;
            case MapLayer.Monuments: row.ShowMonuments = enabled; break;
            case MapLayer.Vendor: row.ShowVendor = enabled; break;
            case MapLayer.Players: row.ShowPlayers = enabled; break;
            case MapLayer.Rigs: row.ShowRigs = enabled; break;
            default: throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown map layer.");
        }

        if (existing is null)
        {
            context.ServerMapSettings.Add(row);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 6: Register the store**

In `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`, after the `IMuteStore` registration (line ~37) add:

```csharp
        services.AddScoped<IMapSettingsStore, MapSettingsStore>();
```

Add `using RustPlusBot.Persistence.Map;` if needed.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter MapSettingsStoreTests`
Expected: PASS.

- [ ] **Step 8: Run full suite + commit**

```bash
git add src/RustPlusBot.Persistence/Map src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs tests/RustPlusBot.Persistence.Tests/Map/MapSettingsStoreTests.cs
git commit -m "feat(map): IMapSettingsStore + EF-backed store with all-on defaults"
```

---

### Task 3: Vendor icon assets + `MapIcons` registry + `MonumentIconMap` + `NOTICE`

**Files:**

- Create: `src/RustPlusBot.Features.Map/Assets/icons/*.png` (copied from rustplus-desktop)
- Create: `src/RustPlusBot.Features.Map/Assets/MapIcons.cs`
- Create: `src/RustPlusBot.Features.Map/Assets/MonumentIconMap.cs`
- Modify: `src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj` (embed icons)
- Modify: `NOTICE`
- Test: `tests/RustPlusBot.Features.Map.Tests/Assets/MapIconsTests.cs`

**Interfaces:**

- Consumes: `RustPlusBot.Features.Connections.Listening.MarkerKind` (incl. `TravellingVendor` — added in Task 4; for now reference the existing kinds and add the `TravellingVendor` branch in Task 4).
- Produces: `MapIcons.Marker(MarkerKind) -> Image<Rgba32>?`, `MapIcons.Monument(string token) -> Image<Rgba32>?` (null = no icon, caller skips), `MapIcons.Rig(RigKind, bool active) -> Image<Rgba32>?`, `MonumentIconMap.IconKeyFor(string token) -> string?`.

> **ImageSharp lifetime:** `MapIcons` loads each PNG once into an `Image<Rgba32>` held statically (icons are immutable inputs; the renderer clones/draws from them, never mutates the cached source). Loading via `Image.Load<Rgba32>(GetManifestResourceStream(...))`.

- [ ] **Step 1: Copy the icon assets**

Copy the needed PNGs from `/home/handys11/Dev/rustplus-desktop/RustPlusDesktop/Assets/icons/` into `src/RustPlusBot.Features.Map/Assets/icons/`. Minimum set:

Markers: `cargo.png`, `patrol.png`, `ch47.png`, `vendor.png`, `player.png`.
Monuments: `airfield.png`, `arcticresearch.png`, `banditcamp.png`, `dome.png`, `excavator.png`, `ferryterminal.png`, `fishingvillage.png`, `fishingvillagelarge.png`, `gasstation.png`, `harbour.png`, `harbour2.png`, `hqmquarry.png`, `junkyard.png`, `largeoilrig.png`, `launchsite.png`, `lighthouse.png`, `militarybase.png`, `militarytunnel.png`, `miningoutpost.png`, `missilesilo.png`, `oilrig.png`, `outpost.png`, `powerplant.png`, `powerstation.png`, `satellitedish.png`, `sewerbranch.png`, `stable.png`, `stonequarry.png`, `sulfurquarry.png`, `supermarket.png`, `swamp.png`, `trainyard.png`, `traintunnel.png`, `watertreatment.png`, `waterwell.png`.

```bash
mkdir -p src/RustPlusBot.Features.Map/Assets/icons
# copy each listed file, e.g.:
cp /home/handys11/Dev/rustplus-desktop/RustPlusDesktop/Assets/icons/cargo.png src/RustPlusBot.Features.Map/Assets/icons/
# (repeat for all listed icons)
```

- [ ] **Step 2: Embed the icons in the csproj**

In `src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj`, extend the EmbeddedResource ItemGroup:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Assets/LiberationSans-Regular.ttf" />
    <EmbeddedResource Include="Assets/icons/*.png" />
  </ItemGroup>
```

> Embedded resource names become `RustPlusBot.Features.Map.Assets.icons.<file>.png` (folder dots). `MapIcons` builds these names.

- [ ] **Step 3: Write `MonumentIconMap`**

Create `src/RustPlusBot.Features.Map/Assets/MonumentIconMap.cs`. Map the live protobuf tokens (from rustplusplus `monumentInfo`, plus the rig tokens 2a-ii pinned) to icon file keys (filename without `.png`). Unknown tokens → null.

```csharp
namespace RustPlusBot.Features.Map.Assets;

/// <summary>Maps a Rust+ monument protobuf token to a vendored icon key (filename without extension). Unknown tokens return null and are skipped.</summary>
public static class MonumentIconMap
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        ["AbandonedMilitaryBase"] = "militarybase",
        ["airfield_display_name"] = "airfield",
        ["arctic_base_a"] = "arcticresearch",
        ["bandit_camp"] = "banditcamp",
        ["dome_monument_name"] = "dome",
        ["excavator"] = "excavator",
        ["ferryterminal"] = "ferryterminal",
        ["fishing_village_display_name"] = "fishingvillage",
        ["large_fishing_village_display_name"] = "fishingvillagelarge",
        ["gas_station"] = "gasstation",
        ["harbor_display_name"] = "harbour",
        ["harbor_2_display_name"] = "harbour2",
        ["junkyard_display_name"] = "junkyard",
        ["large_oil_rig"] = "largeoilrig",
        ["oilrig_1"] = "oilrig",
        ["launchsite"] = "launchsite",
        ["lighthouse_display_name"] = "lighthouse",
        ["military_tunnels_display_name"] = "militarytunnel",
        ["mining_outpost_display_name"] = "miningoutpost",
        ["mining_quarry_hqm_display_name"] = "hqmquarry",
        ["mining_quarry_stone_display_name"] = "stonequarry",
        ["mining_quarry_sulfur_display_name"] = "sulfurquarry",
        ["missile_silo_monument"] = "missilesilo",
        ["outpost"] = "outpost",
        ["power_plant_display_name"] = "powerplant",
        ["satellite_dish_display_name"] = "satellitedish",
        ["sewer_display_name"] = "sewerbranch",
        ["stables_a"] = "stable",
        ["stables_b"] = "stable",
        ["supermarket"] = "supermarket",
        ["swamp_c"] = "swamp",
        ["train_yard_display_name"] = "trainyard",
        ["train_tunnel_display_name"] = "traintunnel",
        ["water_treatment_plant_display_name"] = "watertreatment",
    };

    /// <summary>Gets the icon key for a monument token, or null when unmapped.</summary>
    /// <param name="token">The Rust+ monument protobuf token.</param>
    /// <returns>The icon key (filename without extension), or null.</returns>
    public static string? IconKeyFor(string token) =>
        token is not null && Map.TryGetValue(token, out var key) ? key : null;
}
```

> Tokens with no vendored icon (`underwater_lab`, `train_tunnel_link_display_name`) are intentionally absent → skipped. The rig tokens `oilrig_1`/`large_oil_rig` map to `oilrig`/`largeoilrig` so the monuments layer renders them; the Rigs layer (Task 7) overlays state styling on the same positions.

- [ ] **Step 4: Write the failing `MapIcons` tests**

Create `tests/RustPlusBot.Features.Map.Tests/Assets/MapIconsTests.cs`:

```csharp
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Map.Assets;
using Xunit;

namespace RustPlusBot.Features.Map.Tests.Assets;

public sealed class MapIconsTests
{
    [Theory]
    [InlineData(MarkerKind.CargoShip)]
    [InlineData(MarkerKind.PatrolHelicopter)]
    [InlineData(MarkerKind.Chinook)]
    public void Marker_resolves_for_known_kinds(MarkerKind kind) =>
        Assert.NotNull(MapIcons.Marker(kind));

    [Fact]
    public void Monument_resolves_known_token() =>
        Assert.NotNull(MapIcons.Monument("launchsite"));

    [Fact]
    public void Monument_returns_null_for_unknown_token() =>
        Assert.Null(MapIcons.Monument("definitely_not_a_monument"));

    [Fact]
    public void MonumentIconMap_maps_rig_tokens_to_icons()
    {
        Assert.Equal("oilrig", MonumentIconMap.IconKeyFor("oilrig_1"));
        Assert.Equal("largeoilrig", MonumentIconMap.IconKeyFor("large_oil_rig"));
    }
}
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapIconsTests`
Expected: FAIL — `MapIcons` not defined.

- [ ] **Step 6: Write `MapIcons`**

Create `src/RustPlusBot.Features.Map/Assets/MapIcons.cs`:

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Features.Connections.Listening;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>Loads vendored icon assets (embedded PNGs) once and serves them by marker kind or monument token. Cached images are immutable inputs the renderer draws from; never mutate them.</summary>
public static class MapIcons
{
    private const string ResourcePrefix = "RustPlusBot.Features.Map.Assets.icons.";

    private static readonly ConcurrentDictionary<string, Image<Rgba32>?> Cache = new(StringComparer.Ordinal);

    /// <summary>Gets the icon for a marker kind, or null when there is no icon for that kind.</summary>
    /// <param name="kind">The marker kind.</param>
    /// <returns>The cached icon image, or null.</returns>
    public static Image<Rgba32>? Marker(MarkerKind kind) => kind switch
    {
        MarkerKind.CargoShip => Load("cargo"),
        MarkerKind.PatrolHelicopter => Load("patrol"),
        MarkerKind.Chinook => Load("ch47"),
        MarkerKind.TravellingVendor => Load("vendor"),
        _ => null,
    };

    /// <summary>Gets the player position icon.</summary>
    /// <returns>The player icon, or null when missing.</returns>
    public static Image<Rgba32>? Player() => Load("player");

    /// <summary>Gets the icon for a monument token, or null when the token is unmapped or the file is missing.</summary>
    /// <param name="token">The Rust+ monument protobuf token.</param>
    /// <returns>The cached icon image, or null.</returns>
    public static Image<Rgba32>? Monument(string token)
    {
        var key = MonumentIconMap.IconKeyFor(token);
        return key is null ? null : Load(key);
    }

    private static Image<Rgba32>? Load(string key) => Cache.GetOrAdd(key, static k =>
    {
        var asm = typeof(MapIcons).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourcePrefix + k + ".png");
        return stream is null ? null : Image.Load<Rgba32>(stream);
    });
}
```

- [ ] **Step 7: Add NOTICE attribution**

Append to `NOTICE` (after the existing font attribution):

```
Map marker and monument icons under src/RustPlusBot.Features.Map/Assets/icons/
originate from Rust (© Facepunch Studios) and are reused here under the same
companion-app fair-use basis as the official Rust+ application. They were
sourced via the rustplus-desktop project, which redistributes the game art;
the icons are Facepunch's game assets, not original GPL-licensed work, and are
not treated as GPL-licensed nor as encumbering this MIT-licensed project.
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapIconsTests`
Expected: PASS.

- [ ] **Step 9: Run full suite + commit**

```bash
git add src/RustPlusBot.Features.Map/Assets src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj NOTICE tests/RustPlusBot.Features.Map.Tests/Assets
git commit -m "feat(map): vendor icon assets + MapIcons registry + MonumentIconMap"
```

---

### Task 4: `MarkerKind.TravellingVendor` + surface vendor bucket in the shim

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Listening/MarkerKind.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` (the `GetMapMarkersAsync` bucket calls, ~line 339)
- Test: `tests/RustPlusBot.Features.Connections.Tests/...` (a `MarkerKind`-coverage assertion if the project has one; otherwise this is shim-only and verified by Task 6's composer test using the new kind)

**Interfaces:**

- Produces: `MarkerKind.TravellingVendor` enum member.

> `RustPlusSocketSource.GetMapMarkersAsync` is the untested integration shim (repo convention) — no unit test for the bucket add. The enum addition is what tests reference.

- [ ] **Step 1: Add the enum member**

In `src/RustPlusBot.Features.Connections/Listening/MarkerKind.cs`, add after `Crate`:

```csharp
    /// <summary>The travelling vendor.</summary>
    TravellingVendor = 5,
```

- [ ] **Step 2: Surface the vendor bucket in the shim**

In `RustPlusSocketSource.GetMapMarkersAsync` (~line 339), add after the three existing `AddMarkers` calls:

```csharp
            AddMarkers(markers, data.CargoShipMarkers, MarkerKind.CargoShip);
            AddMarkers(markers, data.PatrolHelicopterMarkers, MarkerKind.PatrolHelicopter);
            AddMarkers(markers, data.Ch47Markers, MarkerKind.Chinook);
            AddMarkers(markers, data.TravellingVendorMarkers, MarkerKind.TravellingVendor);
```

Update the method's comment to note vendor is now surfaced.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/RustPlusBot.Features.Connections`
Expected: build 0 errors. (`TravellingVendorMarker : Marker`, so the generic `AddMarkers<TMarker>` constraint is satisfied — CONFIRMED against the RustPlusApi source.)

- [ ] **Step 4: Run full suite + commit**

Run: `dotnet test` — confirm Connections assembly still green, no drop.

```bash
git add src/RustPlusBot.Features.Connections/Listening/MarkerKind.cs src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs
git commit -m "feat(connections): add TravellingVendor marker kind + surface vendor bucket"
```

---

### Task 5: `IRustServerQuery.GetMonumentsAsync` + supervisor impl + fake

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Modify: the test double implementing `IRustServerQuery` (find via `grep -rln "IRustServerQuery" tests`)
- Test: `tests/RustPlusBot.Features.Connections.Tests/...` supervisor test asserting `GetMonumentsAsync` returns `[]` when no live socket (mirror an existing `GetTeamInfoAsync` no-socket test).

**Interfaces:**

- Consumes: internal `IRustServerConnection.GetMonumentsAsync(TimeSpan, CancellationToken)` (exists, 2a-ii), `MonumentSnapshot(string Token, float X, float Y)` (Abstractions).
- Produces: `IRustServerQuery.GetMonumentsAsync(ulong guildId, Guid serverId, CancellationToken) -> Task<IReadOnlyList<MonumentSnapshot>>` (empty list when no live socket).

- [ ] **Step 1: Add the seam method (failing — interface grows, impls don't satisfy yet)**

In `src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs`, add:

```csharp
    /// <summary>Gets the server's monuments, or an empty list when there is no live socket.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The monuments, or an empty list when there is no live socket.</returns>
    Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken);
```

Add `using RustPlusBot.Abstractions.Connections;` if `MonumentSnapshot` is in that namespace (verify the actual namespace from `MonumentSnapshot.cs`).

- [ ] **Step 2: Write the failing supervisor test**

In the Connections test project, add a test mirroring the existing "GetTeamInfoAsync returns null when no live socket" test, but for monuments:

```csharp
[Fact]
public async Task GetMonumentsAsync_returns_empty_when_no_live_socket()
{
    // Arrange the supervisor exactly as the existing GetTeamInfoAsync test does (same fixture/builder).
    var result = await supervisor.GetMonumentsAsync(1UL, Guid.NewGuid(), CancellationToken.None);
    Assert.Empty(result);
}
```

> Locate the existing `GetTeamInfoAsync` supervisor test and copy its arrangement verbatim, changing only the call + assertion.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter GetMonumentsAsync`
Expected: FAIL — `ConnectionSupervisor` does not implement `GetMonumentsAsync` (won't compile, or method missing).

- [ ] **Step 4: Implement on the supervisor**

In `ConnectionSupervisor.cs`, add the method mirroring `GetTeamInfoAsync` (which reads `_liveSockets` for the live connection). Pattern: look up the live socket for `(guildId, serverId)`; if none, return `[]`; else call `connection.GetMonumentsAsync(_options.HeartbeatTimeout, ct)`.

```csharp
    /// <inheritdoc />
    public async Task<IReadOnlyList<MonumentSnapshot>> GetMonumentsAsync(
        ulong guildId,
        Guid serverId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var connection))
        {
            return [];
        }

        return await connection.GetMonumentsAsync(_options.HeartbeatTimeout, cancellationToken).ConfigureAwait(false);
    }
```

> Match the EXACT `_liveSockets` access + null/try pattern `GetTeamInfoAsync` uses (it may return null rather than `[]` on no-socket — but monuments are a list, so `[]` is the right "no data" value; confirm against the sibling method's shape and keep the broad-catch/log convention if `GetTeamInfoAsync` has one).

- [ ] **Step 5: Confirm no hand-written `IRustServerQuery` fake breaks**

`IRustServerQuery` is consumed via `Substitute.For<IRustServerQuery>()` everywhere (Map/Workspace tests) — NSubstitute auto-implements the new `GetMonumentsAsync` (returns `null`→ must be configured per-test where the composer needs monuments; configure `query.GetMonumentsAsync(...).Returns([...])` in Task 7's composer tests). There is **no** hand-written `IRustServerQuery` fake to update. (`FakeRustSocketSource.FakeConnection` already implements the *internal* `IRustServerConnection.GetMonumentsAsync` from 2a-ii — unchanged.) Build all test projects to confirm none broke.

- [ ] **Step 6: Run test to verify it passes + full suite**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter GetMonumentsAsync` → PASS.
Run: `dotnet test` — confirm Map/Workspace assemblies still compile and counts hold.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests
git commit -m "feat(connections): expose GetMonumentsAsync on IRustServerQuery"
```

---

### Task 6: `MapLayerSet` + `MapRenderer` icon draws + new layers

**Files:**

- Modify: `src/RustPlusBot.Features.Map/Rendering/MapLayerSet.cs`
- Create: `src/RustPlusBot.Features.Map/Rendering/PlayerPlacement.cs`
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/Rendering/MapRendererTests.cs` (extend existing)

**Interfaces:**

- Consumes: `MapIcons` (Task 3), `MarkerPlacement` (exists), `MapDimensions`, `MarkerKind.TravellingVendor` (Task 4).
- Produces: `MapLayerSet(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Players, bool Rigs)` + `MapLayerSet.AllOn`; `PlayerPlacement(string Name, float PixelX, float PixelY, bool IsAlive, bool IsOnline)`; `MonumentPlacement(string Token, float PixelX, float PixelY)`; `RigPlacement(RigKind Kind, float PixelX, float PixelY, bool Active)`; `MapRenderer.Render(byte[] baseJpeg, MapDimensions dims, IReadOnlyList<MarkerPlacement> markers, IReadOnlyList<MonumentPlacement> monuments, IReadOnlyList<PlayerPlacement> players, IReadOnlyList<RigPlacement> rigs, MapLayerSet layers) -> byte[]`.

> The renderer signature grows to take the extra placement lists. Keep all five placement records in `Rendering/`. `RigKind` comes from `RustPlusBot.Abstractions.Events`.

- [ ] **Step 1: Extend `MapLayerSet`**

Replace `src/RustPlusBot.Features.Map/Rendering/MapLayerSet.cs`:

```csharp
namespace RustPlusBot.Features.Map.Rendering;

/// <summary>Which overlay layers the renderer should draw.</summary>
/// <param name="Grid">Draw the map grid lines.</param>
/// <param name="Markers">Draw live cargo/heli/chinook markers.</param>
/// <param name="Monuments">Draw monument icons.</param>
/// <param name="Vendor">Draw the travelling-vendor marker.</param>
/// <param name="Players">Draw teammate position markers.</param>
/// <param name="Rigs">Style oil rigs by activation state.</param>
public sealed record MapLayerSet(bool Grid, bool Markers, bool Monuments, bool Vendor, bool Players, bool Rigs)
{
    /// <summary>All layers enabled — the defaults-on render.</summary>
    public static MapLayerSet AllOn { get; } = new(true, true, true, true, true, true);
}
```

- [ ] **Step 2: Add the placement records**

Create `src/RustPlusBot.Features.Map/Rendering/PlayerPlacement.cs` (hold the three new placement records here for cohesion, or split — keep them together):

```csharp
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Map.Rendering;

/// <summary>One teammate to draw, already projected to pixel coordinates.</summary>
/// <param name="Name">The player display name.</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
/// <param name="IsAlive">Whether the player is alive (dead players are styled differently).</param>
/// <param name="IsOnline">Whether the player is online.</param>
public sealed record PlayerPlacement(string Name, float PixelX, float PixelY, bool IsAlive, bool IsOnline);

/// <summary>One monument to draw, already projected to pixel coordinates.</summary>
/// <param name="Token">The monument protobuf token (selects the icon).</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
public sealed record MonumentPlacement(string Token, float PixelX, float PixelY);

/// <summary>One oil rig to draw with activation styling, already projected to pixel coordinates.</summary>
/// <param name="Kind">Which rig.</param>
/// <param name="PixelX">Pixel X on the rendered tile.</param>
/// <param name="PixelY">Pixel Y on the rendered tile.</param>
/// <param name="Active">Whether the rig is currently active (combat window).</param>
public sealed record RigPlacement(RigKind Kind, float PixelX, float PixelY, bool Active);
```

- [ ] **Step 3: Write/extend the failing renderer test**

In `tests/RustPlusBot.Features.Map.Tests/Rendering/MapRendererTests.cs`, add a test that passes all layer types and asserts valid PNG output. Reuse the existing test's base-JPEG fixture (look at how the existing 2b renderer test builds `baseJpeg` + `MapDimensions`).

```csharp
[Fact]
public void Render_with_all_layers_produces_valid_png()
{
    var renderer = new MapRenderer();
    var dims = new MapDimensions(4000, 4000, 0);
    var markers = new[] { new MarkerPlacement(MarkerKind.CargoShip, 100, 100) };
    var monuments = new[] { new MonumentPlacement("launchsite", 200, 200) };
    var players = new[] { new PlayerPlacement("Alice", 300, 300, IsAlive: true, IsOnline: true) };
    var rigs = new[] { new RigPlacement(RigKind.Large, 400, 400, Active: true) };

    var png = renderer.Render(SampleBaseJpeg(), dims, markers, monuments, players, rigs, MapLayerSet.AllOn);

    using var img = Image.Load(png);            // throws if not a valid image
    Assert.Equal(MapRenderer.OutputSize, img.Width);
}
```

> `SampleBaseJpeg()` = whatever helper the existing renderer test already uses to produce a tiny valid JPEG. Do not invent one.

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter Render_with_all_layers`
Expected: FAIL — `Render` signature mismatch / placement types undefined.

- [ ] **Step 5: Update `MapRenderer.Render`**

Change the `Render` signature and add icon-based draws. Replace `DrawMarker` to composite the `MapIcons.Marker(kind)` image (centered) instead of the glyph; add `DrawMonuments`, `DrawPlayers`, `DrawRigs`. Keep `DrawGrid` unchanged. Icon composite uses `image.Mutate(ctx => ctx.DrawImage(icon, new Point((int)(x - icon.Width/2), (int)(y - icon.Height/2)), 1f))`.

```csharp
    public byte[] Render(byte[] baseJpeg,
        MapDimensions dims,
        IReadOnlyList<MarkerPlacement> markers,
        IReadOnlyList<MonumentPlacement> monuments,
        IReadOnlyList<PlayerPlacement> players,
        IReadOnlyList<RigPlacement> rigs,
        MapLayerSet layers)
    {
        ArgumentNullException.ThrowIfNull(baseJpeg);
        ArgumentNullException.ThrowIfNull(dims);
        ArgumentNullException.ThrowIfNull(markers);
        ArgumentNullException.ThrowIfNull(monuments);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(rigs);
        ArgumentNullException.ThrowIfNull(layers);

        using var image = Image.Load<Rgba32>(baseJpeg);
        image.Mutate(ctx => ctx.Resize(OutputSize, OutputSize));

        if (layers.Grid) DrawGrid(image, dims);
        if (layers.Monuments) DrawMonuments(image, monuments);
        if (layers.Markers) foreach (var m in markers) DrawMarker(image, m);
        if (layers.Rigs) DrawRigs(image, rigs);
        if (layers.Players) DrawPlayers(image, players);

        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }
```

> **Delete `MarkerGlyphs.cs`** in this step (`src/RustPlusBot.Features.Map/Assets/MarkerGlyphs.cs`) — it is fully replaced by `MapIcons`. Its test (`MarkerGlyphsTests.cs`) is deleted in Step 6.

Implement:

- `DrawMarker` → `var icon = MapIcons.Marker(m.Kind); if (icon is not null) image.Mutate(ctx => ctx.DrawImage(icon, CenterAt(m.PixelX, m.PixelY, icon), 1f));` (fallback to nothing if null).
- `DrawMonuments` → for each, `MapIcons.Monument(token)`; skip null; draw centered.
- `DrawRigs` → `MapIcons.Monument(kind == Small ? "oilrig_1" : "large_oil_rig")` (reuse the monument icon) then, if `Active`, overlay a red tint/ring (draw an `EllipsePolygon` outline in red around the icon — reuse the existing `ctx.Draw(Color.Red, width, circle)` idiom from 2b's old `DrawMarker`).
- `DrawPlayers` → draw a small filled circle (e.g. `Color.LimeGreen` alive/online, `Color.Gray` dead-or-offline) via `EllipsePolygon` + the player name as text via the existing `Font`/`RichTextOptions` (reuse 2b's text-draw code) offset below the dot.

> A `CenterAt(x, y, icon)` private helper returns `new Point((int)(x - icon.Width / 2f), (int)(y - icon.Height / 2f))`. Keep the existing `Font`/`LoadFont` and grid code intact.

- [ ] **Step 6: Update the existing broken Map tests**

These existing tests reference removed/changed APIs and will not compile until updated:

- `MapLayerSetTests.cs` — replace the `Default2b_enables_grid_and_markers_only` test with an `AllOn_enables_every_layer` test asserting all six bools true.
- `MapRendererTests.cs` — both tests call the old 4-arg `Render(BaseJpeg(), Dims, markers, MapLayerSet)` + 5-field `MapLayerSet(...)` + `MapLayerSet.Default2b`. Update to the new 7-arg `Render(BaseJpeg(), Dims, markers, monuments: [], players: [], rigs: [], layers)` and the 6-field `MapLayerSet(...)`. Keep the existing `BaseJpeg()` helper + `Dims` (OceanMargin 500).
- `MarkerGlyphsTests.cs` — **delete** (the `MarkerGlyphs` class is removed; `MapIconsTests` from Task 3 replaces its coverage).

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter "Render_with_all_layers|MapRendererTests|MapLayerSetTests"` → PASS.

- [ ] **Step 7: Run full suite + commit**

```bash
git add src/RustPlusBot.Features.Map/Rendering tests/RustPlusBot.Features.Map.Tests/Rendering
git commit -m "feat(map): icon-based renderer with monument/player/rig/vendor layers"
```

---

### Task 7: `MapComposer` reads settings + gathers all layers

**Files:**

- Modify: `src/RustPlusBot.Features.Map/Composing/MapComposer.cs`
- Modify: `src/RustPlusBot.Features.Map/MapServiceCollectionExtensions.cs` (composer ctor deps unchanged at registration — it's `AddSingleton<MapComposer>`, so verify the new ctor deps resolve as singletons/scoped)
- Test: `tests/RustPlusBot.Features.Map.Tests/Composing/MapComposerTests.cs` (extend)

**Interfaces:**

- Consumes: `IMapSettingsStore.GetAsync` (Task 2 — **scoped store**, see scope note), `IEventState.GetActiveMarkers` (exists), `IRustServerQuery.GetMonumentsAsync`/`GetTeamInfoAsync`/`GetMapDimensionsAsync` (Tasks 5 + existing), `IRigState.Get` (2a-ii), `MapRenderer.Render` new signature (Task 6), `WorldToPixel.ToPixel` (exists), `MapLayer`/`MapLayerSettings` (Task 2).
- Produces: `MapComposer.ComposeAsync(ulong, Guid, CancellationToken) -> Task<byte[]?>` (unchanged signature).

> **Scope note:** `MapComposer` is a singleton (`AddSingleton<MapComposer>`), but `IMapSettingsStore` is scoped (EF context). The composer must NOT inject `IMapSettingsStore` directly (captive dependency). Inject `IServiceScopeFactory` and open a scope per `ComposeAsync` to resolve `IMapSettingsStore` (mirror `MapHostedService.OnConnectionStatusAsync`'s scope usage). `IEventState`/`IRigState` are singletons (in-memory) and `IRustServerQuery` is the singleton supervisor — those stay constructor-injected.

- [ ] **Step 1: Update the existing composer test harness + write the failing tests**

`tests/RustPlusBot.Features.Map.Tests/MapComposerTests.cs` (flat path) already exists; its `Build(...)` helper constructs `new MapComposer(cache, events, query, renderer)` (4 args) and uses NSubstitute for `IRustServerQuery`/`IEventState`. Task 7 changes the ctor to **6 args** (`+ IRigState rigs, + IServiceScopeFactory scopeFactory`). Update `Build` to:

- add `var rigs = Substitute.For<IRigState>();` (default `Get(...)` returns `new RigState(RigStatus.Online, null)`),
- build an `IServiceScopeFactory` whose scope resolves a substitute `IMapSettingsStore` (default `GetAsync` returns `MapLayerSettings.AllOn`) — use `new ServiceCollection().AddScoped(_ => settingsStore).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>()`,
- pass both into the new ctor.

Keep the existing two tests passing (they assert null-on-no-base and a PNG renders). Then add the new tests below. Use the existing `BaseJpeg()`/`Dims` helpers.

```csharp
[Fact]
public async Task ComposeAsync_returns_null_when_no_base_map()
{
    // cache empty -> null (preserve 2b behaviour)
}

[Fact]
public async Task ComposeAsync_renders_only_enabled_layers()
{
    // settings: Monuments=false, others on; assert composer does NOT call query.GetMonumentsAsync
    // (or that monument placements are empty) while markers/players are gathered.
}

[Fact]
public async Task ComposeAsync_uses_all_on_when_no_settings_row()
{
    // no row -> AllOn -> all gather paths invoked
}
```

> Build these around the existing `MapComposerTests` arrangement (it already fakes `BaseMapCache`, `IEventState`, `IRustServerQuery`). Add the settings-store fake + `IRigState` fake.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapComposerTests`
Expected: FAIL — composer doesn't read settings / new ctor deps missing.

- [ ] **Step 3: Rewrite `MapComposer`**

> `RigStatus` lives in `RustPlusBot.Features.Events.State` (same namespace as `IRigState`/`RigState`); `RigKind` lives in `RustPlusBot.Abstractions.Events`. Both `using`s are below.

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.Events.State;
using RustPlusBot.Features.Map.Rendering;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Map.Composing;

/// <summary>Gathers the cached base map, per-server settings, and live layer data, then renders the map PNG.</summary>
/// <param name="cache">The base-map cache.</param>
/// <param name="events">Live marker state.</param>
/// <param name="rigs">Inferred oil-rig state.</param>
/// <param name="query">Live query seam (dimensions, monuments, team).</param>
/// <param name="renderer">The image renderer.</param>
/// <param name="scopeFactory">Opens a scope to read the scoped settings store.</param>
public sealed class MapComposer(
    BaseMapCache cache,
    IEventState events,
    IRigState rigs,
    IRustServerQuery query,
    MapRenderer renderer,
    IServiceScopeFactory scopeFactory)
{
    private static readonly MarkerKind[] LiveMarkerKinds =
        [MarkerKind.CargoShip, MarkerKind.PatrolHelicopter, MarkerKind.Chinook];

    public async Task<byte[]?> ComposeAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var baseImage = await cache.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (baseImage is null) return null;

        MapLayerSettings settings;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IMapSettingsStore>();
            settings = await store.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        }

        var layers = new MapLayerSet(settings.Grid, settings.Markers, settings.Monuments,
            settings.Vendor, settings.Players, settings.Rigs);

        var dims = await query.GetMapDimensionsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (dims is null)
        {
            // No dims: render base tile only (all overlays need world->pixel).
            return renderer.Render(baseImage, new MapDimensions(0, 0, 0), [], [], [], [],
                new MapLayerSet(false, false, false, false, false, false));
        }

        var markers = new List<MarkerPlacement>();
        if (layers.Markers)
        {
            foreach (var kind in LiveMarkerKinds)
                foreach (var m in events.GetActiveMarkers(guildId, serverId, kind))
                {
                    var (px, py) = WorldToPixel.ToPixel(m.X, m.Y, dims, MapRenderer.OutputSize);
                    markers.Add(new MarkerPlacement(kind, px, py));
                }
        }
        if (layers.Vendor)
        {
            foreach (var m in events.GetActiveMarkers(guildId, serverId, MarkerKind.TravellingVendor))
            {
                var (px, py) = WorldToPixel.ToPixel(m.X, m.Y, dims, MapRenderer.OutputSize);
                markers.Add(new MarkerPlacement(MarkerKind.TravellingVendor, px, py));
            }
        }

        var monuments = new List<MonumentPlacement>();
        if (layers.Monuments)
        {
            foreach (var mon in await query.GetMonumentsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false))
            {
                var (px, py) = WorldToPixel.ToPixel(mon.X, mon.Y, dims, MapRenderer.OutputSize);
                monuments.Add(new MonumentPlacement(mon.Token, px, py));
            }
        }

        var players = new List<PlayerPlacement>();
        if (layers.Players)
        {
            var team = await query.GetTeamInfoAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            foreach (var member in team?.Members ?? [])
            {
                var (px, py) = WorldToPixel.ToPixel(member.X, member.Y, dims, MapRenderer.OutputSize);
                players.Add(new PlayerPlacement(member.Name, px, py, member.IsAlive, member.IsOnline));
            }
        }

        var rigPlacements = new List<RigPlacement>();
        if (layers.Rigs)
        {
            // Reuse the monument positions for the two rig tokens; query rig state for activation styling.
            var rigMonuments = await query.GetMonumentsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            foreach (var mon in rigMonuments)
            {
                RigKind? kind = mon.Token switch
                {
                    "oilrig_1" => RigKind.Small,
                    "large_oil_rig" => RigKind.Large,
                    _ => null,
                };
                if (kind is { } k)
                {
                    var state = rigs.Get(guildId, serverId, k);
                    var (px, py) = WorldToPixel.ToPixel(mon.X, mon.Y, dims, MapRenderer.OutputSize);
                    rigPlacements.Add(new RigPlacement(k, px, py, state.Status == RigStatus.Active));
                }
            }
        }

        return renderer.Render(baseImage, dims, markers, monuments, players, rigPlacements, layers);
    }
}
```

> Verify the exact member names: `TeamMemberSnapshot.{Name, X, Y, IsAlive, IsOnline}` (3b-ii), `RigState.Status` + `RigStatus.Active` (2a-ii). If `GetMonumentsAsync` being called twice (monuments + rigs) is a concern, hoist it into one local fetched when either layer is on — acceptable optimisation, but keep it correct first.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests --filter MapComposerTests` → PASS.

- [ ] **Step 5: Fix `MapRegistrationTests` + register composer deps**

In `MapServiceCollectionExtensions.AddMap`, the composer is `AddSingleton<MapComposer>`; its new deps must resolve: `IRigState` (registered by `AddEvents`), `IRustServerQuery` (by `AddConnections`), and `IMapSettingsStore` is **scoped** but the composer resolves it via `IServiceScopeFactory` (always available) — so no captive-dependency violation. `MapRegistrationTests` builds its own service collection and resolves `MapComposer`; add `services.AddSingleton(Substitute.For<IRigState>());` (and confirm `IMapSettingsStore`/`IServiceScopeFactory` resolve — `IServiceScopeFactory` is built-in; add a scoped `IMapSettingsStore` substitute) so the resolve succeeds.

Run: `dotnet test tests/RustPlusBot.Features.Map.Tests` → all green.

- [ ] **Step 6: Run full suite + commit**

```bash
git add src/RustPlusBot.Features.Map tests/RustPlusBot.Features.Map.Tests
git commit -m "feat(map): composer reads per-server settings and gathers all layers"
```

---

### Task 8: `MapSettingsChangedEvent` + Map host consumes it

**Files:**

- Create: `src/RustPlusBot.Abstractions/Events/MapSettingsChangedEvent.cs`
- Modify: `src/RustPlusBot.Features.Map/Hosting/MapHostedService.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/Hosting/...` (if the project has a host test harness; otherwise this is integration-shaped — verify by build + the existing host test if any. If no host test exists, this task's deliverable is build-green + the event type, exercised end-to-end in Task 10's manual verify.)

**Interfaces:**

- Produces: `MapSettingsChangedEvent(ulong GuildId, Guid ServerId)` (ns `RustPlusBot.Abstractions.Events`).
- Consumes: `IEventBus.SubscribeAsync<MapSettingsChangedEvent>` (exists), `MapHostedService.RefreshAsync` (private, exists).

- [ ] **Step 1: Write the event**

Create `src/RustPlusBot.Abstractions/Events/MapSettingsChangedEvent.cs` (match the shape of `MapMarkersChangedEvent`):

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a server's map layer settings change, so the map image repaints immediately.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
public sealed record MapSettingsChangedEvent(ulong GuildId, Guid ServerId);
```

- [ ] **Step 2: Add the consumer loop to `MapHostedService`**

Add a fourth loop field + start it + join it on stop + consume method (mirror `ConsumeMarkerEventsAsync` exactly):

```csharp
    private Task? _settingsLoop;
```

In `StartAsync`: `_settingsLoop = Task.Run(() => ConsumeSettingsEventsAsync(_cts.Token), CancellationToken.None);`
In `StopAsync`: add `_settingsLoop` to the joined-loops array.
Add:

```csharp
    private async Task ConsumeSettingsEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<MapSettingsChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await RefreshAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
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
            LogSettingsLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Map settings loop faulted.")]
    private static partial void LogSettingsLoopFaulted(ILogger logger, Exception exception);
```

> **Throttle caveat (defer-accepted, document in code):** an immediate toggle repaint passes through the same `MapRefreshThrottle` as the periodic tick. If a toggle lands inside the throttle window of a recent paint, the immediate repaint is skipped and the change shows on the next tick (≤ `MapRefreshInterval`). Acceptable — matches 2b's coalescing intent. (If instant feedback matters more, a future refinement is a force-flag bypassing the throttle for settings events; out of scope here.)

- [ ] **Step 3: Build + run full suite**

Run: `dotnet build` then `dotnet test` — build 0/0, no assembly dropped.

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Abstractions/Events/MapSettingsChangedEvent.cs src/RustPlusBot.Features.Map/Hosting/MapHostedService.cs
git commit -m "feat(map): repaint #map immediately on MapSettingsChangedEvent"
```

---

### Task 9: Poster skips the control message (attachment-only delete)

**Files:**

- Modify: `src/RustPlusBot.Features.Map/Posting/DiscordMapChannelPoster.cs`
- Test: none (untested integration shim, repo convention) — verified by code review + Task 10 manual verify.

**Interfaces:** unchanged (`IMapChannelPoster.PostAsync`).

- [ ] **Step 1: Change the delete filter**

In `DiscordMapChannelPoster.DeletePriorBotMessagesAsync`, only delete the bot's prior messages that carry a file attachment (the image posts), leaving the reconciler-owned control message (text + components, no attachment) intact:

```csharp
    private async Task DeletePriorBotMessagesAsync(ITextChannel channel, RequestOptions options)
    {
        var batch = await channel.GetMessagesAsync(RecentMessageScan, options: options).FlattenAsync()
            .ConfigureAwait(false);
        // Delete only our prior IMAGE posts (they carry a file attachment); leave the persistent
        // control message (text + toggle components, no attachment) for the workspace reconciler.
        foreach (var message in batch.Where(m => m.Author.Id == client.CurrentUser.Id && m.Attachments.Count > 0))
        {
            await message.DeleteAsync(options).ConfigureAwait(false);
        }
    }
```

- [ ] **Step 2: Build + full suite**

Run: `dotnet build && dotnet test` — green, no drop.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Features.Map/Posting/DiscordMapChannelPoster.cs
git commit -m "fix(map): poster deletes only prior image posts, sparing the control message"
```

---

### Task 10: `#map` control message — `MessageSpec` + renderer + component module + EN/FR + repaint trigger

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs` (add `WorkspaceMessageKeys.ServerMap`)
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs` (register the MessageSpec)
- Create: `src/RustPlusBot.Features.Workspace/Messages/MapControlMessageRenderer.cs`
- Create: `src/RustPlusBot.Features.Workspace/Modules/MapComponentModule.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs` (map control keys)
- Modify: the Workspace renderer/module registration site (where `ServerInfoMessageRenderer` is registered as a keyed/lookup renderer)
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Messages/MapControlMessageRendererTests.cs`

**Interfaces (CONFIRMED against the repo):**

- Consumes: `IMapSettingsStore` (Task 2 — renderers are **scoped**, inject directly, no scope factory), `MapLayer`/`MapLayerSettings` (Task 2), `MapSettingsChangedEvent` (Task 8), `IEventBus.PublishAsync<T>(@event, ct=default) -> ValueTask`, `IWorkspaceReconciler.ReconcileServerAsync(ulong guildId, Guid serverId, CancellationToken=default)` (internal — `MapComponentModule` is in Workspace so it can use it), `ILocalizer`, `MessageRenderContext(GuildId, ServerId, Culture)`.
- Produces: `internal sealed class MapControlMessageRenderer : IMessageRenderer` with `string MessageKey => WorkspaceMessageKeys.ServerMap` and `ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)`; `WorkspaceMessageKeys.ServerMap = "server.map"`; `WorkspaceComponentIds.MapTogglePrefix = "workspace:map:toggle:"`.

> **The renderer seam (CONFIRMED):** `IMessageRenderer` is `internal` (`Registry/IMessageRenderer.cs`): `string MessageKey { get; }` + `ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)`. Renderers are registered `services.AddScoped<IMessageRenderer, XRenderer>()` (in `WorkspaceServiceCollectionExtensions`, lines ~40-45); the `WorkspaceReconciler` builds a `Dictionary<string, IMessageRenderer>` keyed by `MessageKey` and calls `RenderAsync(new MessageRenderContext(guildId, serverId, culture), ct)`. `MessagePayload` is `(string? Text, Embed? Embed, MessageComponent? Components)`. The control renderer returns `new MessagePayload(headerText, null, sixToggleButtons)`. Since renderers are scoped, inject `IMapSettingsStore` + `ILocalizer` directly (mirror `ServerInfoMessageRenderer`'s ctor injection).

- [ ] **Step 1: Add the message key + spec**

In `WorkspaceKeys.cs`, add to `WorkspaceMessageKeys`:

```csharp
    /// <summary>Key for the per-server map control message (layer toggles).</summary>
    public const string ServerMap = "server.map";
```

In `ServerWorkspaceSpecProvider.GetMessageSpecs()`, add:

```csharp
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerMap, WorkspaceChannelKeys.ServerMap),
```

- [ ] **Step 2: Add the custom-id prefix**

In `src/RustPlusBot.Features.Workspace/WorkspaceComponentIds.cs` (the public ids class, referenced by `ConnectionComponentModule` as `WorkspaceComponentIds.ServerInfoSwapPrefix`), add — note the `workspace:` prefix matches the existing convention:

```csharp
    /// <summary>Prefix for a #map layer toggle button; "{layer}:{serverId}" is appended. Handled by Workspace.</summary>
    public const string MapTogglePrefix = "workspace:map:toggle:";
```

- [ ] **Step 3: Add EN/FR localization keys**

In `LocalizationCatalog.cs`, add keys for each layer label + a header (EN + FR). Example keys: `map.control.header`, `map.layer.grid`, `map.layer.markers`, `map.layer.monuments`, `map.layer.vendor`, `map.layer.players`, `map.layer.rigs`, plus an on/off suffix or rely on button colour. Match the catalog's existing add-pattern (look at `channel.map.name` already there).

- [ ] **Step 4: Write the failing renderer test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Messages/MapControlMessageRendererTests.cs`:

```csharp
[Fact]
public async Task Renders_six_toggle_buttons_reflecting_settings()
{
    // Arrange: fake IMapSettingsStore returning Monuments=false, rest on; culture "en".
    var payload = await renderer.RenderAsync(guildId, serverId, /* culture/context per the seam */);
    var buttons = /* extract ButtonComponents from payload.Components */;
    Assert.Equal(6, buttons.Count);
    // The Monuments button is styled "off" (e.g. Secondary), the rest "on" (Success).
}
```

> Match `ServerInfoMessageRenderer`'s seam signature exactly (sync vs async, what context it takes — culture, serverId). Extract buttons from the built `MessageComponent` the way existing Workspace renderer tests do (if any; else assert on the pre-build builder).

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter MapControlMessageRenderer`
Expected: FAIL — renderer not defined.

- [ ] **Step 6: Implement `MapControlMessageRenderer`**

Implement the same seam `ServerInfoMessageRenderer` uses. It reads `IMapSettingsStore.GetAsync` + the localizer + culture, and builds six buttons via `ComponentBuilder().WithButton(label, $"{WorkspaceComponentIds.MapTogglePrefix}{layer}:{serverId}", style)` where `style = enabled ? ButtonStyle.Success : ButtonStyle.Secondary`. Layer→`MapLayer` enum string in the custom id (e.g. `MapLayer.Grid.ToString()`). Rows: Discord caps 5 buttons/row → two rows (4 + 2 or 3 + 3).

> Reuse the scoped-store access pattern of the seam: if the renderer seam is invoked inside a reconcile scope, `IMapSettingsStore` may be directly injectable (scoped renderer). Confirm whether Workspace renderers are scoped (likely) — if so, inject `IMapSettingsStore` directly.

- [ ] **Step 7: Register the renderer**

At the Workspace renderer registration site (where `ServerInfoMessageRenderer` is added — keyed by message key), register `MapControlMessageRenderer` under `WorkspaceMessageKeys.ServerMap`. Match the exact registration idiom (keyed service / dictionary / `AddScoped` + key).

- [ ] **Step 8: Implement `MapComponentModule`**

Create `src/RustPlusBot.Features.Workspace/Modules/MapComponentModule.cs` (mirror `ConnectionComponentModule`):

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Workspace.Modules;

/// <summary>Handles the #map layer-toggle buttons (ManageGuild).</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="eventBus">Publishes a settings-changed event to trigger an immediate repaint.</param>
public sealed class MapComponentModule(IServiceScopeFactory scopeFactory, IEventBus eventBus)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Flips one layer, re-renders the control message, and triggers a map repaint.</summary>
    /// <param name="tail">The "{layer}:{serverId}" tail captured from the custom id.</param>
    // Custom id: WorkspaceComponentIds.MapTogglePrefix + "*" = "workspace:map:toggle:*"
    // Single wildcard captures the whole tail (mirrors ServerInfoSwapPrefix); we split it ourselves.
    [ComponentInteraction(WorkspaceComponentIds.MapTogglePrefix + "*")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task ToggleAsync(string tail)
    {
        ArgumentNullException.ThrowIfNull(tail);
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var parts = tail.Split(':');
        if (parts.Length != 2
            || !Enum.TryParse<MapLayer>(parts[0], ignoreCase: false, out var layer)
            || !Guid.TryParse(parts[1], out var serverId))
        {
            await RespondAsync("That control wasn't valid.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IMapSettingsStore>();
            var current = await store.GetAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
            var newValue = !IsEnabled(current, layer);
            await store.SetLayerAsync(Context.Guild.Id, serverId, layer, newValue).ConfigureAwait(false);

            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            await reconciler.ReconcileServerAsync(Context.Guild.Id, serverId).ConfigureAwait(false);
        }

        await eventBus.PublishAsync(new MapSettingsChangedEvent(Context.Guild.Id, serverId)).ConfigureAwait(false);
        await FollowupAsync("Updated map layers.", ephemeral: true).ConfigureAwait(false);
    }

    private static bool IsEnabled(MapLayerSettings s, MapLayer layer) => layer switch
    {
        MapLayer.Grid => s.Grid,
        MapLayer.Markers => s.Markers,
        MapLayer.Monuments => s.Monuments,
        MapLayer.Vendor => s.Vendor,
        MapLayer.Players => s.Players,
        MapLayer.Rigs => s.Rigs,
        _ => true,
    };
}
```

> Verify the exact names: `IWorkspaceReconciler.ReconcileServerAsync(guildId, serverId)` (used by `WorkspaceHostedService` per the memory) and `IEventBus.PublishAsync(...)`. The `[ComponentInteraction]` wildcard `"map:toggle:*:*"` maps two `*` segments to the two string params (Discord.Net splits on the wildcard positions) — CONFIRM the framework's multi-wildcard capture; if it captures the whole tail as one arg, switch the custom id to a single wildcard and split manually (mirror however `ConnectionComponentModule`'s single `*` works, and prefer the simplest form: `map:toggle:*` capturing `"{layer}:{serverId}"`, then `Split(':')`).

- [ ] **Step 9: Run renderer test + bump count tests**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter MapControlMessageRenderer` → PASS.
Then run the full Workspace test suite. If any test asserts the **message-spec count** for the per-server scope (now 2: info + map), bump it. Search: `grep -rn "GetMessageSpecs\|MessageSpec" tests/RustPlusBot.Features.Workspace.Tests`.

- [ ] **Step 10: Run full suite + commit**

Run: `dotnet test` — all assemblies green, counts hold/bumped intentionally.

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests
git commit -m "feat(workspace): #map control message with ManageGuild layer toggles"
```

---

### Task 11: Final verification — build, format gate, drift, full suite

**Files:** none (verification only).

- [ ] **Step 1: Full build under strict analyzers**

Run: `dotnet build RustPlusBot.slnx -warnaserror`
Expected: 0 warnings / 0 errors. Fix any Roslynator nits (`RCS1141` missing `<param>`, `CA1031` broad-catch pragmas, `S1135`, `CA1822` on stateless instance methods, `CA1859` concrete-type preferences).

- [ ] **Step 2: Format gate**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Then `git status` — review any reordering, `git add` + commit as a `style:` commit if it touched only 2b-ii files.

```bash
git add -A
git commit -m "style(map): apply jb ReformatAndReorder"
```

- [ ] **Step 3: EF drift check**

Run: `dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host`
Expected: no pending changes. If the tooling errors (2a-ii quirk), verify manually: no entity/config/DbContext changes since the `MapSettings` migration, and the snapshot includes `ServerMapSettings`.

- [ ] **Step 4: Full suite + per-assembly counts**

Run: `dotnet test`
Expected: all green. Read the per-assembly summary — confirm Persistence (+ new Map store/schema tests), Features.Map (+ MapIcons/renderer/composer tests), Features.Workspace (+ control renderer test) all increased and NOTHING dropped to a suspiciously low count.

- [ ] **Step 5: Manual verify (optional, if a live server is available)**

Connect a server, open `#map`: confirm the image shows monuments (icons), teammate dots, vendor (if present), rig styling, grid; confirm the control message has six toggle buttons; toggle one (ManageGuild) and confirm the image repaints within ~throttle window and the button colour flips. Confirm the control message survives an image repaint (not deleted).

- [ ] **Step 6: Final commit (if any verification fixes)**

```bash
git add -A
git commit -m "chore(map): 2b-ii verification fixes"
```

---

## Self-Review (completed)

- **Spec coverage:** every spec section maps to a task — §1→T1+T2, §2→T6, §3→T3, §4→T4+T5+T6+T7, §5→T10, §6→T9, §7→T8, testing/shims/risks→distributed+T11. No gaps.
- **Placeholders:** test code uses the repo's REAL harness (`SqliteContextFixture.Create()`, `BaseJpeg()`/`Dims` in `MapComposerTests`/`MapRendererTests`, NSubstitute) — no invented fixtures. Names verified against source.
- **Type consistency:** `MapLayerSettings` (Persistence) vs `MapLayerSet` (Map) are distinct by design (composer maps 1:1); `MapLayer` order matches entity bools + buttons + `IsEnabled`; `GetMonumentsAsync`/`MonumentSnapshot.{Token,X,Y}`/`TeamMemberSnapshot.{Name,X,Y,IsAlive,IsOnline}`/`RigState.Status`+`RigStatus.Active`/`ReconcileServerAsync(guild,server,ct)`/`PublishAsync<T>(evt,ct)`/`IMessageRenderer.{MessageKey,RenderAsync}` all confirmed against the repo; custom-id `workspace:map:toggle:` + single-wildcard + `Split(':')` is self-consistent between renderer and module.
- **Existing-test breaks enumerated:** `MapLayerSetTests`, `MapRendererTests`, `MarkerGlyphsTests` (delete), `MapComposerTests` (ctor 4→6), `MapRegistrationTests` (new deps) — each called out in its owning task.
