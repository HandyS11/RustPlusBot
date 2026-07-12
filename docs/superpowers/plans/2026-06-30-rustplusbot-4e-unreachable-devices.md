# Subsystem 4e — Per-Device Unreachable Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the three managed smart-device types (switches, alarms, storage monitors) a persisted, three-way per-device reachability status (Reachable / Removed / NoPrivilege / NoResponse) surfaced inline on each device embed, detected from connect-prime, a periodic poll, and (switches) actuation failure.

**Architecture:** A `DeviceReachability` enum + a device-agnostic `DeviceReachabilityChangedEvent` (Approach A). The `RustPlusErrorCode → DeviceReachability` mapping lives only in `RustPlusSocketSource` (via a pure `ReachabilityMapping` helper). Device reads return payload + reachability in one round-trip; the supervisor's prime path and a new `PollReachabilityAsync` loop publish the event on change; each feature relay persists it and re-renders; the switch interaction surfaces the reason on actuation.

**Tech Stack:** .NET 10, C#, EF Core (SQLite), Discord.Net, xUnit, RustPlusApi 2.0.0-beta.3, the in-process `IEventBus`, `ILocalizer`/`Strings.resx`.

## Global Constraints

- Solution file is `RustPlusBot.slnx` (no `.sln`). **Always pass `-maxcpucount:1` on BOTH build and test** (`dotnet build RustPlusBot.slnx -maxcpucount:1`, `dotnet test RustPlusBot.slnx -maxcpucount:1`) — the parallel build races on `.git/config` hooksPath. When running a single project's tests, also read the per-assembly counts: a broken build silently DROPS an assembly's tests, so a lower-than-expected total is a build break, not a pass.
- Build is `-warnaserror` with zero analyzer warnings. New **public** types need XML doc comments; prefer `internal` where possible (CA1515). Known nits: CA1305/CA1307/CA1310 (`InvariantCulture` / `StringComparison.Ordinal`), S1135, CA1031 (justify broad catches with the existing `#pragma` + comment pattern).
- Tests are plain xUnit `Assert.*` (+ NSubstitute where a fake is needed). **No FluentAssertions.**
- `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` is a **hard CI gate** that fails on any diff. Run `dotnet tool restore` first (jb/ef/stryker/docfx are local tools). Run it before the final commit; commit any reformatting it produces.
- **Localization parity tripwire:** `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs` asserts `Assert.Equal(<N>, EnglishKeys().Count)` and that EN/FR key sets are identical. Baseline **248**. This slice adds **12** keys across Tasks 7–10 (3 each), walking the count **248 → 251 → 254 → 257 → 260**. Each resx-touching task adds its 3 keys to BOTH `Strings.resx` and `Strings.fr.resx` AND bumps that count in the same commit.
- EN **and** FR strings for every user-facing string; never hardcode UI text — resolve through `ILocalizer.Get(key, culture)`. Game data (device names) stays English.
- Per-guild / per-server isolation on every Rust-domain row (existing pattern — all stores key on `(guildId, serverId, entityId)`).
- `RustPlusErrorCode` (a RustPlusApi protocol type) must NOT escape `RustPlusBot.Features.Connections` — it does not appear in `Abstractions`, `Domain`, or any other feature.
- `docs/` is gitignored — never `git add` the spec, this plan, or `feature-catalog.md`.
- You are ALREADY on branch `feat/unreachable-devices`. Do NOT create/switch branches or touch `develop`; commit on the current branch.
- TDD: write the failing test first, watch it fail, implement minimally, watch it pass, commit. Frequent commits.
- Reference spec: `docs/superpowers/specs/2026-06-30-rustplusbot-4e-unreachable-devices-design.md`.

---

## File Structure

**New files:**

- `src/RustPlusBot.Abstractions/Connections/DeviceReachability.cs` — the enum.
- `src/RustPlusBot.Abstractions/Connections/DeviceReading.cs` — `DeviceReading` + `StorageReading` result structs.
- `src/RustPlusBot.Abstractions/Events/DeviceReachabilityChangedEvent.cs` — the event.
- `src/RustPlusBot.Features.Connections/Listening/ReachabilityMapping.cs` — pure error-code → reachability mapper.
- `src/RustPlusBot.Features.Connections/Supervisor/ReachabilitySweep.cs` — pure poll-diff helper.
- `src/RustPlusBot.Persistence/Migrations/<generated>_DeviceReachability.cs` — EF migration (generated).
- `src/RustPlusBot.Features.Switches/Modules/SwitchActuationReply.cs` — pure reason → reply-text helper.
- Test files mirroring each (see tasks).

**Modified files:**

- Domain: `SmartSwitch.cs`, `SmartAlarm.cs`, `SmartStorageMonitor.cs` (+ `Reachability`).
- Persistence: `SmartSwitchConfiguration.cs`, `SmartAlarmConfiguration.cs`, `SmartStorageMonitorConfiguration.cs`; `ISwitchStore/SwitchStore`, `IAlarmStore/AlarmStore`, `IStorageMonitorStore/StorageMonitorStore` (+ `SetReachabilityAsync`).
- Connections: `IRustServerConnection.cs`, `RustPlusSocketSource.cs`, `ConnectionSupervisor.cs`, `ConnectionOptions.cs`; `IRustServerQuery.cs`.
- Switches/Alarms/StorageMonitors: each feature's `*StateRelay`/`AlarmRefresher`, `*EmbedRenderer`, `*HostedService`; `SwitchComponentModule.cs`.
- Localization: `Strings.resx`, `Strings.fr.resx`.
- Test fakes: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` and any other `IRustServerConnection` fakes.

---

## Task 1: Reachability vocabulary (Abstractions)

**Files:**

- Create: `src/RustPlusBot.Abstractions/Connections/DeviceReachability.cs`
- Create: `src/RustPlusBot.Abstractions/Connections/DeviceReading.cs`
- Create: `src/RustPlusBot.Abstractions/Events/DeviceReachabilityChangedEvent.cs`
- Test: `tests/RustPlusBot.Abstractions.Tests/DeviceReachabilityVocabularyTests.cs`

**Interfaces:**

- Produces:
  - `enum DeviceReachability { Reachable = 0, Removed = 1, NoPrivilege = 2, NoResponse = 3 }` in `RustPlusBot.Abstractions.Connections`.
  - `readonly record struct DeviceReading(bool? IsActive, DeviceReachability Reachability)` in `RustPlusBot.Abstractions.Connections`.
  - `readonly record struct StorageReading(StorageContentsSnapshot? Contents, DeviceReachability Reachability)` in `RustPlusBot.Abstractions.Connections`.
  - `sealed record DeviceReachabilityChangedEvent(ulong GuildId, Guid ServerId, ulong EntityId, DeviceReachability Reachability)` in `RustPlusBot.Abstractions.Events`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/RustPlusBot.Abstractions.Tests/DeviceReachabilityVocabularyTests.cs
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Events;

namespace RustPlusBot.Abstractions.Tests;

public sealed class DeviceReachabilityVocabularyTests
{
    [Fact]
    public void DeviceReachability_DefaultIsReachable() =>
        Assert.Equal(DeviceReachability.Reachable, default);

    [Fact]
    public void DeviceReading_CarriesPayloadAndReachability()
    {
        var reading = new DeviceReading(IsActive: true, DeviceReachability.Reachable);
        Assert.True(reading.IsActive);
        Assert.Equal(DeviceReachability.Reachable, reading.Reachability);
    }

    [Fact]
    public void Event_CarriesIdentityAndReachability()
    {
        var serverId = Guid.NewGuid();
        var evt = new DeviceReachabilityChangedEvent(10UL, serverId, 99UL, DeviceReachability.Removed);
        Assert.Equal(10UL, evt.GuildId);
        Assert.Equal(serverId, evt.ServerId);
        Assert.Equal(99UL, evt.EntityId);
        Assert.Equal(DeviceReachability.Removed, evt.Reachability);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter "FullyQualifiedName~DeviceReachabilityVocabularyTests"`
Expected: FAIL — types `DeviceReachability` / `DeviceReading` / `DeviceReachabilityChangedEvent` do not exist (build error).

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/RustPlusBot.Abstractions/Connections/DeviceReachability.cs
namespace RustPlusBot.Abstractions.Connections;

/// <summary>Per-device reachability, independent of whole-server connection status.</summary>
public enum DeviceReachability
{
    /// <summary>The device read/actuated successfully.</summary>
    Reachable = 0,

    /// <summary>The in-game entity no longer exists (was destroyed). Maps from <c>not_found</c>.</summary>
    Removed = 1,

    /// <summary>The active player lacks building privilege / token access. Maps from <c>access_denied</c>.</summary>
    NoPrivilege = 2,

    /// <summary>The device did not answer in time (timeout / unknown / other server error).</summary>
    NoResponse = 3,
}
```

```csharp
// src/RustPlusBot.Abstractions/Connections/DeviceReading.cs
namespace RustPlusBot.Abstractions.Connections;

/// <summary>A smart-switch/alarm read: on/off state plus reachability. <see cref="IsActive"/> is null unless <see cref="Reachability"/> is Reachable.</summary>
public readonly record struct DeviceReading(bool? IsActive, DeviceReachability Reachability);

/// <summary>A storage-monitor read: contents plus reachability. <see cref="Contents"/> is null unless <see cref="Reachability"/> is Reachable.</summary>
public readonly record struct StorageReading(StorageContentsSnapshot? Contents, DeviceReachability Reachability);
```

```csharp
// src/RustPlusBot.Abstractions/Events/DeviceReachabilityChangedEvent.cs
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Events;

/// <summary>Raised when a managed device's reachability changes. Device-agnostic; relays filter by entity ownership.</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The server id.</param>
/// <param name="EntityId">The in-game entity id.</param>
/// <param name="Reachability">The new reachability.</param>
public sealed record DeviceReachabilityChangedEvent(
    ulong GuildId,
    Guid ServerId,
    ulong EntityId,
    DeviceReachability Reachability);
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Abstractions.Tests --filter "FullyQualifiedName~DeviceReachabilityVocabularyTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Abstractions tests/RustPlusBot.Abstractions.Tests
git commit -m "feat(devices): add DeviceReachability vocabulary (enum, readings, event)"
```

---

## Task 2: Persistence — Reachability field, config, migration, store mutators

**Files:**

- Modify: `src/RustPlusBot.Domain/Switches/SmartSwitch.cs`, `src/RustPlusBot.Domain/Alarms/SmartAlarm.cs`, `src/RustPlusBot.Domain/StorageMonitors/SmartStorageMonitor.cs`
- Modify: `src/RustPlusBot.Persistence/Configurations/SmartSwitchConfiguration.cs`, `SmartAlarmConfiguration.cs`, `SmartStorageMonitorConfiguration.cs`
- Modify: `src/RustPlusBot.Persistence/Switches/ISwitchStore.cs` + `SwitchStore.cs`; `Alarms/IAlarmStore.cs` + `AlarmStore.cs`; `StorageMonitors/IStorageMonitorStore.cs` + `StorageMonitorStore.cs`
- Create (generated): `src/RustPlusBot.Persistence/Migrations/<ts>_DeviceReachability.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Switches/SwitchStoreTests.cs` (add), `Alarms/AlarmStoreTests.cs` (add), `StorageMonitors/StorageMonitorStoreTests.cs` (add)

**Interfaces:**

- Consumes: `DeviceReachability` (Task 1).
- Produces: on each entity `public DeviceReachability Reachability { get; set; } = DeviceReachability.Reachable;`. On each store `Task SetReachabilityAsync(ulong guildId, Guid serverId, ulong entityId, DeviceReachability reachability, CancellationToken cancellationToken = default)`.

- [ ] **Step 1: Write the failing test (switch store; repeat for alarm & storage)**

```csharp
// add to tests/RustPlusBot.Persistence.Tests/Switches/SwitchStoreTests.cs
[Fact]
public async Task SetReachabilityAsync_PersistsTheReachability()
{
    await using var ctx = NewContext(); // existing helper in this test class
    var store = new SwitchStore(ctx);
    var serverId = await SeedServerAsync(ctx); // existing helper
    await store.AddAsync(1UL, serverId, 42UL, "Door", 7UL);

    await store.SetReachabilityAsync(1UL, serverId, 42UL, DeviceReachability.Removed);

    var sw = await store.GetAsync(1UL, serverId, 42UL);
    Assert.Equal(DeviceReachability.Removed, sw!.Reachability);
}
```

> Note: use the existing context/server-seed helpers already present in each `*StoreTests` class (check the file top — they vary slightly per test class). Add the analogous test to `AlarmStoreTests` (entity id + `AddAsync` signature differ) and `StorageMonitorStoreTests`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter "FullyQualifiedName~SetReachabilityAsync"`
Expected: FAIL — `SetReachabilityAsync` / `Reachability` do not exist (build error).

- [ ] **Step 3: Add the Domain field (all three entities)**

```csharp
// in SmartSwitch.cs, SmartAlarm.cs, SmartStorageMonitor.cs — add this property to each:
using RustPlusBot.Abstractions.Connections; // at top of each file

/// <summary>Per-device reachability; defaults to Reachable. Orthogonal to whole-server connection status.</summary>
public DeviceReachability Reachability { get; set; } = DeviceReachability.Reachable;
```

- [ ] **Step 4: Map it in each EF configuration**

```csharp
// add inside each Configure(...) body, after the existing Property maps:
builder.Property(s => s.Reachability)
    .HasConversion<int>()
    .HasDefaultValue(DeviceReachability.Reachable);
// (rename the lambda param to match each config: 's' for switch, 'a' for alarm, 'm'/'s' for storage — match the file)
// add `using RustPlusBot.Abstractions.Connections;` to each configuration file
```

- [ ] **Step 5: Add `SetReachabilityAsync` to each store interface + implementation**

```csharp
// ISwitchStore.cs (and the alarm/storage equivalents) — add to the interface:
/// <summary>Sets a device's reachability (no-op if absent).</summary>
Task SetReachabilityAsync(
    ulong guildId,
    Guid serverId,
    ulong entityId,
    DeviceReachability reachability,
    CancellationToken cancellationToken = default);
```

```csharp
// SwitchStore.cs — mirror UpdateStateAsync (uses the existing private MutateAsync helper):
public Task SetReachabilityAsync(
    ulong guildId,
    Guid serverId,
    ulong entityId,
    DeviceReachability reachability,
    CancellationToken cancellationToken = default) =>
    MutateAsync(guildId, serverId, entityId, s => s.Reachability = reachability, cancellationToken);
```

> `AlarmStore` and `StorageMonitorStore` have the same `MutateAsync` private helper — use it identically (`a => a.Reachability = reachability` / `m => m.Reachability = reachability`). Add `using RustPlusBot.Abstractions.Connections;` to each store + interface file as needed.

- [ ] **Step 6: Generate the migration**

Run: `dotnet ef migrations add DeviceReachability --project src/RustPlusBot.Persistence`
Expected: a new `Migrations/<ts>_DeviceReachability.cs` whose `Up` calls `migrationBuilder.AddColumn<int>("Reachability", ...)` on `SmartSwitches`, `SmartAlarms`, and `SmartStorageMonitors`, each with `defaultValue: 0`. Verify the three columns are present; verify the `BotDbContextModelSnapshot.cs` was updated.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter "FullyQualifiedName~SetReachabilityAsync"`
Expected: PASS (switch, alarm, storage).

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Domain src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests
git commit -m "feat(devices): persist per-device Reachability (+ migration, store mutators)"
```

---

## Task 3: Pure reachability mapper (Features.Connections)

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Listening/ReachabilityMapping.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ReachabilityMappingTests.cs`

**Interfaces:**

- Consumes: `DeviceReachability` (Task 1), `RustPlusApi.Data.RustPlusErrorCode`.
- Produces: `internal static DeviceReachability FromResponse(bool isSuccess, RustPlusErrorCode? errorCode)` in `RustPlusBot.Features.Connections.Listening`.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/RustPlusBot.Features.Connections.Tests/ReachabilityMappingTests.cs
using RustPlusApi.Data;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ReachabilityMappingTests
{
    [Fact]
    public void Success_IsReachable() =>
        Assert.Equal(DeviceReachability.Reachable, ReachabilityMapping.FromResponse(true, null));

    [Theory]
    [InlineData(RustPlusErrorCode.NotFound, DeviceReachability.Removed)]
    [InlineData(RustPlusErrorCode.AccessDenied, DeviceReachability.NoPrivilege)]
    [InlineData(RustPlusErrorCode.Unknown, DeviceReachability.NoResponse)]
    [InlineData(RustPlusErrorCode.ServerError, DeviceReachability.NoResponse)]
    [InlineData(RustPlusErrorCode.RateLimit, DeviceReachability.NoResponse)]
    public void Failure_MapsByCode(RustPlusErrorCode code, DeviceReachability expected) =>
        Assert.Equal(expected, ReachabilityMapping.FromResponse(false, code));

    [Fact]
    public void Failure_NullCode_IsNoResponse() =>
        Assert.Equal(DeviceReachability.NoResponse, ReachabilityMapping.FromResponse(false, null));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~ReachabilityMappingTests"`
Expected: FAIL — `ReachabilityMapping` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// src/RustPlusBot.Features.Connections/Listening/ReachabilityMapping.cs
using RustPlusApi.Data;
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Maps a Rust+ response outcome to <see cref="DeviceReachability"/>. The ONLY place RustPlusErrorCode is interpreted.</summary>
internal static class ReachabilityMapping
{
    /// <summary>Reachable on success; Removed for not_found; NoPrivilege for access_denied; NoResponse otherwise.</summary>
    public static DeviceReachability FromResponse(bool isSuccess, RustPlusErrorCode? errorCode)
    {
        if (isSuccess)
        {
            return DeviceReachability.Reachable;
        }

        return errorCode switch
        {
            RustPlusErrorCode.NotFound => DeviceReachability.Removed,
            RustPlusErrorCode.AccessDenied => DeviceReachability.NoPrivilege,
            _ => DeviceReachability.NoResponse,
        };
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~ReachabilityMappingTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Connections/Listening/ReachabilityMapping.cs tests/RustPlusBot.Features.Connections.Tests/ReachabilityMappingTests.cs
git commit -m "feat(devices): pure RustPlusErrorCode -> DeviceReachability mapper"
```

---

## Task 4: Widen the connection seam (IRustServerConnection + RustPlusSocketSource + supervisor internals)

This is a green-preserving type-widening refactor: device reads/actuation on `IRustServerConnection` now carry reachability; `RustPlusSocketSource` fills it via `ReachabilityMapping`; `ConnectionSupervisor` unwraps internally so that `IRustServerQuery` and all its consumers are unchanged in this task. No new behavior yet — verification is the existing suite staying green.

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` (and any other `IRustServerConnection` fake)
- Test: existing `ConnectionSupervisorTests.cs` / `RustPlusSocketSourceTests.cs` (regression only)

**Interfaces:**

- Produces (new `IRustServerConnection` signatures):
  - `Task<DeviceReading> GetSmartDeviceInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken ct)`
  - `Task<StorageReading> GetStorageMonitorInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken ct)`
  - `Task<DeviceReachability> SetSmartSwitchValueAsync(ulong entityId, bool value, TimeSpan timeout, CancellationToken ct)`
  - `Task<DeviceReachability> StrobeSmartSwitchAsync(ulong entityId, int timeoutMs, bool value, TimeSpan timeout, CancellationToken ct)`
- `IRustServerQuery` is **unchanged** this task (supervisor unwraps).

- [ ] **Step 1: Update the `IRustServerConnection` signatures**

```csharp
// IRustServerConnection.cs — change the four device methods' return types:
Task<DeviceReading> GetSmartDeviceInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken cancellationToken);
Task<StorageReading> GetStorageMonitorInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken cancellationToken);
Task<DeviceReachability> SetSmartSwitchValueAsync(ulong entityId, bool value, TimeSpan timeout, CancellationToken cancellationToken);
Task<DeviceReachability> StrobeSmartSwitchAsync(ulong entityId, int timeoutMs, bool value, TimeSpan timeout, CancellationToken cancellationToken);
// add `using RustPlusBot.Abstractions.Connections;`
```

- [ ] **Step 2: Implement the mapping in `RustPlusSocketSource`**

Replace the four method bodies so each call captures the response and maps it. The two reads return payload **only when Reachable**; actuation returns the reachability directly. Catch/timeout/no-data → `NoResponse`.

```csharp
// GetSmartDeviceInfoAsync (was: returns bool?; line ~360-385)
public async Task<DeviceReading> GetSmartDeviceInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken cancellationToken)
{
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(timeout);
    try
    {
        var response = await _rustPlus.GetSmartDeviceInfoAsync(entityId, timeoutCts.Token)
            .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        var reachability = ReachabilityMapping.FromResponse(response.IsSuccess, response.Error?.Code);
        var isActive = response is { IsSuccess: true, Data: { } info } ? info.IsActive : (bool?)null;
        return new DeviceReading(isActive, reachability);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return new DeviceReading(null, DeviceReachability.NoResponse);
    }
#pragma warning disable CA1031 // Broad catch: a failed read maps to NoResponse; never surface a token/secret.
    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
    {
        LogQueryFailed(_logger, ex);
        return new DeviceReading(null, DeviceReachability.NoResponse);
    }
}
```

```csharp
// GetStorageMonitorInfoAsync (was: returns StorageContentsSnapshot?; line ~395-422)
public async Task<StorageReading> GetStorageMonitorInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken cancellationToken)
{
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(timeout);
    try
    {
        var response = await _rustPlus.GetStorageMonitorInfoAsync(entityId, timeoutCts.Token)
            .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        var reachability = ReachabilityMapping.FromResponse(response.IsSuccess, response.Error?.Code);
        var contents = response is { IsSuccess: true, Data: { } info } ? MapContents(info) : null;
        return new StorageReading(contents, reachability);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return new StorageReading(null, DeviceReachability.NoResponse);
    }
#pragma warning disable CA1031
    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
    {
        LogQueryFailed(_logger, ex);
        return new StorageReading(null, DeviceReachability.NoResponse);
    }
}
```

```csharp
// SetSmartSwitchValueAsync (was: returns bool; line ~424-450)
public async Task<DeviceReachability> SetSmartSwitchValueAsync(ulong entityId, bool value, TimeSpan timeout, CancellationToken cancellationToken)
{
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(timeout);
    try
    {
        var response = await _rustPlus.SetSmartSwitchValueAsync(entityId, value, timeoutCts.Token)
            .WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        return ReachabilityMapping.FromResponse(response.IsSuccess, response.Error?.Code);
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return DeviceReachability.NoResponse;
    }
#pragma warning disable CA1031
    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
    {
        LogQueryFailed(_logger, ex);
        return DeviceReachability.NoResponse;
    }
}
```

Apply the identical shape to `StrobeSmartSwitchAsync` (returns `DeviceReachability`). Add `using RustPlusBot.Abstractions.Connections;`. If `response.Error` is a non-nullable property, use `response.Error?.Code` only where `Error` can be null — confirm against the type: `Response.Error` is `ErrorMessage?`; on success it is null, so `response.Error?.Code` is correct.

- [ ] **Step 3: Make `ConnectionSupervisor` unwrap (keep `IRustServerQuery` unchanged)**

Update every place the supervisor calls these connection methods:

```csharp
// IRustServerQuery.GetSmartSwitchStateAsync forward (line ~255-268): unwrap .IsActive
var reading = await live.Connection.GetSmartDeviceInfoAsync(entityId, _options.HeartbeatTimeout, cancellationToken)
    .ConfigureAwait(false);
return reading.IsActive;

// IRustServerQuery.GetStorageContentsAsync forward (line ~271-285): unwrap .Contents
var reading = await live.Connection.GetStorageMonitorInfoAsync(entityId, _options.HeartbeatTimeout, cancellationToken)
    .ConfigureAwait(false);
return reading.Contents;

// IRustServerQuery.SetSmartSwitchAsync forward (line ~288-303): unwrap to bool
var reachability = await live.Connection.SetSmartSwitchValueAsync(entityId, value, _options.HeartbeatTimeout, cancellationToken)
    .ConfigureAwait(false);
return reachability == DeviceReachability.Reachable;

// IRustServerQuery.StrobeSmartSwitchAsync forward (line ~306-322): unwrap to bool
var reachability = await live.Connection.StrobeSmartSwitchAsync(entityId, timeoutMs, value, _options.HeartbeatTimeout, cancellationToken)
    .ConfigureAwait(false);
return reachability == DeviceReachability.Reachable;
```

```csharp
// PublishDevicePrimeAsync (line ~1013-1021): unwrap .IsActive (behavior unchanged for now)
var reading = await connection.GetSmartDeviceInfoAsync(entityId, _options.HeartbeatTimeout, _shutdown.Token)
    .ConfigureAwait(false);
await eventBus.PublishAsync(
        new SmartDeviceTriggeredEvent(key.Guild, key.Server, entityId, reading.IsActive ?? false), _shutdown.Token)
    .ConfigureAwait(false);

// PublishStoragePrimeAsync (line ~1078-1091): unwrap .Contents
var reading = await connection.GetStorageMonitorInfoAsync(entityId, _options.HeartbeatTimeout, _shutdown.Token)
    .ConfigureAwait(false);
if (reading.Contents is not { } contents)
{
    return;
}
await eventBus.PublishAsync(
        new StorageMonitorTriggeredEvent(key.Guild, key.Server, entityId, contents), _shutdown.Token)
    .ConfigureAwait(false);
```

Add `using RustPlusBot.Abstractions.Connections;` to the supervisor.

- [ ] **Step 4: Update the test fakes**

In `FakeRustSocketSource.cs` (and any other `IRustServerConnection` test double), change the four methods to the new return types. Default reachable behavior:

```csharp
public Task<DeviceReading> GetSmartDeviceInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken ct) =>
    Task.FromResult(new DeviceReading(/* existing bool? state */ _state, DeviceReachability.Reachable));
public Task<StorageReading> GetStorageMonitorInfoAsync(ulong entityId, TimeSpan timeout, CancellationToken ct) =>
    Task.FromResult(new StorageReading(/* existing contents */ _contents, DeviceReachability.Reachable));
public Task<DeviceReachability> SetSmartSwitchValueAsync(ulong entityId, bool value, TimeSpan timeout, CancellationToken ct) =>
    Task.FromResult(DeviceReachability.Reachable);
public Task<DeviceReachability> StrobeSmartSwitchAsync(ulong entityId, int timeoutMs, bool value, TimeSpan timeout, CancellationToken ct) =>
    Task.FromResult(DeviceReachability.Reachable);
```

> Match the fake's existing backing fields; if a fake currently lets a test inject a `bool?`/contents/`bool` result, keep that injection but wrap it in the new types (add a settable `DeviceReachability` for tests that will need it in Task 5).

- [ ] **Step 5: Build + run the full Connections suite (regression gate)**

Run: `dotnet build RustPlusBot.slnx`
Expected: build succeeds (all consumers compile — `IRustServerQuery` is unchanged so modules/relays are untouched).

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests`
Expected: PASS (existing supervisor/socket tests still green).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "refactor(devices): widen connection reads/actuation to carry DeviceReachability"
```

---

## Task 5: Prime publishes reachability (ConnectionSupervisor)

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (prime methods)
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs`

**Interfaces:**

- Consumes: `IRustServerConnection` readings (Task 4), `DeviceReachabilityChangedEvent` (Task 1), the supervisor's existing `eventBus`.
- Produces: on connect-prime, a `DeviceReachabilityChangedEvent` per primed device reflecting the read; a non-Reachable switch/alarm no longer publishes `SmartDeviceTriggeredEvent(IsActive=false)`.

- [ ] **Step 1: Write the failing test**

Use the existing `ConnectionSupervisorTests` harness (it already wires a fake connection + a capturing event bus — match its existing helpers/fakes). Add:

```csharp
[Fact]
public async Task Prime_RemovedSwitch_PublishesRemovedReachability_AndNoActiveState()
{
    // Arrange: a managed switch exists; the fake connection returns a Removed reading for it.
    // (Configure the fake's GetSmartDeviceInfoAsync to return new DeviceReading(null, DeviceReachability.Removed).)
    // Act: drive the connect/prime path the existing tests use.
    // Assert:
    Assert.Contains(PublishedEvents, e =>
        e is DeviceReachabilityChangedEvent { EntityId: 42UL, Reachability: DeviceReachability.Removed });
    Assert.DoesNotContain(PublishedEvents, e =>
        e is SmartDeviceTriggeredEvent { EntityId: 42UL, IsActive: false });
}
```

> Wire the entity-id/guild/server to whatever the test harness seeds. If the harness lacks a per-entity reachability hook on its fake connection, add one (a `Dictionary<ulong, DeviceReachability>` the fake consults in `GetSmartDeviceInfoAsync`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~Prime_RemovedSwitch"`
Expected: FAIL — no `DeviceReachabilityChangedEvent` is published; the removed switch is published as `IsActive=false`.

- [ ] **Step 3: Update the prime methods to publish reachability**

```csharp
// PublishDevicePrimeAsync (switches + alarms):
var reading = await connection.GetSmartDeviceInfoAsync(entityId, _options.HeartbeatTimeout, _shutdown.Token)
    .ConfigureAwait(false);
await eventBus.PublishAsync(
        new DeviceReachabilityChangedEvent(key.Guild, key.Server, entityId, reading.Reachability), _shutdown.Token)
    .ConfigureAwait(false);
if (reading.Reachability == DeviceReachability.Reachable)
{
    await eventBus.PublishAsync(
            new SmartDeviceTriggeredEvent(key.Guild, key.Server, entityId, reading.IsActive ?? false), _shutdown.Token)
        .ConfigureAwait(false);
}
```

```csharp
// PublishStoragePrimeAsync:
var reading = await connection.GetStorageMonitorInfoAsync(entityId, _options.HeartbeatTimeout, _shutdown.Token)
    .ConfigureAwait(false);
await eventBus.PublishAsync(
        new DeviceReachabilityChangedEvent(key.Guild, key.Server, entityId, reading.Reachability), _shutdown.Token)
    .ConfigureAwait(false);
if (reading.Reachability == DeviceReachability.Reachable && reading.Contents is { } contents)
{
    await eventBus.PublishAsync(
            new StorageMonitorTriggeredEvent(key.Guild, key.Server, entityId, contents), _shutdown.Token)
        .ConfigureAwait(false);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~Prime_RemovedSwitch"`
Expected: PASS. Then run the whole project to confirm no regression: `dotnet test tests/RustPlusBot.Features.Connections.Tests`.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(devices): connect-prime publishes per-device reachability"
```

---

## Task 6: Periodic reachability poll (ConnectionSupervisor)

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/ConnectionOptions.cs`
- Create: `src/RustPlusBot.Features.Connections/Supervisor/ReachabilitySweep.cs` (pure diff)
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (add `PollReachabilityAsync`, wire into the connected window)
- Test: `tests/RustPlusBot.Features.Connections.Tests/ReachabilitySweepTests.cs`

**Interfaces:**

- Consumes: `DeviceReachability` (Task 1), the three stores' `ListByServerAsync`, `IRustServerConnection` readings (Task 4).
- Produces: `ConnectionOptions.ReachabilityPollInterval` (default 5 min); `ReachabilitySweep.Diff(previous, current) → IReadOnlyList<KeyValuePair<ulong, DeviceReachability>>` of entities whose reachability differs from `previous`.

- [ ] **Step 1: Write the failing test (pure diff)**

```csharp
// tests/RustPlusBot.Features.Connections.Tests/ReachabilitySweepTests.cs
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Connections.Supervisor;

namespace RustPlusBot.Features.Connections.Tests;

public sealed class ReachabilitySweepTests
{
    [Fact]
    public void Diff_NoPrevious_ReturnsNothing_SeedsSilently()
    {
        var current = new Dictionary<ulong, DeviceReachability> { [1] = DeviceReachability.Removed };
        var changes = ReachabilitySweep.Diff(new Dictionary<ulong, DeviceReachability>(), current);
        // First cycle is seeded by the caller before the first Diff; an empty 'previous' that already
        // equals 'current' keys is the caller's job. Diff itself only reports differences:
        Assert.Single(changes); // 1 went from (absent==Reachable default) to Removed
    }

    [Fact]
    public void Diff_ReportsChangesIncludingRecovery()
    {
        var previous = new Dictionary<ulong, DeviceReachability>
        {
            [1] = DeviceReachability.Removed,
            [2] = DeviceReachability.Reachable,
        };
        var current = new Dictionary<ulong, DeviceReachability>
        {
            [1] = DeviceReachability.Reachable, // recovered
            [2] = DeviceReachability.NoPrivilege, // newly degraded
        };
        var changes = ReachabilitySweep.Diff(previous, current);
        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Key == 1 && c.Value == DeviceReachability.Reachable);
        Assert.Contains(changes, c => c.Key == 2 && c.Value == DeviceReachability.NoPrivilege);
    }

    [Fact]
    public void Diff_NoChanges_ReturnsEmpty()
    {
        var same = new Dictionary<ulong, DeviceReachability> { [1] = DeviceReachability.Reachable };
        Assert.Empty(ReachabilitySweep.Diff(same, new Dictionary<ulong, DeviceReachability>(same)));
    }
}
```

> Decision: `Diff` treats an entity absent from `previous` as `Reachable` (its default), so a first observed `Removed` is reported. The loop seeds `previous` from its own first cycle to stay silent at startup — see Step 5.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~ReachabilitySweepTests"`
Expected: FAIL — `ReachabilitySweep` does not exist.

- [ ] **Step 3: Write the pure diff**

```csharp
// src/RustPlusBot.Features.Connections/Supervisor/ReachabilitySweep.cs
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Features.Connections.Supervisor;

/// <summary>Pure helper: which entities' reachability changed between two poll snapshots.</summary>
internal static class ReachabilitySweep
{
    /// <summary>Entities in <paramref name="current"/> whose reachability differs from <paramref name="previous"/> (absent ⇒ Reachable).</summary>
    public static IReadOnlyList<KeyValuePair<ulong, DeviceReachability>> Diff(
        IReadOnlyDictionary<ulong, DeviceReachability> previous,
        IReadOnlyDictionary<ulong, DeviceReachability> current)
    {
        var changes = new List<KeyValuePair<ulong, DeviceReachability>>();
        foreach (var (entityId, reachability) in current)
        {
            var before = previous.TryGetValue(entityId, out var p) ? p : DeviceReachability.Reachable;
            if (before != reachability)
            {
                changes.Add(new KeyValuePair<ulong, DeviceReachability>(entityId, reachability));
            }
        }

        return changes;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~ReachabilitySweepTests"`
Expected: PASS.

- [ ] **Step 5: Add the option + the poll loop, wire it into the connected window**

```csharp
// ConnectionOptions.cs — add beside MarkerPollInterval:
/// <summary>How often to poll managed devices for reachability changes while connected. Default 5m.</summary>
public TimeSpan ReachabilityPollInterval { get; set; } = TimeSpan.FromMinutes(5);
```

```csharp
// ConnectionSupervisor.cs — new loop (sibling of PollMarkersAsync). Reads each managed device, diffs, publishes changes.
private async Task PollReachabilityAsync(
    (ulong Guild, Guid Server) key,
    IRustServerConnection connection,
    CancellationToken ct)
{
    var previous = new Dictionary<ulong, DeviceReachability>();
    var seeded = false;
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(_options.ReachabilityPollInterval, ct).ConfigureAwait(false);
        Dictionary<ulong, DeviceReachability> current;
        try
        {
            current = await ReadAllReachabilityAsync(key, connection, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a failed reachability sweep is logged and retried next cycle.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogReachabilityPollFailed(logger, ex, key.Server);
            continue;
        }

        if (!seeded)
        {
            foreach (var kvp in current)
            {
                previous[kvp.Key] = kvp.Value;
            }

            seeded = true;
            continue; // first cycle: silent baseline
        }

        foreach (var change in ReachabilitySweep.Diff(previous, current))
        {
            previous[change.Key] = change.Value;
            await eventBus.PublishAsync(
                    new DeviceReachabilityChangedEvent(key.Guild, key.Server, change.Key, change.Value), ct)
                .ConfigureAwait(false);
        }
    }
}

// Reads switches+alarms via GetSmartDeviceInfoAsync and storage via GetStorageMonitorInfoAsync; returns entityId -> reachability.
private async Task<Dictionary<ulong, DeviceReachability>> ReadAllReachabilityAsync(
    (ulong Guild, Guid Server) key,
    IRustServerConnection connection,
    CancellationToken ct)
{
    var result = new Dictionary<ulong, DeviceReachability>();
    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var switches = await scope.ServiceProvider.GetRequiredService<ISwitchStore>()
            .ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
        var alarms = await scope.ServiceProvider.GetRequiredService<IAlarmStore>()
            .ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);
        var monitors = await scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>()
            .ListByServerAsync(key.Guild, key.Server, ct).ConfigureAwait(false);

        foreach (var entityId in switches.Select(s => s.EntityId).Concat(alarms.Select(a => a.EntityId)))
        {
            var reading = await connection.GetSmartDeviceInfoAsync(entityId, _options.HeartbeatTimeout, ct)
                .ConfigureAwait(false);
            result[entityId] = reading.Reachability;
        }

        foreach (var monitor in monitors)
        {
            var reading = await connection.GetStorageMonitorInfoAsync(monitor.EntityId, _options.HeartbeatTimeout, ct)
                .ConfigureAwait(false);
            result[monitor.EntityId] = reading.Reachability;
        }
    }

    return result;
}

[LoggerMessage(Level = LogLevel.Warning, Message = "Reachability poll for server {ServerId} failed.")]
private static partial void LogReachabilityPollFailed(ILogger logger, Exception exception, Guid serverId);
```

Wire it into the connected window beside the marker poll (the `using var pollCts` block, line ~515):

```csharp
var markerPoll = Task.Run(() => PollMarkersAsync(key, connection, dims, rigs, tracker, pollCts.Token), CancellationToken.None);
var reachabilityPoll = Task.Run(() => PollReachabilityAsync(key, connection, pollCts.Token), CancellationToken.None);
```

And in the `finally`, join it the same way the marker poll is joined:

```csharp
await pollCts.CancelAsync().ConfigureAwait(false);
try
{
#pragma warning disable VSTHRD003
    await Task.WhenAll(markerPoll, reachabilityPoll).ConfigureAwait(false);
#pragma warning restore VSTHRD003
}
catch (OperationCanceledException)
{
    // Expected on stop.
}
```

- [ ] **Step 6: Build + run the suite**

Run: `dotnet build RustPlusBot.slnx`
Expected: success.

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests`
Expected: PASS (sweep tests + existing).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(devices): periodic reachability poll publishes on change"
```

---

## Task 7: Switches — relay handler, renderer reasons, strings, wiring

**Files:**

- Modify: `src/RustPlusBot.Features.Switches/Relaying/SwitchStateRelay.cs` (add `HandleReachabilityChangedAsync`)
- Modify: `src/RustPlusBot.Features.Switches/Rendering/SwitchEmbedRenderer.cs` (reason branches)
- Modify: `src/RustPlusBot.Features.Switches/Hosting/SwitchesHostedService.cs` (subscribe)
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx` (3 switch keys)
- Test: `tests/RustPlusBot.Features.Switches.Tests/SwitchStateRelayTests.cs`, `SwitchEmbedRendererTests.cs`

**Interfaces:**

- Consumes: `DeviceReachabilityChangedEvent` (Task 1), `ISwitchStore.SetReachabilityAsync`/`ExistsAsync`/`GetAsync` (Task 2), `SmartSwitch.Reachability` (Task 2).
- Produces: `SwitchStateRelay.HandleReachabilityChangedAsync(DeviceReachabilityChangedEvent evt, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// SwitchStateRelayTests.cs — add (match the existing test class's fakes/helpers)
[Fact]
public async Task HandleReachabilityChanged_ForeignEntity_IsIgnored()
{
    // store.ExistsAsync returns false for this entity -> no persist, no render
    var relay = NewRelay(); // existing helper
    await relay.HandleReachabilityChangedAsync(
        new DeviceReachabilityChangedEvent(1UL, ServerId, 999UL, DeviceReachability.Removed), default);
    Assert.False(Store.SetReachabilityCalled); // add a flag to the fake store, or assert via a captured value
}

[Fact]
public async Task HandleReachabilityChanged_OwnedEntity_PersistsAndRenders()
{
    var relay = NewRelay();
    await SeedSwitchAsync(42UL); // existing helper
    await relay.HandleReachabilityChangedAsync(
        new DeviceReachabilityChangedEvent(1UL, ServerId, 42UL, DeviceReachability.Removed), default);
    Assert.Equal(DeviceReachability.Removed, await GetReachabilityAsync(42UL));
    Assert.True(Poster.EnsureCalled);
}
```

```csharp
// SwitchEmbedRendererTests.cs — add
[Theory]
[InlineData(DeviceReachability.Removed, "switch.status.removed")]
[InlineData(DeviceReachability.NoPrivilege, "switch.status.noprivilege")]
[InlineData(DeviceReachability.NoResponse, "switch.status.noresponse")]
public void RenderSwitch_NonReachable_ShowsReasonStatus(DeviceReachability reachability, string expectedKey)
{
    var sw = new SmartSwitch { Name = "Door", Reachability = reachability };
    var (embed, _) = new SwitchEmbedRenderer(Localizer).RenderSwitch(sw, isActive: true, "en");
    // Localizer in tests returns the key; assert the embed surfaced the reason key:
    Assert.Contains(expectedKey, embed.Description ?? embed.Fields.FirstOrDefault().Value?.ToString() ?? "");
}
```

> Match the existing renderer-test assertions (the current tests already assert on status text via the fake localizer — mirror that style; the fake localizer returns the key, so assert on the key string).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Switches.Tests --filter "FullyQualifiedName~Reachability|FullyQualifiedName~NonReachable"`
Expected: FAIL — `HandleReachabilityChangedAsync` missing; renderer ignores `Reachability`.

- [ ] **Step 3: Add the relay handler**

```csharp
// SwitchStateRelay.cs — new public method (mirrors HandleDeviceTriggeredAsync’s scope/ownership shape)
public async Task HandleReachabilityChangedAsync(DeviceReachabilityChangedEvent evt, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(evt);
    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var store = scope.ServiceProvider.GetRequiredService<ISwitchStore>();
        if (!await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false))
        {
            return; // not a switch this relay manages.
        }

        await store.SetReachabilityAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.Reachability, cancellationToken)
            .ConfigureAwait(false);
        var sw = await store.GetAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false);
        if (sw is null)
        {
            return;
        }

        var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken).ConfigureAwait(false);
        await RenderAsync(store, sw, sw.LastIsActive, evt.GuildId, evt.ServerId, culture, cancellationToken)
            .ConfigureAwait(false);
    }
}
```

> The existing `RenderAsync` calls `renderer.RenderSwitch(sw, isActive, culture)`; since the renderer now reads `sw.Reachability`, no signature change is needed — pass `sw.LastIsActive` as the state.

- [ ] **Step 4: Add reason branches to the renderer**

```csharp
// SwitchEmbedRenderer.RenderSwitch — replace the status/disabled logic:
var reason = sw.Reachability;
var blocked = reason is DeviceReachability.Removed or DeviceReachability.NoPrivilege;
var serverDown = reason == DeviceReachability.Reachable && isActive is null;
var statusKey = reason switch
{
    DeviceReachability.Removed => "switch.status.removed",
    DeviceReachability.NoPrivilege => "switch.status.noprivilege",
    DeviceReachability.NoResponse => "switch.status.noresponse",
    _ => isActive switch
    {
        true => "switch.status.active",
        false => "switch.status.inactive",
        null => "switch.status.unreachable",
    },
};
var controlsDisabled = blocked || serverDown; // NoResponse keeps controls enabled (likely transient)
// use controlsDisabled in place of the old `unreachable` for ON/OFF/STROBE/RENAME disabled flags;
// ON additionally disabled when isActive == true, OFF when isActive == false (as today)
```

> Confirm the exact status keys for active/inactive match the file (it uses `switch.status.active`/`.inactive` per the earlier read). Keep them.

- [ ] **Step 5: Add the resx keys (EN + FR)**

```xml
<!-- Strings.resx -->
<data name="switch.status.removed" xml:space="preserve"><value>❌ Removed in-game</value></data>
<data name="switch.status.noprivilege" xml:space="preserve"><value>⛔ No building privilege</value></data>
<data name="switch.status.noresponse" xml:space="preserve"><value>⚠️ No response</value></data>
```

```xml
<!-- Strings.fr.resx -->
<data name="switch.status.removed" xml:space="preserve"><value>❌ Supprimé en jeu</value></data>
<data name="switch.status.noprivilege" xml:space="preserve"><value>⛔ Aucun privilège de construction</value></data>
<data name="switch.status.noresponse" xml:space="preserve"><value>⚠️ Aucune réponse</value></data>
```

Then bump the parity tripwire: in `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs`, change `Assert.Equal(248, EnglishKeys().Count)` → `Assert.Equal(251, EnglishKeys().Count)`. Run `dotnet test tests/RustPlusBot.Localization.Tests -maxcpucount:1` → PASS (EN/FR parity + count 251).

- [ ] **Step 6: Subscribe in the hosted service**

```csharp
// SwitchesHostedService.cs — add a loop mirroring ConsumeDeviceTriggeredAsync:
_reachabilityLoop = Task.Run(() => ConsumeReachabilityChangedAsync(_cts.Token), CancellationToken.None);

private async Task ConsumeReachabilityChangedAsync(CancellationToken cancellationToken)
{
    await foreach (var evt in eventBus.SubscribeAsync<DeviceReachabilityChangedEvent>(cancellationToken)
        .ConfigureAwait(false))
    {
        var scope = scopeFactory.CreateAsyncScope(); // match how this service resolves the relay today
        await using (scope.ConfigureAwait(false))
        {
            var relay = scope.ServiceProvider.GetRequiredService<SwitchStateRelay>();
            await relay.HandleReachabilityChangedAsync(evt, cancellationToken).ConfigureAwait(false);
        }
    }
}
```

> Match the service's existing relay-resolution pattern (it may resolve the relay once or per-event — copy whichever the sibling `ConsumeDeviceTriggeredAsync` uses). Add the `_reachabilityLoop` field and await it in `StopAsync`/dispose alongside the others.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Switches.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Switches src/RustPlusBot.Localization tests/RustPlusBot.Features.Switches.Tests
git commit -m "feat(switches): inline per-device reachability (relay + renderer + EN/FR)"
```

---

## Task 8: Alarms — relay/refresher handler, renderer reasons, strings, wiring

**Files:**

- Modify: `src/RustPlusBot.Features.Alarms/Relaying/AlarmStateRelay.cs` (+ handler); `Relaying/AlarmRefresher.cs` + `IAlarmRefresher.cs` (carry the reason)
- Modify: `src/RustPlusBot.Features.Alarms/Rendering/AlarmEmbedRenderer.cs` (reason branches)
- Modify: `src/RustPlusBot.Features.Alarms/Hosting/AlarmsHostedService.cs` (subscribe)
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx` (3 alarm keys)
- Test: `tests/RustPlusBot.Features.Alarms.Tests/*` (relay + renderer)

**Interfaces:**

- Consumes: `DeviceReachabilityChangedEvent`, `IAlarmStore.SetReachabilityAsync`/`ExistsAsync`/`GetAsync`, `SmartAlarm.Reachability`.
- Produces: `AlarmStateRelay.HandleReachabilityChangedAsync(DeviceReachabilityChangedEvent evt, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// AlarmStateRelay test (mirror the Switches relay tests; alarm entity id + AddAsync shape differ)
[Fact]
public async Task HandleReachabilityChanged_OwnedAlarm_PersistsAndRenders()
{
    var relay = NewRelay();
    await SeedAlarmAsync(42UL);
    await relay.HandleReachabilityChangedAsync(
        new DeviceReachabilityChangedEvent(1UL, ServerId, 42UL, DeviceReachability.NoPrivilege), default);
    Assert.Equal(DeviceReachability.NoPrivilege, await GetReachabilityAsync(42UL));
    Assert.True(Poster.EnsureCalled);
}
```

```csharp
// AlarmEmbedRendererTests — non-reachable shows the reason. Note: RenderAlarm currently takes an
// `unreachable` bool; after this task it reads alarm.Reachability. Keep the existing `unreachable`
// param for the server-down path, but add reason precedence.
[Theory]
[InlineData(DeviceReachability.Removed, "alarm.status.removed")]
[InlineData(DeviceReachability.NoPrivilege, "alarm.status.noprivilege")]
[InlineData(DeviceReachability.NoResponse, "alarm.status.noresponse")]
public void RenderAlarm_NonReachable_ShowsReasonStatus(DeviceReachability reachability, string expectedKey)
{
    var alarm = new SmartAlarm { Name = "Trap", Reachability = reachability };
    var (embed, _) = new AlarmEmbedRenderer(Localizer).RenderAlarm(alarm, unreachable: false, "en");
    Assert.Contains(expectedKey, embed.Description ?? embed.Fields.FirstOrDefault().Value?.ToString() ?? "");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Alarms.Tests --filter "FullyQualifiedName~Reachability|FullyQualifiedName~NonReachable"`
Expected: FAIL.

- [ ] **Step 3: Add the relay handler**

```csharp
// AlarmStateRelay.cs — new method (the relay delegates render to the AlarmRefresher today; reuse it)
public async Task HandleReachabilityChangedAsync(DeviceReachabilityChangedEvent evt, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(evt);
    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var store = scope.ServiceProvider.GetRequiredService<IAlarmStore>();
        if (!await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await store.SetReachabilityAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.Reachability, cancellationToken)
            .ConfigureAwait(false);
    }

    // The refresher re-loads + renders; server-down 'unreachable' stays false here (the connection-status
    // path owns that). The renderer reads alarm.Reachability for the reason.
    await refresher.RefreshAsync(evt.GuildId, evt.ServerId, evt.EntityId, unreachable: false, cancellationToken)
        .ConfigureAwait(false);
}
```

> If `AlarmStateRelay` doesn't already hold an `IAlarmRefresher`, inject it (it's already registered). Match the relay's existing constructor/DI shape.

- [ ] **Step 4: Add reason branches to `AlarmEmbedRenderer`**

```csharp
// AlarmEmbedRenderer.RenderAlarm(SmartAlarm alarm, bool unreachable, string culture):
var statusKey = alarm.Reachability switch
{
    DeviceReachability.Removed => "alarm.status.removed",
    DeviceReachability.NoPrivilege => "alarm.status.noprivilege",
    DeviceReachability.NoResponse => "alarm.status.noresponse",
    _ => unreachable
        ? "alarm.status.unreachable"
        : (alarm.LastIsActive ? "alarm.status.active" : "alarm.status.armed"),
};
// disable any control buttons when alarm.Reachability is Removed or NoPrivilege (match the switch policy)
```

> Use the alarm's existing active/armed status keys (`alarm.status.active`/`.armed`/`.unreachable` per the resx). Keep them.

- [ ] **Step 5: Add the resx keys (EN + FR)**

```xml
<!-- Strings.resx -->
<data name="alarm.status.removed" xml:space="preserve"><value>❌ Removed in-game</value></data>
<data name="alarm.status.noprivilege" xml:space="preserve"><value>⛔ No building privilege</value></data>
<data name="alarm.status.noresponse" xml:space="preserve"><value>⚠️ No response</value></data>
<!-- Strings.fr.resx -->
<data name="alarm.status.removed" xml:space="preserve"><value>❌ Supprimé en jeu</value></data>
<data name="alarm.status.noprivilege" xml:space="preserve"><value>⛔ Aucun privilège de construction</value></data>
<data name="alarm.status.noresponse" xml:space="preserve"><value>⚠️ Aucune réponse</value></data>
```

Then bump the parity tripwire: in `StringsResourceParityTests.cs`, change `Assert.Equal(251, ...)` → `Assert.Equal(254, ...)`. Run `dotnet test tests/RustPlusBot.Localization.Tests -maxcpucount:1` → PASS (count 254).

- [ ] **Step 6: Subscribe in `AlarmsHostedService`**

Add a `ConsumeReachabilityChangedAsync` loop subscribing to `DeviceReachabilityChangedEvent` and calling `AlarmStateRelay.HandleReachabilityChangedAsync`, mirroring the switch service (Task 7 Step 6) and this service's own existing subscription loops.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Alarms.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Alarms src/RustPlusBot.Localization tests/RustPlusBot.Features.Alarms.Tests
git commit -m "feat(alarms): inline per-device reachability (relay + renderer + EN/FR)"
```

---

## Task 9: Storage monitors — relay handler, renderer reasons, strings, wiring

**Files:**

- Modify: `src/RustPlusBot.Features.StorageMonitors/Relaying/StorageMonitorStateRelay.cs` (+ handler)
- Modify: `src/RustPlusBot.Features.StorageMonitors/Rendering/StorageMonitorEmbedRenderer.cs` (reason branches)
- Modify: `src/RustPlusBot.Features.StorageMonitors/Hosting/StorageMonitorsHostedService.cs` (subscribe)
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx` (3 storage keys)
- Test: `tests/RustPlusBot.Features.StorageMonitors.Tests/*`

**Interfaces:**

- Consumes: `DeviceReachabilityChangedEvent`, `IStorageMonitorStore.SetReachabilityAsync`/`ExistsAsync`/`GetAsync`, `SmartStorageMonitor.Reachability`.
- Produces: `StorageMonitorStateRelay.HandleReachabilityChangedAsync(DeviceReachabilityChangedEvent evt, CancellationToken ct)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// StorageMonitorStateRelay test (mirror Switches relay tests)
[Fact]
public async Task HandleReachabilityChanged_OwnedMonitor_PersistsAndRenders()
{
    var relay = NewRelay();
    await SeedMonitorAsync(42UL);
    await relay.HandleReachabilityChangedAsync(
        new DeviceReachabilityChangedEvent(1UL, ServerId, 42UL, DeviceReachability.Removed), default);
    Assert.Equal(DeviceReachability.Removed, await GetReachabilityAsync(42UL));
    Assert.True(Poster.EnsureCalled);
}
```

```csharp
// StorageMonitorEmbedRenderer test
[Theory]
[InlineData(DeviceReachability.Removed, "storagemonitor.status.removed")]
[InlineData(DeviceReachability.NoPrivilege, "storagemonitor.status.noprivilege")]
[InlineData(DeviceReachability.NoResponse, "storagemonitor.status.noresponse")]
public void RenderMonitor_NonReachable_ShowsReasonStatus(DeviceReachability reachability, string expectedKey)
{
    var monitor = new SmartStorageMonitor { Name = "Box", Reachability = reachability };
    var (embed, _) = new StorageMonitorEmbedRenderer(Localizer).RenderMonitor(monitor, contents: null, "en");
    Assert.Contains(expectedKey, embed.Description ?? embed.Fields.FirstOrDefault().Value?.ToString() ?? "");
}
```

> Confirm the storage renderer's existing unreachable status key name (likely `storagemonitor.status.unreachable`) and match the prefix for the new keys.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests --filter "FullyQualifiedName~Reachability|FullyQualifiedName~NonReachable"`
Expected: FAIL.

- [ ] **Step 3: Add the relay handler**

```csharp
// StorageMonitorStateRelay.cs — new method (mirror HandleTriggeredAsync’s scope/ownership shape)
public async Task HandleReachabilityChangedAsync(DeviceReachabilityChangedEvent evt, CancellationToken cancellationToken)
{
    ArgumentNullException.ThrowIfNull(evt);
    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var store = scope.ServiceProvider.GetRequiredService<IStorageMonitorStore>();
        if (!await store.ExistsAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await store.SetReachabilityAsync(evt.GuildId, evt.ServerId, evt.EntityId, evt.Reachability, cancellationToken)
            .ConfigureAwait(false);
        var monitor = await store.GetAsync(evt.GuildId, evt.ServerId, evt.EntityId, cancellationToken).ConfigureAwait(false);
        if (monitor is null)
        {
            return;
        }

        var culture = await GetCultureAsync(scope.ServiceProvider, evt.GuildId, cancellationToken).ConfigureAwait(false);
        await RenderAsync(store, monitor, contents: null, evt.GuildId, evt.ServerId, culture, cancellationToken)
            .ConfigureAwait(false);
    }
}
```

> Passing `contents: null` is fine — when `monitor.Reachability` is non-Reachable the renderer shows the reason; when Reachable it falls back to the last-known render path (the next trigger/prime refreshes real contents).

- [ ] **Step 4: Add reason branches to `StorageMonitorEmbedRenderer`**

```csharp
// StorageMonitorEmbedRenderer.RenderMonitor(SmartStorageMonitor monitor, StorageContentsSnapshot? contents, string culture):
var statusKey = monitor.Reachability switch
{
    DeviceReachability.Removed => "storagemonitor.status.removed",
    DeviceReachability.NoPrivilege => "storagemonitor.status.noprivilege",
    DeviceReachability.NoResponse => "storagemonitor.status.noresponse",
    _ => contents is null ? "storagemonitor.status.unreachable" : /* existing reachable/contents path */ null,
};
// when statusKey is non-null (degraded), render the reason in place of the contents block; disable
// Refresh/Rename controls for Removed/NoPrivilege (match the switch policy)
```

> Adapt to the renderer's actual structure — it likely builds a contents field when `contents` is present and an "unreachable" status otherwise. Insert the reason precedence above that branch.

- [ ] **Step 5: Add the resx keys (EN + FR)**

```xml
<!-- Strings.resx -->
<data name="storagemonitor.status.removed" xml:space="preserve"><value>❌ Removed in-game</value></data>
<data name="storagemonitor.status.noprivilege" xml:space="preserve"><value>⛔ No building privilege</value></data>
<data name="storagemonitor.status.noresponse" xml:space="preserve"><value>⚠️ No response</value></data>
<!-- Strings.fr.resx -->
<data name="storagemonitor.status.removed" xml:space="preserve"><value>❌ Supprimé en jeu</value></data>
<data name="storagemonitor.status.noprivilege" xml:space="preserve"><value>⛔ Aucun privilège de construction</value></data>
<data name="storagemonitor.status.noresponse" xml:space="preserve"><value>⚠️ Aucune réponse</value></data>
```

Then bump the parity tripwire: in `StringsResourceParityTests.cs`, change `Assert.Equal(254, ...)` → `Assert.Equal(257, ...)`. Run `dotnet test tests/RustPlusBot.Localization.Tests -maxcpucount:1` → PASS (count 257).

- [ ] **Step 6: Subscribe in `StorageMonitorsHostedService`**

Add a `ConsumeReachabilityChangedAsync` loop subscribing to `DeviceReachabilityChangedEvent` and calling `StorageMonitorStateRelay.HandleReachabilityChangedAsync`, mirroring Task 7 Step 6 and this service's existing loops.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.StorageMonitors src/RustPlusBot.Localization tests/RustPlusBot.Features.StorageMonitors.Tests
git commit -m "feat(storage): inline per-device reachability (relay + renderer + EN/FR)"
```

---

## Task 10: Switch actuation surfaces the reason

Widen the two `IRustServerQuery` actuation methods to `DeviceReachability`, make the supervisor forwards pass-through, and update `SwitchComponentModule` to report the reason + publish the embed update. `GetSmartSwitchStateAsync`/`GetStorageContentsAsync` on `IRustServerQuery` stay as-is (no actuation reason needed there).

**Files:**

- Modify: `src/RustPlusBot.Abstractions/Connections/IRustServerQuery.cs` (Set/Strobe → `DeviceReachability`)
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (forwards become pass-through)
- Create: `src/RustPlusBot.Features.Switches/Modules/SwitchActuationReply.cs` (pure reason → text)
- Modify: `src/RustPlusBot.Features.Switches/Modules/SwitchComponentModule.cs` (use reason + publish)
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx` (3 reply keys)
- Test: `tests/RustPlusBot.Features.Switches.Tests/SwitchActuationReplyTests.cs`

**Interfaces:**

- Consumes: `DeviceReachability`, `ILocalizer`.
- Produces:
  - `IRustServerQuery.SetSmartSwitchAsync(...) → Task<DeviceReachability>`, `StrobeSmartSwitchAsync(...) → Task<DeviceReachability>`.
  - `static string SwitchActuationReply.Describe(DeviceReachability reachability, ILocalizer localizer, string culture)` returning the localized ephemeral message (empty/normal handled by the caller for `Reachable`).

- [ ] **Step 1: Write the failing test (pure reply mapping)**

```csharp
// tests/RustPlusBot.Features.Switches.Tests/SwitchActuationReplyTests.cs
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Switches.Modules;

namespace RustPlusBot.Features.Switches.Tests;

public sealed class SwitchActuationReplyTests
{
    [Theory]
    [InlineData(DeviceReachability.Removed, "switch.actuation.removed")]
    [InlineData(DeviceReachability.NoPrivilege, "switch.actuation.noprivilege")]
    [InlineData(DeviceReachability.NoResponse, "switch.actuation.noresponse")]
    public void Describe_NonReachable_ReturnsReasonText(DeviceReachability reachability, string expectedKey)
    {
        // Fake localizer returns the key it is given (existing test pattern).
        var text = SwitchActuationReply.Describe(reachability, new KeyEchoLocalizer(), "en");
        Assert.Equal(expectedKey, text);
    }
}
```

> `KeyEchoLocalizer` = a 2-line `ILocalizer` whose `Get(key, culture)` returns `key` (the Switches.Tests project almost certainly already has such a fake — reuse it; otherwise add it).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Switches.Tests --filter "FullyQualifiedName~SwitchActuationReplyTests"`
Expected: FAIL — `SwitchActuationReply` does not exist.

- [ ] **Step 3: Write the pure helper**

```csharp
// src/RustPlusBot.Features.Switches/Modules/SwitchActuationReply.cs
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Switches.Modules;

/// <summary>Maps a failed actuation's reachability to a localized ephemeral reply.</summary>
internal static class SwitchActuationReply
{
    /// <summary>The localized reason text for a non-Reachable actuation result.</summary>
    public static string Describe(DeviceReachability reachability, ILocalizer localizer, string culture)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var key = reachability switch
        {
            DeviceReachability.Removed => "switch.actuation.removed",
            DeviceReachability.NoPrivilege => "switch.actuation.noprivilege",
            _ => "switch.actuation.noresponse",
        };
        return localizer.Get(key, culture);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Switches.Tests --filter "FullyQualifiedName~SwitchActuationReplyTests"`
Expected: PASS.

- [ ] **Step 5: Widen `IRustServerQuery` + supervisor forwards**

```csharp
// IRustServerQuery.cs — change the two actuation methods:
Task<DeviceReachability> SetSmartSwitchAsync(ulong guildId, Guid serverId, ulong entityId, bool value, CancellationToken cancellationToken);
Task<DeviceReachability> StrobeSmartSwitchAsync(ulong guildId, Guid serverId, ulong entityId, int timeoutMs, bool value, CancellationToken cancellationToken);
// add `using RustPlusBot.Abstractions.Connections;`
```

```csharp
// ConnectionSupervisor.cs — the forwards become pass-through (drop the `== Reachable` unwrap from Task 4):
public async Task<DeviceReachability> SetSmartSwitchAsync(ulong guildId, Guid serverId, ulong entityId, bool value, CancellationToken cancellationToken)
{
    if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
    {
        return DeviceReachability.NoResponse;
    }

    return await live.Connection.SetSmartSwitchValueAsync(entityId, value, _options.HeartbeatTimeout, cancellationToken)
        .ConfigureAwait(false);
}
// StrobeSmartSwitchAsync: same shape, returns the connection's DeviceReachability.
```

- [ ] **Step 6: Update `SwitchComponentModule` to use the reason + publish**

```csharp
// SwitchComponentModule.SetAsync — replace the bool check:
var result = await query.SetSmartSwitchAsync(Context.Guild.Id, serverId, entityId, value, CancellationToken.None)
    .ConfigureAwait(false);
if (result != DeviceReachability.Reachable)
{
    var culture = await GetCultureAsync(serverId).ConfigureAwait(false); // resolve guild culture as elsewhere in the module/feature
    await eventBus.PublishAsync(new DeviceReachabilityChangedEvent(Context.Guild.Id, serverId, entityId, result))
        .ConfigureAwait(false);
    await FollowupAsync(SwitchActuationReply.Describe(result, localizer, culture), ephemeral: true).ConfigureAwait(false);
    return;
}

await eventBus.PublishAsync(new SwitchStateChangedEvent(Context.Guild.Id, serverId, entityId, value)).ConfigureAwait(false);
await FollowupAsync(value ? "Turned on." : "Turned off.", ephemeral: true).ConfigureAwait(false);
```

> The module currently has no `ILocalizer` or culture lookup — inject `ILocalizer` (it's registered) and resolve culture the way the feature does elsewhere (via `IWorkspaceStore.GetCultureAsync` in a scope). Apply the same change to `StrobeAsync` (its `!ok` branch). Keep the success replies as they are (or localize them too if the feature already localizes module replies — match current behavior; they are currently hardcoded English "Turned on."/"Turned off." which is pre-existing and out of scope to change here).

- [ ] **Step 7: Add the resx keys (EN + FR)**

```xml
<!-- Strings.resx -->
<data name="switch.actuation.removed" xml:space="preserve"><value>This switch was removed in-game.</value></data>
<data name="switch.actuation.noprivilege" xml:space="preserve"><value>No building privilege for this switch.</value></data>
<data name="switch.actuation.noresponse" xml:space="preserve"><value>The switch didn’t respond. Try again.</value></data>
<!-- Strings.fr.resx -->
<data name="switch.actuation.removed" xml:space="preserve"><value>Cet interrupteur a été supprimé en jeu.</value></data>
<data name="switch.actuation.noprivilege" xml:space="preserve"><value>Aucun privilège de construction pour cet interrupteur.</value></data>
<data name="switch.actuation.noresponse" xml:space="preserve"><value>L’interrupteur n’a pas répondu. Réessayez.</value></data>
```

- [ ] **Step 8: Bump the parity tripwire**

In `StringsResourceParityTests.cs`, change `Assert.Equal(257, ...)` → `Assert.Equal(260, ...)`. Run `dotnet test tests/RustPlusBot.Localization.Tests -maxcpucount:1` → PASS (count 260 — final).

- [ ] **Step 9: Build + run the suites**

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: success (all `IRustServerQuery` consumers updated).

Run: `dotnet test tests/RustPlusBot.Features.Switches.Tests -maxcpucount:1` and `dotnet test tests/RustPlusBot.Features.Connections.Tests -maxcpucount:1`
Expected: PASS.

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusBot.Abstractions src/RustPlusBot.Features.Connections src/RustPlusBot.Features.Switches src/RustPlusBot.Localization tests/RustPlusBot.Features.Switches.Tests tests/RustPlusBot.Localization.Tests
git commit -m "feat(switches): surface actuation reachability reason + update embed"
```

---

## Task 11: Full verification, format gate, catalog, wrap-up

**Files:**

- Local-only (not committed): `docs/product/feature-catalog.md` (flip subsystem-4 rows to Done)
- No new source.

- [ ] **Step 1: Full build**

Run: `dotnet build RustPlusBot.slnx`
Expected: 0 warnings-as-errors, success.

- [ ] **Step 2: Full test run**

Run: `dotnet test RustPlusBot.slnx`
Expected: all green. (Spot-check coverage of: mapper, sweep diff, prime-publish, each relay persist+filter, each renderer reason, switch actuation reply, store round-trips, migration.)

- [ ] **Step 3: Format gate**

Run: `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx`
Expected: no file changes. If it reformats anything, review, `git add`, and amend/commit the formatting.

- [ ] **Step 4: Manual smoke reasoning (no commit)**

Re-read the spec's §6.2 table and confirm the rendered behavior matches per reason (status line + control gating) and that server-down still takes precedence when `Reachability == Reachable`.

- [ ] **Step 5: Update the local catalog (not committed)**

Edit `docs/product/feature-catalog.md`: flip the subsystem-4 "UnreachableSmartDevices" row (and the subsystem-4 roadmap row) to ✔️ Done with a note that detection is inline + periodic poll, no separate channel. Do **not** `git add` it (gitignored).

- [ ] **Step 6: Final commit (if the format gate or any cleanup touched tracked files)**

```bash
git add -A
git commit -m "chore(devices): finish subsystem 4 — per-device unreachable status"
```

> If nothing tracked changed after Task 10, skip this commit.

---

## Self-Review

**Spec coverage:**

- §3 vocabulary → Task 1. §4 persistence/migration/stores → Task 2. §5.1/5.2 seam + mapping → Tasks 3–4. §5.3 prime → Task 5. §5.4 poll + option → Task 6. §6.1 relays → Tasks 7–9. §6.2 rendering → Tasks 7–9. §6.3 switch actuation → Task 10. §7 i18n → folded into Tasks 7–10 (per-feature keys). §8 testing → each task's tests + Task 11. §9 files → covered across tasks. No gaps.

**Type consistency:** `DeviceReachability`, `DeviceReading(bool?, DeviceReachability)`, `StorageReading(StorageContentsSnapshot?, DeviceReachability)`, `DeviceReachabilityChangedEvent(ulong,Guid,ulong,DeviceReachability)`, `SetReachabilityAsync(ulong,Guid,ulong,DeviceReachability,CancellationToken)`, `ReachabilityMapping.FromResponse(bool, RustPlusErrorCode?)`, `ReachabilitySweep.Diff(IReadOnlyDictionary,IReadOnlyDictionary)`, `HandleReachabilityChangedAsync(DeviceReachabilityChangedEvent,CancellationToken)`, `SwitchActuationReply.Describe(DeviceReachability,ILocalizer,string)` — names/signatures consistent across tasks.

**Placeholder scan:** No "TBD"/"implement later". Where a step says "match the existing helper/fake", it names the concrete sibling to copy (e.g., `ConsumeDeviceTriggeredAsync`, `MutateAsync`, the per-test seed helpers) — this is adaptation to existing conventions, not an unfilled blank.
