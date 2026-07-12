# Subsystem 6c — Item Database Calculators pt.3 (Durability / raid-cost)

**Status:** Approved (brainstorming) · **Date:** 2026-06-28 · **Branch:** `feat/item-database-3` off `develop`
**Predecessor:** 6b (PR #29, `a05739c`) — Decay + Upkeep calculators (`/decay /upkeep` + in-game, schema v2).

---

## 1. Summary

6c is the third slice of the Item Database subsystem. It ships the **durability** calculator —
in practice a **raid-cost calculator**: given a target (a deployable, a building block, or a
vehicle), it lists the **explosives** required to destroy it, with quantity, **sulfur** cost, and
time. Surfaces are the usual pair: in-game `!durability <target>` and Discord `/durability <target>`.

It follows the 6a/6b shape — extend the bundle, bump the schema, add a generator source + validator
rules, regenerate the embedded bundle, add an in-game handler + a slash command + a pure formatter +
EN/FR strings — with **one structural departure**: raid data does **not** live on `ItemRecord`. The
core raid targets (walls, doors, floors) are name-keyed building grades, not item-DB items, so the
data gets its own top-level **`RaidTargets`** table covering all three target kinds uniformly
(decision locked in brainstorming: Approach A).

The upstream source (`rustlabsDurabilityData.json`) is **19.3 MB** and must never be bundled raw.
The generator trims it to the `explosive` tool group only (~5,361 rows of the 65,276), collapsing it
to a ~1 MB projection. No new entities, migrations, events, options, or background services (static
read-only data — zero EF drift).

The command keeps the roadmap name **`/durability`** for continuity even though its payload is raid
cost; internal types are named `Raid*` for honesty.

---

## 2. Scope

### In scope (6c)

- New `RaidTarget` / `RaidCost` records + `RaidTargetKind` enum on `ItemDataset` (a new top-level
  `RaidTargets` list; `ItemRecord` untouched).
- `SchemaVersion` bump **v2 → v3**; `DatasetSources` gains `DurabilityAsOf`.
- Generator: a `DurabilitySource` (+ offline impl) reads `rustlabsDurabilityData.json`, keeps only
  the `explosive` group, projects the three sections (`items`/`buildingBlocks`/`other`) into
  `RaidTargets`; `Program.cs` wires it; `DatasetValidator` gains raid-target checks; the embedded
  `item-data.json` is regenerated.
- Lookup: a reusable name-matching core feeds `ResolveRaidTarget(query) → RaidMatch` on
  `IItemDatabase`.
- In-game `!durability`; Discord `/durability`; `/help` ItemDb group gains 2 rows (in-game + slash).
- EN/FR resx keys; full TDD coverage.

### Deferred (later slices)

- **Smelting** — furnace/oven-centric, small (29 KB) but a different per-smelter UX; its own slice
  (your explicit call this round: durability now, smelting next).
- **Non-explosive tool groups** — `guns` (39 k rows), `melee`, `throw`, `torpedo`, `turret`. Dropped
  from the bundle; a later slice could add `torpedo`/`turret` for vehicle raiding if wanted.
- **CCTV** — monument→camera-codes lookup, not item-keyed; separate plumbing, its own micro-slice.
- **Live-scrape source** — the `IRustLabsSource` seam stays offline-transform only, as in 6a/6b.

### Explicit non-goals

- No item-condition / repair-cost data (the literal meaning of "durability"). Out of scope; the
  command is a raid calculator by design.
- No raid *planning* (sequencing, soft-side pathing) — a flat per-target cost list only.
- No new Discord channel, entity, migration, option, or hosted service.

---

## 3. Architecture

### 3.1 Schema (`Features.ItemData/Data/ItemDataset.cs`)

`ItemDataset` gains a fourth member and a provenance date; the schema version bumps to **3**.

```csharp
public sealed record ItemDataset(
    int SchemaVersion,
    DatasetSources Sources,
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<RaidTarget> RaidTargets);          // NEW

public sealed record DatasetSources(
    DateOnly NamesAsOf, DateOnly RecycleAsOf, DateOnly CraftAsOf,
    DateOnly ResearchAsOf, DateOnly DecayAsOf, DateOnly UpkeepAsOf,
    DateOnly DurabilityAsOf);                          // NEW

/// <summary>One raid target and the explosive cost to destroy it.</summary>
public sealed record RaidTarget(
    string Key,                 // item-id string (Item kind) or target name (block/vehicle)
    string Name,                // display name
    RaidTargetKind Kind,
    IReadOnlyList<RaidCost> Costs);

public enum RaidTargetKind { Item, BuildingBlock, Vehicle }   // items / buildingBlocks / other

/// <summary>One explosive's cost against a target. Fields null where RustLabs omits them.</summary>
public sealed record RaidCost(
    int ToolId,                 // the explosive item id (resolves via the item spine)
    string? Side,               // "soft" | "hard" | "both" | null (building-block face)
    string? Caption,            // sub-label, e.g. "Semi-Automatic Rifle", "Stuck (right click)"
    double Quantity,            // units of the tool (may be fractional in source; rendered rounded up)
    double? TimeSeconds,        // total time
    int? Sulfur,                // total sulfur cost (the metric raiders compare on)
    int? Fuel);                 // total low-grade fuel cost
```

Dropped from the upstream row shape: `quantityTypeId` (never populated for the `explosive` group —
verified 0/5,361) and per-target HP (absent from the durability source). `ItemRecord`, `DecayInfo`,
`UpkeepCost`, etc. are **unchanged**.

### 3.2 Source data

`rustlabsDurabilityData.json` — top-level `{ items, buildingBlocks, other }`:

- `items` (299): keyed by item id; **all resolve** via `items.json` → `RaidTargetKind.Item`.
- `buildingBlocks` (100): keyed by **name** ("Stone Wall", "Sheet Metal Door"…) → `BuildingBlock`.
- `other` (17): keyed by **name** ("Bradley APC", "Attack Helicopter"…) → `Vehicle`.

Each row: `{ group, which, toolId, caption, quantity, quantityTypeId, time, timeString, fuel,
sulfur }`. The generator keeps only `group == "explosive"` (15 tools: Timed Explosive Charge, Rocket
/ HV / Incendiary / MLRS Rocket, Satchel, Beancan, F1, 40mm HE, Explosive 5.56 ammo, Molotov, Flame
Thrower, Fire Arrow, Homing Missile). `which` → `Side`; `time`/`sulfur`/`fuel` carried straight.

### 3.3 Dependency direction

`Features.ItemData` (leaf) owns the schema, the bundle, and `IItemDatabase`. `Features.Commands`
depends on it (handlers + module + formatter). The generator (`tools/…`) depends on
`Features.ItemData` for the schema types only and is referenced by nothing in the app graph —
exactly as in 6a/6b.

---

## 4. Command surfaces (`Features.Commands`)

### 4.1 In-game `!durability`

`DurabilityCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer)` —
`Name => "durability"`. The name resolver is injected because the formatter resolves tool ids →
names (same 3-arg shape as `UpkeepCommandHandler`). Joins `context.Args` to a query, calls
`database.ResolveRaidTarget(query)`, and switches:

```text
RaidMatch.Found f   → localizer.Get("command.durability.ok",   culture, DurabilityLine.Format(f.Target, names))
RaidMatch.Ambiguous → localizer.Get("command.item.ambiguous",  culture, candidate names)   // reuse 6a key
_ (NotFound)        → localizer.Get("command.item.notfound",   culture, query)             // reuse 6a key
```

There is no "found but empty" branch — a `RaidTarget` is only emitted when it has ≥1 explosive cost.
Mirrors `DecayCommandHandler` exactly.

### 4.2 Slash `/durability`

Added to `ItemCommandModule`. The existing `RespondForAsync` is `ItemRecord`-centric (resolves an
item, passes `ItemRecord`), so `/durability` gets a **parallel** `RespondForRaidAsync` helper that
resolves a `RaidTarget` instead, with the same guild guard, scoped DI, culture lookup, ephemeral
embed, and `data as of <DurabilityAsOf>` footer.

```csharp
[SlashCommand("durability", "Show the explosives needed to destroy a target")]
public Task DurabilityAsync([Summary("target", "Item, wall/door, or vehicle name")] string target) =>
    RespondForRaidAsync(target);
```

### 4.3 Formatter (`Features.Commands/Formatting/DurabilityLine.cs`)

Pure static `Format(RaidTarget target, IItemNameResolver names) → string`. One line per
`(tool, side)` cost, **sorted by sulfur ascending with null sulfur last**, tie-broken by quantity.
Each line: tool name (resolved via `names`), `×<quantity rounded up>`, side annotation when
`Side ∉ { null, "both" }`, `<sulfur> sulfur` when present, time when present, and `caption` when
present. Renders only non-null fields (the 6b `DecayLine`/`UpkeepLine` discipline).

Example — **Stone Wall**:

```text
Timed Explosive Charge ×2 — 4400 sulfur (11.5s)
Satchel Charge ×10 — 4800 sulfur (22.5s)
Rocket ×4 — 5600 sulfur (18s)
High Velocity Rocket ×31 (soft) — 6200 sulfur
Explosive 5.56 Rifle Ammo ×173 (soft) — 4325 sulfur · Semi-Automatic Rifle
…
```

### 4.4 `/help`

`CommandHelpCatalog` gains 2 ItemDb rows (in-game `!durability`, slash `/durability`), with the same
drift-guard test the other ItemDb commands have (every registered command appears in the catalog).

---

## 5. Lookup (`Features.ItemData/Lookup`)

`ItemLookup`'s fuzzy match core (exact id / exact name / case-insensitive / contains, with ambiguity
detection) is **generalized** to operate over any `(displayName, payload)` sequence, then reused for
raid targets. `IItemDatabase` gains:

```csharp
RaidMatch ResolveRaidTarget(string query);
```

returning a discriminated union mirroring `ItemMatch`:

```csharp
public abstract record RaidMatch
{
    public sealed record Found(RaidTarget Target) : RaidMatch;
    public sealed record Ambiguous(IReadOnlyList<RaidTarget> Candidates) : RaidMatch;
    public sealed record NotFound : RaidMatch;
}
```

`EmbeddedItemDatabase` already owns the deserialized dataset, so it builds the raid name index once
and answers `ResolveRaidTarget`. Targets match by display name (and, for `Item` kind, by item id).

---

## 6. The generator tool (`tools/RustPlusBot.ItemData.Generator`)

- New `IDurabilitySource` + `OfflineDurabilitySource` reading `rustlabsDurabilityData.json` from the
  rustplusplus `staticFiles` dir, filtering to `group == "explosive"`, projecting all three sections
  into `RaidTarget`s (id-keyed items resolve their name via the names source; blocks/vehicles use
  their name as both key and display).
- `Program.cs` wires the source, stamps `DurabilityAsOf`, and includes `RaidTargets` in the emitted
  dataset.
- `DatasetValidator` gains: every `RaidCost.ToolId` resolves to a known item; `Quantity > 0`; a
  **raid-target floor** (≥ 300 targets) so upstream shape drift can't silently gut the table. On any
  failure: print to stderr, exit non-zero, **do not write** (never clobber a good bundle).
- The bundle (`item-data.json`) is regenerated: ~585 KB → ~1.6 MB (embedded resource, deserialized
  once at startup). README source-file table + provenance section updated.

---

## 7. Error handling

- **Bundle**: schema-version mismatch already hard-fails the loader (existing behaviour); the v3 bump
  forces a regenerate. A malformed/oversized raw source fails generator validation, not the bot.
- **Lookup**: unknown target → `NotFound` → friendly "not found" reply (reused 6a key). Ambiguous →
  candidate list (reused 6a key). No exceptions on user input.
- **Formatter**: every field is null-guarded; a target with no sulfur on a row still renders (time /
  fuel / bare quantity). Targets with zero explosive costs are never emitted, so the handler has no
  empty-list path.

---

## 8. Testing (TDD)

- **Generator**: explosive-only filter (drops guns/melee/throw/torpedo/turret); three-section
  projection with correct `RaidTargetKind`; item-id vs name keying; validator floor + unknown-tool +
  non-positive-quantity loud-fail; no-write-on-failure.
- **Lookup**: exact id, exact name, case-insensitive, contains, ambiguous, not-found over raid names;
  generalized matcher parity with `ItemLookup`.
- **Formatter**: sulfur-ascending sort with null-last; side annotation only when soft/hard; caption
  rendered; quantity rounded up; null sulfur/time/fuel omitted cleanly.
- **Handler + module**: Found / Ambiguous / NotFound switch; guild guard on the slash path.
- **Bundle smoke**: a known target (e.g. "Stone Wall") resolves and reports the expected C4 count;
  schema version is 3; `RaidTargets` count ≥ floor.

Mirrors the 6b test footprint; suite stays green, format + analyzers clean.

## 9. Integration touchpoints (verify during build, don't assume)

- `IItemDatabase` surface + `EmbeddedItemDatabase` deserialization (new member must round-trip).
- `ItemCommandModule` DI scope + `IItemNameResolver` availability for tool-name resolution.
- `CommandServiceCollectionExtensions.AddCommands` — register `DurabilityCommandHandler`.
- `CommandHelpCatalog` drift-guard test (new rows).
- `Strings.resx` / `Strings.fr.resx` shared catalog (new `command.durability.*` keys).
- Generator README + `DatasetSources` constructor arity (compile-time fan-out across call sites).

## 10. Gates (per project conventions)

- `RustPlusBot.slnx` builds; `dotnet jb cleanupcode --profile=ReformatAndReorder` produces no diff;
  full test suite green; bundle regenerated and committed.

## 11. Decisions locked in brainstorming

- **Scope**: durability (raid cost) **this slice**; smelting deferred to the next.
- **Architecture A**: dedicated `RaidTargets` table; `ItemRecord` untouched.
- **Trim**: keep the whole `explosive` group (rustlabs' own classification); drop guns/melee/throw/
  torpedo/turret. Never bundle the 19.3 MB raw.
- **Command name** stays `/durability` (roadmap continuity); internal types named `Raid*`.
- **Output**: all explosive rows per target (no top-N), sorted by sulfur ascending.
- **Drop** `quantityTypeId` and HP (absent/unpopulated for explosives).
