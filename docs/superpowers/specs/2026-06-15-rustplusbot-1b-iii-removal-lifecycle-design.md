# Subsystem 1b-iii — Server removal & account disconnect (design)

**Status:** Approved (brainstorming) · **Created:** 2026-06-15 · **Branch:** `feat/removal-lifecycle` (off `develop`)
**Predecessor:** 1b-ii live connection (PR #6, merged `e035493`)
**Catalog rows:** "Credential show/remove" (C), "RustServer-removal FK/cascade cleanup" (B) · roadmap row **1b-iii**

---

## 1. Scope

This round delivers **B + C + the connection-state cascade** from the 1b-iii bundle:

- **B — Server removal flow:** an admin can permanently remove a registered server; this stops its
  live socket, deletes the `RustServer` (cascading its pooled credentials *and* its connection-state
  row), and tears down its Discord category/channels/records.
- **C — Account disconnect flow (full account removal):** a user can disconnect their account, which
  stops their FCM listener, marks their `FcmRegistration` **Disabled**, and removes **all** their
  pooled player-credentials in that guild — failing over any server where they were the active player.
- **Cascade:** add the `ConnectionState → RustServer` FK with `OnDelete(Cascade)` so server removal
  leaves no orphaned status row (the `PlayerCredential` cascade already exists from 1b-i).

### Explicitly deferred (carry-forwards → a future 1b-iv)

- **A — IP-change re-identification.** The FCM `ServerEvent` we map exposes only Name/Ip/Port/PlayerId/
  PlayerToken — **no stable server id** — so "same server, new IP" has no reliable key. rustplusplus
  itself keys by ip+port and does not re-identify.
- **D — Live auth-reject verification.** `RustPlusSocketSource` maps both connect-failure and
  heartbeat-non-success to `Unreachable`; distinguishing a real auth reject needs a **live Rust
  server** to observe the error shape. Until then a dead token loops as `Unreachable` and never fails
  over. (Account disconnect's failover path in C is unaffected — it is driven by credential *removal*,
  not by socket auth detection.)

---

## 2. Background (current state, verified)

- **Dependency graph:** `Connections → Workspace`, `Pairing → Workspace`; Workspace depends on neither.
  Cross-feature coordination today is the in-process **event bus** (`ServerRegisteredEvent`,
  `ConnectionStatusChangedEvent`), which **fans out per-subscriber**. Host references everything.
- **Cascade:** `PlayerCredentialConfiguration` already has `HasOne<RustServer>().WithMany()
  .OnDelete(Cascade)` (migration `PairingCredentials`). `ConnectionStateConfiguration` has **only**
  `HasKey(RustServerId)` — no FK → removing a server would orphan the connection-state row.
- **Failover is removal-friendly:** `ConnectionStore.GetActiveCredentialAsync` selects by
  `Status == CredentialStatus.Active` (not by `ConnectionState.ActiveCredentialId`). So deleting the
  active `PlayerCredential` leaves no Active row, and `ConnectionSupervisor.PrepareAsync` promotes the
  next `Standby` (or returns null → `NoCredentials`). No connection-store change is needed for failover.
- **No removal UI exists.** `IServerService.RemoveAsync` is never called. `WorkspaceTeardownService
  .RemoveServerAsync` only deletes the Discord category/channels/records — it does not stop the socket
  or delete the `RustServer` row. `IWorkspaceTeardownService` is **internal** (only the dev
  `/workspace reset` uses `ResetGuildAsync`).
- **No `/credentials` UI exists.** Only the `#setup` "Connect account" button (`WorkspaceComponentIds
  .ConnectAccount`), rendered by Workspace's `SetupMessageRenderer`, handled by Pairing's
  `CredentialModule`. The `#info` swap select (`ServerInfoSwapPrefix`) is rendered by Workspace's
  `ServerInfoMessageRenderer`, handled by Connections' `ConnectionComponentModule`. This shared
  public-custom-id pattern is the model the new buttons follow.
- **FCM `Disabled` already honored:** `PairingSupervisor.EnsureListenerAsync` refuses to start a
  `Disabled` registration; `IFcmRegistrationStore.SetStatusAsync` can write it. `Disabled` is the
  read-but-never-written loose end this round closes.
- **Ephemeral interaction responses are English** across every existing module (e.g.
  `CredentialModule`, `ConnectionComponentModule`); only rendered channel messages are localized.

---

## 3. Architecture decision — coordination model (hybrid)

| Flow | Coordination | Why |
|---|---|---|
| **Server removal (B)** | **Synchronous orchestrator** | Ordering is load-bearing: the socket must stop **before** the row is deleted, or a late `NoCredentials` status write would re-insert a `ConnectionState` row pointing at a deleted `RustServer` and trip the new FK; a stray reconcile could also re-touch a torn-down category. A single ordered method is race-free and gives the admin an immediate definitive result. |
| **Account disconnect (C)** | **Event-driven side-effects** | Pairing owns the FCM registration + credentials but **cannot reach Connections** (no project ref). Failover + `#info` refresh are natural fan-out, exactly what the bus provides. |

Rejected alternatives: *fully event-driven removal* (races above; FK violations), *Host-level
orchestrator* (behavior in the composition root; still needs public seams).

---

## 4. Data model & migration

- **`ConnectionStateConfiguration`:** add
  `builder.HasOne<RustServer>().WithOne().HasForeignKey<ConnectionState>(s => s.RustServerId)
  .OnDelete(DeleteBehavior.Cascade);`. `RustServerId` is already the PK, so no extra index.
- **Migration `ServerRemovalCascade`:** adds `FK_ConnectionStates_RustServers_RustServerId`
  (`onDelete: Cascade`). Mirrors the `PairingCredentials` cascade shape. Verify **no EF model drift**
  after generating (`BotDbContextModelSnapshot` updated).
- No new entities, columns, or enum values (the `Disabled` status and credential statuses exist).

---

## 5. Server removal flow (B)

### Removal — components

- **`IServerWorkspaceRemover`** — new **public** interface in Workspace exposing only
  `Task RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken)`. Implemented by the
  existing `WorkspaceTeardownService` (which already has this method). `IWorkspaceTeardownService`
  and `ResetGuildAsync` stay internal.
- **`IServerRemovalService` / `ServerRemovalService`** — new, **in Connections** (the only feature
  besides Host that can reach `IConnectionSupervisor` *and* a Workspace seam, and it already owns
  `#info` components). Single method `RemoveServerAsync(guildId, serverId, ct)` that runs, in order:
  1. `IConnectionSupervisor.StopAsync(guildId, serverId)` — stop the socket, remove the handle, no
     further status writes.
  2. `IServerService.RemoveAsync(guildId, serverId)` — delete the `RustServer`; DB cascade removes its
     `PlayerCredential`s and (new) its `ConnectionState`.
  3. `IServerWorkspaceRemover.RemoveServerAsync(guildId, serverId)` — delete the Discord category,
     channels, and provisioning records under the per-guild lock.
  Returns whether a server was actually removed (false if `RemoveAsync` found nothing).

### Removal — UI

- **Rendered button:** `ServerInfoMessageRenderer` adds a **"Remove server"** button (Danger style)
  to the `#info` message, custom id `WorkspaceComponentIds.ServerInfoRemovePrefix + serverId`
  (new public const, parallel to `ServerInfoSwapPrefix`).
- **Handler (Connections module):** `[ComponentInteraction(ServerInfoRemovePrefix + "*")]`,
  `[RequireUserPermission(GuildPermission.ManageGuild)]`. Click → ephemeral **two-step confirm**
  ("This permanently deletes the category, all channels, and stored credentials for **{server}**.")
  with **Confirm / Cancel** buttons whose custom id encodes the serverId (private Connections const).
  Confirm → `ServerRemovalService.RemoveServerAsync` → ephemeral result. The module stays thin; all
  logic is in the service. Self-heal does not fight this (row + records are gone before teardown
  returns).

---

## 6. Account disconnect flow (C) — full account removal

### Disconnect — components

- **`ICredentialStore`** (Abstractions) gains:
  - `Task<IReadOnlyList<Guid>> RemoveForOwnerAsync(ulong guildId, ulong ownerUserId, CancellationToken)`
    — deletes every `PlayerCredential` for `(guild, owner)`, returns the **distinct affected
    serverIds**.
  - `Task<IReadOnlyList<Guid>> ListServerIdsForOwnerAsync(ulong guildId, ulong ownerUserId,
    CancellationToken)` — read for the confirm "show" list. (Both return `Guid` only; Abstractions
    stays Domain-free.)
- **`IPairingSupervisor`** gains `Task StopListenerAsync(ulong guildId, ulong ownerUserId)` (promote
  the existing private method to the interface).
- **`IAccountDisconnectService` / `AccountDisconnectService`** — new, **in Pairing**. Method
  `DisconnectAsync(guildId, ownerUserId, ct)`:
  1. `IPairingSupervisor.StopListenerAsync(guildId, ownerUserId)`.
  2. Resolve the registration (`IFcmRegistrationStore.GetAsync`); if present, `SetStatusAsync(id,
     Disabled)`. Idempotent/no-op when absent.
  3. `ICredentialStore.RemoveForOwnerAsync(guildId, ownerUserId)` → affected serverIds.
  4. For each affected serverId, publish `ServerCredentialsChangedEvent(guildId, serverId)`.
  Returns a small result (e.g. affected server count) for the ephemeral message.
  A server emptied this way becomes `NoCredentials` — **never auto-removed**.

### Disconnect — UI

- **Rendered button:** `SetupMessageRenderer` adds a **"Disconnect account"** button (Danger style)
  to `#setup`, custom id `WorkspaceComponentIds.DisconnectAccount` (new public const).
- **Handler (Pairing's `CredentialModule`, which already owns the `#setup` Connect button):**
  `[ComponentInteraction(DisconnectAccount)]`. Click → ephemeral **confirm that lists the affected
  servers by name** (resolve `ListServerIdsForOwnerAsync` → `IServerService.GetAsync`; doubles as the
  "show"). If the user has no registration and no credentials → "You're not connected." Confirm →
  `AccountDisconnectService.DisconnectAsync` → ephemeral result. Reconnecting later just re-runs the
  existing Connect-account modal (the FCM upsert flips the registration back to `Active`).

---

## 7. New event & cross-feature wiring

- **`ServerCredentialsChangedEvent(ulong GuildId, Guid ServerId)`** — new record in Abstractions/Events.
- **Publisher:** Pairing `AccountDisconnectService`, once per affected server.
- **Consumers (bus fans out per-subscriber):**
  - **Connections** `ConnectionHostedService` — add a second subscription alongside
    `ServerRegisteredEvent`: on the event → `IConnectionSupervisor.EnsureConnectionAsync(guild,
    server)` (restarts the loop → re-prepares → fails over to a Standby or → `NoCredentials`).
  - **Workspace** `WorkspaceHostedService` — add a third subscription alongside
    `ServerRegisteredEvent`/`ConnectionStatusChangedEvent`: on the event →
    `IWorkspaceReconciler.ReconcileServerAsync(guild, server)` (refreshes the `#info` embed + swap
    select to the new pool).

---

## 8. Rendering & localization

- `WorkspaceComponentIds` gains `DisconnectAccount` and `ServerInfoRemovePrefix`.
- New EN/FR localizer keys for the two **button labels** (rendered path — the established i18n seam):
  e.g. `setup.disconnect.button`, `server.info.remove.button`. Confirm/result strings live in the
  modules.
- **Conscious limitation:** ephemeral **confirm/result** text stays English, matching every existing
  interaction module. Localizing it would require giving modules a localizer + per-guild culture
  lookup (new ground) — left as a possible later sweep across all modules.

---

## 9. Testing

Interaction modules stay thin and untested (repo precedent); all logic lives in services/stores.

- **Persistence.Tests:** `ConnectionState` cascade on `RustServer` delete (state row gone);
  `RemoveForOwnerAsync` (removes the owner's creds across servers, returns distinct serverIds, leaves
  other owners' creds); `ListServerIdsForOwnerAsync`.
- **Connections.Tests:** `ServerRemovalService` — calls stop → remove → workspace-remove in order
  (fakes), returns false when the server is absent; `ConnectionHostedService` consumes
  `ServerCredentialsChangedEvent` → `EnsureConnectionAsync`.
- **Pairing.Tests:** `AccountDisconnectService` — listener stopped, status set `Disabled`, creds
  removed, one event published per affected server, idempotent when the user is not connected;
  `IPairingSupervisor.StopListenerAsync`.
- **Workspace.Tests:** `SetupMessageRenderer` emits the Disconnect button; `ServerInfoMessageRenderer`
  emits the Remove-server button; `WorkspaceHostedService` consumes `ServerCredentialsChangedEvent` →
  `ReconcileServerAsync`; EN/FR keys resolve.

---

## 10. Process / conventions

- Build subagent-driven (per-task TDD + spec-review + code-quality-review, then a whole-feature review),
  matching 1b-i/1b-ii.
- Run `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
  (ReSharper) before pushing — the CI format gate is `jb`, not `dotnet format`; the pre-push hook also
  enforces it.
- Mock internal interfaces with NSubstitute via the existing `DynamicProxyGenAssembly2` InternalsVisibleTo.
- Spec + plan are gitignored under `docs/superpowers/` (not committed), per project convention.

---

## 11. Out of scope

- IP-change re-identification (A), live auth-reject verification (D) — deferred to 1b-iv.
- Granular per-server credential removal, `/credentials show` as a standalone command, and any
  localization of ephemeral responses.
