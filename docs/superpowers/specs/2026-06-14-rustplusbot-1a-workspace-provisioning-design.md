# RustPlusBot — Subsystem 1a: Discord Workspace Provisioning & Configuration

**Date:** 2026-06-14
**Status:** Approved design (subsystem 1a)
**Scope:** The bot owns the Discord categories, channels, and messages it needs for display and
configuration. It provisions them on demand, renders configuration/display via in-channel
components, and keeps everything converged against drift. **No Rust+ connection, FCM, or
credentials** — those belong to subsystem 1b.

---

## 1. Context & why this reshapes the roadmap

The foundation (subsystem 0, PR #1) shipped a bootable host, persistence (Persistord
`DiscordDbContext` + Rust schema + `InitialCreate`), an in-process event bus, credential storage
protected at rest, and two slash commands: `/server add|list|remove` and `/bind <feature> <channel>`.

Those two commands are **wrong for this product** and are removed here:

- **`/server` is irrelevant.** A user connects their FCM credentials and the bot **listens for
  in-game server pairing itself** — servers auto-register; they are never typed in by hand.
- **`/bind` is irrelevant.** Rather than asking an admin to wire channels to features, the bot
  **provisions its own categories, channels, and messages** — a built-in, managed Discord workspace.

The reference bot ([rustplusplus](https://github.com/alexemanuelol/rustplusplus)) provisions a single
shared `rustplusplus` category with a fixed channel set and a **single active server** at a time. This
bot's foundation already supports **one live socket per `(guild, server)`** — multiple servers live at
once — so the workspace is laid out **one category per registered server**, which is both the natural
fit and a clear improvement over the reference.

### Subsystem split

The reshaped subsystem #1 is split into two specs, built in order:

- **1a (this spec)** — Discord workspace provisioning & configuration. Fully testable with **no real
  Rust+ connection**.
- **1b (later)** — credential connect + FCM pairing → real server registration + live connection
  lifecycle + hot-swap/failover. Plugs into 1a's provisioner.

---

## 2. Decisions (settled during brainstorming)

| Topic | Decision |
| --- | --- |
| Workspace topology | **One category per registered server**, plus one **global** `RustPlusBot` category for bot-wide config. |
| Bootstrap | **Nothing on guild join.** An admin runs `/setup` to provision the global channels. |
| Credentials | **Not required early; set anytime.** Credential entry is deferred to 1b (a button contributed into `# setup`). |
| Channel catalog | **Grow per subsystem.** 1a provisions only what it can populate now (global channels + a server `# info`). Later subsystems register their own channels. |
| Provisioning engine | **Declarative desired-state reconciler** (Approach A) — a registry of specs + a converge function. Idempotent, self-healing. |
| Config/display surface | **In-channel component messages** (buttons / select-menus for config; embeds for display), anchored and edited in place. |
| Run-twice guard | Structural idempotency (resolve → adopt → create) **plus** a per-guild provisioning lock. |
| Dev erase | A confirm-gated `/workspace reset`, gated behind a development config flag. |
| i18n | Resource-based localized strings keyed by per-guild `Culture`; **EN first, FR structured**. Landed in 1a. |

---

## 3. Provisioning engine — approaches considered

- **A — Declarative desired-state reconciler (chosen).** The desired workspace is described as data
  (`ChannelSpec` / `MessageSpec` contributed by each subsystem). A `WorkspaceReconciler` diffs desired
  (registry) against actual (stored snowflakes + live Discord) and creates/repairs/rebinds to converge.
  `/setup`, startup, the `ServerRegistered` event, and `ChannelDeleted` self-heal all run the same
  converge path. Idempotent by construction; the "grow per subsystem" seam is first-class; testable
  without Discord via a gateway fake.
- **B — Imperative step-by-step provisioner.** A sequence of `EnsureChannelExists(...)` calls (the
  reference's `addTextChannel` style). Least code, but repair logic scatters and drift handling is
  ad-hoc — the model where the reference left `setName` disabled with "halts the entire application…
  too lazy to fix."
- **C — Hybrid** (declarative channels, imperative messages). Splits the idempotency story across two
  mental models.

**Chosen: A.** It is the "better than rustplusplus" core and makes the registry seam — which the
"grow per subsystem" decision requires — first-class rather than an afterthought.

---

## 4. Architecture

### 4.1 Project placement

A new feature project, following the foundation's per-subsystem layout:

```text
RustPlusBot.Features.Workspace
  ├─ Registry     : IChannelSpec / IMessageSpec providers (the "grow per subsystem" seam)
  ├─ Reconciler   : desired-state diff & converge
  ├─ Gateway      : IWorkspaceGateway abstraction over Discord.Net (testable seam)
  └─ Modules      : /setup command, /workspace reset, settings components, global renderers
```

Depends only on `Abstractions`, `Domain`, `Persistence`, and `Discord` (for the gateway impl). Later
feature subsystems depend on `Workspace` to register their own specs.

### 4.2 Two desired-state scopes

- **Global scope** — one `RustPlusBot` category per guild: `# information`, `# setup`, `# settings`.
  1a fully owns and renders these.
- **Per-server scope** — one category per registered server (named after the server). In 1a this is
  driven by a **stub registration trigger**: an `IEventBus` `ServerRegistered` event fired manually
  (a temporary dev command or a test). 1a provisions the category + a `# info` channel holding a
  **static** server-identity message (name, host:port, paired-by, registered-at). 1b later fires the
  real event from FCM pairing and enriches `# info` with live status.

### 4.3 Components

```text
IWorkspaceRegistry
  - aggregates IChannelSpec / IMessageSpec contributed via DI (IEnumerable<…>)
  - 1a registers the global + per-server-info specs; later subsystems add theirs

ChannelSpec  { Scope (Global|PerServer), Key (stable string), NameKey (i18n),
               Kind (Text), PermissionProfile (ReadOnly|Interactive), Order }
MessageSpec  { Scope, Key, ChannelKey, Renderer (builds embed+components) }   // anchored, edited in place

IWorkspaceGateway                       // the testable seam over Discord.Net
  EnsureCategory / EnsureTextChannel / SetPermissions / FindChannelByName /
  EnsureAnchoredMessage / DeleteChannel / DeleteCategory
  // real impl wraps SocketGuild; tests use an in-memory fake → no Discord needed

WorkspaceReconciler
  ReconcileGlobal(guildId)
  ReconcileServer(guildId, serverId)
  // uses IWorkspaceStore for stored snowflakes; runs under the per-guild ProvisioningLock

IWorkspaceStore (Persistence)
  load/save ProvisionedCategory / ProvisionedChannel / ProvisionedMessage

WorkspaceTeardownService
  RemoveServer(guildId, serverId)   // delete that server's category+channels, clear records (1b unpair)
  ResetGuild(guildId)               // delete the whole workspace (the dev /workspace reset)

Discord modules
  SetupModule          → /setup            (ManageGuild) → ReconcileGlobal + reconcile known servers
  WorkspaceAdminModule → /workspace reset  (dev-flag + confirm button) → ResetGuild
  SettingsComponents   → settings channel select-menu/button handlers
```

**Single converge path.** Every trigger funnels through the reconciler, so idempotency holds
everywhere: `/setup`; bot **startup** (re-attach to existing workspaces, heal what was deleted while
offline); the `ServerRegistered` event (stub in 1a, FCM in 1b); and a `ChannelDeleted` gateway event
(self-heal the affected scope, debounced).

---

## 5. Data model

### 5.1 Removed (the "irrelevant" pieces)

`ChannelBinding` entity, `BoundFeature` enum, `BindingService`/`IBindingService`, `BindModule`
(`/bind`), and `ServerModule` (`/server add|list|remove`).

`RustServer` and `ServerService` **stay** — servers are still persisted, just created by the
registration trigger (stub in 1a, FCM in 1b) instead of a command. `ServerService.AddAsync/RemoveAsync`
are reused by the trigger and by teardown.

### 5.2 Added (provisioning records)

All guild-scoped. `RustServerId` **null = global scope**.

```text
ProvisionedCategory { Id, GuildId, RustServerId?, DiscordCategoryId, CreatedAt }
    unique (GuildId, RustServerId)                 -- one category per scope

ProvisionedChannel  { Id, GuildId, RustServerId?, ChannelKey, DiscordChannelId, CreatedAt }
    unique (GuildId, RustServerId, ChannelKey)     -- ChannelKey = spec Key: "information","setup","settings","info",…

ProvisionedMessage  { Id, GuildId, RustServerId?, MessageKey, DiscordChannelId, DiscordMessageId, CreatedAt, UpdatedAt }
    unique (GuildId, RustServerId, MessageKey)     -- anchored messages, edited in place
```

These are the reconciler's record of "what I created," keyed by the stable spec `Key` — so on restart
it re-attaches by snowflake, and on drift it knows exactly what is missing. Snowflakes are stored as
`ulong` (Persistord's global `ulong↔long` handling applies), consistent with the removed
`ChannelBinding.ChannelId`.

Entities live in `Domain/Workspace/`; EF configurations in `Persistence/Configurations`; the store in
`Persistence/Workspace`.

### 5.3 GuildSettings & migration

`GuildSettings.Culture` **already exists** (BCP-47 string, default `"en"`) and is reused as-is to drive
the i18n language selector. No new column is needed; only the i18n seam that consumes it (§7) is new.

One new EF migration `WorkspaceProvisioning`: drop `ChannelBindings`; create the three tables. Tests
apply migrations (per the `fix: apply migrations in tests` commit).

**FK / cascade:** `ProvisionedCategory/Channel/Message.RustServerId` → `RustServer` with **cascade
delete**, so removing a server (teardown / 1b unpair) cleans its provisioning rows automatically.

---

## 6. Reconcile flow

`ReconcileScope(guildId, serverId?)`, always under the per-guild `ProvisioningLock`:

1. **Ensure category.** Stored snowflake resolves to a live category → reuse. Else **adopt** a
   category found by expected name (`RustPlusBot` or the server name) and re-bind the record. Else
   create. Save the record.
2. **Ensure channels.** For each `ChannelSpec` of this scope (ordered by `Order`): stored snowflake →
   reuse; else adopt a same-named child of the category; else create under it. Apply the spec's
   `PermissionProfile` and parent. Save/refresh the `ProvisionedChannel`. Records whose `ChannelKey`
   is no longer in the registry are **left untouched and logged**, never deleted (a removed feature
   must not nuke history).
3. **Ensure messages.** For each `MessageSpec`: the anchored `ProvisionedMessage` still exists → **edit
   in place** with the renderer's current embed+components; else post fresh and store the new id.

Every step is **resolve → adopt → create**, so a second `/setup`, a startup reconcile, and a self-heal
all converge to the same state with **no duplicates** — the run-twice guard is structural, not a flag
check.

### Triggers

- **`/setup`** → `ReconcileGlobal` + `ReconcileServer` for every known `RustServer` in the guild;
  ephemeral progress reply.
- **Startup** → for each guild with a `ProvisionedCategory`, reconcile all scopes.
- **`ServerRegistered`** (stub 1a / FCM 1b) → create the `RustServer` row if new, then
  `ReconcileServer`.
- **`ChannelDeleted`** → map the deleted snowflake to one of ours; if matched, reconcile that scope to
  recreate it (debounced so a bulk delete does not thrash).

### Teardown

- `RemoveServer(guild, server)` → delete that server's channels + category, cascade-clear records
  (used by 1b on unpair).
- `ResetGuild(guild)` → delete all provisioned categories/channels for the guild, clear all records.
  Backs `/workspace reset` (dev-flag + confirm button); runs under the same lock so it cannot race a
  reconcile.

---

## 7. Global channels, settings & i18n

**Rendered by 1a** (anchored, edited-in-place messages):

- **`# information`** — a status/help embed: what the bot is, how to connect an account (points to
  `# setup`), how to pair a server in-game, docs link, and a live "servers registered: N" line from
  the `RustServer` count. Read-only.
- **`# setup`** — an instructional "how to connect your Rust+ account" anchor message. The interactive
  **Connect account** button + ephemeral credential modal is **contributed by 1b** via the registry;
  in 1a the channel exists with instructions only. (Clean application of the "grow per subsystem"
  seam.)
- **`# settings`** — a settings message whose first control is a **language select-menu (EN/FR)** →
  persists `GuildSettings.Culture`. Later subsystems contribute their own controls into this channel
  via `MessageSpec`s.

**i18n (1a lands the seam).** Resource-based localized strings keyed by per-guild `Culture` (EN first,
FR structured). Channel names, embed copy, and button labels resolve through it. Changing the language
**re-renders message content immediately**; channel **names** are localized at creation and refreshed
best-effort on the next reconcile — never live-renamed mid-session (the rate-limit trap that made the
reference disable `setName`). Names are reconcile-time, not interaction-time.

---

## 8. Permissions

**Channel permission profiles** (set on the category; channels sync):

- **ReadOnly (display):** `@everyone` ViewChannel = allow, SendMessages = deny; the bot can
  send/manage. **All 1a channels use this** — members interact via buttons/select-menus, never by
  typing.
- **Interactive:** reserved for later channels where members type (team-chat input, `!commands`) →
  SendMessages = allow.

**Required bot guild permissions** — Manage Channels, Manage Roles, Send Messages, Embed Links, Manage
Messages, Read Message History, View Channels. `/setup` **pre-flight-checks** these and returns a clear
ephemeral error listing what is missing, rather than half-provisioning (a robustness win over the
reference's silent `catch { /* Ignore */ }`).

**Command gating** — `/setup` and `/workspace reset` require `[RequireUserPermission(ManageGuild)]` +
`[RequireBotPermission(...)]`; reset additionally requires the development config flag and a confirm
button.

---

## 9. Testing strategy

The `IWorkspaceGateway` seam makes the engine unit-testable with **no Discord connection** (xUnit +
NSubstitute, the existing stack):

- **Reconciler (core)** against an in-memory gateway fake: fresh provision; **idempotent re-run → zero
  duplicates**; **adopt-by-name** (stored snowflake gone, same-named channel exists → rebind);
  **self-heal** (channel deleted → recreated); spec removed from registry → record retained, not
  deleted; message **edit-in-place vs repost**; `Order` honored; permission profile applied; bot-perms
  pre-flight failure path.
- **Concurrency:** two concurrent reconciles under `ProvisioningLock` converge once.
- **Registry aggregation:** specs from multiple DI providers aggregate (simulate a later subsystem by
  registering an extra spec provider).
- **Persistence** against SQLite (`SqliteContextFixture` pattern): unique constraints; cascade-delete
  of provisioning rows on `RustServer` removal; `WorkspaceProvisioning` migration applies cleanly.
- **Teardown:** `ResetGuild` / `RemoveServer` delete + clear.
- **i18n:** localizer resolves EN/FR; channel name uses guild culture.
- **Manual/smoke only** (interactive, not automated): the real Discord.Net gateway impl against a test
  guild.

---

## 10. Success criteria

- `/setup` builds the global workspace with correct content and permissions.
- Re-running `/setup` converges with **no duplicates**; concurrent runs are serialized.
- Deleting a provisioned channel self-heals (via `ChannelDeleted` or the next reconcile).
- Firing the stub `ServerRegistered` event creates a server category + a static `# info` message.
- `/workspace reset` (dev) removes everything and clears records.
- The language selector switches rendered content language.
- All of the above proven by reconciler unit tests against the gateway fake (no Discord needed).

---

## 11. Explicit boundary — deferred to 1b

- Connect-account button + ephemeral credential modal + encrypted storage (reuses the foundation's
  `CredentialStore` / `ICredentialProtector`).
- The FCM pairing listener that fires **real** `ServerRegistered` events; entity pairing (switches /
  alarms / storage) registering later subsystems' channels.
- Live socket lifecycle, hot-swap active identity, auto-failover.
- Enriching `# info` with live server status (player count, time, map image).

---

## 12. Open questions

- Exact copy/layout of the `# information` and `# settings` anchor messages — refine during
  implementation against the renderers.
- Whether the `ChannelDeleted` self-heal should be on by default or behind a setting — default **on**,
  revisit if it proves noisy.
