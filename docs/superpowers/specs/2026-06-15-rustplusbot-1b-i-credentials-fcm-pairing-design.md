# RustPlusBot — Subsystem 1b-i: Credential Intake & FCM Pairing → Server Registration

**Date:** 2026-06-15
**Status:** Approved design (subsystem 1b-i)
**Scope:** A guild member connects their Rust+ FCM credentials through a modal in `#setup`; the
bot runs a per-user FCM listener and, when that user pairs with a server in-game, **auto-registers
the server** (creating its `RustServer` row, provisioning its workspace via the existing 1a
reconciler) and stores the per-server player credential into a multi-owner pool. **No live Rust+
socket** — opening sockets, hot-swapping the active identity, auto-failover, and `#info`
live-status all belong to subsystem **1b-ii**.

---

## 1. Context & why 1b splits

The roadmap's subsystem 1b ("credentials + FCM pairing + live connection") is four distinct
chunks: (1) credential connect modal in `#setup`, (2) FCM pairing listener that fires the real
`ServerRegisteredEvent`, (3) live socket lifecycle with hot-swap + auto-failover, and (4) `#info`
live-status enrichment. There is a clean fault line after (2): chunks 1+2 take the system from "no
servers" to "pair in-game → server auto-registered → workspace provisioned → credentials stored" —
a fully demoable milestone with **no live socket**. Chunks 3+4 are the live-connection engine,
which is where the genuinely tricky concurrency lives.

This spec is **1b-i = chunks 1+2**. 1b-ii (chunks 3+4) gets its own spec → plan → build.

1a already built the seams this plugs into:

- `ServerRegisteredEvent` is consumed by `WorkspaceHostedService`; today it is only fired by the dev
  `simulate-server` command. 1b-i fires the **real** event from FCM pairing.
- `SetupMessageRenderer` notes "the interactive button arrives in 1b" — 1b-i adds the Connect button.
- The workspace reconciler is idempotent and per-guild-locked; provisioning a server is a no-op if
  already provisioned.

## 2. Confirmed flow (RustPlusApi)

1. A user generates an **FCM credentials JSON** by running `RustPlusApi.Fcm.Registration` **locally**
   (its CDP/Chrome shim cannot run server-side for a remote user) and pastes that blob into a modal
   in `#setup`.
2. The bot's FCM listener connects with those credentials and receives a **server-pairing
   notification** each time that user clicks "Pair with Server" in-game.
3. The pairing notification carries the server `Ip`/`Port`/name **plus** `PlayerId` (Steam64) and
   `PlayerToken`.

Consequences that shape the design:

- FCM credentials are **per-user**: one registration receives pairing notifications for *every*
  server that user pairs with, over time.
- `PlayerToken` + `SteamId` exist **only after a pairing arrives**, not at paste time.
- **Multiple users** register per guild, forming a **pool** of credentials per server; the active
  identity can later be hot-swapped (1b-ii).
- Credentials **expire**: FCM credentials can be rejected (user must reconnect), and a per-server
  player credential can go stale when the server becomes unreachable.

## 3. Decisions locked during brainstorming

1. **Scope:** build 1b-i (intake + pairing → registration) now; defer the live socket to 1b-ii.
2. **Data model:** split into two entities — a per-user `FcmRegistration` and a slimmed per-server
   `PlayerCredential` — instead of bundling the FCM blob onto a server-keyed credential row.
3. **Swap boundary:** 1b-i supports the pool in data (multiple owners per server, status, and the
   first pairing per server auto-marked `Active`); the swap **control** and the actual reconnect
   land in 1b-ii.
4. **Listener integration:** one long-lived listener per active `FcmRegistration`, behind an
   `IPairingSource` seam, driven by a dedicated hosted service (rejected: a single shared listener —
   FCM identities can't be multiplexed; manual pairing-paste import — UX regression).
5. **Notification on expiry:** DM the owner, falling back to a log line + the `Expired` state shown
   when they reopen the Connect modal.
6. **Connect permission:** any guild member may connect their **own** account; the active-identity
   swap is ManageGuild-gated in 1b-ii.
7. **Server identity:** `(GuildId, Ip, Port)`; an IP change reads as a new server (known limitation,
   carry-forward).
8. **Component seam:** minimal — Workspace exposes a public custom-id constant and renders the
   button; Pairing handles it. A general `IComponentContributor` registry seam is deferred until a
   second consumer exists (YAGNI).

## 4. Project structure

New feature project **`RustPlusBot.Features.Pairing`** (1b-ii's live socket later gets its own
`RustPlusBot.Features.Connections`). It contains:

- the credential-intake interaction module (button handler + modal handler);
- the `IPairingSource` seam and its `RustPlusApi.Fcm` adapter;
- `IPairingSupervisor` (singleton) — owns the live `owner → listener` map and the pairing pipeline;
- `PairingHostedService` — thin `IHostedService` that drives the supervisor from the host lifecycle;
- `IOwnerNotifier` seam (DM the owner) + its Discord-backed implementation;
- `PairingOptions`;
- `AddPairing()` service-collection extension, wired in `Program.cs` after `AddWorkspace()`.

Dependency direction: `Features.Pairing` → `Features.Workspace` (to read the public custom-id
constant) and → `Persistence`/`Abstractions`/`Domain`. Workspace never depends on Pairing.

## 5. Data model

### 5.1 `FcmRegistration` (new, per `(GuildId, OwnerUserId)`)

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | surrogate key |
| `GuildId` | `ulong` | owning guild |
| `OwnerUserId` | `ulong` | the Discord user |
| `ProtectedFcmCredentials` | `string` | the pasted blob, protected at rest |
| `Status` | `FcmRegistrationStatus` | `Active` / `Expired` / `Disabled` |
| `UpdatedAt` | `DateTimeOffset` | via `IClock` |

`FcmRegistrationStatus`: **`Active`** — listener should run; **`Expired`** — FCM rejected the creds,
needs a reconnect; **`Disabled`** — removed by the user (reserved; disconnect UI deferred).

Unique index on `(GuildId, OwnerUserId)` so reconnect **upserts** rather than duplicating.

### 5.2 `PlayerCredential` (slimmed, per `(GuildId, RustServerId, OwnerUserId)`)

- **Remove** `ProtectedFcmCredentials` (moved to `FcmRegistration`).
- Keep `Id`, `GuildId`, `RustServerId`, `OwnerUserId`, `SteamId`, `ProtectedPlayerToken`, `Status`
  (`Standby` / `Active` / `Invalid`).
- Replace the non-unique `(GuildId, RustServerId)` index with a **unique** index on
  `(GuildId, RustServerId, OwnerUserId)` so re-pairing the same server by the same owner upserts.

### 5.3 `RustServer`

- Add a **unique index** on `(GuildId, Ip, Port)` to back resolve-or-create and the dedup race.

### 5.4 Migration `PairingCredentials`

Single migration: drop `PlayerCredentials.ProtectedFcmCredentials`; add the `FcmRegistrations`
table; add the `RustServer (GuildId, Ip, Port)` unique index; swap the `PlayerCredential` index for
the unique 3-column one. (Pre-release database; no production data to preserve.)

## 6. Persistence surface

- **`IFcmRegistrationStore`** (new): `UpsertAsync(guildId, ownerUserId, protectedCredentials)` →
  sets `Active`, returns id; `ListActiveAsync()` (across guilds, for startup); `SetStatusAsync(id,
  status)`; `GetAsync(guildId, ownerUserId)`.
- **`ICredentialStore`** (reshaped): replace `StoreAsync(StoreCredentialRequest{… FcmCredentialsJson})`
  with a per-server **upsert** keyed by `(GuildId, RustServerId, OwnerUserId)` taking `SteamId` +
  `PlayerToken`. On insert: if it is the **first** credential for that server → `Active`, else
  `Standby`. On update: refresh the token and, if the row was `Invalid`, reset to `Standby`.
  `CountForServerAsync` stays. (A `SetStatusAsync(..., Invalid)` exists for 1b-ii to call.)
- **`IServerService`**: add `ResolveOrCreateByEndpointAsync(guildId, ownerUserId, name, ip, port)`
  returning `(RustServer server, bool created)`; read-then-write, catching the unique-index
  violation and re-reading on the race. Existing methods unchanged.

All token/credential material is protected via the existing `ICredentialProtector` before
persistence and is never logged.

## 7. Credential intake flow

1. **Button.** `SetupMessageRenderer` (Workspace) renders a "Connect / reconnect account" button on
   the `#setup` message using a public `WorkspaceComponentIds.ConnectAccount` constant. Button text
   is localized by Workspace (EN/FR), matching the existing localizer.
2. **Modal.** Pairing's `public` interaction module handles that custom id and responds with an
   **ephemeral modal**: one paragraph text input for the FCM credentials JSON. Owner is always
   `Context.User`; guild is `Context.Guild`. (Blob is well under Discord's 4000-char modal limit.)
3. **Submit** (`[ModalInteraction]`, scoped per interaction via `IServiceScopeFactory`,
   `DeferAsync(ephemeral)` first so we have time for the bounded connect):
   - **Parse-validate** the blob into the expected FCM shape. Bad input → ephemeral error pointing
     at how to generate the credentials, nothing stored.
   - **Upsert** the `FcmRegistration` (protected) → `Active`.
   - Call **`IPairingSupervisor.EnsureListenerAsync(guildId, ownerUserId, ProbeTimeout)`**, which
     starts (or restarts) the owner's long-lived listener and reports the **initial connect
     outcome** within `ProbeTimeout`. There is exactly **one** connection per FCM identity — the
     same listener serves both the immediate feedback and the ongoing listening, so we never open
     two concurrent connections for one identity.
   - Report ephemerally from the outcome: **Connected** → "Pair with a server in-game and it'll show
     up here automatically."; **Rejected** → the supervisor has already flipped the registration to
     `Expired`, so reply "these credentials were rejected"; **Timeout** (network) → optimistic
     "connected — still verifying", the listener keeps retrying in the background.
4. First-connect and reset/reconnect are the **same path** (upsert + `EnsureListenerAsync`).

**Deferred:** "Disconnect account" (remove registration + that owner's credentials) — reset is
covered by reconnect.

## 8. FCM pairing pipeline → server registration

### 8.1 `IPairingSource` seam

Abstracts one owner's FCM listener. Yields `PairingNotification` records carrying a `PairingKind`
(`Server` | `Entity`):

- *Server*: `ServerName`, `Ip`, `Port`, `PlayerId` (Steam64), `PlayerToken`.
- *Entity*: also `EntityId`, `EntityType`, `EntityName`.

Real implementation wraps `RustPlusApi.Fcm`; the **fake** lets tests enqueue notifications, so no
real FCM is needed in tests. The seam also distinguishes an **auth rejection** from a **transient**
failure (so the supervisor can decide between `Expired` and retry).

### 8.2 `IPairingSupervisor` (singleton) + `PairingHostedService`

`IPairingSupervisor` owns the live `ownerKey → (listener, CancellationTokenSource)` map:

- `EnsureListenerAsync(guildId, ownerUserId, probeTimeout)` → start (or restart) that owner's
  listener and return the initial connect outcome (`Connected` / `Rejected` / `Timeout`). Called by
  the modal handler for immediate feedback and by startup for each `Active` registration.
- **Auth rejection** for a listener → mark its registration `Expired`, stop the listener, and DM the
  owner via `IOwnerNotifier` ("your Rust+ credentials expired — reconnect in #setup"); DM failure →
  log only.
- **Transient error** → exponential backoff retry, capped (`PairingOptions`); no status change.
- Each listener is isolated (broad catch + log) — one faulting listener never crashes the host or
  the others.

`PairingHostedService` is a thin `IHostedService`: once the Discord client is ready it asks the
supervisor to start listeners for every `Active` registration (`IFcmRegistrationStore.ListActiveAsync`
→ `EnsureListenerAsync` each); on shutdown it tells the supervisor to cancel all listeners. This
mirrors `WorkspaceHostedService`'s lifecycle shape.

### 8.3 Pairing handler (per notification, in its own DI scope)

- **Server pairing:** `ResolveOrCreateByEndpointAsync(guildId, ownerUserId, name, ip, port)` →
  upsert the `PlayerCredential` for `(guild, server, owner)` with `SteamId` + `PlayerToken` (first
  credential for that server → `Active`, else `Standby`). **Only when the `RustServer` was newly
  created** publish `ServerRegisteredEvent(guildId, serverId)` → the 1a reconciler provisions the
  category (idempotent, so a stray re-fire is harmless either way).
- **Entity pairing:** ignored in 1b-i (debug-logged); deferred to subsystems 4 (smart devices) /
  5 (cameras).
- Broad catch + log per notification so a single malformed payload can't kill the listener loop.

## 9. Configuration

- **`PairingOptions`** bound from configuration section `"Pairing"`: `ProbeTimeout`, retry
  backoff/cap. Validated with `AddOptions().Validate().ValidateOnStart()` (the 1a carry-forward
  pattern). No global FCM secret — credentials are per-user, supplied via the modal.
- **Packages:** add `RustPlusApi.Fcm` and `RustPlusApi.Fcm.Extensions.DependencyInjection`
  `2.0.0-beta.1` to `Directory.Packages.props`. The core socket package (`RustPlusApi`) waits for
  1b-ii.

## 10. Error handling summary

| Surface | Behavior |
|---|---|
| Invalid FCM blob in modal | ephemeral parse error, nothing stored (validation precedes upsert) |
| Connect rejected during modal | registration flipped `Expired`; ephemeral "credentials rejected" |
| Connect timeout during modal (network) | registration stays `Active`; ephemeral optimistic; listener retries |
| Listener auth rejection (background) | registration → `Expired`, stop listener, DM owner |
| Listener transient error | exponential backoff retry, no status change |
| DM to owner fails | log; `Expired` shown when modal reopened |
| Malformed pairing notification | log + skip; loop continues |
| Concurrent same-server pairing | unique-index race handled by re-read |
| Secrets | protected at rest; never logged |

## 11. Testing strategy

New project `RustPlusBot.Features.Pairing.Tests` (+ additions to `Persistence.Tests`):

- **Pairing pipeline** (via `IPairingSource` fake): server notification → `RustServer` created,
  `PlayerCredential` upserted (first → `Active`), `ServerRegisteredEvent` fired **exactly once**;
  second owner pairing the same server → second credential `Standby`, **no** new event; entity
  notification → ignored.
- **Stores** (SQLite, like `Persistence.Tests`): `FcmRegistration` upsert/`SetStatus`/`ListActive`;
  `PlayerCredential` upsert + first→`Active` + re-pair refresh + `Invalid`→reset; `RustServer`
  resolve-or-create incl. the unique-violation race.
- **Modal handler:** good/bad blob parse; `EnsureListenerAsync` outcome (via a faked
  `IPairingSupervisor`) mapped to the correct ephemeral reply (`Connected`/`Rejected`/`Timeout`).
- **`IPairingSupervisor`:** `EnsureListenerAsync` returns the right outcome; auth rejection →
  `Expired` + DM via a faked `IOwnerNotifier`; transient → retry without status change. The thin
  `PairingHostedService` starts listeners for all `Active` regs on ready and cancels them on stop.

Build 0/0 under the strict analyzer set; maintain coverage parity with the existing projects.

## 12. Explicit boundary — deferred to 1b-ii (and later)

- Live Rust+ socket per `(guild, server)` using the `Active` credential; `ConnectionState` health +
  restart survival.
- Active-identity **swap UI** (ManageGuild) + reconnect under the new identity.
- **Auto-failover** to a `Standby` credential on rejection; marking credentials `Invalid`.
- **Server-unreachable detection** → expire the per-server credential (needs the socket).
- `#info` **live-status** enrichment (`ServerInfoMessageRenderer`).
- **IP-change** server re-identification.
- A general `IComponentContributor` registry seam — only if subsystems 2/4/5 need to contribute
  controls and a second consumer materializes.
- **Entity** pairings (switches/alarms/cameras) — subsystems 4 / 5.

## 13. Carry-forwards honored from 1a / foundation

- Per-guild isolation: every new row carries `GuildId`.
- `AddOptions().Validate().ValidateOnStart()` for `PairingOptions`.
- Read-then-write upsert with unique-violation handling (`RustServer` resolve-or-create).
- Hosted-service shape (broad-catch isolation, scope-per-unit-of-work) mirrors
  `WorkspaceHostedService`.
- FK/cascade when a `RustServer` is removed must clean its `PlayerCredential`s — verify the cascade
  is in place when adding the unique indexes (orphaned credentials hold secrets).
