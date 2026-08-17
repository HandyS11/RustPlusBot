# Vending Machine Search and Tracking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let players search every vending machine on a Rust server for an item, register their own shop by grid cell, and get Discord notifications when a rival undercuts them or when their own listings sell out.

**Architecture:** Vending data rides along on the existing 5-second `GetMapMarkers` poll — no new Rust+ request. The connection supervisor publishes a `VendingMachinesObservedEvent`; `Features.Vending` keeps a wholesale-replaced in-memory index per server, and two **pure** evaluators turn (index + tracks) into a desired set of notifications. A relay reconciles that desired set against persisted Discord message ids by posting, editing, or deleting. Only track registrations and posted message ids touch SQLite.

**Tech Stack:** .NET 10, C# 14, Discord.Net, EF Core 10 + SQLite, RustPlusApi 2.0.0-beta.7, xUnit + NSubstitute, `.resx` localization (en/fr).

**Spec:** `docs/superpowers/specs/2026-08-17-vending-machine-tracking-design.md`

## Global Constraints

- Build with `dtk dotnet build RustPlusBot.slnx`, test with `dtk dotnet test RustPlusBot.slnx` (the `dtk` wrapper strips SDK noise). Never call raw `dotnet build`/`test`.
- Every public type and member needs an XML doc comment — the build treats missing docs as errors (`Directory.Build.props`).
- Feature internals are `internal`; test access comes from `<InternalsVisibleTo>` in the feature `.csproj`.
- Long-running event consumers MUST use `eventBus.ConsumeAsync<TEvent>(handler, onFailure, ct)` from `RustPlusBot.Abstractions.Events.EventBusConsumption`. It puts the try/catch *inside* the `await foreach`, so one failing handler costs one event instead of killing the subscription forever. Never hand-roll the loop.
- Broad `catch (Exception)` needs `#pragma warning disable CA1031` with a one-line justification comment, matching the existing style.
- All user-facing strings go in **both** `src/RustPlusBot.Localization/Strings.resx` and `Strings.fr.resx`. A missing French key is a silent English fallback, not a build error — check both files.
- Unit-price comparison is **exact integer cross-multiplication**, never floating point: `costA * qtyB <= costB * qtyA` in `long`.
- Comparisons never cross a `ListingKey`. Scrap is never compared to cloth; blueprint and non-blueprint are distinct listings.
- New projects must be added to `RustPlusBot.slnx` (both the `src` and `tests` project lists).
- EF tooling is a local tool: run `dotnet tool restore` once, then `dotnet ef ...`.

---

### Task 1: Carry vending machines on the marker poll

Mechanical widening of the marker-poll return type. No behaviour change — the goal is a green build with vending data reaching the supervisor and going nowhere yet.

**Files:**
- Create: `src/RustPlusBot.Abstractions/Connections/VendingOfferSnapshot.cs`
- Create: `src/RustPlusBot.Abstractions/Connections/VendingMachineSnapshot.cs`
- Create: `src/RustPlusBot.Abstractions/Connections/MapMarkersSnapshot.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs:123-128`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs:93-95, 595-623`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs:757-795`
- Test: `tests/RustPlusBot.Features.Connections.Tests/` (existing fakes)

**Interfaces:**
- Consumes: `RustPlusApi.Data.Markers.VendingMachineMarker`, `RustPlusApi.Data.VendingMachineItem`.
- Produces: `MapMarkersSnapshot`, `VendingMachineSnapshot`, `VendingOfferSnapshot`; `IRustServerConnection.GetMapMarkersAsync` now returns `Task<MapMarkersSnapshot>`.

- [ ] **Step 1: Create the three snapshot records**

`src/RustPlusBot.Abstractions/Connections/VendingOfferSnapshot.cs`:

```csharp
namespace RustPlusBot.Abstractions.Connections;

/// <summary>One sell order listed by a player vending machine.</summary>
/// <param name="ItemId">The Rust item id being sold.</param>
/// <param name="ItemIsBlueprint">True when the item sold is a blueprint.</param>
/// <param name="Quantity">How many items one order yields (RustPlusApi <c>StackSize</c>); always at least 1.</param>
/// <param name="CurrencyId">The Rust item id accepted as payment.</param>
/// <param name="CurrencyIsBlueprint">True when the currency is a blueprint.</param>
/// <param name="CostPerOrder">The currency amount charged for one order (RustPlusApi <c>CostPerStack</c>).</param>
/// <param name="AmountInStock">Orders remaining (RustPlusApi <c>StackSizeAmount</c>); 0 means sold out.</param>
public sealed record VendingOfferSnapshot(
    int ItemId,
    bool ItemIsBlueprint,
    int Quantity,
    int CurrencyId,
    bool CurrencyIsBlueprint,
    int CostPerOrder,
    int AmountInStock);
```

`src/RustPlusBot.Abstractions/Connections/VendingMachineSnapshot.cs`:

```csharp
namespace RustPlusBot.Abstractions.Connections;

/// <summary>One player vending machine observed in a <c>GetMapMarkers</c> poll.</summary>
/// <param name="Id">The stable marker id.</param>
/// <param name="X">World X coordinate.</param>
/// <param name="Y">World Y coordinate.</param>
/// <param name="Name">The shopfront name, if any.</param>
/// <param name="IsOutOfStock">Whole-machine empty flag; null when the server did not report it.</param>
/// <param name="Offers">The machine's sell orders.</param>
public sealed record VendingMachineSnapshot(
    ulong Id,
    float X,
    float Y,
    string? Name,
    bool? IsOutOfStock,
    IReadOnlyList<VendingOfferSnapshot> Offers);
```

`src/RustPlusBot.Abstractions/Connections/MapMarkersSnapshot.cs`:

```csharp
namespace RustPlusBot.Abstractions.Connections;

/// <summary>The result of one <c>GetMapMarkers</c> poll: the markers the bot diffs, plus the vending machines.</summary>
/// <param name="Markers">Cargo ship / patrol helicopter / chinook / travelling vendor markers.</param>
/// <param name="VendingMachines">Every player vending machine on the server, with its full offer list.</param>
public sealed record MapMarkersSnapshot(
    IReadOnlyList<MapMarkerSnapshot> Markers,
    IReadOnlyList<VendingMachineSnapshot> VendingMachines)
{
    /// <summary>An empty snapshot, used when there is no live socket.</summary>
    public static MapMarkersSnapshot Empty { get; } = new([], []);
}
```

- [ ] **Step 2: Widen the connection interface**

In `IRustServerConnection.cs`, replace the `GetMapMarkersAsync` declaration:

```csharp
    /// <summary>Polls the current map markers and vending machines. Throws on failure.</summary>
    /// <param name="timeout">How long to wait for the response.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The markers the bot diffs plus every player vending machine.</returns>
    Task<MapMarkersSnapshot> GetMapMarkersAsync(TimeSpan timeout,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Map the vending bucket in the socket source**

In `RustPlusSocketSource.cs`, the disconnected stub at line ~93 becomes:

```csharp
        public Task<MapMarkersSnapshot> GetMapMarkersAsync(TimeSpan timeout,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MapMarkersSnapshot.Empty);
```

and the live implementation returns the snapshot, adding the vending mapping after the four `AddMarkers` calls:

```csharp
            AddMarkers(markers, data.TravellingVendorMarkers, MarkerKind.TravellingVendor, m => m.Rotation);
            return new MapMarkersSnapshot(markers, MapVendingMachines(data.VendingMachineMarkers));
        }

        // Quantity is a divisor in every unit-price comparison, so a malformed 0 from the server is
        // clamped to 1 here rather than guarded at each of the (many) downstream comparison sites.
        private static IReadOnlyList<VendingMachineSnapshot> MapVendingMachines(
            IReadOnlyDictionary<ulong, RustPlusApi.Data.Markers.VendingMachineMarker> markers)
        {
            var machines = new List<VendingMachineSnapshot>();
            foreach (var marker in markers.Values)
            {
                if (marker.Id is not { } id || marker.X is not { } x || marker.Y is not { } y)
                {
                    continue;
                }

                var offers = (marker.VendingMachineItems ?? [])
                    .Select(i => new VendingOfferSnapshot(
                        i.Id,
                        i.IsItemBlueprint,
                        Math.Max(1, i.StackSize),
                        i.CurrencyId,
                        i.IsCurrencyBlueprint,
                        i.CostPerStack,
                        i.StackSizeAmount))
                    .ToList();
                machines.Add(new VendingMachineSnapshot(id, x, y, marker.Name, marker.IsOutOfStock, offers));
            }

            return machines;
        }
```

- [ ] **Step 4: Fix the supervisor call site**

In `ConnectionSupervisor.PollMarkersAsync`, the poll now yields a snapshot; the existing diff logic reads `.Markers`:

```csharp
                var snapshot = await connection.GetMapMarkersAsync(_options.HeartbeatTimeout, ct)
                    .ConfigureAwait(false);
                var current = snapshot.Markers;
                anyCh47 = current.Any(m => m.Kind == MarkerKind.Chinook);
```

Everything below that line is unchanged.

- [ ] **Step 5: Build and fix every remaining call site and test fake**

Run: `dtk dotnet build RustPlusBot.slnx`
Expected: compile errors listing each `IRustServerConnection` fake in the test projects. Change each one's `GetMapMarkersAsync` to return `MapMarkersSnapshot`, wrapping its existing marker list: `new MapMarkersSnapshot(markers, [])`. Repeat until the build is clean.

- [ ] **Step 6: Run the full test suite**

Run: `dtk dotnet test RustPlusBot.slnx`
Expected: PASS. No behaviour changed — this step proves it.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Abstractions/Connections src/RustPlusBot.Features.Connections tests
git commit -m "feat: carry vending machines on the map-marker poll"
```

---

### Task 2: Publish `VendingMachinesObservedEvent`

**Files:**
- Create: `src/RustPlusBot.Abstractions/Events/VendingMachinesObservedEvent.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (in `PollMarkersAsync`)
- Test: `tests/RustPlusBot.Features.Connections.Tests/ConnectionSupervisorTests.cs`

**Interfaces:**
- Consumes: `MapMarkersSnapshot` (Task 1), `IEventBus`.
- Produces: `VendingMachinesObservedEvent(ulong GuildId, Guid ServerId, uint WorldSize, IReadOnlyList<VendingMachineSnapshot> Machines)`.

- [ ] **Step 1: Write the failing test**

Add to `ConnectionSupervisorTests.cs` (follow the file's existing harness for building a supervisor with a fake connection and a real `InMemoryEventBus`):

```csharp
    [Fact]
    public async Task PollMarkers_PublishesObservedVendingMachines()
    {
        await using var harness = Harness.Create();
        harness.Connection
            .GetMapMarkersAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(new MapMarkersSnapshot([], [
                new VendingMachineSnapshot(42UL, 100f, 200f, "Bob's Shop", false, [
                    new VendingOfferSnapshot(69511070, false, 1, -932201673, false, 12, 5)
                ])
            ]));

        var observed = await harness.WaitForEventAsync<VendingMachinesObservedEvent>();

        Assert.Equal(harness.ServerId, observed.ServerId);
        var machine = Assert.Single(observed.Machines);
        Assert.Equal(42UL, machine.Id);
        Assert.Equal("Bob's Shop", machine.Name);
    }
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Connections.Tests --filter PollMarkers_PublishesObservedVendingMachines`
Expected: FAIL — `VendingMachinesObservedEvent` does not exist.

- [ ] **Step 3: Create the event**

`src/RustPlusBot.Abstractions/Events/VendingMachinesObservedEvent.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Events;

/// <summary>
/// Published on every marker poll with the server's complete vending-machine set. Rust re-sends the
/// full set each poll, so consumers replace their state wholesale rather than diffing.
/// </summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The target server id.</param>
/// <param name="WorldSize">The world size in game units, for grid-label maths; 0 when dimensions are unavailable.</param>
/// <param name="Machines">Every player vending machine observed in this poll.</param>
public sealed record VendingMachinesObservedEvent(
    ulong GuildId,
    Guid ServerId,
    uint WorldSize,
    IReadOnlyList<VendingMachineSnapshot> Machines);
```

- [ ] **Step 4: Publish it from the poll**

In `PollMarkersAsync`, immediately after `var current = snapshot.Markers;`:

```csharp
                // Published every poll, not only on change: the index is a wholesale replacement and the
                // evaluators are pure, so change detection lives downstream where it is unit-testable.
                await bus.PublishAsync(
                        new VendingMachinesObservedEvent(
                            key.Guild, key.Server, localDims?.WorldSize ?? 0u, snapshot.VendingMachines),
                        ct)
                    .ConfigureAwait(false);
```

Use whichever field name the class already holds the `IEventBus` in — check the top of `ConnectionSupervisor` and match it.

- [ ] **Step 5: Run the test**

Run: `dtk dotnet test tests/RustPlusBot.Features.Connections.Tests --filter PollMarkers_PublishesObservedVendingMachines`
Expected: PASS

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Abstractions/Events src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat: publish observed vending machines from the marker poll"
```

---

### Task 3: Vending contracts in Abstractions

Pure types with no dependencies, so the command handlers can consume the feature without `Features.Commands` referencing `Features.Vending`.

**Files:**
- Create: `src/RustPlusBot.Abstractions/Vending/ListingKey.cs`
- Create: `src/RustPlusBot.Abstractions/Vending/UnitPrice.cs`
- Create: `src/RustPlusBot.Abstractions/Vending/VendingOffer.cs`
- Create: `src/RustPlusBot.Abstractions/Vending/IVendingReadModel.cs`
- Create: `src/RustPlusBot.Abstractions/Vending/IVendingTrackService.cs`
- Create: `tests/RustPlusBot.Abstractions.Tests/UnitPriceTests.cs`

**Interfaces:**
- Produces: `ListingKey`, `UnitPrice.IsAtOrBelow`, `UnitPrice.Compare`, `VendingOffer`, `IVendingReadModel`, `IVendingTrackService`, `GridTrackResult`, `VendingTrackSummary`, `TrackedListing`.

- [ ] **Step 1: Write the failing test for exact price comparison**

`tests/RustPlusBot.Abstractions.Tests/UnitPriceTests.cs`:

```csharp
using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Abstractions.Tests;

/// <summary>Unit tests for <see cref="UnitPrice"/>.</summary>
public sealed class UnitPriceTests
{
    [Fact]
    public void IsAtOrBelow_BundledCheaperOffer_Undercuts()
    {
        // 2 for 10 = 5 each, versus 1 for 6 = 6 each.
        Assert.True(UnitPrice.IsAtOrBelow(costA: 10, qtyA: 2, costB: 6, qtyB: 1));
    }

    [Fact]
    public void IsAtOrBelow_EqualUnitPrice_CountsAsUndercut()
    {
        // "less or the same resources" — matching the price is still a threat.
        Assert.True(UnitPrice.IsAtOrBelow(costA: 10, qtyA: 2, costB: 5, qtyB: 1));
    }

    [Fact]
    public void IsAtOrBelow_DearerBundle_DoesNotUndercut()
    {
        // 3 for 16 = 5.33 each, versus 1 for 5. Float rounding must not make this look equal.
        Assert.False(UnitPrice.IsAtOrBelow(costA: 16, qtyA: 3, costB: 5, qtyB: 1));
    }

    [Fact]
    public void Compare_OrdersByUnitPriceNotOrderCost()
    {
        // 20 for 100 (5 each) is cheaper than 1 for 6, despite the far larger order cost.
        Assert.True(UnitPrice.Compare(100, 20, 6, 1) < 0);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dtk dotnet test tests/RustPlusBot.Abstractions.Tests --filter UnitPriceTests`
Expected: FAIL — `UnitPrice` does not exist. If `tests/RustPlusBot.Abstractions.Tests` does not exist yet, create it by copying the `.csproj` from `tests/RustPlusBot.Features.Alarms.Tests`, changing the `ProjectReference` to `RustPlusBot.Abstractions`, and adding it to `RustPlusBot.slnx`.

- [ ] **Step 3: Write the contracts**

`ListingKey.cs`:

```csharp
namespace RustPlusBot.Abstractions.Vending;

/// <summary>
/// The identity of a tradeable listing. Prices are only ever compared within one key, which is what
/// keeps scrap from being compared to cloth and a blueprint from being compared to the item itself.
/// </summary>
/// <param name="ItemId">The Rust item id being sold.</param>
/// <param name="ItemIsBlueprint">True when the item sold is a blueprint.</param>
/// <param name="CurrencyId">The Rust item id accepted as payment.</param>
/// <param name="CurrencyIsBlueprint">True when the currency is a blueprint.</param>
public readonly record struct ListingKey(
    int ItemId,
    bool ItemIsBlueprint,
    int CurrencyId,
    bool CurrencyIsBlueprint);
```

`UnitPrice.cs`:

```csharp
namespace RustPlusBot.Abstractions.Vending;

/// <summary>
/// Exact per-item price comparison. A listing is "<c>Cost</c> currency for <c>Qty</c> items", so the
/// unit price is a ratio; comparing the ratios by cross-multiplication in <see cref="long"/> keeps the
/// comparison exact, whereas dividing would let rounding decide whether someone has undercut you.
/// </summary>
public static class UnitPrice
{
    /// <summary>True when offer A's unit price is at or below offer B's.</summary>
    /// <param name="costA">Currency charged for one order of A.</param>
    /// <param name="qtyA">Items yielded by one order of A; must be at least 1.</param>
    /// <param name="costB">Currency charged for one order of B.</param>
    /// <param name="qtyB">Items yielded by one order of B; must be at least 1.</param>
    /// <returns>True when <c>costA / qtyA &lt;= costB / qtyB</c>.</returns>
    public static bool IsAtOrBelow(int costA, int qtyA, int costB, int qtyB) =>
        (long)costA * qtyB <= (long)costB * qtyA;

    /// <summary>Compares two offers by unit price, for sorting.</summary>
    /// <param name="costA">Currency charged for one order of A.</param>
    /// <param name="qtyA">Items yielded by one order of A; must be at least 1.</param>
    /// <param name="costB">Currency charged for one order of B.</param>
    /// <param name="qtyB">Items yielded by one order of B; must be at least 1.</param>
    /// <returns>Negative when A is cheaper per item, 0 when equal, positive when dearer.</returns>
    public static int Compare(int costA, int qtyA, int costB, int qtyB) =>
        ((long)costA * qtyB).CompareTo((long)costB * qtyA);
}
```

`VendingOffer.cs`:

```csharp
namespace RustPlusBot.Abstractions.Vending;

/// <summary>One sell order, resolved to a grid reference and ready to display or compare.</summary>
/// <param name="MachineId">The vending machine's marker id.</param>
/// <param name="ShopName">The shopfront name, if any.</param>
/// <param name="Grid">The grid reference of the machine, e.g. "D7".</param>
/// <param name="Key">The listing identity.</param>
/// <param name="Quantity">Items yielded by one order; always at least 1.</param>
/// <param name="CostPerOrder">Currency charged for one order.</param>
/// <param name="AmountInStock">Orders remaining; 0 means sold out.</param>
public sealed record VendingOffer(
    ulong MachineId,
    string? ShopName,
    string Grid,
    ListingKey Key,
    int Quantity,
    int CostPerOrder,
    int AmountInStock)
{
    /// <summary>True when at least one order remains.</summary>
    public bool InStock => AmountInStock > 0;
}
```

`IVendingReadModel.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;

namespace RustPlusBot.Abstractions.Vending;

/// <summary>Reads the live vending index built from the marker poll. Singleton; synchronous.</summary>
public interface IVendingReadModel
{
    /// <summary>True when a poll has been observed for this server since the last connect.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <returns>True when the index holds data.</returns>
    bool HasData(ulong guildId, Guid serverId);

    /// <summary>
    /// Finds every offer of an item, in-stock first and then cheapest per item.
    /// </summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="itemId">The Rust item id to search for.</param>
    /// <param name="gridStyle">Which grid convention to label positions with.</param>
    /// <returns>The matching offers in display order; empty when nothing matches or no poll has landed.</returns>
    IReadOnlyList<VendingOffer> Search(ulong guildId, Guid serverId, int itemId, MapGridStyle gridStyle);
}
```

`IVendingTrackService.cs`:

```csharp
namespace RustPlusBot.Abstractions.Vending;

/// <summary>Registering and listing the vending listings a team tracks. Scoped.</summary>
public interface IVendingTrackService
{
    /// <summary>Registers a grid cell so every machine inside it counts as the team's own.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="grid">The grid reference, e.g. "D7"; case-insensitive.</param>
    /// <param name="steamId">The Steam id of the registering player.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The outcome, including how many machines and listings the cell holds right now.</returns>
    Task<GridTrackResult> TrackGridAsync(ulong guildId, Guid serverId, string grid, ulong steamId, CancellationToken ct);

    /// <summary>Unregisters a grid cell.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="grid">The grid reference to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True when a registration was removed; false when the cell was not registered.</returns>
    Task<bool> UntrackGridAsync(ulong guildId, Guid serverId, string grid, CancellationToken ct);

    /// <summary>Registers (or reprices) a listing the team sells, with no machine required.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="key">The listing identity.</param>
    /// <param name="quantity">Items yielded by one order; must be at least 1.</param>
    /// <param name="costPerOrder">Currency charged for one order.</param>
    /// <param name="userId">The Discord user registering it.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when persisted.</returns>
    Task TrackListingAsync(
        ulong guildId,
        Guid serverId,
        ListingKey key,
        int quantity,
        int costPerOrder,
        ulong userId,
        CancellationToken ct);

    /// <summary>Unregisters a manually tracked listing.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="key">The listing identity to remove.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>True when a listing was removed.</returns>
    Task<bool> UntrackListingAsync(ulong guildId, Guid serverId, ListingKey key, CancellationToken ct);

    /// <summary>Lists everything the team currently tracks on a server.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The registered grid cells and manual listings.</returns>
    Task<VendingTrackSummary> GetTrackedAsync(ulong guildId, Guid serverId, CancellationToken ct);
}

/// <summary>The outcome of registering a grid cell.</summary>
/// <param name="GridValid">False when the reference does not exist on this map, or the world size is unknown.</param>
/// <param name="MachinesFound">Machines currently standing in the cell.</param>
/// <param name="ListingsTracked">Distinct listings those machines currently offer.</param>
public sealed record GridTrackResult(bool GridValid, int MachinesFound, int ListingsTracked);

/// <summary>A manually registered listing.</summary>
/// <param name="Key">The listing identity.</param>
/// <param name="Quantity">Items yielded by one order.</param>
/// <param name="CostPerOrder">Currency charged for one order.</param>
public sealed record TrackedListing(ListingKey Key, int Quantity, int CostPerOrder);

/// <summary>Everything a team tracks on one server.</summary>
/// <param name="Grids">Registered grid references, ascending.</param>
/// <param name="Listings">Manually registered listings.</param>
public sealed record VendingTrackSummary(
    IReadOnlyList<string> Grids,
    IReadOnlyList<TrackedListing> Listings);
```

- [ ] **Step 4: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Abstractions.Tests --filter UnitPriceTests`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Abstractions/Vending tests/RustPlusBot.Abstractions.Tests RustPlusBot.slnx
git commit -m "feat: add vending listing contracts and exact unit-price comparison"
```

---

### Task 4: Domain entities, store, and migration

**Files:**
- Create: `src/RustPlusBot.Domain/Vending/VendingGridTrack.cs`, `VendingListingTrack.cs`, `VendingNotification.cs`, `VendingStockNotification.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/VendingGridTrackConfiguration.cs`, `VendingListingTrackConfiguration.cs`, `VendingNotificationConfiguration.cs`, `VendingStockNotificationConfiguration.cs`
- Create: `src/RustPlusBot.Persistence/Vending/IVendingStore.cs`, `VendingStore.cs`
- Modify: `src/RustPlusBot.Persistence/BotDbContext.cs` (four `DbSet` properties)
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs` (register `IVendingStore`)
- Test: `tests/RustPlusBot.Persistence.Tests/VendingStoreTests.cs`

**Interfaces:**
- Consumes: `ListingKey` (Task 3).
- Produces: `IVendingStore` with `ListGridsAsync`, `AddGridAsync`, `RemoveGridAsync`, `ListListingsAsync`, `UpsertListingAsync`, `RemoveListingAsync`, `ListNotificationsAsync`, `UpsertNotificationAsync`, `RemoveNotificationAsync`, `ListStockNotificationsAsync`, `UpsertStockNotificationAsync`, `RemoveStockNotificationAsync`, `PurgeGridsAsync`.

- [ ] **Step 1: Write the failing store test**

`tests/RustPlusBot.Persistence.Tests/VendingStoreTests.cs` (follow `ClanStoreTests.cs` for the `SqliteContextFixture` usage):

```csharp
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Persistence.Tests;

/// <summary>Unit tests for <see cref="VendingStore"/>.</summary>
public sealed class VendingStoreTests(SqliteContextFixture fixture) : IClassFixture<SqliteContextFixture>
{
    private static readonly ListingKey Pipe = new(69511070, false, -932201673, false);

    [Fact]
    public async Task AddGrid_IsIdempotentPerCell()
    {
        await using var context = fixture.CreateContext();
        var serverId = await fixture.SeedServerAsync(context);
        var store = new VendingStore(context);

        await store.AddGridAsync(10UL, serverId, "D7", 1UL, TestContext.Current.CancellationToken);
        await store.AddGridAsync(10UL, serverId, "D7", 2UL, TestContext.Current.CancellationToken);

        var grids = await store.ListGridsAsync(10UL, serverId, TestContext.Current.CancellationToken);
        Assert.Equal(["D7"], grids);
    }

    [Fact]
    public async Task UpsertListing_SecondCallRepricesRatherThanDuplicating()
    {
        await using var context = fixture.CreateContext();
        var serverId = await fixture.SeedServerAsync(context);
        var store = new VendingStore(context);

        await store.UpsertListingAsync(10UL, serverId, Pipe, 1, 12, 5UL, TestContext.Current.CancellationToken);
        await store.UpsertListingAsync(10UL, serverId, Pipe, 1, 9, 5UL, TestContext.Current.CancellationToken);

        var listing = Assert.Single(await store.ListListingsAsync(10UL, serverId, TestContext.Current.CancellationToken));
        Assert.Equal(9, listing.CostPerOrder);
    }
}
```

Match whatever server-seeding helper `SqliteContextFixture` already exposes; if it has none, seed a `RustServer` inline exactly as `ClanStoreTests` does.

- [ ] **Step 2: Run it and watch it fail**

Run: `dtk dotnet test tests/RustPlusBot.Persistence.Tests --filter VendingStoreTests`
Expected: FAIL — `VendingStore` does not exist.

- [ ] **Step 3: Write the domain entities**

`src/RustPlusBot.Domain/Vending/VendingGridTrack.cs`:

```csharp
namespace RustPlusBot.Domain.Vending;

/// <summary>A registered grid cell; every vending machine inside it counts as the team's own.</summary>
public sealed class VendingGridTrack
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this registration belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The grid reference, upper-cased, e.g. "D7".</summary>
    public string Grid { get; set; } = string.Empty;

    /// <summary>The Steam id of the player who registered the cell, for display.</summary>
    public ulong RegisteredBySteamId { get; set; }

    /// <summary>When the cell was registered (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; }
}
```

`VendingListingTrack.cs`:

```csharp
namespace RustPlusBot.Domain.Vending;

/// <summary>A listing the team sells, registered by hand rather than read off a machine.</summary>
public sealed class VendingListingTrack
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this listing belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The Rust item id being sold.</summary>
    public int ItemId { get; set; }

    /// <summary>True when the item sold is a blueprint.</summary>
    public bool ItemIsBlueprint { get; set; }

    /// <summary>The Rust item id accepted as payment.</summary>
    public int CurrencyId { get; set; }

    /// <summary>True when the currency is a blueprint.</summary>
    public bool CurrencyIsBlueprint { get; set; }

    /// <summary>Items yielded by one order; at least 1.</summary>
    public int Quantity { get; set; }

    /// <summary>Currency charged for one order.</summary>
    public int CostPerOrder { get; set; }

    /// <summary>The Discord user who registered the listing, for display.</summary>
    public ulong RegisteredByUserId { get; set; }

    /// <summary>When the listing was registered (UTC).</summary>
    public DateTimeOffset CreatedUtc { get; set; }
}
```

`VendingNotification.cs`:

```csharp
namespace RustPlusBot.Domain.Vending;

/// <summary>
/// A live undercut message in #vending. Stores the reference price it was rendered against so that
/// "did the owner reprice" is answerable without a second poll — an owner reprice deletes the message,
/// whereas a rival's move only edits it.
/// </summary>
public sealed class VendingNotification
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this notification belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The Rust item id being sold.</summary>
    public int ItemId { get; set; }

    /// <summary>True when the item sold is a blueprint.</summary>
    public bool ItemIsBlueprint { get; set; }

    /// <summary>The Rust item id accepted as payment.</summary>
    public int CurrencyId { get; set; }

    /// <summary>True when the currency is a blueprint.</summary>
    public bool CurrencyIsBlueprint { get; set; }

    /// <summary>The Discord message id of the posted notification.</summary>
    public ulong MessageId { get; set; }

    /// <summary>The owner's order quantity this message was rendered against.</summary>
    public int ReferenceQuantity { get; set; }

    /// <summary>The owner's order cost this message was rendered against.</summary>
    public int ReferenceCostPerOrder { get; set; }

    /// <summary>When the message was posted (UTC).</summary>
    public DateTimeOffset PostedUtc { get; set; }
}
```

`VendingStockNotification.cs`:

```csharp
namespace RustPlusBot.Domain.Vending;

/// <summary>
/// A live sell-out message in #vending, one per registered machine. Stores the sold-out set it was
/// rendered against so an owner restock deletes the message rather than silently editing it.
/// </summary>
public sealed class VendingStockNotification
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this notification belongs to (FK to RustServer, cascade delete).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The vending machine's marker id.</summary>
    public ulong MachineId { get; set; }

    /// <summary>The Discord message id of the posted notification.</summary>
    public ulong MessageId { get; set; }

    /// <summary>
    /// The sold-out set this message was rendered against: "*" when the whole machine was empty,
    /// otherwise the sold-out item ids sorted ascending and comma-joined. Compared whole, never parsed.
    /// </summary>
    public string SoldOutSignature { get; set; } = string.Empty;

    /// <summary>When the message was posted (UTC).</summary>
    public DateTimeOffset PostedUtc { get; set; }
}
```

> **Deviation from the spec, deliberate:** the spec's table lists a separate `MachineEmpty` flag on this
> entity. `SoldOutSignature == "*"` already means "the whole machine was empty", so a second column would
> be a second source of truth for the same fact and could disagree with it. The flag is dropped; the
> signature carries it.

- [ ] **Step 4: Write the four EF configurations**

Each follows `SmartAlarmConfiguration`. `VendingGridTrackConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Vending;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class VendingGridTrackConfiguration : IEntityTypeConfiguration<VendingGridTrack>
{
    public void Configure(EntityTypeBuilder<VendingGridTrack> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Grid).IsRequired().HasMaxLength(8);
        builder.HasIndex(g => new { g.GuildId, g.ServerId, g.Grid }).IsUnique();

        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(g => g.ServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`VendingListingTrackConfiguration` and `VendingNotificationConfiguration` use the same shape with

```csharp
        builder.HasIndex(x => new
        {
            x.GuildId, x.ServerId, x.ItemId, x.ItemIsBlueprint, x.CurrencyId, x.CurrencyIsBlueprint
        }).IsUnique();
```

and `VendingStockNotificationConfiguration` uses

```csharp
        builder.Property(s => s.SoldOutSignature).IsRequired().HasMaxLength(512);
        builder.HasIndex(s => new { s.GuildId, s.ServerId, s.MachineId }).IsUnique();
```

All four keep the same cascade `HasOne<RustServer>()` block.

- [ ] **Step 5: Add the DbSets**

In `BotDbContext.cs`, alongside the existing sets:

```csharp
    /// <summary>Registered vending grid cells.</summary>
    public DbSet<VendingGridTrack> VendingGridTracks => Set<VendingGridTrack>();

    /// <summary>Manually registered vending listings.</summary>
    public DbSet<VendingListingTrack> VendingListingTracks => Set<VendingListingTrack>();

    /// <summary>Live undercut notifications.</summary>
    public DbSet<VendingNotification> VendingNotifications => Set<VendingNotification>();

    /// <summary>Live sell-out notifications.</summary>
    public DbSet<VendingStockNotification> VendingStockNotifications => Set<VendingStockNotification>();
```

Add `using RustPlusBot.Domain.Vending;` at the top. Configurations are picked up by the existing `ApplyConfigurationsFromAssembly` call — confirm that call exists in `OnModelCreating`; if the context registers configurations one by one instead, add all four explicitly.

- [ ] **Step 6: Write the store**

`src/RustPlusBot.Persistence/Vending/IVendingStore.cs` declares the thirteen methods listed in this task's **Produces** block; `VendingStore.cs` implements them against `BotDbContext`. The two that carry real logic:

```csharp
    /// <inheritdoc />
    public async Task AddGridAsync(ulong guildId, Guid serverId, string grid, ulong steamId, CancellationToken ct)
    {
        var normalized = Normalize(grid);
        var existing = await context.VendingGridTracks
            .FirstOrDefaultAsync(g => g.GuildId == guildId && g.ServerId == serverId && g.Grid == normalized, ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            return; // Re-registering a cell is a no-op, not an error: !vtrack is a natural thing to repeat.
        }

        context.VendingGridTracks.Add(new VendingGridTrack
        {
            GuildId = guildId,
            ServerId = serverId,
            Grid = normalized,
            RegisteredBySteamId = steamId,
            CreatedUtc = clock.UtcNow,
        });
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpsertListingAsync(
        ulong guildId, Guid serverId, ListingKey key, int quantity, int costPerOrder, ulong userId,
        CancellationToken ct)
    {
        var row = await context.VendingListingTracks
            .FirstOrDefaultAsync(
                l => l.GuildId == guildId && l.ServerId == serverId
                     && l.ItemId == key.ItemId && l.ItemIsBlueprint == key.ItemIsBlueprint
                     && l.CurrencyId == key.CurrencyId && l.CurrencyIsBlueprint == key.CurrencyIsBlueprint,
                ct)
            .ConfigureAwait(false);

        if (row is null)
        {
            row = new VendingListingTrack
            {
                GuildId = guildId,
                ServerId = serverId,
                ItemId = key.ItemId,
                ItemIsBlueprint = key.ItemIsBlueprint,
                CurrencyId = key.CurrencyId,
                CurrencyIsBlueprint = key.CurrencyIsBlueprint,
                RegisteredByUserId = userId,
                CreatedUtc = clock.UtcNow,
            };
            context.VendingListingTracks.Add(row);
        }

        row.Quantity = Math.Max(1, quantity);
        row.CostPerOrder = costPerOrder;
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static string Normalize(string grid) => grid.Trim().ToUpperInvariant();
```

The `Upsert*NotificationAsync` pair follows the same find-then-update shape keyed on `(GuildId, ServerId, ListingKey)` and `(GuildId, ServerId, MachineId)`. `PurgeGridsAsync` removes every `VendingGridTrack` for a server via `ExecuteDeleteAsync`. Constructor is `VendingStore(BotDbContext context, IClock clock)`.

- [ ] **Step 7: Register the store**

In `PersistenceServiceCollectionExtensions.AddBotPersistence`, next to the other stores:

```csharp
        services.AddScoped<IVendingStore, VendingStore>();
```

- [ ] **Step 8: Run the store tests**

Run: `dtk dotnet test tests/RustPlusBot.Persistence.Tests --filter VendingStoreTests`
Expected: PASS (2 tests)

- [ ] **Step 9: Generate the migration**

```bash
dotnet tool restore
dotnet ef migrations add VendingTracking \
  --project src/RustPlusBot.Persistence \
  --startup-project src/RustPlusBot.Host
```

Open the generated `*_VendingTracking.cs` and confirm it creates exactly four tables with the four unique indexes and cascade foreign keys, and drops nothing.

- [ ] **Step 10: Run the full suite**

Run: `dtk dotnet test RustPlusBot.slnx`
Expected: PASS. `BotDbContextTests` and `PersistenceRegistrationTests` also exercise the new model.

- [ ] **Step 11: Commit**

```bash
git add src/RustPlusBot.Domain/Vending src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests
git commit -m "feat: persist vending track registrations and notification message ids"
```

---

### Task 5: Grid ownership and the offer view builder

The first piece of the new feature project. Pure functions — no DI, no I/O.

**Files:**
- Create: `src/RustPlusBot.Features.Vending/RustPlusBot.Features.Vending.csproj`
- Create: `src/RustPlusBot.Features.Vending/Ownership/GridOwnership.cs`
- Create: `tests/RustPlusBot.Features.Vending.Tests/RustPlusBot.Features.Vending.Tests.csproj`
- Create: `tests/RustPlusBot.Features.Vending.Tests/GridOwnershipTests.cs`
- Modify: `RustPlusBot.slnx`

**Interfaces:**
- Consumes: `VendingMachineSnapshot` (Task 1), `VendingOffer`, `ListingKey` (Task 3), `MapGrid`, `MapGridStyle`.
- Produces: `GridOwnership.GridOf(machine, worldSize, style)`, `GridOwnership.IsOwned(machine, worldSize, style, grids)`, `GridOwnership.ToOffers(machine, worldSize, style)`.

- [ ] **Step 1: Create the two projects**

`src/RustPlusBot.Features.Vending/RustPlusBot.Features.Vending.csproj` — copy `RustPlusBot.Features.Alarms.csproj` verbatim, changing the `InternalsVisibleTo` to `RustPlusBot.Features.Vending.Tests`. The same seven project references apply.

`tests/RustPlusBot.Features.Vending.Tests/RustPlusBot.Features.Vending.Tests.csproj` — copy `RustPlusBot.Features.Alarms.Tests.csproj`, pointing its `ProjectReference` at `RustPlusBot.Features.Vending`.

Add both to `RustPlusBot.slnx` beside the Alarms entries (lines 11 and 41).

- [ ] **Step 2: Write the failing test**

`tests/RustPlusBot.Features.Vending.Tests/GridOwnershipTests.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Vending.Ownership;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="GridOwnership"/>.</summary>
public sealed class GridOwnershipTests
{
    private const uint WorldSize = 4000;

    private static VendingMachineSnapshot Machine(float x, float y) =>
        new(1UL, x, y, "Shop", false, [new VendingOfferSnapshot(69511070, false, 2, -932201673, false, 10, 3)]);

    [Fact]
    public void GridOf_AgreesWithMapGrid()
    {
        var machine = Machine(500f, 3000f);
        Assert.Equal(
            MapGrid.LabelFor(500f, 3000f, WorldSize, MapGridStyle.InGame),
            GridOwnership.GridOf(machine, WorldSize, MapGridStyle.InGame));
    }

    [Fact]
    public void GridOf_DiffersBetweenGridStyles_AtTheInsetBoundary()
    {
        // The Rust+ style shifts rows 100 units south of the in-game style, so a machine just below a
        // row boundary lands in different rows under the two conventions.
        var machine = Machine(500f, WorldSize - 50f);
        Assert.NotEqual(
            GridOwnership.GridOf(machine, WorldSize, MapGridStyle.InGame),
            GridOwnership.GridOf(machine, WorldSize, MapGridStyle.RustPlus));
    }

    [Fact]
    public void IsOwned_MatchesRegisteredCellCaseInsensitively()
    {
        var machine = Machine(500f, 3000f);
        var grid = GridOwnership.GridOf(machine, WorldSize, MapGridStyle.InGame);
        Assert.True(GridOwnership.IsOwned(machine, WorldSize, MapGridStyle.InGame,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { grid.ToLowerInvariant() }));
    }

    [Fact]
    public void IsOwned_UnregisteredCell_IsFalse()
    {
        var machine = Machine(500f, 3000f);
        Assert.False(GridOwnership.IsOwned(machine, WorldSize, MapGridStyle.InGame,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ZZ99" }));
    }

    [Fact]
    public void ToOffers_CarriesGridShopNameAndListingKey()
    {
        var offer = Assert.Single(GridOwnership.ToOffers(Machine(500f, 3000f), WorldSize, MapGridStyle.InGame));

        Assert.Equal("Shop", offer.ShopName);
        Assert.Equal(new ListingKey(69511070, false, -932201673, false), offer.Key);
        Assert.Equal(2, offer.Quantity);
        Assert.Equal(10, offer.CostPerOrder);
        Assert.True(offer.InStock);
    }
}
```

- [ ] **Step 3: Run it and watch it fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter GridOwnershipTests`
Expected: FAIL — `GridOwnership` does not exist.

- [ ] **Step 4: Implement**

`src/RustPlusBot.Features.Vending/Ownership/GridOwnership.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Features.Vending.Ownership;

/// <summary>
/// Decides which machines belong to the team and projects them into comparable offers. Ownership is by
/// grid cell rather than by machine id, so a machine deployed after registration is picked up on the
/// next poll without anyone re-registering.
/// </summary>
internal static class GridOwnership
{
    /// <summary>Gets the grid reference a machine stands in.</summary>
    /// <param name="machine">The observed machine.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin against.</param>
    /// <returns>The grid label, e.g. "D7".</returns>
    public static string GridOf(VendingMachineSnapshot machine, uint worldSize, MapGridStyle style)
    {
        ArgumentNullException.ThrowIfNull(machine);
        return MapGrid.LabelFor(machine.X, machine.Y, worldSize, style);
    }

    /// <summary>True when the machine stands in one of the registered cells.</summary>
    /// <param name="machine">The observed machine.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin against.</param>
    /// <param name="grids">The registered cells; should use an ordinal-ignore-case comparer.</param>
    /// <returns>True when the machine is the team's own.</returns>
    public static bool IsOwned(
        VendingMachineSnapshot machine,
        uint worldSize,
        MapGridStyle style,
        IReadOnlySet<string> grids)
    {
        ArgumentNullException.ThrowIfNull(grids);
        return grids.Contains(GridOf(machine, worldSize, style));
    }

    /// <summary>Projects a machine's sell orders into grid-resolved offers.</summary>
    /// <param name="machine">The observed machine.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin against.</param>
    /// <returns>One offer per sell order.</returns>
    public static IReadOnlyList<VendingOffer> ToOffers(
        VendingMachineSnapshot machine,
        uint worldSize,
        MapGridStyle style)
    {
        ArgumentNullException.ThrowIfNull(machine);
        var grid = GridOf(machine, worldSize, style);
        return machine.Offers
            .Select(o => new VendingOffer(
                machine.Id,
                machine.Name,
                grid,
                new ListingKey(o.ItemId, o.ItemIsBlueprint, o.CurrencyId, o.CurrencyIsBlueprint),
                o.Quantity,
                o.CostPerOrder,
                o.AmountInStock))
            .ToList();
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter GridOwnershipTests`
Expected: PASS (5 tests)

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Vending tests/RustPlusBot.Features.Vending.Tests RustPlusBot.slnx
git commit -m "feat: add grid-cell ownership and vending offer projection"
```

---

### Task 6: Search ordering

**Files:**
- Create: `src/RustPlusBot.Features.Vending/Searching/VendingSearch.cs`
- Create: `tests/RustPlusBot.Features.Vending.Tests/VendingSearchTests.cs`

**Interfaces:**
- Consumes: `VendingOffer`, `UnitPrice` (Task 3).
- Produces: `VendingSearch.Order(IEnumerable<VendingOffer>)`, `VendingSearch.Take(IReadOnlyList<VendingOffer>, int limit)` returning `(IReadOnlyList<VendingOffer> Shown, int More)`.

- [ ] **Step 1: Write the failing test**

`tests/RustPlusBot.Features.Vending.Tests/VendingSearchTests.cs`:

```csharp
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Searching;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingSearch"/>.</summary>
public sealed class VendingSearchTests
{
    private static readonly ListingKey Pipe = new(69511070, false, -932201673, false);

    private static VendingOffer Offer(string grid, int qty, int cost, int stock) =>
        new(1UL, "Shop", grid, Pipe, qty, cost, stock);

    [Fact]
    public void Order_PutsInStockBeforeSoldOut_EvenWhenDearer()
    {
        var ordered = VendingSearch.Order([Offer("A1", 1, 20, 5), Offer("B2", 1, 5, 0)]);

        Assert.Equal("A1", ordered[0].Grid);
        Assert.Equal("B2", ordered[1].Grid);
    }

    [Fact]
    public void Order_SortsByUnitPriceNotOrderCost()
    {
        // 20 for 100 is 5 each and must beat 1 for 6, despite costing far more per order.
        var ordered = VendingSearch.Order([Offer("A1", 1, 6, 5), Offer("B2", 20, 100, 5)]);

        Assert.Equal("B2", ordered[0].Grid);
    }

    [Fact]
    public void Order_TiesBreakByGrid_SoResultsAreStable()
    {
        var ordered = VendingSearch.Order([Offer("K12", 1, 5, 5), Offer("B3", 1, 5, 5)]);

        Assert.Equal("B3", ordered[0].Grid);
    }

    [Fact]
    public void Take_ReportsHowManyWereNotShown()
    {
        var offers = VendingSearch.Order([
            Offer("A1", 1, 5, 5), Offer("B2", 1, 6, 5), Offer("C3", 1, 7, 5), Offer("D4", 1, 8, 5)
        ]);

        var (shown, more) = VendingSearch.Take(offers, 3);

        Assert.Equal(3, shown.Count);
        Assert.Equal(1, more);
    }

    [Fact]
    public void Take_UnderLimit_ReportsNoneHidden()
    {
        var (shown, more) = VendingSearch.Take([Offer("A1", 1, 5, 5)], 10);

        Assert.Single(shown);
        Assert.Equal(0, more);
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingSearchTests`
Expected: FAIL — `VendingSearch` does not exist.

- [ ] **Step 3: Implement**

`src/RustPlusBot.Features.Vending/Searching/VendingSearch.cs`:

```csharp
using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Features.Vending.Searching;

/// <summary>Orders and truncates search results. Pure.</summary>
internal static class VendingSearch
{
    /// <summary>Orders offers in-stock first, then cheapest per item, then by grid for stability.</summary>
    /// <param name="offers">The matching offers.</param>
    /// <returns>The offers in display order.</returns>
    public static IReadOnlyList<VendingOffer> Order(IEnumerable<VendingOffer> offers)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return offers
            .OrderByDescending(o => o.InStock)
            .ThenBy(o => o, UnitPriceComparer.Instance)
            .ThenBy(o => o.Grid, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Truncates to a limit, reporting how many were hidden.</summary>
    /// <param name="offers">The ordered offers.</param>
    /// <param name="limit">The maximum to show.</param>
    /// <returns>The shown offers and the count omitted.</returns>
    public static (IReadOnlyList<VendingOffer> Shown, int More) Take(IReadOnlyList<VendingOffer> offers, int limit)
    {
        ArgumentNullException.ThrowIfNull(offers);
        return offers.Count <= limit
            ? (offers, 0)
            : (offers.Take(limit).ToList(), offers.Count - limit);
    }

    private sealed class UnitPriceComparer : IComparer<VendingOffer>
    {
        public static readonly UnitPriceComparer Instance = new();

        public int Compare(VendingOffer? x, VendingOffer? y) =>
            x is null || y is null
                ? 0
                : UnitPrice.Compare(x.CostPerOrder, x.Quantity, y.CostPerOrder, y.Quantity);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingSearchTests`
Expected: PASS (5 tests)

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Vending/Searching tests/RustPlusBot.Features.Vending.Tests/VendingSearchTests.cs
git commit -m "feat: order vending search results by availability then unit price"
```

---

### Task 7: The undercut evaluator

The heart of the feature. Pure: (machines, world size, style, tracks) → desired undercut notices.

**Files:**
- Create: `src/RustPlusBot.Features.Vending/Evaluating/UndercutNotice.cs`
- Create: `src/RustPlusBot.Features.Vending/Evaluating/UndercutEvaluator.cs`
- Create: `tests/RustPlusBot.Features.Vending.Tests/UndercutEvaluatorTests.cs`

**Interfaces:**
- Consumes: `GridOwnership` (Task 5), `VendingSearch.Order` (Task 6), `UnitPrice`, `TrackedListing`.
- Produces: `UndercutNotice(ListingKey Key, int ReferenceQuantity, int ReferenceCostPerOrder, IReadOnlyList<VendingOffer> Undercutters)`; `UndercutEvaluator.Evaluate(machines, worldSize, style, grids, listings)` returning `IReadOnlyList<UndercutNotice>`.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Vending.Tests/UndercutEvaluatorTests.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Evaluating;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="UndercutEvaluator"/>.</summary>
public sealed class UndercutEvaluatorTests
{
    private const uint WorldSize = 4000;
    private const int Pipe = 69511070;
    private const int Scrap = -932201673;
    private const int Cloth = -858312878;

    // Two positions far enough apart to land in different grid cells (one cell is 146.25 units).
    private const float MineX = 500f, MineY = 3000f;
    private const float RivalX = 2000f, RivalY = 1000f;

    private static readonly HashSet<string> MyGrid = new(StringComparer.OrdinalIgnoreCase)
    {
        MapGrid.LabelFor(MineX, MineY, WorldSize, MapGridStyle.InGame),
    };

    private static VendingMachineSnapshot Machine(
        ulong id, float x, float y, params VendingOfferSnapshot[] offers) =>
        new(id, x, y, $"Shop{id}", false, offers);

    private static VendingOfferSnapshot Sell(
        int qty, int cost, int stock, int currency = Scrap, int item = Pipe) =>
        new(item, false, qty, currency, false, cost, stock);

    private static IReadOnlyList<UndercutNotice> Evaluate(params VendingMachineSnapshot[] machines) =>
        UndercutEvaluator.Evaluate(machines, WorldSize, MapGridStyle.InGame, MyGrid, []);

    [Fact]
    public void CheaperRival_RaisesNotice()
    {
        var notice = Assert.Single(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 8, 5))));

        Assert.Equal(Pipe, notice.Key.ItemId);
        Assert.Equal(10, notice.ReferenceCostPerOrder);
        Assert.Equal(2UL, Assert.Single(notice.Undercutters).MachineId);
    }

    [Fact]
    public void EqualPriceRival_RaisesNotice()
    {
        var notice = Assert.Single(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 10, 5))));

        Assert.Single(notice.Undercutters);
    }

    [Fact]
    public void DearerRival_RaisesNothing()
    {
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 12, 5))));
    }

    [Fact]
    public void ReferenceIsMyLowestPrice_SoAMidPricedRivalIsNotAThreat()
    {
        // I sell at 5 and at 6; a rival at 6 has not beaten my best price.
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 5, 5)),
            Machine(3UL, MineX, MineY, Sell(1, 6, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 6, 5))));
    }

    [Fact]
    public void SoldOutRival_RaisesNothing()
    {
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 1, 0))));
    }

    [Fact]
    public void DifferentCurrency_IsNeverCompared()
    {
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 1, 5, currency: Cloth))));
    }

    [Fact]
    public void BlueprintAndItem_AreDistinctListings()
    {
        var mine = Machine(1UL, MineX, MineY, new VendingOfferSnapshot(Pipe, true, 1, Scrap, false, 10, 5));
        var rival = Machine(2UL, RivalX, RivalY, new VendingOfferSnapshot(Pipe, false, 1, Scrap, false, 1, 5));

        Assert.Empty(Evaluate(mine, rival));
    }

    [Fact]
    public void BundledRival_UndercutsOnUnitPrice()
    {
        // Rival sells 2 for 10 (5 each) against my 1 for 6.
        var notice = Assert.Single(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 6, 5)),
            Machine(2UL, RivalX, RivalY, Sell(2, 10, 5))));

        Assert.Single(notice.Undercutters);
    }

    [Fact]
    public void MachineInMyGrid_IsNeverAnUndercutter()
    {
        Assert.Empty(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(3UL, MineX, MineY, Sell(1, 1, 5))));
    }

    [Fact]
    public void ManualListing_IsTrackedWithoutAMachine()
    {
        var notices = UndercutEvaluator.Evaluate(
            [Machine(2UL, RivalX, RivalY, Sell(1, 4, 5))],
            WorldSize,
            MapGridStyle.InGame,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [new TrackedListing(new ListingKey(Pipe, false, Scrap, false), 1, 5)]);

        Assert.Single(Assert.Single(notices).Undercutters);
    }

    [Fact]
    public void Undercutters_AreOrderedCheapestFirst()
    {
        var notice = Assert.Single(Evaluate(
            Machine(1UL, MineX, MineY, Sell(1, 10, 5)),
            Machine(2UL, RivalX, RivalY, Sell(1, 9, 5)),
            Machine(4UL, RivalX + 500f, RivalY, Sell(1, 4, 5))));

        Assert.Equal(4UL, notice.Undercutters[0].MachineId);
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter UndercutEvaluatorTests`
Expected: FAIL — `UndercutEvaluator` does not exist.

- [ ] **Step 3: Write the notice record**

`src/RustPlusBot.Features.Vending/Evaluating/UndercutNotice.cs`:

```csharp
using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Features.Vending.Evaluating;

/// <summary>One listing of ours that rivals are matching or beating right now.</summary>
/// <param name="Key">The listing identity.</param>
/// <param name="ReferenceQuantity">Our best offer's order quantity.</param>
/// <param name="ReferenceCostPerOrder">Our best offer's order cost.</param>
/// <param name="Undercutters">The rival offers at or below our price, cheapest first.</param>
public sealed record UndercutNotice(
    ListingKey Key,
    int ReferenceQuantity,
    int ReferenceCostPerOrder,
    IReadOnlyList<VendingOffer> Undercutters);
```

- [ ] **Step 4: Implement the evaluator**

`src/RustPlusBot.Features.Vending/Evaluating/UndercutEvaluator.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Ownership;
using RustPlusBot.Features.Vending.Searching;

namespace RustPlusBot.Features.Vending.Evaluating;

/// <summary>
/// Works out which of our listings are being matched or beaten. Pure: the same inputs always give the
/// same notices, which is what lets the relay treat its output as a desired state to reconcile.
/// </summary>
internal static class UndercutEvaluator
{
    /// <summary>Evaluates undercuts across a server's observed machines.</summary>
    /// <param name="machines">Every machine observed in the latest poll.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin positions against.</param>
    /// <param name="grids">Our registered grid cells; use an ordinal-ignore-case set.</param>
    /// <param name="listings">Our manually registered listings.</param>
    /// <returns>One notice per listing of ours with at least one rival at or below our price.</returns>
    public static IReadOnlyList<UndercutNotice> Evaluate(
        IReadOnlyList<VendingMachineSnapshot> machines,
        uint worldSize,
        MapGridStyle style,
        IReadOnlySet<string> grids,
        IReadOnlyList<TrackedListing> listings)
    {
        ArgumentNullException.ThrowIfNull(machines);
        ArgumentNullException.ThrowIfNull(listings);

        var mine = new Dictionary<ListingKey, (int Quantity, int CostPerOrder)>();
        var theirs = new List<VendingOffer>();

        foreach (var machine in machines)
        {
            var owned = GridOwnership.IsOwned(machine, worldSize, style, grids);
            foreach (var offer in GridOwnership.ToOffers(machine, worldSize, style))
            {
                if (owned)
                {
                    Reference(mine, offer.Key, offer.Quantity, offer.CostPerOrder);
                }
                else
                {
                    theirs.Add(offer);
                }
            }
        }

        foreach (var listing in listings)
        {
            Reference(mine, listing.Key, listing.Quantity, listing.CostPerOrder);
        }

        var notices = new List<UndercutNotice>();
        foreach (var (key, reference) in mine)
        {
            // A sold-out rival takes none of our sales, so it is not a threat — but it is still a real
            // search result, which is why the exclusion lives here and not in the index.
            var undercutters = theirs
                .Where(o => o.Key == key
                            && o.InStock
                            && UnitPrice.IsAtOrBelow(
                                o.CostPerOrder, o.Quantity, reference.CostPerOrder, reference.Quantity))
                .ToList();

            if (undercutters.Count > 0)
            {
                notices.Add(new UndercutNotice(
                    key, reference.Quantity, reference.CostPerOrder, VendingSearch.Order(undercutters)));
            }
        }

        return notices;
    }

    // Our reference is our cheapest offer for a listing: that is what defines our competitiveness, so a
    // rival who beats our dearer duplicate but not our best price has not actually taken anything.
    private static void Reference(
        Dictionary<ListingKey, (int Quantity, int CostPerOrder)> mine,
        ListingKey key,
        int quantity,
        int costPerOrder)
    {
        if (!mine.TryGetValue(key, out var best)
            || UnitPrice.IsAtOrBelow(costPerOrder, quantity, best.CostPerOrder, best.Quantity))
        {
            mine[key] = (quantity, costPerOrder);
        }
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter UndercutEvaluatorTests`
Expected: PASS (11 tests)

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Vending/Evaluating tests/RustPlusBot.Features.Vending.Tests/UndercutEvaluatorTests.cs
git commit -m "feat: evaluate vending undercuts against our cheapest listing price"
```

---

### Task 8: The sell-out evaluator

**Files:**
- Create: `src/RustPlusBot.Features.Vending/Evaluating/StockNotice.cs`
- Create: `src/RustPlusBot.Features.Vending/Evaluating/StockEvaluator.cs`
- Create: `tests/RustPlusBot.Features.Vending.Tests/StockEvaluatorTests.cs`

**Interfaces:**
- Consumes: `GridOwnership` (Task 5), `VendingSearch.Order` (Task 6).
- Produces: `StockNotice(ulong MachineId, string? ShopName, string Grid, bool MachineEmpty, IReadOnlyList<VendingOffer> SoldOut)` with a `Signature` property; `StockEvaluator.Evaluate(machines, worldSize, style, grids)`.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Vending.Tests/StockEvaluatorTests.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Vending.Evaluating;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="StockEvaluator"/>.</summary>
public sealed class StockEvaluatorTests
{
    private const uint WorldSize = 4000;
    private const int Scrap = -932201673;
    private const float MineX = 500f, MineY = 3000f;
    private const float RivalX = 2000f, RivalY = 1000f;

    private static readonly HashSet<string> MyGrid = new(StringComparer.OrdinalIgnoreCase)
    {
        MapGrid.LabelFor(MineX, MineY, WorldSize, MapGridStyle.InGame),
    };

    private static VendingOfferSnapshot Sell(int item, int stock) =>
        new(item, false, 1, Scrap, false, 10, stock);

    private static IReadOnlyList<StockNotice> Evaluate(params VendingMachineSnapshot[] machines) =>
        StockEvaluator.Evaluate(machines, WorldSize, MapGridStyle.InGame, MyGrid);

    [Fact]
    public void SoldOutOffer_IsListed()
    {
        var notice = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", false, [Sell(101, 0), Sell(102, 4)])));

        Assert.False(notice.MachineEmpty);
        Assert.Equal(101, Assert.Single(notice.SoldOut).Key.ItemId);
    }

    [Fact]
    public void EmptyMachineFlag_CollapsesInsteadOfEnumerating()
    {
        var notice = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", true, [Sell(101, 0), Sell(102, 0)])));

        Assert.True(notice.MachineEmpty);
        Assert.Empty(notice.SoldOut);
        Assert.Equal("*", notice.Signature);
    }

    [Fact]
    public void UnknownFlag_FallsBackToPerOfferStock()
    {
        var notice = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", null, [Sell(101, 0)])));

        Assert.False(notice.MachineEmpty);
        Assert.Single(notice.SoldOut);
    }

    [Fact]
    public void FullyStockedMachine_RaisesNothing()
    {
        Assert.Empty(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", false, [Sell(101, 4)])));
    }

    [Fact]
    public void MachineOutsideMyGrids_IsIgnored()
    {
        Assert.Empty(Evaluate(
            new VendingMachineSnapshot(9UL, RivalX, RivalY, "Rival", true, [Sell(101, 0)])));
    }

    [Fact]
    public void Signature_IsOrderIndependent()
    {
        var a = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", false, [Sell(102, 0), Sell(101, 0)])));
        var b = Assert.Single(Evaluate(
            new VendingMachineSnapshot(1UL, MineX, MineY, "Shop", false, [Sell(101, 0), Sell(102, 0)])));

        Assert.Equal(a.Signature, b.Signature);
    }
}
```

- [ ] **Step 2: Run and watch them fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter StockEvaluatorTests`
Expected: FAIL — `StockEvaluator` does not exist.

- [ ] **Step 3: Write the notice record**

`src/RustPlusBot.Features.Vending/Evaluating/StockNotice.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Abstractions.Vending;

namespace RustPlusBot.Features.Vending.Evaluating;

/// <summary>One machine of ours with something sold out.</summary>
/// <param name="MachineId">The machine's marker id.</param>
/// <param name="ShopName">The shopfront name, if any.</param>
/// <param name="Grid">The grid reference the machine stands in.</param>
/// <param name="MachineEmpty">True when the whole machine reports empty; <see cref="SoldOut"/> is then empty.</param>
/// <param name="SoldOut">The individual sold-out offers, cheapest first.</param>
public sealed record StockNotice(
    ulong MachineId,
    string? ShopName,
    string Grid,
    bool MachineEmpty,
    IReadOnlyList<VendingOffer> SoldOut)
{
    /// <summary>
    /// The sold-out set as a comparable string: "*" for a wholly empty machine, otherwise the sold-out
    /// item ids sorted ascending and comma-joined. The relay compares this against the persisted value to
    /// tell an owner restock (delete the message) from an item selling out (edit it).
    /// </summary>
    public string Signature => MachineEmpty
        ? "*"
        : string.Join(',', SoldOut
            .Select(o => o.Key.ItemId)
            .Order()
            .Select(id => id.ToString(CultureInfo.InvariantCulture)));
}
```

- [ ] **Step 4: Implement the evaluator**

`src/RustPlusBot.Features.Vending/Evaluating/StockEvaluator.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Vending.Ownership;
using RustPlusBot.Features.Vending.Searching;

namespace RustPlusBot.Features.Vending.Evaluating;

/// <summary>
/// Works out which of our own machines have run dry. Pure, and deliberately separate from the undercut
/// evaluator: the two answer different questions about the same poll and are reconciled independently.
/// </summary>
internal static class StockEvaluator
{
    /// <summary>Evaluates sell-outs across our registered machines.</summary>
    /// <param name="machines">Every machine observed in the latest poll.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="style">Which grid convention to bin positions against.</param>
    /// <param name="grids">Our registered grid cells; use an ordinal-ignore-case set.</param>
    /// <returns>One notice per machine of ours with something sold out.</returns>
    public static IReadOnlyList<StockNotice> Evaluate(
        IReadOnlyList<VendingMachineSnapshot> machines,
        uint worldSize,
        MapGridStyle style,
        IReadOnlySet<string> grids)
    {
        ArgumentNullException.ThrowIfNull(machines);

        var notices = new List<StockNotice>();
        foreach (var machine in machines)
        {
            if (!GridOwnership.IsOwned(machine, worldSize, style, grids))
            {
                continue;
            }

            var grid = GridOwnership.GridOf(machine, worldSize, style);

            // A wholly empty machine collapses to one line: enumerating twenty dead listings tells the
            // owner nothing they do not already know from "the shop is empty".
            if (machine.IsOutOfStock == true)
            {
                notices.Add(new StockNotice(machine.Id, machine.Name, grid, MachineEmpty: true, []));
                continue;
            }

            // A null flag means the server did not report one; per-offer stock is still trustworthy.
            var soldOut = GridOwnership.ToOffers(machine, worldSize, style)
                .Where(o => !o.InStock)
                .ToList();
            if (soldOut.Count > 0)
            {
                notices.Add(new StockNotice(
                    machine.Id, machine.Name, grid, MachineEmpty: false, VendingSearch.Order(soldOut)));
            }
        }

        return notices;
    }
}
```

- [ ] **Step 5: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter StockEvaluatorTests`
Expected: PASS (6 tests)

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Vending/Evaluating tests/RustPlusBot.Features.Vending.Tests/StockEvaluatorTests.cs
git commit -m "feat: evaluate sell-outs for registered vending machines"
```

---

### Task 9: The in-memory index

**Files:**
- Create: `src/RustPlusBot.Features.Vending/Indexing/VendingIndex.cs`
- Create: `tests/RustPlusBot.Features.Vending.Tests/VendingIndexTests.cs`

**Interfaces:**
- Consumes: `IVendingReadModel` (Task 3), `GridOwnership`, `VendingSearch`.
- Produces: `VendingIndex : IVendingReadModel` with `Replace(guildId, serverId, worldSize, machines)`, `Clear(guildId, serverId)`, `TryGet(guildId, serverId, out ServerVendingState state)`, and `ServerVendingState(uint WorldSize, IReadOnlyList<VendingMachineSnapshot> Machines)`.

- [ ] **Step 1: Write the failing test**

`tests/RustPlusBot.Features.Vending.Tests/VendingIndexTests.cs`:

```csharp
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Features.Vending.Indexing;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingIndex"/>.</summary>
public sealed class VendingIndexTests
{
    private static readonly Guid ServerId = Guid.NewGuid();
    private const int Pipe = 69511070;
    private const int Scrap = -932201673;

    private static VendingMachineSnapshot Machine(ulong id, int cost, int stock) =>
        new(id, 500f, 3000f, "Shop", false, [new VendingOfferSnapshot(Pipe, false, 1, Scrap, false, cost, stock)]);

    [Fact]
    public void Replace_SupersedesThePreviousPoll()
    {
        var index = new VendingIndex();
        index.Replace(10UL, ServerId, 4000u, [Machine(1UL, 10, 5)]);
        index.Replace(10UL, ServerId, 4000u, [Machine(2UL, 7, 5)]);

        var offer = Assert.Single(index.Search(10UL, ServerId, Pipe, MapGridStyle.InGame));
        Assert.Equal(2UL, offer.MachineId);
    }

    [Fact]
    public void Search_UnknownServer_IsEmptyRatherThanThrowing()
    {
        var index = new VendingIndex();

        Assert.False(index.HasData(10UL, ServerId));
        Assert.Empty(index.Search(10UL, ServerId, Pipe, MapGridStyle.InGame));
    }

    [Fact]
    public void Clear_DropsTheServerState()
    {
        var index = new VendingIndex();
        index.Replace(10UL, ServerId, 4000u, [Machine(1UL, 10, 5)]);

        index.Clear(10UL, ServerId);

        Assert.False(index.HasData(10UL, ServerId));
    }

    [Fact]
    public void Search_ReturnsOnlyTheRequestedItem()
    {
        var index = new VendingIndex();
        index.Replace(10UL, ServerId, 4000u, [Machine(1UL, 10, 5)]);

        Assert.Empty(index.Search(10UL, ServerId, itemId: 999, MapGridStyle.InGame));
    }
}
```

- [ ] **Step 2: Run and watch it fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingIndexTests`
Expected: FAIL — `VendingIndex` does not exist.

- [ ] **Step 3: Implement**

`src/RustPlusBot.Features.Vending/Indexing/VendingIndex.cs`:

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Connections;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Vending.Ownership;
using RustPlusBot.Features.Vending.Searching;

namespace RustPlusBot.Features.Vending.Indexing;

/// <summary>One server's latest observed vending state.</summary>
/// <param name="WorldSize">The world size in game units, for grid maths.</param>
/// <param name="Machines">Every machine observed in the latest poll.</param>
internal sealed record ServerVendingState(uint WorldSize, IReadOnlyList<VendingMachineSnapshot> Machines);

/// <summary>
/// The live vending index, one entry per (guild, server). Rust re-sends the complete vending set on every
/// poll, so state is replaced wholesale — there is no merge, no eviction and no staleness bookkeeping.
/// Singleton, written by the hosted service and read by commands, hence the concurrent dictionary.
/// </summary>
internal sealed class VendingIndex : IVendingReadModel
{
    private readonly ConcurrentDictionary<(ulong Guild, Guid Server), ServerVendingState> _states = new();

    /// <summary>Replaces a server's state with the latest poll.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="worldSize">The world size in game units.</param>
    /// <param name="machines">Every machine observed in this poll.</param>
    public void Replace(
        ulong guildId, Guid serverId, uint worldSize, IReadOnlyList<VendingMachineSnapshot> machines) =>
        _states[(guildId, serverId)] = new ServerVendingState(worldSize, machines);

    /// <summary>Drops a server's state, e.g. on disconnect.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    public void Clear(ulong guildId, Guid serverId) => _states.TryRemove((guildId, serverId), out _);

    /// <summary>Gets a server's latest state.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="state">The state, when present.</param>
    /// <returns>True when the server has observed state.</returns>
    public bool TryGet(ulong guildId, Guid serverId, out ServerVendingState state) =>
        _states.TryGetValue((guildId, serverId), out state!);

    /// <inheritdoc />
    public bool HasData(ulong guildId, Guid serverId) => _states.ContainsKey((guildId, serverId));

    /// <inheritdoc />
    public IReadOnlyList<VendingOffer> Search(
        ulong guildId, Guid serverId, int itemId, MapGridStyle gridStyle)
    {
        if (!TryGet(guildId, serverId, out var state))
        {
            return [];
        }

        var matches = state.Machines
            .SelectMany(m => GridOwnership.ToOffers(m, state.WorldSize, gridStyle))
            .Where(o => o.Key.ItemId == itemId);
        return VendingSearch.Order(matches);
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingIndexTests`
Expected: PASS (4 tests)

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Vending/Indexing tests/RustPlusBot.Features.Vending.Tests/VendingIndexTests.cs
git commit -m "feat: add the in-memory vending index and search read model"
```

---

### Task 10: The `#vending` channel

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/IVendingChannelLocator.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/VendingChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs`

**Interfaces:**
- Produces: `WorkspaceChannelKeys.ServerVending`, `IVendingChannelLocator.GetChannelIdAsync(guildId, serverId, ct)`.

- [ ] **Step 1: Add the channel key**

In `WorkspaceKeys.cs`, after `ServerStorageMonitors`:

```csharp
    /// <summary>Key for the per-server #vending channel (undercut and sell-out notifications).</summary>
    public const string ServerVending = "vending";
```

- [ ] **Step 2: Add the channel spec**

In `ServerWorkspaceSpecProvider.GetChannelSpecs()`, after the storage-monitors entry:

```csharp
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerVending, "channel.vending.name",
            ChannelPermissionProfile.ReadOnly, 10),
```

- [ ] **Step 3: Add the locator**

`IVendingChannelLocator.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the per-server #vending channel (used to post undercut and sell-out notifications).</summary>
public interface IVendingChannelLocator
{
    /// <summary>Gets the Discord channel id of #vending for (<paramref name="guildId"/>, <paramref name="serverId"/>), or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

`VendingChannelLocator.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the #vending channel id for a (guild, server).</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class VendingChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : CachingChannelLocator(scopeFactory, clock, WorkspaceChannelKeys.ServerVending), IVendingChannelLocator;
```

- [ ] **Step 4: Register the locator**

In `WorkspaceServiceCollectionExtensions`, beside the other locator registrations:

```csharp
        services.AddSingleton<IVendingChannelLocator, VendingChannelLocator>();
```

- [ ] **Step 5: Add the channel-name strings**

`Strings.resx`:

```xml
  <data name="channel.vending.name" xml:space="preserve">
    <value>vending</value>
  </data>
```

`Strings.fr.resx`:

```xml
  <data name="channel.vending.name" xml:space="preserve">
    <value>distributeurs</value>
  </data>
```

- [ ] **Step 6: Run the workspace tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Workspace.Tests`
Expected: PASS. If a test asserts the exact per-server channel count or ordering, update it to include `#vending` at order 10.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Workspace src/RustPlusBot.Localization tests/RustPlusBot.Features.Workspace.Tests
git commit -m "feat: provision a per-server #vending channel"
```

---

### Task 11: Embed renderer and channel poster

**Files:**
- Create: `src/RustPlusBot.Features.Vending/Rendering/VendingEmbedRenderer.cs`
- Create: `src/RustPlusBot.Features.Vending/Posting/IVendingChannelPoster.cs`
- Create: `src/RustPlusBot.Features.Vending/Posting/DiscordVendingChannelPoster.cs`
- Create: `tests/RustPlusBot.Features.Vending.Tests/VendingEmbedRendererTests.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`

**Interfaces:**
- Consumes: `UndercutNotice` (Task 7), `StockNotice` (Task 8), `IItemDatabase`, `ILocalizer`.
- Produces: `VendingEmbedRenderer.RenderUndercut(notice, culture)`, `RenderStock(notice, culture)`, `RenderSearch(itemName, shown, more, culture)`, all returning `Discord.Embed`; `IVendingChannelPoster.EnsureAsync(channelId, messageId, embed, ct)` returning `Task<ulong?>` and `DeleteMessageAsync(channelId, messageId, ct)`.

- [ ] **Step 1: Add the strings**

Add to both resx files (French values in `Strings.fr.resx`):

| Key | English | French |
|---|---|---|
| `vending.undercut.title` | `Undercut: {0}` | `Prix cassé : {0}` |
| `vending.undercut.yours` | `Your price: {0} for {1}` | `Votre prix : {0} pour {1}` |
| `vending.undercut.rival` | `{0} — {1} for {2} ({3} left)` | `{0} — {1} pour {2} ({3} restants)` |
| `vending.stock.title` | `Sold out: {0}` | `Épuisé : {0}` |
| `vending.stock.empty` | `The whole shop is empty.` | `La boutique est entièrement vide.` |
| `vending.stock.item` | `{0} — sold out` | `{0} — épuisé` |
| `vending.search.title` | `Selling: {0}` | `En vente : {0}` |
| `vending.search.row` | `{0} — {1} for {2} ({3})` | `{0} — {1} pour {2} ({3})` |
| `vending.search.instock` | `{0} left` | `{0} restants` |
| `vending.search.soldout` | `out of stock` | `épuisé` |
| `vending.search.more` | `+{0} more` | `+{0} de plus` |
| `vending.search.none` | `No vending machine is selling {0}.` | `Aucun distributeur ne vend {0}.` |

- [ ] **Step 2: Write the failing renderer test**

`tests/RustPlusBot.Features.Vending.Tests/VendingEmbedRendererTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.Vending.Evaluating;
using RustPlusBot.Features.Vending.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Vending.Tests;

/// <summary>Unit tests for <see cref="VendingEmbedRenderer"/>.</summary>
public sealed class VendingEmbedRendererTests
{
    private static readonly ListingKey Pipe = new(69511070, false, -932201673, false);

    private static VendingEmbedRenderer Create()
    {
        var localizer = Substitute.For<ILocalizer>();
        localizer.Get(Arg.Any<string>(), Arg.Any<string>()).Returns(ci => ci.ArgAt<string>(0));
        localizer.Get(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<object[]>())
            .Returns(ci => $"{ci.ArgAt<string>(0)}|{string.Join('|', ci.ArgAt<object[]>(2))}");
        return new VendingEmbedRenderer(Substitute.For<IItemDatabase>(), localizer);
    }

    [Fact]
    public void RenderUndercut_IsStableForTheSameNotice()
    {
        var renderer = Create();
        var notice = new UndercutNotice(Pipe, 1, 10,
            [new VendingOffer(2UL, "Rival", "K12", Pipe, 1, 8, 4)]);

        // The relay only edits when the render changes, so an unstable render would burn Discord calls
        // every five seconds forever.
        Assert.Equal(
            renderer.RenderUndercut(notice, "en").Description,
            renderer.RenderUndercut(notice, "en").Description);
    }

    [Fact]
    public void RenderStock_EmptyMachine_DoesNotEnumerateItems()
    {
        var renderer = Create();
        var notice = new StockNotice(1UL, "Shop", "D7", MachineEmpty: true, []);

        Assert.Contains("vending.stock.empty", renderer.RenderStock(notice, "en").Description, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run and watch it fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingEmbedRendererTests`
Expected: FAIL — `VendingEmbedRenderer` does not exist.

- [ ] **Step 4: Implement the renderer**

`VendingEmbedRenderer` takes `(IItemDatabase items, ILocalizer localizer)`. Resolve display names with `items.GetById(id)?.Name ?? id.ToString(CultureInfo.InvariantCulture)` — this is how currency ids become "Scrap". Build each embed with `new EmbedBuilder().WithTitle(...).WithDescription(string.Join('\n', lines)).Build()`, following `AlarmEmbedRenderer` for colour and footer conventions. Render undercut rows with `vending.undercut.rival` (grid, `qty x item`, `cost currency`, stock), stock rows with `vending.stock.item`, and search rows with `vending.search.row` plus `vending.search.more` in the footer when `more > 0`.

- [ ] **Step 5: Implement the poster**

`IVendingChannelPoster` declares:

```csharp
    /// <summary>Edits the message at <paramref name="messageId"/> if present and found; otherwise posts a new one.</summary>
    /// <param name="channelId">The #vending channel id.</param>
    /// <param name="messageId">The known message id, or null to post fresh.</param>
    /// <param name="embed">The embed to show.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The (possibly new) message id, or null on failure.</returns>
    Task<ulong?> EnsureAsync(
        ulong channelId, ulong? messageId, global::Discord.Embed embed, CancellationToken cancellationToken);

    /// <summary>Deletes a message in the given channel (missing message/channel tolerated).</summary>
    /// <param name="channelId">The #vending channel id.</param>
    /// <param name="messageId">The message id to delete.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the message has been deleted or the failure swallowed.</returns>
    Task DeleteMessageAsync(ulong channelId, ulong messageId, CancellationToken cancellationToken);
```

`DiscordVendingChannelPoster` is `DiscordAlarmChannelPoster` with the components argument dropped — copy it, including its swallow-and-log error handling and its repost-on-404 behaviour.

- [ ] **Step 6: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingEmbedRendererTests`
Expected: PASS (2 tests)

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Vending/Rendering src/RustPlusBot.Features.Vending/Posting src/RustPlusBot.Localization tests/RustPlusBot.Features.Vending.Tests/VendingEmbedRendererTests.cs
git commit -m "feat: render vending embeds and post them to #vending"
```

---

### Task 12: The notification relay

**Files:**
- Create: `src/RustPlusBot.Features.Vending/VendingOptions.cs`
- Create: `src/RustPlusBot.Features.Vending/Relaying/VendingNotificationRelay.cs`
- Create: `tests/RustPlusBot.Features.Vending.Tests/VendingNotificationRelayTests.cs`

**Interfaces:**
- Consumes: `VendingIndex` (9), `UndercutEvaluator` (7), `StockEvaluator` (8), `IVendingStore` (4), `IVendingChannelLocator` (10), `IVendingChannelPoster` + `VendingEmbedRenderer` (11), `IMapSettingsStore`.
- Produces: `VendingNotificationRelay.HandleObservedAsync(VendingMachinesObservedEvent, CancellationToken)` and `HandleConnectionStatusAsync(ConnectionStatusChangedEvent, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Vending.Tests/VendingNotificationRelayTests.cs`. Build a harness with NSubstitute for `IVendingStore`, `IVendingChannelLocator`, `IVendingChannelPoster` and `IMapSettingsStore` (registered scoped in a `ServiceCollection`, exactly as `AlarmWipePurgerTests` does), a real `VendingIndex`, and a real renderer over a stub localizer. Then:

```csharp
    [Fact]
    public async Task FirstUndercut_PostsAndStoresTheMessageId()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, default, default).ReturnsForAnyArgs([MyGrid]);
        h.Poster.EnsureAsync(default, default, default!, default).ReturnsForAnyArgs(555UL);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10), RivalMachine(cost: 8)), h.Ct);

        await h.Store.Received(1).UpsertNotificationAsync(
            10UL, h.ServerId, Pipe, 555UL, 1, 10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnchangedUndercut_EditsRatherThanReposts()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, default, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListNotificationsAsync(default, default, default)
            .ReturnsForAnyArgs([Notification(messageId: 555UL, refQty: 1, refCost: 10)]);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10), RivalMachine(cost: 8)), h.Ct);

        await h.Poster.Received(1).EnsureAsync(
            Arg.Any<ulong>(), 555UL, Arg.Any<Embed>(), Arg.Any<CancellationToken>());
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task OwnerReprices_DeletesTheMessageRatherThanEditingIt()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, default, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListNotificationsAsync(default, default, default)
            .ReturnsForAnyArgs([Notification(messageId: 555UL, refQty: 1, refCost: 10)]);

        // We dropped to 9; the rival at 8 still beats us, but the stale message must go first.
        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 9), RivalMachine(cost: 8)), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 555UL, Arg.Any<CancellationToken>());
        await h.Store.Received(1).RemoveNotificationAsync(10UL, h.ServerId, Pipe, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RivalGone_DeletesTheMessage()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, default, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListNotificationsAsync(default, default, default)
            .ReturnsForAnyArgs([Notification(messageId: 555UL, refQty: 1, refCost: 10)]);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10)), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 555UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OwnerRestocks_DeletesTheStockMessage()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, default, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListStockNotificationsAsync(default, default, default)
            .ReturnsForAnyArgs([StockNotification(machineId: 1UL, messageId: 777UL, signature: "69511070")]);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10, stock: 5)), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 777UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disconnect_ClearsTheIndexButDeletesNothing()
    {
        var h = Harness.Create();
        h.Store.ListGridsAsync(default, default, default).ReturnsForAnyArgs([MyGrid]);
        h.Store.ListNotificationsAsync(default, default, default)
            .ReturnsForAnyArgs([Notification(messageId: 555UL, refQty: 1, refCost: 10)]);

        await h.Relay.HandleConnectionStatusAsync(h.Disconnected(), h.Ct);

        Assert.False(h.Index.HasData(10UL, h.ServerId));
        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }

    [Fact]
    public async Task NoChannelProvisioned_SkipsWithoutTouchingTheStore()
    {
        var h = Harness.Create(channelId: null);
        h.Store.ListGridsAsync(default, default, default).ReturnsForAnyArgs([MyGrid]);

        await h.Relay.HandleObservedAsync(h.Observed(MyMachine(cost: 10), RivalMachine(cost: 8)), h.Ct);

        await h.Poster.DidNotReceiveWithAnyArgs().EnsureAsync(default, default, default!, default);
    }
```

- [ ] **Step 2: Run and watch them fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingNotificationRelayTests`
Expected: FAIL — `VendingNotificationRelay` does not exist.

- [ ] **Step 3: Write the options**

`src/RustPlusBot.Features.Vending/VendingOptions.cs`:

```csharp
namespace RustPlusBot.Features.Vending;

/// <summary>Tuning for the vending feature.</summary>
public sealed class VendingOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Vending";

    /// <summary>
    /// Maximum live notifications per server, applied separately to undercut and sell-out messages so a
    /// busy undercut situation cannot starve alerts about the team's own shop. Excess is logged, never
    /// silently dropped.
    /// </summary>
    public int MaxNotificationsPerServer { get; set; } = 50;
}
```

- [ ] **Step 4: Implement the relay**

`VendingNotificationRelay` is a singleton taking `(VendingIndex index, IServiceScopeFactory scopeFactory, IVendingChannelLocator locator, IVendingChannelPoster poster, VendingEmbedRenderer renderer, IOptions<VendingOptions> options, ILogger<VendingNotificationRelay> logger)`.

`HandleObservedAsync`:

1. `index.Replace(evt.GuildId, evt.ServerId, evt.WorldSize, evt.Machines)`.
2. Return early when `evt.WorldSize == 0` — grid maths is meaningless without it, and acting would misfile every machine.
3. Resolve the channel id; return when null.
4. Open a scope, read `IVendingStore.ListGridsAsync` / `ListListingsAsync` / `ListNotificationsAsync` / `ListStockNotificationsAsync` and the grid style from `IMapSettingsStore`.
5. Return when there are no grids and no manual listings — nothing is tracked, so nothing to reconcile.
6. `var desired = UndercutEvaluator.Evaluate(...)` and `var desiredStock = StockEvaluator.Evaluate(...)`.
7. Reconcile undercuts: for each desired notice, cap-check, then

```csharp
            var existing = notifications.FirstOrDefault(n => KeyOf(n) == notice.Key);
            if (existing is not null
                && (existing.ReferenceCostPerOrder != notice.ReferenceCostPerOrder
                    || existing.ReferenceQuantity != notice.ReferenceQuantity))
            {
                // The owner repriced. Delete rather than edit so reacting to an alert produces a new
                // unread; the repost below re-raises it if rivals still beat the new price.
                await poster.DeleteMessageAsync(channelId, existing.MessageId, ct).ConfigureAwait(false);
                await store.RemoveNotificationAsync(guildId, serverId, notice.Key, ct).ConfigureAwait(false);
                existing = null;
            }

            var embed = renderer.RenderUndercut(notice, culture);
            var messageId = await poster.EnsureAsync(channelId, existing?.MessageId, embed, ct)
                .ConfigureAwait(false);
            if (messageId is { } id)
            {
                await store.UpsertNotificationAsync(
                        guildId, serverId, notice.Key, id,
                        notice.ReferenceQuantity, notice.ReferenceCostPerOrder, ct)
                    .ConfigureAwait(false);
            }
```

8. Delete every persisted undercut notification whose key is not in `desired`, removing the row.
9. Reconcile sell-outs identically, comparing `existing.SoldOutSignature != notice.Signature` instead of the reference price, keyed on `MachineId`. Persisted stock rows for machines not in `desiredStock` — including machines that vanished from the poll — are deleted.
10. When either desired set exceeds `MaxNotificationsPerServer`, take the first N and `LogCapExceeded(logger, dropped, serverId)`.

`HandleConnectionStatusAsync` calls `index.Clear(evt.GuildId, evt.ServerId)` when `!evt.IsConnected` and does nothing else — a dropped socket must never be read as "every rival vanished".

- [ ] **Step 5: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingNotificationRelayTests`
Expected: PASS (7 tests)

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Vending tests/RustPlusBot.Features.Vending.Tests/VendingNotificationRelayTests.cs
git commit -m "feat: reconcile vending notifications against posted Discord messages"
```

---

### Task 13: Wipe purger

**Files:**
- Create: `src/RustPlusBot.Features.Vending/Relaying/VendingWipePurger.cs`
- Create: `tests/RustPlusBot.Features.Vending.Tests/VendingWipePurgerTests.cs`

**Interfaces:**
- Consumes: `IVendingStore` (4), `IVendingChannelLocator` (10), `IVendingChannelPoster` (11), `VendingIndex` (9).
- Produces: `VendingWipePurger.HandleServerWipedAsync(ServerWipedEvent, CancellationToken)`.

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task Wipe_DeletesEveryNotificationMessage()
    {
        var h = Harness.Create(
            notifications: [Notification(555UL)],
            stockNotifications: [StockNotification(777UL)]);

        await h.Purger.HandleServerWipedAsync(h.Wiped(), h.Ct);

        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 555UL, Arg.Any<CancellationToken>());
        await h.Poster.Received(1).DeleteMessageAsync(Arg.Any<ulong>(), 777UL, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Wipe_ClearsGridsButKeepsManualListings()
    {
        var h = Harness.Create();

        await h.Purger.HandleServerWipedAsync(h.Wiped(), h.Ct);

        // The base is gone, so the cell registration is meaningless — but an item+price pair a player
        // typed in is still exactly as valid next wipe.
        await h.Store.Received(1).PurgeGridsAsync(10UL, h.ServerId, Arg.Any<CancellationToken>());
        await h.Store.DidNotReceiveWithAnyArgs().RemoveListingAsync(default, default, default, default);
    }

    [Fact]
    public async Task Wipe_WithNothingTracked_IsANoOp()
    {
        var h = Harness.Create(notifications: [], stockNotifications: []);

        await h.Purger.HandleServerWipedAsync(h.Wiped(), h.Ct);

        await h.Poster.DidNotReceiveWithAnyArgs().DeleteMessageAsync(default, default, default);
    }
```

- [ ] **Step 2: Run and watch them fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingWipePurgerTests`
Expected: FAIL — `VendingWipePurger` does not exist.

- [ ] **Step 3: Implement**

`src/RustPlusBot.Features.Vending/Relaying/VendingWipePurger.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Vending.Indexing;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Vending;

namespace RustPlusBot.Features.Vending.Relaying;

/// <summary>
/// Clears a wiped server's vending state: the rows go first (so no stale message can be re-edited),
/// then the Discord messages best-effort. Idempotent — a duplicate wipe event finds nothing to do.
/// </summary>
/// <param name="scopeFactory">Opens scopes for the scoped vending store.</param>
/// <param name="index">The live index, cleared so the first post-wipe poll starts clean.</param>
/// <param name="locator">Resolves the #vending channel id.</param>
/// <param name="poster">Deletes the notification messages.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class VendingWipePurger(
    IServiceScopeFactory scopeFactory,
    VendingIndex index,
    IVendingChannelLocator locator,
    IVendingChannelPoster poster,
    ILogger<VendingWipePurger> logger)
{
    /// <summary>Handles one <see cref="ServerWipedEvent"/> by clearing the server's vending state.</summary>
    /// <param name="evt">The wipe event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>A task that completes when rows and messages have been removed.</returns>
    public async Task HandleServerWipedAsync(ServerWipedEvent evt, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evt);

        List<ulong> messageIds;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IVendingStore>();
            var undercuts = await store.ListNotificationsAsync(evt.GuildId, evt.ServerId, ct)
                .ConfigureAwait(false);
            var stock = await store.ListStockNotificationsAsync(evt.GuildId, evt.ServerId, ct)
                .ConfigureAwait(false);
            messageIds = [.. undercuts.Select(n => n.MessageId), .. stock.Select(n => n.MessageId)];

            foreach (var notification in undercuts)
            {
                await store.RemoveNotificationAsync(
                        evt.GuildId,
                        evt.ServerId,
                        new ListingKey(
                            notification.ItemId,
                            notification.ItemIsBlueprint,
                            notification.CurrencyId,
                            notification.CurrencyIsBlueprint),
                        ct)
                    .ConfigureAwait(false);
            }

            foreach (var notification in stock)
            {
                await store.RemoveStockNotificationAsync(
                        evt.GuildId, evt.ServerId, notification.MachineId, ct)
                    .ConfigureAwait(false);
            }

            // Grid registrations point at a base that no longer exists. Manual listings are just an
            // item and a price the player typed in, and stay exactly as valid next wipe — so they stay.
            await store.PurgeGridsAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        }

        index.Clear(evt.GuildId, evt.ServerId);
        if (messageIds.Count == 0)
        {
            return;
        }

        var channelId = await locator.GetChannelIdAsync(evt.GuildId, evt.ServerId, ct).ConfigureAwait(false);
        if (channelId is { } cid)
        {
            foreach (var messageId in messageIds)
            {
                await poster.DeleteMessageAsync(cid, messageId, ct).ConfigureAwait(false);
            }
        }

        LogPurged(logger, messageIds.Count, evt.GuildId, evt.ServerId);
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Purged {Count} vending notifications after wipe for guild {GuildId} server {ServerId}.")]
    private static partial void LogPurged(ILogger logger, int count, ulong guildId, Guid serverId);
}
```

Add `using RustPlusBot.Abstractions.Vending;` and `using RustPlusBot.Features.Vending.Posting;` alongside the usings shown.

- [ ] **Step 4: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Vending.Tests --filter VendingWipePurgerTests`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Vending/Relaying tests/RustPlusBot.Features.Vending.Tests/VendingWipePurgerTests.cs
git commit -m "feat: purge vending grids and notifications on wipe"
```

---

### Task 14: Track service, hosted service, and DI wiring

**Files:**
- Create: `src/RustPlusBot.Features.Vending/Tracking/VendingTrackService.cs`
- Create: `src/RustPlusBot.Features.Vending/Hosting/VendingHostedService.cs`
- Create: `src/RustPlusBot.Features.Vending/VendingServiceCollectionExtensions.cs`
- Create: `tests/RustPlusBot.Features.Vending.Tests/VendingRegistrationTests.cs`
- Modify: `src/RustPlusBot.Host/Program.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `IServiceCollection.AddVending()`.

- [ ] **Step 1: Implement the track service**

`VendingTrackService : IVendingTrackService`, scoped, taking `(IVendingStore store, VendingIndex index, IMapSettingsStore mapSettings)`. `TrackGridAsync` normalises the grid to upper case, validates it against the indexed world size, and counts what is currently in the cell:

```csharp
    /// <inheritdoc />
    public async Task<GridTrackResult> TrackGridAsync(
        ulong guildId, Guid serverId, string grid, ulong steamId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grid);
        var normalized = grid.Trim().ToUpperInvariant();

        // Validate against the live map rather than a regex: "Z99" is well-formed but does not exist on a
        // 4000 world, and silently registering a cell nobody can reach is worse than an error.
        if (!index.TryGet(guildId, serverId, out var state) || state.WorldSize == 0)
        {
            return new GridTrackResult(GridValid: false, 0, 0);
        }

        var settings = await mapSettings.GetAsync(guildId, serverId, ct).ConfigureAwait(false);
        var cells = MapGrid.CellCount(state.WorldSize);
        var valid = Enumerable.Range(0, cells)
            .SelectMany(c => Enumerable.Range(0, cells)
                .Select(r => MapGrid.ColumnLetters(c) + r.ToString(CultureInfo.InvariantCulture)))
            .Contains(normalized, StringComparer.OrdinalIgnoreCase);
        if (!valid)
        {
            return new GridTrackResult(GridValid: false, 0, 0);
        }

        await store.AddGridAsync(guildId, serverId, normalized, steamId, ct).ConfigureAwait(false);

        var inCell = state.Machines
            .Where(m => string.Equals(
                GridOwnership.GridOf(m, state.WorldSize, settings.GridStyle),
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        var listings = inCell
            .SelectMany(m => GridOwnership.ToOffers(m, state.WorldSize, settings.GridStyle))
            .Select(o => o.Key)
            .Distinct()
            .Count();

        return new GridTrackResult(GridValid: true, inCell.Count, listings);
    }
```

The remaining four methods delegate straight to `IVendingStore`, mapping `VendingListingTrack` rows to `TrackedListing`.

- [ ] **Step 2: Implement the hosted service**

`VendingHostedService` mirrors `AlarmsHostedService` exactly, with three loops. Subscribe eagerly in `StartAsync` using the stream overload of `ConsumeAsync`, so events published between start-up and the background task being scheduled are not dropped:

```csharp
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var observed = eventBus.SubscribeAsync<VendingMachinesObservedEvent>(_cts.Token);
        var status = eventBus.SubscribeAsync<ConnectionStatusChangedEvent>(_cts.Token);
        var wiped = eventBus.SubscribeAsync<ServerWipedEvent>(_cts.Token);

        _observedLoop = Task.Run(
            () => observed.ConsumeAsync(
                relay.HandleObservedAsync,
                ex => LogHandlerFailed(logger, ex, nameof(VendingMachinesObservedEvent)),
                _cts.Token),
            CancellationToken.None);
        _statusLoop = Task.Run(
            () => status.ConsumeAsync(
                relay.HandleConnectionStatusAsync,
                ex => LogHandlerFailed(logger, ex, nameof(ConnectionStatusChangedEvent)),
                _cts.Token),
            CancellationToken.None);
        _wipedLoop = Task.Run(
            () => wiped.ConsumeAsync(
                purger.HandleServerWipedAsync,
                ex => LogHandlerFailed(logger, ex, nameof(ServerWipedEvent)),
                _cts.Token),
            CancellationToken.None);
        return Task.CompletedTask;
    }
```

`StopAsync` joins the three loops with the same `OperationCanceledException` swallow as `AlarmsHostedService`.

- [ ] **Step 3: Write the DI extension**

```csharp
namespace RustPlusBot.Features.Vending;

/// <summary>DI registration for the vending feature.</summary>
public static class VendingServiceCollectionExtensions
{
    /// <summary>Registers the index, read model, track service, renderer, poster, relay, purger, modules, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddVending(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRustPlusBotLocalization();
        services.AddSingleton<VendingIndex>();
        services.AddSingleton<IVendingReadModel>(sp => sp.GetRequiredService<VendingIndex>());
        services.AddScoped<IVendingTrackService, VendingTrackService>();
        services.AddSingleton<VendingEmbedRenderer>();
        services.AddSingleton<IVendingChannelPoster, DiscordVendingChannelPoster>();
        services.AddSingleton<VendingNotificationRelay>();
        services.AddSingleton<VendingWipePurger>();
        services.AddHostedService<VendingHostedService>();

        services.AddSingleton(new InteractionModuleAssembly(typeof(VendingServiceCollectionExtensions).Assembly));

        return services;
    }
}
```

`VendingTrackService` is scoped but depends on the singleton `VendingIndex` — that direction is fine.

- [ ] **Step 4: Wire it into the host**

In `src/RustPlusBot.Host/Program.cs`, after `builder.Services.AddStorageMonitors();`:

```csharp
builder.Services.AddOptions<VendingOptions>()
    .Bind(builder.Configuration.GetSection(VendingOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddVending();
```

Match the exact `AddOptions` style the neighbouring registrations use.

- [ ] **Step 5: Write the registration test**

`VendingRegistrationTests` builds a service provider via `AddVending()` (plus the fakes the other `*RegistrationTests` use) and asserts `IVendingReadModel`, `IVendingTrackService`, `VendingNotificationRelay` and `VendingWipePurger` all resolve, and that `IVendingReadModel` and `VendingIndex` resolve to the *same* instance. Copy the harness from `tests/RustPlusBot.Features.Alarms.Tests/AlarmRegistrationTests.cs`.

- [ ] **Step 6: Run the full suite**

Run: `dtk dotnet test RustPlusBot.slnx`
Expected: PASS

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Vending src/RustPlusBot.Host tests/RustPlusBot.Features.Vending.Tests/VendingRegistrationTests.cs
git commit -m "feat: wire up the vending feature host"
```

---

### Task 15: In-game commands

**Files:**
- Create: `src/RustPlusBot.Features.Commands/Formatting/VendingLine.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/VendingCommandHandler.cs`, `VTrackCommandHandler.cs`, `VUntrackCommandHandler.cs`, `VTrackedCommandHandler.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`
- Create: `tests/RustPlusBot.Features.Commands.Tests/VendingCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IVendingReadModel`, `IVendingTrackService` (Task 3), `IItemDatabase`, `IMapSettingsStore`, `ILocalizer`.
- Produces: four `ICommandHandler` implementations named `vending`, `vtrack`, `vuntrack`, `vtracked`.

- [ ] **Step 1: Add the strings**

Both resx files:

| Key | English |
|---|---|
| `command.vending.usage` | `Usage: !vending <item>` |
| `command.vending.ok` | `{0}: {1}` |
| `command.vending.offer` | `{0} {1} for {2} ({3})` |
| `command.vending.none` | `No vending machine is selling {0}.` |
| `command.vtrack.usage` | `Usage: !vtrack <grid>` |
| `command.vtrack.badgrid` | `{0} is not a grid on this map.` |
| `command.vtrack.ok` | `Tracking {0}: {1} machines, {2} listings.` |
| `command.vuntrack.ok` | `No longer tracking {0}.` |
| `command.vuntrack.notracked` | `{0} was not tracked.` |
| `command.vtracked.ok` | `Tracking: {0}` |
| `command.vtracked.none` | `Nothing is tracked. Use !vtrack <grid>.` |

Add French values for each.

- [ ] **Step 2: Write the failing test**

```csharp
    [Fact]
    public async Task Vending_NoArgs_ReturnsUsage()
    {
        var handler = Create();
        var reply = await handler.ExecuteAsync(Context([]), TestContext.Current.CancellationToken);

        Assert.Equal("command.vending.usage", reply);
    }

    [Fact]
    public async Task Vending_ShowsAtMostThreeOffers()
    {
        var handler = Create(offers: [Offer("A1", 5), Offer("B2", 6), Offer("C3", 7), Offer("D4", 8)]);
        var reply = await handler.ExecuteAsync(Context(["pipe"]), TestContext.Current.CancellationToken);

        Assert.DoesNotContain("D4", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Vending_NotConnected_SaysSo()
    {
        var handler = Create(hasData: false);
        var reply = await handler.ExecuteAsync(Context(["pipe"]), TestContext.Current.CancellationToken);

        Assert.Equal("command.notconnected", reply);
    }
```

Follow `AfkCommandHandlerTests` for the context builder and stub localizer.

- [ ] **Step 3: Run and watch it fail**

Run: `dtk dotnet test tests/RustPlusBot.Features.Commands.Tests --filter VendingCommandHandlerTests`
Expected: FAIL

- [ ] **Step 4: Write `VendingLine`**

`Formatting/VendingLine.cs` exposes one static method, matching the shape of the sibling `ItemLine` / `RecycleLine` formatters:

```csharp
    /// <summary>Formats one offer as a single in-game chat fragment.</summary>
    /// <param name="offer">The offer to format.</param>
    /// <param name="names">Resolves the currency id to a display name.</param>
    /// <param name="localizer">The reply localizer.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>e.g. <c>D7 1 for 12 scrap (18 left)</c>.</returns>
    public static string Format(
        VendingOffer offer, IItemNameResolver names, ILocalizer localizer, string culture)
```

It fills `command.vending.offer` with the grid, quantity, `"{cost} {currencyName}"`, and either `command.vending.instock` (with `AmountInStock`) or `command.vending.soldout`. The caller joins fragments with `" · "`.

Add `command.vending.instock` (`{0} left` / `{0} restants`) and `command.vending.soldout` (`out` / `épuisé`) to both resx files alongside the Step 1 keys.

- [ ] **Step 5: Write the four handlers**

Each is `internal sealed class XCommandHandler(...) : ICommandHandler` with a lowercase `Name`.

```csharp
using RustPlusBot.Abstractions.Vending;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Map;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!vending &lt;item&gt; — the three best offers for an item, in stock first.</summary>
/// <param name="readModel">The live vending index.</param>
/// <param name="items">The bundled item database, for name resolution.</param>
/// <param name="names">Resolves currency ids to display names.</param>
/// <param name="mapSettings">Supplies the server's grid convention.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class VendingCommandHandler(
    IVendingReadModel readModel,
    IItemDatabase items,
    IItemNameResolver names,
    IMapSettingsStore mapSettings,
    ILocalizer localizer) : ICommandHandler
{
    private const int MaxOffers = 3;

    /// <inheritdoc />
    public string Name => "vending";

    /// <inheritdoc />
    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Args.Count == 0)
        {
            return localizer.Get("command.vending.usage", context.Culture);
        }

        var match = items.Resolve(string.Join(' ', context.Args));
        if (match is ItemMatch.NotFound)
        {
            return localizer.Get("command.item.notfound", context.Culture, string.Join(' ', context.Args));
        }

        if (match is ItemMatch.Ambiguous ambiguous)
        {
            return localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", ambiguous.Candidates.Select(c => c.Name)));
        }

        var item = ((ItemMatch.Found)match).Item;

        // No index entry means no poll has landed for this server since the socket last came up.
        if (!readModel.HasData(context.GuildId, context.ServerId))
        {
            return localizer.Get("command.notconnected", context.Culture);
        }

        var settings = await mapSettings.GetAsync(context.GuildId, context.ServerId, cancellationToken)
            .ConfigureAwait(false);
        var offers = readModel.Search(context.GuildId, context.ServerId, item.Id, settings.GridStyle);
        if (offers.Count == 0)
        {
            return localizer.Get("command.vending.none", context.Culture, item.Name);
        }

        var lines = offers
            .Take(MaxOffers)
            .Select(o => VendingLine.Format(o, names, localizer, context.Culture));
        return localizer.Get("command.vending.ok", context.Culture, item.Name, string.Join(" · ", lines));
    }
}
```

`VTrackCommandHandler` (`Name => "vtrack"`) requires one arg, calls `TrackGridAsync(guildId, serverId, args[0], context.SenderSteamId, ct)`, and maps `GridValid: false` to `command.vtrack.badgrid` and success to `command.vtrack.ok` with `MachinesFound` and `ListingsTracked`. `VUntrackCommandHandler` (`"vuntrack"`) maps `UntrackGridAsync`'s boolean to `command.vuntrack.ok` / `command.vuntrack.notracked`. `VTrackedCommandHandler` (`"vtracked"`) renders `GetTrackedAsync` — grids comma-joined, then one entry per manual listing — falling back to `command.vtracked.none` when both lists are empty. All three take `IVendingTrackService` and `ILocalizer`, and all three return `command.vending.usage`-style usage strings when their required argument is missing.

- [ ] **Step 6: Register the handlers**

In `CommandServiceCollectionExtensions`, beside the others:

```csharp
        services.AddScoped<ICommandHandler, VendingCommandHandler>();
        services.AddScoped<ICommandHandler, VTrackCommandHandler>();
        services.AddScoped<ICommandHandler, VUntrackCommandHandler>();
        services.AddScoped<ICommandHandler, VTrackedCommandHandler>();
```

Add a `ProjectReference` to `RustPlusBot.Abstractions` if `RustPlusBot.Features.Commands.csproj` does not already have one (it does — verify only).

- [ ] **Step 7: Run the tests**

Run: `dtk dotnet test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS. `CommandRegistrationTests` may assert a handler count — update it.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Commands src/RustPlusBot.Localization tests/RustPlusBot.Features.Commands.Tests
git commit -m "feat: add in-game vending search and tracking commands"
```

---

### Task 16: Slash commands

**Files:**
- Create: `src/RustPlusBot.Features.Vending/Modules/VendingModule.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`

**Interfaces:**
- Consumes: `IVendingReadModel`, `IVendingTrackService`, `IItemDatabase`, `VendingEmbedRenderer`, `ServerResolver`.
- Produces: `/vending`, `/vending-track`, `/vending-untrack`, `/vending-tracked`.

- [ ] **Step 1: Write the module**

`VendingModule(IServiceScopeFactory scopeFactory) : InteractionModuleBase<SocketInteractionContext>`, following `ItemCommandModule` for the scope-per-interaction pattern and the `MustBeUsedInServer` guard.

```csharp
    /// <summary>Finds every vending machine selling an item.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("vending", "Find vending machines selling an item")]
    public Task VendingAsync([Summary("item", "Item name or id")] string item) => SearchAsync(item);

    /// <summary>Registers a listing you sell, to be alerted when someone matches or beats your price.</summary>
    /// <param name="item">The item name or id you sell.</param>
    /// <param name="price">The currency charged for one order.</param>
    /// <param name="currency">The currency item name or id; defaults to Scrap.</param>
    /// <param name="quantity">Items yielded by one order; defaults to 1.</param>
    [SlashCommand("vending-track", "Track a listing you sell and get alerted when undercut")]
    public Task TrackAsync(
        [Summary("item", "Item name or id")] string item,
        [Summary("price", "Cost of one order")] int price,
        [Summary("currency", "Currency item name or id")] string currency = "scrap",
        [Summary("quantity", "Items per order")] int quantity = 1) => ...
```

`/vending-untrack` takes a single `string target` with an autocomplete handler listing the team's registered grids and manual listings; `/vending-tracked` renders `GetTrackedAsync` into an embed. Resolve `item` and `currency` through `IItemDatabase.Resolve`, replying ephemerally with `command.item.notfound` / `command.item.ambiguous` on a bad match, and reject `price < 1` or `quantity < 1` with a new `vending.track.badprice` string.

- [ ] **Step 2: Add the module strings**

Both resx files: `vending.track.ok`, `vending.track.badprice`, `vending.untrack.ok`, `vending.untrack.notfound`, `vending.tracked.title`, `vending.tracked.grids`, `vending.tracked.listings`, `vending.tracked.none`.

- [ ] **Step 3: Build and run everything**

Run: `dtk dotnet build RustPlusBot.slnx && dtk dotnet test RustPlusBot.slnx`
Expected: PASS

- [ ] **Step 4: Commit**

```bash
git add src/RustPlusBot.Features.Vending/Modules src/RustPlusBot.Localization
git commit -m "feat: add vending slash commands"
```

---

### Task 17: Help catalog and docs

**Files:**
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandGroup.cs` (add a `Vending` group)
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`
- Modify: `README.md`

- [ ] **Step 1: Add the catalog entries**

In `CommandHelpCatalog.InGame`:

```csharp
        new("vending", CommandGroup.Vending, "help.vending"),
        new("vtrack", CommandGroup.Vending, "help.vtrack"),
        new("vuntrack", CommandGroup.Vending, "help.vuntrack"),
        new("vtracked", CommandGroup.Vending, "help.vtracked"),
```

In `CommandHelpCatalog.Slash`:

```csharp
        new("vending", CommandGroup.Vending, "help.slash.vending"),
        new("vending-track", CommandGroup.Vending, "help.slash.vending.track"),
        new("vending-untrack", CommandGroup.Vending, "help.slash.vending.untrack"),
        new("vending-tracked", CommandGroup.Vending, "help.slash.vending.tracked"),
```

Add `Vending` to the `CommandGroup` enum and its display-name string wherever the other groups declare theirs.

- [ ] **Step 2: Add the help strings**

Eight `help.*` keys in both resx files, one line each, e.g. `help.vending` → `Find vending machines selling an item`.

- [ ] **Step 3: Run the drift test**

Run: `dtk dotnet test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS — the catalog test compares the manifest against the live handler registry, so a typo in a command name fails here.

- [ ] **Step 4: Update the README**

Add the four in-game and four slash commands to the command tables, and `#vending` to the per-server channel list. Mention that undercut comparison is same-currency only and that `!vtrack` binds a grid cell.

- [ ] **Step 5: Full verification**

Run: `dtk dotnet build RustPlusBot.slnx && dtk dotnet test RustPlusBot.slnx`
Expected: PASS, zero warnings introduced.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Help src/RustPlusBot.Localization README.md
git commit -m "docs: document the vending commands and channel"
```

---

## Verification Checklist

Before calling this done, confirm each behaviour from the spec by running the suite and reading the assertions:

- [ ] Search orders in-stock before sold-out, then by unit price, not order cost.
- [ ] A rival matching your price exactly raises a notification ("less **or the same**").
- [ ] A rival selling in a different currency never raises one.
- [ ] A sold-out rival never raises one, but still appears in search.
- [ ] Your reference price is your cheapest listing, not your dearest.
- [ ] Repricing deletes the undercut message; a rival's move only edits it.
- [ ] Restocking deletes the sell-out message; an item selling out only edits it.
- [ ] A wholly empty machine renders one line, not one per item.
- [ ] A disconnect clears the index and deletes nothing.
- [ ] A wipe clears grids and all messages, and keeps manual listings.
- [ ] Both `Strings.resx` and `Strings.fr.resx` contain every new key.
