# Player Events Channel — Design

**Date:** 2026-07-22
**Status:** Approved

## Problem

Every live notification lands in the same per-server `#events` channel: map events (cargo
ship, patrol helicopter, chinook, oil rigs), wipe announcements, and team-player presence
transitions (login, logout, death, AFK). The player noise drowns the map events.

## Goal

Route team-player presence transitions to a new per-server `#player-events` channel. Map
events and wipe announcements stay in `#events`. In-game team-chat messaging is unchanged
for both feeds.

## Scope

**In scope:** `PlayerStateChangedEvent` (the presence transitions published by the team-info
poll) — the only event `Features.Players` relays.

**Out of scope:** `MapMarkersChangedEvent` / `RigStateChangedEvent` (`Features.Events`),
wipe announcements (`Features.Wipes`), the pinned `#info` team roster embed, smart alarms
and switches (already have their own channels). None of these change.

## Current Architecture

Three relays resolve the same locator to the same channel:

| Consumer | Feature project | Locator | Channel |
|---|---|---|---|
| `EventRelay` | `Features.Events` | `IEventChannelLocator` | `#events` |
| `WipeAnnouncer` | `Features.Wipes` | `IEventChannelLocator` | `#events` |
| `PlayerEventRelay` | `Features.Players` | `IEventChannelLocator` | `#events` |

Channels are declarative: `ServerWorkspaceSpecProvider` returns `ChannelSpec` records, and
`WorkspaceReconciler.EnsureChannelsAsync` creates any missing channel and enforces the
declared ordering via `EnsureChannelOrderAsync`. Locators are singletons subclassing
`CachingChannelLocator` (30s TTL over `IWorkspaceStore.GetChannelsByKeyAsync`).

Player events already live in their own feature project, so the split is a new channel key
plus a new locator — not a code move.

## Approach

Mirror the existing locator pattern, which the codebase already uses nine times over.

Rejected alternatives:

- **Parameterize one locator by channel key.** Would collapse nine near-identical locator
  classes into one, but touches every locator and consumer. Unrelated refactor.
- **Single channel with Discord tags/threads.** Does not meet the goal.

## Changes

### 1. Channel key

`WorkspaceKeys.cs` — add to `WorkspaceChannelKeys`:

```csharp
/// <summary>Key for the per-server #player-events channel.</summary>
public const string ServerPlayerEvents = "playerevents";
```

### 2. Channel spec

`ServerWorkspaceSpecProvider.GetChannelSpecs()` — insert after `ServerEvents` at position 5,
shifting `ServerMap`, `ServerSwitches`, `ServerAlarms`, `ServerStorageMonitors` to 6–9:

```csharp
new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerPlayerEvents, "channel.playerevents.name",
    ChannelPermissionProfile.ReadOnly, 5),
```

Read-only, like `#events` — the bot posts, players do not.

### 3. Localized names

`Strings.resx`: `channel.playerevents.name` = `player-events`
`Strings.fr.resx`: `channel.playerevents.name` = `evenements-joueurs`

### 4. Locator

`Features.Workspace/Locating/IPlayerEventChannelLocator.cs` and
`PlayerEventChannelLocator.cs`, modelled exactly on `IEventChannelLocator` /
`EventChannelLocator`:

```csharp
internal sealed class PlayerEventChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : CachingChannelLocator(scopeFactory, clock, WorkspaceChannelKeys.ServerPlayerEvents),
      IPlayerEventChannelLocator;
```

Registered in `WorkspaceServiceCollectionExtensions` alongside the other locators:

```csharp
services.AddSingleton<IPlayerEventChannelLocator, PlayerEventChannelLocator>();
```

### 5. Routing

`PlayerEventRelay` swaps its `IEventChannelLocator locator` constructor parameter for
`IPlayerEventChannelLocator locator`. The XML doc comment updates from "#events" to
"#player-events". Nothing else in the class changes — in particular the
`teamChatSender.SendAsync(...)` call, the renderer calls, and the per-transition loop are
untouched, so the in-game team-chat output is byte-identical.

`EventRelay` and `WipeAnnouncer` are not modified.

## Data Flow

```
Team-info poll → PlayerStateChangedEvent → PlayersHostedService → PlayerEventRelay
    ├─ teamChatSender.SendAsync(...)                    → in-game team chat  (unchanged)
    └─ IPlayerEventChannelLocator.GetChannelIdAsync(...) → #player-events     (new target)

Map markers → MapMarkersChangedEvent → EventRelay
    ├─ teamChatSender.SendAsync(...)                    → in-game team chat  (unchanged)
    └─ IEventChannelLocator.GetChannelIdAsync(...)      → #events            (unchanged)
```

## Migration

None required. On the next reconcile, `WorkspaceReconciler` creates `#player-events` in every
existing per-server category and reorders the category to match the declared positions.
Historical messages already in `#events` stay there.

## Error Handling

`PlayerEventRelay` already treats a null channel id as "skip the Discord post" while still
sending the in-game line. That covers the window between deploy and first reconcile, and any
case where a guild admin deletes the channel. No fallback to `#events` — a temporary gap
matches how every other channel behaves on introduction.

## Testing

| Test | Assertion |
|---|---|
| `PlayerEventRelayTests` | Substitutes `IPlayerEventChannelLocator`; player embeds post to the player-events channel id and never to `#events`. In-game line still sent when the locator returns null. |
| `PlayersHostedServiceTests`, `PlayerEventRegistrationTests` | Updated to the new interface; DI graph resolves. |
| `WorkspaceRegistrationTests` | `IPlayerEventChannelLocator` is registered. |
| `ServerWorkspaceSpecProvider` spec test | `playerevents` spec exists at position 5, read-only; `events` remains at position 4 and the four following channels are at 6–9. |
| `EventRelayTests`, `WipeAnnouncerTests` | Unchanged — regression proof that map events and wipe announcements still target `#events`. |
