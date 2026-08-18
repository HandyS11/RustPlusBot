# Vending Machine Search and Tracking — Design

**Date:** 2026-08-17
**Status:** Approved

## Problem

Rust servers carry hundreds of player vending machines, and the game gives players no way to
answer the two questions that matter to a shop owner: "who is selling the item I want, and for
how much?" and "is anyone undercutting me right now?". Players currently fly the map reading
shopfronts one by one. Shop owners also have no way to learn that a listing has sold out
without physically visiting the machine.

The bot already receives every vending machine on the server, with its full offer list, on
every map-marker poll — and throws that data away.

## Goal

Three capabilities, on both the Discord and in-game surfaces:

1. **Search** — find every machine selling an item, ordered by availability then price.
2. **Undercut tracking** — register the listings you sell and get notified in a dedicated
   Discord channel when another machine sells the same item for less or the same.
3. **Sell-out tracking** — for machines you have registered, get notified when an offer runs
   out of stock or the machine empties entirely.

## Scope

**In scope:** the `#vending` per-server channel; the `!vending`, `!vtrack`, `!vuntrack`,
`!vtracked` in-game commands; the `/vending`, `/vending-track`, `/vending-untrack`,
`/vending-tracked` slash commands; vending data on the existing marker poll; persistence for
track registrations and posted-notification message ids; wipe handling.

**Out of scope:** price history and trend reporting; cross-currency price conversion;
buyer-side "alert me when this item appears" watches; automated repricing; anything that
writes to a vending machine (the Rust+ API is read-only for vending).

## Current Architecture

`ConnectionSupervisor.PollMarkersAsync` polls `GetMapMarkers` every `MarkerPollInterval`
(5s default, 2s while a Chinook is up) per connected server, diffs the result, and publishes
`MapMarkersChangedEvent`. `RustPlusSocketSource.GetMapMarkersAsync` maps four buckets
(`CargoShipMarkers`, `PatrolHelicopterMarkers`, `Ch47Markers`, `TravellingVendorMarkers`) into
`MapMarkerSnapshot` and discards the rest — including `VendingMachineMarkers`.

RustPlusApi 2.0.0-beta.7 exposes:

```csharp
MapMarkers.VendingMachineMarkers : Dictionary<ulong, VendingMachineMarker>
VendingMachineMarker  : Id, X, Y, Name, IsOutOfStock (bool?), VendingMachineItems
VendingMachineItem    : Id, StackSize, CurrencyId, CostPerStack, StackSizeAmount,
                        IsItemBlueprint, IsCurrencyBlueprint, ItemLife, ItemMaxLife,
                        PriceMultiplier, ReceivedQuantityMultiplier
```

Rust re-sends the complete vending set on every poll — there is no incremental protocol.

Existing patterns this design reuses rather than reinvents:

- `Features.Workspace` channel keys, `ChannelSpec` providers, and `IXChannelLocator`.
- `Posting/IXChannelPoster` with `EnsureAsync` / `DeleteMessageAsync` (see
  `IAlarmChannelPoster`), which self-heals a hand-deleted message.
- `Discord/Posting/RenderGate` + `RenderCanonicalizer`, which suppress no-op Discord edits.
- `Relaying/XWipePurger` reacting to `ServerWipedEvent`.
- `IItemDatabase.Resolve` for name-or-id lookup, with its `ambiguous` / `notfound` outcomes.
- `ServerAutocompleteHandler` / `ServerResolver` for multi-server guilds.
- `MapGrid.LabelFor(x, y, worldSize, style)` for grid labels under both grid conventions.

## Decisions

Recorded with the reasoning, because several were live forks during design.

| Decision | Choice | Why |
|---|---|---|
| What `/vending-track` watches | Undercuts, same model as `!vtrack` | One notification model to build, test and explain; the grid form is just bulk registration |
| Price comparison | Same currency only, per-unit | Cross-currency exchange rates are guesses that drift every wipe and would produce false alarms |
| Grid registration binding | The grid cell, not a snapshot of machine ids | "and the future ones" — a machine deployed later is picked up automatically |
| Undercut message shape | One live message per tracked listing | A drained popular item would otherwise burst the channel |
| Sell-out message shape | One live message per registered machine | A fully drained 20-item shop is one message, not twenty |
| Search output | In-game top 3 one-liner; Discord top 10 embed | In-game chat truncates hard; Discord can carry a table |
| Command naming | Flat, dedicated names | Matches every existing command in the repo |
| Reference price | The **lowest** of my prices for a listing | That is what defines my competitiveness; if I am still cheapest, nothing is wrong |
| Sold-out rivals | Excluded from undercut alerts, shown in search | A sold-out rival takes none of my sales, but is still a real search result |
| Wipe | Purge grid registrations, keep manual listings | The base is gone; an item+price pair stays valid across wipes |
| Pings | None | Undercut alerts fire far more often than alarms and would train people to mute the channel |

### The message lifecycle principle

Both notification kinds obey one rule:

> **Your own action clears the message; the world's changes only edit it.**

- A rival appears, changes price, or restocks → the undercut message is **edited**.
- *You* reprice → the undercut message is **deleted**; a fresh one is posted only if rivals
  still beat your new price.
- An offer sells out → the stock message is **edited**.
- *You* refill → the stock message is **deleted**; a fresh one is posted only if something is
  still sold out.

The delete-and-repost half exists so that reacting to an alert produces a new unread rather
than a silent edit that still says you are losing, which nobody notices. Each persisted
notification row therefore stores the state it was rendered against (the reference price, or
the sold-out offer set) so "did *I* change something" is answerable without a second poll.

## Architecture

### 1. Data path

New records in `RustPlusBot.Abstractions/Connections`:

```csharp
public sealed record VendingOfferSnapshot(
    int ItemId,  bool ItemIsBlueprint,
    int Quantity,                 // VendingMachineItem.StackSize
    int CurrencyId, bool CurrencyIsBlueprint,
    int CostPerOrder,             // VendingMachineItem.CostPerStack
    int AmountInStock);           // VendingMachineItem.StackSizeAmount

public sealed record VendingMachineSnapshot(
    ulong Id, float X, float Y, string? Name, bool? IsOutOfStock,
    IReadOnlyList<VendingOfferSnapshot> Offers);

public sealed record MapMarkersSnapshot(
    IReadOnlyList<MapMarkerSnapshot> Markers,
    IReadOnlyList<VendingMachineSnapshot> VendingMachines);
```

`IRustServerConnection.GetMapMarkersAsync` returns `MapMarkersSnapshot` instead of
`IReadOnlyList<MapMarkerSnapshot>`. `RustPlusSocketSource` maps `data.VendingMachineMarkers`;
the disconnected stub returns an empty snapshot. This is a single Rust+ request carrying both
payloads — a separate vending poll would double the heaviest recurring request, since
`GetMapMarkers` also carries every player marker.

In `PollMarkersAsync`, `snapshot.Markers` feeds every existing code path unchanged, and a new
`VendingMachinesObservedEvent(GuildId, ServerId, WorldSize, Machines)` is published on the
event bus each poll. World size comes from the existing `DimensionsHolder`; grid labels need
it. The event is published on every poll, not only on change — the index is a wholesale
replacement and the evaluators are pure, so change detection lives downstream where it can be
tested.

### 2. Core model

```
ListingKey = (ItemId, ItemIsBlueprint, CurrencyId, CurrencyIsBlueprint)
UnitPrice  = CostPerOrder / Quantity
```

Comparison never crosses a `ListingKey`, so currencies and blueprint-ness cannot be conflated.
`UnitPrice` is never materialised as a float: comparisons are exact integer cross
multiplications, `a.CostPerOrder * b.Quantity <= b.CostPerOrder * a.Quantity`. This keeps
ordering stable and test assertions exact.

**Ownership.** A machine is mine when its `(X, Y)` falls inside a grid cell registered for that
server, resolved with `MapGrid.LabelFor` under the server's configured grid style
(`ServerMapSettings.GridStyle`, the same setting the map renderer uses), so `!vtrack D7` means
the cell the player sees on their own map. Every other machine is a
competitor. A consequence worth stating: a neighbour's machine in a registered cell counts as
mine and is never reported as an undercut.

**Reference price.** For a `ListingKey`, the reference is the lowest `UnitPrice` across all my
sources — machines inside my registered grids, plus manual `/vending-track` entries. Listing
the same item at 5 and 6 scrap means a rival at 5.5 has not beaten me and no alert fires.

**Undercut.** A competitor machine offering the same `ListingKey` at `UnitPrice <= reference`
with `AmountInStock > 0`.

**Sell-out.** For a machine inside a registered grid: `IsOutOfStock == true` means the whole
shop is empty and the message says so without enumerating; otherwise the message lists the
offers with `AmountInStock == 0`. A null `IsOutOfStock` is *unknown* and falls back to
per-offer stock. An offer that disappears from the list is a delisting, not a sell-out.
Manual `/vending-track` listings have no machine behind them and never produce stock alerts.

### 3. In-memory index

`Features.Vending` holds one index per `(guild, server)`, replaced wholesale on each observed
event. Rust re-sends the complete set every poll, so there is no merge, no eviction and no
staleness bookkeeping. A disconnect clears the index.

Nothing about the index is persisted. Search reads it; the evaluators read it.

### 4. Evaluation and reconciliation

Two **pure** functions, each a function of (index snapshot, tracks) → desired notification set:

- `UndercutEvaluator` — reference pricing, currency isolation, sold-out exclusion, exact
  rational comparison.
- `StockEvaluator` — machine-level collapse, per-offer zeros, unknown-flag fallback.

`VendingNotificationRelay` does nothing but reconcile those two desired sets against persisted
state:

1. Desired, no stored message id → post, store the id and the rendered-against state.
2. Desired, stored id, rendered-against state unchanged by me → edit behind `RenderGate`
   (an unchanged render costs zero Discord calls).
3. Desired, stored id, **I** changed the underlying state (reference price moved / a sold-out
   offer was restocked) → delete, clear the row; the next evaluation reposts fresh if the
   problem persists.
4. Not desired → delete, drop the row.

Keeping all the logic that must be correct inside two pure functions means it is testable with
no Discord, no database and no socket.

`VendingOptions.MaxNotificationsPerServer` (default 50) applies **per kind**, so a busy
undercut situation cannot starve alerts about your own shop. Past the cap the relay stops
posting new messages and logs a warning naming the count dropped — never a silent truncation.

### 5. Discord channel

A new per-server `#vending` channel: `WorkspaceChannelKeys.ServerVending = "vending"`,
`ChannelPermissionProfile.ReadOnly`, order 10 in `ServerWorkspaceSpecProvider`, with
`IVendingChannelLocator` / `VendingChannelLocator` mirroring `AlarmChannelLocator`. Always
provisioned; no capability gate.

### 6. Commands

Tracks are per `(guild, server)` — shared team state, not per-user. The registering Steam id
or Discord user is stored for display only. Multi-server guilds resolve the target via the
existing `ServerAutocompleteHandler` / `ServerResolver`. Grid arguments are validated against
the live world size, so `!vtrack Z99` gets a clear error rather than a silent no-op.

In-game (`ICommandHandler`, single-line replies):

| Command | Behaviour |
|---|---|
| `!vending <item>` | `Metal Pipe: D7 12 scrap (18 left) · K12 15 scrap (4) · B3 15 scrap (out)` |
| `!vtrack <grid>` | Registers the cell; replies with machines found and listings now tracked |
| `!vuntrack <grid>` | Unregisters; deletes that cell's orphaned notifications |
| `!vtracked` | Registered cells and tracked-listing counts |

Slash (`VendingModule`):

| Command | Behaviour |
|---|---|
| `/vending <item>` | Embed, top 10, in-stock first then unit price, `+N more` footer |
| `/vending-track <item> <price> [currency] [quantity]` | Manual listing; currency defaults to Scrap, quantity to 1; item and currency use item autocomplete |
| `/vending-untrack` | Autocomplete over registered cells and manual listings |
| `/vending-tracked` | Embed of both |

All strings live in `Strings.resx` and `Strings.fr.resx`. Both surfaces get
`CommandHelpCatalog` entries; the repo has a test that fails on drift.

## Component Layout

`IVendingReadModel` and `IVendingTrackService` go in `RustPlusBot.Abstractions/Vending/`,
matching the existing `Abstractions/Map/IInfoMapReadModel.cs`. This matters: `ICommandHandler`
is internal to `Features.Commands`, so the in-game handlers must live there, and routing
through Abstractions keeps `Features.Commands` from depending on `Features.Vending`.

```
src/RustPlusBot.Abstractions/Vending/
  IVendingReadModel.cs, IVendingTrackService.cs, VendingOffer.cs, ListingKey.cs
src/RustPlusBot.Abstractions/Connections/
  VendingOfferSnapshot.cs, VendingMachineSnapshot.cs, MapMarkersSnapshot.cs
src/RustPlusBot.Abstractions/Events/
  VendingMachinesObservedEvent.cs
src/RustPlusBot.Features.Vending/
  VendingOptions.cs, VendingServiceCollectionExtensions.cs
  Indexing/VendingIndex.cs
  Ownership/GridOwnership.cs
  Evaluating/UndercutEvaluator.cs, StockEvaluator.cs
  Searching/VendingSearch.cs
  Relaying/VendingNotificationRelay.cs, VendingWipePurger.cs
  Posting/IVendingChannelPoster.cs, DiscordVendingChannelPoster.cs
  Rendering/VendingEmbedRenderer.cs
  Hosting/VendingHostedService.cs
  Modules/VendingModule.cs
src/RustPlusBot.Features.Commands/Handlers/
  VendingCommandHandler.cs, VTrackCommandHandler.cs,
  VUntrackCommandHandler.cs, VTrackedCommandHandler.cs
src/RustPlusBot.Domain/Vending/
  VendingGridTrack.cs, VendingListingTrack.cs,
  VendingNotification.cs, VendingStockNotification.cs
src/RustPlusBot.Persistence/Vending/
  IVendingStore.cs, VendingStore.cs
src/RustPlusBot.Persistence/Configurations/
  VendingGridTrackConfiguration.cs, VendingListingTrackConfiguration.cs,
  VendingNotificationConfiguration.cs, VendingStockNotificationConfiguration.cs
tests/RustPlusBot.Features.Vending.Tests/
```

## Persistence

Migration `VendingTracking`. All four entities cascade from `RustServer`, following the
existing `ServerRemovalCascade` migration.

| Entity | Unique key | Fields |
|---|---|---|
| `VendingGridTrack` | `(ServerId, Grid)` | grid label, registering Steam id, created-at |
| `VendingListingTrack` | `(ServerId, ListingKey)` | listing key, quantity, cost per order, registering Discord user, created-at |
| `VendingNotification` | `(ServerId, ListingKey)` | message id, `ReferenceQuantity`, `ReferenceCostPerOrder`, posted-at |
| `VendingStockNotification` | `(ServerId, MachineId)` | message id, rendered sold-out set, machine-empty flag, posted-at |

Re-running `/vending-track` for an existing `ListingKey` updates the price rather than
inserting a duplicate.

The "rendered sold-out set" is stored as the sorted, comma-joined item ids the message was
rendered against — enough to answer "did the owner restock something" by comparison, without a
child table. It is compared as a whole string, never parsed.

## Failure Behaviour

Two rules do most of the work:

- **Evaluation only ever runs on a freshly observed event.** A failed poll publishes nothing
  and the last good index stands. A disconnect clears the index and *freezes* notifications in
  place — a dropped socket must never be read as "every rival vanished" and trigger a mass
  delete.
- **The consume loop catches per event, inside the `await foreach`.** This repo has been bitten
  by a `try`/`catch` wrapped *around* the loop, where one escaping exception killed the
  consumer permanently. Each event body gets its own guard.

Beyond those: Discord failures are swallowed and logged with the message id retained, so the
next pass retries; a hand-deleted message surfaces as a 404 on edit and is reposted by
`EnsureAsync`. Item-resolution ambiguity reuses the existing `command.item.ambiguous` and
`command.item.notfound` keys. No live socket reuses `command.notconnected`.

A machine absent from a successful poll is genuinely gone (destroyed, decayed, picked up) and
its stock message is deleted; the disconnect rule already covers the case where the whole poll
is missing.

## Wipe

`VendingWipePurger` on `ServerWipedEvent` deletes every notification message of both kinds and
clears grid registrations, but keeps manual `/vending-track` listings. Grid registrations point
at a base that no longer exists; a manual item+price pair stays valid. This differs from
`AlarmWipePurger` and `SwitchWipePurger`, which purge everything because their entity ids
become invalid — not the case here.

## Testing

TDD; the pure units get their tests written first.

- `UndercutEvaluatorTests` — reference is the lowest of my prices; sold-out rivals excluded;
  scrap never compared to cloth; blueprint and non-blueprint are distinct listings; exact
  rational comparison (2-for-10 *does* undercut 1-for-6; 3-for-16 does *not* undercut 1-for-5).
- `StockEvaluatorTests` — machine-level flag collapses instead of enumerating; per-offer zeros
  listed when the flag is false; null flag falls back to per-offer; restock deletes; vanished
  machine deletes; a manual listing never produces a stock alert.
- `VendingSearchTests` — in-stock before out-of-stock, then unit price; top-3 and top-10
  truncation with the `+N more` count.
- `GridOwnershipTests` — cell binding under both `InGame` and `RustPlus` grid styles, including
  the partial edge cell.
- `VendingNotificationRelayTests` — the full state machine, especially reference-price change →
  delete not edit; restock → delete not edit; disconnect → no deletes; cap exceeded → warning
  logged.
- `VendingWipePurgerTests` — grids cleared, manual listings kept, all messages deleted.
- `VendingStoreTests` on the existing `SqliteContextFixture`.
- `VendingRegistrationTests` for DI wiring, plus the existing help-catalog drift test.

Existing tests that construct `IRustServerConnection` fakes need updating for the
`MapMarkersSnapshot` return type; this is mechanical and the compiler finds every site.

## Build Order

Each step leaves a green build.

1. Abstractions — snapshots, `MapMarkersSnapshot`, the observed event, read-model and
   track-service interfaces.
2. `RustPlusSocketSource` + `ConnectionSupervisor` + every call site and test fake. No
   behaviour change yet.
3. Domain entities, `VendingStore`, EF configurations, `VendingTracking` migration.
4. Pure logic — `GridOwnership`, `UndercutEvaluator`, `StockEvaluator`, `VendingSearch`.
5. Workspace `#vending` channel, locator, poster, embed renderer.
6. `VendingHostedService`, `VendingNotificationRelay`, `VendingWipePurger`.
7. Commands — in-game handlers, slash module, help catalog, `Strings.resx` and
   `Strings.fr.resx`.
8. README and docs.

## Open Risks

- **Neighbour contamination.** A machine belonging to someone else inside a registered grid
  cell is treated as mine, so it never raises an undercut and its sell-outs are reported as
  mine. Accepted as the cost of "future machines are tracked automatically"; a per-shop-name
  filter is the escape hatch if it bites.
- **Marker id stability.** Stock notifications key on the vending machine's marker id. If Rust
  reassigns ids across a server restart, stale stock messages are deleted and reposted once —
  noisy but self-correcting.
- **Payload growth.** `MapMarkersSnapshot` now carries every machine's full offer list on a 5s
  poll. It stays in-process and is never serialised, but on a very busy server the index is the
  largest object this bot holds; worth watching if memory regresses.
