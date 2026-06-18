# RustPlusBot

A self-hosted Discord bot for the Rust+ companion app.

## Status

The bot is live: it pairs with Rust+ over FCM, holds a socket per server, and
auto-provisions its own Discord channels. Pairing/connection, the chat bridge,
in-game `!commands` and slash surfaces, live map events, and map rendering are all
shipped. Smart devices and cameras are next.

## Features

### Onboarding & connection

- **Self-provisioning workspace** — `/setup` (Manage Server) creates a global
  `RustPlusBot` category (`#information`, `#setup`, `#settings`) plus one category
  per paired server. A declarative reconciler keeps channels/messages converged,
  self-heals deleted channels, and is idempotent under a per-guild lock.
- **FCM pairing** — connect a Rust+ account from `#setup`; pairing a server in-game
  auto-registers it (servers are never typed by hand) and the bot provisions that
  server's channels.
- **Credential pool per server** — multiple accounts can pair with the same server;
  the bot keeps one live socket per `(guild, server)` with **hot-swap** of the
  active player (select menu on `#info`) and **auto-failover** to a standby
  credential when one goes invalid (owner is DM'd).
- **Lifecycle controls** — "Remove server" (on `#info`) and "Disconnect account"
  (on `#setup`), both Manage-Server-gated with a confirmation step; cascade
  cleanup of channels, credentials, and live state.
- **Live status** — `#info` shows connection status, player count, and a team
  summary (online/total + leader), refreshed on connection-state changes.
- **Bilingual** — every provisioned surface renders in **English or French**,
  switchable from a select menu in `#settings`.

### Team chat bridge

- Two-way relay between in-game team chat and a per-server `#teamchat` channel
  (via a managed webhook), with echo/loop suppression.

### In-game `!commands`

Run in team chat by any teammate; replies in the guild's language with a
configurable per-server prefix (default `!`) and per-command cooldowns:

- **Server** — `!pop`, `!time`, `!wipe`
- **Team** — `!online`, `!offline`, `!team`, `!alive`, `!steamid [name]`, `!prox [name]`
- **Live events** — `!cargo`, `!heli`, `!chinook`, `!small`, `!large`, `!events`
- **Control** — `!mute` / `!unmute` (gate all bot→game output)

### Slash commands

- **Server data** (ephemeral, with a `server` selector when several are paired) —
  `/pop`, `/time`, `/wipe`, `/online`, `/offline`, `/team`, `/alive`, `/small`, `/large`
- **Utility** — `/help`, `/uptime`, `/leader` (Manage Server — transfer in-game team leadership)
- **Admin** — `/setup`, `/workspace reset`, `/workspace simulate-server`

### Live map events

- Per-server `#events` feed (and an in-game team-chat mirror) for **Cargo Ship**,
  **Patrol Helicopter**, and **Chinook (CH47)** entering/leaving, plus **small /
  large oil rig** activation, "crate lootable", and respawn — derived from
  polling the Rust+ map markers and monuments.

### Map rendering

- Per-server `#map` channel renders the live game map as an image with toggleable
  layers — **Grid**, **Markers**, **Monuments**, **Vendor**, **Players**, **Rigs** —
  controlled from a Manage-Server-gated control message.

### Foundation

- Multi-guild, self-hosted, per-guild isolation everywhere; SQLite persistence;
  credentials protected at rest; fail-fast token validation on startup.

## Tech stack

| Component | Library / version |
| --- | --- |
| Runtime | .NET 10 |
| Discord gateway | Discord.Net 3.20 |
| Rust+ client | RustPlusApi (prerelease) |
| Discord persistence | Persistord (prerelease, EF Core 10) |
| Local database | EF Core 10 / SQLite |

## Build & test

```bash
dotnet build RustPlusBot.slnx
```

```bash
dotnet test RustPlusBot.slnx
```

## Run locally

See [docs/development/running-locally.md](docs/development/running-locally.md).

## Project layout

| Project | Responsibility |
| --- | --- |
| `RustPlusBot.Abstractions` | Shared seams: `IClock`, `IEventBus`, credential/connection contracts, events |
| `RustPlusBot.Domain` | Entities and enums |
| `RustPlusBot.Persistence` | `BotDbContext`, EF Core services, SQLite migrations |
| `RustPlusBot.Discord` | Discord.Net gateway, hosted service, interaction modules |
| `RustPlusBot.Features.Workspace` | Channel/message provisioning, reconciler, `#info`/`#setup`/`#settings` surfaces |
| `RustPlusBot.Features.Pairing` | FCM pairing listener, credential intake, account disconnect |
| `RustPlusBot.Features.Connections` | Live socket supervisor, hot-swap/failover, Rust+ query seam |
| `RustPlusBot.Features.Chat` | Two-way `#teamchat` ↔ in-game chat bridge |
| `RustPlusBot.Features.Commands` | In-game `!commands`, slash surfaces, `/help`/`/leader` |
| `RustPlusBot.Features.Events` | Live map-event classification + `#events` feed |
| `RustPlusBot.Features.Map` | Map image rendering with toggleable layers |
| `RustPlusBot.Host` | Generic Host entry point, DI wiring, startup validation |

## Roadmap

Subsystems are built in order; each has its own spec → plan → build cycle.

| # | Subsystem | Status |
| --- | --- | --- |
| 0 | Foundation (bootable host, config/persistence, workspace provisioning) | Done |
| 1 | Pairing & connection (FCM, credential pool, hot-swap, auto-failover) | Done |
| 2 | Map + live events (events feed, oil-rig detection, map render + layers) | Done |
| 3 | Chat bridge + `!commands` + slash surfaces | Done |
| 4 | Smart devices | Planned |
| 5 | Cameras | Planned |

## License

MIT — see [LICENSE](LICENSE).
