# Subsystem 6b — Item Database Calculators pt.2 (Decay + Upkeep)

**Status:** Approved (brainstorming) · **Date:** 2026-06-27 · **Branch:** `feat/item-database-2` off `develop`
**Predecessor:** 6a (PR #28, `6270b4e`) — Item Database + `/item /recycle /craft /research` + refresh tool.

---

## 1. Summary

6b is the second slice of the Item Database subsystem. It extends the 6a dataset with two new
per-item attributes — **decay** and **upkeep** — and ships the matching calculators on both
surfaces: in-game `!decay`/`!upkeep` and Discord `/decay`/`/upkeep`.

It is a faithful mirror of 6a: extend `ItemRecord` with two nullable fields, bump the schema version,
add two generator source loaders + validator rules, regenerate the embedded bundle, and add two
in-game handlers + two slash commands + two pure formatters + EN/FR strings. No new entities,
migrations, events, options, or background services (static read-only data — zero EF drift).

Upkeep is the dataset 4c's deferred Tool-Cupboard upkeep calculator was waiting on. **6b makes that
data available but does not build the 4c integration** — the `#storagemonitors` TC-upkeep summary
remains its own later slice. 6b stops at the dataset + the per-item calculators, exactly parallel
to 6a.

---

## 2. Scope

### In scope (6b)

- `DecayInfo` + `UpkeepCost`/`UpkeepEntry` on `ItemRecord` (nullable; inlined like recycle/craft/research).
- `SchemaVersion` bump; `DatasetSources` gains `DecayAsOf` + `UpkeepAsOf`.
- Generator: `IRustLabsSource` (+ offline impl) loads decay + upkeep; `Program.cs` inlines them;
  `DatasetValidator` gains decay/upkeep checks; the embedded `item-data.json` is regenerated.
- In-game `!decay`, `!upkeep`; Discord `/decay`, `/upkeep`; `/help` ItemDb group gains 2 rows.
- EN/FR resx keys; full TDD coverage.

### Deferred (later slices)

- **Durability** (raid calculator) — 19 MB tool×structure matrix; its own large slice (needs an
  embedding strategy, heavy trim or external).
- **Smelting** — furnace/oven-centric, different UX; its own slice.
- **CCTV** — monument→camera-codes lookup, not item-keyed; separate plumbing, its own micro-slice.
- **4c TC-upkeep summary** — wiring the new upkeep data into the `#storagemonitors` TC contents
  embed. 6b provides the data; the StorageMonitors integration is a separate slice.

### Explicit non-goals

- No live scraping (the generator stays offline-transform; live `IRustLabsSource` scrape is the
  standing 6a follow-up, untouched here).
- No new socket surface — decay/upkeep are static dataset attributes.
- No base-total upkeep aggregation — `/upkeep` is a per-building-block lookup, not a whole-base sum.

---

## 3. Architecture

### 3.1 Schema (`Features.ItemData/Data/ItemDataset.cs`)

```csharp
public sealed record ItemRecord(
    int Id, string Name, int StackSize, int? DespawnSeconds,
    RecycleYield? Recycle, CraftRecipe? Craft, ResearchCost? Research,
    DecayInfo? Decay,        // NEW — null when the item does not decay / unknown
    UpkeepCost? Upkeep);     // NEW — null when the item has no upkeep cost

/// <summary>Decay timing for an item/deployable/building block. All fields nullable; a field is
/// populated only when RustLabs provides it. Seconds is the base/default decay.</summary>
public sealed record DecayInfo(
    int? Seconds, int? OutsideSeconds, int? InsideSeconds, int? UnderwaterSeconds, int? Hp);

/// <summary>The per-24h upkeep cost to maintain a building block.</summary>
public sealed record UpkeepCost(IReadOnlyList<UpkeepEntry> Entries);

/// <summary>One upkeep resource cost. Min == Max for a single (non-range) quantity.</summary>
public sealed record UpkeepEntry(int ItemId, int QuantityMin, int QuantityMax);
```

`DatasetSources` gains `DecayAsOf` and `UpkeepAsOf` (both `DateOnly`). `SchemaVersion` is
incremented; `EmbeddedItemDatabase` already rejects a bundle whose version does not match.

**Decided fork — upkeep range:** the source quantity is a string, either a single number (`"1"`)
or an en-dash range (`"8–25"`, U+2013). It is parsed into `QuantityMin`/`QuantityMax` at generation
time (single value → `Min == Max`). The structured form matches the recycle/craft ethos and
future-proofs the deferred 4c TC calc. The generator **fails loud** on any unparseable quantity
(it does not silently drop or stringify).

**Decided fork — decay variants:** all four environment fields + HP are modelled; the formatter
prints only the non-null ones. Most items carry only `Seconds`; some building blocks/deployables
carry the inside/outside/underwater variants.

### 3.2 Source data

- `~/Dev/rustplusplus/src/staticFiles/rustlabsDecayData.json` — `{ "items": { "<id>": { decay,
  decayString, decayOutside, decayInside, decayUnderwater, hp, hpString } } }` (the `*String` and
  `*Outside/Inside/Underwater` fields may be null).
- `~/Dev/rustplusplus/src/staticFiles/rustlabsUpkeepData.json` — `{ "items": { "<id>": [ { id:
  "<resourceId>", quantity: "<n | a–b>" } ] } }`.

Both are wrapped in an `"items"` object (note: different from the flat 6a recycle/craft/research
files). The loader reads `root.items`, not `root`.

### 3.3 Dependency direction

Unchanged from 6a. `Features.ItemData` stays a pure leaf (refs only Abstractions + Localization).
`Features.Commands` already references it. No new project references.

---

## 4. Command surfaces (`Features.Commands`)

### 4.1 In-game `!commands`

- `Handlers/DecayCommandHandler.cs`, `Handlers/UpkeepCommandHandler.cs` — `internal sealed
  ICommandHandler`, `AddScoped` in `AddCommands`. The in-game registration-count test bumps by 2.
- Same outcome switch as 6a handlers: `Found {Decay/Upkeep: not null}` → ok line; `Found` → none
  line; `Ambiguous` → `command.item.ambiguous`; else → `command.item.notfound`.

### 4.2 Slash commands

- Two `[SlashCommand]`s added to the existing `ItemCommandModule` (`/decay`, `/upkeep`), ephemeral,
  same `RespondForAsync` scope/lookup helper.
- **Footer refinement:** the helper currently hardcodes `db.Sources.NamesAsOf`. Generalise it so
  each command shows its own section date (`/decay` → `DecayAsOf`, `/upkeep` → `UpkeepAsOf`,
  existing four unchanged). Smallest change: pass the chosen `DateOnly` through the callback.

### 4.3 Formatters (`Features.Commands/Formatting`)

- `DecayLine.cs` — pure; renders base decay + any present variant + HP, using
  `DurationFormat.Compact` for the seconds.
- `UpkeepLine.cs` — pure; resolves each `UpkeepEntry.ItemId` via `IItemNameResolver`, renders
  `"<min>–<max> <name>"` when `Min != Max`, else `"<qty> <name>"`, joined.

### 4.4 `/help`

- 2 new rows under the existing `ItemDb` `CommandGroup`; the drift-guard golden handler list and the
  `CommandRegistrationTests` count tripwire move together with the 2 new handlers.

---

## 5. The generator tool (`tools/RustPlusBot.ItemData.Generator`)

- `IRustLabsSource` gains `LoadDecay()` → `IReadOnlyDictionary<int, DecayInfo>` and `LoadUpkeep()`
  → `IReadOnlyDictionary<int, UpkeepCost>`; `OfflineRustLabsSource` implements both (reading the
  two new files via the existing `JsonDocument` pattern; `int.TryParse` ids; the upkeep quantity
  parser handles `"n"` and `"a–b"`/`"a-b"`).
- `Program.cs` inlines decay/upkeep onto each `ItemRecord` by id (an id with no item record is not
  attached, same as 6a recycle/craft) and stamps `DecayAsOf`/`UpkeepAsOf`.
- `DatasetValidator`: every `UpkeepEntry.ItemId` resolves to a known item (mirrors the
  recycle/craft id checks); decay `Seconds`/`Hp` non-negative; upkeep `Min <= Max`. Loud failure
  leaves the good bundle untouched.
- Regenerate the embedded `item-data.json`. **The 6a tests must stay green against the regenerated
  bundle** (the authoritative names/ids/recycle/craft/research must not regress).

---

## 6. Error handling

- Unknown / empty / whitespace query → `command.item.notfound` / usage hint (reuse 6a behaviour).
- Item found but no decay/upkeep → `command.decay.none` / `command.upkeep.none` (`"<name> has no …"`).
- Generator: unparseable upkeep quantity, negative decay, or unresolved upkeep id → validation
  failure, non-zero exit, bundle not overwritten.

---

## 7. Testing

- Pure formatter tests: `DecayLine` (base only / with variants / with HP), `UpkeepLine` (single qty
  / range qty / multi-resource), EN+FR.
- Handler tests: Found-with-data / Found-without-data / Ambiguous / NotFound, EN+FR, via
  `ResxLocalizer` + NSubstitute (the 6a handler-test harness).
- Generator: `OfflineRustLabsSource` decay/upkeep loaders (incl. range parsing + `"items"` wrapper);
  `DatasetValidator` new rules (unresolved upkeep id, bad range, negative decay).
- Lookup is unaffected (no test changes beyond the bundle regeneration check).
- Run the FULL suite reading per-assembly counts (a fake missing a new member silently drops tests).

---

## 8. Integration touchpoints (verify during build, don't assume)

- Exact `ItemRecord` positional shape + every existing construction site (the record gains 2 params —
  all callers/tests/the generator must pass them).
- `DatasetSources` construction sites (generator + any test fixtures) gain 2 dates.
- `SchemaVersion` constant location + the loader's rejection test (update the expected version).
- `ItemCommandModule.RespondForAsync` signature change for the per-section footer date.
- In-game registration-count test + `/help` golden handler list + `CommandRegistrationTests` count.
- Confirm building-block item ids (foundation/wall/etc.) exist in the names source so decay/upkeep
  actually attach; note any orphan upkeep/decay entries during generation.
- Confirm the upkeep **period label** against RustLabs before writing the formatter string (the
  source gives only a resource + quantity; "per 24h" is the conventional reading but unverified —
  the `command.upkeep.ok` resx wording must match whatever the data actually represents).

## 9. Gates (per project conventions)

- Solution is `RustPlusBot.slnx`; build `0/0 -warnaserror`.
- `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` zero-diff (hard CI gate).
- Full test suite green, per-assembly counts read (baseline 586 + new).
- No EF drift (no Migrations/ModelSnapshot/DbContext/entity changes on the branch).

## 10. Decisions locked in brainstorming

- Scope = **decay + upkeep only**; durability / smelting / cctv each deferred to their own slice.
- 6b = **dataset + per-item calculators only**; the 4c `#storagemonitors` TC-upkeep summary is a
  separate later slice (6b only makes the data available).
- Upkeep quantity = **structured `QuantityMin`/`QuantityMax`** (parse the range; fail loud), not a
  display string.
- Decay = **model all four environment fields + HP**; formatter prints only the non-null ones.
- No new entities/migrations/events/options/background services (static read-only, no EF drift).
