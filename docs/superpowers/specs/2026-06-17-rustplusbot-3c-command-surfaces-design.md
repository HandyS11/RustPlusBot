# RustPlusBot 3c — Command Discord Surfaces — Design

**Date:** 2026-06-17
**Subsystem:** 3c (third slice of subsystem 3: chat + `!commands`)
**Predecessors:** 3a chat bridge (PR #8, `8132240`), 3b command framework (PR #9, `240a349`), 3b-ii team-intel (PR #10, `70a92c7`).
**Branch to use:** `feat/command-surfaces` off `develop`.

## Summary

3c surfaces the in-game command framework into Discord and adds three slash
commands. It ships **four** of the five surfaces the catalog tagged to 3c:

- **`/help`** — ephemeral embed listing the in-game `!commands` (grouped, with the
  server's prefix) and the slash commands; per-guild culture (EN/FR).
- **`/uptime`** — ephemeral bot-process uptime.
- **`/leader`** — transfers in-game team leadership: opens a select of live team
  members and promotes the chosen one (ManageGuild-gated).
- **`#info` team summary** — a "Online N/M · leader X" field added to the existing
  per-server `#info` embed, shown only while Connected.

**Explicitly deferred to a future 3c-ii:** the **`#commands` channel** (a
per-server Discord channel that relays typed messages into the in-game command
pipeline). It needs a new channel spec plus a Discord→in-game routing path through
the dispatcher, and a second privileged-intent message listener alongside
`#teamchat`; it is the heaviest piece and is cleanly separable. **`#activity`**
log remains out of subsystem 3 (3/4 in the catalog). **Per-connection uptime** is
deferred (no "connected since" is stored today).

## Decisions (locked in brainstorming)

| Topic | Decision |
| --- | --- |
| Scope this slice | `/help`, `/uptime`, `/leader`, `#info` team summary. `#commands` channel → 3c-ii. |
| Where new code lives | **Approach A** — slash modules + help catalog in the existing `Features.Commands` project; `#info` edit local to Workspace's renderer. No new project. |
| `/leader` target selection | Pick a teammate from a select menu populated by `GetTeamInfoAsync`; promote via `PromoteToLeaderAsync(steamId)`. ManageGuild-gated. |
| `#info` summary content | Online/total + leader name; rendered only when `Status == Connected`; refreshed on the **existing** triggers (`ConnectionStatusChangedEvent` / reconcile) — **no new polling timer**. |
| `/help` content | In-game `!commands` (grouped, with prefix) **and** the slash commands; EN/FR. |
| `/help` metadata source | A curated **`CommandHelpCatalog`** (name + group + localized description key), unit-tested against the live `ICommandHandler` registry to catch drift. |
| Server targeting (slash) | Auto-use the single server if a guild has exactly one; if multiple → `/leader` shows a server picker first, `/help` falls back to default `!` with a one-line note. |
| Localization | Per-guild culture (EN/FR), via the existing `CommandLocalizer` (Commands surfaces) and Workspace `Localizer` (the `#info` field). |

## Architecture

### New in `Features.Commands`

- **`Modules/CommandSurfaceModule.cs`** — `InteractionModuleBase<SocketInteractionContext>`
  hosting `/help`, `/uptime`, `/leader`. Resolves scoped services through
  `IServiceScopeFactory` per interaction (the repo's module idiom, per
  `SetupModule`/`SettingsComponentModule`).
- **`Modules/LeaderComponentModule.cs`** — wildcard component module handling the
  `/leader` select callbacks. Custom-id prefixes:
  - `leader-server:` — server-pick step (only used in multi-server guilds), value = serverId.
  - `leader-promote:{serverId}` — member-pick step, value = target SteamId.
  ManageGuild is **re-checked** on the component callback (Discord does not re-gate
  components).
- **`Help/CommandHelpCatalog.cs`** — a curated manifest: ordered entries of
  `(string Name, CommandGroup Group, string DescriptionKey)`. `CommandGroup` is an
  enum `Control | Server | TeamIntel | Bot`. Plus a small `HelpEmbedRenderer` (pure,
  testable) that builds the embed from `(catalog, prefix, culture, multiServerNote)`.
- **`CommandLocalizationCatalog`** gains EN/FR keys: per-command help descriptions,
  group headings, `/help` title + multi-server prefix note, `/uptime` reply,
  `/leader` not-connected / no-server / empty-team / success / failure replies.
- **`CommandServiceCollectionExtensions`** registers a new
  `InteractionModuleAssembly(thisAssembly)` singleton so `DiscordBotService`
  discovers the modules (mirrors Pairing/Connections/Workspace).

### Extended seam (Connections)

- **`IRustServerQuery.PromoteToLeaderAsync(ulong guildId, Guid serverId, ulong steamId, CancellationToken)`**
  → `Task<bool>` (true = promoted; false = no live socket or API non-success).
- **`IRustServerConnection.PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken)`**
  → `Task<bool>` (internal seam).
- **`ConnectionSupervisor`** implements the query method over `_liveSockets`
  (mirrors `GetTeamInfoAsync`: `TryGetValue` → delegate with `HeartbeatTimeout` →
  false when no socket).
- **`RustPlusSocketSource`** (the one untested integration shim) wraps
  `RustPlus.PromoteToLeaderAsync(ulong, CancellationToken)` → payload-free
  `Response` → `response.IsSuccess`. Broad-catch → false; never surface a
  token/secret. **VERIFIED against the 2.0.0-beta.1 DLL XML:**
  `M:RustPlusApi.RustPlus.PromoteToLeaderAsync(System.UInt64,System.Threading.CancellationToken)`
  returns a payload-free `RustPlusApi.Data.Response` with `.IsSuccess`.
- **`FakeRustSocketSource`** (test double) gains `PromoteToLeaderAsync` or the
  Connections suite silently drops tests (the 3a `FakeWorkspaceStore` / 3b-ii lesson:
  always run the full suite and read per-assembly counts).

### `/leader` promotion logic (testable)

The promote decision is extracted into an internal `LeaderService` (or equivalent)
in `Features.Commands`, taking `(guildId, serverId, steamId, culture)` and the
`IRustServerQuery`, returning a localized result + outcome — so the
fetch-members / promote logic is covered at the service level. The interaction
modules stay thin (untested — repo convention: `InteractionModuleBase` types are
not unit-tested here).

### Edited (Workspace)

- **`ServerInfoMessageRenderer`** — when `status == ConnectionStatus.Connected`,
  call `IRustServerQuery.GetTeamInfoAsync`. On a non-null snapshot add a field
  "Online: {online}/{total} · leader {leaderName}". `online` = members where
  `IsOnline`; `total` = member count; leader name = the member whose `SteamId ==
  LeaderSteamId`, falling back to the SteamId string if absent. On a null snapshot
  (race: status Connected but the query fails) **omit the field silently**.
  (Workspace already references `Connections` and depends on `IConnectionStore`;
  it gains an `IRustServerQuery` dependency on the renderer.)
- **Workspace `LocalizationCatalog`** gains EN/FR keys: the team field label and the
  "leader" word / `online of total` format.

### Dependency direction

`Features.Commands` already references `Connections` + `Workspace`; Workspace
already references `Connections`. Nothing new is inverted. No new project.

### Not added

No new entity, migration, event, options, or background service.

## Per-surface behavior

### `/help` (ephemeral; any guild member)

1. Resolve guild culture (`IWorkspaceStore.GetCultureAsync`).
2. Resolve target prefix: if the guild has exactly one registered server, read that
   server's prefix (`IMuteStore.GetPrefixAsync`). If multiple servers, use the
   default `!` and include a one-line note that prefixes are configured per server.
   If zero servers, use the default `!` with no note (nothing to disambiguate).
3. Render an embed via `HelpEmbedRenderer`: one group per `CommandGroup`
   (Control / Server / Team intel / Bot) each listing
   `{prefix}{name} — {localized description}`, plus a "Slash commands" group for
   `/help`, `/uptime`, `/leader`.

### `/uptime` (ephemeral)

Reports `BotUptime.Elapsed` via `DurationFormat.Compact`, localized. (No
per-connection uptime this slice.)

### `/leader` (ManageGuild; defers ephemeral)

1. Determine target server:
   - 0 servers → ephemeral "No server is set up yet."
   - 1 server → use it.
   - >1 servers → respond with a **server select** (`leader-server:`); on pick,
     proceed as if that server were chosen.
2. For the chosen server, call `IRustServerQuery.GetTeamInfoAsync`.
   - No live socket → ephemeral "Not connected to this server."
   - Empty team → ephemeral "No team members to promote."
3. Respond with a **member select** (`leader-promote:{serverId}`): options labeled by
   member name, value = SteamId, capped at `SelectMenuBuilder.MaxOptionCount` (25);
   the current leader is shown marked as default.
4. `LeaderComponentModule` handles the member pick → re-check ManageGuild →
   `IRustServerQuery.PromoteToLeaderAsync(guild, server, steamId)`:
   - true → ephemeral success (member name).
   - false → ephemeral "Couldn't promote — the server may be unreachable."

### `#info` team summary

In `ServerInfoMessageRenderer`, when `Status == Connected`, fetch `GetTeamInfoAsync`
and add the team field as described above; omit on null. Refreshed by the existing
`#info` triggers only.

## Error handling & edge cases

- **No live socket** (`/leader`, `#info` field): query returns null / false →
  `/leader` ephemeral "Not connected"; `#info` omits the field.
- **No registered servers** (`/leader`): ephemeral "No server is set up yet."
- **Empty team** (`/leader`): "No team members to promote."
- **Promote fails** (socket dropped between select and click, or API non-success):
  ephemeral "Couldn't promote — the server may be unreachable."
- **Stale component interaction**: re-resolve live data on the callback; if the
  socket is gone, the same not-connected path applies. ManageGuild re-checked.
- **Secrets:** no tokens in any reply or log (existing rule; `RustPlusSocketSource`
  broad-catches to false rather than throwing with any value).
- **Fault isolation:** module exceptions are handled by the existing global Discord
  interaction error handling; nothing crashes the gateway.

## Testing

- **`CommandHelpCatalog` drift guard:** every registered `ICommandHandler.Name` has a
  catalog entry, and every entry's `DescriptionKey` resolves in both EN and FR.
- **`HelpEmbedRenderer`:** pure render from `(catalog, prefix, culture)` → assert
  grouping order, prefix substitution, the slash-commands group, and the
  multi-server note path.
- **`LeaderService`:** covers no-socket / empty-team / promote-success /
  promote-failure against a fake `IRustServerQuery`.
- **`ConnectionSupervisor.PromoteToLeaderAsync`:** tested via `FakeRustSocketSource`
  (add the new method to the fake). `RustPlusSocketSource` mapping is the one
  untested integration shim, by design.
- **`ServerInfoMessageRenderer`:** add cases for connected-with-team,
  connected-but-query-null (field omitted), and not-connected (field omitted).
- Interaction modules (`CommandSurfaceModule`, `LeaderComponentModule`) stay thin and
  untested (repo convention).

## Out of scope (this slice)

- `#commands` channel + Discord→in-game command relay → **3c-ii**.
- `#activity` log → subsystem 3/4.
- Per-connection uptime (needs a "connected since" on `ConnectionState`).
- `!afk` poller slice (separate deferred work from 3b-ii).
- Role/permission gating of commands → subsystem 9.

## Execution notes / gotchas (carry-forward)

- Run the **full** test suite and read per-assembly counts after touching shared
  seams (`IRustServerQuery`, `FakeRustSocketSource`) — a missing fake method silently
  drops a whole assembly's tests.
- NSubstitute on internal interfaces needs `DynamicProxyGenAssembly2`
  `InternalsVisibleTo` in the project under test (already present in Connections;
  add to Commands if mocking an internal there).
- `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (ReSharper) is
  the real format gate — run it (after `dotnet tool restore`) before pushing; the
  pre-push hook enforces it and it reorders members Roslynator never flags.
- `Discord.ConnectionState` collides with `Domain.Connections.ConnectionState` — alias
  if both are named in one file.
- `SelectMenuBuilder` hard-caps at 25 options — `.Take(SelectMenuBuilder.MaxOptionCount)`
  the member list (as `#info`'s swap select already does).
- Roslynator: `// TODO` comments are errors (`S1135`); use `<remarks>` instead.
