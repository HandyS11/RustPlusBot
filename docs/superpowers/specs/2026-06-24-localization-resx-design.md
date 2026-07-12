# Localization → .resx consolidation + duplication quick-wins

**Date:** 2026-06-24
**Branch:** `feat/localization-resx` (off `develop`)
**Goal:** Cut SonarQube duplication (4.4% / 754 lines) by replacing the 6 hand-written
C# dictionary localization catalogs with a single shared `.resx` resource set, then take
low-risk quick-wins on the remaining non-localization duplication. Make adding a new
language a one-file drop with zero code.

## Problem (grounded in live SonarQube data)

Duplication: **4.4% = 754 lines across 20 files**. Code smells: **6** (already low after the
`.editorconfig` hardening). Breakdown of the 754 duplicated lines:

| Cluster | Dup lines | Share |
|---|---|---|
| Localization catalogs (6 files) | ~385 | ~51% |
| Device feature symmetry (Switch/Alarm modules, renderers, pairing, relays) | ~250 | ~33% |
| Hosted-service + command-handler boilerplate | ~120 | ~16% |

**Why the catalogs are flagged:** each `*LocalizationCatalog.cs` has an `["en"] = { ... }`
block and an `["fr"] = { ... }` block with an **identical key sequence**, differing only in
values. Sonar sees the FR block as a near-copy of the EN block. `CommandLocalizationCatalog.cs`
is 186 lines, 94% duplicated, purely from this EN-twins-FR structure. A `.resx` model removes the
twinning because each language is its own file with no repeated C# dictionary syntax.

## Current shape (what exists today)

- Shared `RustPlusBot.Localization` project: `ILocalizer` (Get(key,culture) + Get(key,culture,args))
  and `DictionaryLocalizer` (dictionary-backed, EN fallback, culture normalization, per-culture
  format provider).
- Each feature duplicates the stack:
  - `*LocalizationCatalog.cs` — the EN/FR dictionary data (Workspace, Commands, Players, Events,
    Switches, Alarms).
  - `*Localizer.cs` — a 2-line subclass of `DictionaryLocalizer`.
  - `I*Localizer.cs` — an empty marker interface extending the shared `ILocalizer`
    (Workspace aliases it as `Localization.ILocalizer`).
- Runtime culture is a **per-call string parameter** (`context.Culture`), because one bot instance
  serves many Discord servers each with their own language. This rules out satellite-assembly
  `Thread.CurrentCulture` resolution; culture must be passed per call.

### Verified facts driving the design

- **195 distinct keys** across all 6 catalogs, **globally unique** (no cross-catalog collisions) →
  safe to merge into one flat resx with no renaming.
- The `Get(key, culture, args)` format overload is used at **132 call sites**; per-culture format
  provider behavior must be preserved.
- `Directory.Build.props`: `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`,
  `GenerateDocumentationFile=true`, SonarAnalyzer + NetAnalyzers on. No `InvariantGlobalization`,
  so real `CultureInfo.GetCultureInfo("fr")` works.
- Keys contain `.` (e.g. `switch.status.on`) → not legal C# identifiers, so the generated
  strongly-typed `.Designer.cs` accessor is unusable. We call `ResourceManager.GetString(key, culture)`
  with the literal dotted key string → **no call-site key strings change**.

## Decisions (locked with user)

1. **Scope:** Localization rework + low-risk non-localization quick-wins. Do **not** force-merge
   genuinely-separate device features (Switch/Alarm symmetry) just to satisfy the metric.
2. **Storage/access model:** Central `.resx` with **per-call culture** via `ResourceManager.GetString`.
3. **Interfaces:** Collapse all 6 `I*Localizer` + `*Localizer` into the single shared `ILocalizer`.
4. **Resx layout:** One flat set — `Strings.resx` (en, neutral/fallback) + `Strings.fr.resx` —
   all 195 keys, prefix-namespaced.

## Target architecture

### `RustPlusBot.Localization` (shared project)

```
Strings.resx        <- 195 en keys (neutral culture, fallback)
Strings.fr.resx     <- 195 fr keys
Strings.<c>.resx    <- adding a language = 1 file, zero code
ResxLocalizer : ILocalizer
ILocalizer          <- unchanged contract (kept)
```

`ResxLocalizer` wraps a single `ResourceManager` and preserves the exact current contract:

- `Get(key, culture)`:
  - normalize culture (`"fr-FR"`→`"fr"`, blank→`"en"`);
  - `ResourceManager.GetString(key, CultureInfo.GetCultureInfo(normalized))`;
  - `ResourceManager` already walks `fr → neutral(en)` fallback automatically;
  - if still null (missing key), return the key itself (unchanged behavior).
  - On `CultureNotFoundException`, fall back to invariant/en (mirrors current `ResolveFormatProvider`).
- `Get(key, culture, args)`: resolve format via `Get(key, culture)`, then
  `string.Format(cultureFormatProvider, format, args)` — preserves per-culture formatting.

`.resx` MSBuild: the neutral `Strings.resx` generates a `ResourceManager`; set
`<EmbeddedResource Update="Strings.resx"><Generator>...</Generator></EmbeddedResource>` only if a
typed accessor is wanted — **not needed here** (dotted keys), so a plain `ResourceManager`
constructed against the `Strings` base name is sufficient. Mark `Strings.fr.resx` as
`DependentUpon Strings.resx`. Set `NeutralResourcesLanguage("en")` on the assembly so the neutral
resx is treated as English explicitly.

### DI

Replace the 6 per-feature registrations with a single registration in the shared project,
exposed as an `AddRustPlusBotLocalization(this IServiceCollection)` extension:
`services.AddSingleton<ILocalizer, ResxLocalizer>()`. Each feature's
`*ServiceCollectionExtensions` drops its catalog + localizer registration and (if not already)
relies on the shared registration being present once at host composition.

### Deletions

- 6 × `*LocalizationCatalog.cs`
- 6 × `*Localizer.cs`
- 6 × `I*Localizer.cs` (+ Workspace `ILocalizer` alias)
- `DictionaryLocalizer.cs`: **keep only if** still referenced after migration (tests may use it as a
  trivial in-memory `ILocalizer`); otherwise delete. Decide during implementation by reference count.

### Call-site changes (mechanical)

- ~44 injection sites: `ISwitchLocalizer`/`ICommandLocalizer`/`IPlayerLocalizer`/`IEventLocalizer`/
  `IAlarmLocalizer`/Workspace `ILocalizer` → shared `RustPlusBot.Localization.ILocalizer`.
  Key strings and `.Get(...)` calls are untouched.
- `using` directives updated to `RustPlusBot.Localization`.

### Tests

- Replace 6 × `*LocalizationCatalogTests` + `DictionaryLocalizerTests` with:
  - One `ResxLocalizerTests`: fallback to en for unknown culture, key-returned-when-missing,
    format-overload uses correct culture provider.
  - One `StringsResourceParityTests`: every key present in `Strings.resx` is present in
    `Strings.fr.resx` and vice-versa (coverage parity), enumerated from the `ResourceManager` /
    `.resx` directly so new keys are auto-covered.

## Quick-wins (after localization lands, separate commits)

Low-risk only:

- **Hosted services:** `CommandsHostedService` / `PlayersHostedService` (37 dup-lines each) →
  extract a shared base or helper for the common subscribe/lifecycle boilerplate.
- **Event command handlers:** `Cargo`/`Heli`/`Chinook` handlers (11 each) → a shared
  event-reply helper.

**Explicitly out of scope:** merging Switch/Alarm `*ComponentModule`, `*PairingCoordinator`,
`*StateRelay`, `*EmbedRenderer` symmetry. Those are separate features; abstracting them only to
satisfy the metric would add coupling. Leave for a future, intentional shared-device-UX design.

## Verification

- `dotnet build` clean (warnings = errors).
- `dotnet test` all green (520+ tests).
- `dotnet jb cleanupcode --profile=ReformatAndReorder` → zero diff (hard CI gate).
- Behavioral equivalence: for a sampled set of keys in both `en` and `fr`, assert `ResxLocalizer`
  output is byte-identical to the old catalog values (one-off check; can be a temporary test).
- Re-pull SonarQube duplication after PR analysis; expect localization cluster (~385 lines / ~51%)
  to drop to ~0, plus quick-win reductions.

## Risks / landmines

- **`.resx` value fidelity:** the strings contain emoji (⚡⭘⚠️), accented French, and `{0}`
  placeholders. `.resx` XML must preserve these exactly (UTF-8, `xml:space="preserve"`). Generate
  the resx from the existing catalog values programmatically to avoid transcription errors.
- **Neutral-culture fallback:** ensure `NeutralResourcesLanguage("en")` so `GetString(key, fr)`
  falls back to the neutral resx (en) — otherwise a missing fr key returns null and we'd return the
  key. This matches current "fall back to en, then key" semantics.
- **DI ordering:** the single registration must be added exactly once at host composition; verify no
  feature double-registers and nothing depends on a feature-typed interface after the rename.
- **`TreatWarningsAsErrors` + XML docs:** new public types need XML doc comments or the build fails.
- **jb cleanupcode gate:** run it before pushing; resx-adjacent csproj edits and the rename sweep
  must leave zero formatting diff.
