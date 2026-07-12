# Smart-Device Refactor (RustPlusApi beta.3) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bump RustPlusApi/Fcm to 2.0.0-beta.3 and reshape the switch in-game-trigger path into a device-agnostic `SmartDeviceTriggeredEvent`, with switches behaving exactly as before.

**Architecture:** A pure refactor on `feat/smart-device-refactor` off `develop`. The Connections seam's `SmartSwitchTriggered (EventHandler<ulong>)` becomes `SmartDeviceTriggered (EventHandler<SmartDeviceTrigger>)` carrying `(EntityId, IsActive)`; the supervisor publishes a new Abstractions `SmartDeviceTriggeredEvent`; the switch relay consumes it filtered to switch ids via `ISwitchStore.ExistsAsync`. The existing `SwitchStateChangedEvent` stays for the switch UI module's user-action refreshes.

**Tech Stack:** .NET 10, C#, RustPlusApi/RustPlusApi.Fcm 2.0.0-beta.3, EF Core+SQLite (unchanged), Discord.Net, NSubstitute + xUnit, Roslynator + ReSharper (jb) analyzers.

## Global Constraints

- **Branch:** `feat/smart-device-refactor` off `develop` (already cut at `4f89eef`; plain branch, no worktree).
- **Package bump:** `RustPlusApi` + `RustPlusApi.Fcm` → **`2.0.0-beta.3`** in `Directory.Packages.props` (already edited in the working tree; Task 1 commits it).
- **Solution file is `RustPlusBot.slnx`**.
- **Build gate:** `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` = 0 warnings / 0 errors (strict Roslynator).
- **Test gate:** `dotnet test RustPlusBot.slnx -maxcpucount:1`; **read per-assembly counts** — a fake that doesn't implement a renamed member compiles-but-drops a whole assembly's tests silently.
- **Format gate:** `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` before push (the real format gate; pre-push hook enforces it).
- **NO new migration, NO entity/schema change, NO EF drift.** This is a code/seam refactor only.
- **Behaviour-preservation is the acceptance bar:** switches work exactly as before (same embeds, ON/OFF/Strobe/Rename, connect-priming, live updates).
- New events live in **Abstractions** (no project refs, no Discord, no FCM).
- The repo's `RustPlusBot.Discord` namespace shadows Discord.Net's `Discord` (irrelevant here — Connections has no Discord types).
- `docs/superpowers/` is **gitignored / local-only** — never `git add` the spec/plan.

## Verified beta.3 API (against the restored 2.0.0-beta.3 DLL)

- Event: `RustPlus.OnSmartDeviceTriggered` (was `OnSmartSwitchTriggered`), arg `RustPlusApi.Data.Events.SmartDeviceEventArg` (was `SmartSwitchEventArg`), which **inherits `SmartDeviceInfo`** so it carries `ulong Id` + `bool IsActive`.
- Type: `RustPlusApi.Data.Entities.SmartDeviceInfo { bool IsActive }` (replaces `SmartSwitchInfo` + `AlarmInfo`).
- `RustPlus.GetSmartSwitchInfoAsync` / `GetAlarmInfoAsync` / `SetSmartSwitchValueAsync` / `StrobeSmartSwitchAsync` all return `Response<SmartDeviceInfo?>`.
- FCM: `AlarmEvent` → `AlarmNotification`; `OnAlarmTriggered` → `EventHandler<AlarmNotification?>` (NOT consumed here).

---

## File Structure

**Abstractions:**

- Create `src/RustPlusBot.Abstractions/Events/SmartDeviceTriggeredEvent.cs`.

**Connections (the rename + reshape):**

- `src/RustPlusBot.Features.Connections/Listening/SmartDeviceTrigger.cs` — new internal carrier record.
- `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs` — rename event + `GetSmartSwitchInfoAsync`→`GetSmartDeviceInfoAsync`.
- `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` — beta.3 retypes + event rename.
- `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` — `OnSmartDevice`, `PrimeDevicesAsync`, publish `SmartDeviceTriggeredEvent`.

**Connections tests:**

- `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` — rename the fake's event + raise helper + `GetSmartDeviceInfoAsync`.
- `tests/RustPlusBot.Features.Connections.Tests/SwitchQueryTests.cs` — retarget to `SmartDeviceTriggeredEvent`.

**Switches feature:**

- `src/RustPlusBot.Features.Switches/Relaying/SwitchStateRelay.cs` — new `SmartDeviceTriggeredEvent` handler (filtered by `ExistsAsync`).
- `src/RustPlusBot.Features.Switches/Hosting/SwitchesHostedService.cs` — 4th bus loop.
- `tests/RustPlusBot.Features.Switches.Tests/SwitchStateRelayTests.cs` — new-path test.

**Abstractions tests:**

- `tests/RustPlusBot.Abstractions.Tests/SmartDeviceTriggeredEventTests.cs`.

---

## Task 1: Bump to beta.3 + `SmartDeviceTriggeredEvent` (Abstractions)

This task bumps the package and adds the new event. The solution will NOT fully build yet (Connections still references the old `SmartSwitchEventArg`) — that's expected; Task 2 fixes Connections. To keep this task independently committable, build + test only the **Abstractions** project here.

**Files:**

- Modify: `Directory.Packages.props` (already edited in tree — just commit it with this task)
- Create: `src/RustPlusBot.Abstractions/Events/SmartDeviceTriggeredEvent.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/SmartDeviceTriggeredEventTests.cs`

**Interfaces:**

- Produces: `public sealed record SmartDeviceTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive)`.

- [ ] **Step 1: Confirm the package bump is in the working tree** — `grep beta.3 Directory.Packages.props` should show both `RustPlusApi` and `RustPlusApi.Fcm` at `2.0.0-beta.3`. If not, set them. Then `dotnet restore RustPlusBot.slnx`.

- [ ] **Step 2: Write the failing test** (copy the shape of `tests/RustPlusBot.Abstractions.Tests/AlarmEventsTests.cs` if present, else `SwitchEventsTests.cs`):

```csharp
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests;

public sealed class SmartDeviceTriggeredEventTests
{
    [Fact]
    public void CarriesIdentityAndState()
    {
        var e = new SmartDeviceTriggeredEvent(10UL, Guid.Empty, 42UL, IsActive: true);
        Assert.Equal(10UL, e.GuildId);
        Assert.Equal(42UL, e.EntityId);
        Assert.True(e.IsActive);
    }
}
```

- [ ] **Step 3: Run to verify it fails** — `dotnet test tests/RustPlusBot.Abstractions.Tests/RustPlusBot.Abstractions.Tests.csproj -maxcpucount:1` → FAIL (type missing).

- [ ] **Step 4: Create the record** (mirror `SwitchStateChangedEvent`'s doc style — read it):

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>A managed smart device (switch/alarm/…) changed state in-game, as reported by the socket broadcast.</summary>
/// <param name="GuildId">The owning Discord guild snowflake.</param>
/// <param name="ServerId">The local Rust server id.</param>
/// <param name="EntityId">The in-game entity id (the discriminant — features filter to the ids they manage).</param>
/// <param name="IsActive">The new on/off state, carried directly on the broadcast (no re-read).</param>
public sealed record SmartDeviceTriggeredEvent(ulong GuildId, Guid ServerId, ulong EntityId, bool IsActive);
```

- [ ] **Step 5: Run to verify it passes** — same command → PASS.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props \
        src/RustPlusBot.Abstractions/Events/SmartDeviceTriggeredEvent.cs \
        tests/RustPlusBot.Abstractions.Tests/SmartDeviceTriggeredEventTests.cs
git commit -m "feat(connections): bump RustPlusApi to beta.3 and add SmartDeviceTriggeredEvent"
```

---

## Task 2: Connections seam + socket shim → device-generic (beta.3)

Reshape the internal seam and the untested socket shim for beta.3. After this task the **Connections project** builds on beta.3; the supervisor still references the old member names (fixed in Task 3) — so build only what compiles incrementally and rely on Task 3 to green the whole project. To keep this committable, do Tasks 2+3 as one continuous build target: **defer the full Connections build assertion to Task 3** and here just confirm the two edited files are internally consistent (the supervisor edit in Task 3 will reference the new names).

> Because the seam rename and the supervisor edits are mutually dependent (the supervisor subscribes to the renamed event and calls the renamed method), Tasks 2 and 3 cannot each independently produce a green Connections build. Treat them as a pair: Task 2 edits the seam + shim, Task 3 edits the supervisor + tests, and the green-build gate lands at the end of Task 3. Commit Task 2 with `--no-verify`-free but accept that `dotnet build` of Connections fails until Task 3. (The reviewer is told this.)

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Listening/SmartDeviceTrigger.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`

**Interfaces:**

- Produces:
  - `internal sealed record SmartDeviceTrigger(ulong EntityId, bool IsActive)` in `RustPlusBot.Features.Connections.Listening`.
  - `IRustServerConnection`: `event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered` (replaces `event EventHandler<ulong>? SmartSwitchTriggered`); `Task<bool?> GetSmartDeviceInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken)` (replaces `GetSmartSwitchInfoAsync`). `SetSmartSwitchValueAsync` / `StrobeSmartSwitchAsync` keep their names.

- [ ] **Step 1: Create `SmartDeviceTrigger`**

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>An in-game device-state broadcast: the entity id and its new on/off state.</summary>
/// <param name="EntityId">The in-game entity id.</param>
/// <param name="IsActive">The new on/off state carried on the broadcast.</param>
internal sealed record SmartDeviceTrigger(ulong EntityId, bool IsActive);
```

- [ ] **Step 2: Edit `IRustServerConnection.cs`** — rename the event (line ~110-111) and the read method (line ~50-55):

Replace the `SmartSwitchTriggered` event:

```csharp
    /// <summary>Raised when a managed smart device's state changes in-game; carries the entity id and new state.</summary>
    event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered;
```

Replace `GetSmartSwitchInfoAsync` with the generic name (same signature/semantics, doc generalized):

```csharp
    /// <summary>Reads a smart device's on/off state, or null on failure/timeout. Also primes the socket's interest in the entity (so triggers fire for it thereafter).</summary>
    /// <param name="entityId">The in-game entity id (switch or alarm).</param>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>True/false for on/off, or null on failure/timeout.</returns>
    Task<bool?> GetSmartDeviceInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken cancellationToken);
```

Leave `SetSmartSwitchValueAsync` and `StrobeSmartSwitchAsync` unchanged.

- [ ] **Step 3: Edit `RustPlusSocketSource.cs`:**

(a) Constructor (line ~119) and DisposeAsync (line ~559): rename the package event subscription.

```csharp
            _rustPlus.OnSmartDeviceTriggered += OnSmartDeviceTriggered;   // ctor
```

```csharp
            _rustPlus.OnSmartDeviceTriggered -= OnSmartDeviceTriggered;   // DisposeAsync
```

(b) Rename our event field (line ~348):

```csharp
        public event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered;
```

(c) Rename the handler (line ~574-575) to read both Id and IsActive off the beta.3 arg:

```csharp
        private void OnSmartDeviceTriggered(object? sender, RustPlusApi.Data.Events.SmartDeviceEventArg e) =>
            SmartDeviceTriggered?.Invoke(this, new SmartDeviceTrigger(e.Id, e.IsActive));
```

(d) Rename `GetSmartSwitchInfoAsync` → `GetSmartDeviceInfoAsync` (line ~350) and update the comment to reference `SmartDeviceInfo`/`OnSmartDeviceTriggered`; the body is unchanged except the package call `_rustPlus.GetSmartSwitchInfoAsync(entityId, timeoutCts.Token)` stays (the package method keeps that name and now returns `Response<SmartDeviceInfo?>`; `info.IsActive` still works since `SmartDeviceInfo` has `IsActive`). So only the method name, doc, and comment change — the `response.IsSuccess && response.Data is { } info ? info.IsActive : null` line compiles as-is against `SmartDeviceInfo`.

(e) `SetSmartSwitchValueAsync` / `StrobeSmartSwitchAsync` bodies: the package calls keep their names and now return `Response<SmartDeviceInfo?>`; the code only reads `response.IsSuccess`, so no change needed beyond updating the stale `SmartSwitchInfo` comments to `SmartDeviceInfo`.

- [ ] **Step 4: Build the Connections project to surface remaining errors** — `dotnet build src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj -warnaserror -maxcpucount:1`. Expect errors ONLY from `ConnectionSupervisor.cs` (it still says `connection.SmartSwitchTriggered` and `GetSmartSwitchInfoAsync` and publishes `SwitchStateChangedEvent`) — those are Task 3. The seam + shim files themselves must be error-free. If any error is in `IRustServerConnection.cs` or `RustPlusSocketSource.cs`, fix it here.

- [ ] **Step 5: Commit** (Connections won't fully build yet — that's expected, green-build gate is Task 3)

```bash
git add src/RustPlusBot.Features.Connections/Listening/
git commit -m "refactor(connections): rename SmartSwitchTriggered seam to device-generic SmartDeviceTriggered (beta.3)"
```

---

## Task 3: Supervisor publishes `SmartDeviceTriggeredEvent` + fake + supervisor test

Complete the Connections reshape: the supervisor subscribes the renamed event, publishes the new Abstractions event (using the IsActive on the trigger arg — no re-read on the trigger path), renames `PrimeSwitchesAsync`→`PrimeDevicesAsync` (the prime path still re-reads via `GetSmartDeviceInfoAsync`), and the fake + supervisor test retarget. This task lands the **green Connections build + tests**.

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/SwitchQueryTests.cs`

**Interfaces:**

- Consumes: `SmartDeviceTrigger`, `SmartDeviceTriggered` event, `GetSmartDeviceInfoAsync` (Task 2); `SmartDeviceTriggeredEvent` (Task 1).
- Produces: supervisor publishes `SmartDeviceTriggeredEvent` on both trigger and prime; the fake raises `SmartDeviceTriggered(SmartDeviceTrigger)` via `RaiseSmartDeviceTriggered(ulong entityId, bool isActive)`.

- [ ] **Step 1: Edit the fake (`FakeRustSocketSource.cs`)** — rename the event + raise helper + read method so the test double matches the renamed seam:

Event (line ~155):

```csharp
        /// <summary>Raised by <see cref="RaiseSmartDeviceTriggered"/>.</summary>
        public event EventHandler<SmartDeviceTrigger>? SmartDeviceTriggered;
```

Read method (line ~187) — rename `GetSmartSwitchInfoAsync` → `GetSmartDeviceInfoAsync` (signature + body unchanged; it returns the `SwitchStates` lookup).
Raise helper (line ~262):

```csharp
        public void RaiseSmartDeviceTriggered(ulong entityId, bool isActive) =>
            SmartDeviceTriggered?.Invoke(this, new SmartDeviceTrigger(entityId, isActive));
```

(Keep the `SwitchStates` dictionary name — it's the prime-path re-read source; only the trigger path now carries IsActive directly.)

- [ ] **Step 2: Edit `ConnectionSupervisor.cs`:**

(a) Trigger handler (line ~470-476) — rename + carry IsActive:

```csharp
#pragma warning disable RCS1163 // Unused 'sender': required by the EventHandler<SmartDeviceTrigger> delegate shape.
        void OnSmartDevice(object? sender, SmartDeviceTrigger trigger)
        {
            // Fire-and-forget: PublishDeviceTriggerAsync catches everything internally, so the discarded task never
            // surfaces an unobserved exception. Device triggers are low-volume, so unbounded concurrency is fine.
            _ = PublishDeviceTriggerAsync(key, trigger);
        }
#pragma warning restore RCS1163
```

(b) Subscribe/unsubscribe (lines ~481, ~524): `connection.SmartDeviceTriggered += OnSmartDevice;` / `-= OnSmartDevice;`.

(c) Prime call (line ~483): `await PrimeDevicesAsync(key, connection, ct).ConfigureAwait(false);`.

(d) Rename `PrimeSwitchesAsync` → `PrimeDevicesAsync` (line ~802) — body unchanged except it calls `PublishDevicePrimeAsync` (below) instead of `PublishSwitchStateAsync`; keep the `ISwitchStore.ListByServerAsync` (this branch primes switches only). Keep the two log methods or rename to `LogDeviceListFailed`/`LogDevicePrimeFailed` (rename for clarity — update the `[LoggerMessage]` partials).

(e) Replace `PublishSwitchStateAsync` (line ~852) with TWO methods:

```csharp
// Trigger path: IsActive is carried on the broadcast arg — no re-read.
private async Task PublishDeviceTriggerAsync(
    (ulong Guild, Guid Server) key,
    SmartDeviceTrigger trigger)
{
    if (_disposed)
    {
        return;
    }

    try
    {
        await eventBus.PublishAsync(
                new SmartDeviceTriggeredEvent(key.Guild, key.Server, trigger.EntityId, trigger.IsActive),
                _shutdown.Token)
            .ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        // Shutting down.
    }
#pragma warning disable CA1031 // Broad catch: a device publish failure must not crash the socket callback.
    catch (Exception ex)
#pragma warning restore CA1031
    {
        LogDevicePublishFailed(logger, ex, trigger.EntityId, key.Server);
    }
}

// Prime path: read state on connect (also primes the socket's interest), then publish.
private async Task PublishDevicePrimeAsync(
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
        var isActive = await connection
            .GetSmartDeviceInfoAsync(entityId, _options.HeartbeatTimeout, _shutdown.Token)
            .ConfigureAwait(false);
        await eventBus.PublishAsync(
                new SmartDeviceTriggeredEvent(key.Guild, key.Server, entityId, isActive ?? false),
                _shutdown.Token)
            .ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        // Shutting down.
    }
#pragma warning disable CA1031 // Broad catch: a device prime failure must not crash the socket callback.
    catch (Exception ex)
#pragma warning restore CA1031
    {
        LogDevicePublishFailed(logger, ex, entityId, key.Server);
    }
}
```

Update `PrimeDevicesAsync`'s inner call from `PublishSwitchStateAsync(key, connection, sw.EntityId)` to `PublishDevicePrimeAsync(key, connection, sw.EntityId)`. Reconcile the `[LoggerMessage]` partial(s): replace `LogSwitchPublishFailed`/whatever the old name was with `LogDevicePublishFailed` (and the list/prime log names if you renamed them). Keep `using RustPlusBot.Abstractions.Events;` (already present for `SwitchStateChangedEvent`); `SwitchStateChangedEvent` is no longer referenced by the supervisor after this — remove the now-unused publish, and if no other supervisor code uses it, the `using` stays valid for other Abstractions events (verify no unused-using warning; remove only if the analyzer flags it).

- [ ] **Step 3: Retarget the supervisor tests (`SwitchQueryTests.cs`):**

The two tests that assert switch-trigger/prime behavior now assert `SmartDeviceTriggeredEvent`:

- `Trigger_publishes_state_change` (line ~169): change the subscribed type to `SmartDeviceTriggeredEvent`; **the trigger now carries IsActive directly** — replace `source.LastConnection!.SwitchStates[42UL] = true;` + `RaiseSmartSwitchTriggered(42UL)` with `source.LastConnection!.RaiseSmartDeviceTriggered(42UL, isActive: true);` (no `SwitchStates` needed for the trigger path); assert `evt.IsActive` is true.
- The prime test (the one around line 160, asserting `evt.IsActive == false` from an absent entity) → subscribe `SmartDeviceTriggeredEvent`; the prime path still re-reads via `GetSmartDeviceInfoAsync` (absent → null → false), so the assertion stays `Assert.False(evt.IsActive)`.

Update the `using`/event type names and any `SwitchStateChangedEvent` references in this file to `SmartDeviceTriggeredEvent`. (If the file name `SwitchQueryTests` no longer fits, leave it — renaming a test file is out of scope; the switch-query *methods* `GetSmartSwitchStateAsync`/`SetSmartSwitchAsync`/`StrobeSmartSwitchAsync` on `IRustServerQuery` are unchanged and still tested here.)

- [ ] **Step 4: Build the whole solution strict** — `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` → 0/0. (Connections + Switches both green now; the Switches relay still compiles because `SwitchStateChangedEvent` is untouched — the supervisor simply stopped publishing it, which is a runtime behavior change handled in Task 4. **Switch live-updates are temporarily broken between Task 3 and Task 4** — that's why Task 4 immediately follows.)

- [ ] **Step 5: Run the Connections + Abstractions tests** — `dotnet test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj -maxcpucount:1` and the Abstractions test project → PASS. **Read the Connections per-assembly count** — it must not have dropped (a missed fake rename would silently drop it).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs \
        tests/RustPlusBot.Features.Connections.Tests/
git commit -m "refactor(connections): supervisor publishes SmartDeviceTriggeredEvent (trigger carries state, prime re-reads)"
```

---

## Task 4: Switch relay consumes `SmartDeviceTriggeredEvent` (restore live updates)

Wire the switch feature onto the new event so in-game switch triggers + connect-priming refresh embeds again. The switch relay gains a handler that filters to switch ids via `ISwitchStore.ExistsAsync`; the hosted service gains a 4th bus loop. The module's `SwitchStateChangedEvent` publishes (user actions) stay.

**Files:**

- Modify: `src/RustPlusBot.Features.Switches/Relaying/SwitchStateRelay.cs`
- Modify: `src/RustPlusBot.Features.Switches/Hosting/SwitchesHostedService.cs`
- Test: `tests/RustPlusBot.Features.Switches.Tests/SwitchStateRelayTests.cs`

**Interfaces:**

- Consumes: `SmartDeviceTriggeredEvent` (Abstractions); `ISwitchStore.ExistsAsync` / `GetAsync` / `UpdateStateAsync`.
- Produces: `SwitchStateRelay.HandleDeviceTriggeredAsync(SmartDeviceTriggeredEvent, CancellationToken)`.

- [ ] **Step 1: Write the failing relay tests** (in `SwitchStateRelayTests.cs`, model on the existing `HandleStateChangedAsync` tests):
  - a `SmartDeviceTriggeredEvent` for a **managed switch id** (`ExistsAsync` true) → persists `UpdateStateAsync(isActive)` + re-renders the embed (poster `EnsureAsync` called).
  - a `SmartDeviceTriggeredEvent` for a **non-switch id** (`ExistsAsync` false) → no store write, no render (ignored).

Use the existing test harness/substitutes in this file (copy how `HandleStateChangedAsync` tests build the relay + fakes).

- [ ] **Step 2: Run to verify it fails** — `dotnet test tests/RustPlusBot.Features.Switches.Tests/RustPlusBot.Features.Switches.Tests.csproj -maxcpucount:1` → FAIL (`HandleDeviceTriggeredAsync` missing).

- [ ] **Step 3: Add `HandleDeviceTriggeredAsync` to `SwitchStateRelay`:**

```csharp
/// <summary>Handles an in-game device trigger: ignore ids this relay doesn't manage, else persist + re-render.</summary>
/// <param name="evt">The device-triggered event.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>A task that completes when the embed has been re-rendered (or the id was ignored).</returns>
public async Task HandleDeviceTriggeredAsync(SmartDeviceTriggeredEvent evt, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(evt);
    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
        if (!await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false))
        {
            return; // not a switch this relay manages (e.g. an alarm) — ignore.
        }

        await store.UpdateStateAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.IsActive, cancellationToken)
            .ConfigureAwait(false);
        var sw = await store.GetAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken)
            .ConfigureAwait(false);
        if (sw is null)
        {
            return;
        }

        var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken).ConfigureAwait(false);
        await RenderAsync(store, sw, evt.IsActive, evt.GuildId, evt.ServerId, culture, cancellationToken)
            .ConfigureAwait(false);
    }
}
```

> Read the existing `HandleStateChangedAsync` + `RenderAsync` + `GetCultureAsync` helpers in this file and reuse them exactly (the body above mirrors `HandleStateChangedAsync` plus the `ExistsAsync` guard). Add `using RustPlusBot.Abstractions.Events;` if not already present.

- [ ] **Step 4: Run to verify the relay tests pass** — same command → PASS.

- [ ] **Step 5: Add the 4th bus loop to `SwitchesHostedService`:**

Add a `_deviceLoop` field; start it in `StartAsync`; join it in `StopAsync` (add to the array); add the consume method mirroring `ConsumeStateAsync`:

```csharp
private async Task ConsumeDeviceTriggeredAsync(CancellationToken cancellationToken)
{
    try
    {
        await foreach (var evt in eventBus.SubscribeAsync<SmartDeviceTriggeredEvent>(cancellationToken)
                           .ConfigureAwait(false))
        {
            await relay.HandleDeviceTriggeredAsync(evt, cancellationToken).ConfigureAwait(false);
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
        LogDeviceLoopFaulted(logger, ex);
    }
}

[LoggerMessage(Level = LogLevel.Error, Message = "Switch device-triggered relay loop faulted.")]
private static partial void LogDeviceLoopFaulted(ILogger logger, Exception exception);
```

Wire `_deviceLoop = Task.Run(() => ConsumeDeviceTriggeredAsync(_cts.Token), CancellationToken.None);` in `StartAsync` and add `_deviceLoop` to the `StopAsync` join array. Add `using RustPlusBot.Abstractions.Events;` if needed.

- [ ] **Step 6: Full solution build + Switches tests** — `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` (0/0); `dotnet test tests/RustPlusBot.Features.Switches.Tests/RustPlusBot.Features.Switches.Tests.csproj -maxcpucount:1` (PASS, count not dropped).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Switches/Relaying/SwitchStateRelay.cs \
        src/RustPlusBot.Features.Switches/Hosting/SwitchesHostedService.cs \
        tests/RustPlusBot.Features.Switches.Tests/SwitchStateRelayTests.cs
git commit -m "feat(switches): consume SmartDeviceTriggeredEvent (filtered by ExistsAsync) for live updates"
```

---

## Task 5: Full verification + format gate

**Files:** none (verification only).

- [ ] **Step 1: Full suite** — `dotnet test RustPlusBot.slnx -maxcpucount:1`. Expected: all assemblies green; **read per-assembly counts** vs. the pre-refactor baseline (no assembly dropped; Connections/Switches/Abstractions counts ≥ baseline, allowing for the retargeted/added tests). If any assembly shows 0 or is missing, a fake didn't implement a renamed member — fix before proceeding.

- [ ] **Step 2: Strict build** — `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1` → 0/0.

- [ ] **Step 3: Format gate** — `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`. Review the diff (expect only refactor-touched files; jb may reorder members Roslynator doesn't flag). If it changed files, commit:

```bash
git add -A
git commit -m "style: jb cleanupcode reformat (smart-device refactor)"
```

- [ ] **Step 4: Confirm no EF drift** — `git diff --stat develop -- src/RustPlusBot.Persistence/Migrations` is EMPTY (this refactor adds no migration).

- [ ] **Step 5: Behaviour sanity (manual reasoning, recorded)** — confirm the end-to-end switch path is intact: socket `OnSmartDeviceTriggered` → `SmartDeviceTrigger` → supervisor `SmartDeviceTriggeredEvent` → switch relay (`ExistsAsync` true) → embed re-render; connect → `PrimeDevicesAsync` → `GetSmartDeviceInfoAsync` → `SmartDeviceTriggeredEvent` → re-render; user ON/OFF/Strobe/Rename → module `SwitchStateChangedEvent` → relay re-render. Note this in the report.

- [ ] **Step 6: Confirm `docs/superpowers/` is not staged** — `git status` shows no spec/plan files.

---

## Final verification (whole branch)

- [ ] Full suite green, per-assembly counts checked (no drop): `dotnet test RustPlusBot.slnx -maxcpucount:1`
- [ ] Strict build 0/0: `dotnet build RustPlusBot.slnx -warnaserror -maxcpucount:1`
- [ ] jb-clean
- [ ] No EF drift (no migration added)
- [ ] Switch behaviour preserved (the three paths above)
- [ ] `docs/superpowers/` not staged
- [ ] Open PR `feat/smart-device-refactor` → `develop` (only when the user asks); then rebase `feat/smart-alarms` onto this branch and re-plan the alarm slice on the new model

```
