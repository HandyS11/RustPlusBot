# #info Server-Data Embeds — Design

**Date:** 2026-07-20
**Status:** Approved (brainstorm gate passed)
**Branch (planned):** feat/info-embeds off develop

## Problem

Nine slash commands — `/pop`, `/time`, `/wipe`, `/online`, `/offline`, `/team`,
`/alive`, `/small`, `/large` — exist only as thin wrappers around the in-game
`!command` handlers. Each builds a synthetic `CommandContext`, calls the same
`ICommandHandler`, and returns the handler's single plain localized line as an
ephemeral text followup (`Modules/ServerCommandModule.cs:89-128`). They produce
no embed at all.

That surface is poor on three counts:

- **Pull, not push.** The data is live and constantly changing, but a user has
  to ask for each fact one command at a time.
- **Multi-server friction.** With more than one paired server and no `server`
  argument, `ServerResolver` hard-errors with `command.server.specify` rather
  than offering a picker (`Servers/ServerResolver.cs:17-49`).
- **Thin output.** `ServerInfoSnapshot` already carries `MaxPlayers`,
  `QueuedPlayers` and `WipeTimeUtc`, and `#info` renders none of them
  (`Messages/ServerInfoMessageRenderer.cs:53-70`).

Meanwhile the per-server **#info** channel shows only connection status, the
active credential's SteamId, a bare player count and a one-line team summary.

**Goal:** delete the nine slash commands and surface the same data — plus more —
as three auto-refreshing embeds in **#info**, at the density rustplusplus
achieves. The in-game `!command` surface is untouched.

## Decisions (user-confirmed)

1. **Three embeds, not one** — Server / Events / Team, below the existing map
   image embed. Maps 1:1 onto the removed commands, keeps each readable, and
   lets each refresh independently.
2. **Periodic refresh with a configurable interval**, on top of the existing
   event-driven triggers.
3. **The "Switch active player" select comes off the embed** and is replaced by
   a `/server player` slash command.
4. **The "Remove server" button stays** on the embed — the nearest existing
   commands (`/workspace purge`, `/admin reset-database`) are guild-wide, not
   per-server, so removing it would lose a capability.
5. **In-game `!pop` / `!time` / `!wipe` / `!online` / `!offline` / `!team` /
   `!alive` / `!small` / `!large` all stay.** Discord is not visible from inside
   Rust; that surface is the reason the handlers exist.

### The server selector disappears

The original framing asked for "a server selector when several are paired".
That requirement dissolves: **#info is already per-server**, one channel per
`RustServer` under its own category (`Specs/ServerWorkspaceSpecProvider.cs:11`).
Each server's data renders in its own channel, so nothing needs disambiguating.
Only `/server player` still needs to resolve a server, and it reuses the
existing autocomplete.

## Scope

### Deleted

| Path | Reason |
| ---- | ------ |
| `Features.Commands/Modules/ServerCommandModule.cs` | the nine `[SlashCommand]`s |
| `Features.Commands/Servers/ServerQueryService.cs` | only caller was the module |
| `Features.Commands.Tests/Servers/ServerQueryServiceTests.cs` | tests deleted code |

Plus the DI registration of `ServerQueryService`
(`CommandServiceCollectionExtensions.cs:66`).

### Moved

`ServerResolver.cs`, `ServerResolution.cs` and `ServerAutocompleteHandler.cs`
move from `Features.Commands/Servers/` to `Features.Connections/Servers/`, with
their namespace changed and their DI registration relocated.

The new `/server player` command must live in `Features.Connections`, because it
references `WorkspaceComponentIds.ServerInfoSwapPrefix` and **`Features.Commands`
does not reference `Features.Workspace`** (its refs are Abstractions,
Localization, Persistence, Features.Connections, Features.Events,
Features.ItemData, Discord). Since deleting `ServerCommandModule` leaves all
three files with no remaining consumer in Commands, moving beats duplicating.
`Features.Commands` references `Features.Connections`, so nothing there breaks.

Their three resx keys — `command.server.none`, `command.server.specify`,
`command.server.unknown` — are therefore **retained**, not deleted:
`ServerResolver` still resolves them for `/server player`.

### Kept

- All nine `ICommandHandler`s in `Features.Commands/Handlers/` and every
  `command.*` resx key they resolve, including `command.notconnected`.
- `ServerResolverTests`, updated only for the new namespace.

## The three embeds

Declaration order in `ServerWorkspaceSpecProvider.GetMessageSpecs()` controls
in-channel order, so the specs are declared map → server → events → team.

```text
[1] 🗺️  map image embed                    server.info.map    (unchanged)

[2] 🟢 My Rust Server                       server.info        (rewritten)
    rust.io:28082
    Status         Connected
    Players        187 / 200  ·  12 queued     ← /pop
    Time           14:32 ☀️  ·  3h12m to night ← /time
    Wipe           4d 6h ago                   ← /wipe
    Active player  76561198012345678
    [Remove server]

[3] ⚡ Events                               server.events      (new)
    🚢 Cargo Ship       Not out
    🚁 Patrol Heli      Not out
    🚁 Chinook          Out · G12
    🛢️ Small Oil Rig    Crate unlocking · 4m left  ← /small
    🛢️ Large Oil Rig    Online                     ← /large

[4] 👥 Team — 3/5 online                    server.team        (new)
    👑🟢 Alice    G12   2h14m
      🟢 Bob      H9    AFK 7m
      💀 Carol    G12   dead 6m
      ⚫ Dave     —     offline
```

### Server embed (`server.info`, rewritten)

Keeps the existing title glyph, endpoint description, status colour and Status /
Active-player fields. Adds three fields:

| Field | Source | Replaces |
| ----- | ------ | -------- |
| Players | `ServerInfoSnapshot.Players` / `.MaxPlayers` / `.QueuedPlayers` | `/pop` |
| Time | `ServerTimeSnapshot.TimeOfDay` / `.Sunrise` / `.Sunset` | `/time` |
| Wipe | `ServerInfoSnapshot.WipeTimeUtc` + `IClock.UtcNow`, via `DurationFormat.Compact` | `/wipe` |

The bare `state.PlayerCount` field is replaced by the richer Players field.

**Map size and seed stay out** — they are already rendered by
`ServerInfoMapMessageRenderer` in the embed directly above
(`map.info.size`, `map.info.seed`). No duplication.

**Day/night computation is extracted** into the shared `Daylight` helper in
Abstractions described under "Renderer placement" below, consumed by both
`TimeCommandHandler` and this renderer so `!time` and the embed cannot drift
apart. The helper returns raw values; each caller formats with its own resx keys.

### Events embed (`server.events`, new)

Five inline fields. Cargo / heli / chinook come from
`IEventState.GetActiveMarkers(guildId, serverId, kind)` — present means out,
rendered with its grid reference and time since `SeenAtUtc`; absent means
"Not out". Small and large rigs come from `IRigState.Get(...)`, mapping
`RigStatus.Online | Active | Offline`, showing `RigState.Remaining` when the
phase is timed.

Cargo, heli and chinook are **not** in the removed-command list, but their state
is already tracked in-process at zero marginal cost, and a two-row Events embed
would not justify a message slot.

`RigReply` (the existing status→text mapper) lives in `Features.Commands`, which
references `Features.Events` — the wrong direction to reuse from. The renderer
implements its own mapping against its own `server.events.rig.*` keys.

### Team embed (`server.team`, new)

Rendered as an embed **description list**, not fields: Rust caps a team at 8
members so there is no truncation risk, and a list reads far better than
Discord's three-column inline grid.

- One line per member: leader crown, status glyph, name, grid reference, duration.
- Status glyphs — 🟢 online and alive, 😴 AFK, 💀 dead, ⚫ offline.
- AFK membership comes from `IAfkState.GetAfkMembersAsync`, which also supplies
  `StillFor` for the duration.
- Grid references use `GridReference.From(x, y, dims, style)` with the
  per-server `MapGridStyle` read from `IMapSettingsStore`, matching every other
  grid reference the bot prints.
- Offline members render `—` rather than a grid, since their reported
  coordinates are last-known and would read as current. They also carry **no
  duration**: `TeamMemberSnapshot` exposes `LastSpawnTimeUtc` and
  `LastDeathTimeUtc` but no disconnect timestamp, so any "offline for X" figure
  would be fabricated from the wrong field.
- Sort order: online-alive by survival time descending (the `/alive` ordering),
  then AFK, then dead, then offline.
- Durations use `DurationFormat.Compact`.

Together this replaces `/team` (the roster), `/online` and `/offline` (the
glyphs and the `n/m online` title), and `/alive` (the ordering and survival
durations).

## Renderer placement

`IMessageRenderer`, `MessageRenderContext` and `MessagePayload` are currently
`internal` to `Features.Workspace`. **All three become public**, and each new
renderer lives next to the data it renders:

| Renderer | Project | Dependencies |
| -------- | ------- | ------------ |
| `ServerInfoMessageRenderer` (rewritten) | Features.Workspace | `IRustServerQuery` (Abstractions), `IConnectionStore`, `IServerService` |
| `ServerEventsMessageRenderer` (new) | Features.Events | `IEventState`, `IRigState`, `GridReference` |
| `ServerTeamMessageRenderer` (new) | Features.Players | `IRustServerQuery`, `IAfkState` (Connections), `GridReference` (Events), `IMapSettingsStore` |

The Team renderer lands in **Features.Players**, not Features.Connections. It
needs both `IAfkState` (Features.Connections) and `GridReference`
(Features.Events), and Connections sits *below* Events in the reference graph —
`Features.Events → Features.Connections`, not the reverse. Features.Players is
the lowest project that already references Workspace, Connections **and** Events,
so it is the only existing home that sees every dependency.

`Features.Events` and `Features.Players` already reference `Features.Workspace`
one-way, so this adds **no new project references**. Each feature registers its
own renderer in its existing `IServiceCollection` extension.

### Prerequisite: `DurationFormat` moves down

All three renderers format durations, but `DurationFormat` is currently
`internal` to `Features.Commands` (`Formatting/DurationFormat.cs`) — the
*highest* project in the graph, invisible to all three renderer homes. It moves
to `RustPlusBot.Abstractions/Formatting/DurationFormat.cs` as `public static`,
which every project already references. Its thirteen existing call sites are all
inside Features.Commands and need only a `using` swap.

`GridReference` is already `public` in `Features.Events.Formatting` and does not
move.

### Prerequisite: a shared daylight helper

`TimeCommandHandler` computes day-vs-night inline
(`Handlers/TimeCommandHandler.cs:27`), and it lives in Features.Commands where
the Server renderer cannot reach it. The computation moves to
`RustPlusBot.Abstractions/Connections/Daylight.cs` as a `public static` helper
over `ServerTimeSnapshot`, consumed by both the handler and the renderer so
`!time` and the embed cannot drift apart.

The helper reports the interval to the next sunrise/sunset in **in-game hours**.
Rust's day length is server-configurable and the API does not report it, so the
render must not imply real-world minutes; the resx string says so explicitly.

The alternative — keeping all three renderers in Workspace — would require
promoting `IEventState`, `IRigState` and `IAfkState` into Abstractions along
with `ActiveMarker`, `TrailPoint`, `RigState`, `RigStatus`, `AfkMember` and
`GridReference`. Strictly more churn than moving one formatting helper, and it
would invert the current layering where Workspace depends on no feature project.

`MessageSpec` declarations stay centralised in `ServerWorkspaceSpecProvider` so
the ordering invariant remains visible in one file.

## Refresh

### Hosted service

New `Hosting/ServerInfoRefreshHostedService` in `Features.Workspace`, mirroring
`MapHostedService.RunPeriodicRefreshAsync` (`Features.Map/Hosting/MapHostedService.cs:156-179`)
for its guild/server enumeration. Each tick, for every server whose
`ConnectionState.Status` is `Connected`, it calls the refresher below.

Interval comes from a new `WorkspaceOptions.InfoRefreshInterval`
(`Features.Workspace/WorkspaceOptions.cs`, alongside `EnableDangerCommands`),
default `00:01:00`, surfaced in `appsettings.json` under the existing
`Workspace` section.

The existing event triggers in `WorkspaceHostedService` are unchanged and keep
firing an immediate full reconcile on connect/disconnect, credential change,
map-ready and server-registered.

### Narrow refresh path

The tick must **not** call `ReconcileServerAsync`: that acquires the per-guild
`ProvisioningLock` and re-walks channel provisioning and ordering repair
(`Reconciler/WorkspaceReconciler.cs:162-331`) — far too heavy for a
minute-by-minute pulse.

A new `RefreshServerMessagesAsync(guildId, serverId, ct)` on
`IWorkspaceReconciler` does only:

1. Read the provisioned message ids for the server's #info channel from
   `IWorkspaceStore` (a DB read, no Discord call).
2. Render the three payloads.
3. Canonicalize each via `RenderCanonicalizer.Canonicalize`.
4. `RenderGate.ShouldSend` → skip when unchanged; otherwise
   `EditMessageAsync` then `RenderGate.Commit`.
5. On a missing message id, or a 404 from the edit, fall back to the full
   `ReconcileServerAsync` for that server — the existing self-heal.

### Render gating is new here

`WorkspaceReconciler.EnsureMessagesAsync` edits **unconditionally** today; it
has no gate. Introducing `RenderGate` (`Discord/Posting/RenderGate.cs`, already
a DI singleton) on this path means the Events embed — which changes rarely —
stops burning a PATCH every minute. The Server embed will almost always change,
since in-game time advances every tick.

Steady-state cost per connected server per tick: three rustplus calls
(`GetServerInfoAsync`, `GetTimeAsync`, `GetTeamInfoAsync`; event and rig state
are in-process and free) and zero to three PATCHes.

## Replacing the swap select

`/server player [server]` replies **ephemerally with the same select menu** the
embed carries today — same `workspace:info:swap:{serverId}` custom id, handled
by the unchanged `ConnectionComponentModule` (`Features.Connections/Modules/ConnectionComponentModule.cs:19-21`).
No new interaction logic; the select is simply relocated from a persistent
message to an ephemeral response.

- The command lives in `Features.Connections/Modules/`, beside its handler.
- `server` is optional, resolved by `ServerResolver`: auto-selected when the
  guild has exactly one server, autocompleted otherwise.
- The `server.info.swap.placeholder` resx key is retained and reused.

`CommandHelpCatalog.Slash` gains an entry for it. The in-game list is unchanged.

## Edge cases

- **Not connected.** The Server embed renders Status and Active player only,
  omitting the live fields. Events and Team render an explicit "Not connected"
  body rather than an empty payload — an empty payload makes the reconciler skip
  the edit, which would leave stale data presented as current.
- **Snapshot returns null mid-tick** (socket dropped between the status check
  and the query). Distinct from a known disconnect: that embed's edit is skipped
  for this tick, preserving the previous render. The next tick recovers.
- **Server removed mid-tick.** `IServerService.GetAsync` returns null → the
  refresher skips the server; the deletion flow already tears down the channel.
- **Team query succeeds with zero members.** Title renders `0/0 online` and the
  body renders a localized "No team members" line.
- **Member with an empty display name.** Falls back to the SteamId, matching the
  existing leader-name handling (`ServerInfoMessageRenderer.cs:116-118`).
- **Deleted slash commands lingering in Discord.** Guild command registration
  bulk-overwrites, so the nine disappear on the next registration pass. Confirm
  during the live smoke rather than assuming.
- **Interval set absurdly low.** The interval is clamped to a one-second floor
  when read, so a misconfiguration cannot spin the loop.

## Testing

- **Renderer unit tests, per embed** — mirroring
  `Features.Workspace.Tests/Messages/RendererTests.cs`: connected and
  disconnected, null snapshot, zero-member team, AFK member, dead member,
  offline member, leader crown, each of the three rig phases, marker present
  and absent, and grid style honoured.
- **Refresher tests** — an unchanged render produces no `EditMessageAsync`; a
  changed render produces exactly one; a 404 escalates to `ReconcileServerAsync`;
  a missing message id escalates likewise.
- **Hosted service test** — only `Connected` servers are refreshed.
- **Existing Workspace tests updated** — `RendererTests` currently asserts the
  swap select is present (`:66`, `:118`) and that the select and button occupy
  separate action rows (`:246`); those assertions change to expect the button
  alone.
- **Command tests** — `ServerQueryServiceTests` deleted.
  `CommandRegistrationTests` drops its `ServerResolver` and `ServerQueryService`
  resolution assertions (the former moves to the Connections container, the
  latter is gone); its `Assert.Equal(28, handlers.Count)` is **unchanged**,
  since no `ICommandHandler` is removed. A matching assertion that
  `ServerResolver` resolves is added to the Connections registration test.
  The in-game `QueryHandlersTests` must keep passing unmodified — that is the
  guard that the `!command` surface survived intact.
- **Localization** — `StringsResourceParityTests.Catalog_has_expected_key_count`
  hard-codes `281` (`tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs:44`);
  update to the new total, with every added key present in both `Strings.resx`
  and `Strings.fr.resx`.
- **Live smoke (user gate)** — confirm the four messages render in order in
  #info, that they refresh on the configured interval, that the nine slash
  commands are gone from Discord's picker, that the in-game `!commands` still
  answer, and that `/server player` swaps the active credential.

## Non-goals

- Battlemetrics-sourced player data (subsystem 7).
- A per-member player-profile embed or Steam avatars.
- Historical charts (population over time, wipe-cycle trends).
- Making the Events embed interactive (per-event mute toggles).
- Per-server refresh intervals — the interval is process-wide.
- Restoring a slash-command equivalent for `/pop` and friends. The in-game
  handlers remain the only on-demand surface; #info is the Discord surface.
