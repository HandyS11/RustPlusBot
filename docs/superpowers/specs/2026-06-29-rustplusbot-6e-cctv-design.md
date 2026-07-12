# Subsystem 6e — Item Database Calculators pt.5 (CCTV)

**Status:** Approved (brainstorming) · **Date:** 2026-06-29 · **Branch:** `feat/item-database-5` off `develop`
**Predecessor:** 6d (PR #31, `0458284`) — Smelting calculator (`/smelt` + in-game, schema v4).

---

## 1. Summary

6e is the fifth and **final** slice of the Item Database subsystem. It ships the **CCTV** lookup:
given a **monument**, it lists every Computer Station camera **code** for that monument, plus a note
when the codes carry a per-map numerical wildcard. Surfaces are the usual pair: Discord
`/cctv <monument>` and in-game `!cctv <monument>`.

Unlike every prior 6x slice, CCTV is **not item-keyed** — there are no item ids involved at all. It
reuses 6c/6d's **structural departure**: the data lives in its own top-level **`Cctv`** table on
`ItemDataset`, resolved by name, exactly mirroring 6c's `RaidTargets` and 6d's `Smelters`. `ItemRecord`
is untouched.

It follows the established 6a–6d shape — extend the bundle, bump the schema, add a generator source +
validator rules, regenerate the embedded bundle, add an in-game handler + a slash command + a pure
formatter + EN/FR strings — with one new wrinkle: the `/cctv` slash command uses a **fixed `[Choice]`
dropdown** of the monuments (the first slash command in the codebase to do so), guarded against
drift from the generated data by a test.

The upstream source (`cctv.json`) is **2.3 KB / 11 monuments** — tiny, transformed offline, no
trimming. No new entities, migrations, events, options, or background services (static read-only data
— zero EF drift). This slice **closes subsystem 6**.

---

## 2. Scope

### In scope (6e)

- New `CctvMonument` record on `ItemDataset` (a new top-level `Cctv` list; `ItemRecord` untouched).
- `SchemaVersion` bump **v4 → v5**; `DatasetSources` gains `CctvAsOf`.
- Generator: an `ICctvSource` (+ offline impl) reads `cctv.json`, un-escapes the markdown-escaped
  asterisks, and projects each entry into `Cctv`; `Program.cs` wires it; `DatasetValidator` gains
  CCTV checks; the embedded `item-data.json` is regenerated.
- Lookup: the generalized name-matching core (from 6c) feeds `ResolveCctv(query) → CctvMatch` on
  `IItemDatabase`.
- In-game `!cctv` (fuzzy resolve); Discord `/cctv` (fixed dropdown); `/help` ItemDb group gains 2 rows.
- A **drift-guard test** asserting the slash dropdown choices equal the dataset's monument set.
- EN/FR resx keys; full TDD coverage.

### Deferred / out of scope

- **Localized monument names** — monument names are game data and stay English in replies, exactly
  like item names today (the i18n charter localizes bot UI text, not game data).
- **Computer Station pairing / camera stills / PTZ** — that is **subsystem 5 (Cameras)**, a separate
  live feature; 6e is the static codes lookup only.
- **Map-marker cross-reference** (linking a monument's CCTV to its map position) — out of scope.
- **Live-scrape source** — the offline-transform seam stays as in 6a–6d.

### Explicit non-goals

- No new Discord channel, entity, migration, option, or hosted service.
- No item-id linkage (CCTV carries no item ids).

---

## 3. Architecture

### 3.1 Schema (`Features.ItemData/Data/ItemDataset.cs`)

`ItemDataset` gains a sixth member and a provenance date; the schema version bumps to **5**.

```csharp
public sealed record ItemDataset(
    int SchemaVersion,
    DatasetSources Sources,
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<RaidTarget> RaidTargets,
    IReadOnlyList<Smelter> Smelters,
    IReadOnlyList<CctvMonument> Cctv);                // NEW

public sealed record DatasetSources(
    DateOnly NamesAsOf, DateOnly RecycleAsOf, DateOnly CraftAsOf,
    DateOnly ResearchAsOf, DateOnly DecayAsOf, DateOnly UpkeepAsOf,
    DateOnly DurabilityAsOf, DateOnly SmeltingAsOf,
    DateOnly CctvAsOf);                               // NEW

/// <summary>One monument and the CCTV codes for its Computer Station cameras.</summary>
public sealed record CctvMonument(
    string Name,                          // display name ("Small Oil Rig", "Underwater Labs", …)
    IReadOnlyList<string> Codes,          // ["OILRIG1HELI", …] — clean, literal asterisks for wildcards
    bool Dynamic);                        // true ⇒ codes contain a per-map numerical wildcard
```

No item ids are involved, so — unlike `RaidTarget` — there is no `Kind` enum and no `Key`/id field;
the monument `Name` is the resolution key. `ItemRecord`, `DecayInfo`, `UpkeepCost`, `RaidTarget`,
`Smelter`, etc. are **unchanged**.

### 3.2 Source data

`cctv.json` — a top-level object keyed by **monument display name** (11 keys), each mapping to a
codes list and a `dynamic` flag:

```jsonc
"Small Oil Rig":  { "codes": ["OILRIG1HELI", "OILRIG1DOCK", …], "dynamic": false },
"Underwater Labs":{ "codes": ["AUXPOWER\\*\\*\\*\\*", "BRIG\\*\\*\\*\\*", …], "dynamic": true }
```

The 11 monuments: Abandoned Military Base (dynamic), Airfield, Bandit Camp, Dome, Large Oil Rig,
Missile Silo, Outpost, Small Oil Rig, Underwater Labs (dynamic), Cargo Ship, Ferry Terminal.

**Asterisk handling:** the JSON stores codes markdown-escaped for Discord (`"COMPOUND\\*\\*\\*\\*\\*\\*"`,
i.e. the literal string `COMPOUND\*\*\*\*\*\*`). The generator **un-escapes** them to the clean game
form `COMPOUND******`. The asterisks mark the per-map numerical wildcard the player must fill in;
their count is preserved (it tells the player how many digits). Source order of codes is preserved.

The `dynamic` flag is carried straight; it is `true` only for the two monuments whose codes contain
wildcards (Abandoned Military Base, Underwater Labs).

### 3.3 Dependency direction

`Features.ItemData` (leaf) owns the schema, the bundle, and `IItemDatabase`. `Features.Commands`
depends on it (handler + module + formatter). The generator depends on `Features.ItemData` for the
schema types only — exactly as in 6a–6d.

---

## 4. Command surfaces (`Features.Commands`)

### 4.1 In-game `!cctv`

`CctvCommandHandler(IItemDatabase database, ILocalizer localizer)` — `Name => "cctv"`. **No
`IItemNameResolver`** is injected (CCTV carries no item ids — a 2-arg shape, simpler than
`SmeltCommandHandler`'s 3-arg). Joins `context.Args` to a query, calls `database.ResolveCctv(query)`,
and switches:

```text
CctvMatch.Found f    → body = localizer.Get("command.cctv.ok", culture, CctvLine.Format(f.Monument));
                       f.Monument.Dynamic ? body + "\n\n" + localizer.Get("command.cctv.note", culture) : body
CctvMatch.Ambiguous  → localizer.Get("command.item.ambiguous", culture, candidate names)   // reuse 6a key
_ (NotFound)         → localizer.Get("command.item.notfound",  culture, query)             // reuse 6a key
```

> **As-built note (key name):** the note is a standalone `command.cctv.note` key appended in C# (not a
> baked-in `command.cctv.dynamic` template), so the slash path can fence only `{0}` and let the note
> ride outside the fence. `command.cctv.ok` stays the `{0}` passthrough.

A `CctvMonument` always has ≥1 code (validator-enforced), so there is no "found but empty" branch.
In-game passes the **raw** code list (plain team chat — no markdown). Max list is 13 codes, within
what `!smelt` (20 rows) and `!durability` already send unchunked.

### 4.2 Slash `/cctv`

Added to `ItemCommandModule` via a **parallel** `RespondForCctvAsync` helper (the 6c/6d precedent for
a non-item resolve). Same guild guard, scoped DI, culture lookup, ephemeral embed, and
`data as of <CctvAsOf>` footer.

Unlike the other eight slash commands (free-text `string`), `/cctv` uses a **fixed `[Choice]`
dropdown** of the 11 monuments (a closed set — best UX, matches rustplusplus). Choice **values are
English** monument names, consistent with the codebase's English-only slash metadata (names,
descriptions, choices are all registered in English; only *responses* are localized).

```csharp
[SlashCommand("cctv", "Show the CCTV camera codes for a monument")]
public Task CctvAsync(
    [Summary("monument", "The monument to look up")]
    [Choice("Abandoned Military Base", "Abandoned Military Base")]
    [Choice("Airfield", "Airfield")]
    // … one [Choice] per monument (all 11) …
    string monument) => RespondForCctvAsync(monument);
```

Because the dropdown is hard-coded but the data is generated, the slash value still goes through
`ResolveCctv` (it will be an exact-name `Found`), and the **drift-guard test** (§6) keeps the two in
lock-step.

The Discord embed wraps the codes in a **fenced code block** so wildcard asterisks render literally
(no markdown italics) and codes are copy-clean; the dynamic note (when present) follows the block as
prose. This is the one presentational difference from in-game and is handled at the surface, not in
the shared formatter.

### 4.3 Formatter (`Features.Commands/Formatting/CctvLine.cs`)

Pure static `Format(CctvMonument monument) → string`. Header `"<Name> CCTV:"` then one code per line,
source order — mirroring `SmeltLine`'s `"<Name>:\n…"` shape so the reply is **self-describing** (in
particular, the in-game fuzzy path echoes the *matched* monument, which may differ from what was
typed):

```text
Small Oil Rig CCTV:
OILRIG1HELI
OILRIG1DOCK
OILRIG1L1
OILRIG1L2
OILRIG1L3
OILRIG1L4
```

The formatter emits the header + codes only — **no dynamic note**. The localized template supplies
the note:

- `command.cctv.ok` = `"{0}"` — header + codes, nothing added.
- `command.cctv.note` = the standalone `<asterisk note>` string, appended in C# (with a blank line)
  only when `monument.Dynamic`. (Earlier draft folded this into a `command.cctv.dynamic` template;
  the as-built uses a separate key so the Discord fence can wrap only `{0}`.)

The surface picks the template by `monument.Dynamic` and passes `CctvLine.Format(monument)` as `{0}`.
The **in-game** handler passes it raw. The **slash** module passes it **wrapped in a fenced code
block**, so the fence surrounds only the header + codes (`{0}`) and the dynamic note lands *outside*
the fence as prose — one shared template pair, both surfaces, no separate note key. The asterisk
note text:

- EN: `*'s mean a numerical code that differs on every map.`
- FR: `* signifie que vous avez besoin d'un code numérique différent pour chaque carte` (from upstream).

### 4.4 `/help`

`CommandHelpCatalog` gains 2 ItemDb rows (in-game `!cctv`, slash `/cctv`), covered by the existing
drift-guard test (every registered command appears in the catalog).

---

## 5. Lookup (`Features.ItemData/Lookup`)

The generalized `NameMatcher` (extracted in 6c) is reused over the monument names. `IItemDatabase`
gains:

```csharp
CctvMatch ResolveCctv(string query);
```

returning a discriminated union mirroring `SmeltMatch`/`RaidMatch`:

```csharp
public abstract record CctvMatch
{
    public sealed record Found(CctvMonument Monument) : CctvMatch;
    public sealed record Ambiguous(IReadOnlyList<CctvMonument> Candidates) : CctvMatch;
    public sealed record NotFound : CctvMatch;
}
```

`EmbeddedItemDatabase` already owns the deserialized dataset, so it builds the monument name index
once and answers `ResolveCctv`. A new `CctvLookup` wraps the matcher, paralleling `SmeltLookup` /
`RaidLookup`. Monuments match by display name (exact, case-insensitive, contains) via the shared
matcher; there is no id axis (no ids exist).

---

## 6. The generator tool (`tools/RustPlusBot.ItemData.Generator`)

- New `ICctvSource` + `OfflineCctvSource` reading `cctv.json` from the rustplusplus `staticFiles`
  dir. For each monument key it projects the entry into a `CctvMonument`: **un-escapes** the codes
  (`\*` → `*`), preserves code order, carries the `dynamic` flag. A monument with no codes is
  dropped and logged (the orphan-drop pattern; verified 0 such entries at the current snapshot).
- `Program.cs` wires the source, stamps `CctvAsOf = 2025-11-12` (the rustplusplus upstream
  last-change date for `cctv.json`), and includes `Cctv` in the emitted dataset; schema → **5**.
- `DatasetValidator` gains: monument count ≥ floor (`MinCctvCount`, e.g. 8); every monument name
  non-empty; every monument has ≥1 code; every code non-empty (after un-escaping). On any failure:
  print to stderr, exit non-zero, **do not write** (never clobber a good bundle).
- The bundle (`item-data.json`) is regenerated — the delta is tiny (CCTV is a ~2 KB source). README
  source-file table + provenance section updated (`cctv.json` row, `CctvAsOf` note).

---

## 7. Error handling

- **Bundle**: schema-version mismatch already hard-fails the loader; the v5 bump forces a regenerate.
  A malformed source fails generator validation, not the bot.
- **Lookup**: unknown monument → `NotFound` → friendly "not found" reply (reused 6a key). Ambiguous →
  candidate list (reused 6a key). No exceptions on user input. (The slash path can only send a valid
  choice, so it always resolves `Found`; the in-game path is the only one that can miss.)
- **Formatter**: a monument always has ≥1 code, so the formatter never produces an empty body; the
  handler has no empty-list path. The dynamic note is driven solely by the `Dynamic` flag.

---

## 8. Testing (TDD)

- **Generator**: per-monument projection (name, codes, `dynamic` carried); asterisk un-escape
  (`\*` → `*`, count preserved); code order preserved; empty-codes monument dropped; loud-fail on
  validator floor, empty-name, and empty-codes; no-write-on-failure.
- **Lookup**: exact name, case-insensitive, contains, ambiguous, not-found over monument names;
  shared-matcher parity with `SmeltLookup`/`RaidLookup`.
- **Formatter**: `"<Name> CCTV:"` header + code-order preservation, no note in the formatter; dynamic
  vs non-dynamic template selection produces the note only when `Dynamic`.
- **Handler + module**: Found / Ambiguous / NotFound switch; guild guard on the slash path; fenced
  block on the Discord path; raw list in-game.
- **Drift guard (new)**: the `/cctv` `[Choice]` set equals the dataset's monument-name set, **both
  directions** (every choice resolves to a monument; every monument has a choice) — catches the gap
  rustplusplus has (its `cctv.json` lists *Airfield* but its slash command omits it).
- **Bundle smoke**: a known monument (e.g. "Small Oil Rig") resolves and reports `OILRIG1HELI`; a
  dynamic monument (e.g. "Underwater Labs") carries `Dynamic = true`; schema version is 5; `Cctv`
  count ≥ floor.

Mirrors the 6d test footprint; suite stays green, format + analyzers clean.

## 9. Integration touchpoints (verify during build, don't assume)

- `IItemDatabase` surface + `EmbeddedItemDatabase` deserialization (new `Cctv` member must
  round-trip).
- `ItemCommandModule` DI scope; `RespondForCctvAsync` needs **no** `IItemNameResolver`.
- `CommandServiceCollectionExtensions.AddCommands` — register `CctvCommandHandler`.
- `CommandHelpCatalog` drift-guard test (new rows).
- `Strings.resx` / `Strings.fr.resx` shared catalog (new `command.cctv.ok` / `command.cctv.note`).
- Generator README + `DatasetSources` constructor arity (compile-time fan-out across call sites).
- Discord.Net `[Choice]` attribute mechanics on a `string` slash parameter (registration shape).

## 10. Gates (per project conventions)

- `RustPlusBot.slnx` builds; `dotnet jb cleanupcode --profile=ReformatAndReorder` produces no diff;
  full test suite green; bundle regenerated and committed.

## 11. Decisions locked in brainstorming

- **Scope**: CCTV **this slice** — closes subsystem 6. Camera stills/PTZ remain subsystem 5.
- **Data home**: **extend the bundle** (schema v4 → v5), dedicated `Cctv` table resolved by name;
  `ItemRecord` untouched. Not a separate embedded resource.
- **Schema**: no `Kind` enum, no id/`Key` (CCTV has no item ids); `Name` is the resolution key.
- **Slash input**: **fixed `[Choice]` dropdown** of all 11 monuments (incl. Airfield), English
  values; in-game `!cctv` uses the fuzzy `NameMatcher`. Reconciled by a **drift-guard test**.
- **Asterisks**: store codes **clean** (un-escaped, wildcard count preserved); Discord embed wraps
  them in a **fenced code block**; in-game sends them raw. Dynamic note via a second resx template.
- **Monument names**: **English** in replies (game data, like item names) — not localized.
- **Command name** `/cctv` + `!cctv`; internal types named `Cctv*` / `CctvMonument`.
