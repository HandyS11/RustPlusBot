# Subsystem 3b — In-game `!command` framework (thin slice)

**Status:** Approved design · **Created:** 2026-06-16 · **Branch:** `feat/command-framework` off `develop`
**Predecessor:** 3a chat bridge (PR #8, merged `8132240`). **Successors:** 3b-ii (team-intel commands), 3c (Discord surfaces).

---

## 1. Goal & scope

Build the **in-game command framework** — the engine that turns a `!`-prefixed line in in-game team
chat into a parsed, authorized, rate-limited command whose localized reply is sent back to team chat —
and prove it with a **thin slice of six commands** that need no new team-event tracking:

- `!mute` / `!unmute` — toggle all bot→game output (persisted per `(guild, server)`).
- `!uptime` — bot uptime (+ best-effort per-connection uptime).
- `!pop` — population via `GetServerInfoAsync` (`players/max (queued)`).
- `!time` — in-game time + day/night countdown via `GetTimeAsync`.
- `!wipe` — time since wipe via `GetServerInfoAsync.WipeTime`.

**Explicitly deferred** (the catalog's other subsystem-3 in-game commands), to **3b-ii**:
`!online` / `!offline` / `!team` / `!afk` / `!alive` / `!prox` / `!steamid` (need `GetTeamInfoAsync`
plumbing — feasible but a wider seam), and `!deaths` / `!connections` / `!leader` (need a team-event
history tracker and `PromoteToLeader` — their own slice). **Discord surfaces** (`#commands` channel,
`/help`, `/uptime`, `/leader`) stay deferred to **3c**. This slice is **in-game only**.

### Locked decisions (from brainstorming)

| Topic | Decision |
|---|---|
| Command scope | Framework + thin slice (6 commands above) |
| Surface | In-game team chat only (no Discord input, no `#commands` echo) |
| `!mute` reach | **All bot→game output**: command replies AND Discord→game relayed messages. Game→Discord relay is unaffected. `!mute`/`!unmute` always respond. |
| Authorization | **Any teammate** may run any command. No role gating (that is subsystem 9). |
| Prefix | **Configurable per `(guild, server)`**, default `!`, read by the dispatcher. |
| Cooldown | **Per-`(server, command)` in-memory cooldown**; a command on cooldown is silently dropped. |
| Reply language | **Per-guild culture** (`GuildSettings.Culture`, EN/FR) — the whole team sees the guild's language. |

---

## 2. Architecture

New project **`RustPlusBot.Features.Commands`** (mirrors `Features.Pairing` / `Features.Connections`
/ `Features.Chat`). The command path branches off the **existing** `TeamMessageReceivedEvent` that the
chat relay already consumes — `InMemoryEventBus` fans out per-subscriber (proven in 1b-ii), so **no new
bus event** is introduced.

```
in-game line
  → RustPlusServerConnection.OnTeamChatReceived            (Features.Connections, unchanged)
  → ConnectionSupervisor publishes TeamMessageReceivedEvent (unchanged)
  → InMemoryEventBus fans out per-subscriber:
       ├─ TeamChatRelay        (existing, Features.Chat → posts to Discord)
       └─ CommandDispatcher    (NEW, Features.Commands)
              1. skip if evt.FromActivePlayer   (never react to our own echoed replies)
              2. read prefix for (guild,server); skip if line doesn't start with it
              3. parse "<prefix>name args" → (name, args[])   — skip if unparsable
              4. resolve ICommandHandler by name; unknown → silent skip
              5. cooldown: CommandCooldown.TryConsume(server,name) — false → drop
              6. mute gate: if muted AND name ∉ {mute,unmute} → drop
              7. reply = await handler.ExecuteAsync(ctx)  (already localized; null → no reply)
              8. ITeamChatSender.SendAsync(guild,server,reply)   (existing seam)
```

### Why skip `FromActivePlayer`

A command reply is a bot→game send; it echoes back on the socket as a line from the active player.
Skipping `FromActivePlayer` at step 1 prevents replies from re-triggering as commands. The Chat relay
already drops the same echo via `RelayDedupBuffer`, so the reply still posts to Discord **once** (as a
normal bridged bot line) and never double-posts. The command feature does **not** touch the dedup buffer.

### Hosting

Thin `CommandsHostedService` (`IHostedService`) runs one subscription loop over
`TeamMessageReceivedEvent` and hands each event to `CommandDispatcher`, with a broad-catch that
isolates a faulting command from the loop and the host — identical shape to `ChatHostedService`'s
relay loop and `ConnectionSupervisor`'s isolation.

---

## 3. Components (`Features.Commands`)

| Component | Responsibility | Tested |
|---|---|---|
| `CommandsHostedService` | Subscribe to `TeamMessageReceivedEvent`; dispatch each; broad-catch isolation. | thin (no unit test, like other hosted services) |
| `CommandDispatcher` | The pipeline of §2 (steps 1–8). Builds a name→handler map from the injected `IEnumerable<ICommandHandler>` at construction. | unit, with fakes |
| `CommandLine` | Pure parser: `(prefix, rawLine) → (name, string[] args)?`. Case-insensitive name; trims; tolerates extra whitespace; returns null when the line is just the prefix or empty. | unit |
| `ICommandHandler` | `string Name { get; }` + `Task<string?> ExecuteAsync(CommandContext ctx, CancellationToken ct)`. Returns the **already-localized** reply, or `null` for "no reply". | per-handler unit |
| `CommandContext` | Immutable carrier: `GuildId`, `ServerId`, `Culture`, `SenderSteamId`, `SenderName`, `IReadOnlyList<string> Args`, plus injected `IRustServerQuery`, `IMuteStore`, `ICommandLocalizer`, `IClock`. | — |
| `CommandCooldown` | Singleton; per-`(serverId, name)` last-run instant under `IClock`. `TryConsume` returns false within the window. Cooldown window from `CommandOptions`. | unit (IClock-driven) |
| `CommandLocalizer` + `CommandLocalizationCatalog` | Feature-owned EN/FR reply strings (see §5). | unit |
| `CommandOptions` | `Prefix` default `"!"` (fallback only — the per-server prefix overrides), `Cooldown` window (e.g. 4s). `ValidateOnStart`. | — |

### Handlers (6)

| Handler | Command | Data source | Reply (localized) |
|---|---|---|---|
| `MuteCommandHandler` | `!mute` | `IMuteStore.SetMutedAsync(true)` | "Bot muted." (always runs even when muted) |
| `UnmuteCommandHandler` | `!unmute` | `IMuteStore.SetMutedAsync(false)` | "Bot unmuted." (always runs even when muted) |
| `UptimeCommandHandler` | `!uptime` | process-start baseline via `IClock`; per-connection uptime best-effort | "Uptime: {bot}[, server: {conn}]" |
| `PopCommandHandler` | `!pop` | `IRustServerQuery.GetServerInfoAsync` | "Pop: {players}/{max} ({queued} queued)" or not-connected |
| `TimeCommandHandler` | `!time` | `IRustServerQuery.GetTimeAsync` | "Time: {hh:mm} — {day\|night} for {countdown}" or not-connected |
| `WipeCommandHandler` | `!wipe` | `IRustServerQuery.GetServerInfoAsync.WipeTime` | "Wiped {duration} ago" or not-connected |

Handlers are registered by DI; the dispatcher builds the name map once. **Adding a 3b-ii command =
drop in a new `ICommandHandler` class** — no dispatcher change.

`!uptime` uptime baseline: 3b adds a process-start `DateTimeOffset` captured at host start (no existing
baseline). Per-connection uptime is **best-effort** — used only if a `ConnectedSince`-equivalent is
available on `ConnectionState`/the supervisor; otherwise the reply degrades to bot-uptime-only. (Do not
assume a `ConnectedSince` column exists; verify during execution and omit the server segment if absent.)

---

## 4. Cross-feature seams (two new public surfaces)

Both modeled on the existing `ITeamChatSender` (public seam in `Features.Connections` over the live
socket registry).

### 4.1 `IRustServerQuery` — read side of the live socket

`public` interface in **`Features.Connections`**, implemented by **`ConnectionSupervisor`** (which
already owns `_liveSockets` and already implements `ITeamChatSender` exactly this way):

```csharp
public interface IRustServerQuery
{
    Task<ServerInfoSnapshot?> GetServerInfoAsync(ulong guildId, Guid serverId, CancellationToken ct);
    Task<ServerTimeSnapshot?>  GetTimeAsync(ulong guildId, Guid serverId, CancellationToken ct);
}
```

- Returns **`null`** when there is no live socket for `(guild, server)` → handler emits a localized
  "not connected" reply.
- `ServerInfoSnapshot` / `ServerTimeSnapshot` are **our own DTOs** — RustPlusApi types never leak into
  `Features.Commands`. Mapping RustPlusApi → snapshot happens in the Connections adapter.
- Implementation: extend the internal `IRustServerConnection` with `GetServerInfoAsync` /
  `GetTimeAsync` returning snapshots; the untested `RustPlusServerConnection` shim maps the
  RustPlusApi response. `GetInfoAsync` already exists on the connection for the heartbeat — **reuse /
  generalize it** for `GetServerInfoAsync` rather than adding a parallel call.

**Execution note (untested shim):** verify the exact RustPlusApi field names against the
`2.0.0-beta.1` DLL during the build, as every prior shim did. Probed so far: `GetTimeAsync`,
`GetInfoAsync` exist; `ServerInfo` exposes `PlayerCount`/`MaxPlayers`/`QueuedPlayers`/`WipeTime`; the
time type (`ToTimeInfo`) exposes `Time`/`TimeOfDay`/`DayLengthMinutes`/`SunriseTime`/`SunsetTime`
(exact getters/units, and whether `WipeTime` is epoch seconds vs DateTime, to be confirmed). **Never
put a token/secret in an exception or log** (3b carry-forward from 1b-ii).

### 4.2 `IMuteStore` + `ServerCommandSettings` — shared command config

`public` interface in **Persistence** (where stores live). One entity holds both per-`(guild,server)`
command settings:

```
ServerCommandSettings { GuildId (ulong), ServerId (Guid), Prefix (string, default "!"), Muted (bool) }
  — composite/owned key on (GuildId, ServerId); FK to RustServer with cascade delete
    (consistent with the 1b-iii ConnectionState→RustServer cascade, so removing a server cleans it up).
```

`IMuteStore` surface: `GetMutedAsync`, `SetMutedAsync`, `GetPrefixAsync` (default `"!"` when no row).
New migration `CommandSettings`.

- **`Features.Commands`** reads prefix + reads/writes mute.
- **`Features.Chat`'s `TeamChatInboundProcessor` reads mute** before relaying Discord→game (the
  "mute = all bot→game output" decision). This is the **only** place mute crosses into another
  feature — a small read seam. Chat gains a constructor dependency on `IMuteStore`; its existing
  tests get an unmuted-by-default fake so they stay green.

---

## 5. Localization

`ILocalizer` / `LocalizationCatalog` are **internal to `Features.Workspace`** and must not be a
dependency of a chat-side feature. **Decision: each feature owns its own catalog.** `Features.Commands`
gets a small `ICommandLocalizer` + `CommandLocalizationCatalog` (only its reply strings) and reads the
guild culture via the **existing public** `IWorkspaceStore.GetCultureAsync(guildId)` (Persistence). The
`Normalize`/English-fallback logic (~30 lines) is duplicated from Workspace's `Localizer`; leave a
`// TODO: consolidate localizers into a shared project` marker. Promoting `ILocalizer` to a shared
project is a **separate later cleanup**, out of 3b scope.

Culture is read once per dispatched command (`GetCultureAsync(guildId)`) and passed in `CommandContext`.

---

## 6. Error handling

- **Isolation:** the dispatcher wraps each command in broad-catch + structured log; a faulting handler
  never crashes the loop or the host (mirrors `ConnectionSupervisor` / `ChatHostedService`).
- **User-facing failures never throw:** unknown command → silent skip (rustplusplus behavior; avoids
  spam from ordinary `!`-chatter); on cooldown → silent drop; muted non-mute command → silent drop;
  no live socket → localized "not connected" reply; bad/extra args → handler tolerates or replies a
  localized usage line. No exception ever reaches team chat.
- **Send failures:** `ITeamChatSender.SendAsync` returns its result enum; a failed send is logged, not
  retried (consistent with the relay).

---

## 7. Testing

- **Unit (with fakes):** `CommandDispatcher` (every pipeline branch: skip-active, wrong-prefix,
  unknown, cooldown, mute-gate, success), `CommandLine` parser, `CommandCooldown` (IClock-driven), each
  of the 6 handlers, `CommandLocalizer`. Fakes for `ITeamChatSender`, `IRustServerQuery`, `IMuteStore`,
  `IWorkspaceStore`, `IClock`.
- **Persistence:** `ServerCommandSettingsSchemaTests` (+ the RustServer-cascade behavior, seed a
  RustServer first — the 1b-iii FK lesson) and `MuteStoreTests` / prefix tests.
- **Untested shim:** `RustPlusServerConnection` snapshot mapping (the one integration shim, consistent
  with prior subsystems).
- **No relay regression:** the only Chat change is `TeamChatInboundProcessor` consulting `IMuteStore`;
  give its tests an unmuted-by-default fake.
- **Test-double maintenance (3a lesson):** adding members to `IWorkspaceStore` / `ITeamChatSender` /
  any mocked interface breaks test-double classes (`FakeWorkspaceStore`, etc.) and silently drops a
  whole assembly's tests. **Run the FULL suite and read per-assembly counts** — a low total means an
  assembly didn't build. NSubstitute on an `internal` interface needs
  `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in that project's csproj.

---

## 8. DI & host wiring

- New `AddCommands(this IServiceCollection)` extension in `Features.Commands`: registers the dispatcher,
  parser, cooldown (singleton), localizer + catalog, the 6 handlers (as `IEnumerable<ICommandHandler>`),
  `CommandsHostedService`, and `CommandOptions` with `ValidateOnStart`.
- `IMuteStore` + `ServerCommandSettings` registered in Persistence (`AddPersistence`); migration added.
- `IRustServerQuery` registered in `Features.Connections` (same concrete `ConnectionSupervisor`
  singleton that backs `IConnectionSupervisor` + `ITeamChatSender` — one instance, three interfaces).
- Host `Program.cs` calls `AddCommands()`; `Features.Chat` DI unchanged except its processor now
  resolves `IMuteStore`.
- **No new gateway intents** (3b reads/sends over the existing socket + already-widened intents from 3a).

---

## 9. Out of scope (carry-forward)

- Team-intel commands (`!online`/`!team`/`!afk`/`!alive`/`!prox`/`!steamid`) → **3b-ii** (`GetTeamInfoAsync` seam).
- History commands (`!deaths`/`!connections`) + `!leader` → later 3b slice (team-event tracker + `PromoteToLeader`).
- Discord surfaces (`#commands` channel, `/help`, `/uptime`, `/leader`) → **3c**.
- A `#settings` control to edit the command prefix → later (the column + store exist now; default `!`).
- Consolidating the duplicated localizer into a shared project → separate cleanup.
- Live-server auth-reject verification on the read path (the 1b-ii open `VERIFY`) → unchanged here.
