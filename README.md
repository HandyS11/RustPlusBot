# RustPlusBot

A self-hosted Discord bot for the Rust+ companion app.

## Status

**Foundation** — the bot boots, registers slash commands, and persists data.
Available now: guild configuration, `/server add|list|remove`, `/bind`.
Live Rust+ features (pairing, chat bridge, map events, smart devices, cameras) are planned in upcoming subsystems.

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
| `RustPlusBot.Abstractions` | Shared seams: `IClock`, `IEventBus`, `ICredentialProtector`, `ICredentialStore` |
| `RustPlusBot.Domain` | Entities and enums |
| `RustPlusBot.Persistence` | `BotDbContext`, EF Core services, SQLite migrations |
| `RustPlusBot.Discord` | Discord.Net gateway, hosted service, slash-command interaction modules |
| `RustPlusBot.Host` | Generic Host entry point, DI wiring, startup validation |

## Roadmap

Subsystems are built in order; each has its own spec → plan → build cycle.

| # | Subsystem | Status |
| --- | --- | --- |
| 0 | Foundation (bootable host, config/persistence, `/server`, `/bind`) | Done |
| 1 | Pairing & connection (FCM, credential pool, hot-swap, auto-failover) | Planned |
| 2 | Chat bridge + `!commands` | Planned |
| 3 | Map + live events | Planned |
| 4 | Smart devices | Planned |
| 5 | Cameras | Planned |

## License

MIT — see [LICENSE](LICENSE).
