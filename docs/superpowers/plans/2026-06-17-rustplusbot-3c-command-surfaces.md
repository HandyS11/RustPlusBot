# RustPlusBot 3c — Command Discord Surfaces — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the in-game command framework into Discord — `/help`, `/uptime`, `/leader`, and a team summary on the per-server `#info` embed.

**Architecture:** Slash modules + a help catalog live in the existing `RustPlusBot.Features.Commands` project (Approach A); they reuse `BotUptime`, `ICommandLocalizer`, the `ICommandHandler` registry, and `IRustServerQuery`. `/leader` adds a `PromoteToLeaderAsync` method to the `IRustServerQuery`/`IRustServerConnection` seam (supervisor + untested `RustPlusSocketSource` shim). The `#info` team summary is a local edit to Workspace's `ServerInfoMessageRenderer`. No new project, entity, migration, event, options, or background service.

**Tech Stack:** .NET 10, C# 13, Discord.Net 3.20 (`Discord.Interactions`), RustPlusApi 2.0.0-beta.1, xUnit + NSubstitute, EF Core/SQLite (test harness only), strict Roslynator analyzers, ReSharper `jb cleanupcode` format gate.

## Global Constraints

- **Branch:** `feat/command-surfaces` off `develop`.
- **No secrets in replies or logs** — `PromoteToLeaderAsync` broad-catches to `false`; never throw with a token value.
- **Localization:** all user-facing copy is EN + FR via the existing per-feature catalogs (`CommandLocalizationCatalog` for Commands; `LocalizationCatalog` for Workspace). Ephemeral confirm/result text is acceptable in English only where it mirrors prior slices' convention, but reply embeds (`/help`, `/uptime`, `/leader`, `#info` field) ARE localized.
- **Interaction modules (`InteractionModuleBase`) are NOT unit-tested** — push logic into testable services/renderers; keep modules thin.
- **Run the FULL test suite and read per-assembly counts** after touching shared seams (`IRustServerQuery`, `FakeRustSocketSource`, `IRustServerConnection`) — a missing fake method silently drops a whole assembly's tests.
- **NSubstitute on internal interfaces** needs `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in the project under test (Connections already has it; check Commands).
- **`SelectMenuBuilder` hard-caps at 25 options** — `.Take(SelectMenuBuilder.MaxOptionCount)` any member/server list.
- **`Discord.ConnectionState` collides** with `Domain.Connections.ConnectionState` — alias (`using DomainConnectionState = …`) if both are named in one file.
- **Roslynator:** `// TODO` comments are errors (`S1135`) — use `<remarks>`. An interface impl must repeat `= default` on a `CancellationToken` (`S1006`). Prefer concrete `Dictionary<>` where `CA1859` flags an interface field.
- **Before pushing:** `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (the real format gate; the pre-push hook enforces it). It reorders members Roslynator never flags.
- **Build must be 0 warnings / 0 errors** under strict analyzers; **no EF model drift** (this slice adds none).

---

## File Structure

**Connections (extend the existing seam):**
- Modify `src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs` — add `PromoteToLeaderAsync`.
- Modify `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs` — add `PromoteToLeaderAsync`.
- Modify `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` — implement the query method over `_liveSockets`.
- Modify `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` — implement on both `RejectedConnection` (returns false) and `RustPlusServerConnection` (wraps the real API).
- Modify `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` — add the method to `FakeConnection`.
- Modify `tests/RustPlusBot.Features.Connections.Tests/ServerQueryTests.cs` — add promote tests.

**Commands (new surfaces):**
- Create `src/RustPlusBot.Features.Commands/Help/CommandGroup.cs` — the grouping enum.
- Create `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs` — curated manifest.
- Create `src/RustPlusBot.Features.Commands/Help/HelpEmbedRenderer.cs` — pure embed builder.
- Create `src/RustPlusBot.Features.Commands/Leader/LeaderService.cs` — testable promote/fetch logic.
- Create `src/RustPlusBot.Features.Commands/Modules/CommandSurfaceModule.cs` — `/help`, `/uptime`, `/leader`.
- Create `src/RustPlusBot.Features.Commands/Modules/LeaderComponentModule.cs` — select callbacks.
- Modify `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs` — new EN/FR keys.
- Modify `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs` — register catalog, renderer, service, module assembly.
- Modify `src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj` — add a `RustPlusBot.Discord` project reference and a `Discord.Net` package reference (needed for `InteractionModuleBase`/`SocketInteractionContext`/`InteractionModuleAssembly`). The `DynamicProxyGenAssembly2` + test-project InternalsVisibleTo are already present — leave them.
- Create test files under `tests/RustPlusBot.Features.Commands.Tests/Help/` and `…/Leader/`.
- Modify `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs` — assert new registrations resolve.

**Workspace (`#info` summary):**
- Modify `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs` — add the team field.
- Modify `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs` — new EN/FR keys.
- Modify `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs` — new ctor arg + team-field cases.

---

## Task 1: `PromoteToLeaderAsync` on the connection seam

**Files:**
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs`
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ServerQueryTests.cs`

**Interfaces:**
- Produces:
  - `IRustServerQuery.PromoteToLeaderAsync(ulong guildId, Guid serverId, ulong steamId, CancellationToken)` → `Task<bool>` (true = promoted; false = no live socket or API non-success).
  - `IRustServerConnection.PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken)` → `Task<bool>`.

- [ ] **Step 1: Add the fake method (so the suite still builds) and write the failing supervisor tests**

In `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs`, add to `FakeConnection` (after `GetTeamInfoAsync`):

```csharp
/// <summary>The result returned by <see cref="PromoteToLeaderAsync"/>. Defaults to true.</summary>
public bool PromoteResult { get; set; } = true;

/// <summary>The Steam ID passed to the most recent <see cref="PromoteToLeaderAsync"/> call.</summary>
public ulong LastPromotedSteamId { get; private set; }

public Task<bool> PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken cancellationToken)
{
    LastPromotedSteamId = steamId;
    return Task.FromResult(PromoteResult);
}
```

In `tests/RustPlusBot.Features.Connections.Tests/ServerQueryTests.cs`, add (mirrors `GetTeamInfo_*`):

```csharp
[Fact]
public async Task PromoteToLeader_ReturnsTrueAndForwardsSteamId_WhenConnected()
{
    var source = new FakeRustSocketSource();
    var (provider, supervisor) = CreateHarness(source);
    await using var _ = provider;
    var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
    await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

    source.LastConnection!.PromoteResult = true;
    var promoted = await supervisor.PromoteToLeaderAsync(10UL, serverId, 999UL, cts.Token);

    Assert.True(promoted);
    Assert.Equal(999UL, source.LastConnection.LastPromotedSteamId);
    await supervisor.StopAllAsync();
}

[Fact]
public async Task PromoteToLeader_ReturnsFalse_WhenApiNonSuccess()
{
    var source = new FakeRustSocketSource();
    var (provider, supervisor) = CreateHarness(source);
    await using var _ = provider;
    var serverId = await SeedServerWithActiveAsync(provider, steamId: 555UL);

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    await supervisor.EnsureConnectionAsync(10UL, serverId, cts.Token);
    await WaitUntilAsync(() => supervisor.HasLiveSocket(10UL, serverId), cts.Token);

    source.LastConnection!.PromoteResult = false;
    var promoted = await supervisor.PromoteToLeaderAsync(10UL, serverId, 999UL, cts.Token);

    Assert.False(promoted);
    await supervisor.StopAllAsync();
}

[Fact]
public async Task PromoteToLeader_ReturnsFalse_WhenNoLiveSocket()
{
    var source = new FakeRustSocketSource();
    var (provider, supervisor) = CreateHarness(source);
    await using var _ = provider;

    var promoted = await supervisor.PromoteToLeaderAsync(10UL, Guid.NewGuid(), 999UL, CancellationToken.None);

    Assert.False(promoted);
}
```

- [ ] **Step 2: Run the new tests to verify they fail to compile / fail**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~PromoteToLeader"`
Expected: COMPILE ERROR — `IRustServerConnection`/`IRustServerQuery`/`ConnectionSupervisor` have no `PromoteToLeaderAsync`.

- [ ] **Step 3: Add `PromoteToLeaderAsync` to `IRustServerConnection`**

In `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`, after `SendTeamMessageAsync`:

```csharp
/// <summary>Promotes a team member to team leader; returns true on success.</summary>
/// <param name="steamId">Steam64 id of the member to promote.</param>
/// <param name="timeout">How long to wait for the response.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>True if the promotion succeeded; false on failure/timeout.</returns>
Task<bool> PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken cancellationToken);
```

- [ ] **Step 4: Add `PromoteToLeaderAsync` to `IRustServerQuery`**

In `src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs`, after `GetTeamInfoAsync`:

```csharp
/// <summary>Promotes a team member to team leader; returns false when there is no live socket or the API fails.</summary>
/// <param name="guildId">The owning guild snowflake.</param>
/// <param name="serverId">The target server id.</param>
/// <param name="steamId">Steam64 id of the member to promote.</param>
/// <param name="cancellationToken">A cancellation token.</param>
/// <returns>True if promoted; false when no live socket or the API fails.</returns>
Task<bool> PromoteToLeaderAsync(ulong guildId, Guid serverId, ulong steamId, CancellationToken cancellationToken);
```

- [ ] **Step 5: Implement on `ConnectionSupervisor`**

In `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`, after `GetTeamInfoAsync` (before `SendAsync`):

```csharp
/// <inheritdoc />
public async Task<bool> PromoteToLeaderAsync(
    ulong guildId,
    Guid serverId,
    ulong steamId,
    CancellationToken cancellationToken)
{
    if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
    {
        return false;
    }

    return await live.Connection.PromoteToLeaderAsync(steamId, _options.HeartbeatTimeout, cancellationToken)
        .ConfigureAwait(false);
}
```

- [ ] **Step 6: Implement on the `RejectedConnection` and `RustPlusServerConnection` shim classes**

In `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs`, add to `RejectedConnection` (after `SendTeamMessageAsync`):

```csharp
public Task<bool> PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken cancellationToken) =>
    Task.FromResult(false);
```

And add to `RustPlusServerConnection` (after `SendTeamMessageAsync`, before `DisposeAsync`):

```csharp
public async Task<bool> PromoteToLeaderAsync(ulong steamId, TimeSpan timeout, CancellationToken cancellationToken)
{
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(timeout);
    try
    {
        // CONFIRMED (2.0.0-beta.1): PromoteToLeaderAsync(ulong, CancellationToken) returns a payload-free
        // Task<Response>; Response.IsSuccess indicates the outcome.
        var response = await _rustPlus.PromoteToLeaderAsync(steamId, timeoutCts.Token).WaitAsync(timeoutCts.Token)
            .ConfigureAwait(false);
        return response.IsSuccess;
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return false;
    }
#pragma warning disable CA1031 // Broad catch: any promote failure maps to false; never surface a token/secret.
    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
    {
        LogQueryFailed(_logger, ex);
        return false;
    }
}
```

- [ ] **Step 7: Run the new tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter "FullyQualifiedName~PromoteToLeader"`
Expected: PASS (3 tests).

- [ ] **Step 8: Run the FULL Connections suite + confirm count**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests`
Expected: PASS, count = 26 (23 prior + 3 new). If the assembly's total looks low, a fake method is missing — fix before continuing.

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(3c): add PromoteToLeaderAsync to the connection query seam

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: `CommandGroup` + `CommandHelpCatalog` with a drift/i18n guard

**Files:**
- Create: `src/RustPlusBot.Features.Commands/Help/CommandGroup.cs`
- Create: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`
- Modify: `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs`

**Interfaces:**
- Consumes: the existing `ICommandHandler.Name` registry (12 handlers: `mute`, `unmute`, `uptime`, `pop`, `wipe`, `time`, `online`, `offline`, `team`, `steamid`, `alive`, `prox`).
- Produces:
  - `enum CommandGroup { Control, Server, TeamIntel, Bot }`.
  - `sealed record CommandHelpEntry(string Name, CommandGroup Group, string DescriptionKey)`.
  - `static class CommandHelpCatalog` with `IReadOnlyList<CommandHelpEntry> InGame` and `IReadOnlyList<CommandHelpEntry> Slash`.

- [ ] **Step 1: Write the failing catalog tests**

Create `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Help;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Tests.Help;

public sealed class CommandHelpCatalogTests
{
    // The 12 registered in-game handler names (see AddCommands / CommandRegistrationTests).
    private static readonly string[] HandlerNames =
    [
        "mute", "unmute", "uptime", "pop", "wipe", "time",
        "online", "offline", "team", "steamid", "alive", "prox",
    ];

    [Fact]
    public void EveryRegisteredHandlerHasAnInGameEntry()
    {
        var entryNames = CommandHelpCatalog.InGame.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in HandlerNames)
        {
            Assert.Contains(name, entryNames);
        }
    }

    [Fact]
    public void InGameCatalogHasNoEntryWithoutAHandler()
    {
        var handlerNames = HandlerNames.ToHashSet(StringComparer.Ordinal);
        foreach (var entry in CommandHelpCatalog.InGame)
        {
            Assert.Contains(entry.Name, handlerNames);
        }
    }

    [Fact]
    public void EveryDescriptionKeyResolvesInEnglishAndFrench()
    {
        var loc = new CommandLocalizer(CommandLocalizationCatalog.Default);
        foreach (var entry in CommandHelpCatalog.InGame.Concat(CommandHelpCatalog.Slash))
        {
            // Get returns the key itself when unresolved; a resolved value must differ from the key.
            Assert.NotEqual(entry.DescriptionKey, loc.Get(entry.DescriptionKey, "en"));
            Assert.NotEqual(entry.DescriptionKey, loc.Get(entry.DescriptionKey, "fr"));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "FullyQualifiedName~CommandHelpCatalogTests"`
Expected: COMPILE ERROR — `CommandHelpCatalog`/`CommandGroup` do not exist.

- [ ] **Step 3: Create `CommandGroup`**

Create `src/RustPlusBot.Features.Commands/Help/CommandGroup.cs`:

```csharp
namespace RustPlusBot.Features.Commands.Help;

/// <summary>The display grouping for a command in the <c>/help</c> listing.</summary>
internal enum CommandGroup
{
    /// <summary>Bot control commands (mute/unmute).</summary>
    Control,

    /// <summary>Server-state commands (pop/time/wipe).</summary>
    Server,

    /// <summary>Team-intel commands (online/offline/team/steamid/alive/prox).</summary>
    TeamIntel,

    /// <summary>Bot-meta commands (uptime).</summary>
    Bot,
}
```

- [ ] **Step 4: Create `CommandHelpCatalog`**

Create `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`:

```csharp
namespace RustPlusBot.Features.Commands.Help;

/// <summary>One entry in the help listing.</summary>
/// <param name="Name">The command name without prefix (in-game) or slash command name.</param>
/// <param name="Group">The display group.</param>
/// <param name="DescriptionKey">The localization key for the description.</param>
internal sealed record CommandHelpEntry(string Name, CommandGroup Group, string DescriptionKey);

/// <summary>The curated help manifest. Tested against the live handler registry to catch drift.</summary>
internal static class CommandHelpCatalog
{
    /// <summary>The in-game <c>!commands</c>, in display order.</summary>
    public static IReadOnlyList<CommandHelpEntry> InGame { get; } =
    [
        new("mute", CommandGroup.Control, "help.mute"),
        new("unmute", CommandGroup.Control, "help.unmute"),
        new("pop", CommandGroup.Server, "help.pop"),
        new("time", CommandGroup.Server, "help.time"),
        new("wipe", CommandGroup.Server, "help.wipe"),
        new("online", CommandGroup.TeamIntel, "help.online"),
        new("offline", CommandGroup.TeamIntel, "help.offline"),
        new("team", CommandGroup.TeamIntel, "help.team"),
        new("steamid", CommandGroup.TeamIntel, "help.steamid"),
        new("alive", CommandGroup.TeamIntel, "help.alive"),
        new("prox", CommandGroup.TeamIntel, "help.prox"),
        new("uptime", CommandGroup.Bot, "help.uptime"),
    ];

    /// <summary>The Discord slash commands, in display order.</summary>
    public static IReadOnlyList<CommandHelpEntry> Slash { get; } =
    [
        new("help", CommandGroup.Bot, "help.slash.help"),
        new("uptime", CommandGroup.Bot, "help.slash.uptime"),
        new("leader", CommandGroup.Bot, "help.slash.leader"),
    ];
}
```

- [ ] **Step 5: Add the localization keys**

In `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs`, add to the `["en"]` dictionary (after `command.prox.alone`):

```csharp
["help.title"] = "Commands",
["help.group.control"] = "Control",
["help.group.server"] = "Server",
["help.group.teamintel"] = "Team",
["help.group.bot"] = "Bot",
["help.group.slash"] = "Slash commands",
["help.prefixnote"] = "Prefixes are configured per server; showing the default.",
["help.mute"] = "Silence all bot output to the game.",
["help.unmute"] = "Resume bot output to the game.",
["help.pop"] = "Show the server population.",
["help.time"] = "Show the in-game time.",
["help.wipe"] = "Show how long ago the server wiped.",
["help.online"] = "List online teammates.",
["help.offline"] = "List offline teammates.",
["help.team"] = "List all team members.",
["help.steamid"] = "Show teammates' Steam IDs.",
["help.alive"] = "Show how long each teammate has survived.",
["help.prox"] = "Show distance to each teammate.",
["help.uptime"] = "Show how long the bot has been running.",
["help.slash.help"] = "Show this help message.",
["help.slash.uptime"] = "Show the bot's uptime.",
["help.slash.leader"] = "Transfer in-game team leadership.",
["uptime.ok"] = "Bot uptime: {0}",
["leader.noserver"] = "No server is set up yet.",
["leader.notconnected"] = "Not connected to this server.",
["leader.noteam"] = "No team members to promote.",
["leader.pickserver"] = "Choose a server:",
["leader.pickmember"] = "Choose who to promote to team leader:",
["leader.success"] = "Promoted {0} to team leader.",
["leader.failure"] = "Couldn't promote — the server may be unreachable.",
```

And add the matching `["fr"]` entries (after `command.prox.alone`):

```csharp
["help.title"] = "Commandes",
["help.group.control"] = "Contrôle",
["help.group.server"] = "Serveur",
["help.group.teamintel"] = "Équipe",
["help.group.bot"] = "Bot",
["help.group.slash"] = "Commandes slash",
["help.prefixnote"] = "Les préfixes sont configurés par serveur ; affichage du préfixe par défaut.",
["help.mute"] = "Couper toute sortie du bot vers le jeu.",
["help.unmute"] = "Réactiver la sortie du bot vers le jeu.",
["help.pop"] = "Afficher la population du serveur.",
["help.time"] = "Afficher l'heure en jeu.",
["help.wipe"] = "Afficher depuis combien de temps le serveur a été wipe.",
["help.online"] = "Lister les coéquipiers en ligne.",
["help.offline"] = "Lister les coéquipiers hors ligne.",
["help.team"] = "Lister tous les membres de l'équipe.",
["help.steamid"] = "Afficher les identifiants Steam des coéquipiers.",
["help.alive"] = "Afficher depuis combien de temps chaque coéquipier survit.",
["help.prox"] = "Afficher la distance à chaque coéquipier.",
["help.uptime"] = "Afficher depuis combien de temps le bot fonctionne.",
["help.slash.help"] = "Afficher ce message d'aide.",
["help.slash.uptime"] = "Afficher la disponibilité du bot.",
["help.slash.leader"] = "Transférer le rôle de chef d'équipe en jeu.",
["uptime.ok"] = "Disponibilité du bot : {0}",
["leader.noserver"] = "Aucun serveur n'est encore configuré.",
["leader.notconnected"] = "Non connecté à ce serveur.",
["leader.noteam"] = "Aucun membre d'équipe à promouvoir.",
["leader.pickserver"] = "Choisissez un serveur :",
["leader.pickmember"] = "Choisissez qui promouvoir chef d'équipe :",
["leader.success"] = "{0} promu chef d'équipe.",
["leader.failure"] = "Impossible de promouvoir — le serveur est peut-être injoignable.",
```

- [ ] **Step 6: Run the catalog tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "FullyQualifiedName~CommandHelpCatalogTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Help src/RustPlusBot.Features.Commands/Localization tests/RustPlusBot.Features.Commands.Tests/Help
git commit -m "feat(3c): add command help catalog + help/leader/uptime localization keys

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: `HelpEmbedRenderer` (pure)

**Files:**
- Create: `src/RustPlusBot.Features.Commands/Help/HelpEmbedRenderer.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Help/HelpEmbedRendererTests.cs`

**Interfaces:**
- Consumes: `CommandHelpCatalog`, `ICommandLocalizer`.
- Produces: `HelpEmbedRenderer(ICommandLocalizer localizer)` with
  `Discord.Embed Render(string prefix, string culture, bool showPrefixNote)`.

- [ ] **Step 1: Write the failing renderer tests**

Create `tests/RustPlusBot.Features.Commands.Tests/Help/HelpEmbedRendererTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Help;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Tests.Help;

public sealed class HelpEmbedRendererTests
{
    private static readonly HelpEmbedRenderer Renderer =
        new(new CommandLocalizer(CommandLocalizationCatalog.Default));

    [Fact]
    public void Render_PrefixesInGameCommands()
    {
        var embed = Renderer.Render("$", "en", showPrefixNote: false);

        var body = string.Concat(embed.Fields.Select(f => f.Value));
        Assert.Contains("$pop", body, StringComparison.Ordinal);
        Assert.Contains("$online", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_IncludesSlashCommandsGroup()
    {
        var embed = Renderer.Render("!", "en", showPrefixNote: false);

        var body = string.Concat(embed.Fields.Select(f => f.Value));
        Assert.Contains("/leader", body, StringComparison.Ordinal);
        Assert.Contains("/uptime", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_AddsPrefixNote_WhenRequested()
    {
        var withNote = Renderer.Render("!", "en", showPrefixNote: true);
        var withoutNote = Renderer.Render("!", "en", showPrefixNote: false);

        Assert.NotNull(withNote.Description);
        Assert.Contains("per server", withNote.Description, StringComparison.OrdinalIgnoreCase);
        Assert.True(string.IsNullOrEmpty(withoutNote.Description));
    }

    [Fact]
    public void Render_LocalizesToFrench()
    {
        var embed = Renderer.Render("!", "fr", showPrefixNote: false);

        var body = string.Concat(embed.Fields.Select(f => f.Value));
        Assert.Contains("Afficher la population", body, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "FullyQualifiedName~HelpEmbedRendererTests"`
Expected: COMPILE ERROR — `HelpEmbedRenderer` does not exist.

- [ ] **Step 3: Create `HelpEmbedRenderer`**

Create `src/RustPlusBot.Features.Commands/Help/HelpEmbedRenderer.cs`:

```csharp
using System.Globalization;
using Discord;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Help;

/// <summary>Builds the <c>/help</c> embed from the catalog, the server prefix, and the guild culture.</summary>
/// <param name="localizer">Resolves the title, group headings, and per-command descriptions.</param>
internal sealed class HelpEmbedRenderer(ICommandLocalizer localizer)
{
    private static readonly (CommandGroup Group, string HeadingKey)[] GroupOrder =
    [
        (CommandGroup.Control, "help.group.control"),
        (CommandGroup.Server, "help.group.server"),
        (CommandGroup.TeamIntel, "help.group.teamintel"),
        (CommandGroup.Bot, "help.group.bot"),
    ];

    /// <summary>Renders the help embed.</summary>
    /// <param name="prefix">The in-game command prefix to display (e.g. "!").</param>
    /// <param name="culture">The guild culture ("en"/"fr").</param>
    /// <param name="showPrefixNote">True to add the multi-server prefix note to the description.</param>
    /// <returns>The built embed.</returns>
    public Embed Render(string prefix, string culture, bool showPrefixNote)
    {
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("help.title", culture))
            .WithColor(Color.Blue);

        if (showPrefixNote)
        {
            embed.WithDescription(localizer.Get("help.prefixnote", culture));
        }

        foreach (var (group, headingKey) in GroupOrder)
        {
            var lines = CommandHelpCatalog.InGame
                .Where(e => e.Group == group)
                .Select(e => string.Create(CultureInfo.InvariantCulture,
                    $"`{prefix}{e.Name}` — {localizer.Get(e.DescriptionKey, culture)}"))
                .ToList();
            if (lines.Count > 0)
            {
                embed.AddField(localizer.Get(headingKey, culture), string.Join('\n', lines));
            }
        }

        var slashLines = CommandHelpCatalog.Slash
            .Select(e => string.Create(CultureInfo.InvariantCulture,
                $"`/{e.Name}` — {localizer.Get(e.DescriptionKey, culture)}"))
            .ToList();
        embed.AddField(localizer.Get("help.group.slash", culture), string.Join('\n', slashLines));

        return embed.Build();
    }
}
```

- [ ] **Step 4: Run the renderer tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "FullyQualifiedName~HelpEmbedRendererTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Help/HelpEmbedRenderer.cs tests/RustPlusBot.Features.Commands.Tests/Help/HelpEmbedRendererTests.cs
git commit -m "feat(3c): add HelpEmbedRenderer

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: `LeaderService` (testable promote/fetch logic)

**Files:**
- Create: `src/RustPlusBot.Features.Commands/Leader/LeaderService.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Leader/LeaderServiceTests.cs`

**Interfaces:**
- Consumes: `IRustServerQuery` (`GetTeamInfoAsync`, `PromoteToLeaderAsync`), `ICommandLocalizer`, `TeamInfoSnapshot`/`TeamMemberSnapshot` (from `RustPlusBot.Features.Connections.Listening`).
- Produces:
  - `sealed record LeaderMemberOption(ulong SteamId, string Name, bool IsLeader)`.
  - `sealed record LeaderTeamResult(IReadOnlyList<LeaderMemberOption> Members, string? ErrorMessage)` — `ErrorMessage` non-null means stop and show it; otherwise render the select from `Members`.
  - `LeaderService(IRustServerQuery query, ICommandLocalizer localizer)` with:
    - `Task<LeaderTeamResult> GetMembersAsync(ulong guildId, Guid serverId, string culture, CancellationToken)`.
    - `Task<string> PromoteAsync(ulong guildId, Guid serverId, ulong steamId, string memberName, string culture, CancellationToken)` → the localized result message.

- [ ] **Step 1: Write the failing service tests**

Create `tests/RustPlusBot.Features.Commands.Tests/Leader/LeaderServiceTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Features.Commands.Leader;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Tests.Leader;

public sealed class LeaderServiceTests
{
    private static readonly CommandLocalizer Loc = new(CommandLocalizationCatalog.Default);
    private static readonly Guid Server = Guid.NewGuid();

    [Fact]
    public async Task GetMembers_ReturnsError_WhenNotConnected()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1UL, Server, Arg.Any<CancellationToken>())
            .Returns((TeamInfoSnapshot?)null);
        var service = new LeaderService(query, Loc);

        var result = await service.GetMembersAsync(1UL, Server, "en", CancellationToken.None);

        Assert.Empty(result.Members);
        Assert.Equal("Not connected to this server.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetMembers_ReturnsError_WhenTeamEmpty()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1UL, Server, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(0UL, []));
        var service = new LeaderService(query, Loc);

        var result = await service.GetMembersAsync(1UL, Server, "en", CancellationToken.None);

        Assert.Empty(result.Members);
        Assert.Equal("No team members to promote.", result.ErrorMessage);
    }

    [Fact]
    public async Task GetMembers_MapsMembersAndMarksLeader()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetTeamInfoAsync(1UL, Server, Arg.Any<CancellationToken>())
            .Returns(new TeamInfoSnapshot(
                10UL,
                [
                    new TeamMemberSnapshot(10UL, "alice", 0f, 0f, true, true,
                        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                    new TeamMemberSnapshot(20UL, "bob", 0f, 0f, true, true,
                        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                ]));
        var service = new LeaderService(query, Loc);

        var result = await service.GetMembersAsync(1UL, Server, "en", CancellationToken.None);

        Assert.Null(result.ErrorMessage);
        Assert.Equal(2, result.Members.Count);
        Assert.Contains(result.Members, m => m is { SteamId: 10UL, Name: "alice", IsLeader: true });
        Assert.Contains(result.Members, m => m is { SteamId: 20UL, Name: "bob", IsLeader: false });
    }

    [Fact]
    public async Task Promote_ReturnsSuccessMessage_WhenPromoted()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.PromoteToLeaderAsync(1UL, Server, 20UL, Arg.Any<CancellationToken>()).Returns(true);
        var service = new LeaderService(query, Loc);

        var message = await service.PromoteAsync(1UL, Server, 20UL, "bob", "en", CancellationToken.None);

        Assert.Equal("Promoted bob to team leader.", message);
    }

    [Fact]
    public async Task Promote_ReturnsFailureMessage_WhenApiFails()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.PromoteToLeaderAsync(1UL, Server, 20UL, Arg.Any<CancellationToken>()).Returns(false);
        var service = new LeaderService(query, Loc);

        var message = await service.PromoteAsync(1UL, Server, 20UL, "bob", "en", CancellationToken.None);

        Assert.Equal("Couldn't promote — the server may be unreachable.", message);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "FullyQualifiedName~LeaderServiceTests"`
Expected: COMPILE ERROR — `LeaderService` does not exist.

- [ ] **Step 3: Create `LeaderService`**

Create `src/RustPlusBot.Features.Commands/Leader/LeaderService.cs`:

```csharp
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Leader;

/// <summary>One selectable team member for the <c>/leader</c> promote select.</summary>
/// <param name="SteamId">The member's Steam64 id.</param>
/// <param name="Name">The member's in-game name.</param>
/// <param name="IsLeader">True if this member is the current team leader.</param>
internal sealed record LeaderMemberOption(ulong SteamId, string Name, bool IsLeader);

/// <summary>The members to offer, or an error message to show instead.</summary>
/// <param name="Members">The selectable members (empty when <paramref name="ErrorMessage"/> is set).</param>
/// <param name="ErrorMessage">A localized message to show instead of a select, or null to proceed.</param>
internal sealed record LeaderTeamResult(IReadOnlyList<LeaderMemberOption> Members, string? ErrorMessage);

/// <summary>The testable logic behind <c>/leader</c>: fetch live members, then promote one.</summary>
/// <param name="query">The live-server query seam.</param>
/// <param name="localizer">Resolves the result/error messages.</param>
internal sealed class LeaderService(IRustServerQuery query, ICommandLocalizer localizer)
{
    /// <summary>Fetches the current team for the promote select, or an error message.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The members to offer, or an error message.</returns>
    public async Task<LeaderTeamResult> GetMembersAsync(
        ulong guildId,
        Guid serverId,
        string culture,
        CancellationToken cancellationToken)
    {
        var team = await query.GetTeamInfoAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (team is null)
        {
            return new LeaderTeamResult([], localizer.Get("leader.notconnected", culture));
        }

        if (team.Members.Count == 0)
        {
            return new LeaderTeamResult([], localizer.Get("leader.noteam", culture));
        }

        var members = team.Members
            .Select(m => new LeaderMemberOption(m.SteamId, m.Name, m.SteamId == team.LeaderSteamId))
            .ToList();
        return new LeaderTeamResult(members, null);
    }

    /// <summary>Promotes a member and returns the localized result message.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="steamId">The Steam64 id to promote.</param>
    /// <param name="memberName">The member's name (for the success message).</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The localized result message.</returns>
    public async Task<string> PromoteAsync(
        ulong guildId,
        Guid serverId,
        ulong steamId,
        string memberName,
        string culture,
        CancellationToken cancellationToken)
    {
        var promoted = await query.PromoteToLeaderAsync(guildId, serverId, steamId, cancellationToken)
            .ConfigureAwait(false);
        return promoted
            ? localizer.Get("leader.success", culture, memberName)
            : localizer.Get("leader.failure", culture);
    }
}
```

- [ ] **Step 4: Run the service tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "FullyQualifiedName~LeaderServiceTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Leader tests/RustPlusBot.Features.Commands.Tests/Leader
git commit -m "feat(3c): add LeaderService (fetch members + promote)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Slash + component modules and DI registration

**Files:**
- Create: `src/RustPlusBot.Features.Commands/Modules/CommandSurfaceModule.cs`
- Create: `src/RustPlusBot.Features.Commands/Modules/LeaderComponentModule.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`

**Interfaces:**
- Consumes: `HelpEmbedRenderer`, `LeaderService`, `BotUptime`, `ICommandLocalizer`, `IServerService`, `IMuteStore`, `IWorkspaceStore`, `IServiceScopeFactory`, `RustPlusBot.Discord.InteractionModuleAssembly`, `DurationFormat`.
- Produces (custom-id contracts used across the two modules):
  - server-pick: `leader-server:` + serverId value carried as the select option value.
  - member-pick: `leader-promote:{serverId}` with the SteamId as the option value.

**Note on module registration:** `AddCommands` must register `HelpEmbedRenderer`, `LeaderService` (scoped — they use scoped stores/seams resolved per interaction via `IServiceScopeFactory` inside the module, OR register them and resolve from the scope), and a new `InteractionModuleAssembly(thisAssembly)` singleton so `DiscordBotService` discovers the modules. Follow `SetupModule`'s scope-per-interaction idiom: the module ctor takes only `IServiceScopeFactory`, and resolves `HelpEmbedRenderer`/`LeaderService`/stores from a fresh `CreateAsyncScope()` per call.

- [ ] **Step 1: Add the module-discovery + registration assertions to `CommandRegistrationTests`**

In `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`, add `using RustPlusBot.Discord;` and a new test:

```csharp
[Fact]
public void Commands_contribute_an_interaction_module_assembly()
{
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddSingleton<IClock>(Substitute.For<IClock>());
    services.AddSingleton<IEventBus>(Substitute.For<IEventBus>());
    services.AddSingleton<ITeamChatSender>(Substitute.For<ITeamChatSender>());
    services.AddSingleton<IRustServerQuery>(Substitute.For<IRustServerQuery>());
    services.AddScoped<IMuteStore>(_ => Substitute.For<IMuteStore>());
    services.AddScoped<IWorkspaceStore>(_ => Substitute.For<IWorkspaceStore>());
    services.AddOptions<CommandOptions>();
    services.AddCommands();

    using var provider = services.BuildServiceProvider(validateScopes: true);

    var assemblies = provider.GetServices<InteractionModuleAssembly>();
    Assert.Contains(assemblies, a => a.Assembly == typeof(CommandServiceCollectionExtensions).Assembly);
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "FullyQualifiedName~Commands_contribute_an_interaction_module_assembly"`
Expected: FAIL — no `InteractionModuleAssembly` is registered for the Commands assembly.

- [ ] **Step 2b: Add the Discord project + package references to the Commands csproj**

The Commands project does not yet reference `RustPlusBot.Discord` (where `InteractionModuleAssembly` lives) or the `Discord.Net` package (where `InteractionModuleBase`/`SocketInteractionContext`/`Discord.Interactions` live). Edit `src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj` so the reference groups read:

```xml
  <ItemGroup>
    <ProjectReference Include="..\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
    <ProjectReference Include="..\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
    <ProjectReference Include="..\RustPlusBot.Features.Connections\RustPlusBot.Features.Connections.csproj" />
    <ProjectReference Include="..\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Discord.Net" />
  </ItemGroup>
```

(`Discord.Net` version comes from `Directory.Packages.props` central management — no version attribute here, matching Connections.)

- [ ] **Step 3: Register the surfaces in `AddCommands`**

In `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`, add `using RustPlusBot.Discord;`, `using RustPlusBot.Features.Commands.Help;`, `using RustPlusBot.Features.Commands.Leader;`, and append before `return services;`:

```csharp
// Discord surfaces (3c): help renderer, leader service, and the interaction modules.
services.AddScoped<HelpEmbedRenderer>();
services.AddScoped<LeaderService>();
services.AddSingleton(new InteractionModuleAssembly(typeof(CommandServiceCollectionExtensions).Assembly));
```

- [ ] **Step 4: Run the registration test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "FullyQualifiedName~Commands_contribute_an_interaction_module_assembly"`
Expected: PASS.

- [ ] **Step 5: Create `CommandSurfaceModule`**

Create `src/RustPlusBot.Features.Commands/Modules/CommandSurfaceModule.cs`:

```csharp
using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Help;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Commands.Leader;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Persistence.Commands;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>The /help, /uptime, and /leader slash commands.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class CommandSurfaceModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>The custom-id prefix for the /leader server-pick select.</summary>
    public const string LeaderServerSelectId = "leader-server";

    /// <summary>The custom-id prefix for the /leader member-pick select (suffixed with the server id).</summary>
    public const string LeaderPromotePrefix = "leader-promote:";

    /// <summary>Lists the available in-game and slash commands.</summary>
    [SlashCommand("help", "Show the available commands")]
    public async Task HelpAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var muteStore = scope.ServiceProvider.GetRequiredService<IMuteStore>();
            var renderer = scope.ServiceProvider.GetRequiredService<HelpEmbedRenderer>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var known = await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false);

            string prefix = "!";
            var showNote = false;
            if (known.Count == 1)
            {
                prefix = await muteStore.GetPrefixAsync(Context.Guild.Id, known[0].Id).ConfigureAwait(false);
            }
            else if (known.Count > 1)
            {
                showNote = true;
            }

            var embed = renderer.Render(prefix, culture, showNote);
            await FollowupAsync(embed: embed, ephemeral: true).ConfigureAwait(false);
        }
    }

    /// <summary>Reports the bot process uptime.</summary>
    [SlashCommand("uptime", "Show how long the bot has been running")]
    public async Task UptimeAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var localizer = scope.ServiceProvider.GetRequiredService<ICommandLocalizer>();
            var uptime = scope.ServiceProvider.GetRequiredService<BotUptime>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var text = localizer.Get("uptime.ok", culture, DurationFormat.Compact(uptime.Elapsed));
            await RespondAsync(text, ephemeral: true).ConfigureAwait(false);
        }
    }

    /// <summary>Transfers in-game team leadership (ManageGuild). Opens a server or member select.</summary>
    [SlashCommand("leader", "Transfer in-game team leadership")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task LeaderAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var leader = scope.ServiceProvider.GetRequiredService<LeaderService>();
            var localizer = scope.ServiceProvider.GetRequiredService<ICommandLocalizer>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var known = await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false);
            if (known.Count == 0)
            {
                await FollowupAsync(localizer.Get("leader.noserver", culture), ephemeral: true).ConfigureAwait(false);
                return;
            }

            if (known.Count > 1)
            {
                var serverSelect = new SelectMenuBuilder()
                    .WithCustomId(LeaderServerSelectId)
                    .WithPlaceholder(localizer.Get("leader.pickserver", culture));
                foreach (var server in known.Take(SelectMenuBuilder.MaxOptionCount))
                {
                    serverSelect.AddOption(server.Name, server.Id.ToString());
                }

                var components = new ComponentBuilder().WithSelectMenu(serverSelect).Build();
                await FollowupAsync(localizer.Get("leader.pickserver", culture), components: components,
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            await ShowMemberSelectAsync(scope.ServiceProvider, leader, localizer, Context.Guild.Id, known[0].Id,
                culture).ConfigureAwait(false);
        }
    }

    private async Task ShowMemberSelectAsync(
        IServiceProvider provider,
        LeaderService leader,
        ICommandLocalizer localizer,
        ulong guildId,
        Guid serverId,
        string culture)
    {
        _ = provider;
        var result = await leader.GetMembersAsync(guildId, serverId, culture, CancellationToken.None)
            .ConfigureAwait(false);
        if (result.ErrorMessage is { } error)
        {
            await FollowupAsync(error, ephemeral: true).ConfigureAwait(false);
            return;
        }

        var select = new SelectMenuBuilder()
            .WithCustomId(string.Create(CultureInfo.InvariantCulture, $"{LeaderPromotePrefix}{serverId}"))
            .WithPlaceholder(localizer.Get("leader.pickmember", culture));
        foreach (var member in result.Members.Take(SelectMenuBuilder.MaxOptionCount))
        {
            select.AddOption(member.Name, member.SteamId.ToString(CultureInfo.InvariantCulture),
                isDefault: member.IsLeader);
        }

        var components = new ComponentBuilder().WithSelectMenu(select).Build();
        await FollowupAsync(localizer.Get("leader.pickmember", culture), components: components, ephemeral: true)
            .ConfigureAwait(false);
    }
}
```

- [ ] **Step 6: Create `LeaderComponentModule`**

Create `src/RustPlusBot.Features.Commands/Modules/LeaderComponentModule.cs`:

```csharp
using System.Globalization;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Commands.Leader;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>Handles the /leader server-pick and member-pick select callbacks.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class LeaderComponentModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Handles the server-pick step (multi-server guilds): re-shows a member select for the chosen server.</summary>
    /// <param name="values">The selected server id (one value).</param>
    [ComponentInteraction(CommandSurfaceModule.LeaderServerSelectId, ignoreGroupNames: true)]
    public async Task OnServerSelectedAsync(string[] values)
    {
        if (!await EnsureManageGuildAsync().ConfigureAwait(false) ||
            values is not [var raw] || !Guid.TryParse(raw, out var serverId))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var leader = scope.ServiceProvider.GetRequiredService<LeaderService>();
            var localizer = scope.ServiceProvider.GetRequiredService<ICommandLocalizer>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
            var result = await leader.GetMembersAsync(Context.Guild.Id, serverId, culture, CancellationToken.None)
                .ConfigureAwait(false);
            if (result.ErrorMessage is { } error)
            {
                await FollowupAsync(error, ephemeral: true).ConfigureAwait(false);
                return;
            }

            var select = new SelectMenuBuilder()
                .WithCustomId(string.Create(CultureInfo.InvariantCulture,
                    $"{CommandSurfaceModule.LeaderPromotePrefix}{serverId}"))
                .WithPlaceholder(localizer.Get("leader.pickmember", culture));
            foreach (var member in result.Members.Take(SelectMenuBuilder.MaxOptionCount))
            {
                select.AddOption(member.Name, member.SteamId.ToString(CultureInfo.InvariantCulture),
                    isDefault: member.IsLeader);
            }

            var components = new ComponentBuilder().WithSelectMenu(select).Build();
            await FollowupAsync(localizer.Get("leader.pickmember", culture), components: components, ephemeral: true)
                .ConfigureAwait(false);
        }
    }

    /// <summary>Handles the member-pick step: promotes the chosen member.</summary>
    /// <param name="serverIdRaw">The server id from the wildcard custom-id.</param>
    /// <param name="values">The selected member's Steam id (one value).</param>
    [ComponentInteraction($"{CommandSurfaceModule.LeaderPromotePrefix}*", ignoreGroupNames: true)]
    public async Task OnMemberSelectedAsync(string serverIdRaw, string[] values)
    {
        if (!await EnsureManageGuildAsync().ConfigureAwait(false) ||
            !Guid.TryParse(serverIdRaw, out var serverId) ||
            values is not [var rawSteamId] ||
            !ulong.TryParse(rawSteamId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var steamId))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var leader = scope.ServiceProvider.GetRequiredService<LeaderService>();

            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);

            // Re-resolve the member name for the success message; fall back to the SteamId.
            var team = await leader.GetMembersAsync(Context.Guild.Id, serverId, culture, CancellationToken.None)
                .ConfigureAwait(false);
            var name = team.Members.FirstOrDefault(m => m.SteamId == steamId)?.Name
                       ?? steamId.ToString(CultureInfo.InvariantCulture);

            var message = await leader
                .PromoteAsync(Context.Guild.Id, serverId, steamId, name, culture, CancellationToken.None)
                .ConfigureAwait(false);
            await FollowupAsync(message, ephemeral: true).ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureManageGuildAsync()
    {
        // Discord does not re-gate component callbacks, so re-check ManageGuild here.
        if (Context.User is SocketGuildUser user && user.GuildPermissions.ManageGuild)
        {
            return true;
        }

        await RespondAsync("You need the Manage Server permission.", ephemeral: true).ConfigureAwait(false);
        return false;
    }
}
```

- [ ] **Step 7: Run the full Commands suite + confirm it builds and passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS. Count = prior 70 + Task 2 (3) + Task 3 (4) + Task 4 (5) + Task 5 (1) = 83. Confirm the per-assembly total; a low count means a module failed to compile.

- [ ] **Step 8: Build the whole solution to confirm `DiscordBotService` discovery wiring compiles**

Run: `dotnet build RustPlusBot.slnx`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Features.Commands tests/RustPlusBot.Features.Commands.Tests
git commit -m "feat(3c): add /help, /uptime, /leader slash + component modules

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: `#info` team summary field

**Files:**
- Modify: `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`

**Interfaces:**
- Consumes: `IRustServerQuery.GetTeamInfoAsync` (added to the renderer ctor), `TeamInfoSnapshot`/`TeamMemberSnapshot`.
- Produces: a new embed field "Online: {online}/{total} · leader {name}" rendered only when `Status == Connected` and the team query returns non-null.

- [ ] **Step 1: Add the localization keys**

In `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`, add to `["en"]` (after `server.info.players.label`):

```csharp
["server.info.team.label"] = "Team",
["server.info.team.value"] = "{0}/{1} online · leader {2}",
```

And to `["fr"]`:

```csharp
["server.info.team.label"] = "Équipe",
["server.info.team.value"] = "{0}/{1} en ligne · chef {2}",
```

- [ ] **Step 2: Update the existing renderer tests for the new ctor arg, then add the team-field cases**

In `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`:

Add usings:

```csharp
using RustPlusBot.Features.Connections.Listening;
```

Every `new ServerInfoMessageRenderer(servers, connections, Loc)` becomes
`new ServerInfoMessageRenderer(servers, connections, query, Loc)`. Add a query
substitute to each `ServerInfo_*` test that returns null by default (field omitted),
e.g. at the top of each such test body:

```csharp
var query = Substitute.For<IRustServerQuery>();
query.GetTeamInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
    .Returns((TeamInfoSnapshot?)null);
```

(Add `using RustPlusBot.Features.Connections.Listening;` is above; NSubstitute is
already imported.) Then add two new tests:

```csharp
[Fact]
public async Task ServerInfo_Connected_WithTeam_ShowsTeamSummary()
{
    var serverId = Guid.NewGuid();
    var credId = Guid.NewGuid();
    var servers = Substitute.For<IServerService>();
    servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "S", Ip = "1.2.3.4", Port = 28015 });
    var connections = Substitute.For<IConnectionStore>();
    connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new DomainConnectionState
        {
            RustServerId = serverId, GuildId = 1, ActiveCredentialId = credId,
            Status = ConnectionStatus.Connected, PlayerCount = 5,
        });
    connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new List<PlayerCredential>
        {
            new() { Id = credId, GuildId = 1, RustServerId = serverId, OwnerUserId = 7, SteamId = 5UL,
                Status = CredentialStatus.Active },
        });
    var query = Substitute.For<IRustServerQuery>();
    query.GetTeamInfoAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new TeamInfoSnapshot(
            10UL,
            [
                new TeamMemberSnapshot(10UL, "alice", 0f, 0f, true, true,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
                new TeamMemberSnapshot(20UL, "bob", 0f, 0f, false, true,
                    DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch),
            ]));
    var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

    var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

    var body = string.Concat(payload.Embed!.Fields.Select(f => f.Value));
    Assert.Contains("1/2 online", body, StringComparison.Ordinal);
    Assert.Contains("alice", body, StringComparison.Ordinal);
}

[Fact]
public async Task ServerInfo_Connected_NullTeam_OmitsTeamSummary()
{
    var serverId = Guid.NewGuid();
    var credId = Guid.NewGuid();
    var servers = Substitute.For<IServerService>();
    servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "S", Ip = "1.2.3.4", Port = 28015 });
    var connections = Substitute.For<IConnectionStore>();
    connections.GetStateAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new DomainConnectionState
        {
            RustServerId = serverId, GuildId = 1, ActiveCredentialId = credId,
            Status = ConnectionStatus.Connected, PlayerCount = 5,
        });
    connections.ListPoolAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns(new List<PlayerCredential>
        {
            new() { Id = credId, GuildId = 1, RustServerId = serverId, OwnerUserId = 7, SteamId = 5UL,
                Status = CredentialStatus.Active },
        });
    var query = Substitute.For<IRustServerQuery>();
    query.GetTeamInfoAsync(1, serverId, Arg.Any<CancellationToken>())
        .Returns((TeamInfoSnapshot?)null);
    var renderer = new ServerInfoMessageRenderer(servers, connections, query, Loc);

    var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

    Assert.DoesNotContain("online", string.Concat(payload.Embed!.Fields.Select(f => f.Value)),
        StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2b: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter "FullyQualifiedName~RendererTests"`
Expected: COMPILE ERROR — `ServerInfoMessageRenderer` has no 4-arg ctor.

- [ ] **Step 3: Add the `IRustServerQuery` dependency and render the field**

In `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`:

Add `using RustPlusBot.Features.Connections.Listening;`. Change the primary ctor to:

```csharp
internal sealed class ServerInfoMessageRenderer(
    IServerService servers,
    IConnectionStore connections,
    IRustServerQuery query,
    ILocalizer localizer) : IMessageRenderer
```

(update the `<param>` doc comments to add `<param name="query">Live team query.</param>`).

Then, inside `RenderAsync`, after the `if (status == ConnectionStatus.Connected && state?.PlayerCount is int count)` block (after its closing brace, before `var eligible = …`), add:

```csharp
if (status == ConnectionStatus.Connected)
{
    var team = await query.GetTeamInfoAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
    if (team is not null)
    {
        var online = team.Members.Count(m => m.IsOnline);
        var leaderName = team.Members.FirstOrDefault(m => m.SteamId == team.LeaderSteamId)?.Name
                         ?? team.LeaderSteamId.ToString(CultureInfo.InvariantCulture);
        embed.AddField(
            localizer.Get("server.info.team.label", context.Culture),
            localizer.Get("server.info.team.value", context.Culture,
                online.ToString(CultureInfo.InvariantCulture),
                team.Members.Count.ToString(CultureInfo.InvariantCulture),
                leaderName));
    }
}
```

- [ ] **Step 4: Run the renderer tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter "FullyQualifiedName~RendererTests"`
Expected: PASS. The two new tests pass, and the 5 existing `ServerInfo_*` tests still pass (their query substitute returns null → field omitted → prior assertions unaffected).

- [ ] **Step 5: Run the FULL Workspace suite + confirm count**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests`
Expected: PASS, count = prior + 2. If low, a renderer ctor site was missed.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests
git commit -m "feat(3c): add team summary field to #info embed

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: Whole-feature verification, format gate, and plan commit

**Files:** none (verification + the gitignored plan doc).

- [ ] **Step 1: Run the entire test suite and read per-assembly counts**

Run: `dotnet test RustPlusBot.slnx`
Expected: ALL pass. Confirm each assembly's count is at or above its prior baseline (Connections +3, Commands +13, Workspace +2; others unchanged). A drop in any assembly = a missing fake/ctor.

- [ ] **Step 2: Confirm no EF model drift (this slice adds none)**

Run: `git diff --name-only develop... | grep -iE "Migrations|ModelSnapshot|DbContext|Domain/.*\.cs|Persistence/.*Configuration"`
Expected: NO output (3c touches no entities, migrations, or EF configuration). If the `has-pending-model-changes` tooling is available and works, also run it; if it errors with "Unable to retrieve project metadata" (a known tooling quirk), rely on the diff check.

- [ ] **Step 3: Run the ReSharper format gate**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Then: `git status --short`
Expected: jb may reorder members in the new files. Review the changes are cosmetic (member ordering, brace/collection-initializer wrapping), then stage and amend/commit them.

- [ ] **Step 4: Rebuild to confirm 0/0 after formatting**

Run: `dotnet build RustPlusBot.slnx`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 5: Commit any formatting changes and the plan doc**

```bash
git add -A
git add -f docs/superpowers/plans/2026-06-17-rustplusbot-3c-command-surfaces.md
git commit -m "chore(3c): apply jb formatting + commit plan

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 6: Push and open the PR**

```bash
git push -u origin feat/command-surfaces
gh pr create --base develop --title "Subsystem 3c: command Discord surfaces (/help, /uptime, /leader, #info team summary)" --body "$(cat <<'EOF'
Third slice of subsystem 3. Surfaces the in-game command framework into Discord.

- `/help` — ephemeral, lists in-game `!commands` (grouped, with the server's prefix) and the slash commands; EN/FR; rendered from a drift-guarded `CommandHelpCatalog`.
- `/uptime` — ephemeral bot-process uptime.
- `/leader` — ManageGuild; server-pick (multi-server) → member-select → `PromoteToLeaderAsync`.
- `#info` team summary — "online/total · leader" field, shown only while Connected, on the existing refresh path.

New seam method `PromoteToLeaderAsync` on `IRustServerQuery`/`IRustServerConnection`/`ConnectionSupervisor`/`RustPlusSocketSource`. No new project/entity/migration/event/timer.

The `#commands` channel relay is deferred to a future 3c-ii.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
EOF
)"
```

---

## Self-Review

**1. Spec coverage:**
- `/help` (in-game + slash, grouped, prefix, EN/FR, catalog-driven, drift guard) → Tasks 2, 3, 5. ✓
- `/uptime` (bot process) → Task 5 (reuses `BotUptime`). ✓
- `/leader` (ManageGuild, member select, server pick when >1, promote) → Tasks 1, 4, 5. ✓
- `#info` team summary (online/total + leader, only Connected, existing refresh) → Task 6. ✓
- `PromoteToLeaderAsync` seam (query/connection/supervisor/shim/fake) → Task 1. ✓
- Server targeting (auto-one / pick-multi / default-prefix-note) → Task 5 (`HelpAsync`/`LeaderAsync`) + Task 3 (note rendering). ✓
- No new project/entity/migration/event/options/service → respected throughout; verified in Task 7 Step 2. ✓
- Module assembly registration → Task 5 Steps 1–4. ✓
- Error/edge cases (no socket, no servers, empty team, promote fail, stale component re-gate) → Tasks 4 + 5 (`EnsureManageGuildAsync`, `GetMembersAsync` error paths). ✓

**2. Placeholder scan:** No "TBD"/"add error handling"/"similar to". Every code step shows full code. The `_ = provider;` discard in `ShowMemberSelectAsync` is intentional (the helper resolves nothing further; it could be simplified during implementation but is shown complete and compiling). ✓

**3. Type consistency:**
- `PromoteToLeaderAsync` query signature `(ulong, Guid, ulong, CancellationToken)→Task<bool>` and connection signature `(ulong, TimeSpan, CancellationToken)→Task<bool>` are consistent across Task 1 (definition), Task 4 (`LeaderService` consumes the query form), and the fake. ✓
- Custom-ids: `LeaderServerSelectId = "leader-server"` and `LeaderPromotePrefix = "leader-promote:"` defined in Task 5 `CommandSurfaceModule` and consumed in `LeaderComponentModule` (`$"{…LeaderPromotePrefix}*"`). ✓
- Localization keys used in `HelpEmbedRenderer`/`LeaderService`/`CommandSurfaceModule`/renderer all match the keys added in Tasks 2 and 6 (`help.*`, `uptime.ok`, `leader.*`, `server.info.team.*`). ✓
- `HelpEmbedRenderer.Render(string, string, bool)` signature consistent between Task 3 definition, its tests, and the `HelpAsync` call site. ✓
- `LeaderTeamResult`/`LeaderMemberOption` records consistent between Task 4 and the module consumers. ✓
