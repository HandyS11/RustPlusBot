# Running locally

1. Create a Discord application and bot at the [Discord Developer Portal](https://discord.com/developers/applications),
   and copy the bot token.

2. Provide the token without committing it (user-secrets):

   ```bash
   dotnet user-secrets set "Discord:Token" "<your-bot-token>" --project src/RustPlusBot.Host
   ```

   This works because `src/RustPlusBot.Host/RustPlusBot.Host.csproj` already contains a `UserSecretsId`.

3. Invite the bot to a test guild with the `applications.commands` and `bot` scopes.

4. Run the bot:

   ```bash
   dotnet run --project src/RustPlusBot.Host
   ```

   On first run the host applies the EF Core migration and creates `rustplusbot.db` in the working directory.

5. In the guild, use the slash commands per the provisioning flow below.

A missing or empty `Discord:Token` makes the host fail fast at startup with a clear `OptionsValidationException`.

## Discord application setup (team chat bridge)

The team chat bridge reads messages typed in each server's `#teamchat` channel, which requires a
privileged gateway intent:

1. Open the [Discord Developer Portal](https://discord.com/developers/applications) → your application
   → **Bot**.
2. Enable **Message Content Intent** (under *Privileged Gateway Intents*). No verification is required
   while the bot is in fewer than 100 servers.
3. Ensure the bot's role/invite grants **Manage Webhooks** (used to post in-game lines as each player)
   and **Send Messages** in the provisioned channels.

## Clearing stale slash commands

If the Discord application was previously used by another bot, leftover **global** commands can
appear in every guild alongside this bot's commands. Run once with `Discord:ResetCommandsOnStartup`
enabled to delete all global commands at startup before the current commands are registered:

```bash
dotnet run --project src/RustPlusBot.Host -- --Discord:ResetCommandsOnStartup=true
```

The flag also reads from the `Discord__ResetCommandsOnStartup` environment variable or a
`"Discord": { "ResetCommandsOnStartup": true }` entry in configuration. It is a one-shot: once the
stale global commands are gone, leave it off for normal runs. Per-guild commands are already
overwritten on every startup, so a normal run after the reset leaves only the current command set.

## Provisioning the workspace

1. Invite the bot with the **Manage Channels**, **Manage Roles**, **Manage Messages**, and
   **Embed Links** permissions.
2. In your test guild, run `/setup`. The bot creates a **RustPlusBot** category with
   `#information`, `#setup`, and `#settings` channels and posts its anchored messages.
3. Re-running `/setup` is safe — it reconciles and repairs without creating duplicates.
4. Change the language from the selector in `#settings`.
5. In Development (`Workspace:EnableDangerCommands = true`), use
   `/workspace simulate-server name:<n> ip:<host> port:<port>` to create a server category, and
   `/workspace reset` to delete the whole workspace.

Each provisioned server category also includes an `#events` channel, which receives live alerts when a
**cargo ship** or **patrol helicopter** enters or leaves the map and when a **chinook** spawns. No new
Discord gateway intent is required — event markers are detected by polling the Rust+ socket, not the
Discord gateway; only the existing **Send Messages** and **Embed Links** permissions on the provisioned
channel are used.
