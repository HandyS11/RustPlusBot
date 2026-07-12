# Teamchat bridge: suppress bot-originated echoes with [R+] prefix

**Date:** 2026-07-05
**Status:** Approved
**Branch target:** `feat/*` off `develop`

## Problem

Every line the bot writes into in-game team chat echoes back on the Rust+ socket as a
`TeamMessageReceivedEvent` from the bot's active player, and `TeamChatRelay` webhook-posts it into the
Discord #teamchat channel. The Discord→game bridge path is already deduped (`TeamChatInboundProcessor`
records the exact line in `RelayDedupBuffer` before sending; `TeamChatRelay.TryConsume` drops the echo),
but four bot-originated senders are not:

- `CommandDispatcher` — in-game `!command` replies
- `EventRelay` — cargo/heli/chinook + oil-rig lines
- `PlayerEventRelay` — join/leave/death lines
- `AlarmStateRelay` — alarm trigger/reset lines

Their echoes clutter #teamchat, which should carry only human discussion.

## Desired behavior

| Line origin | In game | In Discord #teamchat |
| --- | --- | --- |
| Player types in game | as typed | relayed (unchanged) |
| Discord member types in #teamchat | `[Name] text` (unchanged) | their own Discord message only; echo dropped (unchanged) |
| Bot-originated (commands/events/players/alarms) | `[R+] text` (new prefix) | **never posted** (new) |

## Design

### New pieces — `RustPlusBot.Features.Connections/Listening`

- `BotTeamChat` static class holding `public const string Prefix = "[R+]";` — shared by the sender
  and the relay drop rule. Not localized (brand marker, same in every culture).
- `IBotTeamChatSender` — same `SendAsync(ulong guildId, Guid serverId, string message,
  CancellationToken)` shape as `ITeamChatSender`, returning `TeamChatSendResult`.
- `internal sealed BotTeamChatSender(ITeamChatSender inner) : IBotTeamChatSender` — prepends
  `"[R+] "` (prefix + single space, invariant formatting) and forwards; passes the result through
  untouched. Registered in `ConnectionServiceCollectionExtensions` alongside `ITeamChatSender`.

### Callers switched to `IBotTeamChatSender`

- `CommandDispatcher` (Features.Commands)
- `EventRelay` — via its `RelayChannels` record property (Features.Events)
- `PlayerEventRelay` (Features.Players)
- `AlarmStateRelay` — via its channels record property (Features.Alarms)

`TeamChatInboundProcessor` keeps the plain `ITeamChatSender`: bridged player speech stays unprefixed
(`[Name] text`) and keeps its existing buffer-based echo dedup.

### Drop rule — `TeamChatRelay.RelayAsync`

Beside the existing `RelayDedupBuffer.TryConsume` check:

```csharp
if (evt.FromActivePlayer && evt.Message.StartsWith(BotTeamChat.Prefix, StringComparison.Ordinal))
{
    return; // Bot-originated line echoing back.
}
```

- Gated on `FromActivePlayer`: a teammate who literally types `[R+] hi` in game still relays to
  Discord; only the paired (active) player's prefixed lines are treated as bot output.
- Robust against game-side truncation of long lines — the prefix sits at the front, so
  `StartsWith` always matches on the echo.
- Accepted edge: if the paired player manually types a line starting with `[R+]` in game, it is
  suppressed from Discord.

### Unchanged

`RelayDedupBuffer` and the record-then-send bridge path, mute gating in `TeamChatInboundProcessor`,
webhook posting for genuine player lines, all Discord-side embeds/channels for events/alarms/players.

### Error handling

No new failure modes. The decorator is pass-through; callers' existing handling of
`Sent`/`NotConnected`/`Failed` is untouched.

## Testing

- `BotTeamChatSender`: prefixes the message (exactly one space between prefix and text), forwards
  guild/server/token, passes each `TeamChatSendResult` value through.
- `TeamChatRelay`: drops `FromActivePlayer` + prefix; posts non-active-player lines even with the
  prefix; still consumes bridge dedup entries; still posts plain player lines.
- Existing tests for the four switched relays updated to fake `IBotTeamChatSender`.
- No `.resx` changes → localization parity untouched.

## Visible change

Every bot line in game now starts with `[R+] ` (~5 characters of the game's team-chat message
budget). Discord #teamchat shows only human messages.

## Addendum (2026-07-05, follow-up fixes on the same branch)

Live testing surfaced two related issues, fixed in commits `06bd47d` + `13a6024`:

1. **Paired player's in-game `!commands` never dispatched.** `CommandDispatcher` opened with
   `if (evt.FromActivePlayer) return;` (pre-existing since subsystem 3b) — the only available
   "ignore our own output" signal before the `[R+]` prefix existed, but it also swallowed every
   line the owner typed in game. Replaced with a `BotTeamChat.Prefix` guard: only `[R+]`-prefixed
   echoes are ignored, so the paired player's commands now run (reply appears in game as
   `[R+] <reply>`; still suppressed from Discord). Self-trigger loops remain impossible: `[R+] …`
   and bridged `[Name] …` lines can never parse as commands.
2. **Command invocations no longer relay to #teamchat.** `TeamChatRelay` now resolves the
   per-server command prefix (scoped `IMuteStore`, default `!`) and drops any player line whose
   trimmed text starts with it — the reply lives in game, so the bare trigger is noise in Discord.
   Trade-off (user-approved): any prefix-leading chat line (e.g. `!!!`) is also hidden from
   Discord; it still shows in game.
