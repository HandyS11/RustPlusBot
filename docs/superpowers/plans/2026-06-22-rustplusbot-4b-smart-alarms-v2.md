# Subsystem 4b — Smart Alarms (v2, socket-trigger model) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship Smart Alarms end-to-end on the socket-trigger model — pair → validate → per-alarm `#alarms` embed; on the in-game `SmartDeviceTriggeredEvent` (primed on connect, like switches), update the embed and, per toggles, @everyone-ping and/or relay a "🚨 {name} triggered" line to team chat.

**Architecture:** Builds on the merged smart-device refactor (PR #22): alarms consume the generic `SmartDeviceTriggeredEvent` filtered by `IAlarmStore.ExistsAsync`, and are primed on connect by extending `ConnectionSupervisor.PrimeDevicesAsync` to also list alarms. A new `RustPlusBot.Features.Alarms` project mirrors `Features.Switches`; pairing routes by `PairedEntityKind` (pairing only — no FCM trigger). Shared `ILocalizer` + `IChannelEmbedPoster` (in `RustPlusBot.Discord`, from the refactor) are consumed, not copied.

**Tech Stack:** .NET 10, C#, EF Core+SQLite, Discord.Net 3.20, RustPlusApi/Fcm 2.0.0-beta.3, NSubstitute + xUnit, Roslynator + ReSharper (jb).

## Branch state (already done — do NOT redo)

`feat/smart-alarms` is rebased onto `develop` (post-PR-#22) and has **3 committed keeper commits**:

- `SmartAlarm` entity + EF config + `SmartAlarms` migration (Task 1 revises this).
- `IAlarmStore` + `AlarmStore` (Task 1 revises this).
- `IAlarmStore` DI registration (`PersistenceServiceCollectionExtensions`) — keep as-is.

The obsolete FCM-trigger commits (`AlarmTriggeredEvent`, `PairingKind.AlarmTriggered` routing, `OnAlarmTriggered`) were **dropped in the rebase** — do NOT re-add them.

## Global Constraints

- **Branch:** `feat/smart-alarms` (rebased onto develop@9bf6c36).
- **Packages:** RustPlusApi/Fcm at `2.0.0-beta.3` (already on develop).
- **Solution `RustPlusBot.slnx`**; build gate `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` = 0/0.
- **Test gate** `-maxcpucount:1`; read per-assembly counts (a fake missing a new member silently drops an assembly).
- **Format gate** `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` before push.
- **EF:** exactly ONE `SmartAlarms` migration on the branch — Task 1 REGENERATES it (delete + recreate), not a second migration.
- Entities in **Domain**; stores `public sealed` in **Persistence**; events in **Abstractions** (no Discord/FCM).
- Alarms consume the SHARED `ILocalizer` + `IChannelEmbedPoster` from `RustPlusBot.Discord` (do NOT make per-slice copies).
- `global::Discord.*` in Alarms files touching Discord.Net; `DynamicProxyGenAssembly2` InternalsVisibleTo for NSubstitute on internals; SQLite can't ORDER BY DateTimeOffset (client-side order); RCS1141 complete XML docs; RCS1217 no adjacent-interpolation custom-ids; EF migrations `--startup-project Persistence`.
- `docs/superpowers/` gitignored — never `git add`.

## Verified refactor surface this plan consumes (on develop)

- `RustPlusBot.Abstractions.Events.SmartDeviceTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive)`.
- `ConnectionSupervisor.PrimeDevicesAsync` (lists switches via `ISwitchStore`, primes each via `PublishDevicePrimeAsync` → `GetSmartDeviceInfoAsync` → publishes `SmartDeviceTriggeredEvent`). Task 3 extends it for alarms.
- `RustPlusBot.Discord.Localization.{ILocalizer, Localizer}` (ctor takes the `culture→key→value` dict) and `RustPlusBot.Discord.Posting.{IChannelEmbedPoster, DiscordChannelEmbedPoster}` (`EnsureAsync` + `SendEveryonePingAsync`).
- `IRustServerConnection.GetSmartDeviceInfoAsync`; the switch slice's `SwitchStateRelay` consumes `SmartDeviceTriggeredEvent` filtered by `ISwitchStore.ExistsAsync` — mirror this for alarms.

---

## File Structure

**Domain/Persistence (revise committed):** `Domain/Alarms/SmartAlarm.cs`, `Persistence/Alarms/IAlarmStore.cs`+`AlarmStore.cs`, `Persistence/Configurations/SmartAlarmConfiguration.cs` (unchanged — no Title/Message config), regenerate `Migrations/*_SmartAlarms.*`, `tests/.../Alarms/{SmartAlarmSchemaTests,AlarmStoreTests}.cs`.

**Abstractions:** `Events/AlarmPairedEvent.cs` (+test).

**Pairing (kind-routing only):** `Listening/PairingNotification.cs` (add `EntityKind` only), `Pairing/PairingHandler.cs` (2-arm kind switch), `Listening/RustPlusFcmPairingSource.cs` (subscribe `OnSmartAlarmPairing` only), `tests/.../PairingHandlerTests.cs`.

**Connections (one additive edit):** `Supervisor/ConnectionSupervisor.cs` (`PrimeDevicesAsync` also primes alarms), the connection test fakes if they gate priming.

**Workspace:** `WorkspaceKeys.cs`, `Specs/ServerWorkspaceSpecProvider.cs`, `Localization/LocalizationCatalog.cs`, `Locating/{IAlarmChannelLocator,AlarmChannelLocator}.cs` (+test).

**Features.Alarms (new):** csproj; `Rendering/{AlarmComponentIds,AlarmLocalizationCatalog,AlarmEmbedRenderer}.cs`; `Pairing/AlarmPairingCoordinator.cs`; `Relaying/{IAlarmRefresher,AlarmRefresher,AlarmStateRelay}.cs`; `Modules/{AlarmComponentModule,AlarmRenameModal}.cs`; `Hosting/AlarmsHostedService.cs`; `AlarmServiceCollectionExtensions.cs`; test project.

**Host:** `Program.cs` (`AddAlarms`).

---

## Task 1: Revise `SmartAlarm` entity + `IAlarmStore` + regenerate the migration

**Files:**

- Modify: `src/RustPlusBot.Domain/Alarms/SmartAlarm.cs`
- Modify: `src/RustPlusBot.Persistence/Alarms/IAlarmStore.cs`, `AlarmStore.cs`
- Modify: `tests/RustPlusBot.Persistence.Tests/Alarms/AlarmStoreTests.cs` (and `SmartAlarmSchemaTests.cs` if it references dropped fields)
- Regenerate: `src/RustPlusBot.Persistence/Migrations/*_SmartAlarms.*` + snapshot

**Interfaces:**

- Produces: `SmartAlarm` with `bool LastIsActive`, `DateTimeOffset? LastTriggeredUtc` (NO `LastTitle`/`LastMessage`/`LastFiredUtc`). `IAlarmStore.UpdateStateAsync(ulong guildId, Guid serverId, ulong entityId, bool isActive, DateTimeOffset? triggeredUtc, CancellationToken ct = default)` replacing `RecordFiredAsync`.

- [ ] **Step 1: Update the failing store test first** — in `AlarmStoreTests.cs`, replace the `RecordFiredAsync` test with an `UpdateStateAsync` test:

```csharp
[Fact]
public async Task UpdateStateAsync_active_sets_state_and_triggered_time()
{
    var (context, connection) = TestDb.Create();
    await using var _ = context; await using var __ = connection;
    var server = SeedServer(context);
    var store = new AlarmStore(context, FixedClock(DateTimeOffset.UnixEpoch));
    await store.AddAsync(10UL, server.Id, 42UL, "Alarm 42", 1UL);

    var t = DateTimeOffset.Parse("2026-06-22T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    await store.UpdateStateAsync(10UL, server.Id, 42UL, isActive: true, triggeredUtc: t);

    var a = await store.GetAsync(10UL, server.Id, 42UL);
    Assert.True(a!.LastIsActive);
    Assert.Equal(t, a.LastTriggeredUtc);
}

[Fact]
public async Task UpdateStateAsync_inactive_keeps_triggered_time()
{
    var (context, connection) = TestDb.Create();
    await using var _ = context; await using var __ = connection;
    var server = SeedServer(context);
    var store = new AlarmStore(context, FixedClock(DateTimeOffset.UnixEpoch));
    await store.AddAsync(10UL, server.Id, 42UL, "Alarm 42", 1UL);
    var t = DateTimeOffset.Parse("2026-06-22T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    await store.UpdateStateAsync(10UL, server.Id, 42UL, isActive: true, triggeredUtc: t);

    await store.UpdateStateAsync(10UL, server.Id, 42UL, isActive: false, triggeredUtc: null);

    var a = await store.GetAsync(10UL, server.Id, 42UL);
    Assert.False(a!.LastIsActive);
    Assert.Equal(t, a.LastTriggeredUtc); // unchanged — only the active edge stamps it
}
```

Copy `SeedServer`/`FixedClock` from the existing test file. Remove the old `RecordFiredAsync` test and any assertion on `LastTitle`/`LastMessage`.

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1` → FAIL (compile: `UpdateStateAsync`/`LastIsActive`/`LastTriggeredUtc` missing).

- [ ] **Step 3: Revise `SmartAlarm.cs`** — replace the three trailing properties + fix the class doc:

```csharp
/// <summary>A paired Smart Alarm the bot manages, surviving restarts. Guild- and server-scoped. Driven by the live socket (primed on connect, reacts to SmartDeviceTriggered) — the entity id is the switch-vs-alarm discriminant.</summary>
```

Replace lines for `LastTitle`/`LastMessage`/`LastFiredUtc` with:

```csharp
    /// <summary>The last observed on/off state from the in-game socket broadcast.</summary>
    public bool LastIsActive { get; set; }

    /// <summary>When the alarm most recently went active (UTC), or null if never triggered.</summary>
    public DateTimeOffset? LastTriggeredUtc { get; set; }
```

Also fix the `PingEveryone`/`RelayToTeamChat` doc lines: "a fire" → "a trigger going active".

- [ ] **Step 4: Revise `IAlarmStore.cs`** — replace the `RecordFiredAsync` member with:

```csharp
    /// <summary>Updates the alarm's on/off state; stamps the last-triggered time only when going active (no-op if absent).</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The Rust server id.</param>
    /// <param name="entityId">The in-game smart-alarm entity id.</param>
    /// <param name="isActive">The new on/off state.</param>
    /// <param name="triggeredUtc">When it went active (UTC); pass non-null only on the active edge — when null, the existing last-triggered time is kept.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when the state has been persisted.</returns>
    Task UpdateStateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool isActive,
        DateTimeOffset? triggeredUtc,
        CancellationToken ct = default);
```

- [ ] **Step 5: Revise `AlarmStore.cs`** — remove the `RecordFiredAsync` implementation; add (via the existing `MutateAsync` helper):

```csharp
    /// <inheritdoc />
    public Task UpdateStateAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        bool isActive,
        DateTimeOffset? triggeredUtc,
        CancellationToken ct = default) =>
        MutateAsync(guildId, serverId, entityId, a =>
        {
            a.LastIsActive = isActive;
            if (triggeredUtc is { } t)
            {
                a.LastTriggeredUtc = t;
            }
        }, ct);
```

Also remove any `LastTitle`/`LastMessage` seeding from `AddAsync` (the committed `AddAsync` sets only Name/PairedByUserId/CreatedUtc — confirm it does not touch the dropped fields; if it does, delete those lines).

- [ ] **Step 6: Run the store tests** — same command → PASS.

- [ ] **Step 7: Regenerate the single `SmartAlarms` migration.** Delete the existing migration files, then recreate:

```bash
rm src/RustPlusBot.Persistence/Migrations/*_SmartAlarms.cs src/RustPlusBot.Persistence/Migrations/*_SmartAlarms.Designer.cs
dotnet ef migrations remove --project src/RustPlusBot.Persistence/RustPlusBot.Persistence.csproj --startup-project src/RustPlusBot.Persistence/RustPlusBot.Persistence.csproj 2>/dev/null || true
dotnet ef migrations add SmartAlarms --project src/RustPlusBot.Persistence/RustPlusBot.Persistence.csproj --startup-project src/RustPlusBot.Persistence/RustPlusBot.Persistence.csproj
```

> The `rm` + `migrations remove` belt-and-suspenders handles the snapshot: `migrations remove` rolls the model snapshot back; then `add` regenerates a clean `SmartAlarms` with the new columns (`LastIsActive`, `LastTriggeredUtc`; no `LastTitle`/`LastMessage`/`LastFiredUtc`). Inspect the generated `Up()`: it must `CreateTable SmartAlarms` with exactly the revised columns + the unique index + cascade FK. If `migrations remove` errors because the snapshot already lacks it, the `rm` already cleared the files — just run `add`.

- [ ] **Step 8: Verify the migration + no extra drift** — open the new `*_SmartAlarms.cs`; confirm columns are `Id, GuildId, ServerId, EntityId, Name, MessageId, PairedByUserId, CreatedUtc, PingEveryone, RelayToTeamChat, LastIsActive, LastTriggeredUtc` (no Title/Message/FiredUtc). `git status src/RustPlusBot.Persistence/Migrations` shows only the SmartAlarms files + snapshot changed.

- [ ] **Step 9: Full Persistence build + tests** — `dotnet build src/RustPlusBot.Persistence/RustPlusBot.Persistence.csproj -warnaserror -maxcpucount:1` (0/0); `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj -maxcpucount:1` (PASS).

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusBot.Domain/Alarms/SmartAlarm.cs src/RustPlusBot.Persistence/Alarms/ src/RustPlusBot.Persistence/Migrations/ tests/RustPlusBot.Persistence.Tests/Alarms/
git commit -m "refactor(alarms): revise SmartAlarm to socket-state model (LastIsActive/LastTriggeredUtc; UpdateStateAsync)"
```

---

## Task 2: `AlarmPairedEvent` (Abstractions)

**Files:**

- Create: `src/RustPlusBot.Abstractions/Events/AlarmPairedEvent.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/AlarmPairedEventTests.cs`

**Interfaces:**

- Produces: `public sealed record AlarmPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId)`.

- [ ] **Step 1: Failing test** (mirror `SmartDeviceTriggeredEventTests`):

```csharp
using RustPlusBot.Abstractions.Events;
namespace RustPlusBot.Abstractions.Tests;
public sealed class AlarmPairedEventTests
{
    [Fact] public void CarriesIdentity()
    {
        var e = new AlarmPairedEvent(10UL, Guid.Empty, 42UL);
        Assert.Equal(10UL, e.GuildId);
        Assert.Equal(42UL, e.EntityId);
    }
}
```

- [ ] **Step 2: Run → FAIL** — `dotnet test tests/RustPlusBot.Abstractions.Tests/RustPlusBot.Abstractions.Tests.csproj -maxcpucount:1`.

- [ ] **Step 3: Create the record** (XML-doc the record + all 3 params, mirror `SmartDeviceTriggeredEvent`):

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>A Smart Alarm was paired in-game and needs validation before the bot manages it.</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game smart-alarm entity id.</param>
public sealed record AlarmPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId);
```

- [ ] **Step 4: Run → PASS.**

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Abstractions/Events/AlarmPairedEvent.cs tests/RustPlusBot.Abstractions.Tests/AlarmPairedEventTests.cs
git commit -m "feat(alarms): add AlarmPairedEvent"
```

---

## Task 3: Pairing kind-routing (pairing only) + alarm priming in the supervisor

Two related Connections/Pairing edits: route alarm *pairings* to `AlarmPairedEvent`, and prime alarms on connect. NO trigger routing (that's the obsolete FCM model).

**Files:**

- Modify: `src/RustPlusBot.Features.Pairing/Listening/PairingNotification.cs` (add `EntityKind` only)
- Modify: `src/RustPlusBot.Features.Pairing/Pairing/PairingHandler.cs`
- Modify: `src/RustPlusBot.Features.Pairing/Listening/RustPlusFcmPairingSource.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (`PrimeDevicesAsync`)
- Test: `tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/` (a priming test — alarms primed alongside switches)

**Interfaces:**

- Consumes: `AlarmPairedEvent` (Task 2); `PairedEntityKind` (Domain, exists); `IAlarmStore.ListByServerAsync` (Task 1).
- Produces: `PairingNotification` gains `PairedEntityKind EntityKind = PairedEntityKind.SmartSwitch`; `PairingHandler.HandleEntityAsync` 2-arm kind switch; `RustPlusFcmPairingSource` subscribes `OnSmartAlarmPairing`; `PrimeDevicesAsync` primes alarms too.

- [ ] **Step 1: Failing PairingHandler tests** — add to `PairingHandlerTests.cs` (`using RustPlusBot.Domain.Entities;`):

```csharp
private static PairingNotification AlarmPairing(Guid fpServer, ulong entityId = 55UL) =>
    new(PairingKind.Entity, string.Empty, string.Empty, 0, 1UL, "t",
        FacepunchServerId: fpServer, EntityId: entityId, EntityKind: PairedEntityKind.SmartAlarm);

[Fact]
public async Task EntityPairing_Alarm_PublishesAlarmPairedEvent_NotSwitch()
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
        Arg.Is<AlarmPairedEvent>(e => e.ServerId == server.Id && e.EntityId == 55UL), Arg.Any<CancellationToken>());
    await bus.DidNotReceive().PublishAsync(Arg.Any<SwitchPairedEvent>(), Arg.Any<CancellationToken>());
}
```

Keep the existing `EntityPairing_KnownServer_PublishesSwitchPairedEvent` test (it uses the default `EntityKind=SmartSwitch`) — confirm it still passes (switch path unregressed).

- [ ] **Step 2: Run → FAIL** — `dotnet test tests/RustPlusBot.Features.Pairing.Tests/RustPlusBot.Features.Pairing.Tests.csproj -maxcpucount:1` (`EntityKind`/`AlarmPairedEvent` missing).

- [ ] **Step 3: Add `EntityKind` to `PairingNotification`** — append ONE optional param (no Title/Message — those were the dropped trigger fields):

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
    RustPlusBot.Domain.Entities.PairedEntityKind EntityKind = RustPlusBot.Domain.Entities.PairedEntityKind.SmartSwitch);
```

Add the `<param name="EntityKind">` doc. The `PairingKind` enum stays `Server/Entity` (NO `AlarmTriggered`).

- [ ] **Step 4: `PairingHandler.HandleEntityAsync` — 2-arm kind switch** (replace the hardcoded `SwitchPairedEvent` publish):

```csharp
    switch (notification.EntityKind)
    {
        case RustPlusBot.Domain.Entities.PairedEntityKind.SmartSwitch:
            await eventBus.PublishAsync(new SwitchPairedEvent(guildId, server.Id, notification.EntityId), cancellationToken).ConfigureAwait(false);
            break;
        case RustPlusBot.Domain.Entities.PairedEntityKind.SmartAlarm:
            await eventBus.PublishAsync(new AlarmPairedEvent(guildId, server.Id, notification.EntityId), cancellationToken).ConfigureAwait(false);
            break;
        default:
            LogUnroutedEntityKind(logger, notification.EntityKind);
            break;
    }
```

Add `[LoggerMessage(Level = LogLevel.Debug, Message = "Dropping entity pairing of unrouted kind {Kind}.")] private static partial void LogUnroutedEntityKind(ILogger logger, RustPlusBot.Domain.Entities.PairedEntityKind kind);`. NO `HandleAlarmTriggerAsync`, NO `AlarmTriggered` branch in `HandleAsync`.

- [ ] **Step 5: `RustPlusFcmPairingSource` — subscribe `OnSmartAlarmPairing` only** (mirror `OnSmartSwitchPairing`; ctor + DisposeAsync; NO `OnAlarmTriggered`):

```csharp
_fcm.OnSmartAlarmPairing += OnSmartAlarmPairing;   // ctor
_fcm.OnSmartAlarmPairing -= OnSmartAlarmPairing;   // DisposeAsync
```

```csharp
private void OnSmartAlarmPairing(object? sender, Notification<ulong?> e)
{
    if (e?.Data is not { } entityId)
    {
        return;
    }

    Dispatch(new PairingNotification(
        Kind: PairingKind.Entity,
        ServerName: string.Empty, Ip: string.Empty, Port: 0,
        PlayerId: e.PlayerId,
        PlayerToken: e.PlayerToken.ToString(System.Globalization.CultureInfo.InvariantCulture),
        FacepunchServerId: e.ServerId,
        EntityId: entityId,
        EntityKind: RustPlusBot.Domain.Entities.PairedEntityKind.SmartAlarm));
}
```

- [ ] **Step 6: Run the Pairing tests → PASS** (existing switch + server tests green; the new alarm test green).

- [ ] **Step 7: Failing priming test (Connections)** — find the existing switch-priming test (it asserts a switch entity → `SmartDeviceTriggeredEvent` on connect; look in `SwitchQueryTests.cs` / the supervisor tests). Add an alarm analogue: seed a managed alarm via `IAlarmStore`, connect, assert a `SmartDeviceTriggeredEvent` is published for the alarm's entity id. The connection harness/fake must let `GetSmartDeviceInfoAsync` return a value for that id (the fake's `SwitchStates`-style dict). Mirror the switch priming test exactly.

> If the connection test harness seeds switches through a scoped `ISwitchStore`, seed alarms the same way through `IAlarmStore` (register it in the test provider — it's already DI-registered in Persistence). Confirm the harness's DI includes `IAlarmStore`; if not, add it.

- [ ] **Step 8: Run → FAIL** (supervisor doesn't prime alarms yet).

- [ ] **Step 9: Extend `PrimeDevicesAsync`** — after the existing switch list+prime loop, add an alarm list+prime loop using a scoped `IAlarmStore`:

```csharp
        IReadOnlyList<Domain.Alarms.SmartAlarm> alarms;
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
                alarms = await store.ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a failed alarm-list read just skips alarm priming for this connection.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeviceListFailed(logger, ex, key.Server);
            return;
        }

#pragma warning disable S3267 // Not a projection: each iteration awaits with per-alarm best-effort error handling.
        foreach (var alarm in alarms)
#pragma warning restore S3267
        {
            try
            {
                await PublishDevicePrimeAsync(key, connection, alarm.EntityId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Broad catch: a single alarm's prime failure is logged and skipped.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDevicePrimeFailed(logger, ex, alarm.EntityId, key.Server);
            }
        }
```

Add `using RustPlusBot.Persistence.Alarms;` (and `RustPlusBot.Domain.Alarms` if needed). Reuse the existing `LogDeviceListFailed`/`LogDevicePrimeFailed`/`PublishDevicePrimeAsync` from the refactor. `Features.Connections` already references Persistence — no csproj change.

> Consider extracting a small local helper to avoid duplicating the switch loop verbatim (DRY) — e.g. a `PrimeEntityIdsAsync(key, connection, IReadOnlyList<ulong> entityIds, ct)` that both the switch and alarm loops call. The reviewer will prefer the de-duplicated form. Keep the two store reads separate (different stores), but share the prime loop.

- [ ] **Step 10: Run the Connections tests → PASS** (read the count — must not drop). Full build `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` 0/0.

- [ ] **Step 11: Commit**

```bash
git add src/RustPlusBot.Features.Pairing/ src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs tests/RustPlusBot.Features.Pairing.Tests/ tests/RustPlusBot.Features.Connections.Tests/
git commit -m "feat(alarms): route alarm pairings to AlarmPairedEvent + prime alarms on connect"
```

---

## Task 4: Workspace — `#alarms` channel + i18n + locator

(Identical in shape to the switch `#switches` work; copy `SwitchChannelLocator`/`ISwitchChannelLocator`.)

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs` (`ServerAlarms = "alarms"`)
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs` (`#alarms` ChannelSpec, Interactive, next sort index)
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs` (`channel.alarms.name` = `alarms`/`alarmes`)
- Create: `src/RustPlusBot.Features.Workspace/Locating/IAlarmChannelLocator.cs`, `AlarmChannelLocator.cs`
- Modify: wherever `ISwitchChannelLocator` is registered — add `IAlarmChannelLocator`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Locating/AlarmChannelLocatorTests.cs`

- [ ] **Step 1: Failing locator test** — copy `SwitchChannelLocatorTests`, retype to `AlarmChannelLocator` + `WorkspaceChannelKeys.ServerAlarms`.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3:** add `ServerAlarms = "alarms"` in `WorkspaceKeys.cs`.
- [ ] **Step 4:** add the ChannelSpec `new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerAlarms, "channel.alarms.name", ChannelPermissionProfile.Interactive, <next index>)` in `ServerWorkspaceSpecProvider.GetChannelSpecs()`.
- [ ] **Step 5:** add `["channel.alarms.name"] = "alarms"` (en) / `"alarmes"` (fr) in `LocalizationCatalog.cs`.
- [ ] **Step 6:** create `IAlarmChannelLocator`/`AlarmChannelLocator` (copy switch verbatim, swap `ServerSwitches`→`ServerAlarms` + type names; keep `IDisposable`).
- [ ] **Step 7:** register `services.AddSingleton<IAlarmChannelLocator, AlarmChannelLocator>()` next to the switch locator.
- [ ] **Step 8:** Run tests → PASS; run the full Workspace suite (count must not drop — `FakeWorkspaceStore` needs no new member here).
- [ ] **Step 9: Commit** — `feat(alarms): provision #alarms channel + AlarmChannelLocator`.

---

## Task 5: `Features.Alarms` scaffold — project, component ids, catalog

(The shared `ILocalizer`/`IChannelEmbedPoster` already exist in `RustPlusBot.Discord` from the refactor — consume them; do NOT create per-slice copies.)

**Files:**

- Create: `src/RustPlusBot.Features.Alarms/RustPlusBot.Features.Alarms.csproj` (refs Abstractions, Persistence, Domain, Discord, Workspace, Connections [for `ITeamChatSender`]; InternalsVisibleTo Tests + DynamicProxyGenAssembly2)
- Create: `Rendering/AlarmComponentIds.cs`, `Rendering/AlarmLocalizationCatalog.cs`
- Create: test project `tests/RustPlusBot.Features.Alarms.Tests/`
- Test: `AlarmLocalizationCatalogTests.cs`

- [ ] **Step 1:** create + `dotnet sln RustPlusBot.slnx add` both the project and the test project (copy the switch csproj shapes; add the Connections ProjectReference).
- [ ] **Step 2: Failing catalog test** — assert EN/FR key parity for: `alarm.status.armed`, `alarm.status.active`, `alarm.status.unreachable`, `alarm.button.ping.on`, `alarm.button.ping.off`, `alarm.button.relay.on`, `alarm.button.relay.off`, `alarm.button.rename`, `alarm.embed.footer`, `alarm.embed.nevertriggered`, `alarm.embed.lasttriggered`, `alarm.prompt.title`, `alarm.prompt.body`, `alarm.prompt.accept`, `alarm.prompt.dismiss`, `alarm.rename.modal.title`, `alarm.rename.input.label`, `alarm.triggered.teamchat`.
- [ ] **Step 3:** Run → FAIL.
- [ ] **Step 4:** `AlarmComponentIds` (copy `SwitchComponentIds`, alarm tails: `alarm:accept:`/`dismiss:`/`ping:`/`relay:`/`rename:`/`rename:modal:`/`rename:input`).
- [ ] **Step 5:** `AlarmLocalizationCatalog` exposing `public static IReadOnlyDictionary<string, IReadOnlyDictionary<string,string>> Default` (the dict shape the shared `Localizer` ctor takes). EN:
  - `alarm.status.armed`="🔔 Armed", `alarm.status.active`="🚨 Active", `alarm.status.unreachable`="⚠️ Unreachable", `alarm.button.ping.on`="Ping @everyone: on", `...off`="Ping @everyone: off", `alarm.button.relay.on`="Relay to team chat: on", `...off`="Relay to team chat: off", `alarm.button.rename`="Rename", `alarm.embed.footer`="Entity {0}", `alarm.embed.nevertriggered`="Never triggered", `alarm.embed.lasttriggered`="Last triggered {0} ago", `alarm.prompt.title`="New alarm detected", `alarm.prompt.body`="Detected a new Smart Alarm ({0}). Add it?", `alarm.prompt.accept`="Accept", `alarm.prompt.dismiss`="Dismiss", `alarm.rename.modal.title`="Rename alarm", `alarm.rename.input.label`="Alarm name", `alarm.triggered.teamchat`="🚨 {0} triggered".
  - FR mirror (Armée/Active/Injoignable, "Ping @everyone : activé/désactivé", "Relais tchat équipe : activé/désactivé", "Renommer", "Entité {0}", "Jamais déclenchée", "Déclenchée il y a {0}", "Nouvelle alarme détectée", "Nouvelle alarme connectée détectée ({0}). L'ajouter ?", "Accepter", "Ignorer", "Renommer l'alarme", "Nom de l'alarme", "🚨 {0} déclenchée").
- [ ] **Step 6:** Run → PASS. Full build 0/0.
- [ ] **Step 7: Commit** — `feat(alarms): scaffold Features.Alarms (component ids + localization catalog)`.

---

## Task 6: `AlarmEmbedRenderer`

**Files:** Create `src/RustPlusBot.Features.Alarms/Rendering/AlarmEmbedRenderer.cs`; Test `tests/.../AlarmEmbedRendererTests.cs`.

**Interfaces:**

- Consumes: shared `RustPlusBot.Discord.Localization.ILocalizer`, `SmartAlarm`, `AlarmComponentIds`, `RustPlusBot.Abstractions.Time.IClock` (for the "N ago" relative format).

> **Relative-time formatting — do NOT reference `Features.Commands`.** `DurationFormat.Compact` lives in `RustPlusBot.Features.Commands`; referencing it from `Features.Alarms` would add an odd cross-feature dependency. Instead, add a tiny private static formatter inside `AlarmEmbedRenderer` (or a small `Rendering/RelativeTime.cs` helper in `Features.Alarms`) that renders a `TimeSpan` compactly (e.g. `"<1m"`, `"5m"`, `"2h 10m"`, `"3d"`). The exact format is not load-bearing — keep it simple and unit-test it via the renderer tests. (A future refactor may hoist a shared duration helper into a common project; out of scope here.)

- Produces: `internal sealed class AlarmEmbedRenderer(ILocalizer localizer, IClock clock)` with `(global::Discord.Embed, global::Discord.MessageComponent) RenderAlarm(SmartAlarm alarm, bool unreachable, string culture)` and `(…) RenderPrompt(Guid serverId, ulong entityId, string defaultName, string culture)`.

- [ ] **Step 1: Failing renderer tests** (copy `SwitchEmbedRendererTests` shape — component traversal `components.Components.OfType<ActionRowComponent>().SelectMany(r=>r.Components).OfType<ButtonComponent>()`). Cover: never-triggered (`LastTriggeredUtc` null) → description has "Never triggered"; triggered → has "Last triggered {N} ago"; `unreachable: true` → status unreachable + buttons disabled; ping on/off + relay on/off → correct button labels; active vs armed status from `LastIsActive`; EN/FR.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement** (`using Discord;`):

```csharp
public (Embed Embed, MessageComponent Components) RenderAlarm(SmartAlarm alarm, bool unreachable, string culture)
{
    ArgumentNullException.ThrowIfNull(alarm);
    var statusKey = unreachable ? "alarm.status.unreachable"
        : alarm.LastIsActive ? "alarm.status.active" : "alarm.status.armed";

    var triggered = alarm.LastTriggeredUtc is { } t
        ? localizer.Get("alarm.embed.lasttriggered", culture, DurationFormat.Compact(clock.UtcNow - t))
        : localizer.Get("alarm.embed.nevertriggered", culture);

    var embed = new EmbedBuilder()
        .WithTitle(alarm.Name)
        .WithDescription($"{localizer.Get(statusKey, culture)}\n{triggered}")
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

`RenderPrompt` is the switch copy with `alarm.prompt.*` + Accept/Dismiss ids. (Find `DurationFormat.Compact`'s real namespace via grep; it's used by `!alive`/events.)

- [ ] **Step 4: Run → PASS. Step 5: Commit** — `feat(alarms): add AlarmEmbedRenderer`.

---

## Task 7: `IAlarmRefresher`/`AlarmRefresher` + `AlarmPairingCoordinator`

**Files:** Create `Relaying/IAlarmRefresher.cs`+`AlarmRefresher.cs`, `Pairing/AlarmPairingCoordinator.cs`; Tests `AlarmRefresherTests.cs`, `AlarmPairingCoordinatorTests.cs`.

**Interfaces:**

- `IAlarmRefresher.RefreshAsync(ulong guildId, Guid serverId, ulong entityId, bool unreachable, CancellationToken)`; `AlarmRefresher(IServiceScopeFactory, IAlarmChannelLocator, IChannelEmbedPoster, AlarmEmbedRenderer) : IAlarmRefresher` — load alarm + culture + channel, render, `EnsureAsync`, persist changed `MessageId`; no-op if alarm/channel absent.
- `AlarmPairingCoordinator(IServiceScopeFactory, IAlarmChannelLocator, IChannelEmbedPoster, AlarmEmbedRenderer)` — `HandlePairedAsync(AlarmPairedEvent, ct)`, `Task<bool> TryAcceptAsync(guild, server, entity, acceptingUserId, ct)`, `bool TryDismiss(...)`.

- [ ] **Step 1–4:** copy the structures from the switch slice's `SwitchStateRelay.RenderAsync` (→ `AlarmRefresher`) and `SwitchPairingCoordinator` (→ `AlarmPairingCoordinator`), swapping `ISwitchStore`→`IAlarmStore`, switch locator/poster→`IAlarmChannelLocator`/shared `IChannelEmbedPoster`, `SwitchEmbedRenderer`→`AlarmEmbedRenderer`, default name `"Alarm "`, accept render `renderer.RenderAlarm(added, unreachable: false, culture)`. The coordinator gets culture via `IWorkspaceStore.GetCultureAsync`. Tests mirror `SwitchPairingCoordinatorTests` + a small `AlarmRefresher` test (alarm present+channel → render+EnsureAsync; absent → no-op). Use the shared `Localizer(AlarmLocalizationCatalog.Default)` real instance where a renderer is needed.
- [ ] **Step 5: Commit** — `feat(alarms): add AlarmRefresher + AlarmPairingCoordinator`.

---

## Task 8: `AlarmStateRelay`

**Files:** Create `Relaying/AlarmStateRelay.cs`; Test `AlarmStateRelayTests.cs`.

**Interfaces:**

- Consumes: `IServiceScopeFactory`, `IAlarmRefresher`, `IAlarmChannelLocator`, `IChannelEmbedPoster`, `IAlarmStore`, `IConnectionStore`, `RustPlusBot.Features.Connections.Listening.ITeamChatSender`, shared `ILocalizer` (for the team-chat line), `IClock`, `SmartDeviceTriggeredEvent`, `ConnectionStatusChangedEvent`, `RustPlusBot.Domain.Connections.ConnectionStatus`.
- Produces: `AlarmStateRelay.HandleTriggeredAsync(SmartDeviceTriggeredEvent, ct)` + `HandleConnectionStatusAsync(ConnectionStatusChangedEvent, ct)`.

- [ ] **Step 1: Failing tests.** Cover: triggered+matched alarm going **active** → `UpdateStateAsync(isActive:true, triggeredUtc:now)` + `IAlarmRefresher.RefreshAsync(unreachable:false)` + (ping when `PingEveryone`) `IChannelEmbedPoster.SendEveryonePingAsync` + (relay when `RelayToTeamChat`) `ITeamChatSender` send of the localized "🚨 {name} triggered"; triggered+matched going **inactive** → `UpdateStateAsync(isActive:false, triggeredUtc:null)` + refresh, NO ping/relay; triggered+**non-alarm id** (`ExistsAsync` false) → nothing; relay send throws → swallowed (refresh+ping still happen); connection not-Connected → each alarm refreshed `unreachable:true`, Connected → no-op. Use `Substitute.For<IAlarmRefresher>()`.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3: Implement:**

```csharp
public async Task HandleTriggeredAsync(SmartDeviceTriggeredEvent evt, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(evt);
    bool ping, relay; string name;
    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
        var alarm = await store.GetAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false);
        if (alarm is null)
        {
            return; // not an alarm this relay manages (e.g. a switch) — ignore
        }

        await store.UpdateStateAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.IsActive,
            evt.IsActive ? clock.UtcNow : null, cancellationToken).ConfigureAwait(false);
        ping = alarm.PingEveryone; relay = alarm.RelayToTeamChat; name = alarm.Name;
    }

    await refresher.RefreshAsync(evt.GuildId, evt.ServerId, evt.EntityId, unreachable: false, cancellationToken).ConfigureAwait(false);

    if (!evt.IsActive)
    {
        return; // only the active edge notifies
    }

    if (ping)
    {
        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
        if (channelId is { } channel)
        {
            await poster.SendEveryonePingAsync(channel, $"@everyone {name}", cancellationToken).ConfigureAwait(false);
        }
    }

    if (relay)
    {
        await RelayToTeamChatSafeAsync(evt, name, cancellationToken).ConfigureAwait(false);
    }
}
```

`RelayToTeamChatSafeAsync` builds the line via the shared localizer (`localizer.Get("alarm.triggered.teamchat", culture, name)` — get culture via `IWorkspaceStore.GetCultureAsync` in a scope) and calls `ITeamChatSender` (confirm its real method name/signature) inside a broad-catch that logs+swallows. `HandleConnectionStatusAsync` copies `SwitchStateRelay.HandleConnectionStatusAsync` but calls `refresher.RefreshAsync(unreachable:true)` per alarm (Connected → return). Inject `IClock`. Add `[LoggerMessage]` for a relay-failure warning.

- [ ] **Step 4: Run → PASS. Step 5: Build 0/0. Step 6: Commit** — `feat(alarms): add AlarmStateRelay (state update, active-edge ping/relay, unreachable)`.

---

## Task 9: `AlarmComponentModule` + `AlarmRenameModal` (thin, untested)

**Files:** Create `Modules/AlarmRenameModal.cs`, `Modules/AlarmComponentModule.cs`.

- [ ] **Step 1:** `AlarmRenameModal` (copy `SwitchRenameModal`, `alarm.rename.*` + `AlarmComponentIds.RenameInputId`).
- [ ] **Step 2:** `AlarmComponentModule(IServiceScopeFactory, IAlarmRefresher) : InteractionModuleBase<SocketInteractionContext>` — `Accept`/`Dismiss` (via `AlarmPairingCoordinator` from scope), `alarm-ping`/`alarm-relay` toggles (scope → `IAlarmStore.GetAsync` → flip → `SetPingEveryoneAsync`/`SetRelayToTeamChatAsync` → `refresher.RefreshAsync(unreachable:false)` → "Updated."), `alarm-rename` modal + submit (`RenameAsync` → refresh). Reuse the switch module's `TryParse`/`DeletePromptMessageSafeAsync`. No `[RequireUserPermission]`.
- [ ] **Step 3: Build 0/0. Step 4: Commit** — `feat(alarms): add AlarmComponentModule + rename modal`.

---

## Task 10: `AlarmsHostedService` + `AddAlarms` DI + Host wiring + full gate

**Files:** Create `Hosting/AlarmsHostedService.cs`, `AlarmServiceCollectionExtensions.cs`; Modify `src/RustPlusBot.Host/Program.cs`; Test `AlarmRegistrationTests.cs`.

- [ ] **Step 1: Failing registration test** (copy `SwitchRegistrationTests`): register the deps as substitutes (`DiscordSocketClient`, `IClock`, stores, `IWorkspaceStore`, `ITeamChatSender`, `IConnectionStore`, `IEventBus`, the shared `IChannelEmbedPoster`), `AddAlarms()`, `BuildServiceProvider(validateScopes:true)`, assert resolves: the alarm-catalog-bound `ILocalizer`, `AlarmEmbedRenderer`, `IAlarmRefresher`, `AlarmPairingCoordinator`, `AlarmStateRelay`, the `IHostedService`.
- [ ] **Step 2: Run → FAIL.**
- [ ] **Step 3:** `AlarmsHostedService` — bus loops: `AlarmPairedEvent`→coordinator; `SmartDeviceTriggeredEvent`→relay.HandleTriggeredAsync; `ConnectionStatusChangedEvent`→relay.HandleConnectionStatusAsync (mirror `SwitchesHostedService`'s loop structure incl. per-loop broad-catch).
- [ ] **Step 4:** `AddAlarms`:

```csharp
services.AddSingleton<ILocalizer>(new Localizer(AlarmLocalizationCatalog.Default));
services.AddSingleton<AlarmEmbedRenderer>();
services.AddSingleton<IChannelEmbedPoster, DiscordChannelEmbedPoster>(); // first/only registration — verified not registered elsewhere yet
services.AddSingleton<IAlarmRefresher, AlarmRefresher>();
services.AddSingleton<AlarmPairingCoordinator>();
services.AddSingleton<AlarmStateRelay>();
services.AddHostedService<AlarmsHostedService>();
services.AddSingleton(new InteractionModuleAssembly(typeof(AlarmServiceCollectionExtensions).Assembly));
```

> `IChannelEmbedPoster` is NOT yet registered anywhere (verified) — `AddAlarms` is its first registration. `ILocalizer` is registered here bound to the alarm catalog (the switch slice keeps its own `SwitchLocalizer` — no collision in 4b). `IAlarmStore` is already DI-registered (the kept commit). `IAlarmChannelLocator` is registered in Workspace (Task 4). `ITeamChatSender.SendAsync(ulong guildId, Guid serverId, string message, …)` returns `Task<TeamChatSendResult>` — confirmed; adapt the Task 8 relay call to it.

- [ ] **Step 5:** `Program.cs` — `builder.Services.AddAlarms();` after `AddSwitches();`.
- [ ] **Step 6: Run registration test → PASS.**
- [ ] **Step 7: FULL GATE** — `dotnet test RustPlusBot.slnx -maxcpucount:1` (all assemblies green, read per-assembly counts, none dropped — NEW Alarms.Tests present); `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` (0/0); `dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (commit any reformat); `git diff --stat develop -- src/RustPlusBot.Persistence/Migrations` shows only the one regenerated `SmartAlarms` migration.
- [ ] **Step 8: Commit** — `feat(alarms): add AlarmsHostedService, AddAlarms DI, and Host wiring`.

---

## Final verification (whole feature)

- [ ] Full suite green, per-assembly counts checked (no drop; Alarms.Tests present)
- [ ] Strict build 0/0; jb-clean
- [ ] EF: exactly ONE regenerated `SmartAlarms` migration (no second, no other drift)
- [ ] End-to-end: pair (FCM `OnSmartAlarmPairing` → `AlarmPairedEvent` → prompt → Accept → embed) → connect-prime (`PrimeDevicesAsync` alarm loop → `SmartDeviceTriggeredEvent` → embed state) → in-game trigger (`SmartDeviceTriggeredEvent` → `AlarmStateRelay`: active edge → embed + ping/relay) → toggles/rename → unreachable on disconnect
- [ ] No obsolete FCM-trigger code present (`AlarmTriggeredEvent`/`PairingKind.AlarmTriggered`/`OnAlarmTriggered` all absent)
- [ ] `docs/superpowers/` not staged
- [ ] Open PR `feat/smart-alarms` → `develop` (only when the user asks)

```
