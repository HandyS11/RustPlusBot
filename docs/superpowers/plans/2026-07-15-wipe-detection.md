# Server Wipe Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect a Rust server wipe on reconnect (persisted WipeTime/Seed/WorldSize baseline), announce it in the per-server #events channel (optionally pinging @everyone via a new global setting, default off), and auto-purge all paired smart devices (rows + Discord embeds) so no stale devices or alarm notifications survive the wipe.

**Architecture:** A new `RustPlusBot.Features.Wipes` feature project subscribes to the existing `ConnectionStatusChangedEvent`; on each transition to connected it reads `IRustServerQuery.GetServerInfoAsync`/`GetWorldAsync`, diffs against a baseline persisted on `RustServer` (new columns), and publishes a new `ServerWipedEvent` on the in-process bus. Alarms/Switches/StorageMonitors each subscribe and purge their own rows and channel messages. A `PingEveryoneOnWipe` flag on `GuildSettings` is surfaced as a toggle button in the #settings message.

**Tech Stack:** C# / .NET 10, EF Core 10 (SQLite), Discord.Net, xUnit + NSubstitute, resx localization (en/fr).

**Spec:** `docs/superpowers/specs/2026-07-15-wipe-detection-design.md`

**Spec deviation (agreed):** the spec's "Features.Pairing purges `PairedEntity` rows" step is DROPPED — nothing in production code ever inserts `PairedEntity` rows (verified: the only writer is a test; the only reader is `GuildPurgeService`), so a per-server wipe purge of that table would be dead code.

## Global Constraints

- Solution file is `RustPlusBot.slnx` (no `.sln`). Build with `dotnet build RustPlusBot.slnx`.
- **`-maxcpucount:1` is MANDATORY on EVERY `dotnet build` AND `dotnet test`.** A `ConfigureGitHooks BeforeTargets="Build"` target races on `.git/config` under parallel builds; a failed build silently DROPS an assembly's tests (they report 0 and look "passing"). Always read the per-assembly test counts, never just "passed".
- Run `dotnet tool restore` once before the first build (jb / ef / stryker / docfx are local manifest tools).
- Build is `-warnaserror` (`TreatWarningsAsErrors=true` in `Directory.Build.props`): every public/internal type and member needs XML `///` docs; `CA1305/CA1307/CA1310` → pass `CultureInfo.InvariantCulture` / `StringComparison.Ordinal`; `CA2007` → `.ConfigureAwait(false)` on every awaited task in `src/` (tests don't need it).
- Tests are plain xUnit `Assert.*` + NSubstitute. NO FluentAssertions. `using Xunit` is a global using — do not add it per-file.
- `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx` is a hard CI gate that fails on any diff — run it before every commit (local tool, `dotnet jb`). It is slow; running it once per task right before `git commit` is fine.
- EF migrations use the local tool: `dotnet ef` (dotnet-ef 10.0.9). Migrations live in `src/RustPlusBot.Persistence/Migrations`; `--project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host`. If `dotnet ef` errors with "Unable to retrieve project metadata", build the solution first and retry.
- Event-bus consumer loops follow the exact `AlarmsHostedService` shape: one `Task.Run` loop per event type, `await foreach (… in eventBus.SubscribeAsync<T>(ct))`, `OperationCanceledException` swallowed, broad `catch (Exception)` with `#pragma warning disable CA1031` + a `[LoggerMessage]` log.
- The localization parity test (`tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs`) fails if `Strings.resx` and `Strings.fr.resx` key sets differ — always add new keys to BOTH.
- Commit messages are plain imperative sentences (repo style, no `feat:` prefixes), each ending with the trailer line `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.
- `docs/superpowers/**` is tracked in git (NOT ignored) — commit the spec/plan normally.
- Feature branch: `feat/wipe-detection` off `develop`. Create it before Task 1: `git checkout -b feat/wipe-detection`.

---

### Task 1: Wipe baseline columns + wipe-ping guild setting (Domain + migration)

Add the persisted state everything else diffs against: three nullable baseline columns on `RustServer` and a `PingEveryoneOnWipe` bool on `GuildSettings`, in one EF migration.

**Files:**
- Modify: `src/RustPlusBot.Domain/Servers/RustServer.cs`
- Modify: `src/RustPlusBot.Domain/Guilds/GuildSettings.cs`
- Modify: `src/RustPlusBot.Persistence/Configurations/RustServerConfiguration.cs`
- Create (generated): `src/RustPlusBot.Persistence/Migrations/*_WipeDetection.cs` (+ `.Designer.cs`, snapshot update)
- Test: `tests/RustPlusBot.Persistence.Tests/Servers/RustServerWipeColumnsTests.cs` (create)

**Interfaces:**
- Consumes: nothing new.
- Produces: `RustServer.LastWipeTimeUtc` (`DateTimeOffset?`), `RustServer.LastMapSeed` (`uint?`), `RustServer.LastMapSize` (`uint?`), `GuildSettings.PingEveryoneOnWipe` (`bool`, default `false`). Task 2's store reads/writes these.

- [ ] **Step 1: Write the failing round-trip test**

Create `tests/RustPlusBot.Persistence.Tests/Servers/RustServerWipeColumnsTests.cs` (the `Servers/` test folder already exists; `SqliteContextFixture` applies the real migrations, so this fails until the migration exists):

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Servers;

/// <summary>Round-trips the wipe-baseline columns and the wipe-ping guild flag through the migrated schema.</summary>
public sealed class RustServerWipeColumnsTests
{
    [Fact]
    public async Task Wipe_baseline_columns_round_trip()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL,
            Name = "S",
            Ip = "1.1.1.1",
            Port = 28015,
            LastWipeTimeUtc = new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero),
            LastMapSeed = 123456u,
            LastMapSize = 4250u,
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var loaded = await context.RustServers.FindAsync(server.Id);

        Assert.NotNull(loaded);
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero), loaded.LastWipeTimeUtc);
        Assert.Equal(123456u, loaded.LastMapSeed);
        Assert.Equal(4250u, loaded.LastMapSize);
    }

    [Fact]
    public async Task New_server_has_empty_baseline_and_guild_ping_defaults_false()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;

        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        context.GuildSettings.Add(new GuildSettings
        {
            GuildId = 10UL
        });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var loadedServer = await context.RustServers.FindAsync(server.Id);
        var loadedGuild = await context.GuildSettings.SingleOrDefaultAsync(g => g.GuildId == 10UL);

        Assert.NotNull(loadedServer);
        Assert.Null(loadedServer.LastWipeTimeUtc);
        Assert.Null(loadedServer.LastMapSeed);
        Assert.Null(loadedServer.LastMapSize);
        Assert.NotNull(loadedGuild);
        Assert.False(loadedGuild.PingEveryoneOnWipe);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1 --filter RustServerWipeColumnsTests`
Expected: FAIL — compile error (`LastWipeTimeUtc` does not exist).

- [ ] **Step 3: Add the domain properties**

In `src/RustPlusBot.Domain/Servers/RustServer.cs`, append inside the class (after `FacepunchServerId`):

```csharp
    /// <summary>Baseline: the last observed wipe time (UTC) from getInfo, or null before first observation.</summary>
    public DateTimeOffset? LastWipeTimeUtc { get; set; }

    /// <summary>Baseline: the last observed procedural map seed, or null before first observation.</summary>
    public uint? LastMapSeed { get; set; }

    /// <summary>Baseline: the last observed world size (game units), or null before first observation.</summary>
    public uint? LastMapSize { get; set; }
```

In `src/RustPlusBot.Domain/Guilds/GuildSettings.cs`, append inside the class:

```csharp
    /// <summary>When true, the server-wiped announcement pings @everyone in #events. Off by default.</summary>
    public bool PingEveryoneOnWipe { get; set; }
```

In `src/RustPlusBot.Persistence/Configurations/RustServerConfiguration.cs`, add explicit `long` conversions for the `uint?` columns (keeps the model portable to providers without native uint support), after the existing `Property` lines:

```csharp
        builder.Property(s => s.LastMapSeed).HasConversion<long>();
        builder.Property(s => s.LastMapSize).HasConversion<long>();
```

- [ ] **Step 4: Generate the migration**

Run (from repo root):

```bash
dotnet build RustPlusBot.slnx -maxcpucount:1
dotnet ef migrations add WipeDetection --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host
```

Expected: a new `Migrations/<timestamp>_WipeDetection.cs` whose `Up` adds nullable `LastWipeTimeUtc` (TEXT), `LastMapSeed` (INTEGER), `LastMapSize` (INTEGER) to `RustServers`, and non-nullable `PingEveryoneOnWipe` (INTEGER, `defaultValue: false`) to `GuildSettings`. Inspect the file to confirm exactly these four columns and nothing else.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1`
Expected: PASS, including the 2 new tests (check per-assembly counts).

Also verify no model drift: `dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host`
Expected: "No changes have been made to the model since the last migration."

- [ ] **Step 6: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Add wipe baseline columns and wipe-ping guild setting

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: WipeBaselineStore + wipe-ping accessors on IWorkspaceStore

Persistence seams the detector and announcer will use: a single-purpose baseline store over the new `RustServer` columns, and `Get/SetPingEveryoneOnWipeAsync` beside the existing culture accessors.

**Files:**
- Create: `src/RustPlusBot.Persistence/Wipes/WipeBaseline.cs`
- Create: `src/RustPlusBot.Persistence/Wipes/IWipeBaselineStore.cs`
- Create: `src/RustPlusBot.Persistence/Wipes/WipeBaselineStore.cs`
- Modify: `src/RustPlusBot.Persistence/Workspace/IWorkspaceStore.cs` (two methods, next to `GetCultureAsync`/`SetCultureAsync`)
- Modify: `src/RustPlusBot.Persistence/Workspace/WorkspaceStore.cs`
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs` (register the store)
- Test: `tests/RustPlusBot.Persistence.Tests/Wipes/WipeBaselineStoreTests.cs` (create)
- Test: `tests/RustPlusBot.Persistence.Tests/Workspace/WorkspaceStoreWipePingTests.cs` (create)

**Interfaces:**
- Consumes: Task 1's columns.
- Produces (used by Tasks 3 and 5):

```csharp
public sealed record WipeBaseline(DateTimeOffset? WipeTimeUtc, uint? MapSeed, uint? MapSize);

public interface IWipeBaselineStore
{
    Task<WipeBaseline?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
    Task SetAsync(ulong guildId, Guid serverId, WipeBaseline baseline, CancellationToken cancellationToken = default);
}

// added to IWorkspaceStore:
Task<bool> GetPingEveryoneOnWipeAsync(ulong guildId, CancellationToken cancellationToken = default);
Task SetPingEveryoneOnWipeAsync(ulong guildId, bool enabled, CancellationToken cancellationToken = default);
```

`GetAsync` returns **null when the server row does not exist** (server removed mid-check) and a `WipeBaseline` with all-null members for a never-observed server — callers distinguish the two.

- [ ] **Step 1: Write the failing store tests**

Create `tests/RustPlusBot.Persistence.Tests/Wipes/WipeBaselineStoreTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Wipes;

namespace RustPlusBot.Persistence.Tests.Wipes;

/// <summary>Unit tests for <see cref="WipeBaselineStore"/>.</summary>
public sealed class WipeBaselineStoreTests
{
    private static (WipeBaselineStore Store, BotDbContext Context, SqliteConnection Conn) Create()
    {
        var (context, connection) = SqliteContextFixture.Create();
        return (new WipeBaselineStore(context), context, connection);
    }

    private static async Task<Guid> SeedServerAsync(BotDbContext context)
    {
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        return server.Id;
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_server()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;

        var baseline = await store.GetAsync(10UL, Guid.NewGuid());

        Assert.Null(baseline);
    }

    [Fact]
    public async Task Get_returns_empty_baseline_for_new_server()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var baseline = await store.GetAsync(10UL, serverId);

        Assert.Equal(new WipeBaseline(null, null, null), baseline);
    }

    [Fact]
    public async Task Set_then_Get_round_trips()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);
        var baseline = new WipeBaseline(new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero), 42u, 3500u);

        await store.SetAsync(10UL, serverId, baseline);

        Assert.Equal(baseline, await store.GetAsync(10UL, serverId));
    }

    [Fact]
    public async Task Set_is_noop_for_unknown_server()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;

        await store.SetAsync(10UL, Guid.NewGuid(), new WipeBaseline(null, 42u, null));

        // No throw; nothing persisted.
        Assert.Null(await store.GetAsync(10UL, Guid.NewGuid()));
    }

    [Fact]
    public async Task Get_is_guild_scoped()
    {
        var (store, context, conn) = Create();
        await using var _ = conn;
        await using var __ = context;
        var serverId = await SeedServerAsync(context);

        var baseline = await store.GetAsync(999UL, serverId);

        Assert.Null(baseline);
    }
}
```

Create `tests/RustPlusBot.Persistence.Tests/Workspace/WorkspaceStoreWipePingTests.cs` (mirror the `WorkspaceStore` construction used by the existing tests in that folder — it takes the context; check the neighboring test file's `Create` helper and copy its constructor arguments exactly if it differs):

```csharp
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Persistence.Tests.Workspace;

/// <summary>Wipe-ping accessor tests for <see cref="WorkspaceStore"/>.</summary>
public sealed class WorkspaceStoreWipePingTests
{
    [Fact]
    public async Task Ping_defaults_false_without_settings_row()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;
        var store = new WorkspaceStore(context);

        Assert.False(await store.GetPingEveryoneOnWipeAsync(10UL));
    }

    [Fact]
    public async Task Set_true_then_get_round_trips_and_upserts_row()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;
        var store = new WorkspaceStore(context);

        await store.SetPingEveryoneOnWipeAsync(10UL, enabled: true);

        Assert.True(await store.GetPingEveryoneOnWipeAsync(10UL));
        // The upserted row keeps the default culture.
        Assert.Equal("en", await store.GetCultureAsync(10UL));
    }

    [Fact]
    public async Task Set_preserves_existing_culture()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = connection;
        await using var __ = context;
        var store = new WorkspaceStore(context);
        await store.SetCultureAsync(10UL, "fr");

        await store.SetPingEveryoneOnWipeAsync(10UL, enabled: true);

        Assert.Equal("fr", await store.GetCultureAsync(10UL));
        Assert.True(await store.GetPingEveryoneOnWipeAsync(10UL));
    }
}
```

> If `WorkspaceStore`'s constructor takes more than the context, copy the construction from the existing `Workspace/` tests.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1 --filter "WipeBaselineStoreTests|WorkspaceStoreWipePingTests"`
Expected: FAIL — compile errors (types/methods missing).

- [ ] **Step 3: Implement**

Create `src/RustPlusBot.Persistence/Wipes/WipeBaseline.cs`:

```csharp
namespace RustPlusBot.Persistence.Wipes;

/// <summary>The persisted wipe baseline for a server: the last observed wipe time and world identity. All-null before the first observation.</summary>
/// <param name="WipeTimeUtc">The last observed wipe time (UTC), or null when the server never reported one.</param>
/// <param name="MapSeed">The last observed procedural map seed, or null before first observation.</param>
/// <param name="MapSize">The last observed world size (game units), or null before first observation.</param>
public sealed record WipeBaseline(DateTimeOffset? WipeTimeUtc, uint? MapSeed, uint? MapSize);
```

Create `src/RustPlusBot.Persistence/Wipes/IWipeBaselineStore.cs`:

```csharp
namespace RustPlusBot.Persistence.Wipes;

/// <summary>Reads/writes the per-server wipe baseline stored on the RustServer row.</summary>
public interface IWipeBaselineStore
{
    /// <summary>Gets the baseline, or null when the server row does not exist.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The baseline (all-null members before first observation), or null for an unknown server.</returns>
    Task<WipeBaseline?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Overwrites the baseline (no-op when the server row does not exist).</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="baseline">The new baseline values.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the baseline has been persisted.</returns>
    Task SetAsync(ulong guildId, Guid serverId, WipeBaseline baseline, CancellationToken cancellationToken = default);
}
```

Create `src/RustPlusBot.Persistence/Wipes/WipeBaselineStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace RustPlusBot.Persistence.Wipes;

/// <summary>Default <see cref="IWipeBaselineStore"/> over the RustServer baseline columns.</summary>
/// <param name="context">The bot database context.</param>
internal sealed class WipeBaselineStore(BotDbContext context) : IWipeBaselineStore
{
    /// <inheritdoc />
    public async Task<WipeBaseline?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        var server = await context.RustServers
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.Id == serverId, cancellationToken)
            .ConfigureAwait(false);
        return server is null
            ? null
            : new WipeBaseline(server.LastWipeTimeUtc, server.LastMapSeed, server.LastMapSize);
    }

    /// <inheritdoc />
    public async Task SetAsync(ulong guildId, Guid serverId, WipeBaseline baseline, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var server = await context.RustServers
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.Id == serverId, cancellationToken)
            .ConfigureAwait(false);
        if (server is null)
        {
            return;
        }

        server.LastWipeTimeUtc = baseline.WipeTimeUtc;
        server.LastMapSeed = baseline.MapSeed;
        server.LastMapSize = baseline.MapSize;
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

> If the compiler rejects `internal` here because `Persistence.Tests` can't see it, check `src/RustPlusBot.Persistence/RustPlusBot.Persistence.csproj` for an `InternalsVisibleTo` — `AlarmStore` is constructed directly by its tests, so the mechanism already exists; mirror `AlarmStore`'s access modifiers exactly.

In `src/RustPlusBot.Persistence/Workspace/IWorkspaceStore.cs`, add right after `SetCultureAsync`:

```csharp
    /// <summary>True when the guild wants the server-wiped announcement to ping @everyone. Defaults to false.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The current flag value.</returns>
    Task<bool> GetPingEveryoneOnWipeAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Sets the @everyone-on-wipe flag (upserting the GuildSettings row).</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="enabled">The new flag value.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the flag has been persisted.</returns>
    Task SetPingEveryoneOnWipeAsync(ulong guildId, bool enabled, CancellationToken cancellationToken = default);
```

In `src/RustPlusBot.Persistence/Workspace/WorkspaceStore.cs`, add right after the `SetCultureAsync` implementation (mirroring its upsert shape):

```csharp
    /// <inheritdoc />
    public async Task<bool> GetPingEveryoneOnWipeAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var settings = await context.GuildSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);
        return settings?.PingEveryoneOnWipe ?? false;
    }

    /// <inheritdoc />
    public async Task SetPingEveryoneOnWipeAsync(ulong guildId, bool enabled, CancellationToken cancellationToken = default)
    {
        var settings = await context.GuildSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);

        if (settings is null)
        {
            context.GuildSettings.Add(new GuildSettings
            {
                GuildId = guildId, PingEveryoneOnWipe = enabled
            });
        }
        else
        {
            settings.PingEveryoneOnWipe = enabled;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
```

In `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`, add a using `RustPlusBot.Persistence.Wipes;` and register after the `IMapSettingsStore` line:

```csharp
        services.AddScoped<IWipeBaselineStore, WipeBaselineStore>();
```

Note: adding methods to `IWorkspaceStore` is safe for existing NSubstitute mocks (substitutes auto-implement new members returning defaults — `false` here, which is the correct default).

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1`
Expected: PASS (8 new tests green, no regressions).

- [ ] **Step 5: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Add wipe baseline store and wipe-ping workspace accessors

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Features.Wipes project + ServerWipedEvent + WipeDetector

The new feature project, the bus event, and the diff logic. The project is registered in the solution and Host now; renderer/poster/announcer/hosted-service arrive in Tasks 4–6.

**Files:**
- Create: `src/RustPlusBot.Abstractions/Events/ServerWipedEvent.cs`
- Create: `src/RustPlusBot.Features.Wipes/RustPlusBot.Features.Wipes.csproj`
- Create: `src/RustPlusBot.Features.Wipes/Detection/IWipeDetector.cs`
- Create: `src/RustPlusBot.Features.Wipes/Detection/WipeDetector.cs`
- Create: `src/RustPlusBot.Features.Wipes/WipeServiceCollectionExtensions.cs`
- Create: `tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj`
- Modify: `RustPlusBot.slnx` (two `<Project>` entries)
- Modify: `src/RustPlusBot.Host/Program.cs` (`AddWipes()`)
- Test: `tests/RustPlusBot.Features.Wipes.Tests/WipeDetectorTests.cs` (create)

**Interfaces:**
- Consumes: `IRustServerQuery.GetServerInfoAsync/GetWorldAsync` (singleton), `IWipeBaselineStore` (scoped, Task 2), `IEventBus.PublishAsync`.
- Produces:

```csharp
public sealed record ServerWipedEvent(
    ulong GuildId,
    Guid ServerId,
    DateTimeOffset? PreviousWipeTimeUtc,
    DateTimeOffset? NewWipeTimeUtc,
    uint Seed,
    uint WorldSize);

internal interface IWipeDetector
{
    Task CheckAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Create the projects and wire them up**

Create `src/RustPlusBot.Abstractions/Events/ServerWipedEvent.cs`:

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a reconnected server's wipe baseline no longer matches (the server wiped while we were away or restarting).</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The wiped server id.</param>
/// <param name="PreviousWipeTimeUtc">The baseline wipe time before this wipe, or null if never observed.</param>
/// <param name="NewWipeTimeUtc">The freshly observed wipe time, or null when the server does not report one.</param>
/// <param name="Seed">The new procedural map seed.</param>
/// <param name="WorldSize">The new world size (game units).</param>
public sealed record ServerWipedEvent(
    ulong GuildId,
    Guid ServerId,
    DateTimeOffset? PreviousWipeTimeUtc,
    DateTimeOffset? NewWipeTimeUtc,
    uint Seed,
    uint WorldSize);
```

Create `src/RustPlusBot.Features.Wipes/RustPlusBot.Features.Wipes.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="RustPlusBot.Features.Wipes.Tests" />
    <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
    <ProjectReference Include="..\RustPlusBot.Localization\RustPlusBot.Localization.csproj" />
    <ProjectReference Include="..\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
    <ProjectReference Include="..\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
    <ProjectReference Include="..\RustPlusBot.Domain\RustPlusBot.Domain.csproj" />
    <ProjectReference Include="..\RustPlusBot.Features.Workspace\RustPlusBot.Features.Workspace.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Discord.Net" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
  </ItemGroup>

</Project>
```

Create `tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Discord.Net" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\RustPlusBot.Features.Wipes\RustPlusBot.Features.Wipes.csproj" />
  </ItemGroup>

</Project>
```

In `RustPlusBot.slnx`, add inside the `/src/` folder (after the `Features.Workspace` line):

```xml
    <Project Path="src/RustPlusBot.Features.Wipes/RustPlusBot.Features.Wipes.csproj" />
```

and inside the `/tests/` folder (after the `Features.Workspace.Tests` line):

```xml
    <Project Path="tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj" />
```

- [ ] **Step 2: Write the failing detector tests**

Create `tests/RustPlusBot.Features.Wipes.Tests/WipeDetectorTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Detection;
using RustPlusBot.Persistence.Wipes;

namespace RustPlusBot.Features.Wipes.Tests;

/// <summary>Unit tests for <see cref="WipeDetector"/>'s diff rules.</summary>
public sealed class WipeDetectorTests
{
    private static readonly DateTimeOffset OldWipe = new(2026, 6, 4, 18, 0, 0, TimeSpan.Zero);
    private static readonly Guid ServerId = Guid.NewGuid();

    private sealed record Harness(WipeDetector Detector, IRustServerQuery Query, IWipeBaselineStore Store, IEventBus Bus);

    private static Harness Create(
        ServerInfoSnapshot? info,
        WorldSnapshot? world,
        WipeBaseline? baseline)
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetServerInfoAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(info);
        query.GetWorldAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(world);

        var store = Substitute.For<IWipeBaselineStore>();
        store.GetAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(baseline);

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var bus = Substitute.For<IEventBus>();
        var detector = new WipeDetector(scopeFactory, query, bus, NullLogger<WipeDetector>.Instance);
        return new Harness(detector, query, store, bus);
    }

    private static ServerInfoSnapshot Info(DateTimeOffset? wipeTime) => new(5, 100, 0, wipeTime);

    [Fact]
    public async Task Null_info_snapshot_aborts_silently()
    {
        var h = Create(info: null, world: new WorldSnapshot(3500u, 42u), baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().SetAsync(default, default, null!, default);
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Null_world_snapshot_aborts_silently()
    {
        var h = Create(info: Info(OldWipe), world: null, baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Missing_server_row_aborts_silently()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 42u), baseline: null);

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().SetAsync(default, default, null!, default);
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Empty_baseline_backfills_without_event()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 42u), baseline: new WipeBaseline(null, null, null));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.Received(1).SetAsync(10UL, ServerId, new WipeBaseline(OldWipe, 42u, 3500u), Arg.Any<CancellationToken>());
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Unchanged_baseline_is_a_noop()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 42u), baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().SetAsync(default, default, null!, default);
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Wipe_time_advance_beyond_tolerance_publishes_event()
    {
        var newWipe = OldWipe.AddDays(7);
        var h = Create(info: Info(newWipe), world: new WorldSnapshot(3500u, 42u), baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.Received(1).SetAsync(10UL, ServerId, new WipeBaseline(newWipe, 42u, 3500u), Arg.Any<CancellationToken>());
        await h.Bus.Received(1).PublishAsync(
            new ServerWipedEvent(10UL, ServerId, OldWipe, newWipe, 42u, 3500u),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wipe_time_jitter_within_tolerance_refreshes_baseline_without_event()
    {
        var jittered = OldWipe.AddSeconds(30);
        var h = Create(info: Info(jittered), world: new WorldSnapshot(3500u, 42u), baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.Received(1).SetAsync(10UL, ServerId, new WipeBaseline(jittered, 42u, 3500u), Arg.Any<CancellationToken>());
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }

    [Fact]
    public async Task Seed_change_publishes_event()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 999u), baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Bus.Received(1).PublishAsync(
            new ServerWipedEvent(10UL, ServerId, OldWipe, OldWipe, 999u, 3500u),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Size_change_publishes_event()
    {
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(4250u, 42u), baseline: new WipeBaseline(OldWipe, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Bus.Received(1).PublishAsync(
            new ServerWipedEvent(10UL, ServerId, OldWipe, OldWipe, 42u, 4250u),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wipe_time_appearing_from_null_backfills_without_event()
    {
        // Baseline had seed/size but no wipe time (server started reporting it): not a wipe.
        var h = Create(info: Info(OldWipe), world: new WorldSnapshot(3500u, 42u), baseline: new WipeBaseline(null, 42u, 3500u));

        await h.Detector.CheckAsync(10UL, ServerId, CancellationToken.None);

        await h.Store.Received(1).SetAsync(10UL, ServerId, new WipeBaseline(OldWipe, 42u, 3500u), Arg.Any<CancellationToken>());
        await h.Bus.DidNotReceiveWithAnyArgs().PublishAsync<ServerWipedEvent>(null!, default);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj -maxcpucount:1`
Expected: FAIL — `WipeDetector` does not exist.

- [ ] **Step 4: Implement the detector**

Create `src/RustPlusBot.Features.Wipes/Detection/IWipeDetector.cs`:

```csharp
namespace RustPlusBot.Features.Wipes.Detection;

/// <summary>Checks a freshly connected server against its persisted wipe baseline.</summary>
internal interface IWipeDetector
{
    /// <summary>Reads live info/world, diffs against the baseline, and publishes <see cref="RustPlusBot.Abstractions.Events.ServerWipedEvent"/> on a wipe.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The server that just connected.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the check (and any event publication) has finished.</returns>
    Task CheckAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

Create `src/RustPlusBot.Features.Wipes/Detection/WipeDetector.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Persistence.Wipes;

namespace RustPlusBot.Features.Wipes.Detection;

/// <summary>
/// Default <see cref="IWipeDetector"/>. A wipe always implies a server restart, so the check runs on each
/// transition to connected: the server wiped when the wipe time advanced beyond a small jitter tolerance
/// or the map seed/size changed. The first-ever observation backfills the baseline silently so existing
/// deployments never see a false wipe on upgrade.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped baseline store.</param>
/// <param name="query">Reads live server info from the socket.</param>
/// <param name="eventBus">Publishes <see cref="ServerWipedEvent"/>.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class WipeDetector(
    IServiceScopeFactory scopeFactory,
    IRustServerQuery query,
    IEventBus eventBus,
    ILogger<WipeDetector> logger) : IWipeDetector
{
    /// <summary>Absorbs clock jitter in the reported wipe time across restarts; anything larger counts as a wipe.</summary>
    private static readonly TimeSpan _wipeTimeTolerance = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    public async Task CheckAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var info = await query.GetServerInfoAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        var world = await query.GetWorldAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (info is null || world is null)
        {
            return; // Socket dropped mid-check; the next reconnect retries.
        }

        var observed = new WipeBaseline(info.WipeTimeUtc, world.Seed, world.WorldSize);
        WipeBaseline? baseline;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWipeBaselineStore>();
            baseline = await store.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
            if (baseline is null || baseline == observed)
            {
                return; // Server removed mid-check, or plain reconnect with nothing changed.
            }

            await store.SetAsync(guildId, serverId, observed, cancellationToken).ConfigureAwait(false);
        }

        if (baseline is { WipeTimeUtc: null, MapSeed: null, MapSize: null })
        {
            LogBaselineStored(logger, guildId, serverId);
            return; // First observation ever: backfill silently.
        }

        var wiped =
            (info.WipeTimeUtc is { } newWipe && baseline.WipeTimeUtc is { } oldWipe
                                             && newWipe > oldWipe + _wipeTimeTolerance)
            || (baseline.MapSeed is { } oldSeed && world.Seed != oldSeed)
            || (baseline.MapSize is { } oldSize && world.WorldSize != oldSize);
        if (!wiped)
        {
            return; // Jitter within tolerance or a null field backfilled — baseline refreshed silently.
        }

        await eventBus.PublishAsync(
                new ServerWipedEvent(guildId, serverId, baseline.WipeTimeUtc, info.WipeTimeUtc, world.Seed,
                    world.WorldSize),
                cancellationToken)
            .ConfigureAwait(false);
        LogWipeDetected(logger, guildId, serverId, world.Seed, world.WorldSize);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Stored first wipe baseline for guild {GuildId} server {ServerId}.")]
    private static partial void LogBaselineStored(ILogger logger, ulong guildId, Guid serverId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Wipe detected for guild {GuildId} server {ServerId}: seed {Seed}, size {WorldSize}.")]
    private static partial void LogWipeDetected(ILogger logger, ulong guildId, Guid serverId, uint seed,
        uint worldSize);
}
```

Create `src/RustPlusBot.Features.Wipes/WipeServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Wipes.Detection;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Wipes;

/// <summary>DI registration for the wipe-detection feature.</summary>
public static class WipeServiceCollectionExtensions
{
    /// <summary>Registers the wipe detector (announcer/renderer/poster/hosted service are added by later slices).</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddWipes(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRustPlusBotLocalization();
        services.AddSingleton<IWipeDetector, WipeDetector>();

        return services;
    }
}
```

In `src/RustPlusBot.Host/Program.cs`, add `using RustPlusBot.Features.Wipes;` with the other feature usings and, right after `builder.Services.AddStorageMonitors();`:

```csharp
builder.Services.AddWipes();
```

- [ ] **Step 5: Build + run the tests**

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1` then `dotnet test tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj -maxcpucount:1`
Expected: build clean, 10 detector tests PASS.

- [ ] **Step 6: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Add Features.Wipes project with wipe detector and ServerWipedEvent

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Wipe announcement embed renderer + localization keys

**Files:**
- Create: `src/RustPlusBot.Features.Wipes/Rendering/WipeEmbedRenderer.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`
- Modify: `src/RustPlusBot.Localization/Strings.fr.resx`
- Modify: `src/RustPlusBot.Features.Wipes/WipeServiceCollectionExtensions.cs` (register renderer)
- Test: `tests/RustPlusBot.Features.Wipes.Tests/WipeEmbedRendererTests.cs` (create)

**Interfaces:**
- Consumes: `ServerWipedEvent` (Task 3), `ILocalizer`.
- Produces: `internal sealed class WipeEmbedRenderer` with `Embed Render(ServerWipedEvent evt, string culture)` — Task 5's announcer calls it.

- [ ] **Step 1: Write the failing renderer tests**

Create `tests/RustPlusBot.Features.Wipes.Tests/WipeEmbedRendererTests.cs`:

```csharp
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Wipes.Tests;

/// <summary>Unit tests for <see cref="WipeEmbedRenderer"/>.</summary>
public sealed class WipeEmbedRendererTests
{
    private static readonly DateTimeOffset WipedAt = new(2026, 7, 2, 18, 0, 0, TimeSpan.Zero);

    private static ServerWipedEvent Event(DateTimeOffset? newWipe = null) =>
        new(10UL, Guid.NewGuid(), WipedAt.AddDays(-7), newWipe ?? WipedAt, 999u, 4250u);

    [Fact]
    public void Render_includes_title_body_and_fields()
    {
        var renderer = new WipeEmbedRenderer(new ResxLocalizer());

        var embed = renderer.Render(Event(), "en");

        Assert.Equal("🧹 Server wiped", embed.Title);
        Assert.False(string.IsNullOrEmpty(embed.Description));
        Assert.Contains(embed.Fields, f => f.Value == $"<t:{WipedAt.ToUnixTimeSeconds()}:R>");
        Assert.Contains(embed.Fields, f => f.Value == "4250");
        Assert.Contains(embed.Fields, f => f.Value == "999");
    }

    [Fact]
    public void Render_omits_wiped_at_field_when_wipe_time_unknown()
    {
        var renderer = new WipeEmbedRenderer(new ResxLocalizer());

        var embed = renderer.Render(Event() with
        {
            NewWipeTimeUtc = null
        }, "en");

        Assert.Equal(2, embed.Fields.Length);
    }

    [Fact]
    public void Render_localizes_to_french()
    {
        var renderer = new WipeEmbedRenderer(new ResxLocalizer());

        var en = renderer.Render(Event(), "en");
        var fr = renderer.Render(Event(), "fr");

        Assert.NotEqual(en.Description, fr.Description);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj -maxcpucount:1 --filter WipeEmbedRendererTests`
Expected: FAIL — `WipeEmbedRenderer` does not exist.

- [ ] **Step 3: Add the localization keys**

In `src/RustPlusBot.Localization/Strings.resx`, add before the closing `</root>` (same `<data>` shape as the existing entries):

```xml
  <data name="wipe.title" xml:space="preserve">
    <value>🧹 Server wiped</value>
  </data>
  <data name="wipe.body" xml:space="preserve">
    <value>The server has wiped. All paired smart devices were removed — pair them again in game to keep using your switches, alarms and storage monitors.</value>
  </data>
  <data name="wipe.field.wipedat" xml:space="preserve">
    <value>Wiped</value>
  </data>
  <data name="wipe.field.mapsize" xml:space="preserve">
    <value>Map size</value>
  </data>
  <data name="wipe.field.seed" xml:space="preserve">
    <value>Seed</value>
  </data>
  <data name="settings.wipeping.on" xml:space="preserve">
    <value>🔔 Ping @everyone on wipe: On</value>
  </data>
  <data name="settings.wipeping.off" xml:space="preserve">
    <value>🔕 Ping @everyone on wipe: Off</value>
  </data>
```

In `src/RustPlusBot.Localization/Strings.fr.resx`, add the same seven keys:

```xml
  <data name="wipe.title" xml:space="preserve">
    <value>🧹 Serveur wipé</value>
  </data>
  <data name="wipe.body" xml:space="preserve">
    <value>Le serveur a wipé. Tous les appareils connectés appairés ont été supprimés — appairez-les à nouveau en jeu pour continuer à utiliser vos interrupteurs, alarmes et moniteurs de stockage.</value>
  </data>
  <data name="wipe.field.wipedat" xml:space="preserve">
    <value>Wipe</value>
  </data>
  <data name="wipe.field.mapsize" xml:space="preserve">
    <value>Taille de la carte</value>
  </data>
  <data name="wipe.field.seed" xml:space="preserve">
    <value>Seed</value>
  </data>
  <data name="settings.wipeping.on" xml:space="preserve">
    <value>🔔 Ping @everyone au wipe : activé</value>
  </data>
  <data name="settings.wipeping.off" xml:space="preserve">
    <value>🔕 Ping @everyone au wipe : désactivé</value>
  </data>
```

(The `settings.wipeping.*` keys are consumed in Task 9; adding them now keeps the resx edits in one place and the parity test green throughout.)

- [ ] **Step 4: Implement the renderer**

Create `src/RustPlusBot.Features.Wipes/Rendering/WipeEmbedRenderer.cs`:

```csharp
using System.Globalization;
using Discord;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Wipes.Rendering;

/// <summary>Renders the server-wiped announcement embed posted in #events.</summary>
/// <param name="localizer">String resolution.</param>
internal sealed class WipeEmbedRenderer(ILocalizer localizer)
{
    /// <summary>Renders the announcement for a guild culture.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <returns>The built embed.</returns>
    public Embed Render(ServerWipedEvent evt, string culture)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var builder = new EmbedBuilder()
            .WithTitle(localizer.Get("wipe.title", culture))
            .WithDescription(localizer.Get("wipe.body", culture))
            .WithColor(Color.Orange);

        if (evt.NewWipeTimeUtc is { } wipedAt)
        {
            builder.AddField(localizer.Get("wipe.field.wipedat", culture),
                $"<t:{wipedAt.ToUnixTimeSeconds()}:R>", inline: true);
        }

        builder
            .AddField(localizer.Get("wipe.field.mapsize", culture),
                evt.WorldSize.ToString(CultureInfo.InvariantCulture), inline: true)
            .AddField(localizer.Get("wipe.field.seed", culture),
                evt.Seed.ToString(CultureInfo.InvariantCulture), inline: true);
        return builder.Build();
    }
}
```

In `WipeServiceCollectionExtensions.AddWipes`, add (with `using RustPlusBot.Features.Wipes.Rendering;`):

```csharp
        services.AddSingleton<WipeEmbedRenderer>();
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj -maxcpucount:1` and `dotnet test tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj -maxcpucount:1`
Expected: PASS (renderer tests + resx parity green).

- [ ] **Step 6: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Add wipe announcement embed renderer and localization keys

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Wipe channel poster + announcer

Posting a one-off message (optional `@everyone` content + embed) to the per-server #events channel.

**Files:**
- Create: `src/RustPlusBot.Features.Wipes/Posting/IWipeChannelPoster.cs`
- Create: `src/RustPlusBot.Features.Wipes/Posting/DiscordWipeChannelPoster.cs`
- Create: `src/RustPlusBot.Features.Wipes/Announcing/IWipeAnnouncer.cs`
- Create: `src/RustPlusBot.Features.Wipes/Announcing/WipeAnnouncer.cs`
- Modify: `src/RustPlusBot.Features.Wipes/WipeServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Wipes.Tests/WipeAnnouncerTests.cs` (create)

**Interfaces:**
- Consumes: `IEventChannelLocator` (Workspace singleton), `IWorkspaceStore.GetCultureAsync`/`GetPingEveryoneOnWipeAsync` (scoped), `WipeEmbedRenderer` (Task 4), `ServerWipedEvent`.
- Produces:

```csharp
internal interface IWipeChannelPoster
{
    Task PostAsync(ulong channelId, string? content, Embed embed, CancellationToken cancellationToken);
}

internal interface IWipeAnnouncer
{
    Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken cancellationToken);
}
```

- [ ] **Step 1: Write the failing announcer tests**

Create `tests/RustPlusBot.Features.Wipes.Tests/WipeAnnouncerTests.cs`:

```csharp
using Discord;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Announcing;
using RustPlusBot.Features.Wipes.Posting;
using RustPlusBot.Features.Wipes.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Wipes.Tests;

/// <summary>Unit tests for <see cref="WipeAnnouncer"/>.</summary>
public sealed class WipeAnnouncerTests
{
    private static readonly Guid ServerId = Guid.NewGuid();

    private sealed record Harness(WipeAnnouncer Announcer, IEventChannelLocator Locator, IWipeChannelPoster Poster);

    private static Harness Create(ulong? channelId = 777UL, bool ping = false, string culture = "en")
    {
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(culture);
        workspace.GetPingEveryoneOnWipeAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(ping);

        var services = new ServiceCollection();
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<IEventChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(channelId);
        var poster = Substitute.For<IWipeChannelPoster>();

        var announcer = new WipeAnnouncer(
            scopeFactory,
            locator,
            new WipeEmbedRenderer(new ResxLocalizer()),
            poster,
            NullLogger<WipeAnnouncer>.Instance);
        return new Harness(announcer, locator, poster);
    }

    private static ServerWipedEvent Event() =>
        new(10UL, ServerId, null, new DateTimeOffset(2026, 7, 2, 18, 0, 0, TimeSpan.Zero), 999u, 4250u);

    [Fact]
    public async Task Missing_events_channel_skips_posting()
    {
        var h = Create(channelId: null);

        await h.Announcer.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Poster.DidNotReceiveWithAnyArgs().PostAsync(default, default, null!, default);
    }

    [Fact]
    public async Task Posts_embed_without_ping_by_default()
    {
        var h = Create();

        await h.Announcer.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Poster.Received(1).PostAsync(
            777UL,
            null,
            Arg.Is<Embed>(e => e.Title == "🧹 Server wiped"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Posts_everyone_content_when_guild_setting_enabled()
    {
        var h = Create(ping: true);

        await h.Announcer.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Poster.Received(1).PostAsync(
            777UL,
            "@everyone",
            Arg.Any<Embed>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Renders_with_guild_culture()
    {
        var h = Create(culture: "fr");

        await h.Announcer.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Poster.Received(1).PostAsync(
            777UL,
            null,
            Arg.Is<Embed>(e => e.Title == "🧹 Serveur wipé"),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj -maxcpucount:1 --filter WipeAnnouncerTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement poster and announcer**

Create `src/RustPlusBot.Features.Wipes/Posting/IWipeChannelPoster.cs`:

```csharp
using Discord;

namespace RustPlusBot.Features.Wipes.Posting;

/// <summary>Posts the one-off wipe announcement to a Discord channel.</summary>
internal interface IWipeChannelPoster
{
    /// <summary>Posts <paramref name="embed"/> (with optional mention <paramref name="content"/>) to the channel.</summary>
    /// <param name="channelId">The target Discord channel id.</param>
    /// <param name="content">Optional message content (e.g. "@everyone"), or null for embed-only.</param>
    /// <param name="embed">The announcement embed.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been sent (or the failure swallowed).</returns>
    Task PostAsync(ulong channelId, string? content, Embed embed, CancellationToken cancellationToken);
}
```

Create `src/RustPlusBot.Features.Wipes/Posting/DiscordWipeChannelPoster.cs` (mirrors `DiscordAlarmChannelPoster.SendEveryonePingAsync`):

```csharp
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Features.Wipes.Posting;

/// <summary>Posts wipe announcements in #events via the gateway client. Untested integration shim.</summary>
/// <param name="client">The Discord socket client (raw send so the optional @everyone mention resolves).</param>
/// <param name="logger">The logger.</param>
internal sealed partial class DiscordWipeChannelPoster(
    DiscordSocketClient client,
    ILogger<DiscordWipeChannelPoster> logger) : IWipeChannelPoster
{
    /// <inheritdoc />
    public async Task PostAsync(ulong channelId, string? content, Embed embed, CancellationToken cancellationToken)
    {
        try
        {
            var options = new RequestOptions
            {
                CancelToken = cancellationToken
            };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false)
                is not ITextChannel channel)
            {
                return;
            }

            await channel.SendMessageAsync(
                    content,
                    embed: embed,
                    options: options,
                    allowedMentions: AllowedMentions.All)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup must not crash the wipe loop; swallow the failure.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogPostFailed(logger, ex, channelId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Posting the wipe announcement in channel {ChannelId} failed.")]
    private static partial void LogPostFailed(ILogger logger, Exception exception, ulong channelId);
}
```

Create `src/RustPlusBot.Features.Wipes/Announcing/IWipeAnnouncer.cs`:

```csharp
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Features.Wipes.Announcing;

/// <summary>Posts the wipe announcement embed to the wiped server's #events channel.</summary>
internal interface IWipeAnnouncer
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/>.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the announcement has been posted (or skipped).</returns>
    Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken cancellationToken);
}
```

Create `src/RustPlusBot.Features.Wipes/Announcing/WipeAnnouncer.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Posting;
using RustPlusBot.Features.Wipes.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Wipes.Announcing;

/// <summary>Default <see cref="IWipeAnnouncer"/>: resolves #events, the guild culture and the ping flag, then posts.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="locator">Resolves the #events channel id.</param>
/// <param name="renderer">Builds the announcement embed.</param>
/// <param name="poster">Sends the announcement message.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class WipeAnnouncer(
    IServiceScopeFactory scopeFactory,
    IEventChannelLocator locator,
    WipeEmbedRenderer renderer,
    IWipeChannelPoster poster,
    ILogger<WipeAnnouncer> logger) : IWipeAnnouncer
{
    /// <inheritdoc />
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(evt);
        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken)
            .ConfigureAwait(false);
        if (channelId is not { } cid)
        {
            LogNoEventsChannel(logger, evt.GuildId, evt.ServerId);
            return;
        }

        string culture;
        bool ping;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            culture = await workspace.GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
            ping = await workspace.GetPingEveryoneOnWipeAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        }

        var embed = renderer.Render(evt, culture);
        await poster.PostAsync(cid, ping ? "@everyone" : null, embed, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "No #events channel for guild {GuildId} server {ServerId}; skipping wipe announcement.")]
    private static partial void LogNoEventsChannel(ILogger logger, ulong guildId, Guid serverId);
}
```

In `WipeServiceCollectionExtensions.AddWipes`, add (with usings for `.Announcing` and `.Posting`):

```csharp
        services.AddSingleton<IWipeChannelPoster, DiscordWipeChannelPoster>();
        services.AddSingleton<IWipeAnnouncer, WipeAnnouncer>();
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj -maxcpucount:1`
Expected: PASS (all Wipes tests green).

- [ ] **Step 5: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Add wipe channel poster and announcer

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: WipesHostedService (bus wiring) + registration test

**Files:**
- Create: `src/RustPlusBot.Features.Wipes/Hosting/WipesHostedService.cs`
- Modify: `src/RustPlusBot.Features.Wipes/WipeServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Wipes.Tests/Hosting/WipesHostedServiceTests.cs` (create)
- Test: `tests/RustPlusBot.Features.Wipes.Tests/WipeRegistrationTests.cs` (create)

**Interfaces:**
- Consumes: `IEventBus`, `IWipeDetector` (Task 3), `IWipeAnnouncer` (Task 5), `ConnectionStatusChangedEvent`, `ServerWipedEvent`.
- Produces: the running feature. Nothing downstream consumes new APIs.

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Features.Wipes.Tests/Hosting/WipesHostedServiceTests.cs` (poll-until-deadline pattern copied from `AlarmsHostedServiceTests`):

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Announcing;
using RustPlusBot.Features.Wipes.Detection;
using RustPlusBot.Features.Wipes.Hosting;

namespace RustPlusBot.Features.Wipes.Tests.Hosting;

/// <summary>Verifies <see cref="WipesHostedService"/> routes bus events to the detector and announcer.</summary>
public sealed class WipesHostedServiceTests
{
    private sealed record Harness(
        WipesHostedService Service,
        InMemoryEventBus Bus,
        IWipeDetector Detector,
        IWipeAnnouncer Announcer);

    private static Harness Create()
    {
        var detector = Substitute.For<IWipeDetector>();
        var announcer = Substitute.For<IWipeAnnouncer>();
        var bus = new InMemoryEventBus();
        var service = new WipesHostedService(bus, detector, announcer,
            NullLogger<WipesHostedService>.Instance);
        return new Harness(service, bus, detector, announcer);
    }

    private static async Task WaitForCallAsync(object substitute, string methodName)
    {
        // Same poll-until-deadline shape as AlarmsHostedServiceTests: the consumer loops attach
        // asynchronously after StartAsync, so poll ReceivedCalls() instead of asserting immediately.
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        while (DateTimeOffset.UtcNow < deadline
               && !substitute.ReceivedCalls().Any(c => c.GetMethodInfo().Name == methodName))
        {
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task Connected_transition_runs_the_detector()
    {
        var h = Create();
        await h.Service.StartAsync(default);
        var serverId = Guid.NewGuid();

        await h.Bus.PublishAsync(new ConnectionStatusChangedEvent(10UL, serverId, IsConnected: true,
            WasConnected: false));
        await WaitForCallAsync(h.Detector, nameof(IWipeDetector.CheckAsync));

        await h.Detector.Received(1).CheckAsync(10UL, serverId, Arg.Any<CancellationToken>());
        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task Disconnect_does_not_run_the_detector()
    {
        var h = Create();
        await h.Service.StartAsync(default);

        await h.Bus.PublishAsync(new ConnectionStatusChangedEvent(10UL, Guid.NewGuid(), IsConnected: false,
            WasConnected: true));
        await Task.Delay(200);

        await h.Detector.DidNotReceiveWithAnyArgs().CheckAsync(default, default, default);
        await h.Service.StopAsync(default);
    }

    [Fact]
    public async Task ServerWipedEvent_routes_to_the_announcer()
    {
        var h = Create();
        await h.Service.StartAsync(default);
        var evt = new ServerWipedEvent(10UL, Guid.NewGuid(), null, null, 1u, 3500u);

        await h.Bus.PublishAsync(evt);
        await WaitForCallAsync(h.Announcer, nameof(IWipeAnnouncer.HandleServerWipedAsync));

        await h.Announcer.Received(1).HandleServerWipedAsync(evt, Arg.Any<CancellationToken>());
        await h.Service.StopAsync(default);
    }
}
```

> `ReceivedCalls()` is the public NSubstitute extension (`using NSubstitute;`) — the same one `AlarmsHostedServiceTests` polls.

Create `tests/RustPlusBot.Features.Wipes.Tests/WipeRegistrationTests.cs` (mirrors `AlarmRegistrationTests`):

```csharp
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Announcing;
using RustPlusBot.Features.Wipes.Detection;
using RustPlusBot.Features.Wipes.Posting;
using RustPlusBot.Features.Wipes.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Wipes;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Wipes.Tests;

/// <summary>Validates that <see cref="WipeServiceCollectionExtensions.AddWipes"/> registers all required services.</summary>
public sealed class WipeRegistrationTests
{
    /// <summary>Verifies core service descriptors are present after calling <see cref="WipeServiceCollectionExtensions.AddWipes"/>.</summary>
    [Fact]
    public void AddWipes_registers_core_services()
    {
        var services = new ServiceCollection();
        services.AddWipes();

        Assert.Contains(services, d => d.ServiceType == typeof(ILocalizer));
        Assert.Contains(services, d => d.ServiceType == typeof(IWipeDetector));
        Assert.Contains(services, d => d.ServiceType == typeof(WipeEmbedRenderer));
        Assert.Contains(services, d => d.ServiceType == typeof(IWipeChannelPoster));
        Assert.Contains(services, d => d.ServiceType == typeof(IWipeAnnouncer));
        Assert.Contains(services, d => d.ServiceType == typeof(IHostedService));
    }

    /// <summary>Verifies the container resolves key types without captive-dependency errors when all cross-layer deps are provided.</summary>
    [Fact]
    public void AddWipes_resolves_without_captive_dependency_errors()
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddSingleton(Substitute.For<IEventBus>());
        services.AddSingleton(Substitute.For<IRustServerQuery>());
        services.AddSingleton(Substitute.For<IEventChannelLocator>());
        services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig()));
        services.AddScoped(_ => Substitute.For<IWipeBaselineStore>());
        services.AddScoped(_ => Substitute.For<IWorkspaceStore>());

        services.AddWipes();

        using var provider = services.BuildServiceProvider(validateScopes: true);

        Assert.NotNull(provider.GetRequiredService<IWipeDetector>());
        Assert.NotNull(provider.GetRequiredService<WipeEmbedRenderer>());
        Assert.NotNull(provider.GetRequiredService<IWipeChannelPoster>());
        Assert.NotNull(provider.GetRequiredService<IWipeAnnouncer>());
        Assert.NotNull(provider.GetRequiredService<IHostedService>());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj -maxcpucount:1 --filter "WipesHostedServiceTests|WipeRegistrationTests"`
Expected: FAIL — `WipesHostedService` does not exist / `IHostedService` not registered.

- [ ] **Step 3: Implement the hosted service**

Create `src/RustPlusBot.Features.Wipes/Hosting/WipesHostedService.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Wipes.Announcing;
using RustPlusBot.Features.Wipes.Detection;

namespace RustPlusBot.Features.Wipes.Hosting;

/// <summary>Runs the wipe-check loop (on connected transitions) and the wipe-announcement loop.</summary>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="detector">Diffs the live server against the persisted baseline.</param>
/// <param name="announcer">Posts the wipe announcement in #events.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class WipesHostedService(
    IEventBus eventBus,
    IWipeDetector detector,
    IWipeAnnouncer announcer,
    ILogger<WipesHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _statusLoop;
    private Task? _wipedLoop;

    /// <inheritdoc />
    public void Dispose() => _cts.Dispose();

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _statusLoop = Task.Run(() => ConsumeStatusAsync(_cts.Token), CancellationToken.None);
        _wipedLoop = Task.Run(() => ConsumeWipedAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        foreach (var loop in new[]
                 {
                     _statusLoop, _wipedLoop
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

    private async Task ConsumeStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                if (!evt.IsConnected)
                {
                    continue;
                }

                await detector.CheckAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
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
            LogStatusLoopFaulted(logger, ex);
        }
    }

    private async Task ConsumeWipedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ServerWipedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await announcer.HandleServerWipedAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogWipedLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Wipe connection-status loop faulted.")]
    private static partial void LogStatusLoopFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Wipe announcement loop faulted.")]
    private static partial void LogWipedLoopFaulted(ILogger logger, Exception exception);
}
```

In `WipeServiceCollectionExtensions.AddWipes`, add (with `using Microsoft.Extensions.DependencyInjection;` already present and `using RustPlusBot.Features.Wipes.Hosting;`):

```csharp
        services.AddHostedService<WipesHostedService>();
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Wipes.Tests/RustPlusBot.Features.Wipes.Tests.csproj -maxcpucount:1`
Expected: PASS.

- [ ] **Step 5: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Wire wipe detection and announcement through a hosted service

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Purge smart alarms on wipe

Delete alarm rows + their #alarms embeds when `ServerWipedEvent` fires. Rows are removed first (kills any late trigger notification), messages best-effort after.

**Files:**
- Modify: `src/RustPlusBot.Features.Alarms/Posting/IAlarmChannelPoster.cs` (add `DeleteMessageAsync`)
- Modify: `src/RustPlusBot.Features.Alarms/Posting/DiscordAlarmChannelPoster.cs`
- Create: `src/RustPlusBot.Features.Alarms/Relaying/AlarmWipePurger.cs`
- Modify: `src/RustPlusBot.Features.Alarms/Hosting/AlarmsHostedService.cs` (new consumer loop + constructor param)
- Modify: `src/RustPlusBot.Features.Alarms/AlarmServiceCollectionExtensions.cs`
- Modify: `tests/RustPlusBot.Features.Alarms.Tests/Hosting/AlarmsHostedServiceTests.cs` (constructor call)
- Test: `tests/RustPlusBot.Features.Alarms.Tests/AlarmWipePurgerTests.cs` (create)

**Interfaces:**
- Consumes: `ServerWipedEvent`, `IAlarmStore.ListByServerAsync(ulong, Guid, CancellationToken)` / `RemoveAsync(ulong, Guid, ulong, CancellationToken)`, `IAlarmChannelLocator.GetChannelIdAsync`.
- Produces:

```csharp
// on IAlarmChannelPoster:
Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);

// new:
internal sealed partial class AlarmWipePurger
{
    public Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct);
}
```

- [ ] **Step 1: Write the failing purger tests**

Create `tests/RustPlusBot.Features.Alarms.Tests/AlarmWipePurgerTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Alarms.Relaying;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Alarms;

namespace RustPlusBot.Features.Alarms.Tests;

/// <summary>Unit tests for <see cref="AlarmWipePurger"/>.</summary>
public sealed class AlarmWipePurgerTests
{
    private static readonly Guid ServerId = Guid.NewGuid();

    private sealed record Harness(
        AlarmWipePurger Purger,
        IAlarmStore Store,
        IAlarmChannelLocator Locator,
        IAlarmChannelPoster Poster);

    private static Harness Create(IReadOnlyList<SmartAlarm> alarms, ulong? channelId = 777UL)
    {
        var store = Substitute.For<IAlarmStore>();
        store.ListByServerAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(alarms);

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<IAlarmChannelLocator>();
        locator.GetChannelIdAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(channelId);
        var poster = Substitute.For<IAlarmChannelPoster>();

        var purger = new AlarmWipePurger(scopeFactory, locator, poster,
            NullLogger<AlarmWipePurger>.Instance);
        return new Harness(purger, store, locator, poster);
    }

    private static SmartAlarm Alarm(ulong entityId, ulong? messageId) => new()
    {
        GuildId = 10UL, ServerId = ServerId, EntityId = entityId, Name = $"Alarm {entityId}", MessageId = messageId
    };

    private static ServerWipedEvent Event() => new(10UL, ServerId, null, null, 1u, 3500u);

    [Fact]
    public async Task Removes_rows_and_deletes_messages()
    {
        var h = Create(new[]
        {
            Alarm(1UL, 100UL), Alarm(2UL, 200UL)
        });

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 2UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(777UL, 100UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(777UL, 200UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Alarm_without_message_is_removed_without_delete_call()
    {
        var h = Create(new[]
        {
            Alarm(1UL, messageId: null)
        });

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task Missing_channel_still_removes_rows()
    {
        var h = Create(new[]
        {
            Alarm(1UL, 100UL)
        }, channelId: null);

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task No_alarms_is_a_noop()
    {
        var h = Create(Array.Empty<SmartAlarm>());

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().RemoveAsync(default, default, default, default);
        await h.Locator.DidNotReceiveWithAnyArgs().GetChannelIdAsync(default, default, default);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Alarms.Tests/RustPlusBot.Features.Alarms.Tests.csproj -maxcpucount:1 --filter AlarmWipePurgerTests`
Expected: FAIL — `AlarmWipePurger` / `DeleteMessageAsync` do not exist.

- [ ] **Step 3: Implement**

In `src/RustPlusBot.Features.Alarms/Posting/IAlarmChannelPoster.cs`, add after `SendEveryonePingAsync`:

```csharp
    /// <summary>Deletes a message in the given channel (missing message/channel tolerated).</summary>
    /// <param name="channelId">The #alarms channel id.</param>
    /// <param name="messageId">The message id to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been deleted (or the failure swallowed).</returns>
    Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);
```

In `src/RustPlusBot.Features.Alarms/Posting/DiscordAlarmChannelPoster.cs`, add the implementation (the class already injects `DiscordSocketClient client`):

```csharp
    /// <inheritdoc />
    public async Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken)
    {
        try
        {
            var options = new RequestOptions
            {
                CancelToken = cancellationToken
            };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false)
                is not ITextChannel channel)
            {
                return;
            }

            await channel.DeleteMessageAsync(messageId, options).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup (or already-deleted message) must not crash the purge.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeleteFailed(logger, ex, messageId, channelId);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Deleting message {MessageId} in channel {ChannelId} failed (may already be gone).")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, ulong messageId,
        ulong channelId);
```

Create `src/RustPlusBot.Features.Alarms/Relaying/AlarmWipePurger.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Features.Alarms.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Alarms;

namespace RustPlusBot.Features.Alarms.Relaying;

/// <summary>
/// Deletes all of a wiped server's alarms: entity ids are permanently invalid after a wipe, so the rows
/// are removed first (guaranteeing no stale trigger notifications), then the #alarms embeds best-effort.
/// Idempotent: a duplicate wipe event finds no rows and is a no-op.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped alarm store.</param>
/// <param name="locator">Resolves the #alarms channel id.</param>
/// <param name="poster">Deletes the alarm embeds.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class AlarmWipePurger(
    IServiceScopeFactory scopeFactory,
    IAlarmChannelLocator locator,
    IAlarmChannelPoster poster,
    ILogger<AlarmWipePurger> logger)
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/> by purging the server's alarms.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when rows and embeds have been removed.</returns>
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        IReadOnlyList<SmartAlarm> alarms;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
            alarms = await store.ListByServerAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            foreach (var alarm in alarms)
            {
                await store.RemoveAsync(evt.GuildId, evt.ServerId, alarm.EntityId, ct).ConfigureAwait(false);
            }
        }

        if (alarms.Count == 0)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        if (channelId is { } cid)
        {
            foreach (var alarm in alarms)
            {
                if (alarm.MessageId is { } messageId)
                {
                    await poster.DeleteMessageAsync(cid, messageId, ct).ConfigureAwait(false);
                }
            }
        }

        LogPurged(logger, alarms.Count, evt.GuildId, evt.ServerId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} alarms after wipe for guild {GuildId} server {ServerId}.")]
    private static partial void LogPurged(ILogger logger, int count, ulong guildId, Guid serverId);
}
```

In `src/RustPlusBot.Features.Alarms/Hosting/AlarmsHostedService.cs`:
- Add constructor parameter `AlarmWipePurger purger` (after `relay`), and update the XML `<param>` docs.
- Add field `private Task? _wipedLoop;`, start it in `StartAsync` (`_wipedLoop = Task.Run(() => ConsumeWipedAsync(_cts.Token), CancellationToken.None);`), and add `_wipedLoop` to the `StopAsync` array.
- Add the consumer + log method:

```csharp
    private async Task ConsumeWipedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ServerWipedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await purger.HandleServerWipedAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogWipedLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Alarm wipe-purge loop faulted.")]
    private static partial void LogWipedLoopFaulted(ILogger logger, Exception exception);
```

In `src/RustPlusBot.Features.Alarms/AlarmServiceCollectionExtensions.cs`, add:

```csharp
        services.AddSingleton<AlarmWipePurger>();
```

In `tests/RustPlusBot.Features.Alarms.Tests/Hosting/AlarmsHostedServiceTests.cs`, the `new AlarmsHostedService(...)` call needs the new parameter. Inside `Create()`, before the service construction, add:

```csharp
        var purger = new AlarmWipePurger(scopeFactory, relayLocator, relayPoster,
            NullLogger<AlarmWipePurger>.Instance);
```

and pass `purger` as the argument after `relay`.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Alarms.Tests/RustPlusBot.Features.Alarms.Tests.csproj -maxcpucount:1`
Expected: PASS (4 new purger tests + all existing alarm tests green).

While here, confirm the spec's stale-notification guarantee is covered: `AlarmStateRelayTests` already asserts that `HandleTriggeredAsync` for an entity with no stored alarm row is ignored (the `alarm is null` early return) — that existing test is exactly what makes a late trigger for a purged device harmless. If no such test exists, add one there: store returns null → `refresher`/`poster` receive no calls.

- [ ] **Step 5: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Purge smart alarms on server wipe

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Purge smart switches on wipe

Identical shape to Task 7, in the Switches feature. **The switch poster does not yet inject the socket client — add it.**

**Files:**
- Modify: `src/RustPlusBot.Features.Switches/Posting/ISwitchChannelPoster.cs` (add `DeleteMessageAsync`, same signature/docs as Task 7)
- Modify: `src/RustPlusBot.Features.Switches/Posting/DiscordSwitchChannelPoster.cs`
- Create: `src/RustPlusBot.Features.Switches/Relaying/SwitchWipePurger.cs`
- Modify: `src/RustPlusBot.Features.Switches/Hosting/SwitchesHostedService.cs`
- Modify: `src/RustPlusBot.Features.Switches/SwitchServiceCollectionExtensions.cs`
- Modify: `tests/RustPlusBot.Features.Switches.Tests/Hosting/SwitchesHostedServiceTests.cs` (constructor call — if this file doesn't exist, grep `tests/RustPlusBot.Features.Switches.Tests` for `new SwitchesHostedService(` and update every construction site)
- Test: `tests/RustPlusBot.Features.Switches.Tests/SwitchWipePurgerTests.cs` (create)

**Interfaces:**
- Consumes: `ISwitchStore.ListByServerAsync(ulong, Guid, CancellationToken)` / `RemoveAsync(ulong, Guid, ulong, CancellationToken)` (same signatures as the alarm store), `ISwitchChannelLocator.GetChannelIdAsync`, `SmartSwitch.MessageId` (`ulong?`), `SmartSwitch.EntityId` (`ulong`).
- Produces: `internal sealed partial class SwitchWipePurger` with `Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct)`; `ISwitchChannelPoster.DeleteMessageAsync`.

- [ ] **Step 1: Write the failing purger tests**

Create `tests/RustPlusBot.Features.Switches.Tests/SwitchWipePurgerTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Switches.Relaying;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Switches.Tests;

/// <summary>Unit tests for <see cref="SwitchWipePurger"/>.</summary>
public sealed class SwitchWipePurgerTests
{
    private static readonly Guid ServerId = Guid.NewGuid();

    private sealed record Harness(
        SwitchWipePurger Purger,
        ISwitchStore Store,
        ISwitchChannelLocator Locator,
        ISwitchChannelPoster Poster);

    private static Harness Create(IReadOnlyList<SmartSwitch> switches, ulong? channelId = 777UL)
    {
        var store = Substitute.For<ISwitchStore>();
        store.ListByServerAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(switches);

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<ISwitchChannelLocator>();
        locator.GetChannelIdAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(channelId);
        var poster = Substitute.For<ISwitchChannelPoster>();

        var purger = new SwitchWipePurger(scopeFactory, locator, poster,
            NullLogger<SwitchWipePurger>.Instance);
        return new Harness(purger, store, locator, poster);
    }

    private static SmartSwitch Switch(ulong entityId, ulong? messageId) => new()
    {
        GuildId = 10UL, ServerId = ServerId, EntityId = entityId, Name = $"Switch {entityId}", MessageId = messageId
    };

    private static ServerWipedEvent Event() => new(10UL, ServerId, null, null, 1u, 3500u);

    [Fact]
    public async Task Removes_rows_and_deletes_messages()
    {
        var h = Create(new[]
        {
            Switch(1UL, 100UL), Switch(2UL, 200UL)
        });

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 2UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(777UL, 100UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(777UL, 200UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Switch_without_message_is_removed_without_delete_call()
    {
        var h = Create(new[]
        {
            Switch(1UL, messageId: null)
        });

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task Missing_channel_still_removes_rows()
    {
        var h = Create(new[]
        {
            Switch(1UL, 100UL)
        }, channelId: null);

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task No_switches_is_a_noop()
    {
        var h = Create(Array.Empty<SmartSwitch>());

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().RemoveAsync(default, default, default, default);
        await h.Locator.DidNotReceiveWithAnyArgs().GetChannelIdAsync(default, default, default);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Switches.Tests/RustPlusBot.Features.Switches.Tests.csproj -maxcpucount:1 --filter SwitchWipePurgerTests`
Expected: FAIL — types missing.

- [ ] **Step 3: Implement**

In `src/RustPlusBot.Features.Switches/Posting/ISwitchChannelPoster.cs`, add:

```csharp
    /// <summary>Deletes a message in the given channel (missing message/channel tolerated).</summary>
    /// <param name="channelId">The #switches channel id.</param>
    /// <param name="messageId">The message id to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been deleted (or the failure swallowed).</returns>
    Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);
```

In `src/RustPlusBot.Features.Switches/Posting/DiscordSwitchChannelPoster.cs`: add `using Discord.WebSocket;`, change the declaration to

```csharp
internal sealed partial class DiscordSwitchChannelPoster(
    DiscordSocketClient client,
    DiscordChannelMessenger messenger,
    ILogger<DiscordSwitchChannelPoster> logger) : ISwitchChannelPoster
```

(add the `<param name="client">The Discord socket client (used directly for raw message deletes).</param>` doc line; `DiscordSocketClient` is already a DI singleton from the Discord layer, so no registration change), and add:

```csharp
    /// <inheritdoc />
    public async Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken)
    {
        try
        {
            var options = new RequestOptions
            {
                CancelToken = cancellationToken
            };
            if (await client.GetChannelAsync(channelId, options).ConfigureAwait(false)
                is not ITextChannel channel)
            {
                return;
            }

            await channel.DeleteMessageAsync(messageId, options).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown — let the loop unwind.
        }
#pragma warning disable CA1031 // Broad catch: a Discord hiccup (or already-deleted message) must not crash the purge.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeleteFailed(logger, ex, messageId, channelId);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Deleting message {MessageId} in channel {ChannelId} failed (may already be gone).")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, ulong messageId,
        ulong channelId);
```

Create `src/RustPlusBot.Features.Switches/Relaying/SwitchWipePurger.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Switches.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Switches;

namespace RustPlusBot.Features.Switches.Relaying;

/// <summary>
/// Deletes all of a wiped server's switches: entity ids are permanently invalid after a wipe, so the rows
/// are removed first, then the #switches embeds best-effort. Idempotent: a duplicate wipe event finds no
/// rows and is a no-op.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped switch store.</param>
/// <param name="locator">Resolves the #switches channel id.</param>
/// <param name="poster">Deletes the switch embeds.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class SwitchWipePurger(
    IServiceScopeFactory scopeFactory,
    ISwitchChannelLocator locator,
    ISwitchChannelPoster poster,
    ILogger<SwitchWipePurger> logger)
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/> by purging the server's switches.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when rows and embeds have been removed.</returns>
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        IReadOnlyList<SmartSwitch> switches;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
            switches = await store.ListByServerAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            foreach (var device in switches)
            {
                await store.RemoveAsync(evt.GuildId, evt.ServerId, device.EntityId, ct).ConfigureAwait(false);
            }
        }

        if (switches.Count == 0)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        if (channelId is { } cid)
        {
            foreach (var device in switches)
            {
                if (device.MessageId is { } messageId)
                {
                    await poster.DeleteMessageAsync(cid, messageId, ct).ConfigureAwait(false);
                }
            }
        }

        LogPurged(logger, switches.Count, evt.GuildId, evt.ServerId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} switches after wipe for guild {GuildId} server {ServerId}.")]
    private static partial void LogPurged(ILogger logger, int count, ulong guildId, Guid serverId);
}
```

In `src/RustPlusBot.Features.Switches/Hosting/SwitchesHostedService.cs`: add constructor param `SwitchWipePurger purger` (update `<param>` docs), field `private Task? _wipedLoop;`, start `_wipedLoop = Task.Run(() => ConsumeWipedAsync(_cts.Token), CancellationToken.None);` in `StartAsync`, add `_wipedLoop` to the `StopAsync` join array, and add:

```csharp
    private async Task ConsumeWipedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<ServerWipedEvent>(cancellationToken)
                               .ConfigureAwait(false))
            {
                await purger.HandleServerWipedAsync(evt, cancellationToken).ConfigureAwait(false);
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
            LogWipedLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Switch wipe-purge loop faulted.")]
    private static partial void LogWipedLoopFaulted(ILogger logger, Exception exception);
```

In `src/RustPlusBot.Features.Switches/SwitchServiceCollectionExtensions.cs`, add `services.AddSingleton<SwitchWipePurger>();`.

Update every `new SwitchesHostedService(` construction site in `tests/RustPlusBot.Features.Switches.Tests` (grep for it): build a purger from the harness's existing scope factory + locator/poster substitutes —

```csharp
        var purger = new SwitchWipePurger(scopeFactory, locator, poster,
            NullLogger<SwitchWipePurger>.Instance);
```

(adjust the variable names to that file's existing locator/poster substitutes) and pass it in the new constructor position.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Switches.Tests/RustPlusBot.Features.Switches.Tests.csproj -maxcpucount:1`
Expected: PASS.

- [ ] **Step 5: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Purge smart switches on server wipe

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: Purge storage monitors on wipe

Same shape again, in the StorageMonitors feature (its poster also lacks the client — add it).

**Files:**
- Modify: `src/RustPlusBot.Features.StorageMonitors/Posting/IStorageMonitorChannelPoster.cs`
- Modify: `src/RustPlusBot.Features.StorageMonitors/Posting/DiscordStorageMonitorChannelPoster.cs`
- Create: `src/RustPlusBot.Features.StorageMonitors/Relaying/StorageMonitorWipePurger.cs`
- Modify: `src/RustPlusBot.Features.StorageMonitors/Hosting/StorageMonitorsHostedService.cs`
- Modify: `src/RustPlusBot.Features.StorageMonitors/StorageMonitorServiceCollectionExtensions.cs`
- Modify: every `new StorageMonitorsHostedService(` construction site in `tests/RustPlusBot.Features.StorageMonitors.Tests`
- Test: `tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorWipePurgerTests.cs` (create)

**Interfaces:**
- Consumes: `IStorageMonitorStore.ListByServerAsync/RemoveAsync` (same signatures as the alarm store), `IStorageMonitorChannelLocator.GetChannelIdAsync`, `SmartStorageMonitor.MessageId` (`ulong?`).
- Produces: `StorageMonitorWipePurger.HandleServerWipedAsync(ServerWipedEvent, CancellationToken)`; `IStorageMonitorChannelPoster.DeleteMessageAsync`.

- [ ] **Step 1: Write the failing purger tests**

Create `tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorWipePurgerTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.StorageMonitors.Relaying;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.StorageMonitors;

namespace RustPlusBot.Features.StorageMonitors.Tests;

/// <summary>Unit tests for <see cref="StorageMonitorWipePurger"/>.</summary>
public sealed class StorageMonitorWipePurgerTests
{
    private static readonly Guid ServerId = Guid.NewGuid();

    private sealed record Harness(
        StorageMonitorWipePurger Purger,
        IStorageMonitorStore Store,
        IStorageMonitorChannelLocator Locator,
        IStorageMonitorChannelPoster Poster);

    private static Harness Create(IReadOnlyList<SmartStorageMonitor> monitors, ulong? channelId = 777UL)
    {
        var store = Substitute.For<IStorageMonitorStore>();
        store.ListByServerAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(monitors);

        var services = new ServiceCollection();
        services.AddScoped(_ => store);
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        var locator = Substitute.For<IStorageMonitorChannelLocator>();
        locator.GetChannelIdAsync(10UL, ServerId, Arg.Any<CancellationToken>()).Returns(channelId);
        var poster = Substitute.For<IStorageMonitorChannelPoster>();

        var purger = new StorageMonitorWipePurger(scopeFactory, locator, poster,
            NullLogger<StorageMonitorWipePurger>.Instance);
        return new Harness(purger, store, locator, poster);
    }

    private static SmartStorageMonitor Monitor(ulong entityId, ulong? messageId) => new()
    {
        GuildId = 10UL, ServerId = ServerId, EntityId = entityId, Name = $"Monitor {entityId}", MessageId = messageId
    };

    private static ServerWipedEvent Event() => new(10UL, ServerId, null, null, 1u, 3500u);

    [Fact]
    public async Task Removes_rows_and_deletes_messages()
    {
        var h = Create(new[]
        {
            Monitor(1UL, 100UL), Monitor(2UL, 200UL)
        });

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 2UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(777UL, 100UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(777UL, 200UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Monitor_without_message_is_removed_without_delete_call()
    {
        var h = Create(new[]
        {
            Monitor(1UL, messageId: null)
        });

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task Missing_channel_still_removes_rows()
    {
        var h = Create(new[]
        {
            Monitor(1UL, 100UL)
        }, channelId: null);

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.Received(1).RemoveAsync(10UL, ServerId, 1UL, Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task No_monitors_is_a_noop()
    {
        var h = Create(Array.Empty<SmartStorageMonitor>());

        await h.Purger.HandleServerWipedAsync(Event(), CancellationToken.None);

        await h.Store.DidNotReceiveWithAnyArgs().RemoveAsync(default, default, default, default);
        await h.Locator.DidNotReceiveWithAnyArgs().GetChannelIdAsync(default, default, default);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests/RustPlusBot.Features.StorageMonitors.Tests.csproj -maxcpucount:1 --filter StorageMonitorWipePurgerTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

In `src/RustPlusBot.Features.StorageMonitors/Posting/IStorageMonitorChannelPoster.cs`, add:

```csharp
    /// <summary>Deletes a message in the given channel (missing message/channel tolerated).</summary>
    /// <param name="channelId">The #storagemonitors channel id.</param>
    /// <param name="messageId">The message id to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been deleted (or the failure swallowed).</returns>
    Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);
```

In `src/RustPlusBot.Features.StorageMonitors/Posting/DiscordStorageMonitorChannelPoster.cs`: add `using Discord.WebSocket;`, change the declaration to

```csharp
internal sealed partial class DiscordStorageMonitorChannelPoster(
    DiscordSocketClient client,
    DiscordChannelMessenger messenger,
    ILogger<DiscordStorageMonitorChannelPoster> logger) : IStorageMonitorChannelPoster
```

(add the `<param name="client">The Discord socket client (used directly for raw message deletes).</param>` doc line), and add the same `DeleteMessageAsync` implementation + `LogDeleteFailed` `[LoggerMessage]` shown in full in Task 8 Step 3 — identical code, only the containing class differs.

Create `src/RustPlusBot.Features.StorageMonitors/Relaying/StorageMonitorWipePurger.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.StorageMonitors;

namespace RustPlusBot.Features.StorageMonitors.Relaying;

/// <summary>
/// Deletes all of a wiped server's storage monitors: entity ids are permanently invalid after a wipe, so
/// the rows are removed first, then the #storagemonitors embeds best-effort. Idempotent: a duplicate wipe
/// event finds no rows and is a no-op.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped storage-monitor store.</param>
/// <param name="locator">Resolves the #storagemonitors channel id.</param>
/// <param name="poster">Deletes the storage-monitor embeds.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class StorageMonitorWipePurger(
    IServiceScopeFactory scopeFactory,
    IStorageMonitorChannelLocator locator,
    IStorageMonitorChannelPoster poster,
    ILogger<StorageMonitorWipePurger> logger)
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/> by purging the server's storage monitors.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when rows and embeds have been removed.</returns>
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);
        IReadOnlyList<SmartStorageMonitor> monitors;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
            monitors = await store.ListByServerAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
            foreach (var monitor in monitors)
            {
                await store.RemoveAsync(evt.GuildId, evt.ServerId, monitor.EntityId, ct).ConfigureAwait(false);
            }
        }

        if (monitors.Count == 0)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        if (channelId is { } cid)
        {
            foreach (var monitor in monitors)
            {
                if (monitor.MessageId is { } messageId)
                {
                    await poster.DeleteMessageAsync(cid, messageId, ct).ConfigureAwait(false);
                }
            }
        }

        LogPurged(logger, monitors.Count, evt.GuildId, evt.ServerId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} storage monitors after wipe for guild {GuildId} server {ServerId}.")]
    private static partial void LogPurged(ILogger logger, int count, ulong guildId, Guid serverId);
}
```

In `src/RustPlusBot.Features.StorageMonitors/Hosting/StorageMonitorsHostedService.cs`: add constructor param `StorageMonitorWipePurger purger` (update `<param>` docs), field `private Task? _wipedLoop;`, start `_wipedLoop = Task.Run(() => ConsumeWipedAsync(_cts.Token), CancellationToken.None);` in `StartAsync`, add `_wipedLoop` to the `StopAsync` join array, and add the same `ConsumeWipedAsync` consumer shown in full in Task 8 Step 3, with the log message:

```csharp
    [LoggerMessage(Level = LogLevel.Error, Message = "Storage-monitor wipe-purge loop faulted.")]
    private static partial void LogWipedLoopFaulted(ILogger logger, Exception exception);
```

In `src/RustPlusBot.Features.StorageMonitors/StorageMonitorServiceCollectionExtensions.cs`, add `services.AddSingleton<StorageMonitorWipePurger>();`.

Update every `new StorageMonitorsHostedService(` construction site in `tests/RustPlusBot.Features.StorageMonitors.Tests` (grep for it): build a purger from the harness's existing scope factory + locator/poster substitutes —

```csharp
        var purger = new StorageMonitorWipePurger(scopeFactory, locator, poster,
            NullLogger<StorageMonitorWipePurger>.Instance);
```

(adjust the variable names to that file's existing substitutes) and pass it in the new constructor position.

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests/RustPlusBot.Features.StorageMonitors.Tests.csproj -maxcpucount:1`
Expected: PASS.

- [ ] **Step 5: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Purge storage monitors on server wipe

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 10: @everyone-on-wipe toggle in #settings

A toggle button under the language selector; state persisted via the Task 2 accessors; workspace re-render on toggle.

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/Messages/SettingsMessageRenderer.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Modules/SettingsComponentModule.cs`
- Modify: `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs` (update `SettingsMessageRenderer` construction sites)
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Messages/SettingsMessageRendererWipePingTests.cs` (create)

**Interfaces:**
- Consumes: `IWorkspaceStore.GetPingEveryoneOnWipeAsync/SetPingEveryoneOnWipeAsync` (Task 2), `settings.wipeping.on/off` resx keys (Task 4).
- Produces: `SettingsMessageRenderer.WipePingButtonId` (`"workspace:settings:wipeping"`) — nothing else consumes it beyond the module.

- [ ] **Step 1: Write the failing renderer tests**

Create `tests/RustPlusBot.Features.Workspace.Tests/Messages/SettingsMessageRendererWipePingTests.cs`:

```csharp
using Discord;
using NSubstitute;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

/// <summary>Wipe-ping toggle tests for <see cref="SettingsMessageRenderer"/>.</summary>
public sealed class SettingsMessageRendererWipePingTests
{
    private static SettingsMessageRenderer Create(bool ping)
    {
        var store = Substitute.For<IWorkspaceStore>();
        store.GetPingEveryoneOnWipeAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(ping);
        return new SettingsMessageRenderer(store, new ResxLocalizer());
    }

    private static ButtonComponent SingleButton(MessagePayload payload) =>
        payload.Components!.Components
            .SelectMany(row => row.Components)
            .OfType<ButtonComponent>()
            .Single();

    [Fact]
    public async Task Renders_off_button_by_default()
    {
        var renderer = Create(ping: false);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1UL, null, "en"), CancellationToken.None);

        var button = SingleButton(payload);
        Assert.Equal(SettingsMessageRenderer.WipePingButtonId, button.CustomId);
        Assert.Equal(ButtonStyle.Secondary, button.Style);
        Assert.Equal("🔕 Ping @everyone on wipe: Off", button.Label);
    }

    [Fact]
    public async Task Renders_on_button_when_enabled()
    {
        var renderer = Create(ping: true);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1UL, null, "en"), CancellationToken.None);

        var button = SingleButton(payload);
        Assert.Equal(ButtonStyle.Success, button.Style);
        Assert.Equal("🔔 Ping @everyone on wipe: On", button.Label);
    }

    [Fact]
    public async Task Language_select_menu_is_still_present()
    {
        var renderer = Create(ping: false);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1UL, null, "en"), CancellationToken.None);

        var menu = payload.Components!.Components
            .SelectMany(row => row.Components)
            .OfType<SelectMenuComponent>()
            .Single();
        Assert.Equal(SettingsMessageRenderer.LanguageSelectId, menu.CustomId);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj -maxcpucount:1 --filter SettingsMessageRendererWipePingTests`
Expected: FAIL — constructor mismatch / `WipePingButtonId` missing.

- [ ] **Step 3: Implement**

Rewrite `src/RustPlusBot.Features.Workspace/Messages/SettingsMessageRenderer.cs` as:

```csharp
using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the global #settings message with the language selector and the wipe-ping toggle.</summary>
/// <param name="store">Reads the guild's wipe-ping flag.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class SettingsMessageRenderer(IWorkspaceStore store, ILocalizer localizer) : IMessageRenderer
{
    /// <summary>Custom id of the language select menu, handled by the settings component module.</summary>
    public const string LanguageSelectId = "workspace:settings:culture";

    /// <summary>Custom id of the @everyone-on-wipe toggle button, handled by the settings component module.</summary>
    public const string WipePingButtonId = "workspace:settings:wipeping";

    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.SettingsMain;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var ping = await store.GetPingEveryoneOnWipeAsync(context.GuildId, cancellationToken).ConfigureAwait(false);

        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("settings.title", context.Culture))
            .WithDescription(localizer.Get("settings.body", context.Culture))
            .WithColor(Color.DarkGrey)
            .Build();

        var menu = new SelectMenuBuilder()
            .WithCustomId(LanguageSelectId)
            .WithPlaceholder(localizer.Get("settings.language.label", context.Culture))
            .AddOption("English", "en")
            .AddOption("Français", "fr");
        var wipePing = new ButtonBuilder()
            .WithCustomId(WipePingButtonId)
            .WithLabel(localizer.Get(ping ? "settings.wipeping.on" : "settings.wipeping.off", context.Culture))
            .WithStyle(ping ? ButtonStyle.Success : ButtonStyle.Secondary);
        var components = new ComponentBuilder()
            .WithSelectMenu(menu)
            .WithButton(wipePing, row: 1)
            .Build();

        return new MessagePayload(null, embed, components);
    }
}
```

In `src/RustPlusBot.Features.Workspace/Modules/SettingsComponentModule.cs`, add after `SetCultureAsync`:

```csharp
    /// <summary>Toggles the @everyone-on-wipe ping and re-renders the workspace.</summary>
    [ComponentInteraction(SettingsMessageRenderer.WipePingButtonId)]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task ToggleWipePingAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var enabled = !await store.GetPingEveryoneOnWipeAsync(Context.Guild.Id).ConfigureAwait(false);
            await store.SetPingEveryoneOnWipeAsync(Context.Guild.Id, enabled).ConfigureAwait(false);

            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            await reconciler.ReconcileGlobalAsync(Context.Guild.Id).ConfigureAwait(false);

            await FollowupAsync($"@everyone ping on wipe {(enabled ? "enabled" : "disabled")}.", ephemeral: true)
                .ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
```

Update the existing construction sites: grep for `new SettingsMessageRenderer(` under `tests/RustPlusBot.Features.Workspace.Tests` (expect hits in `Messages/RendererTests.cs` and possibly the reconciler tests) and prepend a store argument:

```csharp
        var settingsStore = Substitute.For<IWorkspaceStore>();
        settingsStore.GetPingEveryoneOnWipeAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(false);
        // then: new SettingsMessageRenderer(settingsStore, <existing localizer arg>)
```

(If a test file constructs it inside a DI container instead, register the substitute as `services.AddScoped(_ => settingsStore)` — match whatever that file already does for other renderers' stores.)

- [ ] **Step 4: Run the tests**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj -maxcpucount:1`
Expected: PASS (3 new + all existing workspace tests).

- [ ] **Step 5: Cleanup + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add -A
git commit -m "Add @everyone-on-wipe toggle to the settings channel

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 11: Full-solution verification

**Files:** none (verification only; commit only if cleanup produces diffs).

- [ ] **Step 1: Full build + full test run**

```bash
dotnet build RustPlusBot.slnx -maxcpucount:1
dotnet test RustPlusBot.slnx -maxcpucount:1
```

Expected: build clean; ALL test assemblies report non-zero counts (read per-assembly totals — a dropped assembly reports 0 and masquerades as green). `RustPlusBot.Features.Wipes.Tests` must appear with ~22 tests.

- [ ] **Step 2: Migration drift check**

Run: `dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host`
Expected: "No changes have been made to the model since the last migration."

- [ ] **Step 3: Format gate**

Run: `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx` then `git status --short`
Expected: no modified files. If there are diffs, commit them as `Apply code cleanup` (with the co-author trailer).

- [ ] **Step 4: Verify the feature end-to-end (manual smoke, optional but recommended)**

If a dev bot token + test guild are configured (see `docs/development/running-locally.md`): run the Host, pair a test server, confirm `#settings` shows the new toggle button and clicking it flips the label; flip `LastMapSeed` in the DB (`sqlite3 <db> "UPDATE RustServers SET LastMapSeed = 1"`) and restart the connection to watch the wipe announcement land in #events and the device embeds disappear. Otherwise rely on the unit suites.

- [ ] **Step 5: Hand off**

Implementation complete on `feat/wipe-detection`. Use superpowers:finishing-a-development-branch to merge or open a PR to `develop`.
