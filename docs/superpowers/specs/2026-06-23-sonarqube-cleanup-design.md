# SonarQube Code-Smell & Duplication Correction — Design

**Date:** 2026-06-23
**Branch (planned):** `feat/sonar-cleanup` off `develop`
**Project key:** `HandyS11_RustPlusBot_efd0a2f9-c22b-41c4-a6d0-c3f25a4a3178`

## Goal

Drive the 82 maintainability smells toward zero and cut overall duplication
density (currently **8.4%**, 1,551 duplicated lines across 64 blocks / 36 files)
by extracting the genuine shared abstractions behind the copy-paste families.
Sequenced low-risk → high-risk so every step keeps the 520-test suite green.

Quality gate is currently **OK** — none of this is gate-blocking; it is hygiene
and a real missing-abstraction cleanup.

## Findings baseline (2026-06-23)

### Maintainability issues — 82 total

| Bucket | Count | Severity | Nature |
|---|---|---|---|
| `IDE0028/0300/0305` collection-init can be simplified | ~45 | INFO | Trivial auto-fixable; ~half in **test** files |
| `IDE0130` namespace ≠ folder | 8 | INFO | 7 `Abstractions/Connections/*` files declare `RustPlusBot.Features.Connections.Listening`; 1 test ns |
| `S107` too many ctor/method params (8–9 > 7) | 6 | MAJOR | `AlarmStateRelay`, `EventRelay`, `EventsHostedService`, `ConnectionSupervisor`, `WorkspaceReconciler`, `MapHostedService` (9), `TeamStateTracker` (8-param method) |
| `S1192` repeated string literal | 4 | MINOR | "That control wasn't valid." ×6 (Alarm + Switch modules), "This control must be used in a server." ×4, "RustPlusBot" ×4 |
| `S3776` cognitive complexity >15 | 4 | CRITICAL | `ConnectionSupervisor:529` (16), `MapRenderer:184` (19), `ServerInfoMessageRenderer:29` (16) |
| `IDE0007/0042/0045/0060/0066/0230/0290` misc style | ~15 | INFO | var, deconstruct, if→ternary, unused param, switch-expr, UTF-8 literals, primary ctor |

### Duplication — 8.4% density, 1,551 lines, 64 blocks, 36 files

Dominant **structural** patterns (missing shared abstraction, not accidental sloppiness):

- **Localizer triplet per feature** — `XLocalizationCatalog` + `XLocalizer` +
  `IXLocalizer` repeated for Commands / Events / Players / Switches / Alarms /
  Workspace. `Localizer`/`SwitchLocalizer`/`EventLocalizer`/`PlayerLocalizer`/
  `CommandLocalizer` bodies are **byte-identical** apart from the type name; the
  `IXLocalizer` interfaces are identical apart from the doc summary. There is
  already an in-code TODO: *"Duplicated from the command/workspace localizers;
  consolidate into a shared project in a future refactor."*
- **`DiscordXChannelPoster`** family (Alarm/Event/Player/Switch), 52–75%. **Two
  distinct contracts**: ensure-by-message-id self-heal returning `ulong?`
  (Switch/Alarm) vs fire-and-forget post (Event/Player). The shared part is the
  boilerplate (channel fetch, `RequestOptions`, broad catch, `LoggerMessage`),
  not the contract.
- **`XChannelLocator`** family (Event/Map/Switch), identical 48.7% apart from one
  `WorkspaceChannelKeys.X` constant.
- **`XCommandHandler`** family (Cargo/Chinook/Heli), identical 29.7%.
- EF **migrations** (3 files, auto-generated) — analysis noise, exclude not refactor.

## Decisions (locked with user)

- **Scope:** everything as one plan (all 82 smells + all real duplication).
- **Shared localization home:** new **`RustPlusBot.Localization`** project.
- **Poster dedup:** extract **shared boilerplate only**; keep the two contracts
  distinct. Do not force a single one-size poster.

## Architecture of the fix — structural core

### Core 1 — `RustPlusBot.Localization` (new project)

Holds:

- `ILocalizer` — `Get(key, culture)` and `Get(key, culture, params object[] args)`.
- `DictionaryLocalizer` — the identical `Get` / `Normalize` /
  `ResolveFormatProvider` logic + `FallbackCulture = "en"`, constructed from an
  injected `IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>`
  (the `Strings` shape the catalogs already expose).

Each feature keeps **only** its own `XLocalizationCatalog` (the data) and
registers `new DictionaryLocalizer(catalog.Strings)` in DI. The 6 identical
`XLocalizer` bodies (~300 dup lines) and 6 identical `IXLocalizer` interfaces are
deleted. Resolves the in-code TODO.

Catalog *dictionaries* stay per-feature — legitimately different data. Their
remaining SonarQube duplication is the repeated `{ "en", new(){...} }`
scaffolding; acceptable once it is the only duplicated thing left. Not collapsed
(YAGNI).

### Core 2 — `CachingChannelLocator` (in `RustPlusBot.Features.Workspace/Locating`)

The Event/Map/Switch (and Alarm) locators are identical except one
`WorkspaceChannelKeys.X` constant. One sealed `CachingChannelLocator`
parameterized by the channel key (TTL cache + `SemaphoreSlim` refresh gate +
`(GuildId, ServerId) → channelId` map). Per-feature `IXChannelLocator` interfaces
become thin bindings over it. Collapses ~110 dup lines.

### Core 3 — `DiscordChannelMessenger` helper (in `RustPlusBot.Discord`)

Extract only the shared boilerplate the four posters share: channel fetch →
`RequestOptions` → broad `catch` (with `OperationCanceledException` rethrow) →
`LoggerMessage` logging. The two contracts stay as separate thin methods:

- `EnsureAsync(...) -> ulong?` (self-heal edit/repost by message id),
- `PostAsync(...)` (fire-and-forget).

No single unified poster type (avoids a leaky abstraction).

## Mechanical smells — the 82 issues

**A. Zero-risk auto-fixes (~60 INFO).** `IDE0028/0300/0305`, `IDE0007`,
`IDE0042`, `IDE0045`, `IDE0066`, `IDE0230`, `IDE0290`. Apply via `dotnet format`
(analyzer pass) scoped to flagged files (≈half are tests). Test suite verifies.

**B. Repeated-literal constants (`S1192`, 4 MINOR).** Extract consts:
`"That control wasn't valid."` (Alarm + Switch modules — lands in shared code
where the modules already converge), `"This control must be used in a server."`
(CredentialModule), `"RustPlusBot"` (LocalizationCatalog).

**C. Namespace/folder mismatch (`IDE0130`, 8 INFO).** Fix the 7
`Abstractions/Connections/*` namespaces to `RustPlusBot.Abstractions.Connections`
and update every consumer `using`. Cross-project blast radius → its own commit so
a compile break is isolated. The 1 test namespace is trivial.

**D. Too-many-parameters (`S107`, 6 MAJOR).** Introduce a small `record`
options/dependency-bundle per constructor, grouping cohesive collaborators rather
than blindly merging. `MapHostedService` (9) is the priciest.

**E. Cognitive complexity (`S3776`, 4 CRITICAL).** `ConnectionSupervisor:529`
(16), `MapRenderer:184` (19), `ServerInfoMessageRenderer:29` (16). Extract guard
clauses / inner loops into private methods to drop under 15. One focused commit
each, tests as safety net.

**EF migrations:** not refactored — excluded via `sonar.cpd.exclusions` so they
stop polluting the metric. Confirm `obj/` is excluded too.

## Sequencing & PR strategy

Single `feat/sonar-cleanup` branch off `develop` (no-worktrees workflow), ordered
commits, each independently reviewable and bisectable. Likely **2 PRs**:
mechanical first (cheap review), structural second.

Commit order (low-risk → high-risk):

1. `sonar.cpd.exclusions` for EF migrations + obj.
2. Zero-risk auto-fixes (2A) — single `dotnet format` sweep.
3. `S1192` literal constants (2B).
4. `IDE0130` namespace fix + consumer `using` updates (2C).
5. `RustPlusBot.Localization` extraction (Core 1) — biggest dup win.
6. `CachingChannelLocator` (Core 2).
7. `DiscordChannelMessenger` boilerplate extraction (Core 3).
8. `S107` parameter bundling (2D).
9. `S3776` complexity extraction (2E).

## Testing / verification

- `dotnet build` + full `dotnet test` (520 tests) after **each** commit; a step
  is not done until green.
- Steps 5–7 are behaviour-preserving refactors — existing tests are the contract.
  Where a step removes a type with dedicated tests (e.g. `SwitchLocalizerTests`),
  the tests **retarget the shared type** rather than being deleted.
- After the branch is complete, re-run the Sonar workflow / next analysis and
  confirm smell count and duplication density **actually dropped** — evidence
  before any "done" claim.

## Out of scope (YAGNI)

- Collapsing the per-feature catalog *data* dictionaries.
- Unifying posters into a single type.
- Any feature-behaviour change.
- Touching passing/unflagged code.
