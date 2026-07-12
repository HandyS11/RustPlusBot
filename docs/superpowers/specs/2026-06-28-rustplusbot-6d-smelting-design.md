# Subsystem 6d — Item Database Calculators pt.4 (Smelting)

**Status:** Approved (brainstorming) · **Date:** 2026-06-28 · **Branch:** `feat/item-database-4` off `develop`
**Predecessor:** 6c (PR #30, `fc9fcbd`) — Durability / raid-cost calculator (`/durability` + in-game, schema v3).

---

## 1. Summary

6d is the fourth slice of the Item Database subsystem. It ships the **smelting** calculator: given a
**smelter** (Furnace, Large Furnace, Electric Furnace, Camp Fire, Barbeque, Small Oil Refinery, and
the cooking variants), it lists every **conversion** that smelter performs — input → output, with
output quantity/probability, **wood** (fuel) cost, and **time** per smelt. Surfaces are the usual
pair: in-game `!smelt <smelter>` and Discord `/smelt <smelter>`.

It follows the 6a/6b/6c shape — extend the bundle, bump the schema, add a generator source +
validator rules, regenerate the embedded bundle, add an in-game handler + a slash command + a pure
formatter + EN/FR strings — and reuses 6c's **structural departure**: smelting data does **not** live
on `ItemRecord`. It gets its own top-level **`Smelters`** table resolved by name, exactly mirroring
6c's `RaidTargets` (decision locked in brainstorming: **smelter-keyed card**).

The upstream source (`rustlabsSmeltingData.json`) is **28.5 KB** — small, transformed offline, no raw
trimming needed (contrast 6c's 19 MB). No new entities, migrations, events, options, or background
services (static read-only data — zero EF drift).

---

## 2. Scope

### In scope (6d)

- New `Smelter` / `SmeltConversion` records on `ItemDataset` (a new top-level `Smelters` list;
  `ItemRecord` untouched).
- `SchemaVersion` bump **v3 → v4**; `DatasetSources` gains `SmeltingAsOf`.
- Generator: an `ISmeltingSource` (+ offline impl) reads `rustlabsSmeltingData.json`, projecting the
  per-smelter conversion lists into `Smelters`; `Program.cs` wires it; `DatasetValidator` gains
  smelter checks; the embedded `item-data.json` is regenerated.
- Lookup: the generalized name-matching core (from 6c) feeds `ResolveSmelter(query) → SmeltMatch` on
  `IItemDatabase`.
- In-game `!smelt`; Discord `/smelt`; `/help` ItemDb group gains 2 rows (in-game + slash).
- EN/FR resx keys; full TDD coverage.

### Deferred (later slices)

- **Input-keyed "where can I smelt X" view** — the cross-cut over the same `Smelters` table (the
  brainstorming option not chosen). A later slice could add it without a schema change.
- **Slot-count / parallel-throughput math** — slot counts are not in this data; per-item rate only.
- **Fuel burn-duration / fuel-stack modeling** — beyond the per-smelt `woodQuantity` carried here.
- **CCTV** — monument→camera-codes lookup, not item-keyed; separate plumbing, its own micro-slice
  (still the last open subsystem-6 item after this slice).
- **Live-scrape source** — the offline-transform seam stays as in 6a–6c.

### Explicit non-goals

- No input-item lookup on `ItemRecord` (the same input name carries different ids across smelters, so
  per-item keying would split one item across records — the reason for a dedicated table).
- No fuel-efficiency ranking or "best smelter" recommendation — a faithful per-smelter card only.
- No new Discord channel, entity, migration, option, or hosted service.

---

## 3. Architecture

### 3.1 Schema (`Features.ItemData/Data/ItemDataset.cs`)

`ItemDataset` gains a fifth member and a provenance date; the schema version bumps to **4**.

```csharp
public sealed record ItemDataset(
    int SchemaVersion,
    DatasetSources Sources,
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<RaidTarget> RaidTargets,
    IReadOnlyList<Smelter> Smelters);                 // NEW

public sealed record DatasetSources(
    DateOnly NamesAsOf, DateOnly RecycleAsOf, DateOnly CraftAsOf,
    DateOnly ResearchAsOf, DateOnly DecayAsOf, DateOnly UpkeepAsOf,
    DateOnly DurabilityAsOf, DateOnly SmeltingAsOf);  // NEW

/// <summary>One smelter and the conversions it performs.</summary>
public sealed record Smelter(
    string Key,                          // smelter item id (string), like RaidTarget.Key for Item kind
    string Name,                         // display name ("Furnace", "Camp Fire", …)
    IReadOnlyList<SmeltConversion> Conversions);

/// <summary>One input → output conversion within a smelter.</summary>
public sealed record SmeltConversion(
    int InputId,                         // the smeltable input item id (resolves via the item spine)
    int OutputId,                        // the produced item id (resolves via the item spine)
    int OutputQuantity,                  // units produced per smelt
    double OutputProbability,            // 0..1 (e.g. Wood → Charcoal is 0.75)
    double WoodQuantity,                 // fuel per smelt; 0 ⇒ electric / no fuel
    double TimeSeconds);                 // seconds per smelt
```

All 10 smelters are real items (their ids resolve to names via `items.json`), so — unlike
`RaidTarget` — **no `Kind` enum** is needed; `Key` is always an item-id string. Input/output are
stored as ids and resolved to names at display time (the `RaidCost.ToolId` discipline). `ItemRecord`,
`DecayInfo`, `UpkeepCost`, `RaidTarget`, etc. are **unchanged**.

### 3.2 Source data

`rustlabsSmeltingData.json` — a top-level object keyed by **smelter id** (10 keys), each mapping to a
list of conversion rows:

```jsonc
"-1999722522": [                         // Furnace
  { "fromId": "-4031221", "woodQuantity": 1.67, "toId": "69511070",
    "toQuantity": 1, "toProbability": 1, "time": 3.33, "timeString": "3.33 sec" },
  …
]
```

The 10 smelters: **Furnace** (8), **Large Furnace** (8), **Electric Furnace** (7, `woodQuantity` 0),
**Small Oil Refinery** (6), **Camp Fire** / **Barbeque** / **Skull Fire Pit** / **Cursed Cauldron** /
**Hobo Barrel** / **Stone Fireplace** (20 each — cooking + scrap-burn rows). Conversions cover ore →
refined (Metal Ore → Metal Fragments), food cooking, can-scrapping (Empty Tuna Can → Metal
Fragments), crude → fuel, and Wood → Charcoal (prob 0.75). `fromId`/`toId` carried as
`InputId`/`OutputId`; `woodQuantity`/`toQuantity`/`toProbability`/`time` carried straight. The
human-readable `timeString` is dropped (formatter re-derives it). **Source order is preserved.**

Note the duplicate-name wrinkle: the same display name (e.g. "Metal Ore") appears under different
item ids across smelters (cookers use `989925924`, furnaces use `-4031221`). Both resolve to the same
name via `items.json`; storing the raw id and resolving at display time keeps every row faithful.

### 3.3 Dependency direction

`Features.ItemData` (leaf) owns the schema, the bundle, and `IItemDatabase`. `Features.Commands`
depends on it (handler + module + formatter). The generator depends on `Features.ItemData` for the
schema types only — exactly as in 6a–6c.

---

## 4. Command surfaces (`Features.Commands`)

### 4.1 In-game `!smelt`

`SmeltCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer)` —
`Name => "smelt"`. The name resolver is injected because the formatter resolves input/output ids →
names (same 3-arg shape as `DurabilityCommandHandler`). Joins `context.Args` to a query, calls
`database.ResolveSmelter(query)`, and switches:

```text
SmeltMatch.Found f   → localizer.Get("command.smelt.ok",       culture, SmeltLine.Format(f.Smelter, names))
SmeltMatch.Ambiguous → localizer.Get("command.item.ambiguous", culture, candidate names)   // reuse 6a key
_ (NotFound)         → localizer.Get("command.item.notfound",  culture, query)             // reuse 6a key
```

A `Smelter` is only emitted when it has ≥1 conversion, so there is no "found but empty" branch.
Mirrors `DurabilityCommandHandler` exactly.

### 4.2 Slash `/smelt`

Added to `ItemCommandModule` via a **parallel** `RespondForSmeltAsync` helper (the existing
`RespondForAsync` is `ItemRecord`-centric; `RespondForRaidAsync` is the 6c precedent for a non-item
resolve). Same guild guard, scoped DI, culture lookup, ephemeral embed, and
`data as of <SmeltingAsOf>` footer.

```csharp
[SlashCommand("smelt", "Show what a smelter converts and its fuel/time cost")]
public Task SmeltAsync([Summary("smelter", "Furnace, Camp Fire, Electric Furnace, …")] string smelter) =>
    RespondForSmeltAsync(smelter);
```

### 4.3 Formatter (`Features.Commands/Formatting/SmeltLine.cs`)

Pure static `Format(Smelter smelter, IItemNameResolver names) → string`. Header `"<name>:"` then one
line per conversion **in source order**:

```text
Furnace:
Metal Ore → Metal Fragments — 3.3s · 1.67 wood
Sulfur Ore → Sulfur — 1.7s · 0.83 wood
High Quality Metal Ore → High Quality Metal — 6.7s · 3.33 wood
Empty Can Of Beans → 15× Metal Fragments — 10s · 5 wood
Empty Tuna Can → 10× Metal Fragments — 10s · 5 wood
Wood → Charcoal (75%) — 2s · 1 wood
```

Rules: `15×` prefix only when `OutputQuantity > 1`; `(75%)` only when `OutputProbability < 1`;
fuel renders `<n> wood` when `WoodQuantity > 0` else `no fuel` (electric). Time reuses the
`DurabilityLine.FormatTime` style (`3.3s`, `1m 5s`). Renders only meaningful fields (the 6b/6c
`DecayLine`/`DurabilityLine` discipline).

**In-game length:** no top-N cap (mirrors `!durability`); the 20-row cooker cards are long but
faithful, and Discord embeds (4096 chars) hold them comfortably.

### 4.4 `/help`

`CommandHelpCatalog` gains 2 ItemDb rows (in-game `!smelt`, slash `/smelt`), covered by the existing
drift-guard test (every registered command appears in the catalog).

---

## 5. Lookup (`Features.ItemData/Lookup`)

The generalized `NameMatcher` (extracted in 6c) is reused over the smelter names. `IItemDatabase`
gains:

```csharp
SmeltMatch ResolveSmelter(string query);
```

returning a discriminated union mirroring `RaidMatch`:

```csharp
public abstract record SmeltMatch
{
    public sealed record Found(Smelter Smelter) : SmeltMatch;
    public sealed record Ambiguous(IReadOnlyList<Smelter> Candidates) : SmeltMatch;
    public sealed record NotFound : SmeltMatch;
}
```

`EmbeddedItemDatabase` already owns the deserialized dataset, so it builds the smelter name index once
and answers `ResolveSmelter`. Smelters match by display name (and, since `Key` is an item id, by id
too — falls out of the shared matcher). A new `SmeltLookup` wraps the matcher, paralleling
`RaidLookup`.

---

## 6. The generator tool (`tools/RustPlusBot.ItemData.Generator`)

- New `ISmeltingSource` + `OfflineSmeltingSource` reading `rustlabsSmeltingData.json` from the
  rustplusplus `staticFiles` dir. For each smelter key it resolves the smelter's display name via the
  names source and projects its rows into `SmeltConversion`s. A conversion whose input or output id
  has no name is **dropped and logged** (the orphan-drop pattern; verified 0 orphans at the current
  snapshot). A smelter left with no conversions is dropped.
- `Program.cs` wires the source, stamps `SmeltingAsOf = 2023-11-05` (the rustplusplus snapshot date
  for this file — `rustlabsSmeltingData.json` has not changed upstream since then, so it is older
  than the `2024-09-07` of the other rustlabs sources), and includes `Smelters` in the emitted
  dataset; schema → **4**.
- `DatasetValidator` gains: smelter count ≥ floor (`MinSmelterCount`, e.g. 8); every smelter name
  non-empty; every conversion `InputId`/`OutputId` resolves to a known item; conversions non-empty
  per smelter; `TimeSeconds > 0`; `WoodQuantity ≥ 0`; `OutputQuantity > 0`; `OutputProbability` in
  `(0, 1]`. On any failure: print to stderr, exit non-zero, **do not write** (never clobber a good
  bundle).
- The bundle (`item-data.json`) is regenerated — the delta is small (smelting is a ~28 KB source).
  README source-file table + provenance section updated.

---

## 7. Error handling

- **Bundle**: schema-version mismatch already hard-fails the loader; the v4 bump forces a regenerate.
  A malformed source fails generator validation, not the bot.
- **Lookup**: unknown smelter → `NotFound` → friendly "not found" reply (reused 6a key). Ambiguous →
  candidate list (reused 6a key). No exceptions on user input.
- **Formatter**: every optional rendering is guarded; a smelter with conversions always renders.
  Smelters with zero conversions are never emitted, so the handler has no empty-list path.

---

## 8. Testing (TDD)

- **Generator**: per-smelter projection (id → name, conversions mapped); duplicate-input-id rows kept
  faithfully; orphan-drop on unknown id; validator floor + unknown-id + non-positive-time/quantity +
  out-of-range-probability loud-fail; no-write-on-failure.
- **Lookup**: exact id, exact name, case-insensitive, contains, ambiguous, not-found over smelter
  names; shared-matcher parity with `RaidLookup`.
- **Formatter**: source-order preservation; `15×` only when qty > 1; `(75%)` only when prob < 1;
  `no fuel` when wood = 0; time formatting; multi-row cooker card renders fully.
- **Handler + module**: Found / Ambiguous / NotFound switch; guild guard on the slash path.
- **Bundle smoke**: a known smelter (e.g. "Furnace") resolves and reports Metal Ore → Metal
  Fragments; schema version is 4; `Smelters` count ≥ floor.

Mirrors the 6c test footprint; suite stays green, format + analyzers clean.

## 9. Integration touchpoints (verify during build, don't assume)

- `IItemDatabase` surface + `EmbeddedItemDatabase` deserialization (new member must round-trip).
- `ItemCommandModule` DI scope + `IItemNameResolver` availability for input/output-name resolution.
- `CommandServiceCollectionExtensions.AddCommands` — register `SmeltCommandHandler`.
- `CommandHelpCatalog` drift-guard test (new rows).
- `Strings.resx` / `Strings.fr.resx` shared catalog (new `command.smelt.*` keys).
- Generator README + `DatasetSources` constructor arity (compile-time fan-out across call sites).

## 10. Gates (per project conventions)

- `RustPlusBot.slnx` builds; `dotnet jb cleanupcode --profile=ReformatAndReorder` produces no diff;
  full test suite green; bundle regenerated and committed.

## 11. Decisions locked in brainstorming

- **Scope**: smelting **this slice**; CCTV deferred (last open subsystem-6 item).
- **Lookup axis**: **smelter-keyed card** — dedicated `Smelters` table resolved by name; `ItemRecord`
  untouched. Input-keyed "where can I smelt X" view explicitly deferred.
- **Schema**: no `Kind` enum (all smelters are items); store input/output as ids, resolve at display.
- **Output**: all conversions per smelter (no top-N), **source order preserved**; `no fuel` for
  electric (wood = 0).
- **Command name** `/smelt`; internal types named `Smelt*` / `Smelter`.
- **Drop** `timeString` (formatter re-derives); keep `toProbability` (Wood → Charcoal is 0.75).
