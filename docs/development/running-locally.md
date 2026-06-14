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
