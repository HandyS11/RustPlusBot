# RustPlusBot — Foundation & Cross-Cutting Architecture (Phase 1)

**Date:** 2026-06-14
**Status:** Approved design (foundation / subsystem 0)
**Scope:** This spec covers the bot **foundation** and the **cross-cutting architecture** every
feature subsystem inherits. It also records the decomposition and phased roadmap for the full
product. Each later subsystem (chat, map, devices, cameras) gets its own spec → plan → build cycle.

---

## 1. Context & goal

RustPlusBot is a Discord bot that interacts with the [Rust+](https://rust.facepunch.com/companion)
companion service. The goal is to match the feature set of existing community bots
(reference: [rustplusplus](https://github.com/alexemanuelol/rustplusplus)) while doing it *better* —
cleaner UI (slash commands + components), real multi-player support, and multi-guild operation.

The project builds on three prerelease libraries authored by the same developer (expect to fix bugs
in them along the way):

- **RustPlusApi** — the Rust+ client and related packages (see §2).
- **Persistord** — a provider-agnostic, Discord-library-agnostic EF Core 10 persistence layer.
- **Discord.Net** — the Discord gateway/client library.

### Tech landscape (what the libraries actually provide)

**RustPlusApi (core `RustPlus` client):**

- Server data: `GetInfoAsync`, `GetTimeAsync`, `GetMapAsync`, `GetMapMarkersAsync`.
- Team: `GetTeamInfoAsync`, `GetTeamChatAsync`, `SendTeamMessageAsync`, `PromoteToLeaderAsync`,
  plus the `OnTeamChatReceived` event — the backbone of the chat bridge and `!command` system.
- Clan / Nexus: clan chat, MOTD, `GetNexusAuthAsync`.
- Smart devices: smart switch (get/set/toggle/strobe), storage monitor, alarm, subscriptions —
  with `OnSmartSwitchTriggered` / `OnStorageMonitorTriggered` events.
- Cameras: separate **RustPlusApi.Camera** package with a `CameraController` (keep-alive, PTZ /
  turret, `ShootAsync` / `ZoomAsync`) and **frame rendering to images via ImageSharp**.
- DI: `AddRustPlus` / `IRustPlusFactory`.

**RustPlusApi.Fcm** — listens for **pairing notifications** (server / smart switch / alarm / storage
monitor paired) and **alarm-triggered** pushes. DI: `AddRustPlusFcm` / `IRustPlusFcmFactory`.

**RustPlusApi.Fcm.Registration** — native credential acquisition (no Node.js). **Important
constraint:** the Steam login step (`SteamLoginService`) launches a **real Chrome/Chromium on the
same machine** via the DevTools protocol and injects a shim into Facepunch's own login page to
capture the token on a `localhost` listener. Token capture therefore *cannot* be done from a plain
remote website — browser security forbids injecting that shim into a cross-origin page. This is why
a hosted "log in with Steam" page is not straightforward and is deferred (see §9).

**Persistord** — models the **Discord** graph (guilds, channels, users, members, roles, messages,
history) with bit-faithful `ulong ↔ long` snowflake handling done globally, and a Discord.Net
adapter mapping gateway types to entities. It models *Discord*, **not Rust** — all Rust-domain state
is schema we design on top of its `DiscordDbContext`.

### Goals

- A long-running host that holds always-on Rust+ connections, reacts to their events, and fans
  output to Discord.
- Multi-guild, multi-server, multi-player from the foundation, with per-guild isolation.
- Clean seams so feature subsystems can be built independently.

### Non-goals (this phase)

- No feature subsystems implemented yet (chat/map/devices/cameras are later specs).
- No hosted credentials website (deferred; see §9).
- No multi-process / external broker (deferred; see §4).

---

## 2. Decisions (settled during brainstorming)

| Topic | Decision |
| --- | --- |
| Tenancy | **Self-hosted, multi-guild per instance**, with per-guild isolation everywhere. |
| Auth/connection model | **One live socket per `(guild, server)`**; many stored credentials per server; **hot-swap active identity** + **auto-failover**. |
| Onboarding (v1) | **Manual credential paste** via a `/setup` slash command. No HTTP ingress in v1. |
| Persistence | **SQLite by default**, provider-agnostic via Persistord (Postgres is a later drop-in). |
| Host | **.NET Generic Host** worker (single process). |
| Architecture | **Modular monolith, single process**, built with a disciplined in-process event backbone and bounded modules (Approach A with B's discipline — see §4). |
| Roadmap | Foundation → Pairing → Chat bridge → Map/events → Smart devices → Cameras. |

---

## 3. Architecture decision (approaches considered)

The core problem: one process must hold many always-on Rust+ sockets, react to their events, and
fan everything to Discord.

- **A — Modular monolith, single process.** Feature subsystems are projects in one solution;
  connections live in a hosted service; events flow over an in-process bus. Simplest to build and
  operate; fits self-hosted.
- **B — Modular monolith with a formal event backbone.** Same single process, but disciplined
  in-proc pub/sub + bounded modules so a subsystem could later be extracted. More future-friendly.
- **C — Multi-process (separate connection workers + broker).** Connection pool as its own service
  behind a queue. Future-proof for multi-tenant SaaS; heavy ops cost now.

**Chosen: A built with B's discipline.** One process, SQLite, but a clean in-process event backbone
and well-bounded feature modules. Fits "self-hosted multi-server" today; doesn't block the
"multi-tenant + credentials website" future; avoids distributed-systems cost now.

---

## 4. Foundation design

### 4.1 Solution & project layout

```text
RustPlusBot.Host            → Generic Host entrypoint, DI wiring, config, /setup command
RustPlusBot.Domain          → Rust-domain entities + enums (no EF, no Discord)
RustPlusBot.Persistence     → BotDbContext : DiscordDbContext (Persistord) + Rust schema + migrations
RustPlusBot.RustPlus        → Connection manager (the heart): credential pool + live sockets + FCM
RustPlusBot.Discord         → Discord.Net gateway, InteractionService, embeds/components
RustPlusBot.Abstractions    → seams: ICredentialProvider, IEventBus contracts, IClock
(later) RustPlusBot.Features.* → one project per subsystem (Chat, Map, Devices, Cameras)
```

Each unit has a single clear purpose, communicates through interfaces in `Abstractions`, and is
testable in isolation.

### 4.2 Persistence — Rust-domain schema

`BotDbContext : DiscordDbContext` (Persistord) supplies the Discord graph + snowflake handling. Rust
state layered on top, **all rows guild-scoped**:

- **RustServer** — `GuildId`, ip, port, name, paired-by Discord user. The connection target.
- **PlayerCredential** — first-class, **many per `(GuildId, RustServerId)`**: owning Discord user,
  Steam id, player token, FCM/Expo creds, and **status** (`Active` / `Standby` / `Invalid`).
  Protected at rest (see §4.6).
- **ConnectionState** — currently-active identity per server + last-known health, so the active
  identity survives restarts.
- **GuildSettings** — per-guild configuration (culture, defaults).
- **ChannelBinding** — maps a Discord channel to a feature (chat / events / devices / cameras).
- **PairedEntity** — smart switch / alarm / storage monitor (entityId, type, server), populated by
  the FCM pairing listener.
- **EventSubscription** — which map events / alerts a guild wants.

Use short-lived contexts via `AddDbContextFactory` (a bot is long-lived and concurrent; a
`DbContext` is neither thread-safe nor meant to live forever).

### 4.3 Connection management core (the heart)

`RustPlusConnectionManager` is a hosted service that owns:

- A **live socket per `(GuildId, RustServerId)`** (one active connection at a time), created via the
  library's `IRustPlusFactory`.
- That server's **credential pool** (the `PlayerCredential` rows), with active-identity selection.
- Per-connection **reconnect/backoff**, health tracking, and graceful shutdown.

Behaviors:

- **Event fan-out** — subscribes to each client's events (`OnTeamChatReceived`,
  `OnSmartSwitchTriggered`, …) and republishes them onto the in-process event bus (§4.5).
- **Hot-swap** — a command (`/identity switch @user` or by Steam id) gracefully tears down and
  reconnects under the new player token, emitting an `IdentityChanged` event so feature modules
  react.
- **Auto-failover** — on auth failure / repeated drops, the active credential is marked `Invalid`,
  the manager promotes the next `Standby` credential, reconnects, and announces the switch to the
  bound channel.

A parallel **FCM listener** (`IRustPlusFcmFactory`) handles pairing + alarm pushes and writes
`PairedEntity` rows.

The credential-pool + active-identity selection logic is its own well-bounded unit, testable against
**`RustPlusApi.MockServer`** (present in the library's test tree) with no real Rust server.

### 4.4 Discord interaction layer

Discord.Net with the **InteractionService**: slash commands + components (buttons / modals / select
menus) — where the "better UI" lives. Foundation ships:

- `/setup` — paste credentials → validate (connect once) → store as a `PlayerCredential`.
- `/server add | list | remove` — manage a guild's Rust servers.
- `/bind <feature> <channel>` — map a channel to a feature.
- `/identity switch | list` — manage the active player per server.

All commands are **guild-scoped**; per-guild isolation is enforced at the query layer. Feature
subsystems register their own command modules.

### 4.5 In-process event backbone

A lightweight `IEventBus` over `System.Threading.Channels` decouples producers (connection manager,
FCM listener) from consumers (feature modules). Keeps subsystems independently testable and
swappable to an external broker later (the B-discipline).

### 4.6 Cross-cutting concerns

- **Secrets** — player tokens / FCM creds protected at rest (ASP.NET DataProtection or a
  configurable key); guidance on file permissions for the SQLite DB.
- **Resilience** — per-connection backoff, circuit-breaking on repeated auth failures, structured
  logging (the libs accept `ILoggerFactory`).
- **i18n seam** — resource-based strings with per-guild culture (EN first; FR a near-term target —
  the maintainer is French). Cheap to seed now, painful to retrofit.
- **Testing** — xUnit + NSubstitute (already in `Directory.Packages.props`), plus
  **`RustPlusApi.MockServer`** to integration-test the connection layer without a real Rust server.

---

## 5. Roadmap (build order)

Each step is its own spec → plan → build cycle. **The `#` column is the stable subsystem id; rows
are listed in build order** — so chat (3) is built before map (2) deliberately.

| # | Subsystem | Delivers |
| --- | --- | --- |
| **0** | **Foundation** (this spec) | Host, DI, persistence + Rust schema, event bus, Discord interaction shell, cross-cutting. |
| **1** | **Pairing & connection** | `/setup` credential paste, FCM pairing listener, credential pool, hot-swap + auto-failover, live connection lifecycle. |
| **3** | **Chat bridge + `!commands`** | Team-chat ↔ Discord relay; in-game `!commands` (`!time`, `!pop`, `!wipe`, …). *Built first after pairing — exercises connections + event bus + command framework with minimal rendering, de-risking the rest.* |
| **2** | **Map + live events** | Rendered map image with markers; push notifications for cargo ship, patrol heli, locked crate, oil rig, etc. |
| **4** | **Smart devices** | Switches / alarms / storage monitors as Discord buttons + embeds, with push alerts on trigger. |
| **5** | **Cameras** | Live camera stills / PTZ & turret control posted into Discord via the Camera package. |

---

## 6. Testing strategy

- **Unit** — domain logic, credential-pool selection / failover policy, event-bus wiring
  (xUnit + NSubstitute).
- **Integration** — connection manager against `RustPlusApi.MockServer`; persistence against SQLite.
- **Manual** — `/setup` and live pairing require a real Steam login + in-game pairing (interactive,
  not automatable).

---

## 7. Risks & mitigations

- **Prerelease libraries may have bugs** — expect to patch RustPlusApi / Persistord; keep their
  source checkouts handy (`../RustPlusApi`, `../Persistord`).
- **Token/credential expiry** — mitigated by the credential pool + auto-failover.
- **Steam login is interactive & host-local** — accepted for v1 via manual paste; see §9.
- **Always-on socket churn** — backoff + circuit-breaking + health tracking in the connection core.

---

## 8. Open questions

- At-rest encryption mechanism for credentials (DataProtection vs configurable key) — decide in
  subsystem 1.
- Exact `!command` surface and map-event catalog — decide in their respective specs.

---

## 9. Future / deferred

- **Dedicated credentials website** — a separately-hosted site any user visits to obtain their
  credentials, plugging into the `ICredentialProvider` seam. **Caveat (discovered in §1):** because
  Facepunch's Steam login delivers its token via an in-page `ReactNativeWebView.postMessage` bridge
  that can only be captured by a browser the capturer controls (CDP shim injection on the same
  machine), this site cannot be a page the bot simply serves — it must be its own hosted capture
  flow (or a per-user local helper that uploads the result). This is *why* it is its own future
  project, not a quick add to the bot.
- **Concurrent live connections per server** — hold one live socket per authenticated player
  simultaneously (N sockets), enabling true per-user presence with no reconnect. Deferred in favor
  of the lighter one-active-socket model.
- **Multi-tenant / public hosting** — the per-guild isolation invested now keeps this open.
- **Postgres provider** — drop-in via Persistord when scale demands it.
