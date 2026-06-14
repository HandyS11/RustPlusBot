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

5. In the guild, use the slash commands:

   - `/server add name:<name> ip:<host> port:<port>`
   - `/server list`
   - `/server remove id:<id>`
   - `/bind feature:<Chat|Events|Devices|Cameras> channel:<#channel>`

A missing or empty `Discord:Token` makes the host fail fast at startup with a clear `OptionsValidationException`.
