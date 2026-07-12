# RustPlusBot — Subsystem 1b-ii: Live Connection Lifecycle, Failover & `#info` Status

**Date:** 2026-06-15
**Status:** Approved design (subsystem 1b-ii)
**Scope:** Open and supervise a **live Rust+ socket per `(guild, server)`** driven by the server's
`Active` pooled credential; survive restarts via the persisted `ConnectionState`; **auto-failover**
to another pooled credential when a token is rejected; distinguish a **rejected token** from an
**unreachable server** via an events+heartbeat health model; let a ManageGuild admin **swap** the
active player from a select menu on `#info`; and enrich the `#info` message with **live status**.
A new `RustPlusBot.Features.Connections` project owns all of this. **Out of scope (→ 1b-iii):**
IP-change re-identification, RustServer-removal FK/cascade cleanup, and the account
disconnect/remove flow (`FcmRegistrationStatus.Disabled` / credential `SetStatus`).

---

## 1. Context & where this plugs in

1b-i took the system from "no servers" to "pair in-game → server auto-registered → workspace
provisioned → per-server credential stored in a multi-owner pool", with **no live socket**. 1b-ii is
the live-connection engine. It is the first consumer of seams 1a/1b-i deliberately left dangling:

- **`ConnectionState`** already exists in the schema (PK `RustServerId`, `GuildId`,
  `ActiveCredentialId`, `IsHealthy`, `UpdatedAt`) but has **zero consumers**. 1b-ii makes it the
  persisted, restart-surviving source of truth for the live connection.
- **`ServerRegisteredEvent`** is fired (for real) by 1b-i's pairing handler on new-server creation and
  already consumed by `WorkspaceHostedService` (to provision the category). 1b-ii adds a **second
  independent subscriber** (the connection hosted service) — the `InMemoryEventBus` fans out per
  subscriber, so this needs no change to the bus.
- **`ServerInfoMessageRenderer`** renders a static `#info` identity embed today and is documented as
  "live status enriched in 1b". 1b-ii is that enrichment.
- The **pairing supervisor / source / hosted-service** trio from 1b-i is the proven template the
  connection trio mirrors (one connection per identity, connect-with-bounded-timeout + capped
  backoff, scoped store access per unit of work, broad-catch isolation, `IAsyncDisposable`).

## 2. Confirmed flow (RustPlusApi)

1. The bot holds, per server, a pool of `PlayerCredential`s — each `(SteamId, PlayerToken)` for one
   owner — with exactly one marked `Active` (1b-i's invariant: first pairing per server is `Active`).
2. 1b-ii opens **one live Rust+ socket per server** using that `Active` credential's `SteamId` +
   `PlayerToken` against the server's `Ip`/`Port`, via the `RustPlusApi` core socket package.
3. A live socket can: connect successfully; be **auth-rejected** (the player token is stale/invalid);
   or be **unreachable** (server down / network). A connected socket can also go **half-open**
   (silently dead) — so liveness needs an active probe, not just disconnect events.

Consequences that shape the design:

- **Health is events + heartbeat.** Primary signal is the package's connect result and
  disconnect/error callbacks; a low-frequency heartbeat (a lightweight `GetInfo`) confirms a
  still-open socket is really alive and yields a live player count for `#info`.
- **A rejected token is a credential problem; an unreachable server is not.** Rejection → mark that
  credential `Invalid` and **failover** to another pooled credential. Unreachable → keep the active
  credential and **retry with backoff**.
- **Restart survival.** The `Active` credential per server is persisted, so on process restart the
  bot re-opens every server's socket without any user action.

## 3. Decisions locked during brainstorming

1. **Scope = core live-connection slice:** socket lifecycle + restart survival + auto-failover +
   server-unreachable handling + `#info` live status + ManageGuild swap UI. IP-change
   re-identification, RustServer-removal cascade, and the account disconnect/remove flow are deferred
   to **1b-iii**.
2. **Project:** a **new `RustPlusBot.Features.Connections`** project (parallel to Pairing/Workspace),
   not folded into Pairing — keeps "credential intake/FCM pairing" and "live game socket lifecycle"
   as separate, focused concerns. (Rejected: fold into `Features.Pairing` — conflates two concerns,
   bloats the project.)
3. **Health detection = hybrid** (events + low-frequency heartbeat). (Rejected: event-only — a
   half-open socket shows a zombie `Connected`; heartbeat-only — constant traffic, slow to react to a
   clean disconnect.)
4. **Swap UI = a persistent ManageGuild-gated select menu on the `#info` message** listing the pool,
   same render-here/handle-there pattern as 1b-i's Connect button. (Rejected: ephemeral button flow —
   more moving parts; slash command — breaks the component-driven, provisioned-message UX.)
5. **`ConnectionState` is the persisted source of truth** read by the `#info` renderer; cross-feature
   refresh is a new `ConnectionStatusChangedEvent` consumed by Workspace (mirrors
   `ServerRegisteredEvent`). (Rejected: renderer queries the live supervisor in-memory — couples
   Workspace→Connections at runtime and isn't restart-safe for rendering.)
6. **Failover order is deterministic** (eligible `Standby` credentials ordered by `Id`); the swap and
   failover share one `PromoteAsync`.
7. **Owner DM on token death only:** when a specific credential is marked `Invalid`, DM that owner
   ("your credential for *server* was rejected — reconnect"). Automatic failover is otherwise silent.

## 4. Project structure

New feature project **`RustPlusBot.Features.Connections`**. It contains:

- the `IRustSocketSource` seam and its `RustPlusApi` core-socket adapter (the untested integration shim);
- `IConnectionSupervisor` (singleton) — owns the live `(guild, server) → connection` map, the
  connect/heartbeat/failover loop;
- `ConnectionHostedService` — thin `IHostedService` driving the supervisor from the host lifecycle
  (start-all on Ready, consume `ServerRegisteredEvent`, stop-all on shutdown);
- `ConnectionComponentModule` — the `public` interaction module handling the `#info` swap select;
- `ConnectionOptions`;
- `AddConnections()` service-collection extension, wired in `Program.cs` after `AddPairing()`.

Dependency direction: `Features.Connections` → `Features.Workspace` (to read the public swap
custom-id constant) and → `Persistence`/`Abstractions`/`Domain`/`Discord`. **Workspace never depends
on Connections** — the `#info` refresh is decoupled through the event bus. This mirrors how
`Features.Pairing` is wired.

## 5. Data model (one migration: `LiveConnections`)

### 5.1 `ConnectionState` (exists; reshaped, becomes the live source of truth)

| Field | Type | Notes |
|---|---|---|
| `RustServerId` | `Guid` | PK, one row per server |
| `GuildId` | `ulong` | owning guild |
| `ActiveCredentialId` | `Guid?` | mirror pointer to the driving `PlayerCredential` |
| `Status` | `ConnectionStatus` | **replaces** `IsHealthy` |
| `PlayerCount` | `int?` | last heartbeat player count (`#info`); null if unknown |
| `UpdatedAt` | `DateTimeOffset` | bumped via `IClock` |

New enum **`ConnectionStatus`** (Domain, `Connections` namespace):

- **`Connecting`** — attempting connect (initial, after a swap, or after a drop).
- **`Connected`** — socket up and last heartbeat healthy.
- **`Unreachable`** — a valid active credential exists but the server isn't answering; retrying with
  backoff.
- **`NoCredentials`** — no eligible credential (pool empty, or every credential is `Invalid`).

The `PlayerCredential` pool is unchanged: `Status` (`Standby`/`Active`/`Invalid`) is the pool
designation, exactly one `Active` per server, kept in sync with `ConnectionState.ActiveCredentialId`
by `PromoteAsync`.

### 5.2 Migration `LiveConnections`

Single migration altering `ConnectionState`: drop `IsHealthy`; add `Status` (int) and `PlayerCount`
(nullable int). `ActiveCredentialId` is intentionally **not** FK-constrained to `PlayerCredential`
(the credential can be deleted out from under it in 1b-iii; the supervisor treats a dangling pointer
as "no active"). Pre-release database; no production data to preserve.

## 6. Persistence surface — `IConnectionStore` (new, in `Persistence`)

Lives in `Persistence` (returns Domain types) rather than `Abstractions`, the same call 1b-i made for
`IFcmRegistrationStore` — `Abstractions` stays dependency-free. Both the supervisor (writes) and the
Workspace `#info` renderer (reads) depend on it; both already reference `Persistence`.

- `GetAsync(guildId, serverId)` → `ConnectionState?`.
- `UpsertStatusAsync(guildId, serverId, status, playerCount, activeCredentialId)` → upsert the row,
  bump `UpdatedAt`; **returns whether anything changed** so the supervisor only publishes a refresh
  event (and triggers a Discord edit) on a real change.
- `GetActiveCredentialAsync(guildId, serverId)` → the `Active` `PlayerCredential` (with its protected
  token) to connect with, or null.
- `ListConnectableServersAsync()` → every `(GuildId, RustServerId)` that has an `Active` credential,
  across all guilds — the startup enumeration for `StartAllAsync` (analogous to
  `IFcmRegistrationStore.ListActiveAsync`).
- `ListPoolAsync(guildId, serverId)` → the pool (`Id`, `OwnerUserId`, `SteamId`, `Status`) for the
  `#info` select and failover eligibility.
- `PromoteAsync(guildId, serverId, credentialId)` → set `credentialId` `Active`, demote the prior
  `Active`→`Standby` (skip `Invalid`), update `ConnectionState.ActiveCredentialId`. Shared by swap +
  failover; returns false if `credentialId` isn't an eligible pool member.
- `MarkInvalidAsync(credentialId)` → set a `PlayerCredential` `Invalid` (auth-reject failover).

All token material stays protected via the existing `ICredentialProtector`; the store returns the
protected blob and the supervisor unprotects it just-in-time (as `PairingSupervisor` does for FCM
creds). Secrets are never logged.

## 7. Socket adapter — the one untested integration shim

`IRustSocketSource` + `IRustServerConnection` wrap the `RustPlusApi` core socket package, mirroring
`RustPlusFcmPairingSource`:

- `IRustSocketSource.Create(ip, port, steamId, playerToken)` → `IRustServerConnection`.
- `ConnectAsync(timeout, ct)` → `SocketConnectOutcome { Connected, AuthRejected, Unreachable }`.
- `GetInfoAsync(timeout, ct)` → `HeartbeatResult { Ok(int playerCount), Unreachable, AuthRejected }`
  (a lightweight `GetInfo`).
- `DisposeAsync()`.

The three-way **`AuthRejected` vs `Unreachable`** mapping from `RustPlusApi`'s
exceptions/disconnect-codes is the integration risk and is **verified against `2.0.0-beta.1` during
execution** (the same way 1b-i flagged its FCM member names). The real adapter is excluded from unit
tests; a `FakeRustSocketSource` (scriptable per-server connect + heartbeat outcomes) drives every
other test. Add `RustPlusApi` `2.0.0-beta.1` to `Directory.Packages.props`.

## 8. `IConnectionSupervisor` (singleton) + `ConnectionHostedService`

### 8.1 Supervisor loop (one per `(guild, server)`)

A `ConcurrentDictionary<(ulong Guild, Guid Server), Handle>`; each handle runs an isolated loop:

1. Resolve the `Active` credential via `IConnectionStore`. None → `NoCredentials` (publish), stop.
2. Unprotect the token. `CryptographicException` → `MarkInvalid` that credential → DM its owner →
   failover (step 4's reject path).
3. `Connecting` (publish) → `ConnectAsync(ConnectTimeout)`:
   - **Connected** → seed a first heartbeat for the initial `PlayerCount` → `Connected` (publish) →
     enter the heartbeat loop.
   - **AuthRejected** → `MarkInvalid(active)` + DM that owner → promote the next eligible `Standby`
     (deterministic by `Id`) and loop; if none → `NoCredentials` (publish).
   - **Unreachable** → `Unreachable` (publish) → capped exponential backoff → retry the **same**
     credential.
4. **Heartbeat loop** every `HeartbeatInterval` (`GetInfoAsync(HeartbeatTimeout)`):
   - **Ok(count)** → stay `Connected`, update `PlayerCount`/`UpdatedAt` (publish only if changed).
   - **AuthRejected** → the reject/failover path.
   - **Unreachable / timeout** → `Unreachable` (publish), break to the reconnect-with-backoff loop.

Public surface (mirrors `IPairingSupervisor`):

- `StartAllAsync(ct)` — on Ready: open a socket for every server that has an `Active` credential
  (restart survival).
- `EnsureConnectionAsync(guildId, serverId, ct)` — start/restart one server (after a swap, or on
  `ServerRegisteredEvent`).
- `StopAsync(guildId, serverId)` — stop one (swap restart / transition to `NoCredentials`).
- `StopAllAsync()` — shutdown.

`IAsyncDisposable`; a broad catch around each loop guarantees one faulting socket never crashes the
host or the other servers' sockets — identical hardening to `PairingSupervisor`.

### 8.2 `ConnectionHostedService` (`IHostedService`)

On Discord `Ready` (once) → `StartAllAsync`. A background loop subscribes to `ServerRegisteredEvent`
→ `EnsureConnectionAsync` (1b-i already marked the first credential `Active`). On shutdown →
`StopAllAsync`. Same structure as `PairingHostedService` + `WorkspaceHostedService` (the `_started`
guard is safe because `Ready` is dispatched serially on the gateway thread).

## 9. `#info` live status + swap (Workspace)

`ServerInfoMessageRenderer` additionally reads `IConnectionStore.GetAsync` + `ListPoolAsync`:

- **Embed:** colour + glyph by status (🟢 `Connected` / 🟡 `Connecting`·`Unreachable` / 🔴
  `NoCredentials`); fields = **Status**, **Active player** (`SteamId`), **Player count** (only when
  `Connected` and known), **Updated** (relative time). All strings localized EN/FR via
  `LocalizationCatalog`. (Pool entries are labeled by `SteamId`: the gateway runs with only
  `GatewayIntents.Guilds` / `AlwaysDownloadUsers = false`, so reliable Discord-username resolution
  would need a privileged intent or a per-render REST lookup — friendly-name enrichment is deferred.)
- **Swap select** (custom id `workspace:info:swap:{serverId}` built from a new
  `WorkspaceComponentIds.ServerInfoSwapPrefix`): options = pool entries (label = `SteamId`, value =
  `credentialId`, default = the active one). Members see it but can't use it
  (Discord can't gate component visibility — same as the `#settings` language selector); the handler
  enforces permission.

`ConnectionComponentModule` (Connections, `public`) handles it via
`[ComponentInteraction("workspace:info:swap:*")]` + `[RequireUserPermission(GuildPermission.ManageGuild)]`:
validate the chosen `credentialId` is in that server's pool (payloads are forgeable) →
`PromoteAsync` → `EnsureConnectionAsync` (restart on the new identity) → ephemeral confirmation. The
resulting status change publishes `ConnectionStatusChangedEvent`, refreshing `#info`.

**Cross-feature refresh:** the supervisor publishes `ConnectionStatusChangedEvent(GuildId, ServerId)`
— a new record in `Abstractions.Events`, beside `ServerRegisteredEvent` — whenever `ConnectionState`
actually changes; `WorkspaceHostedService` consumes it (alongside
`ServerRegisteredEvent`) and calls the idempotent `ReconcileServerAsync`, which also provisions the
category if a status event happens to arrive before the registration event (ordering-independent).

## 10. Owner notifications

Extract a minimal **`IUserDmSender`** into the `Discord` project (`SendAsync(userId, message)`,
swallows a closed DM, logs) and refactor 1b-i's `DiscordOwnerNotifier` onto it — so Connections can
DM a user when **their** credential is marked `Invalid` without a Connections→Pairing dependency. A
small, justified refactor of existing code. Connections owns its own message text ("Your Rust+
credential for **{server}** was rejected — reconnect in #setup to keep it in the pool."). Automatic
failover between still-valid credentials is otherwise silent.

## 11. Configuration

**`ConnectionOptions`** bound from section `"Connections"`, validated with
`AddOptions().Validate().ValidateOnStart()` (the standing pattern):

| Option | Default | Validation |
|---|---|---|
| `ConnectTimeout` | 30s | > 0 |
| `InitialRetryDelay` | 5s | > 0 |
| `MaxRetryDelay` | 5m | ≥ `InitialRetryDelay` |
| `HeartbeatInterval` | 60s | > 0 |
| `HeartbeatTimeout` | 10s | > 0 |

## 12. Error handling summary

| Surface | Behavior |
|---|---|
| No `Active` credential for a server | `NoCredentials`; no socket; `#info` shows it |
| Active token unreadable (`CryptographicException`) | mark that credential `Invalid`, DM owner, failover |
| Connect auth-rejected | mark `Invalid`, DM owner, promote next `Standby`; none → `NoCredentials` |
| Connect unreachable | `Unreachable`; keep credential; capped backoff retry |
| Heartbeat unreachable / timeout | `Unreachable`; reconnect with backoff |
| Heartbeat auth-rejected | failover path (rare; token revoked mid-session) |
| Faulting socket loop | broad-catch + log; other servers unaffected; host stays up |
| Swap: forged/unknown `credentialId` | rejected (validated against pool), ephemeral error |
| Swap by non-admin | rejected by `RequireUserPermission(ManageGuild)` |
| Redundant status write | `UpsertStatusAsync` reports "unchanged" → no event, no Discord edit |
| Secrets | protected at rest; never logged |

## 13. Testing strategy

New project `RustPlusBot.Features.Connections.Tests` (+ additions to `Persistence.Tests` /
`Features.Workspace.Tests`):

- **Supervisor** (via `FakeRustSocketSource`): connect success → `Connected` + player count;
  auth-reject → `Invalid` + DM + failover to next; all-invalid → `NoCredentials`; unreachable →
  backoff → recover; heartbeat-unreachable → reconnect; swap restarts on the new identity;
  restart-survival via `StartAllAsync` from a persisted `Active`; unreadable token → `Invalid`;
  status published only on change.
- **`IConnectionStore`** (SQLite fixture, like `Persistence.Tests`): Get/Upsert status (+ changed
  flag), `GetActiveCredentialAsync`, `ListPoolAsync`, `PromoteAsync` (flips statuses + pointer,
  rejects non-members), `MarkInvalidAsync`.
- **`ServerInfoMessageRenderer`:** embed reflects each `ConnectionStatus`; select lists the pool with
  the active default; `NoCredentials` hides player count; no `ServerId` → empty payload.
- **`ConnectionComponentModule`:** non-admin rejected; unknown `credentialId` rejected; valid swap →
  `Promote` + `EnsureConnection`.
- **Schema:** `LiveConnections` columns present (`ConnectionState` has `Status`/`PlayerCount`, no
  `IsHealthy`).
- **DI:** `AddConnections` registers source, supervisor (singleton), store (scoped), hosted service,
  and the module assembly.
- The real `RustPlusApi` adapter mirrors `RustPlusFcmPairingSourceTests` (construction/fallback only).

Build 0/0 under the strict analyzer set; run `dotnet jb cleanupcode RustPlusBot.slnx
--profile=ReformatAndReorder` (ReSharper, via `dotnet tool restore`) before pushing — CI's format
gate is `jb`, not `dotnet format`, and they disagree. Maintain coverage parity.

## 14. Explicit boundary — deferred to 1b-iii (and later)

- **IP-change re-identification** — an IP change still reads as a new server (1b-i carry-forward).
- **RustServer-removal FK/cascade** — cleaning a removed server's `PlayerCredential`s /
  `ConnectionState` (no user-facing server-removal trigger exists yet anyway).
- **Account disconnect/remove flow** — writing `FcmRegistrationStatus.Disabled`, a credential
  `SetStatus`, and stopping the corresponding listener/socket.
- **Entity** (smart device / camera) live data — subsystems 4 / 5.

## 15. Carry-forwards honored from 1a / 1b-i / foundation

- Per-guild isolation: every persisted row carries `GuildId`.
- `AddOptions().Validate().ValidateOnStart()` for `ConnectionOptions`.
- Hosted-service shape (broad-catch isolation, scope-per-unit-of-work, `Ready` guard) mirrors
  `WorkspaceHostedService` / `PairingHostedService`.
- One connection per identity, bounded connect + capped backoff, `IAsyncDisposable` — mirrors
  `PairingSupervisor`.
- Secrets protected at rest via `ICredentialProtector`, never logged.
- Render-here / handle-there component seam via a public `WorkspaceComponentIds` constant.
