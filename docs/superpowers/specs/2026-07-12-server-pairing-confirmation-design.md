# Server-Pairing Confirmation in #setup — Design

**Date:** 2026-07-12
**Status:** Approved (brainstorm gate passed)
**Branch (planned):** feat/server-pairing-confirmation off develop

## Problem

Today, when a new server pairing notification arrives over FCM, `PairingHandler`
immediately persists the server and credentials and publishes
`ServerRegisteredEvent`, which triggers the full cycle automatically:
per-server category + 7 channels are provisioned and the Rust+ websocket
connects and starts polling. The user has no say.

The entity flow (switches/alarms/storage monitors) behaves better: a prompt
embed ("New switch detected — Add it?") with Accept/Dismiss buttons is posted,
and nothing is persisted until Accept.

**Goal:** server pairing mirrors the entity pattern — a confirmation prompt is
posted in the guild-level **#setup** channel, and the full cycle runs only on
Accept.

## Decisions (user-confirmed)

1. **Pending state is in-memory only** — exact mirror of the entity
   coordinators. Nothing touches the DB before Accept. A bot restart loses
   pending pairings; the user presses "Pair" in-game again.
2. **Prompt only for genuinely new servers.** Re-pairing an already-known
   server keeps today's silent behavior: Facepunch-ID backfill + standby
   credential upsert, no event, no prompt.
3. **Any guild member can Accept/Dismiss** — same as entity prompts.
4. **Coordinator lives in Features.Pairing.** Credentials (the Rust+ player
   token in the pending notification) never leave the Pairing feature.
   Features.Workspace only contributes a `#setup` channel locator.

## Architecture

### Flow change — `PairingHandler` (Features.Pairing)

The server-pairing path splits on existence by endpoint:

- **Existing server** (lookup by `(guildId, ownerUserId, ip, port)` matches):
  exactly today's behavior — Facepunch-ID backfill (non-empty guard kept) +
  `UpsertFromPairingAsync` credential upsert. No event, no prompt.
- **New server**: nothing persisted. The full `PairingNotification` (plus
  guildId/ownerUserId) is handed to `ServerPairingCoordinator.HandleDetectedAsync`.

`IServerService` needs a **lookup-only** method beside
`ResolveOrCreateByEndpointAsync` (which creates); add e.g.
`GetByEndpointAsync(guildId, ownerUserId, ip, port)` following the existing
resolve method's matching semantics.

### `ServerPairingCoordinator` (new, Features.Pairing)

Mirror of `SwitchPairingCoordinator` (`src/RustPlusBot.Features.Switches/Pairing/SwitchPairingCoordinator.cs`):

- Pending store: in-memory
  `ConcurrentDictionary<PendingKey, Pending>` where `Pending` holds the latest
  `PairingNotification`, the owner user id, and the posted prompt `MessageId`.
  `PendingKey` mirrors the endpoint-matching semantics of
  `ResolveOrCreateByEndpointAsync` (verify during planning): if matching is
  owner-scoped, the key is `(guildId, ownerUserId, ip, port)` and the owner id
  joins the custom-id tail; otherwise `(guildId, ip, port)`. The key and the
  lookup must agree, so a pending prompt and an existing-server check can
  never disagree about which server they refer to.
- **`HandleDetectedAsync`**: if an entry is already pending for the endpoint,
  refresh the stored notification (fresh player token) and keep the existing
  prompt message (no duplicate prompts on repeated in-game "Pair" presses).
  Otherwise resolve the #setup channel, render the prompt, post it, store
  pending state with the message id.
- **`TryAcceptAsync(guildId, ip, port, userId)`**: race-guarded pending
  removal, then runs today's persist path verbatim:
  `ResolveOrCreateByEndpointAsync` → Facepunch-ID backfill (non-empty guard) →
  `UpsertFromPairingAsync(markActive: created)` → publish
  `ServerRegisteredEvent(guildId, serverId)` only when `created`. Then the
  prompt message is edited into a short "Server added" confirmation embed
  (no buttons). Returns an outcome for the module's ephemeral reply.
  If the server already exists by then (concurrent re-pair race),
  `created == false`: credentials still upsert, no event fires, and the reply
  says the server was already added.
- **`TryDismiss(guildId, ip, port)`**: removes pending state; returns whether
  anything was pending (module deletes the prompt message).

### Prompt rendering & posting (Features.Pairing)

- `ServerPromptRenderer`: prompt embed — title "New server detected", body
  with server name + `ip:port`, Accept/Dismiss button row; plus the
  post-accept "Server added" embed.
- `ServerPairingComponentIds`: `server:accept:` / `server:dismiss:` prefixes;
  custom-id tail encodes `{ip}:{port}` (guild id comes from the interaction
  context). Well under Discord's 100-char custom-id limit.
- Posting/editing goes through the same messenger mechanism the entity
  channel posters use (reuse `DiscordChannelMessenger`/poster pattern; final
  shape decided in the plan by mirroring `ISwitchChannelPoster`).

### `ISetupChannelLocator` (new, implemented in Features.Workspace)

Guild-scoped locator: `guildId → #setup channel id (or null)`, resolved from
the provisioned-channel records the reconciler maintains
(`WorkspaceChannelKeys.Setup`, `WorkspaceScope.Global`). Lives beside the
existing entity channel locators (`CachingChannelLocator` family); interface
placed wherever `ISwitchChannelLocator` is declared so Pairing can consume it
without referencing Workspace. Note it is guild-keyed (no server id), unlike
the per-server locators — it may not fit the `CachingChannelLocator` base
as-is; a thin sibling is acceptable.

### `ServerPairingComponentModule` (new, Features.Pairing)

`InteractionModuleBase`, any guild member, mirrors `SwitchComponentModule`:

- **Accept** → defer ephemeral → `TryAcceptAsync` → reply "Server added." /
  "Server was already added." / stale-pending message.
- **Dismiss** → `TryDismiss` → delete prompt message (safe-delete helper) →
  reply "Dismissed."
- On a **stale** click (no pending entry — e.g. after a restart), both
  buttons delete the orphaned prompt message and reply with the
  pairing-expired message.

## Edge cases

| Case | Behavior |
| --- | --- |
| #setup channel missing (deleted after connect, or provisioning records gone) | Warn-log + DM the owner via existing `DiscordOwnerNotifier`: run `/setup`, then pair again. Notification dropped. |
| Bot restarted while prompt pending | Buttons find no pending entry → ephemeral "This pairing expired — press Pair in-game again" + prompt message deleted. |
| Repeated "Pair" press while pending | Stored notification refreshed (latest token wins); existing prompt reused. |
| Entity pairing arrives before server accepted | Unchanged: unknown Facepunch GUID → logged and dropped (user pairs entities after accepting the server). |
| Two different owners pair the same endpoint | Endpoint lookup is owner-scoped (matches `ResolveOrCreateByEndpointAsync` semantics today); each follows its own new/existing path exactly as the current handler would. |
| Accept clicked twice (double-click race) | Race-guarded removal: second click gets the stale-pending reply. |

## Security note

The pending `PairingNotification` holds the Rust+ player token in plaintext
in memory until Accept/Dismiss/restart. Today the token is also plaintext
in memory transiently during handling; the window grows to "until the user
decides". Accepted: in-memory only, never logged, encrypted on persist via
the existing `ICredentialProtector` path.

## Localization

New keys in `Strings.resx` + `Strings.fr.resx`, following the
`switch.prompt.*` naming: `server.prompt.title`, `server.prompt.body`,
`server.prompt.accept`, `server.prompt.dismiss`, `server.prompt.added.title`,
`server.prompt.added.body`. The ephemeral button replies ("Server added.",
"Dismissed.", the expired message) are hardcoded English in the module,
mirroring the existing entity component modules.

## Testing

- **`ServerPairingCoordinatorTests`** (new, Features.Pairing.Tests): new
  server posts prompt to located #setup channel; pending refresh on repeat
  notification reuses message; accept persists server + credential, publishes
  `ServerRegisteredEvent` once, edits prompt; accept race (`created == false`)
  fires no event; dismiss clears pending; stale accept/dismiss return the
  expired outcome; missing #setup channel notifies owner and drops.
- **`PairingHandlerTests`** (updated): existing-server path unchanged
  (backfill + upsert, no event); new-server path routes to the coordinator
  and persists nothing.
- Renderer tests for the two embeds + component-id round-trip.
- `ServerPairingComponentModule` stays coverage-excluded (Discord I/O), per
  existing convention.

## Out of scope

- No DB-backed pending state, no expiry timers.
- No config toggle to restore auto-add behavior.
- No change to entity pairing, credential storage, provisioning, or
  connection logic — Accept replays exactly the code path that runs
  automatically today.
