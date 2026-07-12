# Admin debug/utility commands — design

**Date:** 2026-07-01
**Status:** Approved (design)
**Branch (proposed):** `feat/admin-utility-commands` off `develop`

## Goal

Add a small set of operator-facing admin/debug/utility slash commands to RustPlusBot:
quick diagnostics (`/ping`, `/status`) and destructive maintenance (rebuild/purge a
guild's workspace, wipe the whole database). Fill the gaps identified below without
duplicating what the existing `/setup` and `/workspace reset` commands already do.

## Existing surface (inventory)

Utility/info (public): `/help`, `/uptime` (process uptime).
Admin (`ManageGuild`): `/setup` (provision this guild's channels), `/leader`.
Dangerous admin (`ManageGuild` + `EnableDangerCommands` flag): `/workspace reset`
(delete all bot channels + records **for this guild**), `/workspace simulate-server` (dev).

Gaps this work fills:

- No `/ping` (gateway latency) — `/uptime` is the nearest and reports process uptime only.
- No `/status` / health snapshot.
- No lighter "repair/refresh channels" — only the full teardown (`/workspace reset`) + `/setup`.
- No database-level reset (whole-DB) and no guild-data purge deeper than the channel teardown.

## Decisions (from brainstorming)

- **Command set:** all four requested — `/ping`, `/status`, channel reset (both repair
  **and** rebuild), and database reset (both a per-guild purge **and** a whole-DB reset).
- **Authorization:** reuse the existing model — `[RequireUserPermission(ManageGuild)]`
  plus the `EnableDangerCommands` flag for destructive commands. **No** new bot-owner concept.
- **DB reset scope:** two separate commands — a per-guild data purge and a whole-DB reset.
- **Channel reset:** two options — non-destructive repair and a destructive full rebuild.
- **Grouping:** diagnostics in their own module; guild-scoped ops under the existing
  `/workspace` group; the global DB reset under a new `/admin` group (home matches blast radius).
- **Localization:** `/ping` and `/status` use **plain English** (operator-facing, like the
  existing admin/dev commands). Destructive commands are plain English too, consistent with
  the current `/workspace reset` / `/setup`.
- **DB reset semantics:** **clear all rows, keep schema** (FK-safe order, in a transaction),
  then advise a restart. No live `EnsureDeleted`/drop (unsafe while hosted services hold the DB).

## Commands

### 1. Diagnostics — `DiagnosticsModule` (new, `RustPlusBot.Features.Commands`)

Ungated (any user), ephemeral responses.

- **`/ping`** — replies with Discord gateway latency (`Context.Client.Latency`, ms) and a
  measured round-trip (time between defer and followup). Single line, plain English.
- **`/status`** — read-only embed assembled from:
  - process uptime — `BotUptime.Elapsed` (existing singleton),
  - gateway latency — `Context.Client.Latency`,
  - per-server Rust+ connection status + player count + last-updated — `IConnectionStore.GetStateAsync`
    over `IServerService.ListAsync(guildId)`,
  - server count (this guild) and guild count (`Context.Client.Guilds.Count`).

### 2. Workspace ops — extend `WorkspaceAdminModule` (`/workspace` group, `ManageGuild`)

- **`/workspace repair`** — non-destructive. Calls `IWorkspaceReconciler.HealGuildAsync(guildId)`
  to recreate any missing categories/channels/anchored messages without deleting existing data.
  **Not** danger-gated (parity with `/setup`); `ManageGuild` only.
- **`/workspace rebuild`** — destructive. `IWorkspaceTeardownService.ResetGuildAsync` then
  re-provision (`ReconcileGlobalAsync` + `ReconcileServerAsync` per server, i.e. the `/setup` flow).
  Danger-gated (`EnsureEnabledAsync`) + confirm **button** (matches `/workspace reset`).
- **`/workspace purge`** — destructive, deeper than `reset`. Removes this guild's Discord
  resources **and** its domain data: teardown provisioned channels, then delete the guild's
  `RustServers` (cascade removes connection states, switches, alarms, storage monitors, command
  settings, map settings, event subscriptions, paired entities, credentials) and `GuildSettings`.
  Leaves the guild as if the bot had just joined. Danger-gated + confirm button.
  Backed by a new `IGuildPurgeService`.

### 3. Global maintenance — `MaintenanceModule` (new, `RustPlusBot.Features.Commands`, `/admin` group)

`[Group("admin", ...)]`, `[RequireUserPermission(ManageGuild)]`, danger-gated.

- **`/admin reset-database`** — clears **all** rows in **every** table across **all** guilds
  (schema preserved), via a new `IDatabaseMaintenanceService.ClearAllAsync()`. Requires the
  danger flag and a **typed confirmation**: a required string argument `confirm` that must equal
  the literal `RESET` (a button is too easy for a global, unrecoverable action; a required arg is
  simpler than a modal and matches the Discord.Net idiom). On success the ephemeral response
  includes a "restart the bot for a clean in-memory state" notice.

## New units

| Unit | Project | Responsibility |
|------|---------|----------------|
| `DiagnosticsModule` | Features.Commands | `/ping`, `/status` (thin Discord I/O) |
| `MaintenanceModule` | Features.Commands | `/admin reset-database` (thin Discord I/O + typed confirm) |
| `IGuildPurgeService` / `GuildPurgeService` | Features.Workspace | Guild-scoped teardown + cascade delete of the guild's domain rows |
| `IDatabaseMaintenanceService` / `DatabaseMaintenanceService` | Persistence | `ClearAllAsync()` — delete all rows, FK-safe order, in a transaction |
| `WorkspaceAdminModule` (extend) | Features.Workspace | `/workspace repair`, `rebuild`, `purge` |

Registration: `Features.Commands` and `Features.Workspace` assemblies are already scanned by
`InteractionService`, so new modules are auto-discovered. New services are registered in the
existing `*ServiceCollectionExtensions` for their project. The `/admin` danger gate reads the
same `WorkspaceOptions.EnableDangerCommands` flag (injected where the module lives); if that
cross-project injection is awkward, promote the flag to a small shared options type — decide
during implementation, defaulting to reusing `WorkspaceOptions`.

## Authorization & confirmation

- Destructive commands: `[RequireUserPermission(GuildPermission.ManageGuild)]` at the method/group
  level **and** an `EnableDangerCommands` runtime check (reuse the existing `EnsureEnabledAsync`
  pattern; each module needs its own copy or a shared helper).
- `repair` is non-destructive → `ManageGuild` only, no danger flag.
- Diagnostics → ungated, ephemeral.
- `rebuild` / `purge` → Danger confirm button (existing pattern).
- `reset-database` → typed confirmation token (`RESET`).

## Testing

- `GuildPurgeService` (SQLite-backed `BotDbContext`): seed two guilds; purging guild A removes
  **exactly** A's servers + cascaded rows + `GuildSettings` + provisioned records, and leaves
  guild B untouched.
- `DatabaseMaintenanceService.ClearAllAsync`: seed multiple tables/guilds; after clear every
  `DbSet` is empty and the schema still exists (a subsequent insert succeeds).
- Modules stay thin (Discord I/O) — coverage-excluded per repo convention; logic lives in the
  two services, which carry the tests. Follow the repo's xUnit conventions (data-driven where useful).

## Risks & mitigations

- **Live whole-DB drop is unsafe** (hosted supervisors/map/players hold the DB). Mitigated by
  clearing rows (not dropping the file/schema) in a transaction + advising restart.
- **No owner concept (by choice):** with the danger flag on, any `ManageGuild` admin can trigger
  the global DB reset. Acceptable for a self-hosted single-operator bot (flag defaults off); the
  typed confirmation is the backstop. Documented, not gated further.
- **Purge cascade completeness:** verify the FK cascade (see `ServerRemovalCascade` migration)
  actually covers every per-server table; anything not cascaded must be deleted explicitly in
  `GuildPurgeService`. The test above is the guard.

## Out of scope

- A bot-owner/superadmin permission tier.
- Scheduling/automation of resets.
- Any migration/schema change (clear-rows keeps the schema; no new tables).
