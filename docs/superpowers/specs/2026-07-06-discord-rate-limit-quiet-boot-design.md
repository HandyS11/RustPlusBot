# Quiet Boot: Discord Edit Dedup + Boot-Sweep Suppression

**Date:** 2026-07-06
**Status:** Approved
**Problem evidence:** `src/RustPlusBot.Host/logs/rustplusbot-20260706.log` (boot at 13:42)

## Problem

At startup the bot floods Discord's per-channel `PATCH /channels/{ch}/messages/{msg}`
rate-limit bucket (~5 requests / 5 s / channel) and one embed edit dies with a
`TimeoutException` after queuing past Discord.Net's default 15 s request timeout.

Each device embed is edited ~3 times within ~2 seconds of boot:

1. `ConnectionStatusChangedEvent` (`Connecting`, published at
   `ConnectionSupervisor.PublishStatusAsync`) → each device relay's
   `HandleConnectionStatusAsync` sweeps **all** device embeds to "unreachable".
2. On `Connected`, `PrimeDevicesAsync` publishes a `DeviceReachabilityChangedEvent`
   unconditionally per device → second render.
3. For each reachable device, a `SmartDeviceTriggeredEvent` /
   `SmartDeviceStateObservedEvent` / `StorageMonitorTriggeredEvent` follows → third render.

Almost all of these edits are no-ops — the embeds already show the correct state from
the previous run. The relays render on every event with no "did anything change?" check
and no per-message coalescing. The same gap will bite harder with more devices, more
guilds, and wipe-day reconnect storms.

Side observation, same theme: `[Gateway] A Ready handler is blocking the gateway task`
— guild command registration runs inline in `DiscordBotService.OnReadyAsync`.

## Decisions Made (with user)

- **Structural fix**, not a boot-only patch or Discord.Net tuning alone.
- **Drop the unreachable sweep at boot**: sweep only when a previously-Connected
  server (in this process) drops. Startup keeps last-run embed state until priming
  publishes fresh state. Mid-session behavior unchanged.
- **Dedup by rendered output** (Approach 1: render-hash gate in the messenger), not by
  semantic store-diff in each relay (breaks the TC-protection countdown, duplicates
  logic ×3) and not by comparing against the Discord-fetched embed (brittle round-trip
  comparison; a false "equal" silently freezes an embed).

## Design

### Component 1 — `DiscordChannelMessenger` becomes an injected singleton with a render gate

Convert the static helper (`src/RustPlusBot.Discord/Posting/DiscordChannelMessenger.cs`)
into a sealed instance class registered in DI as a singleton. `DiscordSocketClient` is
injected; callers keep passing their own `ILogger` so log attribution is unchanged;
method signatures otherwise stay as-is. All call sites (3 channel posters, 3 pairing
coordinators, and any other `EnsureAsync`/`PostAsync` users) switch to the injected
instance.

Inside `EnsureAsync`, before `ModifyAsync`:

- A **canonicalizer** turns embed + components into a deterministic string covering:
  title, description, color, author, fields (name/value/inline), footer,
  thumbnail/image URLs, timestamp, and component tree (button custom-ids, labels,
  styles, disabled, emotes).
- A per-process `ConcurrentDictionary<ulong messageId, string canonicalRender>` holds
  the last **successfully sent** render per message. Equal → **skip the PATCH** and
  return the message id. Different or absent → edit, then update the cache.
- The `GetMessageAsync` existence fetch **stays**: deleted embeds still self-heal
  immediately (GET buckets are generous; only the PATCH bucket was exhausted). On the
  self-heal repost path the cache entry re-keys to the new message id (old entry
  removed).
- `PostAsync` (fire-and-forget event posts, always new messages) is untouched by the
  gate.

The gate itself is extracted as a small pure class (working name `RenderGate`:
try-enter / commit / invalidate keyed by message id) so it is unit-testable; the
messenger stays a thin I/O shim.

### Component 2 — Boot-sweep suppression via in-process "was connected" flag

Connection status is DB-persisted (`ConnectionStore.UpsertStatusAsync`), so
"previous status" read from the store is stale across restarts (it would still say
`Connected` at boot and defeat the suppression). Instead the `ConnectionSupervisor`
tracks the last status **it published in this process** per (guild, server) key.

`ConnectionStatusChangedEvent` gains two fields (bools, not the `ConnectionStatus`
enum: the event lives in `RustPlusBot.Abstractions`, a dependency-free leaf project,
while the enum lives in `RustPlusBot.Domain` — and consumers only need
Connected-or-not):

```csharp
public sealed record ConnectionStatusChangedEvent(
    ulong GuildId, Guid ServerId, bool IsConnected, bool WasConnected);
```

`WasConnected` = the previous in-process published status for this key was
`Connected`. The three device relays (`SwitchStateRelay`, `AlarmStateRelay`,
`StorageMonitorStateRelay`) sweep to unreachable only when
`WasConnected && !IsConnected`, and drop their `IConnectionStore` read in that
handler (the event now carries the decision).

Consequences:

- Boot: `Connecting` arrives with `WasConnected = false` → no sweep, no flicker.
- Mid-session drop: exactly one sweep. Subsequent statuses in a reconnect loop have
  `WasConnected = false` → no repeated sweep renders (embeds already marked).
- Other consumers (Workspace/Events/Map hosted services) ignore the new fields.

### Component 3 — Safety nets

- Messenger `RequestOptions`: `RetryMode = RetryMode.AlwaysRetry` and
  `Timeout = 30_000` ms (up from Discord.Net's 15 s default) so a genuine burst
  degrades to slow catch-up instead of `TimeoutException`.
- `DiscordBotService.OnReadyAsync`: move `RegisterCommandsToGuildAsync` work off the
  gateway thread (`Task.Run`), keeping the once-per-process guard. Kills the
  "Ready handler is blocking the gateway task" warning.

### Boot data flow after the change

`Connecting` → no sweep → `Connected` → prime publishes reachability + state per
device → two renders of the same message; the first is a cold-cache miss → 1 PATCH,
the second hashes equal → skipped. **Net: ~1 PATCH per device message per boot**,
comfortably inside the 5/5s bucket for realistic device counts.

Steady state: the storage-contents republish on each reachability-poll cycle hashes
equal and is skipped — except when the TC-protection countdown text actually ticks
over ("2d 5h" → "2d 4h"), which correctly goes through. Dedup by rendered output is
what keeps that countdown fresh without special-casing.

## Error Handling

- The cache updates **only after a successful** `ModifyAsync`/`SendMessageAsync`; on
  any failure the entry is removed so the next render retries rather than believing a
  phantom success.
- Two concurrent renders of one message may both PATCH — benign (Discord.Net's queue
  serializes; last writer wins the cache). No locking beyond `ConcurrentDictionary`.
- Cancellation semantics and the broad-catch-return-null contract of `EnsureAsync`
  are unchanged; the 404 self-heal path is unchanged except cache re-keying.
- Memory: one short string per tracked embed (dozens of messages); no eviction policy.

## Testing

Follows the repo's coverage split: pure logic tested, Discord I/O excluded.

- **Canonicalizer + `RenderGate` unit tests:** identical renders → equal; each varying
  property (field text, color, button disabled, custom id, …) → different; failure
  invalidation forces the next edit; repost re-keys the entry.
- **Messenger:** remains a thin excluded I/O shim (today's "untested integration
  shim" stance).
- **Relay tests:** boot-vs-drop sweep gating (`WasConnected` false/true) added to the
  three relays' existing suites.
- **Supervisor tests:** `WasConnected` correctness across transition sequences
  (boot → Connected → drop → reconnect loop).
- **Live verification:** boot with the current 5 embeds → zero `Rate limit triggered`
  warnings, no `Ready handler is blocking`; toggle a switch → instant edit; delete an
  embed → self-heals on the next render.

## Non-Goals

- No debounce/coalescing queue — dedup caps any storm at one PATCH per message.
  Revisit only if a future feature edits one message with *distinct* content many
  times per second.
- No persisted render hashes — one cold-cache edit per message per restart is the
  accepted cost.
- Map/Events/Players posting paths untouched (different routes, weren't bursting).
- No per-relay semantic store-diffing (Approach 2 dropped).
