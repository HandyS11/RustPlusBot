# Opt-in slash-command reset on startup

**Date:** 2026-06-15
**Status:** Approved

## Problem

The Discord application used to test the bot was previously used by a different
bot. That bot left behind **global (application-level) slash commands**, which
appear in every guild and conflict/duplicate with this bot's commands.

The bot registers commands **per-guild** in `DiscordBotService.OnReadyAsync`
via `InteractionService.RegisterCommandsToGuildAsync`, whose `deleteMissing`
parameter defaults to `true`. That call already bulk-overwrites each guild's
command set on every startup, so stale *guild* commands (in guilds the bot is
in) are already cleared today. The one thing the current flow cannot touch is
**global** commands — and those are the leftover/conflict source.

## Goal

Provide an opt-in, one-shot way to wipe all global commands at startup, then let
the existing per-guild registration install the current command set.

## Design

### Trigger — config flag

Add `ResetCommandsOnStartup` (`bool`, default `false`) to `DiscordOptions`,
bound from the existing `"Discord"` configuration section. Because it is a bound
option it can be set via:

- `appsettings.json` → `"Discord": { "ResetCommandsOnStartup": true }`
- environment variable → `Discord__ResetCommandsOnStartup=true`
- command line → `dotnet run -- --Discord:ResetCommandsOnStartup=true`

It is a one-shot maintenance toggle: run once with it on to clear leftovers,
then leave it off for normal runs.

### Behavior

In `DiscordBotService.OnReadyAsync`, under the existing `_hasRegisteredCommands`
once-guard, **before** the per-guild registration loop:

1. If `ResetCommandsOnStartup` is `true`:
   - `await client.Rest.DeleteAllGlobalCommandsAsync()` — removes every global
     command for this application.
   - Log a **warning** so a destructive reset is clearly visible in the logs.
2. Run the existing per-guild `RegisterCommandsToGuildAsync(guild.Id)` loop
   unchanged — it bulk-overwrites each guild's command set, clearing stale guild
   commands and installing the current ones.

### Scope

- **Global commands only** for the new step. Guild commands are already fully
  overwritten by the existing registration, and stale guild commands in guilds
  the bot is not a member of are inaccessible regardless.
- **No Discord slash-command surface** — the trigger is the startup flag.
- **No separate per-guild bulk-clear** — redundant with existing registration.

### Testing

The reset is a thin call at the Discord REST boundary, consistent with the rest
of `DiscordBotService`, which has no unit tests today because it requires a live
gateway. No test seam is introduced (YAGNI); verification is a build + format
check plus a manual one-shot run against the test application.

## API reference

`DeleteAllGlobalCommandsAsync()` is available on `DiscordRestClient` (and thus
`DiscordSocketClient.Rest`, a `DiscordSocketRestClient : DiscordRestClient`),
confirmed in Discord.Net 3.20.1.
