# Subsystem 4b — Smart Alarms — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Smart Alarms end-to-end on the FCM-push model — pair → validate → per-alarm embed in a per-server `#alarms` channel; on an in-game alarm fire (FCM `OnAlarmTriggered`), update the embed and, per toggles, ping `@everyone` and/or relay the message to in-game team chat.

**Architecture:** A new `RustPlusBot.Features.Alarms` project mirrors `Features.Switches`. The existing Pairing entity path is extended (Approach A) to thread `PairedEntityKind` so switch vs. alarm pairings route to different events, and a new `PairingKind.AlarmTriggered` carries fired alarms through the existing per-account listener closure (which supplies `guildId`) to `PairingHandler`, which publishes a new `AlarmTriggeredEvent`. Alarms add no socket read/control and no supervisor priming; the only cross-feature dependency beyond the switch template is the existing `ITeamChatSender` seam (in `Features.Connections`).

**Tech Stack:** .NET 10, C#, EF Core + SQLite, Discord.Net 3.20, RustPlusApi/RustPlusApi.Fcm 2.0.0-beta.2, NSubstitute + xUnit, Roslynator + ReSharper (`dotnet jb`) analyzers.

## Global Constraints

- **Branch:** `feat/smart-alarms` off `develop` (cut a plain branch in the main checkout; no worktrees).
- **Package versions:** stay on `RustPlusApi` / `RustPlusApi.Fcm` **2.0.0-beta.2** — no bump.
- **Solution file is `RustPlusBot.slnx`** (not `.sln`) — `dotnet sln RustPlusBot.slnx add ...`.
- **Build gate:** `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` must be **0 warnings / 0 errors** (strict Roslynator analyzers).
- **Format gate:** run `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` before every push (the repo's real format gate; the pre-push hook enforces it). It reorders members Roslynator never flags.
- **Test gate:** `dotnet test RustPlusBot.slnx -maxcpucount:1`; **read per-assembly counts**, not just the total — a fake that doesn't implement a new interface member compiles-but-drops a whole assembly's tests silently.
- **EF drift:** exactly one new migration (`SmartAlarms`); no other model changes.
- **Entities live in `RustPlusBot.Domain`**, stores are `public sealed` in `RustPlusBot.Persistence`, events live in `RustPlusBot.Abstractions` (no project refs, no Discord, no FCM).
- **NSubstitute on `internal` interfaces** needs `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in that project's csproj.
- **Discord namespace shadow:** `RustPlusBot.Discord` shadows Discord.Net's `Discord`; in Alarms files that touch Discord.Net types use `global::Discord.Embed` etc. (as `DiscordSwitchChannelPoster` / `SwitchEmbedRenderer` do — note the renderer has `using Discord;` and works because it's not under a `RustPlusBot.Discord`-shadowing using; follow the switch file's exact using set).
- **Localizer copy:** each feature owns its catalog/localizer (copy the switch shape). Leave the standing `// consolidate localizers someday` remark as XML `<remarks>` (a literal `// TODO` is a Roslynator S1135 **error**).
- **No secret in any exception message** (no token/credential ever thrown).
- `docs/superpowers/` is **gitignored / local-only** — never `git add` the spec or this plan.

---

## File Structure

**New project `src/RustPlusBot.Features.Alarms/`** (mirrors `Features.Switches`):

- `RustPlusBot.Features.Alarms.csproj` — refs Abstractions, Persistence, Domain, Discord, Workspace, **Connections** (for `ITeamChatSender` only), + `InternalsVisibleTo` for tests + `DynamicProxyGenAssembly2`.
- `AlarmServiceCollectionExtensions.cs` — `AddAlarms()`.
- `Rendering/AlarmComponentIds.cs`, `AlarmLocalizationCatalog.cs` (catalog dict only), `AlarmEmbedRenderer.cs`.
- (No per-slice localizer or poster — consumes the shared `ILocalizer`/`IChannelEmbedPoster` in `RustPlusBot.Discord`, added in Task 7a.)
- `Pairing/AlarmPairingCoordinator.cs`.
- `Relaying/AlarmFireRelay.cs`.
- `Modules/AlarmComponentModule.cs`, `Modules/AlarmRenameModal.cs`.
- `Hosting/AlarmsHostedService.cs`.

**Domain / Persistence:**

- `src/RustPlusBot.Domain/Alarms/SmartAlarm.cs`.
- `src/RustPlusBot.Persistence/Alarms/IAlarmStore.cs`, `AlarmStore.cs`.
- `src/RustPlusBot.Persistence/Configurations/SmartAlarmConfiguration.cs`.
- `src/RustPlusBot.Persistence/BotDbContext.cs` — add `DbSet<SmartAlarm> SmartAlarms`.
- `src/RustPlusBot.Persistence/Migrations/<stamp>_SmartAlarms.cs` (+ Designer + snapshot delta) via `dotnet ef`.

**Abstractions:**

- `src/RustPlusBot.Abstractions/Events/AlarmPairedEvent.cs`, `AlarmTriggeredEvent.cs`.

**Shared (RustPlusBot.Discord — new, Task 7a):**

- `src/RustPlusBot.Discord/Localization/ILocalizer.cs`, `Localizer.cs` — shared dictionary-catalog localizer (EN-fallback + region-normalize), catalog passed in by each feature.
- `src/RustPlusBot.Discord/Posting/IChannelEmbedPoster.cs`, `DiscordChannelEmbedPoster.cs` — shared ensure/edit/self-heal embed poster + `SendEveryonePingAsync`.

**Pairing (surgical edits):**

- `src/RustPlusBot.Features.Pairing/Listening/PairingNotification.cs` — `PairingKind.AlarmTriggered`; `PairedEntityKind EntityKind`; `Title`/`Message` fields.
- `src/RustPlusBot.Features.Pairing/Listening/RustPlusFcmPairingSource.cs` — subscribe `OnSmartAlarmPairing` + `OnAlarmTriggered`.
- `src/RustPlusBot.Features.Pairing/Pairing/PairingHandler.cs` — kind-switch entity routing + alarm-trigger handling.

**Workspace:**

- `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs` — `ServerAlarms = "alarms"`.
- `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs` — `#alarms` ChannelSpec.
- `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs` — `channel.alarms.name` EN/FR.
- `src/RustPlusBot.Features.Workspace/Locating/IAlarmChannelLocator.cs`, `AlarmChannelLocator.cs`.
- `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs` (or wherever locators register) — register `IAlarmChannelLocator`.

**Host:**

- `src/RustPlusBot.Host/Program.cs` — `builder.Services.AddAlarms();`.

**Tests** (new):

- `tests/RustPlusBot.Abstractions.Tests/AlarmEventsTests.cs`.
- `tests/RustPlusBot.Persistence.Tests/Alarms/SmartAlarmSchemaTests.cs`, `Alarms/AlarmStoreTests.cs`.
- `tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs` (extend).
- `tests/RustPlusBot.Features.Workspace.Tests/Locating/AlarmChannelLocatorTests.cs`.
- `tests/RustPlusBot.Features.Alarms.Tests/` — `AlarmLocalizationCatalogTests.cs`, `AlarmEmbedRendererTests.cs`, `AlarmPairingCoordinatorTests.cs`, `AlarmFireRelayTests.cs`, `AlarmRegistrationTests.cs`.

---

## Task 1: `SmartAlarm` entity + EF config + migration

**Files:**

- Create: `src/RustPlusBot.Domain/Alarms/SmartAlarm.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/SmartAlarmConfiguration.cs`
- Modify: `src/RustPlusBot.Persistence/BotDbContext.cs` (add `DbSet`)
- Create (via `dotnet ef`): `src/RustPlusBot.Persistence/Migrations/<stamp>_SmartAlarms.*`
- Test: `tests/RustPlusBot.Persistence.Tests/Alarms/SmartAlarmSchemaTests.cs`

**Interfaces:**

- Produces: `RustPlusBot.Domain.Alarms.SmartAlarm` with props `Guid Id`, `ulong GuildId`, `Guid ServerId`, `ulong EntityId`, `string Name`, `ulong? MessageId`, `ulong PairedByUserId`, `DateTimeOffset CreatedUtc`, `bool PingEveryone`, `bool RelayToTeamChat`, `string? LastTitle`, `string? LastMessage`, `DateTimeOffset? LastFiredUtc`. `BotDbContext.SmartAlarms`.

- [ ] **Step 1: Write the failing schema test**

Mirror `tests/RustPlusBot.Persistence.Tests/Switches/SmartSwitchSchemaTests.cs`. Open it first to copy the exact `TestDb`/context helper and the cascade assertion shape, then write:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Tests.Alarms;

public sealed class SmartAlarmSchemaTests
{
    [Fact]
    public async Task RemovingServer_CascadeDeletesAlarms()
    {
        var (context, connection) = TestDb.Create(); // copy the harness the switch schema test uses
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.2.3.4", Port = 28015 };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        context.SmartAlarms.Add(new SmartAlarm
        {
            GuildId = 10UL, ServerId = server.Id, EntityId = 42UL, Name = "Alarm 42", CreatedUtc = DateTimeOffset.UtcNow,
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.SmartAlarms.ToListAsync());
    }

    [Fact]
    public async Task DuplicateEntityForSameServer_IsRejected()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer { GuildId = 10UL, Name = "S", Ip = "1.2.3.4", Port = 28015 };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();

        context.SmartAlarms.Add(new SmartAlarm { GuildId = 10UL, ServerId = server.Id, EntityId = 7UL, Name = "a", CreatedUtc = DateTimeOffset.UtcNow });
        await context.SaveChangesAsync();
        context.SmartAlarms.Add(new SmartAlarm { GuildId = 10UL, ServerId = server.Id, EntityId = 7UL, Name = "b", CreatedUtc = DateTimeOffset.UtcNow });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }
}
```

> Check the real `RustServer` property names against `src/RustPlusBot.Domain/Servers/RustServer.cs` before finalizing the seed (use whatever the switch schema test uses verbatim).

- [ ] **Step 2: Run the test to verify it fails (won't compile — `SmartAlarm`/`SmartAlarms` don't exist)**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1`
Expected: FAIL — compile error, `SmartAlarm` / `SmartAlarms` not found.

- [ ] **Step 3: Create the entity**

```csharp
namespace RustPlusBot.Domain.Alarms;

/// <summary>A paired Smart Alarm the bot manages, surviving restarts. Guild- and server-scoped. Notify-only (FCM push).</summary>
public sealed class SmartAlarm
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this alarm belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The in-game smart-alarm entity id.</summary>
    public ulong EntityId { get; set; }

    /// <summary>User-facing label; defaults to a generated "Alarm &lt;EntityId&gt;" (the FCM event carries no name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Discord message id of this alarm's embed, or null until first posted.</summary>
    public ulong? MessageId { get; set; }

    /// <summary>The Discord user who accepted (validated) the pairing.</summary>
    public ulong PairedByUserId { get; set; }

    /// <summary>When the alarm was accepted (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>When true, a fire pings @everyone in #alarms.</summary>
    public bool PingEveryone { get; set; }

    /// <summary>When true, a fire relays the message into in-game team chat.</summary>
    public bool RelayToTeamChat { get; set; }

    /// <summary>The title from the most recent fire, or null if never fired.</summary>
    public string? LastTitle { get; set; }

    /// <summary>The message from the most recent fire, or null if never fired.</summary>
    public string? LastMessage { get; set; }

    /// <summary>When the alarm most recently fired (UTC), or null if never.</summary>
    public DateTimeOffset? LastFiredUtc { get; set; }
}
```

- [ ] **Step 4: Create the EF configuration** (copy `SmartSwitchConfiguration` exactly, retyped)

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Alarms;
using RustPlusBot.Domain.Servers;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class SmartAlarmConfiguration : IEntityTypeConfiguration<SmartAlarm>
{
    public void Configure(EntityTypeBuilder<SmartAlarm> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).IsRequired().HasMaxLength(128);
        builder.HasIndex(a => new
        {
            a.GuildId, a.ServerId, a.EntityId
        }).IsUnique();

        // Removing a RustServer cascades to its alarms so no orphaned rows linger.
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(a => a.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 5: Add the `DbSet` to `BotDbContext`**

Open `src/RustPlusBot.Persistence/BotDbContext.cs`, find the `SmartSwitches` `DbSet` and its `ApplyConfiguration`, and add the parallel alarm lines:

```csharp
public DbSet<RustPlusBot.Domain.Alarms.SmartAlarm> SmartAlarms => Set<RustPlusBot.Domain.Alarms.SmartAlarm>();
```

In `OnModelCreating` (or wherever `SmartSwitchConfiguration` is applied — match the existing pattern; many contexts use `ApplyConfigurationsFromAssembly`, in which case no manual line is needed — check first):

```csharp
modelBuilder.ApplyConfiguration(new Configurations.SmartAlarmConfiguration());
```

> Inspect how `SmartSwitchConfiguration` is wired and replicate exactly — do not add a manual `ApplyConfiguration` if the context already scans the assembly.

- [ ] **Step 6: Create the migration**

Run (from repo root; copy the exact tool invocation the repo uses — check `README`/`running-locally.md` for the startup project, typically the Host):

```bash
dotnet ef migrations add SmartAlarms \
  --project src/RustPlusBot.Persistence/RustPlusBot.Persistence.csproj \
  --startup-project src/RustPlusBot.Host/RustPlusBot.Host.csproj
```

Expected: creates `Migrations/<stamp>_SmartAlarms.cs` (+ `.Designer.cs`) and updates the model snapshot. Inspect the generated `Up()` — it must `CreateTable` only `SmartAlarms` with the unique index + cascade FK, and touch nothing else.

> If `dotnet ef` fails with "Unable to retrieve project metadata" (a known tooling quirk in this repo), build the Persistence + Host projects first, then retry; if it still fails, hand-author the migration to match a prior one (e.g. `SmartSwitches`) and regenerate the snapshot delta by diffing.

- [ ] **Step 7: Run the schema test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1`
Expected: PASS (both new tests).

- [ ] **Step 8: Verify no unintended EF drift**

Run: `git status --porcelain src/RustPlusBot.Persistence/Migrations` — only the new `SmartAlarms` files + the snapshot should appear. No other entity's mapping changed.

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Domain/Alarms/SmartAlarm.cs \
        src/RustPlusBot.Persistence/Configurations/SmartAlarmConfiguration.cs \
        src/RustPlusBot.Persistence/BotDbContext.cs \
        src/RustPlusBot.Persistence/Migrations \
        tests/RustPlusBot.Persistence.Tests/Alarms/SmartAlarmSchemaTests.cs
git commit -m "feat(alarms): add SmartAlarm entity, EF config, and SmartAlarms migration"
```

---

## Task 2: `IAlarmStore` + `AlarmStore`

**Files:**

- Create: `src/RustPlusBot.Persistence/Alarms/IAlarmStore.cs`
- Create: `src/RustPlusBot.Persistence/Alarms/AlarmStore.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Alarms/AlarmStoreTests.cs`

**Interfaces:**

- Consumes: `SmartAlarm`, `BotDbContext.SmartAlarms`, `RustPlusBot.Abstractions.Time.IClock`.
- Produces: `IAlarmStore` with:
  - `Task<SmartAlarm> AddAsync(ulong guildId, Guid serverId, ulong entityId, string name, ulong pairedByUserId, CancellationToken ct = default)`
  - `Task<SmartAlarm?> GetAsync(ulong guildId, Guid serverId, ulong entityId, CancellationToken ct = default)`
  - `Task<IReadOnlyList<SmartAlarm>> ListByServerAsync(ulong guildId, Guid serverId, CancellationToken ct = default)`
  - `Task<bool> ExistsAsync(ulong guildId, Guid serverId, ulong entityId, CancellationToken ct = default)`
  - `Task RenameAsync(ulong guildId, Guid serverId, ulong entityId, string name, CancellationToken ct = default)`
  - `Task SetMessageIdAsync(ulong guildId, Guid serverId, ulong entityId, ulong messageId, CancellationToken ct = default)`
  - `Task SetPingEveryoneAsync(ulong guildId, Guid serverId, ulong entityId, bool value, CancellationToken ct = default)`
  - `Task SetRelayToTeamChatAsync(ulong guildId, Guid serverId, ulong entityId, bool value, CancellationToken ct = default)`
  - `Task RecordFiredAsync(ulong guildId, Guid serverId, ulong entityId, string? title, string? message, DateTimeOffset firedUtc, CancellationToken ct = default)`
  - `Task RemoveAsync(ulong guildId, Guid serverId, ulong entityId, CancellationToken ct = default)`

- [ ] **Step 1: Write the failing store tests**

Copy `tests/RustPlusBot.Persistence.Tests/Switches/SwitchStoreTests.cs` for the harness shape (`TestDb.Create()`, a seeded `RustServer`, a fixed `IClock` substitute), then write tests covering: `AddAsync` persists + returns the row with `Name`/`PairedByUserId`/`CreatedUtc` set; `AddAsync` twice for the same `(guild,server,entity)` is the idempotent race no-op (returns the existing row, no throw); `GetAsync`/`ExistsAsync`; `ListByServerAsync` returns oldest-first; `RenameAsync`; `SetMessageIdAsync`; `SetPingEveryoneAsync` toggles; `SetRelayToTeamChatAsync` toggles; `RecordFiredAsync` sets `LastTitle`/`LastMessage`/`LastFiredUtc`; `RemoveAsync`. Example for the firing path:

```csharp
[Fact]
public async Task RecordFiredAsync_StoresTitleMessageAndTimestamp()
{
    var (context, connection) = TestDb.Create();
    await using var _ = context;
    await using var __ = connection;
    var server = SeedServer(context);            // copy the switch test's helper
    var clock = FixedClock(DateTimeOffset.UnixEpoch);
    var store = new AlarmStore(context, clock);
    await store.AddAsync(10UL, server.Id, 42UL, "Alarm 42", 1UL);

    var fired = DateTimeOffset.Parse("2026-06-22T12:00:00Z");
    await store.RecordFiredAsync(10UL, server.Id, 42UL, "Raid!", "Base under attack", fired);

    var a = await store.GetAsync(10UL, server.Id, 42UL);
    Assert.Equal("Raid!", a!.LastTitle);
    Assert.Equal("Base under attack", a.LastMessage);
    Assert.Equal(fired, a.LastFiredUtc);
}
```

- [ ] **Step 2: Run the tests to verify they fail (compile error — `AlarmStore`/`IAlarmStore` missing)**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1`
Expected: FAIL — `IAlarmStore` / `AlarmStore` not found.

- [ ] **Step 3: Write `IAlarmStore`** with full XML doc on every member (copy `ISwitchStore`'s doc style; Roslynator requires docs on public members). Include the two toggle setters and `RecordFiredAsync` from the Interfaces block above.

- [ ] **Step 4: Write `AlarmStore`** (`public sealed class AlarmStore(BotDbContext context, IClock clock) : IAlarmStore`). Copy `SwitchStore` verbatim, adapting:
  - `AddAsync`: same `DbUpdateException`-catch idempotent-race recovery, but no `LastIsActive` (alarms have no live state); set `Name`/`PairedByUserId`/`CreatedUtc = clock.UtcNow`.
  - `ListByServerAsync`: same client-side `OrderBy(a => a.CreatedUtc)` (SQLite can't ORDER BY `DateTimeOffset`).
  - Add `SetPingEveryoneAsync`/`SetRelayToTeamChatAsync` via the private `MutateAsync` helper (`a => a.PingEveryone = value` etc.).
  - Add `RecordFiredAsync` via `MutateAsync` setting `LastTitle`/`LastMessage`/`LastFiredUtc` in one mutate.
  - Drop `UpdateStateAsync` (not applicable).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1`
Expected: PASS.

- [ ] **Step 6: Build strict + commit**

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
git add src/RustPlusBot.Persistence/Alarms tests/RustPlusBot.Persistence.Tests/Alarms/AlarmStoreTests.cs
git commit -m "feat(alarms): add IAlarmStore + AlarmStore with toggle + record-fired"
```

---

## Task 3: Abstractions events — `AlarmPairedEvent`, `AlarmTriggeredEvent`

**Files:**

- Create: `src/RustPlusBot.Abstractions/Events/AlarmPairedEvent.cs`
- Create: `src/RustPlusBot.Abstractions/Events/AlarmTriggeredEvent.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/AlarmEventsTests.cs`

**Interfaces:**

- Produces:
  - `public sealed record AlarmPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId)`
  - `public sealed record AlarmTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId, string? Title, string? Message)`

- [ ] **Step 1: Write the failing test** (copy `tests/RustPlusBot.Abstractions.Tests/SwitchEventsTests.cs`)

```csharp
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests;

public sealed class AlarmEventsTests
{
    [Fact]
    public void AlarmPairedEvent_CarriesIdentity()
    {
        var e = new AlarmPairedEvent(10UL, Guid.Empty, 42UL);
        Assert.Equal(10UL, e.GuildId);
        Assert.Equal(42UL, e.EntityId);
    }

    [Fact]
    public void AlarmTriggeredEvent_CarriesTitleAndMessage()
    {
        var e = new AlarmTriggeredEvent(10UL, Guid.Empty, 42UL, "Raid!", "Base under attack");
        Assert.Equal("Raid!", e.Title);
        Assert.Equal("Base under attack", e.Message);
    }
}
```

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/RustPlusBot.Abstractions.Tests/RustPlusBot.Abstractions.Tests.csproj -maxcpucount:1` → FAIL (types missing).

- [ ] **Step 3: Create the two records** (copy `SwitchPairedEvent` doc style; XML doc the record + each parameter, matching the switch events file).

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>A Smart Alarm was paired in-game and needs validation.</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game smart-alarm entity id.</param>
public sealed record AlarmPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId);
```

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>A managed Smart Alarm fired in-game (delivered via FCM push).</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game smart-alarm entity id.</param>
/// <param name="Title">The alarm title configured in the Rust+ app, or null.</param>
/// <param name="Message">The alarm message configured in the Rust+ app, or null.</param>
public sealed record AlarmTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId, string? Title, string? Message);
```

- [ ] **Step 4: Run to verify it passes** — same command → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Abstractions/Events/AlarmPairedEvent.cs \
        src/RustPlusBot.Abstractions/Events/AlarmTriggeredEvent.cs \
        tests/RustPlusBot.Abstractions.Tests/AlarmEventsTests.cs
git commit -m "feat(alarms): add AlarmPairedEvent and AlarmTriggeredEvent"
```

---

## Task 4: Pairing — thread `PairedEntityKind` + alarm-trigger through the entity path

**Files:**

- Modify: `src/RustPlusBot.Features.Pairing/Listening/PairingNotification.cs`
- Modify: `src/RustPlusBot.Features.Pairing/Pairing/PairingHandler.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs` (extend)

**Interfaces:**

- Consumes: `AlarmPairedEvent`, `AlarmTriggeredEvent` (Task 3); `RustPlusBot.Domain.Entities.PairedEntityKind` (existing: `SmartSwitch=0`, `SmartAlarm=1`, `StorageMonitor=2`); `IServerService.GetByFacepunchServerIdAsync`.
- Produces: `PairingNotification` gains `PairingKind.AlarmTriggered` (new enum member), `PairedEntityKind EntityKind = PairedEntityKind.SmartSwitch`, `string? Title = null`, `string? Message = null`. `PairingHandler` routes `Entity` by `EntityKind` and handles `AlarmTriggered`.

> **Design note (refinement over the spec):** the alarm *trigger* rides the existing `PairingNotification` → `PairingSupervisor.HandleNotificationAsync(key, ...)` → `PairingHandler.HandleAsync(guildId, ownerUserId, ...)` path, because that closure is the only place the listener's `guildId` is available — the FCM source has no guild. So the source maps `OnAlarmTriggered` into a `PairingNotification(Kind=AlarmTriggered, ...)`; `PairingHandler` resolves the server by `FacepunchServerId` and publishes `AlarmTriggeredEvent`. (The spec's "publish straight from the source" was noted as a plan-level detail; this is the resolved form.)

- [ ] **Step 1: Write the failing PairingHandler tests** (extend the existing file). Add an `AlarmPairing` and an `AlarmTrigger` factory and four tests:

```csharp
private static PairingNotification AlarmPairing(Guid fpServer, ulong entityId = 55UL) =>
    new(PairingKind.Entity, string.Empty, string.Empty, 0, 1UL, "t",
        FacepunchServerId: fpServer, EntityId: entityId, EntityKind: PairedEntityKind.SmartAlarm);

private static PairingNotification AlarmTrigger(Guid fpServer, ulong entityId = 55UL, string title = "Raid!", string msg = "Hit") =>
    new(PairingKind.AlarmTriggered, string.Empty, string.Empty, 0, 1UL, "t",
        FacepunchServerId: fpServer, EntityId: entityId, Title: title, Message: msg);

[Fact]
public async Task EntityPairing_Switch_PublishesSwitchPairedEvent_NotAlarm()
{
    var (context, connection) = TestDb.Create();
    await using var _ = context; await using var __ = connection;
    var bus = Substitute.For<IEventBus>();
    var handler = CreateHandler(context, bus);
    await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);
    var server = await context.RustServers.SingleAsync();
    bus.ClearReceivedCalls();

    // Existing EntityPairing() factory defaults EntityKind to SmartSwitch.
    await handler.HandleAsync(10UL, 1UL, EntityPairing(FpServer, 42UL), CancellationToken.None);

    await bus.Received(1).PublishAsync(Arg.Is<SwitchPairedEvent>(e => e.EntityId == 42UL), Arg.Any<CancellationToken>());
    await bus.DidNotReceive().PublishAsync(Arg.Any<AlarmPairedEvent>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task EntityPairing_Alarm_PublishesAlarmPairedEvent()
{
    var (context, connection) = TestDb.Create();
    await using var _ = context; await using var __ = connection;
    var bus = Substitute.For<IEventBus>();
    var handler = CreateHandler(context, bus);
    await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);
    var server = await context.RustServers.SingleAsync();
    bus.ClearReceivedCalls();

    await handler.HandleAsync(10UL, 1UL, AlarmPairing(FpServer, 55UL), CancellationToken.None);

    await bus.Received(1).PublishAsync(
        Arg.Is<AlarmPairedEvent>(e => e.GuildId == 10UL && e.ServerId == server.Id && e.EntityId == 55UL),
        Arg.Any<CancellationToken>());
    await bus.DidNotReceive().PublishAsync(Arg.Any<SwitchPairedEvent>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task AlarmTrigger_KnownServer_PublishesAlarmTriggeredEvent()
{
    var (context, connection) = TestDb.Create();
    await using var _ = context; await using var __ = connection;
    var bus = Substitute.For<IEventBus>();
    var handler = CreateHandler(context, bus);
    await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);
    var server = await context.RustServers.SingleAsync();
    bus.ClearReceivedCalls();

    await handler.HandleAsync(10UL, 1UL, AlarmTrigger(FpServer, 55UL, "Raid!", "Hit"), CancellationToken.None);

    await bus.Received(1).PublishAsync(
        Arg.Is<AlarmTriggeredEvent>(e => e.ServerId == server.Id && e.EntityId == 55UL && e.Title == "Raid!" && e.Message == "Hit"),
        Arg.Any<CancellationToken>());
}

[Fact]
public async Task AlarmTrigger_UnknownServer_DropsNoPublish()
{
    var (context, connection) = TestDb.Create();
    await using var _ = context; await using var __ = connection;
    var bus = Substitute.For<IEventBus>();
    var handler = CreateHandler(context, bus);

    await handler.HandleAsync(10UL, 1UL, AlarmTrigger(Guid.NewGuid(), 55UL), CancellationToken.None);

    await bus.DidNotReceive().PublishAsync(Arg.Any<AlarmTriggeredEvent>(), Arg.Any<CancellationToken>());
}
```

Add `using RustPlusBot.Domain.Entities;` to the test file.

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj -maxcpucount:1` → FAIL (`PairingKind.AlarmTriggered`, `EntityKind`, `Title`/`Message`, `AlarmPairedEvent` routing all missing).

- [ ] **Step 3: Extend `PairingNotification`**

Add the enum member and fields (keep the existing trailing-optional-params order; the new entity-kind/title/message are appended so existing positional callers still compile):

```csharp
internal enum PairingKind
{
    Server = 0,
    Entity = 1,
    AlarmTriggered = 2,
}
```

```csharp
internal sealed record PairingNotification(
    PairingKind Kind,
    string ServerName,
    string Ip,
    int Port,
    ulong PlayerId,
    string PlayerToken,
    Guid FacepunchServerId = default,
    ulong EntityId = 0UL,
    RustPlusBot.Domain.Entities.PairedEntityKind EntityKind = RustPlusBot.Domain.Entities.PairedEntityKind.SmartSwitch,
    string? Title = null,
    string? Message = null);
```

Add `<param>` doc lines for `EntityKind`, `Title`, `Message` and document the `AlarmTriggered` enum member (Roslynator doc rules). Add the `using` for `PairedEntityKind` if the file's style prefers it over the fully-qualified form (match neighboring code — the file currently has no usings, so the fully-qualified form above is safest).

- [ ] **Step 4: Extend `PairingHandler`** — route the entity kind and handle the trigger.

In `HandleAsync`, before the existing `PairingKind.Entity` branch, add:

```csharp
if (notification.Kind == PairingKind.AlarmTriggered)
{
    await HandleAlarmTriggerAsync(guildId, notification, cancellationToken).ConfigureAwait(false);
    return;
}
```

Replace `HandleEntityAsync`'s hardcoded publish with a kind switch:

```csharp
private async Task HandleEntityAsync(
    ulong guildId,
    PairingNotification notification,
    CancellationToken cancellationToken)
{
    var server = await servers
        .GetByFacepunchServerIdAsync(guildId, notification.FacepunchServerId, cancellationToken)
        .ConfigureAwait(false);
    if (server is null)
    {
        LogUnknownEntityServer(logger, notification.FacepunchServerId);
        return;
    }

    switch (notification.EntityKind)
    {
        case RustPlusBot.Domain.Entities.PairedEntityKind.SmartSwitch:
            await eventBus.PublishAsync(
                    new SwitchPairedEvent(guildId, server.Id, notification.EntityId), cancellationToken)
                .ConfigureAwait(false);
            break;
        case RustPlusBot.Domain.Entities.PairedEntityKind.SmartAlarm:
            await eventBus.PublishAsync(
                    new AlarmPairedEvent(guildId, server.Id, notification.EntityId), cancellationToken)
                .ConfigureAwait(false);
            break;
        default:
            // StorageMonitor (4c) and any future kind are not routed yet.
            LogUnroutedEntityKind(logger, notification.EntityKind);
            break;
    }
}

private async Task HandleAlarmTriggerAsync(
    ulong guildId,
    PairingNotification notification,
    CancellationToken cancellationToken)
{
    var server = await servers
        .GetByFacepunchServerIdAsync(guildId, notification.FacepunchServerId, cancellationToken)
        .ConfigureAwait(false);
    if (server is null)
    {
        LogUnknownEntityServer(logger, notification.FacepunchServerId);
        return;
    }

    await eventBus.PublishAsync(
            new AlarmTriggeredEvent(guildId, server.Id, notification.EntityId, notification.Title, notification.Message),
            cancellationToken)
        .ConfigureAwait(false);
}

[LoggerMessage(Level = LogLevel.Debug, Message = "Dropping entity pairing of unrouted kind {Kind}.")]
private static partial void LogUnroutedEntityKind(ILogger logger, RustPlusBot.Domain.Entities.PairedEntityKind kind);
```

Add `using RustPlusBot.Domain.Entities;` (or use the fully-qualified `PairedEntityKind` shown). The `Roslynator CA1308`/`S` rules: the `[LoggerMessage]` partial signature must use the enum type directly.

- [ ] **Step 5: Run the tests to verify they pass** — same command → PASS (existing switch tests unchanged + the 4 new ones).

- [ ] **Step 6: Build strict + commit**

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
git add src/RustPlusBot.Features.Pairing/Listening/PairingNotification.cs \
        src/RustPlusBot.Features.Pairing/Pairing/PairingHandler.cs \
        tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs
git commit -m "feat(alarms): route entity pairings by kind and publish alarm paired/triggered events"
```

---

## Task 5: FCM source — subscribe `OnSmartAlarmPairing` + `OnAlarmTriggered` (untested shim)

**Files:**

- Modify: `src/RustPlusBot.Features.Pairing/Listening/RustPlusFcmPairingSource.cs`

**Interfaces:**

- Consumes: `RustPlusApi.Fcm.RustPlusFcm.OnSmartAlarmPairing` (`EventHandler<Notification<ulong?>>`), `OnAlarmTriggered` (`EventHandler<Notification<AlarmEvent>>`), `AlarmEvent { string? Title, string? Message }`, raw `Body.EntityId` (`ulong?`).
- Produces: maps both to `PairingNotification` (kind `Entity`+`EntityKind.SmartAlarm`, and `AlarmTriggered`+title/message).

> **This file is an untested integration shim by design** (no unit test — same as the existing `OnServerPairing`/`OnSmartSwitchPairing` handlers). **VERIFY-LIVE carry-forward:** confirm against a live alarm that `OnAlarmTriggered`'s raw `Body.EntityId` is populated. If it is not, the trigger maps `EntityId: 0` and the downstream relay drops it (safe degrade per "drop unmatched fires"). Document whatever is observed in the foundation-status memory.

- [ ] **Step 1: Add subscriptions in the constructor** (next to the existing two), and unsubscribe in `DisposeAsync`:

```csharp
_fcm.OnSmartAlarmPairing += OnSmartAlarmPairing;
_fcm.OnAlarmTriggered += OnAlarmTriggered;
```

```csharp
_fcm.OnSmartAlarmPairing -= OnSmartAlarmPairing;
_fcm.OnAlarmTriggered -= OnAlarmTriggered;
```

- [ ] **Step 2: Add the alarm-pairing handler** (copy `OnSmartSwitchPairing`, set `EntityKind`):

```csharp
private void OnSmartAlarmPairing(object? sender, Notification<ulong?> e)
{
    if (e?.Data is not { } entityId)
    {
        return; // null entity id → drop
    }

    var notification = new PairingNotification(
        Kind: PairingKind.Entity,
        ServerName: string.Empty,
        Ip: string.Empty,
        Port: 0,
        PlayerId: e.PlayerId,
        PlayerToken: e.PlayerToken.ToString(System.Globalization.CultureInfo.InvariantCulture),
        FacepunchServerId: e.ServerId,
        EntityId: entityId,
        EntityKind: RustPlusBot.Domain.Entities.PairedEntityKind.SmartAlarm);

    Dispatch(notification);
}
```

- [ ] **Step 3: Add the alarm-trigger handler.** Read the raw `Body.EntityId` the same way the source already reaches `Body` for other notifications (inspect the file's existing `Body` access; the `Notification<AlarmEvent>` exposes `e.Data` for Title/Message and the raw body via the package's message-data path). If the typed event does not surface `Body` directly, map `EntityId: 0` and rely on the downstream drop:

```csharp
private void OnAlarmTriggered(object? sender, Notification<AlarmEvent> e)
{
    if (e is null)
    {
        return;
    }

    // AlarmEvent carries Title/Message only; the entity id (if present) comes from the raw Body.
    // VERIFY-LIVE: confirm Body.EntityId is populated on a trigger; if absent, entityId stays 0 and the
    // downstream relay drops the fire (per the "drop unmatched fires" decision).
    var entityId = TryReadAlarmEntityId(e);     // returns ulong (0 when unavailable) — see note

    var notification = new PairingNotification(
        Kind: PairingKind.AlarmTriggered,
        ServerName: string.Empty,
        Ip: string.Empty,
        Port: 0,
        PlayerId: e.PlayerId,
        PlayerToken: e.PlayerToken.ToString(System.Globalization.CultureInfo.InvariantCulture),
        FacepunchServerId: e.ServerId,
        EntityId: entityId,
        EntityKind: RustPlusBot.Domain.Entities.PairedEntityKind.SmartAlarm,
        Title: e.Data?.Title,
        Message: e.Data?.Message);

    Dispatch(notification);
}
```

`TryReadAlarmEntityId` is a tiny private helper: inspect the actual `Notification<AlarmEvent>` runtime shape during execution. If the package exposes the raw body (e.g. via a `Body`/`MessageData` property on the notification), read `Body.EntityId ?? 0`. If it does not, the helper returns `0`. Implement it concretely against the observed API — **do not leave it as a stub**; if no body is reachable, inline `var entityId = 0UL;` with the VERIFY-LIVE comment instead of a helper.

- [ ] **Step 4: Build strict** — `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` → 0/0. (No unit test for this shim; the `PairingHandler` tests in Task 4 cover the mapped notification's behavior.)

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Pairing/Listening/RustPlusFcmPairingSource.cs
git commit -m "feat(alarms): subscribe FCM OnSmartAlarmPairing and OnAlarmTriggered"
```

---

## Task 6: Workspace — `#alarms` channel spec, i18n, and locator

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/IAlarmChannelLocator.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/AlarmChannelLocator.cs`
- Modify: wherever `ISwitchChannelLocator` is registered (find it: `grep -rn AddSingleton.*ChannelLocator src/RustPlusBot.Features.Workspace`)
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Locating/AlarmChannelLocatorTests.cs`

**Interfaces:**

- Consumes: `IWorkspaceStore.GetChannelsByKeyAsync`, `WorkspaceChannelKeys.ServerAlarms`, `IClock`.
- Produces: `WorkspaceChannelKeys.ServerAlarms = "alarms"`; `IAlarmChannelLocator.GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken)`.

- [ ] **Step 1: Write the failing locator test** (copy `tests/RustPlusBot.Features.Workspace.Tests/Locating/SwitchChannelLocatorTests.cs` exactly, retyped to `AlarmChannelLocator` + `WorkspaceChannelKeys.ServerAlarms`). It seeds a fake/substitute `IWorkspaceStore` returning a channel row for the `alarms` key and asserts `GetChannelIdAsync` returns it, plus the 30s TTL refresh behavior the switch test covers.

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj -maxcpucount:1` → FAIL (`AlarmChannelLocator`, `ServerAlarms` missing).

- [ ] **Step 3: Add the channel key** in `WorkspaceKeys.cs` next to `ServerSwitches`:

```csharp
public const string ServerAlarms = "alarms";
```

- [ ] **Step 4: Add the ChannelSpec** in `ServerWorkspaceSpecProvider.GetChannelSpecs()` (append, sort index 5):

```csharp
new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerAlarms, "channel.alarms.name",
    ChannelPermissionProfile.Interactive, 5),
```

- [ ] **Step 5: Add EN/FR names** in `LocalizationCatalog.cs` next to `channel.switches.name`:

```csharp
["channel.alarms.name"] = "alarms",   // en
["channel.alarms.name"] = "alarmes",  // fr
```

- [ ] **Step 6: Create `IAlarmChannelLocator`** (copy `ISwitchChannelLocator`) and **`AlarmChannelLocator`** (copy `SwitchChannelLocator` verbatim, swapping `ServerSwitches` → `ServerAlarms`, type names, and the doc text). Keep `IDisposable` (CA1001 — it owns a `SemaphoreSlim`).

- [ ] **Step 7: Register the locator** — in the same place `ISwitchChannelLocator` is registered, add:

```csharp
services.AddSingleton<IAlarmChannelLocator, AlarmChannelLocator>();
```

- [ ] **Step 8: Run tests to verify they pass** — same command → PASS. Also run the full Workspace suite and confirm its count didn't drop (a new `IWorkspaceStore` member would break `FakeWorkspaceStore`, but this task adds none).

- [ ] **Step 9: Build strict + commit**

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests/Locating/AlarmChannelLocatorTests.cs
git commit -m "feat(alarms): provision #alarms channel + add AlarmChannelLocator"
```

---

## Task 7a: Shared `ILocalizer` + `IChannelEmbedPoster` in `RustPlusBot.Discord`

> **Why this task exists (execution-time decision):** instead of copying the switch slice's `SwitchLocalizer`/`SwitchLocalizationCatalog` shape (the 5th localizer copy) and `DiscordSwitchChannelPoster` verbatim, 4b extracts two reusable pieces into `RustPlusBot.Discord` (already referenced by every feature project) and `Features.Alarms` consumes them. **Scope is deliberately minimal:** only the localizer lookup logic and the embed poster shim are shared; store/relay/coordinator/module/hosted-service stay slice-specific. **Existing switch/event/player slices are NOT retro-migrated** in 4b — the shared pieces are additive; only alarms use them. (A follow-up may migrate the others.)

**Files:**

- Create: `src/RustPlusBot.Discord/Localization/ILocalizer.cs`
- Create: `src/RustPlusBot.Discord/Localization/Localizer.cs`
- Create: `src/RustPlusBot.Discord/Posting/IChannelEmbedPoster.cs`
- Create: `src/RustPlusBot.Discord/Posting/DiscordChannelEmbedPoster.cs`
- Modify: `src/RustPlusBot.Discord/RustPlusBot.Discord.csproj` — add `<InternalsVisibleTo Include="RustPlusBot.Discord.Tests" />` and `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` **only if** `ILocalizer`/`Localizer` are `internal`; **make them `public`** instead (they're consumed by other projects) — so no InternalsVisibleTo is needed for cross-project use. The poster is `public` too.
- Create (if absent): `tests/RustPlusBot.Discord.Tests/RustPlusBot.Discord.Tests.csproj` (check first: `ls tests/RustPlusBot.Discord.Tests` — if it doesn't exist, scaffold it by copying another test csproj and retargeting to `RustPlusBot.Discord`, then `dotnet sln RustPlusBot.slnx add`).
- Test: `tests/RustPlusBot.Discord.Tests/LocalizerTests.cs`

**Interfaces:**

- Consumes: nothing new (pure utilities over Discord.Net types the project already references).
- Produces:
  - `public interface ILocalizer { string Get(string key, string culture); string Get(string key, string culture, params object[] args); }`
  - `public sealed class Localizer(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> strings) : ILocalizer` — dictionary-catalog lookup with `"en"` fallback + region normalization (`"fr-FR"` → `"fr"`), and `string.Format` with a culture-resolved provider. **A feature passes its own catalog dictionary to the constructor** (the catalog stays in the feature project; only the lookup logic is shared).
  - `public interface IChannelEmbedPoster { Task<ulong?> EnsureAsync(ulong channelId, ulong? messageId, global::Discord.Embed embed, global::Discord.MessageComponent components, CancellationToken ct); Task SendEveryonePingAsync(ulong channelId, string content, CancellationToken ct); }`
  - `internal sealed class DiscordChannelEmbedPoster(DiscordSocketClient client, ILogger<DiscordChannelEmbedPoster> logger) : IChannelEmbedPoster` — the ensure/edit/self-heal shim **plus** the `@everyone` ping send. (The interface is `public`; the impl can be `internal` since it's resolved via DI by interface — but if a consuming test needs to `new` it, make it `public`. Default: `public sealed` to match `DiscordSwitchChannelPoster`'s accessibility and avoid InternalsVisibleTo churn.)

- [ ] **Step 1: Confirm the Discord test project exists** — `ls tests/RustPlusBot.Discord.Tests 2>/dev/null`. If absent, copy `tests/RustPlusBot.Features.Switches.Tests/RustPlusBot.Features.Switches.Tests.csproj` to `tests/RustPlusBot.Discord.Tests/RustPlusBot.Discord.Tests.csproj`, retarget its ProjectReference to `src/RustPlusBot.Discord/RustPlusBot.Discord.csproj`, and `dotnet sln RustPlusBot.slnx add tests/RustPlusBot.Discord.Tests/RustPlusBot.Discord.Tests.csproj`.

- [ ] **Step 2: Write the failing `Localizer` tests** — assert: exact-culture hit returns the value; missing key falls back to `"en"`; missing in `"en"` too returns the key itself; `"fr-FR"` normalizes to `"fr"`; the `params` overload formats with args using the culture provider. Construct `new Localizer(catalog)` with a tiny inline 2-culture catalog.

```csharp
using RustPlusBot.Discord.Localization;

namespace RustPlusBot.Discord.Tests;

public sealed class LocalizerTests
{
    private static Localizer Make() => new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
    {
        ["en"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "Hello {0}", ["only.en"] = "EN" },
        ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "Bonjour {0}" },
    });

    [Fact] public void ExactCulture_ReturnsValue() => Assert.Equal("Bonjour {0}", Make().Get("k", "fr"));
    [Fact] public void RegionNormalizes() => Assert.Equal("Bonjour {0}", Make().Get("k", "fr-FR"));
    [Fact] public void MissingKey_FallsBackToEnglish() => Assert.Equal("EN", Make().Get("only.en", "fr"));
    [Fact] public void MissingEverywhere_ReturnsKey() => Assert.Equal("nope", Make().Get("nope", "fr"));
    [Fact] public void Format_UsesArgs() => Assert.Equal("Bonjour Bob", Make().Get("k", "fr", "Bob"));
}
```

- [ ] **Step 3: Run to verify it fails** — `dotnet test tests/RustPlusBot.Discord.Tests/RustPlusBot.Discord.Tests.csproj -maxcpucount:1` → FAIL (`Localizer` missing).

- [ ] **Step 4: Implement `ILocalizer` + `Localizer`** — lift the body of `src/RustPlusBot.Features.Switches/Rendering/SwitchLocalizer.cs` verbatim (the `Get`, `Normalize`, `ResolveFormatProvider` logic with `FallbackCulture = "en"`), but: (a) `public` types in namespace `RustPlusBot.Discord.Localization`; (b) the catalog dictionary is a **constructor parameter** (`IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> strings`), not a separate `*LocalizationCatalog` class. Full XML docs (RCS1141).

- [ ] **Step 5: Run to verify it passes** — same command → PASS.

- [ ] **Step 6: Implement `IChannelEmbedPoster` + `DiscordChannelEmbedPoster`** — lift `src/RustPlusBot.Features.Switches/Posting/DiscordSwitchChannelPoster.cs` verbatim (the `EnsureAsync` ensure/edit/self-heal-on-HttpException-404 shim, `using Discord.Net; using Discord.WebSocket; global::Discord.*`), renamed to `DiscordChannelEmbedPoster` in namespace `RustPlusBot.Discord.Posting`, `public sealed`. Add `SendEveryonePingAsync(ulong channelId, string content, CancellationToken ct)`: resolve the channel via `GetChannelAsync`, `channel.SendMessageAsync(content, allowedMentions: global::Discord.AllowedMentions.All, options: options)`, wrapped in the same broad-catch-logs-and-swallows pattern (a ping failure must not crash the relay; rethrow `OperationCanceledException`). Generalize the log messages from "switch embed" to "channel embed".

- [ ] **Step 7: Build strict** — `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` → 0/0. (The poster is an untested shim like its switch original; the `Localizer` is covered by Step 2.)

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Discord/Localization src/RustPlusBot.Discord/Posting tests/RustPlusBot.Discord.Tests RustPlusBot.slnx
git commit -m "feat(discord): extract shared ILocalizer + IChannelEmbedPoster for feature slices"
```

---

## Task 7: Alarms feature project scaffold — catalog + component ids

**Files:**

- Create: `src/RustPlusBot.Features.Alarms/RustPlusBot.Features.Alarms.csproj`
- Create: `src/RustPlusBot.Features.Alarms/Rendering/AlarmComponentIds.cs`
- Create: `src/RustPlusBot.Features.Alarms/Rendering/AlarmLocalizationCatalog.cs`
- Create: `tests/RustPlusBot.Features.Alarms.Tests/RustPlusBot.Features.Alarms.Tests.csproj`
- Test: `tests/RustPlusBot.Features.Alarms.Tests/AlarmLocalizationCatalogTests.cs`

**Interfaces:**

- Consumes: the shared `ILocalizer`/`Localizer` and `IChannelEmbedPoster` from Task 7a (in `RustPlusBot.Discord`).
- Produces: `AlarmComponentIds` consts; `AlarmLocalizationCatalog.Default` — a `public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Default { get; }` (the `culture → key → value` dictionary, **no** `IAlarmLocalizer` class; the alarm catalog is passed to a shared `Localizer` at DI time in Task 12). **No `IAlarmLocalizer`/`AlarmLocalizer` and no `IChannelEmbedPoster`/`DiscordAlarmChannelPoster`** — those are the shared pieces from Task 7a.

- [ ] **Step 1: Create the csproj** — copy `src/RustPlusBot.Features.Switches/RustPlusBot.Features.Switches.csproj`, add the `RustPlusBot.Features.Connections` ProjectReference (for `ITeamChatSender`); `RustPlusBot.Discord` is already referenced by the switch csproj copy (it provides the shared localizer/poster). Keep `<InternalsVisibleTo Include="RustPlusBot.Features.Alarms.Tests" />` and `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />`. Add the project to the solution:

```bash
dotnet sln RustPlusBot.slnx add src/RustPlusBot.Features.Alarms/RustPlusBot.Features.Alarms.csproj
```

- [ ] **Step 2: Create the test csproj** — copy `tests/RustPlusBot.Features.Switches.Tests/*.csproj`, retarget the ProjectReference to `RustPlusBot.Features.Alarms`. Add to the solution.

- [ ] **Step 3: Write the failing catalog test** (copy `SwitchLocalizationCatalogTests`) — assert EN and FR maps in `AlarmLocalizationCatalog.Default` both contain every alarm key and have identical key sets. Keys (Step 5 list): `alarm.status.idle`, `alarm.status.fired`, `alarm.status.unreachable`, `alarm.button.ping.on`, `alarm.button.ping.off`, `alarm.button.relay.on`, `alarm.button.relay.off`, `alarm.button.rename`, `alarm.embed.footer`, `alarm.embed.neverfired`, `alarm.prompt.title`, `alarm.prompt.body`, `alarm.prompt.accept`, `alarm.prompt.dismiss`, `alarm.rename.modal.title`, `alarm.rename.input.label`.

- [ ] **Step 4: Run to verify it fails** — `dotnet test tests/RustPlusBot.Features.Alarms.Tests/RustPlusBot.Features.Alarms.Tests.csproj -maxcpucount:1` → FAIL (catalog missing).

- [ ] **Step 5: Create `AlarmComponentIds`** (copy `SwitchComponentIds`, alarm tails):

```csharp
namespace RustPlusBot.Features.Alarms.Rendering;

/// <summary>Custom ids for alarm components. Tails encode "{serverId}:{entityId}".</summary>
internal static class AlarmComponentIds
{
    public const string AcceptPrefix = "alarm:accept:";
    public const string DismissPrefix = "alarm:dismiss:";
    public const string PingTogglePrefix = "alarm:ping:";
    public const string RelayTogglePrefix = "alarm:relay:";
    public const string RenamePrefix = "alarm:rename:";
    public const string RenameModalPrefix = "alarm:rename:modal:";
    public const string RenameInputId = "alarm:rename:input";
}
```

Doc each const (match the switch file).

- [ ] **Step 6: Create `AlarmLocalizationCatalog`** exposing `public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Default { get; }` (the dictionary shape the shared `Localizer` ctor takes — copy the `Strings` dictionary structure from `SwitchLocalizationCatalog` but expose it as `Default` directly, `StringComparer.Ordinal` on both levels). EN + FR for all keys:

```csharp
["alarm.status.idle"] = "🔔 Armed",
["alarm.status.fired"] = "🚨 Fired",
["alarm.status.unreachable"] = "⚠️ Unreachable",
["alarm.button.ping.on"] = "Ping @everyone: on",
["alarm.button.ping.off"] = "Ping @everyone: off",
["alarm.button.relay.on"] = "Relay to team chat: on",
["alarm.button.relay.off"] = "Relay to team chat: off",
["alarm.button.rename"] = "Rename",
["alarm.embed.footer"] = "Entity {0}",
["alarm.embed.neverfired"] = "Never fired",
["alarm.prompt.title"] = "New alarm detected",
["alarm.prompt.body"] = "Detected a new Smart Alarm ({0}). Add it?",
["alarm.prompt.accept"] = "Accept",
["alarm.prompt.dismiss"] = "Dismiss",
["alarm.rename.modal.title"] = "Rename alarm",
["alarm.rename.input.label"] = "Alarm name",
```

FR:

```csharp
["alarm.status.idle"] = "🔔 Armée",
["alarm.status.fired"] = "🚨 Déclenchée",
["alarm.status.unreachable"] = "⚠️ Injoignable",
["alarm.button.ping.on"] = "Ping @everyone : activé",
["alarm.button.ping.off"] = "Ping @everyone : désactivé",
["alarm.button.relay.on"] = "Relais tchat équipe : activé",
["alarm.button.relay.off"] = "Relais tchat équipe : désactivé",
["alarm.button.rename"] = "Renommer",
["alarm.embed.footer"] = "Entité {0}",
["alarm.embed.neverfired"] = "Jamais déclenchée",
["alarm.prompt.title"] = "Nouvelle alarme détectée",
["alarm.prompt.body"] = "Nouvelle alarme connectée détectée ({0}). L'ajouter ?",
["alarm.prompt.accept"] = "Accepter",
["alarm.prompt.dismiss"] = "Ignorer",
["alarm.rename.modal.title"] = "Renommer l'alarme",
["alarm.rename.input.label"] = "Nom de l'alarme",
```

- [ ] **Step 7: Run the catalog test to verify it passes** — same command → PASS. (No per-slice localizer or poster — those are the shared Task 7a pieces. The alarm catalog dictionary is wired to a shared `Localizer` instance in Task 12's `AddAlarms`.)

- [ ] **Step 8: Build strict + commit**

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
git add src/RustPlusBot.Features.Alarms tests/RustPlusBot.Features.Alarms.Tests RustPlusBot.slnx
git commit -m "feat(alarms): scaffold Features.Alarms project (component ids + localization catalog)"
```

---

## Task 8: `AlarmEmbedRenderer`

**Files:**

- Create: `src/RustPlusBot.Features.Alarms/Rendering/AlarmEmbedRenderer.cs`
- Test: `tests/RustPlusBot.Features.Alarms.Tests/AlarmEmbedRendererTests.cs`

**Interfaces:**

- Consumes: the shared `ILocalizer` (from Task 7a), `SmartAlarm`, `AlarmComponentIds`.
- Produces: `internal sealed class AlarmEmbedRenderer(ILocalizer localizer)` with:
  - `(global::Discord.Embed Embed, global::Discord.MessageComponent Components) RenderAlarm(SmartAlarm alarm, bool unreachable, string culture)`
  - `(global::Discord.Embed Embed, global::Discord.MessageComponent Components) RenderPrompt(Guid serverId, ulong entityId, string defaultName, string culture)`

- [ ] **Step 1: Write the failing renderer tests** (copy `SwitchEmbedRendererTests` shape). Cover:
  - never-fired alarm → description contains the localized "Never fired"; title is the name.
  - fired alarm (`LastTitle`/`LastMessage`/`LastFiredUtc` set) → description contains the last title + message.
  - `unreachable: true` → status shows unreachable; ping/relay/rename buttons disabled.
  - ping toggle on → the ping button label is the `.on` string (and `.off` when off); same for relay.
  - EN vs FR pick the right strings.
  Assert on `embed.Description`/`embed.Title` and the component button labels/`IsDisabled` (mirror how the switch renderer test reaches `MessageComponent` rows — copy that traversal).

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/RustPlusBot.Features.Alarms.Tests/... -maxcpucount:1` → FAIL (renderer missing).

- [ ] **Step 3: Implement `AlarmEmbedRenderer`** (model on `SwitchEmbedRenderer`; `using Discord;` like the switch renderer):

```csharp
public (Embed Embed, MessageComponent Components) RenderAlarm(SmartAlarm alarm, bool unreachable, string culture)
{
    ArgumentNullException.ThrowIfNull(alarm);
    var statusKey = unreachable ? "alarm.status.unreachable"
        : alarm.LastFiredUtc is null ? "alarm.status.idle" : "alarm.status.fired";

    var body = alarm.LastFiredUtc is null
        ? localizer.Get("alarm.embed.neverfired", culture)
        : $"{alarm.LastTitle}\n{alarm.LastMessage}";

    var embed = new EmbedBuilder()
        .WithTitle(alarm.Name)
        .WithDescription($"{localizer.Get(statusKey, culture)}\n{body}")
        .WithFooter(localizer.Get("alarm.embed.footer", culture, alarm.EntityId))
        .Build();

    var tail = $"{alarm.ServerId}:{alarm.EntityId}";
    var pingKey = alarm.PingEveryone ? "alarm.button.ping.on" : "alarm.button.ping.off";
    var relayKey = alarm.RelayToTeamChat ? "alarm.button.relay.on" : "alarm.button.relay.off";
    var components = new ComponentBuilder()
        .WithButton(localizer.Get(pingKey, culture), AlarmComponentIds.PingTogglePrefix + tail,
            alarm.PingEveryone ? ButtonStyle.Success : ButtonStyle.Secondary, disabled: unreachable)
        .WithButton(localizer.Get(relayKey, culture), AlarmComponentIds.RelayTogglePrefix + tail,
            alarm.RelayToTeamChat ? ButtonStyle.Success : ButtonStyle.Secondary, disabled: unreachable)
        .WithButton(localizer.Get("alarm.button.rename", culture), AlarmComponentIds.RenamePrefix + tail,
            ButtonStyle.Secondary, disabled: unreachable)
        .Build();

    return (embed, components);
}
```

`RenderPrompt` is a direct copy of the switch one with `alarm.prompt.*` keys + `AlarmComponentIds.Accept/Dismiss`.

- [ ] **Step 4: Run to verify it passes** — same command → PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Alarms/Rendering/AlarmEmbedRenderer.cs \
        tests/RustPlusBot.Features.Alarms.Tests/AlarmEmbedRendererTests.cs
git commit -m "feat(alarms): add AlarmEmbedRenderer (idle/fired/unreachable + toggle buttons)"
```

---

## Task 9: `AlarmPairingCoordinator`

**Files:**

- Create: `src/RustPlusBot.Features.Alarms/Pairing/AlarmPairingCoordinator.cs`
- Test: `tests/RustPlusBot.Features.Alarms.Tests/AlarmPairingCoordinatorTests.cs`

**Interfaces:**

- Consumes: `IServiceScopeFactory`, `IAlarmChannelLocator`, `IChannelEmbedPoster`, `AlarmEmbedRenderer`, `IAlarmStore`, `IWorkspaceStore` (for `GetCultureAsync`), `AlarmPairedEvent`.
- Produces: `internal sealed class AlarmPairingCoordinator` with `HandlePairedAsync(AlarmPairedEvent, CancellationToken)`, `Task<bool> TryAcceptAsync(ulong guildId, Guid serverId, ulong entityId, ulong acceptingUserId, CancellationToken)`, `bool TryDismiss(ulong guildId, Guid serverId, ulong entityId)`, `string? PendingName(ulong, Guid, ulong)`.

- [ ] **Step 1: Write the failing coordinator tests** (copy `SwitchPairingCoordinatorTests`). Cover: paired alarm not yet managed → posts the prompt with default name `Alarm <id>` + holds pending; paired alarm already in `IAlarmStore` (`ExistsAsync` true) → no prompt; `TryAcceptAsync` persists (default name) + replaces the prompt with the alarm embed + returns true; `TryAcceptAsync` when already managed → returns false (race no-op); `TryDismiss` removes pending. Use NSubstitute for the locator/poster/store and the `SwitchPairingCoordinatorTests` scope-factory helper (a real `ServiceCollection` with substitutes registered, or whatever that file uses — copy it).

- [ ] **Step 2: Run to verify it fails** — FAIL (coordinator missing).

- [ ] **Step 3: Implement `AlarmPairingCoordinator`** — copy `SwitchPairingCoordinator` verbatim, swapping: `SwitchPairedEvent`→`AlarmPairedEvent`, `ISwitchStore`→`IAlarmStore`, `ISwitchChannelLocator`→`IAlarmChannelLocator`, `ISwitchChannelPoster`→`IChannelEmbedPoster`, `SwitchEmbedRenderer`→`AlarmEmbedRenderer`, default name `"Switch "`→`"Alarm "`, and the accept render call `renderer.RenderSwitch(added, isActive: added.LastIsActive, culture)` → `renderer.RenderAlarm(added, unreachable: false, culture)`. Keep the same `ConcurrentDictionary` pending map, `ExistsAsync` guard, scope-per-store-access idiom, and `SetMessageIdAsync` after post.

- [ ] **Step 4: Run to verify it passes** — PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Alarms/Pairing/AlarmPairingCoordinator.cs \
        tests/RustPlusBot.Features.Alarms.Tests/AlarmPairingCoordinatorTests.cs
git commit -m "feat(alarms): add AlarmPairingCoordinator (prompt + accept/dismiss)"
```

---

## Task 10: `AlarmRefresher` + `AlarmFireRelay`

This task ships two collaborators: a small reusable `AlarmRefresher` (load → render → post; the single render/post code path reused by the relay, the coordinator's post-accept render, and the module's post-toggle render — DRY) and the `AlarmFireRelay` that consumes it. Both are unit-tested.

**Files:**

- Create: `src/RustPlusBot.Features.Alarms/Relaying/AlarmRefresher.cs`
- Create: `src/RustPlusBot.Features.Alarms/Relaying/AlarmFireRelay.cs`
- Test: `tests/RustPlusBot.Features.Alarms.Tests/AlarmRefresherTests.cs`
- Test: `tests/RustPlusBot.Features.Alarms.Tests/AlarmFireRelayTests.cs`

**Interfaces:**

- Consumes: `IServiceScopeFactory`, `IAlarmChannelLocator`, `IChannelEmbedPoster`, `AlarmEmbedRenderer`, `IAlarmStore`, `IConnectionStore` (connection status on disconnect), `IWorkspaceStore` (culture), `RustPlusBot.Features.Connections.Listening.ITeamChatSender`, `RustPlusBot.Abstractions.Time.IClock`, `AlarmTriggeredEvent`, `ConnectionStatusChangedEvent`, `RustPlusBot.Domain.Connections.ConnectionStatus`.
- Produces:
  - `IAlarmRefresher` (interface) + `internal sealed class AlarmRefresher(IServiceScopeFactory scopeFactory, IAlarmChannelLocator locator, IChannelEmbedPoster poster, AlarmEmbedRenderer renderer) : IAlarmRefresher` with `Task RefreshAsync(ulong guildId, Guid serverId, ulong entityId, bool unreachable, CancellationToken)` — loads the alarm + guild culture, resolves the channel, renders, posts via `EnsureAsync`, and persists a changed `MessageId`. No-op when the alarm or channel can't be resolved. The interface exists so `AlarmFireRelay` and `AlarmComponentModule` can be exercised/substituted against it (add `<InternalsVisibleTo DynamicProxyGenAssembly2>` already present).
  - `internal sealed class AlarmFireRelay` with `HandleTriggeredAsync(AlarmTriggeredEvent, CancellationToken)` and `HandleConnectionStatusAsync(ConnectionStatusChangedEvent, CancellationToken)`.

> Check `ITeamChatSender`'s exact method name/signature in `src/RustPlusBot.Features.Connections/Listening/ITeamChatSender.cs` before writing the relay call (the spec calls it `SendAsync(...)` — confirm and adapt). Check `IConnectionStore.GetStateAsync` + `ConnectionStatus` names against `SwitchStateRelay` (it already uses them). For culture, copy `SwitchStateRelay`'s private `GetCultureAsync(provider, guildId, ct)` helper.

- [ ] **Step 1: Write the failing `AlarmRefresher` tests**. Cover: alarm present + channel resolved → `renderer.RenderAlarm(alarm, unreachable, culture)` then `poster.EnsureAsync` called; a returned-new `MessageId` is persisted via `store.SetMessageIdAsync`; alarm absent → no post; channel unresolved → no post. Use NSubstitute for locator/poster + the scope-factory helper (copy from `AlarmPairingCoordinatorTests`), with `AlarmEmbedRenderer` constructed real over a real shared `Localizer(AlarmLocalizationCatalog.Default)` (it's pure).

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/RustPlusBot.Features.Alarms.Tests/RustPlusBot.Features.Alarms.Tests.csproj -maxcpucount:1` → FAIL (`AlarmRefresher` missing).

- [ ] **Step 3: Implement `IAlarmRefresher` + `AlarmRefresher`** — define the one-method `IAlarmRefresher` interface, then implement `AlarmRefresher : IAlarmRefresher` modelled on `SwitchStateRelay.RenderAsync` (the load-channel → render → `EnsureAsync` → persist-changed-`MessageId` block), parameterized by `(guildId, serverId, entityId, unreachable)` and opening its own scope:

```csharp
public async Task RefreshAsync(ulong guildId, Guid serverId, ulong entityId, bool unreachable, CancellationToken cancellationToken)
{
    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
        var alarm = await store.GetAsync(guildId, serverId, entityId, cancellationToken).ConfigureAwait(false);
        if (alarm is null)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (channelId is not { } channel)
        {
            return;
        }

        var culture = await GetCultureAsync(scope.ServiceProvider, guildId, cancellationToken).ConfigureAwait(false);
        var (embed, components) = renderer.RenderAlarm(alarm, unreachable, culture);
        var newId = await poster.EnsureAsync(channel, alarm.MessageId, embed, components, cancellationToken).ConfigureAwait(false);
        if (newId is { } mid && mid != alarm.MessageId)
        {
            await store.SetMessageIdAsync(guildId, serverId, entityId, mid, cancellationToken).ConfigureAwait(false);
        }
    }
}
```

Include the `GetCultureAsync` private static helper copied from `SwitchStateRelay`.

- [ ] **Step 4: Run to verify the `AlarmRefresher` tests pass** — PASS.

- [ ] **Step 5: Write the failing `AlarmFireRelay` tests** (model on `SwitchStateRelayTests`). Cover:
  - **fired + matched alarm:** `RecordFiredAsync` called with the event's title/message; `IAlarmRefresher.RefreshAsync(..., unreachable: false)` invoked — assert via an NSubstitute `Substitute.For<IAlarmRefresher>()` injected into the relay (the interface from Step 3 makes this clean).
  - **fired + `PingEveryone` true:** poster `SendEveryonePingAsync` called; false → not called.
  - **fired + `RelayToTeamChat` true:** `ITeamChatSender` send called with the message; false → not called.
  - **fired + no matching alarm (`GetAsync` null):** nothing recorded/refreshed/pinged/relayed (drop).
  - **relay send throws:** record + refresh + ping still complete (failure swallowed).
  - **connection status not-Connected:** `RefreshAsync(..., unreachable: true)` invoked per alarm for that server; **Connected → no-op**.

- [ ] **Step 6: Run to verify it fails** — FAIL (`AlarmFireRelay` / `IAlarmRefresher` missing).

- [ ] **Step 7: Implement `AlarmFireRelay`** (depending on the `IAlarmRefresher` from Step 3):

```csharp
public async Task HandleTriggeredAsync(AlarmTriggeredEvent evt, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(evt);
    bool pingEveryone, relayToTeamChat;
    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
        var alarm = await store.GetAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false);
        if (alarm is null)
        {
            LogUnmatchedFire(logger, evt.ServerId, evt.EntityId);   // drop unmatched fire
            return;
        }

        pingEveryone = alarm.PingEveryone;
        relayToTeamChat = alarm.RelayToTeamChat;
        await store.RecordFiredAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.Title, evt.Message, clock.UtcNow, cancellationToken).ConfigureAwait(false);
    }

    await refresher.RefreshAsync(evt.GuildId, evt.ServerId, evt.EntityId, unreachable: false, cancellationToken).ConfigureAwait(false);

    if (pingEveryone)
    {
        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
        if (channelId is { } channel)
        {
            await poster.SendEveryonePingAsync(channel, $"@everyone {evt.Title} — {evt.Message}", cancellationToken).ConfigureAwait(false);
        }
    }

    if (relayToTeamChat)
    {
        await RelayToTeamChatSafeAsync(evt, cancellationToken).ConfigureAwait(false);
    }
}
```

`RelayToTeamChatSafeAsync` calls the real `ITeamChatSender` method (confirmed in the pre-step) with `$"{evt.Title}: {evt.Message}"` inside a broad-catch that logs+swallows. `HandleConnectionStatusAsync` copies `SwitchStateRelay.HandleConnectionStatusAsync` but calls `refresher.RefreshAsync(..., unreachable: true)` per listed alarm (Connected → return). Inject `IClock clock` (analyzers forbid `DateTimeOffset.UtcNow`; the test controls time). Constructor deps: `(IServiceScopeFactory scopeFactory, IAlarmRefresher refresher, IAlarmChannelLocator locator, IChannelEmbedPoster poster, ITeamChatSender teamChatSender, IClock clock, ILogger<AlarmFireRelay> logger)`. Add `[LoggerMessage]` for `LogUnmatchedFire` + a relay-failure warning.

- [ ] **Step 8: Run to verify it passes** — PASS.

- [ ] **Step 9: Build strict + commit**

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
git add src/RustPlusBot.Features.Alarms/Relaying tests/RustPlusBot.Features.Alarms.Tests/AlarmRefresherTests.cs tests/RustPlusBot.Features.Alarms.Tests/AlarmFireRelayTests.cs
git commit -m "feat(alarms): add AlarmRefresher + AlarmFireRelay (record, ping, relay, drop, unreachable)"
```

---

## Task 11: `AlarmComponentModule` + `AlarmRenameModal` (untested thin module)

**Files:**

- Create: `src/RustPlusBot.Features.Alarms/Modules/AlarmRenameModal.cs`
- Create: `src/RustPlusBot.Features.Alarms/Modules/AlarmComponentModule.cs`

**Interfaces:**

- Consumes: `IServiceScopeFactory`, `IAlarmStore`, `AlarmPairingCoordinator`, `IAlarmRefresher` (from Task 10), `AlarmComponentIds`, `AlarmRenameModal`.

> **Refresh mechanism:** unlike switches, alarms have no "state changed" bus event and no live socket, so the module re-renders an embed after a toggle/rename by calling `IAlarmRefresher.RefreshAsync(guildId, serverId, entityId, unreachable: false, ct)` (the shared service built and tested in Task 10). The module holds **no** render/post logic itself — it mutates the store, then delegates the visual refresh, staying thin and untested (the refresher + store it calls are both covered).

- [ ] **Step 1: Create `AlarmRenameModal`** — copy `SwitchRenameModal`, retitle keys to `alarm.rename.*`, input id `AlarmComponentIds.RenameInputId`.

- [ ] **Step 2: Create `AlarmComponentModule`** — copy `SwitchComponentModule`'s structure, but:
  - constructor: `(IServiceScopeFactory scopeFactory, IAlarmRefresher refresher)` — **no `IRustServerQuery`** (alarms have no socket control).
  - `AcceptAsync`/`DismissAsync`: identical to switch (resolve `AlarmPairingCoordinator` from scope, `TryAcceptAsync`/`TryDismiss`, delete prompt on dismiss).
  - `PingToggleAsync` (`[ComponentInteraction(AlarmComponentIds.PingTogglePrefix + "*")]`): defer ephemeral; open scope; `store.GetAsync` → flip → `store.SetPingEveryoneAsync(!current)`; `refresher.RefreshAsync(..., unreachable: false)`; followup "Updated." (Determine unreachable via `IConnectionStore` if you want precision, or pass `false` and let the next status event correct it — pass `false`; the toggle is a preference and the embed's reachability is refreshed on status changes anyway.)
  - `RelayToggleAsync`: same against `SetRelayToTeamChatAsync`.
  - `RenamePromptAsync` + `RenameSubmitAsync`: copy switch, `store.RenameAsync` then `refresher.RefreshAsync`. Default name on blank → `"Alarm " + entityId`.
  - Reuse the switch module's `TryParse`/`DeletePromptMessageSafeAsync` helpers verbatim.
  - `public sealed class AlarmComponentModule(...) : InteractionModuleBase<SocketInteractionContext>`. No `[RequireUserPermission]`.

- [ ] **Step 3: Build strict** — `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` → 0/0. (No unit test — `InteractionModuleBase` isn't unit-tested in this repo; the `AlarmRefresher`, store, and coordinator it calls are all covered.)

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Alarms/Modules
git commit -m "feat(alarms): add AlarmComponentModule + rename modal (ping/relay toggles, rename)"
```

---

## Task 12: `AlarmsHostedService` + `AddAlarms` DI + Host wiring + full verification

**Files:**

- Create: `src/RustPlusBot.Features.Alarms/Hosting/AlarmsHostedService.cs`
- Create: `src/RustPlusBot.Features.Alarms/AlarmServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Host/Program.cs`
- Test: `tests/RustPlusBot.Features.Alarms.Tests/AlarmRegistrationTests.cs`

**Interfaces:**

- Consumes: everything above.
- Produces: `AddAlarms(this IServiceCollection) -> IServiceCollection`; `AlarmsHostedService` (3 bus loops: `AlarmPairedEvent`→coordinator, `AlarmTriggeredEvent`→relay, `ConnectionStatusChangedEvent`→relay).

- [ ] **Step 1: Write the failing registration test** (copy `SwitchRegistrationTests`). Build a `ServiceCollection`, register the dependencies the feature needs as substitutes (the switch registration test shows which framework services to stub — `DiscordSocketClient`, `IClock`, stores, `IWorkspaceStore`, `ITeamChatSender`, `IConnectionStore`, an `IEventBus`), call `AddAlarms()`, `BuildServiceProvider(validateScopes: true)`, and assert each registered service resolves: `ILocalizer` (the alarm-catalog-bound shared localizer), `AlarmEmbedRenderer`, `IChannelEmbedPoster`, `IAlarmRefresher`, `AlarmPairingCoordinator`, `AlarmFireRelay`, and the `IHostedService` is present.

> **Note on `ILocalizer` registration:** `ILocalizer` is a shared type that multiple features may bind to their own catalog. Register the **alarm** binding as a keyed or feature-local instance to avoid collisions with other slices that adopt the shared localizer later. For 4b, register a concrete `Localizer` instance built from `AlarmLocalizationCatalog.Default` and inject it into `AlarmEmbedRenderer` directly (constructor-resolved). Simplest: `services.AddSingleton(new Localizer(AlarmLocalizationCatalog.Default))` and have `AlarmEmbedRenderer` depend on the concrete `Localizer` rather than `ILocalizer` — OR keep `ILocalizer` and register `services.AddSingleton<ILocalizer>(new Localizer(AlarmLocalizationCatalog.Default))` since no other slice binds `ILocalizer` in 4b (switches keep their own `SwitchLocalizer`). **Use the `ILocalizer` form** (it's the seam); the collision risk is theoretical until another slice migrates, and that migration will introduce keyed registration then. This catches captive-dependency + missing-registration regressions.

- [ ] **Step 2: Run to verify it fails** — FAIL (`AddAlarms` missing).

- [ ] **Step 3: Create `AlarmsHostedService`** — copy `SwitchesHostedService` exactly, three loops:
  - `ConsumePairedAsync`: `SubscribeAsync<AlarmPairedEvent>` → `coordinator.HandlePairedAsync`.
  - `ConsumeTriggeredAsync`: `SubscribeAsync<AlarmTriggeredEvent>` → `relay.HandleTriggeredAsync`.
  - `ConsumeStatusAsync`: `SubscribeAsync<ConnectionStatusChangedEvent>` → `relay.HandleConnectionStatusAsync`.
  Keep the per-loop broad-catch + `[LoggerMessage]` faulted logs.

- [ ] **Step 4: Create `AlarmServiceCollectionExtensions.AddAlarms`** (copy `AddSwitches`):

```csharp
public static IServiceCollection AddAlarms(this IServiceCollection services)
{
    ArgumentNullException.ThrowIfNull(services);

    services.AddSingleton<ILocalizer>(new Localizer(AlarmLocalizationCatalog.Default));
    services.AddSingleton<AlarmEmbedRenderer>();
    services.AddSingleton<IChannelEmbedPoster, DiscordChannelEmbedPoster>();
    services.AddSingleton<IAlarmRefresher, AlarmRefresher>();
    services.AddSingleton<AlarmPairingCoordinator>();
    services.AddSingleton<AlarmFireRelay>();
    services.AddHostedService<AlarmsHostedService>();

    services.AddSingleton(new InteractionModuleAssembly(typeof(AlarmServiceCollectionExtensions).Assembly));

    return services;
}
```

> **`IChannelEmbedPoster` registration:** the shared poster lives in `RustPlusBot.Discord`. Registering `IChannelEmbedPoster → DiscordChannelEmbedPoster` inside `AddAlarms` is fine for 4b (alarms is its only consumer). If a future slice also consumes it, move the registration to the Discord layer's `DiscordServiceCollectionExtensions` and drop it from `AddAlarms` (a duplicate `AddSingleton` of the same pair is harmless but untidy). For 4b, keep it in `AddAlarms` as shown.
>
> `IAlarmStore` is a scoped EF store — confirm where `ISwitchStore` is registered (likely in Persistence's `AddPersistence`); register `IAlarmStore` the same way (`services.AddScoped<IAlarmStore, AlarmStore>()` in the same place `ISwitchStore` is added). Add that line in this step.

- [ ] **Step 5: Wire into the Host** — in `Program.cs`, after `builder.Services.AddSwitches();`:

```csharp
builder.Services.AddAlarms();
```

- [ ] **Step 6: Run the registration test to verify it passes** — `dotnet test tests/RustPlusBot.Features.Alarms.Tests/... -maxcpucount:1` → PASS.

- [ ] **Step 7: Full-suite gate** — run everything and read per-assembly counts:

```bash
dotnet test RustPlusBot.slnx -maxcpucount:1
```

Expected: all assemblies green; counts ≥ prior run + the new tests; **no assembly dropped** (a 0 or missing assembly = a fake didn't implement a new member). If any dropped, fix the offending test double before proceeding.

- [ ] **Step 8: Strict build + format gate** —

```bash
dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1
dotnet tool restore
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
```

Then `git status` — jb will likely reorder members in the new files. Review the diff (expect only 4b files touched, no pre-existing drift), and if it changed anything, `git add` + amend or a follow-up commit.

- [ ] **Step 9: Verify no EF drift** —

```bash
git diff --stat develop -- src/RustPlusBot.Persistence/Migrations
```

Only the `SmartAlarms` migration + snapshot should differ from `develop`.

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusBot.Features.Alarms/Hosting/AlarmsHostedService.cs \
        src/RustPlusBot.Features.Alarms/AlarmServiceCollectionExtensions.cs \
        src/RustPlusBot.Persistence \
        src/RustPlusBot.Host/Program.cs \
        tests/RustPlusBot.Features.Alarms.Tests/AlarmRegistrationTests.cs
git commit -m "feat(alarms): add AlarmsHostedService, AddAlarms DI, and Host wiring"
```

---

## Final verification (whole feature)

- [ ] Full suite green, per-assembly counts checked: `dotnet test RustPlusBot.slnx -maxcpucount:1`
- [ ] Strict build 0/0: `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1`
- [ ] jb-clean: `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` leaves no diff
- [ ] No EF drift beyond `SmartAlarms`
- [ ] `docs/superpowers/` not staged (`git status` shows no spec/plan)
- [ ] VERIFY-LIVE note recorded: whether `OnAlarmTriggered`'s `Body.EntityId` is populated (update the foundation-status memory with the observed answer; if absent, the contingency — server-only attribution — is the follow-up)
- [ ] Open PR `feat/smart-alarms` → `develop` (only when the user asks)

```
