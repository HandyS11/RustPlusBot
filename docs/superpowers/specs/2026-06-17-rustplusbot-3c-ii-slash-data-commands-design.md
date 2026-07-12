# Subsystem 3c-ii — In-game data commands as Discord slash commands

**Date:** 2026-06-17
**Branch:** `feat/slash-data-commands` (off `develop`)
**Status:** Design approved, ready for implementation plan

## Summary

Surface the read-only in-game `!command` data set into Discord as **native slash
commands** (`/pop`, `/time`, `/wipe`, `/online`, `/offline`, `/team`, `/alive`). Each
command queries live server data through the existing `IRustServerQuery` seam and
replies **ephemerally**. This extends 3c's slash-module surface (`/help`, `/uptime`,
`/leader`); it does **not** provision a `#commands` text channel and does **not** relay
anything into in-game team chat.

This re-scopes the originally-deferred "3c-ii `#commands` channel relay." During
brainstorming the user chose Discord-native slash commands over a typed-`!command`
text channel, matching the feature catalog's **C4** "components/slash first" decision.

## Scope

### In scope — 7 new slash commands

| Slash command | Source data | In-game equivalent |
|---|---|---|
| `/pop` | `GetServerInfoAsync` (player count) | `!pop` |
| `/time` | `GetTimeAsync` (in-game time) | `!time` |
| `/wipe` | `GetServerInfoAsync` (wipe time) | `!wipe` |
| `/online` | `GetTeamInfoAsync` (online members) | `!online` |
| `/offline` | `GetTeamInfoAsync` (offline members) | `!offline` |
| `/team` | `GetTeamInfoAsync` (all member names) | `!team` |
| `/alive` | `GetTeamInfoAsync` (survival times) | `!alive` |

All replies are **ephemeral** (only the caller sees them), consistent with
`/help`/`/uptime`/`/leader`.

### Explicitly out of scope

- **No `#commands` text channel** and **no Discord→in-game relay/injection.** Slash
  commands only.
- **`/steamid` and `/prox` are dropped** — both are in-game-caller-centric
  (`!prox` is relative to the in-game caller's position; `!steamid` pairs names to
  ids for an in-game audience) and are irrelevant in a Discord context.
- **`/mute` and `/unmute` excluded** — they gate in-game bot output, meaningless as a
  Discord query.
- **`/leader` already shipped in 3c** — not re-added.
- **`!afk` stays deferred** — needs the position-delta poller (a separate future
  slice), unchanged by this work.
- No new entity, migration, event, option, background service, or project.

## Architecture

Everything lives in the **existing `RustPlusBot.Features.Commands` project** (the same
choice 3c made — no new project). The pattern is 3c's exactly: a thin slash module →
small testable services → `IRustServerQuery`.

The reusable core is the **existing `ICommandHandler` set**. Each in-game handler
(e.g. `TimeCommandHandler`, `AliveCommandHandler`) already encodes exactly the reply
formatting the slash command needs — the day/night calc, the survival sort, the
not-connected and empty-team guards, the localized strings. The slash path **delegates
to those handlers** so there is a single source of truth: in-game and slash replies
cannot drift. The handlers are already registered as `IEnumerable<ICommandHandler>`
(the dispatcher consumes them that way); the slash adapter injects the same set and
resolves by `Name`.

The `CommandDispatcher` itself (parse prefix → cooldown → mute-gate → send in-game) is
**not** reused — only the leaf `ICommandHandler.ExecuteAsync` formatting is. The in-game
`!command` set is otherwise untouched; this slice mirrors the same *data + replies* into
friendlier slash commands. The seam the handlers sit on is `IRustServerQuery` (in
`Abstractions/Connections`, returning `ServerInfoSnapshot`/`ServerTimeSnapshot`/
`TeamInfoSnapshot`), unchanged.

### Components

1. **`ServerCommandModule`** (new, `public sealed : InteractionModuleBase<SocketInteractionContext>`)
   — holds the 7 `[SlashCommand]` methods. Each method: guild-null guard →
   `DeferAsync(ephemeral: true)` → open a DI scope → resolve the target server via
   `ServerResolver` → call `ServerQueryService` → `FollowupAsync(ephemeral: true)`.
   Thin delegation, **untested** (InteractionModuleBase isn't unit-tested in this repo,
   per the established convention), mirroring `CommandSurfaceModule`. Discovered
   automatically — 3c already registered `Discord.InteractionModuleAssembly(thisAssembly)`
   in `AddCommands`.

2. **`ServerQueryService`** (new, `internal sealed`, **testable**) — a thin adapter
   that runs a named in-game handler for the slash path. One method:
   `RunAsync(string commandName, ulong guildId, Guid serverId, string culture, CancellationToken)`
   that builds a **synthetic `CommandContext`** — empty `Args`, `SenderSteamId = 0`,
   `SenderName = ""` (none of the 7 commands read sender identity or args) — resolves the
   matching `ICommandHandler` by `Name` from the injected `IEnumerable<ICommandHandler>`,
   and returns its `ExecuteAsync` reply string. Because the leaf handler does all
   formatting and guards, in-game and slash replies are identical by construction. This
   is where the (small) adapter logic worth testing lives; the per-command formatting is
   already covered by the existing handler tests.

3. **`ServerResolver`** (new, `internal sealed`, **testable**) — encapsulates the
   "which server" rule in one place, reused by all 7 commands. Given
   `(ulong guildId, string? serverArg, string culture)`, returns either the resolved
   `(Guid ServerId, string Name)` or a localized error message:
   - 0 servers registered → error ("no server registered").
   - exactly 1 → default to it (the `serverArg` is ignored/optional).
   - >1 and `serverArg` is null → error ("specify a server").
   - explicit `serverArg` → parse as `Guid` and validate it is a registered server for
     the guild; unknown → error.
   Backed by `IServerService.ListAsync(guildId)`.

4. **`ServerAutocompleteHandler`** (new, `: AutocompleteHandler`) — supplies the
   `server` parameter's choices per-guild from `IServerService.ListAsync`, offering
   each registered server's **name** as the label and its **id (Guid string)** as the
   value. Wired to the `server` parameter via
   `[Autocomplete(typeof(ServerAutocompleteHandler))]`. Untested (framework type;
   thin), like the modules.

5. **Localization** — reply text reuses the **existing** in-game keys in
   `CommandLocalizationCatalog` so Discord and in-game answers are word-for-word
   identical: `command.pop.ok`, `command.time.ok`/`.day`/`.night`, `command.wipe.ok`/
   `.unknown`, `command.online.ok`/`.none`, `command.offline.ok`/`.none`,
   `command.team.ok`/`.none`, `command.alive.ok`/`.member`/`.dead`, and
   `command.notconnected`. The **only new keys** are the `ServerResolver` errors
   (e.g. `command.server.none`, `command.server.specify`, `command.server.unknown`),
   added EN/FR.

### The `server` parameter

Every command declares an optional `server` string parameter with autocomplete:

```csharp
[SlashCommand("pop", "Show the current player count")]
public Task PopAsync(
    [Summary("server", "Which server (if more than one)")]
    [Autocomplete(typeof(ServerAutocompleteHandler))]
    string? server = null) => …
```

`ServerResolver` turns `server` (a Guid string or null) into the target or an error,
applying the 0/1/many rule above. This mirrors how `/leader` disambiguates multiple
servers (via a follow-up select); here the disambiguation is the parameter itself.

## Data flow (one `/pop` invocation)

```
User runs /pop [server:<autocompleted name → Guid value>]
  → ServerCommandModule.PopAsync
      guild-null guard → DeferAsync(ephemeral: true)
      → open DI scope
      → culture = IWorkspaceStore.GetCultureAsync(guildId)
      → ServerResolver.Resolve(guildId, server, culture)
            0 → error | 1 → default | >1 & null → error | explicit → validate
      → on error: FollowupAsync(error, ephemeral: true); return
      → ServerQueryService.RunAsync("pop", guildId, serverId, culture)
            synthetic CommandContext → PopCommandHandler.ExecuteAsync
              IRustServerQuery.GetServerInfoAsync
                null (not connected) → "command.notconnected"
                else → "command.pop.ok" with counts
      → FollowupAsync(text, ephemeral: true)
```

All 7 commands follow this shape; only the handler `Name` passed to
`ServerQueryService.RunAsync` differs.

## Error handling

- **Not in a guild** → ephemeral "must be used in a server" (existing guard).
- **Not connected / no live data** → the localized `command.notconnected` message
  (already modeled by the in-game handlers).
- **Empty team** (`/online`/`/offline`/`/team`/`/alive`) → the localized empty message
  (`command.team.none`, `command.online.none`, `command.offline.none`).
- **Server resolution failure** (0/many/unknown) → the localized `ServerResolver`
  error.
- **Unexpected service exception** → caught in the module body, logged, and a generic
  ephemeral error shown (avoids a Discord "interaction failed"). Broad-catch is
  acceptable here for the same reason the existing modules tolerate it.
- **No cooldown** — Discord's per-user interaction rate limits suffice and ephemeral
  replies aren't channel spam. The in-game `CommandCooldown` exists to curb team-chat
  flooding and is not relevant to slash commands.

## Testing

- **`ServerQueryService`** — unit tests for the adapter: it resolves the named handler
  and returns its reply; an unknown name is a guarded error (should never happen from the
  fixed module call sites). The per-command formatting itself is already covered by the
  existing handler tests, so these stay light (one happy delegation + the unknown-name
  guard).
- **`ServerResolver`** — unit tests: 0 servers, 1 server (default), many + no arg
  (error), explicit valid, explicit unknown/unparseable.
- **`ServerCommandModule` and `ServerAutocompleteHandler`** — **untested** (the
  repo does not unit-test `InteractionModuleBase`/framework types; logic is thin
  delegation, the weight is in the two services).
- **`CommandRegistrationTests`** — update the count tripwire for the new DI
  registrations (`ServerQueryService`, `ServerResolver` scoped; the autocomplete handler
  per framework requirements).

## Out-of-scope / future

- `!afk` poller slice (deferred, unchanged).
- Subsystem 2 (map + live events) is the next major subsystem after subsystem 3 is
  fully done.
- Possible future cleanup (not this slice): the moved `IRustServerQuery` files still
  carry the `RustPlusBot.Features.Connections.Listening` namespace despite living in
  Abstractions (a known deferred rename from 3c).

## Verification notes

- No EF model drift (no entity/migration/DbContext change) — verify via "no
  Migrations/ModelSnapshot/DbContext/entity files changed on the branch" (the
  `dotnet ef migrations has-pending-model-changes` tooling check is unreliable in this
  repo).
- Run `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (the repo's
  real format gate) before pushing.
- `docs/superpowers/` specs/plans are **local-only** and must never be committed.
