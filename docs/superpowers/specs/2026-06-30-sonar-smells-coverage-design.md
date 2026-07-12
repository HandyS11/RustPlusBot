# Design: Improve SonarQube smells + coverage

**Date:** 2026-06-30
**Branch:** feat/sonar-smells-coverage (cut from develop)
**Status:** approved (defaults taken — see "Decisions")

## Problem

SonarQube on `develop` reports:

- **8 code smells**
- **4.5% duplication** (953 lines / 39 blocks)
- **61.2% coverage** (4591 / 7274 lines covered; 2683 uncovered)
- 0 bugs, 0 vulnerabilities

Goal: improve smells and coverage significantly, safely.

## Scope decisions (locked with user)

- **Smells:** fix all 8 → target **0**.
- **Coverage:** raise via **both** boilerplate exclusions **and** real tests → target **≥80%** (low-to-mid 80s realistic).
- **Duplication:** **skip the structural switch/alarm/storage dedup.** No behavior changes to device subsystems. Duplication moves only incidentally (≈4.5% → ≈4.0–4.3%) from the `ItemCommandModule` collapse in Part A.
- **`**/Modules/**` exclusion:** approved — exclude the whole Discord Module/Modal adapter layer from the coverage metric (still analyzed for smells/dup).

## Non-goals

- No refactor of `ComponentModule` device-subsystem UI (switch/alarm/storage) — only `ItemCommandModule` is restructured, because its inline logic is pure and its restructure also fixes smells + duplication.
- No new Sonar rules, quality-gate changes, or analyzer config changes beyond the coverage-exclusions arg.

## Part A — Eliminate all 8 code smells

| # | Rule | Location | Fix |
|---|------|----------|-----|
| 1–3 | `csharpsquid:S1192` (literal ×4) | `ItemCommandModule.cs` (`This command must be used in a server.`, `command.item.ambiguous`, `command.item.notfound`) | Extract the module's per-command logic into a new tested handler (`ItemQueryHandler` or similar) following the existing `Features.Commands/Handlers/` pattern. Collapse the four near-identical `RespondFor*Async` methods into one parameterized path so each literal/resource key appears once. Kills all 3 S1192 **and** removes this file's 45 duplicated lines. |
| 4 | `csharpsquid:S3776` (63→15) | `tools/.../Validation/DatasetValidator.cs:27` | Extract one private static method per validation pass: items-min, recycle refs, craft refs, upkeep, decay, raid targets, smelters, cctv. Each well under 15; `Validate` becomes a short aggregator. |
| 5 | `csharpsquid:S3776` (24→15) | `tools/.../Program.cs:29` | Extract orchestration / argument-handling helpers. |
| 6 | `csharpsquid:S3776` (17→15) | `tools/.../Sources/OfflineRustLabsSource.cs:26` | Extract parsing helper(s). |
| 7 | `csharpsquid:S3776` (23→15) | `tools/.../Sources/OfflineRustLabsSource.cs:76` | Extract parsing helper(s). |
| 8 | `csharpsquid:S3776` (16→15) | `src/.../Connections/Supervisor/ConnectionSupervisor.cs:574` | Single small method extraction (only 1 over threshold); production code, currently 70% covered — add a test for the extracted branch. |

All refactors preserve behavior. Each tools/ refactor is paired with tests (Part C) since the extracted methods become individually testable.

## Part B — Coverage via exclusions (mechanical, ≈+13%)

Edit only the `sonar.coverage.exclusions` argument in `.github/workflows/Sonar.yml` (currently `**/tests/**`). Add genuinely-untestable boilerplate (kept in smell/dup analysis, removed from the coverage denominator):

- `**/Program.cs` — both composition roots (Host + Generator)
- `**/Modules/**` — Discord `InteractionModule` + `*Modal` UI adapters (all ~0% today; logic lives in handlers/services)
- `**/*Options.cs` — options property bags
- `**/*ServiceCollectionExtensions.cs` — DI registration
- `**/DesignTimeDbContextFactory.cs` — EF design-time only

Estimated effect: removes ≈1,100 uncovered lines from the denominator (these carry ≈0 covered lines), lifting coverage to ≈74% before any new tests.

Final exclusions arg becomes:
`**/tests/**,**/Program.cs,**/Modules/**,**/*Options.cs,**/*ServiceCollectionExtensions.cs,**/DesignTimeDbContextFactory.cs`

(Verify each glob against the real file set during implementation; tighten any that over-match a testable class.)

## Part C — Coverage via real tests (≈+8–10%)

Write xUnit tests in the existing per-feature test projects, following established conventions (folder-per-area, mocking). Prioritized by impact:

1. **`ConnectionSupervisor`** uncovered branches (162 lines, production) — largest real win; pairs with smell #8.
2. **Hosted services** already partially tested in the codebase: Map, Events, Switches, StorageMonitors, Alarms, Chat, Commands, Players.
3. **Posters / messengers / gateways:** `DiscordChannelMessenger`, `DiscordWorkspaceGateway`, channel posters (`DiscordMapChannelPoster`, `DiscordTeamChatWebhookPoster`, `DiscordAlarmChannelPoster`, etc.).
4. **Generator** validator + sources — newly extracted methods from Part A; `Generator.Tests` project already exists (`DatasetValidatorTests`, `OfflineRustLabsSourceTests`).
5. **`ItemQueryHandler`** extracted in Part A — covers the per-command resolution logic.
6. Low-coverage command handlers: `CraftCommandHandler`, `ResearchCommandHandler`, and similar.

`RustPlusSocketSource` (247 uncovered, raw socket) is explicitly low-priority — partial coverage only if cheap; not required to hit ≥80%.

## Constraints / gotchas

- `Directory.Build.props`: `TreatWarningsAsErrors=true` + `GenerateDocumentationFile=true` — new **public** types need XML docs; keep new helpers `private`/`internal` where possible.
- Hard CI gate: `dotnet jb cleanupcode --profile=ReformatAndReorder` must produce no diff. Run format before pushing (`.githooks/pre-push` does this).
- Solution file is `RustPlusBot.slnx` (no `.sln`).
- Community-Edition Sonar analyzes only `develop` on push — metric changes appear only after merge. Locally validate with `dotnet test --collect` + the same exclusions to estimate.
- `docs/superpowers/` is gitignored — this spec is local-only, never `git add`.

## Verification

- `dotnet build` (Release) clean, no warnings.
- `dotnet test` all green; no flaky/hang regressions.
- Local coverage run (coverlet opencover) with the new exclusions estimates ≥80%.
- `dotnet jb cleanupcode` produces no diff.
- Behavior unchanged: refactors are pure extractions; existing tests still pass.

## Decisions taken (user approved "y")

- Coverage target: **≥80%**.
- Exclude the entire `**/Modules/**` layer from the coverage metric.
