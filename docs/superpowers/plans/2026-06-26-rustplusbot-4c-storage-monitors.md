# Subsystem 4c — Smart Storage Monitors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add in-game Smart Storage Monitors — pair-in-game → user-validated prompt → a per-monitor live contents embed (items + protection + slots) in a per-server `#storagemonitors` channel, with Refresh and Rename.

**Architecture:** Approach A — a storage-specific read seam and bus event sit beside the existing boolean device plumbing. The slice is a structural copy of `Features.Switches`, with the data shape widened from a bool to a `StorageContentsSnapshot`. FCM `OnStorageMonitorPairing` → `StorageMonitorPairedEvent`; the socket's `OnStorageMonitorTriggered` and connect-time `GetStorageMonitorInfoAsync` prime → `StorageMonitorTriggeredEvent`; a new `Features.StorageMonitors` project consumes both. Item ids are named via a bundled `items.json` lookup (names only; recycle/upkeep-cost calc deferred to subsystem 6).

**Tech Stack:** .NET 10, C# 13, Discord.Net 3.20, EF Core + SQLite, RustPlusApi / RustPlusApi.Fcm 2.0.0-beta.3, xUnit + NSubstitute, FluentAssertions (match the existing test style of the assembly you touch).

## Global Constraints

- Solution file is `RustPlusBot.slnx` (NOT `.sln`). Add new projects with `dotnet sln RustPlusBot.slnx add <path>`.
- Strict build: `-warnaserror`; XML docs required on all public members; Roslynator + .editorconfig analyzers are hard. Known nits: `RCS1141` (XML `<param>` on every parameter), `S1135` (`// TODO` is an error — use `<remarks>`), `CA1031` (annotate broad catches with a `#pragma`), `CA1307`/`CA1310` (pass `StringComparison`), `S4581` (use `Guid.Empty`, not `default`).
- Format gate: `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` must produce a zero diff. Run `dotnet tool restore` first. Run it before every push; it reorders members Roslynator never flags.
- `RustPlusBot.Discord` namespace shadows Discord.Net's top-level `Discord` namespace — write `global::Discord.Embed` / `global::Discord.MessageComponent` in files/tests where both are in scope (posters and any test referencing Discord.Net `Embed`).
- NSubstitute on an `internal` interface requires `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in that project's csproj. `Features.Connections` already has it; add it to the new project only if a test mocks an internal type there.
- Localization is the shared `RustPlusBot.Localization` project: inject `ILocalizer` and call `Get(key, culture)` / `Get(key, culture, args)`. Keys live in `Strings.resx` (en/neutral) and `Strings.fr.resx`. There is NO per-feature localizer. Keys must be byte-exact across both files (emoji/accents/`{0}` placeholders).
- Run the full test suite with `dotnet test RustPlusBot.slnx -maxcpucount:1` and READ the per-assembly counts — a low total means an assembly failed to build (usually a fake missing a new interface member), which silently drops its tests. Parallel runs can undercount and race on `.git/config`.
- `docs/superpowers/` is gitignored and LOCAL-ONLY. Never `git add` this plan or the spec.
- Branch: `feat/storage-monitors` off `develop`. Commit after every task. Do NOT switch branches or touch `develop` directly.
- Spec: `docs/superpowers/specs/2026-06-26-rustplusbot-4c-storage-monitors-design.md`.

---

## File Structure

**New project `src/RustPlusBot.Features.StorageMonitors/`:**

- `StorageMonitorServiceCollectionExtensions.cs` — `AddStorageMonitors()`.
- `Naming/IItemNameResolver.cs`, `Naming/EmbeddedItemNameResolver.cs`, `Naming/items.json` (embedded).
- `Rendering/StorageMonitorComponentIds.cs`, `Rendering/StorageMonitorEmbedRenderer.cs`.
- `Pairing/StorageMonitorPairingCoordinator.cs`.
- `Posting/IStorageMonitorChannelPoster.cs`, `Posting/DiscordStorageMonitorChannelPoster.cs`.
- `Relaying/StorageMonitorStateRelay.cs`.
- `Modules/StorageMonitorComponentModule.cs`, `Modules/StorageMonitorRenameModal.cs`.
- `Hosting/StorageMonitorsHostedService.cs`.

**New test project `tests/RustPlusBot.Features.StorageMonitors.Tests/`.**

**Modified:**

- `src/RustPlusBot.Abstractions/` — new DTOs + events.
- `src/RustPlusBot.Domain/StorageMonitors/SmartStorageMonitor.cs` (new).
- `src/RustPlusBot.Persistence/` — store, interface, EF config, DbSet, migration.
- `src/RustPlusBot.Features.Pairing/Listening/RustPlusFcmPairingSource.cs`, `.../Pairing/PairingHandler.cs`.
- `src/RustPlusBot.Features.Connections/` — `IRustServerConnection`, `RustPlusSocketSource`, `ConnectionSupervisor`, `IRustServerQuery`, the test fake.
- `src/RustPlusBot.Features.Workspace/` — `WorkspaceKeys.cs`, `Specs/ServerWorkspaceSpecProvider.cs`, new `Locating/StorageMonitorChannelLocator.cs` + `Locating/IStorageMonitorChannelLocator.cs`, DI registration.
- `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx`.
- `src/RustPlusBot.Host/Program.cs`.

---

## Task 1: Abstractions — storage DTOs and events

**Files:**

- Create: `src/RustPlusBot.Abstractions/Connections/StorageContentsSnapshot.cs`
- Create: `src/RustPlusBot.Abstractions/Connections/StorageItemSnapshot.cs`
- Create: `src/RustPlusBot.Abstractions/Events/StorageMonitorPairedEvent.cs`
- Create: `src/RustPlusBot.Abstractions/Events/StorageMonitorTriggeredEvent.cs`
- Test: none (records only; exercised by later tasks).

**Interfaces:**

- Produces: `StorageContentsSnapshot(int? Capacity, bool? HasProtection, DateTimeOffset? ProtectionExpiry, IReadOnlyList<StorageItemSnapshot> Items)` in namespace `RustPlusBot.Features.Connections.Listening`; `StorageItemSnapshot(int ItemId, int Quantity, bool IsBlueprint)` same namespace; `StorageMonitorPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId)` and `StorageMonitorTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId, StorageContentsSnapshot Contents)` in namespace `RustPlusBot.Abstractions.Events`.

> Namespace note: the other socket DTOs (`ServerInfoSnapshot`, `MapMarkerSnapshot`, etc.) live physically in `Abstractions` but keep the namespace `RustPlusBot.Features.Connections.Listening` so consumers' usings stay stable. Match that here. Confirm by opening `src/RustPlusBot.Abstractions/Connections/ServerInfoSnapshot.cs` and copying its `namespace` line.

- [ ] **Step 1: Create `StorageItemSnapshot.cs`**

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>One stack inside a storage monitor: the item id, its quantity, and whether it is a blueprint.</summary>
/// <param name="ItemId">The Rust item id (resolve to a display name via the item-name lookup).</param>
/// <param name="Quantity">The stack quantity.</param>
/// <param name="IsBlueprint">True when the stack is a blueprint rather than the item itself.</param>
public sealed record StorageItemSnapshot(int ItemId, int Quantity, bool IsBlueprint);
```

- [ ] **Step 2: Create `StorageContentsSnapshot.cs`**

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A point-in-time read of a storage monitor's contents and protection state.</summary>
/// <param name="Capacity">Total slot count (24 = Tool Cupboard, 48 = large box, 12 = small box), or null if unknown.</param>
/// <param name="HasProtection">For a Tool Cupboard: whether decay protection is active. Null for non-TC monitors.</param>
/// <param name="ProtectionExpiry">When decay protection expires (UTC). Only meaningful when <paramref name="HasProtection"/> is true.</param>
/// <param name="Items">The stacks currently inside the monitor.</param>
public sealed record StorageContentsSnapshot(
    int? Capacity,
    bool? HasProtection,
    DateTimeOffset? ProtectionExpiry,
    IReadOnlyList<StorageItemSnapshot> Items);
```

- [ ] **Step 3: Create `StorageMonitorPairedEvent.cs`**

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>A storage monitor was paired in-game via FCM; the feature offers an "Add it?" prompt.</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game storage-monitor entity id.</param>
public sealed record StorageMonitorPairedEvent(ulong GuildId, Guid ServerId, ulong EntityId);
```

- [ ] **Step 4: Create `StorageMonitorTriggeredEvent.cs`**

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Abstractions.Events;

/// <summary>A managed storage monitor's contents were (re)read — on connect-prime or on an in-game change.</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game entity id (the discriminant — the feature filters to ids it manages).</param>
/// <param name="Contents">The contents snapshot carried on the read/broadcast.</param>
public sealed record StorageMonitorTriggeredEvent(
    ulong GuildId,
    Guid ServerId,
    ulong EntityId,
    StorageContentsSnapshot Contents);
```

- [ ] **Step 5: Build the Abstractions project**

Run: `dotnet build src/RustPlusBot.Abstractions/RustPlusBot.Abstractions.csproj -warnaserror`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Abstractions
git commit -m "feat(abstractions): storage-monitor contents DTOs + paired/triggered events"
```

---

## Task 2: Pairing — route storage-monitor FCM pairings

**Files:**

- Modify: `src/RustPlusBot.Features.Pairing/Listening/RustPlusFcmPairingSource.cs` (subscribe `OnStorageMonitorPairing`)
- Modify: `src/RustPlusBot.Features.Pairing/Pairing/PairingHandler.cs:74-89` (add the `StorageMonitor` switch arm)
- Test: `tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs` (add a test; match the existing file's style)

**Interfaces:**

- Consumes: `StorageMonitorPairedEvent` (Task 1); existing `PairedEntityKind.StorageMonitor` (already in `RustPlusBot.Domain.Entities`); existing `PairingNotification` record with `EntityKind` and `EntityId`.
- Produces: `PairingHandler.HandleEntityAsync` publishes `StorageMonitorPairedEvent` for `PairedEntityKind.StorageMonitor`.

> First, open `tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs` and find the existing test that asserts a `SmartAlarm` pairing publishes `AlarmPairedEvent` (it sets up `IServerService.GetByFacepunchServerIdAsync` to return a server, calls `HandleAsync` with an `EntityKind`-bearing notification, and asserts `eventBus.PublishAsync` received an `AlarmPairedEvent`). Copy its shape exactly.

- [ ] **Step 1: Write the failing test**

Add to `PairingHandlerTests.cs` (adapt names to the file's existing helpers/fakes — `CreateHandler`, the server stub, etc.):

```csharp
[Fact]
public async Task HandleAsync_StorageMonitorEntity_PublishesStorageMonitorPairedEvent()
{
    var guildId = 123UL;
    var serverId = Guid.NewGuid();
    var entityId = 999UL;
    var facepunchId = Guid.NewGuid();
    Servers.GetByFacepunchServerIdAsync(guildId, facepunchId, Arg.Any<CancellationToken>())
        .Returns(new RustServer { Id = serverId, GuildId = guildId });
    var handler = CreateHandler();

    var notification = new PairingNotification(
        Kind: PairingKind.Entity,
        ServerName: string.Empty, Ip: string.Empty, Port: 0,
        PlayerId: 1UL, PlayerToken: "1",
        FacepunchServerId: facepunchId,
        EntityId: entityId,
        EntityKind: PairedEntityKind.StorageMonitor);

    await handler.HandleAsync(guildId, ownerUserId: 1UL, notification, CancellationToken.None);

    await EventBus.Received(1).PublishAsync(
        Arg.Is<StorageMonitorPairedEvent>(e =>
            e.GuildId == guildId && e.ServerId == serverId && e.EntityId == entityId),
        Arg.Any<CancellationToken>());
}
```

Add the needed usings: `using RustPlusBot.Abstractions.Events;`, `using RustPlusBot.Domain.Entities;`, `using RustPlusBot.Domain.Servers;`.

- [ ] **Step 2: Run the test — verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests -maxcpucount:1 --filter HandleAsync_StorageMonitorEntity_PublishesStorageMonitorPairedEvent`
Expected: FAIL — `StorageMonitorPairedEvent` is published 0 times (the `default` arm logs "unrouted kind" instead).

- [ ] **Step 3: Add the switch arm in `PairingHandler.HandleEntityAsync`**

In `PairingHandler.cs`, insert before the `default:` arm (around line 86):

```csharp
            case RustPlusBot.Domain.Entities.PairedEntityKind.StorageMonitor:
                await eventBus
                    .PublishAsync(new StorageMonitorPairedEvent(guildId, server.Id, notification.EntityId),
                        cancellationToken)
                    .ConfigureAwait(false);
                break;
```

- [ ] **Step 4: Subscribe `OnStorageMonitorPairing` in `RustPlusFcmPairingSource`**

In the `RustPlusFcmListener` constructor, after the `OnSmartAlarmPairing` subscription (line 72):

```csharp
            _fcm.OnStorageMonitorPairing += OnStorageMonitorPairing;
```

In `DisposeAsync`, after the alarm unsubscribe (line 105):

```csharp
            _fcm.OnStorageMonitorPairing -= OnStorageMonitorPairing;
```

Add the handler (mirror `OnSmartAlarmPairing` at line 155):

```csharp
        private void OnStorageMonitorPairing(object? sender, Notification<ulong?> e)
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
                EntityKind: RustPlusBot.Domain.Entities.PairedEntityKind.StorageMonitor));
        }
```

- [ ] **Step 5: Run the test — verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Pairing.Tests -maxcpucount:1 --filter HandleAsync_StorageMonitorEntity_PublishesStorageMonitorPairedEvent`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Pairing tests/RustPlusBot.Features.Pairing.Tests
git commit -m "feat(pairing): route storage-monitor FCM pairings to StorageMonitorPairedEvent"
```

---

## Task 3: Connections seam — `GetStorageMonitorInfoAsync` + `StorageMonitorTriggered` + the fake

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs` (add method + event)
- Create: `src/RustPlusBot.Features.Connections/Listening/StorageMonitorTrigger.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` (untested shim — implement both)
- Modify: the Connections test fake (find it: `grep -rl "class FakeRustSocketSource\|IRustServerConnection" tests/RustPlusBot.Features.Connections.Tests`)
- Test: `tests/RustPlusBot.Features.Connections.Tests/...` (a fake-level test that the scripted trigger + read round-trip)

**Interfaces:**

- Consumes: `StorageContentsSnapshot`, `StorageItemSnapshot` (Task 1).
- Produces: on `IRustServerConnection` — `Task<StorageContentsSnapshot?> GetStorageMonitorInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken cancellationToken)` and `event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered`; `internal sealed record StorageMonitorTrigger(ulong EntityId, StorageContentsSnapshot Contents)`. The fake gains an `EnqueueStorageInfo(ulong entityId, StorageContentsSnapshot)` script + a `RaiseStorageTrigger(StorageMonitorTrigger)` helper (match the existing switch-fake naming, e.g. `NextHeartbeat`/`EnqueueMarkers`).

> Open `RustPlusSocketSource.cs` and read `GetSmartDeviceInfoAsync` (line ~351) and the `SmartDeviceTriggered` event impl (lines ~349, 575) — mirror them exactly (timeout CTS, `.WaitAsync`, `Response.IsSuccess`/`.Data`, broad-catch returning null).

- [ ] **Step 1: Create `StorageMonitorTrigger.cs`**

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>An in-game storage-monitor broadcast: the entity id and its current contents.</summary>
/// <param name="EntityId">The in-game entity id.</param>
/// <param name="Contents">The contents snapshot carried on the broadcast.</param>
internal sealed record StorageMonitorTrigger(ulong EntityId, StorageContentsSnapshot Contents);
```

(The `using` is redundant within the same namespace; drop it — kept here only to flag the type's home.)

- [ ] **Step 2: Add the method + event to `IRustServerConnection`**

After `GetSmartDeviceInfoAsync` (line 57) add:

```csharp
    /// <summary>Reads a storage monitor's contents, or null on failure/timeout. Also primes the socket's interest so triggers fire for it thereafter.</summary>
    /// <param name="entityId">The in-game storage-monitor entity id.</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The contents snapshot, or null on failure/timeout.</returns>
    Task<StorageContentsSnapshot?> GetStorageMonitorInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken cancellationToken);
```

After the `SmartDeviceTriggered` event (line 113) add:

```csharp
    /// <summary>Raised when a managed storage monitor's contents change in-game; carries the entity id and the new contents.</summary>
    event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered;
```

- [ ] **Step 3: Implement in `RustPlusSocketSource` (untested shim)**

Subscribe in the connect path (where `_rustPlus.OnSmartDeviceTriggered += OnSmartDeviceTriggered;` is, line ~120):

```csharp
            _rustPlus.OnStorageMonitorTriggered += OnStorageMonitorTriggered;
```

Unsubscribe alongside the switch unsubscribe (line ~560):

```csharp
            _rustPlus.OnStorageMonitorTriggered -= OnStorageMonitorTriggered;
```

Add the event field + raiser (mirror `SmartDeviceTriggered` at line 349 and `OnSmartDeviceTriggered` at line 575):

```csharp
        public event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered;

        private void OnStorageMonitorTriggered(object? sender, RustPlusApi.Data.Events.StorageMonitorEventArg e) =>
            StorageMonitorTriggered?.Invoke(this, new StorageMonitorTrigger(e.Id, MapContents(e)));
```

Add the read method (mirror `GetSmartDeviceInfoAsync` at line 351, but call `GetStorageMonitorInfoAsync` and map the contents):

```csharp
        /// <inheritdoc />
        public async Task<StorageContentsSnapshot?> GetStorageMonitorInfoAsync(
            ulong entityId,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);
            try
            {
                // CONFIRMED (2.0.0-beta.3): GetStorageMonitorInfoAsync(ulong, CancellationToken) returns
                // Task<Response<StorageMonitorInfo?>>; the read also primes the entity so OnStorageMonitorTriggered
                // fires for it thereafter.
                var response = await _rustPlus.GetStorageMonitorInfoAsync(entityId, timeoutCts.Token)
                    .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                return response is { IsSuccess: true, Data: { } info } ? MapContents(info) : null;
            }
#pragma warning disable CA1031 // Broad catch: a failed/timed-out storage read returns null; the caller treats null as unreachable.
            catch (Exception)
#pragma warning restore CA1031
            {
                return null;
            }
        }
```

Add the pure mapper (place near the other private mappers in the shim). `StorageMonitorEventArg : StorageMonitorInfo`, so one mapper covers both:

```csharp
        private static StorageContentsSnapshot MapContents(RustPlusApi.Data.Entities.StorageMonitorInfo info)
        {
            var items = info.Items is null
                ? (IReadOnlyList<StorageItemSnapshot>)[]
                : [.. info.Items.Select(i =>
                    new StorageItemSnapshot(i.Id, i.Quantity ?? 0, i.IsItemBlueprint ?? false))];

            DateTimeOffset? expiry = info.HasProtection == true
                ? new DateTimeOffset(DateTime.SpecifyKind(info.ProtectionExpiry, DateTimeKind.Utc))
                : null;

            return new StorageContentsSnapshot(info.Capacity, info.HasProtection, expiry, items);
        }
```

Add `using System.Linq;` and `using RustPlusBot.Features.Connections.Listening;` if not already present.

- [ ] **Step 4: Extend the Connections test fake**

In the fake connection used by `Connections.Tests` (the one implementing `IRustServerConnection`), add:

- a field `private readonly ConcurrentQueue<(ulong EntityId, StorageContentsSnapshot Contents)> _storageInfo = new();` plus a "hold last" fallback, matching how the fake holds `NextHeartbeat`;
- `public void EnqueueStorageInfo(ulong entityId, StorageContentsSnapshot contents)` to script reads;
- implement `GetStorageMonitorInfoAsync` to dequeue/return the scripted contents (or the held last);
- `public event EventHandler<StorageMonitorTrigger>? StorageMonitorTriggered;` and `public void RaiseStorageTrigger(StorageMonitorTrigger t) => StorageMonitorTriggered?.Invoke(this, t);`.

> If `Connections.Tests` uses an NSubstitute mock rather than a hand-written fake for `IRustServerConnection`, configure `connection.GetStorageMonitorInfoAsync(...).Returns(...)` and `connection.StorageMonitorTriggered += Raise.Event...` in the test instead. Match the assembly's existing approach.

- [ ] **Step 5: Write a fake-level round-trip test**

```csharp
[Fact]
public void Fake_RaiseStorageTrigger_DeliversContents()
{
    var fake = CreateFakeConnection(); // match the assembly's factory
    StorageMonitorTrigger? received = null;
    fake.StorageMonitorTriggered += (_, t) => received = t;

    var contents = new StorageContentsSnapshot(24, true, DateTimeOffset.UnixEpoch,
        [new StorageItemSnapshot(-151838493, 500, false)]);
    fake.RaiseStorageTrigger(new StorageMonitorTrigger(777UL, contents));

    received.Should().NotBeNull();
    received!.EntityId.Should().Be(777UL);
    received.Contents.Items.Should().ContainSingle(i => i.ItemId == -151838493 && i.Quantity == 500);
}
```

- [ ] **Step 6: Build + run**

Run: `dotnet build src/RustPlusBot.Features.Connections -warnaserror` then `dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1`
Expected: build clean; all Connections tests pass (count not lower than before — the fake compiles).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(connections): storage-monitor read seam + trigger event + fake support"
```

---

## Task 4: Supervisor — subscribe, publish triggers, prime, and `IRustServerQuery.GetStorageContentsAsync`

**Files:**

- Modify: `src/RustPlusBot.Abstractions/Connections/IRustServerQuery.cs` (add the query method)
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (subscribe + publish + prime + query impl)
- Test: `tests/RustPlusBot.Features.Connections.Tests/...` (supervisor primes + relays)

**Interfaces:**

- Consumes: `IRustServerConnection.GetStorageMonitorInfoAsync` + `StorageMonitorTriggered` (Task 3); `IStorageMonitorStore.ListByServerAsync` (Task 6 — see note); `StorageMonitorTriggeredEvent` (Task 1).
- Produces: `IRustServerQuery.GetStorageContentsAsync(ulong guildId, Guid serverId, ulong entityId, CancellationToken)` → `Task<StorageContentsSnapshot?>`; the supervisor publishes `StorageMonitorTriggeredEvent` on prime and on trigger.

> **Ordering note:** the prime arm needs `IStorageMonitorStore` (Task 6). To keep tasks independently testable, do Task 6 (persistence) BEFORE this task, OR temporarily prime nothing and add the store arm in Task 6. Recommended: reorder so persistence (Task 6) runs before this. The plan below assumes `IStorageMonitorStore` exists; if executing in number order, split the prime arm out. The reviewer should pick one and note it.

- [ ] **Step 1: Add `GetStorageContentsAsync` to `IRustServerQuery`**

After `GetSmartSwitchStateAsync` (line ~65):

```csharp
    /// <summary>Reads a storage monitor's contents for a (guild, server), or null when there is no live socket.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The storage-monitor entity id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The contents snapshot, or null when unreachable.</returns>
    Task<StorageContentsSnapshot?> GetStorageContentsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken);
```

- [ ] **Step 2: Write the failing supervisor test**

Add to the Connections supervisor tests (match the existing test that asserts switch priming publishes a `SmartDeviceTriggeredEvent`). Seed one `SmartStorageMonitor` via the `IStorageMonitorStore` substitute returning it from `ListByServerAsync`, script `EnqueueStorageInfo` on the fake, connect, and assert the bus received a `StorageMonitorTriggeredEvent` with the contents:

```csharp
[Fact]
public async Task Connect_PrimesStorageMonitors_PublishesTriggeredEvent()
{
    var contents = new StorageContentsSnapshot(24, true, DateTimeOffset.UnixEpoch,
        [new StorageItemSnapshot(-151838493, 500, false)]);
    StorageMonitors.ListByServerAsync(GuildId, ServerId, Arg.Any<CancellationToken>())
        .Returns<IReadOnlyList<SmartStorageMonitor>>([new SmartStorageMonitor
        {
            GuildId = GuildId, ServerId = ServerId, EntityId = 777UL, Name = "Box"
        }]);
    Connection.EnqueueStorageInfo(777UL, contents);

    await StartAndConnectAsync(); // match the assembly's connect helper

    await EventBus.Received().PublishAsync(
        Arg.Is<StorageMonitorTriggeredEvent>(e => e.EntityId == 777UL && e.Contents.Capacity == 24),
        Arg.Any<CancellationToken>());
}
```

> Adapt `StorageMonitors`, `GuildId`, `ServerId`, `Connection`, `EventBus`, `StartAndConnectAsync` to the assembly's existing test harness. You will need to register an `IStorageMonitorStore` substitute in the harness's scope (the supervisor resolves it inside a scope just like `ISwitchStore`) — failing to do so reproduces the "assembly drops tests" trap.

- [ ] **Step 3: Run — verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1 --filter Connect_PrimesStorageMonitors_PublishesTriggeredEvent`
Expected: FAIL (no storage priming yet).

- [ ] **Step 4: Subscribe + publish in the connected window**

In `EnsureConnectionAsync`, next to `OnSmartDevice` (line ~476), add a local handler and subscribe/unsubscribe:

```csharp
#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<StorageMonitorTrigger> delegate shape.
        void OnStorage(object? sender, StorageMonitorTrigger trigger)
        {
            _ = PublishStorageTriggerAsync(key, trigger);
        }
#pragma warning restore RCS1163
```

Subscribe after `connection.SmartDeviceTriggered += OnSmartDevice;` (line ~486):

```csharp
        connection.StorageMonitorTriggered += OnStorage;
```

Unsubscribe in the `finally` after `connection.SmartDeviceTriggered -= OnSmartDevice;` (line ~512):

```csharp
        connection.StorageMonitorTriggered -= OnStorage;
```

Add the publish method (mirror `PublishDeviceTriggerAsync` at line 902):

```csharp
    /// <summary>Trigger path: contents are carried on the broadcast arg — no re-read.</summary>
    /// <param name="key">The (guild, server) routing key.</param>
    /// <param name="trigger">The storage trigger carrying the entity id and contents.</param>
    private async Task PublishStorageTriggerAsync((ulong Guild, Guid Server) key, StorageMonitorTrigger trigger)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await eventBus.PublishAsync(
                    new StorageMonitorTriggeredEvent(key.Guild, key.Server, trigger.EntityId, trigger.Contents),
                    _shutdown.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a storage publish failure must not crash the socket callback.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDevicePublishFailed(logger, ex, trigger.EntityId, key.Server);
        }
    }
```

- [ ] **Step 5: Add the prime arm in `PrimeDevicesAsync`**

After the alarm prime arm (line ~870), add a third arm. Note: the prime read returns contents, so it does NOT reuse `PrimeEntityIdsAsync` (which reads a bool). Add a dedicated loop:

```csharp
        IReadOnlyList<Domain.StorageMonitors.SmartStorageMonitor> monitors;
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
                monitors = await store.ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a failed monitor-list read just skips storage priming for this connection.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDeviceListFailed(logger, ex, key.Server);
            return;
        }

#pragma warning disable S3267 // Not a projection: each iteration awaits with per-entity best-effort error handling.
        foreach (var monitor in monitors)
#pragma warning restore S3267
        {
            try
            {
                await PublishStoragePrimeAsync(key, connection, monitor.EntityId).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
#pragma warning disable CA1031 // Broad catch: a single monitor's prime failure is logged and skipped.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogDevicePrimeFailed(logger, ex, monitor.EntityId, key.Server);
            }
        }
```

Add the prime-publish method (mirror `PublishDevicePrimeAsync` at line 934):

```csharp
    /// <summary>Prime path: read contents on connect (also primes the socket's interest), then publish.</summary>
    /// <param name="key">The (guild, server) routing key.</param>
    /// <param name="connection">The live connection used to read contents.</param>
    /// <param name="entityId">The storage-monitor entity id to prime.</param>
    private async Task PublishStoragePrimeAsync(
        (ulong Guild, Guid Server) key,
        IRustServerConnection connection,
        ulong entityId)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            var contents = await connection
                .GetStorageMonitorInfoAsync(entityId, _options.HeartbeatTimeout, _shutdown.Token)
                .ConfigureAwait(false);
            if (contents is null)
            {
                return; // unreachable read; the relay leaves the embed as-is (or unreachable via status events).
            }

            await eventBus.PublishAsync(
                    new StorageMonitorTriggeredEvent(key.Guild, key.Server, entityId, contents),
                    _shutdown.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
#pragma warning disable CA1031 // Broad catch: a storage prime failure must not crash the connect path.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogDevicePublishFailed(logger, ex, entityId, key.Server);
        }
    }
```

Add `using RustPlusBot.Persistence.StorageMonitors;` for `IStorageMonitorStore`.

- [ ] **Step 6: Implement `GetStorageContentsAsync` on the supervisor**

Next to `GetSmartSwitchStateAsync` (line ~255), mirror its body:

```csharp
    /// <inheritdoc />
    public async Task<StorageContentsSnapshot?> GetStorageContentsAsync(
        ulong guildId,
        Guid serverId,
        ulong entityId,
        CancellationToken cancellationToken)
    {
        if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
        {
            return null;
        }

        return await live.Connection
            .GetStorageMonitorInfoAsync(entityId, _options.HeartbeatTimeout, cancellationToken)
            .ConfigureAwait(false);
    }
```

- [ ] **Step 7: Run — verify pass**

Run: `dotnet build src/RustPlusBot.Features.Connections -warnaserror` then `dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1`
Expected: build clean; the new test passes; no pre-existing test regresses.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Abstractions src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(connections): supervisor primes/relays storage monitors + GetStorageContentsAsync"
```

---

## Task 5: Domain entity

**Files:**

- Create: `src/RustPlusBot.Domain/StorageMonitors/SmartStorageMonitor.cs`
- Test: none (POCO; exercised by Task 6).

**Interfaces:**

- Produces: `RustPlusBot.Domain.StorageMonitors.SmartStorageMonitor` with `Guid Id`, `ulong GuildId`, `Guid ServerId`, `ulong EntityId`, `string Name`, `ulong? MessageId`, `ulong PairedByUserId`, `DateTimeOffset CreatedUtc`.

> No `LastIsActive` equivalent — contents are live-only, never persisted (spec). `MessageId` is kept so the embed self-heals across restarts (same as `SmartSwitch`).

- [ ] **Step 1: Create the entity (mirror `SmartSwitch`, drop `LastIsActive`)**

```csharp
namespace RustPlusBot.Domain.StorageMonitors;

/// <summary>A paired Smart Storage Monitor the bot manages, surviving restarts. Guild- and server-scoped.</summary>
public sealed class SmartStorageMonitor
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this monitor belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The in-game storage-monitor entity id.</summary>
    public ulong EntityId { get; set; }

    /// <summary>User-facing label; defaults to a generated "Storage Monitor &lt;EntityId&gt;" (the FCM event carries no name).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The Discord message id of this monitor's embed, or null until first posted.</summary>
    public ulong? MessageId { get; set; }

    /// <summary>The Discord user who accepted (validated) the pairing.</summary>
    public ulong PairedByUserId { get; set; }

    /// <summary>When the monitor was accepted (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/RustPlusBot.Domain -warnaserror`
Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Domain
git commit -m "feat(domain): SmartStorageMonitor entity"
```

---

## Task 6: Persistence — store, EF config, DbSet, migration

**Files:**

- Create: `src/RustPlusBot.Persistence/StorageMonitors/IStorageMonitorStore.cs`
- Create: `src/RustPlusBot.Persistence/StorageMonitors/StorageMonitorStore.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/SmartStorageMonitorConfiguration.cs`
- Modify: `src/RustPlusBot.Persistence/BotDbContext.cs` (add DbSet)
- Test: `tests/RustPlusBot.Persistence.Tests/StorageMonitorStoreTests.cs`
- Migration: `src/RustPlusBot.Persistence/Migrations/<timestamp>_SmartStorageMonitors.cs` (generated)

**Interfaces:**

- Consumes: `SmartStorageMonitor` (Task 5).
- Produces: `IStorageMonitorStore` with `AddAsync(guildId, serverId, entityId, name, pairedByUserId, ct) -> Task<SmartStorageMonitor>`, `GetAsync(...) -> Task<SmartStorageMonitor?>`, `ListByServerAsync(guildId, serverId, ct) -> Task<IReadOnlyList<SmartStorageMonitor>>`, `ExistsAsync(...) -> Task<bool>`, `RenameAsync(...)`, `SetMessageIdAsync(...)`, `RemoveAsync(...)`. (No `UpdateStateAsync` — no persisted state.)

> Copy `ISwitchStore`/`SwitchStore` verbatim, rename types, drop `UpdateStateAsync` and the `LastIsActive` set in `AddAsync`. Copy `SmartSwitchConfiguration` verbatim, rename. The `DbContext` DbSet line mirrors `SmartSwitches`.

- [ ] **Step 1: Write the failing store test**

`tests/RustPlusBot.Persistence.Tests/StorageMonitorStoreTests.cs` — match the assembly's in-memory SQLite harness (find `SwitchStoreTests.cs` and copy its fixture usage). Seed a `RustServer` first (the FK requires it):

```csharp
[Fact]
public async Task AddAsync_ThenList_ReturnsMonitor()
{
    await using var ctx = NewContext();
    var server = SeedServer(ctx); // copy the helper from SwitchStoreTests
    await ctx.SaveChangesAsync();
    var store = new StorageMonitorStore(ctx, FixedClock);

    await store.AddAsync(server.GuildId, server.Id, entityId: 777UL, name: "Box", pairedByUserId: 5UL);
    var list = await store.ListByServerAsync(server.GuildId, server.Id);

    list.Should().ContainSingle(m => m.EntityId == 777UL && m.Name == "Box");
}

[Fact]
public async Task AddAsync_DuplicateIdentity_RecoversIdempotently()
{
    await using var ctx = NewContext();
    var server = SeedServer(ctx);
    await ctx.SaveChangesAsync();
    var store = new StorageMonitorStore(ctx, FixedClock);

    var first = await store.AddAsync(server.GuildId, server.Id, 777UL, "Box", 5UL);
    var second = await store.AddAsync(server.GuildId, server.Id, 777UL, "Box again", 6UL);

    second.Id.Should().Be(first.Id); // unique-index race recovered to the winner
}
```

- [ ] **Step 2: Run — verify it fails to compile (types absent)**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests -maxcpucount:1 --filter StorageMonitorStoreTests`
Expected: FAIL — `StorageMonitorStore` / `IStorageMonitorStore` do not exist.

- [ ] **Step 3: Create `IStorageMonitorStore.cs`**

Copy `ISwitchStore.cs`, replace `SmartSwitch`→`SmartStorageMonitor`, `Switches`→`StorageMonitors` namespace, drop the `UpdateStateAsync` member, and reword the `<summary>` lines ("storage monitor" instead of "switch"). Namespace: `RustPlusBot.Persistence.StorageMonitors`. Using: `using RustPlusBot.Domain.StorageMonitors;`.

- [ ] **Step 4: Create `StorageMonitorStore.cs`**

Copy `SwitchStore.cs`. Changes: type names; in `AddAsync` drop `LastIsActive = false`; `context.SmartStorageMonitors.Add(entity)`; drop the `UpdateStateAsync` method. Keep the `DbUpdateException` idempotency recovery and the client-side `OrderBy(CreatedUtc)` verbatim.

- [ ] **Step 5: Create `SmartStorageMonitorConfiguration.cs`**

Copy `SmartSwitchConfiguration.cs`, rename to `SmartStorageMonitor`, keep the unique `(GuildId, ServerId, EntityId)` index and the `HasOne<RustServer>().WithMany().HasForeignKey(m => m.ServerId).OnDelete(DeleteBehavior.Cascade)`.

- [ ] **Step 6: Add the DbSet to `BotDbContext`**

After the `SmartAlarms` DbSet (line 53):

```csharp
    /// <summary>Managed Smart Storage Monitors.</summary>
    public DbSet<SmartStorageMonitor> SmartStorageMonitors => Set<SmartStorageMonitor>();
```

Add `using RustPlusBot.Domain.StorageMonitors;`.

- [ ] **Step 7: Run the store tests — verify pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests -maxcpucount:1 --filter StorageMonitorStoreTests`
Expected: PASS.

- [ ] **Step 8: Generate the migration**

Run (from repo root; the repo uses the local `dotnet-ef` tool):

```bash
dotnet tool restore
dotnet ef migrations add SmartStorageMonitors --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host
```

Expected: a new `Migrations/<timestamp>_SmartStorageMonitors.cs` + a `ModelSnapshot` update creating the `SmartStorageMonitors` table with the unique index and the FK. Inspect the generated `Up()` to confirm the table, the `IX_SmartStorageMonitors_GuildId_ServerId_EntityId` unique index, and the cascade FK to `RustServers`.

> If `dotnet ef` errors with "Unable to retrieve project metadata" (a known tooling quirk in this repo), build the solution first (`dotnet build RustPlusBot.slnx`) and retry. If it still fails, write the migration by hand mirroring the `SmartSwitches` migration (`Migrations/*_SmartSwitches.cs`), then verify the model matches.

- [ ] **Step 9: Verify no unintended model drift + build**

Run: `dotnet build RustPlusBot.slnx -warnaserror`
Expected: clean. The only new schema objects are the `SmartStorageMonitors` table + its index/FK.

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests
git commit -m "feat(persistence): SmartStorageMonitor store, EF config + migration"
```

---

## Task 7: Item-name lookup — `items.json` + resolver

**Files:**

- Create: `src/RustPlusBot.Features.StorageMonitors/RustPlusBot.Features.StorageMonitors.csproj` (new project)
- Create: `src/RustPlusBot.Features.StorageMonitors/Naming/items.json` (embedded resource)
- Create: `src/RustPlusBot.Features.StorageMonitors/Naming/IItemNameResolver.cs`
- Create: `src/RustPlusBot.Features.StorageMonitors/Naming/EmbeddedItemNameResolver.cs`
- Create: `tests/RustPlusBot.Features.StorageMonitors.Tests/RustPlusBot.Features.StorageMonitors.Tests.csproj` (new test project)
- Test: `tests/RustPlusBot.Features.StorageMonitors.Tests/EmbeddedItemNameResolverTests.cs`

**Interfaces:**

- Produces: `IItemNameResolver.Resolve(int itemId) -> string`; `EmbeddedItemNameResolver : IItemNameResolver` (singleton, loads the embedded json once into a `FrozenDictionary<int,string>`, returns `"Item {id}"` for unknown ids).

> Create the projects first. Copy a sibling feature `.csproj` (e.g. `src/RustPlusBot.Features.Switches/RustPlusBot.Features.Switches.csproj`) for project references (Abstractions, Persistence, Discord, Domain, Workspace, Connections, Localization) and target framework, and add the embedded-resource item group. Copy a sibling test `.csproj` (`tests/RustPlusBot.Features.Switches.Tests/*.csproj`) for the test project. Register both in `RustPlusBot.slnx`.

- [ ] **Step 1: Create the feature project + register it**

Create `src/RustPlusBot.Features.StorageMonitors/RustPlusBot.Features.StorageMonitors.csproj` mirroring the Switches csproj's `<PropertyGroup>` and `<ProjectReference>`s (Abstractions, Persistence, RustPlusBot.Discord, Domain, Features.Workspace, Features.Connections, Localization). Add the embedded resource:

```xml
  <ItemGroup>
    <EmbeddedResource Include="Naming/items.json" />
  </ItemGroup>
```

Then:

```bash
dotnet sln RustPlusBot.slnx add src/RustPlusBot.Features.StorageMonitors/RustPlusBot.Features.StorageMonitors.csproj
```

- [ ] **Step 2: Source `items.json`**

Produce `Naming/items.json` as a flat JSON object mapping the Rust item id (as a string key, since JSON object keys are strings; negative ids are common) to its display name. Source the id→name pairs from Facepunch's published item manifest (the same data rustplusplus bundles in `staticFiles/items.json`), trimmed to ONLY id→name. Example shape:

```json
{
  "-151838493": "Wood",
  "-2099697608": "Stone",
  "69511070": "Metal Fragments",
  "317398316": "High Quality Metal"
}
```

> Keep only id→name. Do not bundle shortnames/images/crafting (that is subsystem 6). The file may be large (~hundreds of entries); that is fine as an embedded resource. Verify the four ids above against the manifest during sourcing.

- [ ] **Step 3: Create `IItemNameResolver.cs`**

```csharp
namespace RustPlusBot.Features.StorageMonitors.Naming;

/// <summary>Resolves a Rust item id to a human-readable display name.</summary>
public interface IItemNameResolver
{
    /// <summary>Gets the display name for an item id, or a stable "Item {id}" fallback when unknown.</summary>
    /// <param name="itemId">The Rust item id.</param>
    /// <returns>The display name, or "Item {id}" if not in the bundled lookup.</returns>
    string Resolve(int itemId);
}
```

- [ ] **Step 4: Write the failing resolver test**

`tests/RustPlusBot.Features.StorageMonitors.Tests/EmbeddedItemNameResolverTests.cs`:

```csharp
using System.Globalization;
using FluentAssertions;
using RustPlusBot.Features.StorageMonitors.Naming;
using Xunit;

namespace RustPlusBot.Features.StorageMonitors.Tests;

public sealed class EmbeddedItemNameResolverTests
{
    [Fact]
    public void Resolve_KnownId_ReturnsDisplayName()
    {
        var resolver = new EmbeddedItemNameResolver();
        resolver.Resolve(-151838493).Should().Be("Wood");
    }

    [Fact]
    public void Resolve_UnknownId_ReturnsFallback()
    {
        var resolver = new EmbeddedItemNameResolver();
        resolver.Resolve(123456789).Should().Be("Item 123456789");
    }
}
```

- [ ] **Step 5: Run — verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests -maxcpucount:1 --filter EmbeddedItemNameResolverTests`
Expected: FAIL — `EmbeddedItemNameResolver` does not exist.

- [ ] **Step 6: Create `EmbeddedItemNameResolver.cs`**

```csharp
using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace RustPlusBot.Features.StorageMonitors.Naming;

/// <summary>Resolves item ids from the bundled <c>items.json</c> (Facepunch's published item names). Singleton.</summary>
public sealed class EmbeddedItemNameResolver : IItemNameResolver
{
    private static readonly FrozenDictionary<int, string> Names = Load();

    /// <inheritdoc />
    public string Resolve(int itemId) =>
        Names.TryGetValue(itemId, out var name)
            ? name
            : "Item " + itemId.ToString(CultureInfo.InvariantCulture);

    private static FrozenDictionary<int, string> Load()
    {
        var assembly = typeof(EmbeddedItemNameResolver).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("items.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException("Embedded items.json not found.");
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
                  ?? throw new InvalidOperationException("items.json deserialized to null.");
        return raw
            .Where(kvp => int.TryParse(kvp.Key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .ToFrozenDictionary(
                kvp => int.Parse(kvp.Key, NumberStyles.Integer, CultureInfo.InvariantCulture),
                kvp => kvp.Value);
    }
}
```

Add `using System.Linq;` if the analyzer requires the explicit using.

- [ ] **Step 7: Run — verify pass**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests -maxcpucount:1 --filter EmbeddedItemNameResolverTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.StorageMonitors tests/RustPlusBot.Features.StorageMonitors.Tests RustPlusBot.slnx
git commit -m "feat(storage): item-name lookup (embedded items.json + resolver)"
```

---

## Task 8: Embed renderer + component ids + rename modal

**Files:**

- Create: `src/RustPlusBot.Features.StorageMonitors/Rendering/StorageMonitorComponentIds.cs`
- Create: `src/RustPlusBot.Features.StorageMonitors/Rendering/StorageMonitorEmbedRenderer.cs`
- Create: `src/RustPlusBot.Features.StorageMonitors/Modules/StorageMonitorRenameModal.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx` (add keys)
- Test: `tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorEmbedRendererTests.cs`

**Interfaces:**

- Consumes: `ILocalizer` (shared), `IItemNameResolver` (Task 7), `SmartStorageMonitor` (Task 5), `StorageContentsSnapshot` (Task 1).
- Produces: `StorageMonitorEmbedRenderer.RenderMonitor(SmartStorageMonitor monitor, StorageContentsSnapshot? contents, string culture) -> (Embed, MessageComponent)` (contents null ⇒ unreachable) and `RenderPrompt(Guid serverId, ulong entityId, string defaultName, string culture) -> (Embed, MessageComponent)`. Component-id prefixes `storage:refresh:`, `storage:rename:`, `storage:rename:modal:`, `storage:accept:`, `storage:dismiss:`, input id `storage:rename:input`.

> Open `SwitchEmbedRenderer.cs` and `SwitchComponentIds.cs` and mirror them. The control row has Refresh + Rename (no on/off/strobe). Use `global::Discord` types implicitly via `using Discord;` (this project does not have the `RustPlusBot.Discord` namespace shadow problem unless a test references it — use `global::Discord.Embed` in tests).

- [ ] **Step 1: Create `StorageMonitorComponentIds.cs`**

```csharp
namespace RustPlusBot.Features.StorageMonitors.Rendering;

/// <summary>Custom ids for storage-monitor components. Tails encode "{serverId}:{entityId}".</summary>
internal static class StorageMonitorComponentIds
{
    /// <summary>Pairing-prompt Accept button; tail "{serverId}:{entityId}".</summary>
    public const string AcceptPrefix = "storage:accept:";

    /// <summary>Pairing-prompt Dismiss button; tail "{serverId}:{entityId}".</summary>
    public const string DismissPrefix = "storage:dismiss:";

    /// <summary>Refresh button (re-reads contents); tail "{serverId}:{entityId}".</summary>
    public const string RefreshPrefix = "storage:refresh:";

    /// <summary>Rename button (opens the modal); tail "{serverId}:{entityId}".</summary>
    public const string RenamePrefix = "storage:rename:";

    /// <summary>Rename modal id; tail "{serverId}:{entityId}".</summary>
    public const string RenameModalPrefix = "storage:rename:modal:";

    /// <summary>The rename modal's text input id.</summary>
    public const string RenameInputId = "storage:rename:input";
}
```

> Ordering trap: `RenamePrefix` (`storage:rename:`) is a prefix of `RenameModalPrefix` (`storage:rename:modal:`). Discord.Net matches the most specific registered pattern, and the Switch module proves this exact pair works (`switch:rename:` + `switch:rename:modal:`), so it is safe. Keep the modal handler's `[ModalInteraction]` and the button's `[ComponentInteraction]` exactly as the Switch module pairs them.

- [ ] **Step 2: Add localization keys to `Strings.resx` AND `Strings.fr.resx`**

Add these keys (EN in `Strings.resx`, FR in `Strings.fr.resx`). Match the existing `<data name=... xml:space="preserve"><value>...</value></data>` element shape exactly.

| key | EN | FR |
|---|---|---|
| `channel.storagemonitors.name` | `storage-monitors` | `moniteurs-stockage` |
| `storage.type.toolcupboard` | `Tool Cupboard` | `Armoire à outils` |
| `storage.type.largebox` | `Large Box` | `Grande caisse` |
| `storage.type.smallbox` | `Small Box` | `Petite caisse` |
| `storage.type.unknown` | `Storage Monitor` | `Moniteur de stockage` |
| `storage.contents.empty` | `Empty` | `Vide` |
| `storage.contents.bp` | `{0} (BP)` | `{0} (PL)` |
| `storage.contents.line` | `{0} ×{1}` | `{0} ×{1}` |
| `storage.slots` | `{0} / {1} slots` | `{0} / {1} emplacements` |
| `storage.protection.on` | `Protected — expires in {0}` | `Protégé — expire dans {0}` |
| `storage.protection.off` | `Not protected` | `Non protégé` |
| `storage.status.unreachable` | `⚠️ Unreachable` | `⚠️ Injoignable` |
| `storage.embed.footer` | `Entity {0}` | `Entité {0}` |
| `storage.button.refresh` | `Refresh` | `Actualiser` |
| `storage.button.rename` | `Rename` | `Renommer` |
| `storage.prompt.title` | `New storage monitor detected` | `Nouveau moniteur de stockage détecté` |
| `storage.prompt.body` | `Add **{0}**?` | `Ajouter **{0}** ?` |
| `storage.prompt.accept` | `Add it` | `Ajouter` |
| `storage.prompt.dismiss` | `Dismiss` | `Ignorer` |

> Confirm the FR translations read naturally before finalizing; adjust wording freely (these are reasonable defaults). Keep `{0}`/`{1}` placeholders byte-identical between EN and FR.

- [ ] **Step 3: Write the failing renderer test**

`tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorEmbedRendererTests.cs` — use a fake `ILocalizer` that returns the key (or a formatted echo) so assertions are deterministic, and a fake `IItemNameResolver`:

```csharp
using FluentAssertions;
using NSubstitute;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.StorageMonitors.Naming;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Localization;
using Xunit;

namespace RustPlusBot.Features.StorageMonitors.Tests;

public sealed class StorageMonitorEmbedRendererTests
{
    private static StorageMonitorEmbedRenderer Create(out IItemNameResolver names)
    {
        var loc = Substitute.For<ILocalizer>();
        loc.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => (string)ci[0]);
        loc.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(ci => (string)ci[0] + ":" + string.Join(",", (object[])ci[2]));
        names = Substitute.For<IItemNameResolver>();
        names.Resolve(Arg.Any<int>()).Returns(ci => "Item" + (int)ci[0]);
        return new StorageMonitorEmbedRenderer(loc, names);
    }

    [Fact]
    public void RenderMonitor_NullContents_MarksUnreachable_AndDisablesButtons()
    {
        var r = Create(out _);
        var monitor = new SmartStorageMonitor { Id = System.Guid.NewGuid(), ServerId = System.Guid.NewGuid(), EntityId = 7UL, Name = "Box" };

        var (embed, components) = r.RenderMonitor(monitor, contents: null, culture: "en");

        embed.Description.Should().Contain("storage.status.unreachable");
        components.Components.SelectMany(row => row.Components)
            .Should().OnlyContain(c => ((global::Discord.ButtonComponent)c).IsDisabled);
    }

    [Fact]
    public void RenderMonitor_ToolCupboardWithProtection_ShowsTypeAndProtection()
    {
        var r = Create(out _);
        var monitor = new SmartStorageMonitor { Id = System.Guid.NewGuid(), ServerId = System.Guid.NewGuid(), EntityId = 7UL, Name = "TC" };
        var contents = new StorageContentsSnapshot(24, true, System.DateTimeOffset.UtcNow.AddHours(4), []);

        var (embed, _) = r.RenderMonitor(monitor, contents, "en");

        embed.Description.Should().Contain("storage.type.toolcupboard");
        embed.Description.Should().Contain("storage.protection.on");
    }

    [Fact]
    public void RenderMonitor_BoxWithItems_ListsNamedQuantitiesSortedDesc()
    {
        var r = Create(out _);
        var monitor = new SmartStorageMonitor { Id = System.Guid.NewGuid(), ServerId = System.Guid.NewGuid(), EntityId = 7UL, Name = "Box" };
        var contents = new StorageContentsSnapshot(48, null, null,
            [new StorageItemSnapshot(100, 5, false), new StorageItemSnapshot(200, 50, false)]);

        var (embed, _) = r.RenderMonitor(monitor, contents, "en");

        // Item200 (qty 50) appears before Item100 (qty 5); no protection line for a non-TC box.
        embed.Description.Should().Contain("Item200");
        embed.Description.Should().Contain("storage.type.largebox");
        embed.Description.Should().NotContain("storage.protection");
    }
}
```

- [ ] **Step 4: Run — verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests -maxcpucount:1 --filter StorageMonitorEmbedRendererTests`
Expected: FAIL — `StorageMonitorEmbedRenderer` does not exist.

- [ ] **Step 5: Create `StorageMonitorEmbedRenderer.cs`**

```csharp
using System.Globalization;
using System.Text;
using Discord;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.StorageMonitors;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.StorageMonitors.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.StorageMonitors.Rendering;

/// <summary>Renders a Smart Storage Monitor as a Discord embed + control row, and the pairing prompt. Pure.</summary>
/// <param name="localizer">The shared localizer.</param>
/// <param name="names">Resolves item ids to display names.</param>
internal sealed class StorageMonitorEmbedRenderer(ILocalizer localizer, IItemNameResolver names)
{
    /// <summary>Renders the monitor embed and its Refresh/Rename buttons. <paramref name="contents"/> null ⇒ unreachable.</summary>
    /// <param name="monitor">The monitor.</param>
    /// <param name="contents">The current contents, or null when unreachable.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The embed and the component row.</returns>
    public (Embed Embed, MessageComponent Components) RenderMonitor(
        SmartStorageMonitor monitor,
        StorageContentsSnapshot? contents,
        string culture)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        var unreachable = contents is null;

        var description = new StringBuilder();
        if (unreachable)
        {
            description.Append(localizer.Get("storage.status.unreachable", culture));
        }
        else
        {
            description.AppendLine(localizer.Get(TypeKey(contents!.Capacity), culture));
            AppendProtection(description, contents, culture);
            AppendContents(description, contents, culture);
        }

        var embed = new EmbedBuilder()
            .WithTitle(monitor.Name)
            .WithDescription(description.ToString())
            .WithFooter(localizer.Get("storage.embed.footer", culture, monitor.EntityId))
            .Build();

        var tail = $"{monitor.ServerId}:{monitor.EntityId}";
        var components = new ComponentBuilder()
            .WithButton(localizer.Get("storage.button.refresh", culture),
                StorageMonitorComponentIds.RefreshPrefix + tail, ButtonStyle.Primary, disabled: unreachable)
            .WithButton(localizer.Get("storage.button.rename", culture),
                StorageMonitorComponentIds.RenamePrefix + tail, ButtonStyle.Secondary, disabled: unreachable)
            .Build();

        return (embed, components);
    }

    /// <summary>Renders the transient "New storage monitor detected — Add it?" prompt.</summary>
    /// <param name="serverId">The server id.</param>
    /// <param name="entityId">The entity id.</param>
    /// <param name="defaultName">The generated default name.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The prompt embed and Accept/Dismiss row.</returns>
    public (Embed Embed, MessageComponent Components) RenderPrompt(
        Guid serverId,
        ulong entityId,
        string defaultName,
        string culture)
    {
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("storage.prompt.title", culture))
            .WithDescription(localizer.Get("storage.prompt.body", culture, defaultName))
            .Build();

        var tail = $"{serverId}:{entityId}";
        var components = new ComponentBuilder()
            .WithButton(localizer.Get("storage.prompt.accept", culture),
                StorageMonitorComponentIds.AcceptPrefix + tail, ButtonStyle.Success)
            .WithButton(localizer.Get("storage.prompt.dismiss", culture),
                StorageMonitorComponentIds.DismissPrefix + tail, ButtonStyle.Secondary)
            .Build();

        return (embed, components);
    }

    private static string TypeKey(int? capacity) => capacity switch
    {
        24 => "storage.type.toolcupboard",
        48 => "storage.type.largebox",
        12 => "storage.type.smallbox",
        _ => "storage.type.unknown",
    };

    private void AppendProtection(StringBuilder sb, StorageContentsSnapshot contents, string culture)
    {
        // Protection is only meaningful for a Tool Cupboard (capacity 24).
        if (contents.Capacity != 24)
        {
            return;
        }

        if (contents is { HasProtection: true, ProtectionExpiry: { } expiry })
        {
            var remaining = expiry - DateTimeOffset.UtcNow;
            sb.AppendLine(localizer.Get("storage.protection.on", culture,
                DurationFormat.Compact(remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining)));
        }
        else
        {
            sb.AppendLine(localizer.Get("storage.protection.off", culture));
        }
    }

    private void AppendContents(StringBuilder sb, StorageContentsSnapshot contents, string culture)
    {
        var capacityText = contents.Capacity?.ToString(CultureInfo.InvariantCulture) ?? "?";
        sb.AppendLine(localizer.Get("storage.slots", culture, contents.Items.Count, capacityText));

        if (contents.Items.Count == 0)
        {
            sb.AppendLine(localizer.Get("storage.contents.empty", culture));
            return;
        }

        foreach (var item in contents.Items.OrderByDescending(i => i.Quantity))
        {
            var name = names.Resolve(item.ItemId);
            if (item.IsBlueprint)
            {
                name = localizer.Get("storage.contents.bp", culture, name);
            }

            sb.AppendLine(localizer.Get("storage.contents.line", culture, name, item.Quantity));
        }
    }
}
```

> `DurationFormat.Compact` is the repo's existing helper (used by event/team handlers). Confirm its namespace with `grep -rn "DurationFormat" src --include=*.cs | grep -v obj | head` and add the matching `using`. If its signature differs, adapt the call.

- [ ] **Step 6: Create `StorageMonitorRenameModal.cs`** (mirror `SwitchRenameModal`)

```csharp
using Discord;
using Discord.Interactions;
using RustPlusBot.Features.StorageMonitors.Rendering;

namespace RustPlusBot.Features.StorageMonitors.Modules;

/// <summary>The modal that collects a new storage-monitor name. Handled by <see cref="StorageMonitorComponentModule"/>.</summary>
public sealed class StorageMonitorRenameModal : IModal
{
    /// <summary>The new name.</summary>
    [InputLabel("Storage monitor name")]
    [ModalTextInput(StorageMonitorComponentIds.RenameInputId, TextInputStyle.Short, maxLength: 128)]
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Title => "Rename storage monitor";
}
```

- [ ] **Step 7: Run — verify pass**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests -maxcpucount:1 --filter StorageMonitorEmbedRendererTests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.StorageMonitors src/RustPlusBot.Localization tests/RustPlusBot.Features.StorageMonitors.Tests
git commit -m "feat(storage): embed renderer, component ids, rename modal + i18n keys"
```

---

## Task 9: Poster + pairing coordinator

**Files:**

- Create: `src/RustPlusBot.Features.StorageMonitors/Posting/IStorageMonitorChannelPoster.cs`
- Create: `src/RustPlusBot.Features.StorageMonitors/Posting/DiscordStorageMonitorChannelPoster.cs` (untested shim)
- Create: `src/RustPlusBot.Features.StorageMonitors/Pairing/StorageMonitorPairingCoordinator.cs`
- Test: `tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorPairingCoordinatorTests.cs`

**Interfaces:**

- Consumes: `IStorageMonitorChannelLocator` (Task 10 — see ordering note), `IStorageMonitorChannelPoster`, `StorageMonitorEmbedRenderer` (Task 8), `IStorageMonitorStore` (Task 6), `IWorkspaceStore` (existing), `StorageMonitorPairedEvent` (Task 1).
- Produces: `IStorageMonitorChannelPoster.EnsureAsync(channelId, messageId, embed, components, ct) -> Task<ulong?>`; `StorageMonitorPairingCoordinator` with `HandlePairedAsync(StorageMonitorPairedEvent, ct)`, `TryAcceptAsync(guildId, serverId, entityId, acceptingUserId, ct) -> Task<bool>`, `TryDismiss(guildId, serverId, entityId) -> bool`.

> **Ordering note:** the coordinator needs `IStorageMonitorChannelLocator` (Task 10). Do Task 10 before this, OR temporarily inject the locator interface (declare it in Task 10 first). Recommended: execute Task 10 before Task 9. The reviewer picks one.
>
> Copy `ISwitchChannelPoster`/`DiscordSwitchChannelPoster` and `SwitchPairingCoordinator` verbatim, renaming types. The coordinator's `defaultName` becomes `$"Storage Monitor {evt.EntityId}"`. On accept, the freshly-accepted monitor renders with `contents: null` (unknown until the prime/trigger arrives moments later) — so call `renderer.RenderMonitor(added, contents: null, culture)`. The supervisor's prime path republishes real contents shortly (same pattern as switches rendering `LastIsActive`).

- [ ] **Step 1: Create `IStorageMonitorChannelPoster.cs`** (copy `ISwitchChannelPoster`, rename, keep `global::Discord.Embed`/`global::Discord.MessageComponent`).

- [ ] **Step 2: Create `DiscordStorageMonitorChannelPoster.cs`** (copy `DiscordSwitchChannelPoster` verbatim, rename; it delegates to `DiscordChannelMessenger.EnsureAsync` which already self-heals 404).

- [ ] **Step 3: Write the failing coordinator test**

Mirror `SwitchPairingCoordinator`'s tests if they exist; otherwise write:

```csharp
[Fact]
public async Task TryAcceptAsync_PersistsMonitor_AndReturnsTrue()
{
    // Arrange a coordinator with: a scope factory returning a scope whose provider yields
    // a real in-memory IStorageMonitorStore + a stub IWorkspaceStore (GetCultureAsync -> "en"),
    // an IStorageMonitorChannelLocator stub (GetChannelIdAsync -> 555UL),
    // an IStorageMonitorChannelPoster stub (EnsureAsync -> 999UL), and the real renderer.
    // (Copy the harness shape from the Switch coordinator tests / other feature coordinator tests.)

    var accepted = await coordinator.TryAcceptAsync(Guild, Server, entityId: 7UL, acceptingUserId: 5UL, default);

    accepted.Should().BeTrue();
    (await store.ExistsAsync(Guild, Server, 7UL, default)).Should().BeTrue();
}

[Fact]
public void TryDismiss_NoPending_ReturnsFalse() =>
    coordinator.TryDismiss(Guild, Server, 7UL).Should().BeFalse();
```

> If standing up the scoped harness is heavy, model it on the closest existing coordinator test in the repo (search `grep -rln "PairingCoordinator" tests`). Keep at least: accept persists + returns true; accept when already-managed returns false; dismiss of an unknown returns false.

- [ ] **Step 4: Run — verify it fails.** Run the filter; expect FAIL (coordinator absent).

- [ ] **Step 5: Create `StorageMonitorPairingCoordinator.cs`** (copy `SwitchPairingCoordinator`, rename types, `defaultName = $"Storage Monitor {evt.EntityId}"`, render with `contents: null` on accept, store is `IStorageMonitorStore`, locator is `IStorageMonitorChannelLocator`).

- [ ] **Step 6: Run — verify pass.**

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.StorageMonitors tests/RustPlusBot.Features.StorageMonitors.Tests
git commit -m "feat(storage): channel poster + pairing coordinator"
```

---

## Task 10: Workspace channel + locator

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs` (add `ServerStorageMonitors`)
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs` (add the `ChannelSpec`)
- Create: `src/RustPlusBot.Features.Workspace/Locating/IStorageMonitorChannelLocator.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/StorageMonitorChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs` (register the locator — find where `SwitchChannelLocator` is registered)
- Test: `tests/RustPlusBot.Features.Workspace.Tests/...` (assert the spec includes the new channel; match the existing channel-spec test)

**Interfaces:**

- Consumes: `CachingChannelLocator` (existing base), `WorkspaceChannelKeys` (existing).
- Produces: `WorkspaceChannelKeys.ServerStorageMonitors = "storagemonitors"`; `IStorageMonitorChannelLocator.GetChannelIdAsync(guildId, serverId, ct) -> Task<ulong?>`; `StorageMonitorChannelLocator : CachingChannelLocator, IStorageMonitorChannelLocator`.

> Run this BEFORE Task 9 (the coordinator depends on the locator interface). Mirror `SwitchChannelLocator`/`ISwitchChannelLocator` exactly.

- [ ] **Step 1: Add the channel key**

In `WorkspaceKeys.cs`, after `ServerAlarms` (line ~31):

```csharp
    /// <summary>Per-server storage-monitors channel key.</summary>
    public const string ServerStorageMonitors = "storagemonitors";
```

- [ ] **Step 2: Add the `ChannelSpec`**

In `ServerWorkspaceSpecProvider.GetChannelSpecs()`, after the `ServerAlarms` spec (the one at order index 5):

```csharp
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerStorageMonitors, "channel.storagemonitors.name",
            ChannelPermissionProfile.Interactive, 6),
```

> Confirm the positional `ChannelSpec` ctor args by reading `Registry/ChannelSpec.cs`; the last int is the sort order. Use the next free order index after `ServerAlarms`.

- [ ] **Step 3: Create `IStorageMonitorChannelLocator.cs`** (copy `ISwitchChannelLocator`, rename, reword `<summary>` to "#storagemonitors").

- [ ] **Step 4: Create `StorageMonitorChannelLocator.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the #storagemonitors channel id for a (guild, server).</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class StorageMonitorChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : CachingChannelLocator(scopeFactory, clock, WorkspaceChannelKeys.ServerStorageMonitors),
        IStorageMonitorChannelLocator;
```

- [ ] **Step 5: Register the locator**

In `WorkspaceServiceCollectionExtensions.cs`, next to the `ISwitchChannelLocator` registration, add (match the exact registration style — likely `AddSingleton<IStorageMonitorChannelLocator, StorageMonitorChannelLocator>()` plus, if locators are also registered as `IDisposable`/hosted, mirror that):

```csharp
        services.AddSingleton<IStorageMonitorChannelLocator, StorageMonitorChannelLocator>();
```

- [ ] **Step 6: Write/extend the workspace spec test**

Find the test asserting the per-server channel specs (search `grep -rln "ServerSwitches\|GetChannelSpecs" tests/RustPlusBot.Features.Workspace.Tests`). Add an assertion that the specs include `WorkspaceChannelKeys.ServerStorageMonitors` with `ChannelPermissionProfile.Interactive`. If the test counts specs, bump the expected count.

- [ ] **Step 7: Run — verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests -maxcpucount:1`
Expected: PASS (count reflects the new channel).

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests
git commit -m "feat(workspace): #storagemonitors channel spec + locator"
```

---

## Task 11: State relay

**Files:**

- Create: `src/RustPlusBot.Features.StorageMonitors/Relaying/StorageMonitorStateRelay.cs`
- Test: `tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorStateRelayTests.cs`

**Interfaces:**

- Consumes: `IStorageMonitorChannelLocator` (Task 10), `IStorageMonitorChannelPoster` (Task 9), `StorageMonitorEmbedRenderer` (Task 8), `IStorageMonitorStore` (Task 6), `IConnectionStore` + `IWorkspaceStore` (existing), `StorageMonitorTriggeredEvent` + `ConnectionStatusChangedEvent` (Tasks 1 / existing).
- Produces: `StorageMonitorStateRelay` with `HandleTriggeredAsync(StorageMonitorTriggeredEvent, ct)` (ignore unmanaged ids; else render the carried contents) and `HandleConnectionStatusAsync(ConnectionStatusChangedEvent, ct)` (non-Connected ⇒ render every monitor unreachable).

> Copy `SwitchStateRelay`, but: there is no `UpdateStateAsync` (contents aren't persisted). `HandleTriggeredAsync` checks `ExistsAsync` (ignore alarms/switches/unmanaged), loads the monitor, and renders the event's `Contents` directly. `HandleConnectionStatusAsync` is identical in shape (renders `contents: null` for each monitor when not Connected).

- [ ] **Step 1: Write the failing relay test**

```csharp
[Fact]
public async Task HandleTriggeredAsync_UnmanagedEntity_DoesNothing()
{
    // store.ExistsAsync -> false; assert poster.EnsureAsync was NOT called.
    await relay.HandleTriggeredAsync(
        new StorageMonitorTriggeredEvent(Guild, Server, 7UL,
            new StorageContentsSnapshot(24, null, null, [])), default);

    await poster.DidNotReceive().EnsureAsync(
        Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
        Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
}

[Fact]
public async Task HandleTriggeredAsync_ManagedEntity_PostsEmbed()
{
    // Seed a monitor (store.ExistsAsync -> true, store.GetAsync -> the monitor),
    // locator.GetChannelIdAsync -> 555UL, workspace.GetCultureAsync -> "en",
    // poster.EnsureAsync -> 999UL.
    await relay.HandleTriggeredAsync(
        new StorageMonitorTriggeredEvent(Guild, Server, 7UL,
            new StorageContentsSnapshot(48, null, null, [new StorageItemSnapshot(100, 5, false)])), default);

    await poster.Received(1).EnsureAsync(555UL, Arg.Any<ulong?>(),
        Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
}
```

> Build the scoped harness like the coordinator test (scope factory → provider yielding the stores). Reuse helpers from Task 9's test file if shared.

- [ ] **Step 2: Run — verify it fails.**

- [ ] **Step 3: Create `StorageMonitorStateRelay.cs`** (copy `SwitchStateRelay`; rename; drop `UpdateStateAsync`; `HandleTriggeredAsync` renders `evt.Contents`; `HandleConnectionStatusAsync` renders `contents: null`; persist the new `MessageId` via `SetMessageIdAsync` when it changes, exactly like the switch relay's `RenderAsync`).

- [ ] **Step 4: Run — verify pass.**

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.StorageMonitors tests/RustPlusBot.Features.StorageMonitors.Tests
git commit -m "feat(storage): state relay (trigger -> embed, status -> unreachable)"
```

---

## Task 12: Component module

**Files:**

- Create: `src/RustPlusBot.Features.StorageMonitors/Modules/StorageMonitorComponentModule.cs`
- Test: none directly (interaction modules aren't unit-tested in this repo — validated at store/relay level; same convention as `SwitchComponentModule`).

**Interfaces:**

- Consumes: `IServiceScopeFactory`, `IRustServerQuery` (for Refresh — `GetStorageContentsAsync`, Task 4), `IEventBus`, `StorageMonitorPairingCoordinator` (Task 9), `IStorageMonitorStore` (Task 6), `StorageMonitorTriggeredEvent` (Task 1), `IStorageMonitorChannel...` not needed here.
- Produces: a public `StorageMonitorComponentModule : InteractionModuleBase<SocketInteractionContext>` handling `storage:accept:*`, `storage:dismiss:*`, `storage:refresh:*`, `storage:rename:*`, `storage:rename:modal:*`.

> Copy `SwitchComponentModule`. Remove On/Off/Strobe. Add a Refresh handler. Accept/Dismiss/Rename are identical (renamed types + ids + modal). The Refresh handler re-reads via `IRustServerQuery.GetStorageContentsAsync` and publishes a `StorageMonitorTriggeredEvent` so the relay re-renders (the relay is the single render path — the module never renders directly).

- [ ] **Step 1: Create the module**

```csharp
using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Features.StorageMonitors.Modules;
using RustPlusBot.Features.StorageMonitors.Pairing;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Persistence.StorageMonitors;

namespace RustPlusBot.Features.StorageMonitors.Modules;

/// <summary>Thin handler for the #storagemonitors pairing prompt + Refresh/Rename. Any guild member.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="query">Live socket read (for Refresh).</param>
/// <param name="eventBus">Publishes a triggered event to drive an embed refresh.</param>
public sealed class StorageMonitorComponentModule(
    IServiceScopeFactory scopeFactory,
    IRustServerQuery query,
    IEventBus eventBus) : InteractionModuleBase<SocketInteractionContext>
{
    private const string InvalidControlMessage = "That control wasn't valid.";

    /// <summary>Accepts a pending pairing prompt and starts managing the monitor.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(StorageMonitorComponentIds.AcceptPrefix + "*")]
    public async Task AcceptAsync(string tail)
    {
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<StorageMonitorPairingCoordinator>();
            var accepted = await coordinator
                .TryAcceptAsync(Context.Guild.Id, serverId, entityId, Context.User.Id, CancellationToken.None)
                .ConfigureAwait(false);
            await FollowupAsync(accepted ? "Storage monitor added." : "That monitor is already managed.",
                ephemeral: true).ConfigureAwait(false);
        }
    }

    /// <summary>Dismisses a pending pairing prompt and removes the transient prompt message.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(StorageMonitorComponentIds.DismissPrefix + "*")]
    public async Task DismissAsync(string tail)
    {
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<StorageMonitorPairingCoordinator>();
            coordinator.TryDismiss(Context.Guild.Id, serverId, entityId);
        }

        await DeletePromptMessageSafeAsync().ConfigureAwait(false);
        await RespondAsync("Dismissed.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Re-reads the monitor's contents and republishes them so the embed refreshes.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(StorageMonitorComponentIds.RefreshPrefix + "*")]
    public async Task RefreshAsync(string tail)
    {
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var contents = await query
            .GetStorageContentsAsync(Context.Guild.Id, serverId, entityId, CancellationToken.None)
            .ConfigureAwait(false);
        if (contents is null)
        {
            await FollowupAsync("Storage monitor is unreachable right now.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await eventBus
            .PublishAsync(new StorageMonitorTriggeredEvent(Context.Guild.Id, serverId, entityId, contents))
            .ConfigureAwait(false);
        await FollowupAsync("Refreshed.", ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Opens the rename modal, carrying the target tail in the modal custom id.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    [ComponentInteraction(StorageMonitorComponentIds.RenamePrefix + "*")]
    public async Task RenamePromptAsync(string tail)
    {
        if (!TryParse(tail, out _, out _) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await RespondWithModalAsync<StorageMonitorRenameModal>(StorageMonitorComponentIds.RenameModalPrefix + tail)
            .ConfigureAwait(false);
    }

    /// <summary>Persists the new name, then republishes current contents so the embed refreshes.</summary>
    /// <param name="tail">The "{serverId}:{entityId}" custom-id tail.</param>
    /// <param name="modal">The submitted rename modal.</param>
    [ModalInteraction(StorageMonitorComponentIds.RenameModalPrefix + "*")]
    public async Task RenameSubmitAsync(string tail, StorageMonitorRenameModal modal)
    {
        ArgumentNullException.ThrowIfNull(modal);
        if (!TryParse(tail, out var serverId, out var entityId) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var name = string.IsNullOrWhiteSpace(modal.Name)
            ? "Storage Monitor " + entityId.ToString(CultureInfo.InvariantCulture)
            : modal.Name.Trim();
        await DeferAsync(ephemeral: true).ConfigureAwait(false);

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
            await store.RenameAsync(Context.Guild.Id, serverId, entityId, name, CancellationToken.None)
                .ConfigureAwait(false);
        }

        // Re-read (may be null if unreachable) and republish so the relay re-renders the renamed embed.
        var contents = await query
            .GetStorageContentsAsync(Context.Guild.Id, serverId, entityId, CancellationToken.None)
            .ConfigureAwait(false);
        if (contents is not null)
        {
            await eventBus
                .PublishAsync(new StorageMonitorTriggeredEvent(Context.Guild.Id, serverId, entityId, contents))
                .ConfigureAwait(false);
        }

        await FollowupAsync("Renamed.", ephemeral: true).ConfigureAwait(false);
    }

    private static bool TryParse(string tail, out Guid serverId, out ulong entityId)
    {
        serverId = Guid.Empty;
        entityId = 0UL;
        if (tail is null)
        {
            return false;
        }

        var parts = tail.Split(':');
        return parts.Length == 2
               && Guid.TryParse(parts[0], out serverId)
               && ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out entityId);
    }

    private async Task DeletePromptMessageSafeAsync()
    {
        try
        {
            if (Context.Interaction is IComponentInteraction component)
            {
                await component.Message.DeleteAsync().ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Best-effort prompt cleanup; a delete failure is non-fatal.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _ = ex;
        }
    }
}
```

> Drop the unused `RustPlusBot.Features.StorageMonitors.Modules` self-import if the analyzer flags it; keep only the usings actually referenced. The `RenameModalPrefix` is `storage:rename:modal:` — note the rename button uses `storage:rename:` and the modal `storage:rename:modal:`; the Switch module proves this prefix pair routes correctly.

- [ ] **Step 2: Build the project**

Run: `dotnet build src/RustPlusBot.Features.StorageMonitors -warnaserror`
Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Features.StorageMonitors
git commit -m "feat(storage): component module (accept/dismiss/refresh/rename)"
```

---

## Task 13: Hosted service + DI extension + host wiring

**Files:**

- Create: `src/RustPlusBot.Features.StorageMonitors/Hosting/StorageMonitorsHostedService.cs`
- Create: `src/RustPlusBot.Features.StorageMonitors/StorageMonitorServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Host/Program.cs:81` (add `AddStorageMonitors()`)
- Test: `tests/RustPlusBot.Features.StorageMonitors.Tests/StorageMonitorRegistrationTests.cs` (DI composes with `validateScopes: true`)

**Interfaces:**

- Consumes: everything above.
- Produces: `AddStorageMonitors(this IServiceCollection) -> IServiceCollection`; `StorageMonitorsHostedService` running three bus loops (`StorageMonitorPairedEvent` → coordinator, `StorageMonitorTriggeredEvent` → relay, `ConnectionStatusChangedEvent` → relay).

> Copy `SwitchesHostedService`, renaming the loops: drop the `SwitchStateChangedEvent` loop (there is no separate state event); keep three loops — paired, triggered (storage), connection-status. Copy `SwitchServiceCollectionExtensions`, adding `services.AddSingleton<IItemNameResolver, EmbeddedItemNameResolver>();` and registering the poster, renderer, coordinator, relay, hosted service, and the `InteractionModuleAssembly`.

- [ ] **Step 1: Create `StorageMonitorsHostedService.cs`**

Copy `SwitchesHostedService.cs`. Keep loops: `ConsumePairedAsync` (`StorageMonitorPairedEvent` → `coordinator.HandlePairedAsync`), `ConsumeTriggeredAsync` (`StorageMonitorTriggeredEvent` → `relay.HandleTriggeredAsync`), `ConsumeStatusAsync` (`ConnectionStatusChangedEvent` → `relay.HandleConnectionStatusAsync`). Remove the `ConsumeStateAsync`/`SwitchStateChangedEvent` loop. Rename the logger messages. Ctor deps: `IEventBus`, `StorageMonitorPairingCoordinator`, `StorageMonitorStateRelay`, `ILogger<StorageMonitorsHostedService>`.

- [ ] **Step 2: Create `StorageMonitorServiceCollectionExtensions.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.StorageMonitors.Hosting;
using RustPlusBot.Features.StorageMonitors.Naming;
using RustPlusBot.Features.StorageMonitors.Pairing;
using RustPlusBot.Features.StorageMonitors.Posting;
using RustPlusBot.Features.StorageMonitors.Relaying;
using RustPlusBot.Features.StorageMonitors.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.StorageMonitors;

/// <summary>DI registration for the Smart Storage Monitors feature.</summary>
public static class StorageMonitorServiceCollectionExtensions
{
    /// <summary>Registers the item-name resolver, renderer, poster, coordinator, relay, modules, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddStorageMonitors(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRustPlusBotLocalization();
        services.AddSingleton<IItemNameResolver, EmbeddedItemNameResolver>();
        services.AddSingleton<StorageMonitorEmbedRenderer>();
        services.AddSingleton<IStorageMonitorChannelPoster, DiscordStorageMonitorChannelPoster>();
        services.AddSingleton<StorageMonitorPairingCoordinator>();
        services.AddSingleton<StorageMonitorStateRelay>();
        services.AddHostedService<StorageMonitorsHostedService>();

        services.AddSingleton(new InteractionModuleAssembly(
            typeof(StorageMonitorServiceCollectionExtensions).Assembly));

        return services;
    }
}
```

> `IStorageMonitorStore` is registered in Persistence DI (where `ISwitchStore` is registered). Add `services.AddScoped<IStorageMonitorStore, StorageMonitorStore>();` there — find the `ISwitchStore` registration (search `grep -rn "ISwitchStore" src --include=*.cs | grep -i add`) and mirror it. Do this as part of this step.

- [ ] **Step 3: Wire the host**

In `src/RustPlusBot.Host/Program.cs`, after line 81 (`builder.Services.AddAlarms();`):

```csharp
builder.Services.AddStorageMonitors();
```

Add `using RustPlusBot.Features.StorageMonitors;` and a `<ProjectReference>` from `RustPlusBot.Host.csproj` to the new project.

- [ ] **Step 4: Write the registration test**

```csharp
[Fact]
public void AddStorageMonitors_ComposesWithValidatedScopes()
{
    var services = new ServiceCollection();
    // Register the dependencies the feature resolves (mirror SwitchRegistrationTests):
    // DiscordSocketClient stub, IEventBus, IServiceScopeFactory-backed stores, IRustServerQuery,
    // IStorageMonitorChannelLocator, IWorkspaceStore, IConnectionStore, IClock, logging, localization.
    services.AddStorageMonitors();
    // ... register substitutes for the externally-provided services ...

    using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    provider.GetRequiredService<StorageMonitorPairingCoordinator>().Should().NotBeNull();
    provider.GetRequiredService<StorageMonitorStateRelay>().Should().NotBeNull();
    provider.GetRequiredService<IItemNameResolver>().Should().NotBeNull();
}
```

> Mirror `SwitchRegistrationTests` exactly for which substitutes to register; the point is to catch a captive-dependency / missing-registration regression under `ValidateScopes = true`.

- [ ] **Step 5: Run — verify pass**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests -maxcpucount:1`
Expected: PASS.

- [ ] **Step 6: Build the whole solution**

Run: `dotnet build RustPlusBot.slnx -warnaserror`
Expected: clean.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.StorageMonitors src/RustPlusBot.Host src/RustPlusBot.Persistence tests/RustPlusBot.Features.StorageMonitors.Tests
git commit -m "feat(storage): hosted service, DI extension, host wiring"
```

---

## Task 14: Whole-feature verification

**Files:** none (verification only).

- [ ] **Step 1: Full suite, sequential**

Run: `dotnet test RustPlusBot.slnx -maxcpucount:1`
Expected: ALL assemblies green. READ the per-assembly counts: the new `Features.StorageMonitors.Tests` should report its tests; `Connections`, `Pairing`, `Persistence`, `Workspace` counts should be ≥ their previous values (no assembly silently dropped). Record the new total.

- [ ] **Step 2: Format gate (the real CI gate)**

```bash
dotnet tool restore
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git diff --stat
```

Expected: after running jb, `git diff` shows only reorder/whitespace changes in this branch's new/modified files (no pre-existing-file drift). Commit them:

```bash
git add -A
git commit -m "style: jb cleanupcode ReformatAndReorder"
```

Then re-run to confirm idempotence:

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git diff --quiet && echo "CLEAN" || echo "STILL DIRTY"
```

Expected: `CLEAN`.

- [ ] **Step 3: EF model-drift check**

Run: `dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence --startup-project src/RustPlusBot.Host`
Expected: "No changes". If the tool errors ("Unable to retrieve project metadata"), instead verify by inspection that the only migration/model-snapshot changes on the branch are the `SmartStorageMonitors` ones:

```bash
git diff --name-only develop... -- src/RustPlusBot.Persistence/Migrations
```

Expected: only the new `*_SmartStorageMonitors.cs` and the `ModelSnapshot` diff.

- [ ] **Step 4: Sanity — final build**

Run: `dotnet build RustPlusBot.slnx -warnaserror`
Expected: 0 warnings, 0 errors.

- [ ] **Step 5: Commit any residual + summarize**

If steps changed anything, commit. Report: final test total, per-assembly deltas, confirmation the format gate is clean and idempotent, and the untested-by-design shims (`RustPlusSocketSource` storage methods, `DiscordStorageMonitorChannelPoster`).

---

## Self-Review (completed)

**Spec coverage:**

- Pairing → validation → registration: Tasks 2 (route), 9 (coordinator), 12 (module accept/dismiss). ✓
- Live contents embed (type/contents/protection/slots/unreachable): Task 8 (renderer). ✓
- Refresh + Rename: Task 12 (module), buttons in Task 8. ✓
- Lifecycle (prime on connect, live trigger, self-heal): Tasks 3–4 (seam+supervisor), 9 (poster self-heal via `DiscordChannelMessenger`). ✓
- Item-name lookup: Task 7. ✓
- `#storagemonitors` channel + locator: Task 10. ✓
- Domain/persistence/migration: Tasks 5–6. ✓
- Abstractions DTOs/events: Task 1. ✓
- Host wiring + DI + hosted service: Task 13. ✓
- Deferred (recycle/upkeep-cost/alerting/control): not built — confirmed absent. ✓
- EN/FR: Task 8 resx keys. ✓

**Placeholder scan:** All code steps contain full code. The two genuine implementation-time tasks (sourcing `items.json` from the Facepunch manifest; confirming FR wording) are flagged with concrete shape + verification ids, not left as "TBD".

**Type consistency:** `RenderMonitor`(not `RenderSwitch`); `GetStorageMonitorInfoAsync` (seam) vs `GetStorageContentsAsync` (`IRustServerQuery`) used consistently; `StorageMonitorTriggeredEvent` carries `Contents`; `IStorageMonitorStore` has no `UpdateStateAsync`; component-id prefixes consistent (`storage:refresh:`/`rename:`/`rename:modal:`/`accept:`/`dismiss:`).

**Ordering flagged:** Task 4 (supervisor prime) and Task 9 (coordinator) depend on Tasks 6 (store) and 10 (locator) respectively — noted inline with the recommendation to execute persistence (6) and workspace (10) before their consumers. The executor/reviewer should run in the order: 1, 2, 3, 5, 6, 7, 8, 10, 9, 4, 11, 12, 13, 14 — OR keep numeric order and split the dependent arms as noted.
