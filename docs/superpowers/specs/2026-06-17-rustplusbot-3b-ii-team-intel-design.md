# Subsystem 3b-ii — Team-intel commands (design)

**Date:** 2026-06-17
**Branch:** `feat/team-intel` off `develop`
**Status:** Approved design, pending implementation plan.

## Summary

3b-ii is the second slice of subsystem 3 (chat + `!commands`). It adds six in-game
team-intelligence `!commands` that read a single live `GetTeamInfoAsync` snapshot from the
connected Rust+ socket. It is a **pure additive read-path extension** on top of the 3b command
framework: no new entities, migrations, events, options, background services, or projects.

**Ships:** `!online`, `!offline`, `!team`, `!alive`, `!prox`, `!steamid`.

**Deferred:** `!afk` ("inactive teammates 5+ min") — it requires tracking each member's position
across time, which a single snapshot cannot compute. It needs a background position-delta poller
(its own future slice). `!connections`/`!deaths`/`!leader` remain deferred per the feature catalog
(team-event history tracker / `PromoteToLeader`). The Discord surfaces (`#commands`, `/help`,
`/uptime`, `/leader`, `#info` team summary) remain in **3c**.

## Context & confirmed API

Verified against the real `RustPlusApi 2.0.0-beta.1` DLL (decompiled, not guessed):

- `RustPlus.GetTeamInfoAsync(CancellationToken = default)` returns `Task<Response<TeamInfo?>>`
  (`.IsSuccess` / `.Data`, same `Response<T>` shape as `GetInfoAsync`/`GetTimeAsync`).
- `TeamInfo` (record): `ulong LeaderSteamId`, `IEnumerable<MemberInfo>? Members`,
  `DeathNote? DeathNote`, `IEnumerable<PlayerNote>? Notes`, `IEnumerable<PlayerNote>? LeaderNotes`.
  Only `LeaderSteamId` + `Members` are used this slice; notes/death-note are out of scope.
- `MemberInfo` (record): `ulong SteamId`, `string? Name`, `float X`, `float Y`, `bool IsOnline`,
  `DateTime LastSpawnTime` (UTC), `bool IsAlive`, `DateTime LastDeathTime` (UTC).

This is the same package and the same `IRustServerQuery` seam already used by `!pop`/`!time`/`!wipe`.

## Architecture (approach A: extend `IRustServerQuery`)

The new team data crosses the Connections → Commands boundary through the **existing query seam**,
exactly mirroring `GetServerInfoAsync`/`GetTimeAsync`. RustPlusApi types do not leak into Commands —
a dedicated DTO sits at the boundary.

### Connections-side additions

New public DTOs in `src/RustPlusBot.Features.Connections/Listening/`:

```csharp
public sealed record TeamInfoSnapshot(
    ulong LeaderSteamId,
    IReadOnlyList<TeamMemberSnapshot> Members);

public sealed record TeamMemberSnapshot(
    ulong SteamId,
    string Name,
    float X,
    float Y,
    bool IsOnline,
    bool IsAlive,
    DateTimeOffset LastSpawnTimeUtc,
    DateTimeOffset LastDeathTimeUtc);
```

- **`IRustServerQuery`** (public) gains:
  `Task<TeamInfoSnapshot?> GetTeamInfoAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);`
  — returns `null` when `(guildId, serverId)` has no live socket.
- **`IRustServerConnection`** (internal) gains:
  `Task<TeamInfoSnapshot?> GetTeamInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);`
- **`RustPlusSocketSource`** (the untested integration shim) implements it: awaits
  `_rustPlus.GetTeamInfoAsync(ct)` under the timeout, returns `null` on `!IsSuccess`/timeout/null data,
  otherwise maps each `MemberInfo`:
  - `Name` → `member.Name ?? string.Empty` (null-safe).
  - `Members` → `teamInfo.Members ?? []` then `.Select(...).ToList()` (null-safe).
  - `DateTime` → `DateTimeOffset` via `new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc))`
    (same idiom as the existing wipe-time mapping).
- **`ConnectionSupervisor`** implements `IRustServerQuery.GetTeamInfoAsync` over its `_liveSockets`
  registry — identical lookup pattern to `GetServerInfoAsync`/`GetTimeAsync` (look up the live
  connection for the key; if none, return `null`; otherwise delegate with the configured timeout).

### Commands-side additions

Six `internal sealed class : ICommandHandler` in `src/RustPlusBot.Features.Commands/Handlers/`,
each registered `AddScoped<ICommandHandler, …>()` in `CommandServiceCollectionExtensions.AddCommands`.
Each follows the established handler shape: call `IRustServerQuery.GetTeamInfoAsync`; if `null` →
`command.notconnected`; otherwise shape **one compact comma-joined line** via `ICommandLocalizer`.

| Command   | `Name`     | Logic |
|-----------|------------|-------|
| `!online` | `online`   | Members where `IsOnline`; names comma-joined. Empty → `command.online.none`. |
| `!offline`| `offline`  | Members where `!IsOnline`. Empty → `command.offline.none`. |
| `!team`   | `team`     | All member names. Empty → `command.team.none`. |
| `!steamid`| `steamid`  | No arg → `Name SteamId` pairs for all members. Name arg → filter (partial, case-insensitive). |
| `!alive`  | `alive`    | `IsAlive` members sorted by survival (`clock.UtcNow − LastSpawnTimeUtc`) descending, then dead members shown as "dead". Uses `IClock` + `DurationFormat.Compact`. |
| `!prox`   | `prox`     | Caller's own position from the snapshot (match `context.SenderSteamId`); distance to each **other** member via `√(Δx²+Δy²)`, rounded to whole metres. Name arg filters to one. |

Two new small, pure, testable helpers in `src/RustPlusBot.Features.Commands/Formatting/`:

- `Distance.Between(float x1, float y1, float x2, float y2)` → rounded integer metres.
- `TeamMemberFilter.ByName(snapshot.Members, arg)` → partial, case-insensitive name match
  (shared by `!prox` and `!steamid` so the match logic is not duplicated).

### Reply formatting & edge cases (consistent across all six)

- **One compact line**, comma-joined. Matches the existing `!pop`/`!time` single-line style and the
  reference bot. Multi-member lists are not capped this slice (Rust truncation only bites on very
  large teams, which is rare; revisit if it becomes a problem).
- **Not connected** (null snapshot): `command.notconnected` (existing key, reused).
- **Empty result set**: a per-command localized "none" variant (e.g. `command.online.none`).
- **`!prox` caller not in snapshot** (own member entry missing): `command.prox.selfunknown`.
- **`!steamid`/`!prox` name arg matches nothing**: `command.team.nomatch` (takes the arg as `{0}`).
- All replies localized **EN/FR** via new keys added to both cultures in
  `CommandLocalizationCatalog.Default`.

### Loop/echo safety

Unchanged from 3b and confirmed safe: the dispatcher drops `FromActivePlayer` lines so replies do
not re-trigger, and command replies are **not** recorded in `RelayDedupBuffer` (the bot's in-game
answer posts once to Discord `#teamchat`, deliberately). These six handlers add no new outbound path
beyond the existing `ITeamChatSender` reply, so no new loop surface is introduced.

## Testing

TDD per handler (repo convention). New tests in `tests/RustPlusBot.Features.Commands.Tests/Handlers/`:

- One test class per command covering: happy path, not-connected (null snapshot), empty set, and the
  command-specific edge cases — `!prox` self-unknown, `!prox`/`!steamid` name-no-match, `!alive`
  ordering (longest first) with a mix of alive and dead members.
- Pure-helper tests for `Distance.Between` and `TeamMemberFilter.ByName`.
- `IClock` faked with a fixed `UtcNow` so `!alive` survival strings are deterministic.

Connections-side:

- `ServerQueryTests` gains coverage for `ConnectionSupervisor.GetTeamInfoAsync`: mapped snapshot when
  a live socket exists, `null` when none.
- `RustPlusSocketSource`'s team mapping stays **untested** — integration shim, by design (same as the
  existing `GetServerInfoAsync`/`GetTimeAsync` shims).

### Fakes — critical (the 3a/3b lesson)

Adding `GetTeamInfoAsync` to `IRustServerQuery` and `IRustServerConnection` forces **every** test
double to implement it, or the whole assembly silently drops its tests. Must update:

1. `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` (the
   `IRustServerConnection` fake).
2. Any hand-rolled `IRustServerQuery` fake used in Commands tests (NSubstitute auto-stubs interfaces,
   but a concrete fake needs the new member).
3. Any `IRustServerQuery`/`IRustServerConnection` fake referenced in `Features.Chat.Tests`.

After the change, run the **full** suite and read **per-assembly** counts — a low total means an
assembly failed to build and its tests vanished.

## Verification gate (before claiming done)

- `dotnet build` strict (0 warnings / 0 errors under the repo analyzers).
- **Full** `dotnet test`, reading per-assembly counts (not just the grand total).
- `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
  (the repo's real format gate; the pre-push hook enforces it; Roslynator does not catch what jb
  reorders).
- Confirm **no EF model drift** (no new entities/migrations — team info is read-only live data).

## Explicit non-goals (this slice)

- No `!afk` (needs a position-delta poller).
- No `!connections`/`!deaths` (need a team-event history tracker).
- No `!leader` / `PromoteToLeader` (deferred per catalog).
- No new entities, migrations, events, options, background services, or projects.
- No Discord surfaces (those are 3c).
- No caching of the team snapshot (one `GetTeamInfoAsync` call per command — fine at human typing
  speed; a cache was considered and rejected as YAGNI, and would be inconsistent with the existing
  non-caching `GetServerInfoAsync`).
- No member-list length cap / "+N more" truncation (revisit only if real teams overflow).

## After shipping

Flip the `feat/team-intel`-related rows in `docs/product/feature-catalog.md` to ✔️ Done
(`!online`/`!offline`/`!team`/`!alive`/`!prox`/`!steamid`), and update
`docs/product/feature-catalog.md` `!afk` to note it is deferred pending a position poller.
Next after 3b-ii: `!afk` poller slice and/or **3c** (command Discord surfaces), then subsystem 2.
