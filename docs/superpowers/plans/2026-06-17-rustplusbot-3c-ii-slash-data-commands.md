# 3c-ii — Slash Data Commands Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the read-only in-game `!command` data set into Discord as native ephemeral slash commands (`/pop`, `/time`, `/wipe`, `/online`, `/offline`, `/team`, `/alive`), reusing the existing in-game command handlers for identical replies.

**Architecture:** All in the existing `RustPlusBot.Features.Commands` project (no new project). A thin `ServerCommandModule` (Discord.Net `InteractionModuleBase`) holds the 7 slash commands; a `ServerResolver` picks the target server from an optional autocompleted `server` parameter; a `ServerQueryService` adapter builds a synthetic `CommandContext` and delegates to the existing `ICommandHandler` instances so in-game and slash replies cannot drift. Reply text reuses existing localization keys; only the server-resolution errors are new.

**Tech Stack:** .NET 10, C#, Discord.Net 3.20 (`InteractionModuleBase`, `AutocompleteHandler`), xUnit + NSubstitute, EF Core/SQLite (unchanged here), Roslynator + ReSharper (`jb`) strict analyzers.

## Global Constraints

- Target framework `net10.0`; build must be **0 warnings / 0 errors** under the strict analyzers (Roslynator + the repo's `.editorconfig`).
- The repo's real format gate is `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` (ReSharper) — run it (after `dotnet tool restore`) before any push; it reorders members Roslynator never flags. A pre-push hook also enforces it.
- New types are `internal sealed` unless a Discord.Net base type forces `public` (modules/autocomplete handlers must be `public`). Entities are not involved here.
- **No** new entity, migration, event, option, background service, or project. Verify no EF drift via "no Migrations/ModelSnapshot/DbContext/entity files changed on the branch" (the `dotnet ef migrations has-pending-model-changes` check is unreliable in this repo).
- All reply text is localized EN/FR via the existing `ICommandLocalizer`/`CommandLocalizationCatalog`; reuse existing keys, add only the new `command.server.*` keys.
- Replies are **ephemeral**. Slash commands require a guild (`Context.Guild`); guard null with an ephemeral message.
- `docs/superpowers/` specs/plans are **local-only** and must NEVER be committed/pushed (do not `git add -f` them).
- Branch: `feat/slash-data-commands` off `develop`.

**Reference signatures (already in the codebase — consume, do not redefine):**

- `ICommandHandler { string Name { get; } Task<string?> ExecuteAsync(CommandContext context, CancellationToken ct); }` (internal, in `RustPlusBot.Features.Commands.Dispatching`).
- `CommandContext(ulong GuildId, Guid ServerId, string Culture, ulong SenderSteamId, string SenderName, IReadOnlyList<string> Args)` (internal record).
- `ICommandLocalizer.Get(string key, string culture, params object[] args)` (resolves EN/FR with EN fallback).
- `IServerService.ListAsync(ulong guildId, CancellationToken ct = default) -> Task<IReadOnlyList<RustServer>>` and `GetAsync(ulong guildId, Guid serverId, CancellationToken ct = default) -> Task<RustServer?>` (in `RustPlusBot.Persistence.Servers`).
- `RustServer` has `Guid Id` and `string Name`.
- `IWorkspaceStore.GetCultureAsync(ulong guildId, CancellationToken ct = default) -> Task<string>` (in `RustPlusBot.Persistence.Workspace`).
- DI pattern in modules: `IServiceScopeFactory` injected; `var scope = scopeFactory.CreateAsyncScope(); await using (scope.ConfigureAwait(false)) { ... }`.

---

### Task 1: `ServerResolver` — pick the target server

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Servers/ServerResolver.cs`
- Create: `src/RustPlusBot.Features.Commands/Servers/ServerResolution.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Servers/ServerResolverTests.cs`
- Modify: `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs` (add `command.server.*` keys, EN + FR)

**Interfaces:**

- Consumes: `IServerService.ListAsync`, `ICommandLocalizer.Get`.
- Produces:
  - `record ServerResolution(Guid? ServerId, string? Name, string? ErrorMessage)` — exactly one of (`ServerId`+`Name`) or `ErrorMessage` is set.
  - `ServerResolver(IServerService servers, ICommandLocalizer localizer)` with
    `Task<ServerResolution> ResolveAsync(ulong guildId, string? serverArg, string culture, CancellationToken ct)`.

- [ ] **Step 1: Add the new localization keys**

In `CommandLocalizationCatalog.cs`, add these entries to the `["en"]` dictionary (anywhere among the `command.*` keys):

```csharp
["command.server.none"] = "No server is set up yet.",
["command.server.specify"] = "Multiple servers are set up — choose one with the server option.",
["command.server.unknown"] = "That server isn't set up.",
```

And the matching entries in the `["fr"]` dictionary:

```csharp
["command.server.none"] = "Aucun serveur n'est encore configuré.",
["command.server.specify"] = "Plusieurs serveurs sont configurés — choisissez-en un avec l'option serveur.",
["command.server.unknown"] = "Ce serveur n'est pas configuré.",
```

- [ ] **Step 2: Write the failing test**

Create `tests/RustPlusBot.Features.Commands.Tests/Servers/ServerResolverTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Commands.Servers;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Commands.Tests.Servers;

public sealed class ServerResolverTests
{
    private static readonly CommandLocalizer Loc = new(CommandLocalizationCatalog.Default);

    private static RustServer Server(Guid id, string name) => new() { Id = id, Name = name, GuildId = 1UL };

    private static ServerResolver Build(params RustServer[] servers)
    {
        var svc = Substitute.For<IServerService>();
        svc.ListAsync(1UL, Arg.Any<CancellationToken>()).Returns(servers);
        return new ServerResolver(svc, Loc);
    }

    [Fact]
    public async Task ReturnsError_WhenNoServers()
    {
        var r = await Build().ResolveAsync(1UL, null, "en", CancellationToken.None);
        Assert.Null(r.ServerId);
        Assert.Equal("No server is set up yet.", r.ErrorMessage);
    }

    [Fact]
    public async Task DefaultsToSingleServer_IgnoringArg()
    {
        var id = Guid.NewGuid();
        var r = await Build(Server(id, "Main")).ResolveAsync(1UL, null, "en", CancellationToken.None);
        Assert.Equal(id, r.ServerId);
        Assert.Equal("Main", r.Name);
        Assert.Null(r.ErrorMessage);
    }

    [Fact]
    public async Task ReturnsSpecifyError_WhenManyAndNoArg()
    {
        var r = await Build(Server(Guid.NewGuid(), "A"), Server(Guid.NewGuid(), "B"))
            .ResolveAsync(1UL, null, "en", CancellationToken.None);
        Assert.Null(r.ServerId);
        Assert.Equal("Multiple servers are set up — choose one with the server option.", r.ErrorMessage);
    }

    [Fact]
    public async Task ResolvesExplicitArg_WhenMany()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var r = await Build(Server(a, "A"), Server(b, "B"))
            .ResolveAsync(1UL, b.ToString(), "en", CancellationToken.None);
        Assert.Equal(b, r.ServerId);
        Assert.Equal("B", r.Name);
    }

    [Fact]
    public async Task ReturnsUnknownError_WhenArgNotRegistered()
    {
        var r = await Build(Server(Guid.NewGuid(), "A"))
            .ResolveAsync(1UL, Guid.NewGuid().ToString(), "en", CancellationToken.None);
        Assert.Null(r.ServerId);
        Assert.Equal("That server isn't set up.", r.ErrorMessage);
    }

    [Fact]
    public async Task ReturnsUnknownError_WhenArgUnparseable()
    {
        var r = await Build(Server(Guid.NewGuid(), "A"), Server(Guid.NewGuid(), "B"))
            .ResolveAsync(1UL, "not-a-guid", "en", CancellationToken.None);
        Assert.Null(r.ServerId);
        Assert.Equal("That server isn't set up.", r.ErrorMessage);
    }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/ --filter ServerResolverTests`
Expected: FAIL — `ServerResolver`/`ServerResolution` do not exist (compile error).

- [ ] **Step 4: Create `ServerResolution`**

Create `src/RustPlusBot.Features.Commands/Servers/ServerResolution.cs`:

```csharp
namespace RustPlusBot.Features.Commands.Servers;

/// <summary>The outcome of resolving a slash command's target server.</summary>
/// <param name="ServerId">The resolved server id, or null on error.</param>
/// <param name="Name">The resolved server name, or null on error.</param>
/// <param name="ErrorMessage">A localized error to show instead, or null on success.</param>
internal sealed record ServerResolution(Guid? ServerId, string? Name, string? ErrorMessage);
```

- [ ] **Step 5: Create `ServerResolver`**

Create `src/RustPlusBot.Features.Commands/Servers/ServerResolver.cs`:

```csharp
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Commands.Servers;

/// <summary>Picks the target server for a slash command from an optional (autocompleted) server argument.</summary>
/// <param name="servers">The guild's server list.</param>
/// <param name="localizer">Resolves the error messages.</param>
internal sealed class ServerResolver(IServerService servers, ICommandLocalizer localizer)
{
    /// <summary>Resolves the target server, or returns a localized error.</summary>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverArg">The raw server argument (a server id string) or null.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The resolved server, or an error message.</returns>
    public async Task<ServerResolution> ResolveAsync(
        ulong guildId,
        string? serverArg,
        string culture,
        CancellationToken cancellationToken)
    {
        var known = await servers.ListAsync(guildId, cancellationToken).ConfigureAwait(false);
        if (known.Count == 0)
        {
            return new ServerResolution(null, null, localizer.Get("command.server.none", culture));
        }

        if (string.IsNullOrWhiteSpace(serverArg))
        {
            if (known.Count == 1)
            {
                return new ServerResolution(known[0].Id, known[0].Name, null);
            }

            return new ServerResolution(null, null, localizer.Get("command.server.specify", culture));
        }

        if (Guid.TryParse(serverArg, out var id))
        {
            var match = known.FirstOrDefault(s => s.Id == id);
            if (match is not null)
            {
                return new ServerResolution(match.Id, match.Name, null);
            }
        }

        return new ServerResolution(null, null, localizer.Get("command.server.unknown", culture));
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/ --filter ServerResolverTests`
Expected: PASS (6 tests).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Servers/ServerResolver.cs \
        src/RustPlusBot.Features.Commands/Servers/ServerResolution.cs \
        src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs \
        tests/RustPlusBot.Features.Commands.Tests/Servers/ServerResolverTests.cs
git commit -m "feat(3c-ii): add ServerResolver for slash command server targeting"
```

---

### Task 2: `ServerQueryService` — run a named handler for the slash path

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Servers/ServerQueryService.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Servers/ServerQueryServiceTests.cs`

**Interfaces:**

- Consumes: `IEnumerable<ICommandHandler>` (the existing registered handlers), `CommandContext` (synthetic).
- Produces:
  - `ServerQueryService(IEnumerable<ICommandHandler> handlers)` with
    `Task<string?> RunAsync(string commandName, ulong guildId, Guid serverId, string culture, CancellationToken ct)`
    — resolves the handler whose `Name == commandName`, builds a synthetic `CommandContext`
    (empty args, `SenderSteamId = 0`, `SenderName = ""`), runs it, returns the reply; returns
    `null` if no handler matches (guarded — the module only passes known names).

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.Commands.Tests/Servers/ServerQueryServiceTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Servers;

namespace RustPlusBot.Features.Commands.Tests.Servers;

public sealed class ServerQueryServiceTests
{
    private sealed class StubHandler(string name, string? reply) : ICommandHandler
    {
        public CommandContext? Seen { get; private set; }
        public string Name => name;

        public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            Seen = context;
            return Task.FromResult(reply);
        }
    }

    [Fact]
    public async Task RunsMatchingHandler_AndReturnsReply()
    {
        var handler = new StubHandler("pop", "Pop: 5/100 (0 queued)");
        var service = new ServerQueryService([handler]);
        var server = Guid.NewGuid();

        var reply = await service.RunAsync("pop", 1UL, server, "en", CancellationToken.None);

        Assert.Equal("Pop: 5/100 (0 queued)", reply);
        Assert.NotNull(handler.Seen);
        Assert.Equal(1UL, handler.Seen!.GuildId);
        Assert.Equal(server, handler.Seen.ServerId);
        Assert.Equal("en", handler.Seen.Culture);
        Assert.Empty(handler.Seen.Args);
    }

    [Fact]
    public async Task ReturnsNull_WhenNoHandlerMatches()
    {
        var service = new ServerQueryService([new StubHandler("pop", "x")]);

        var reply = await service.RunAsync("time", 1UL, Guid.NewGuid(), "en", CancellationToken.None);

        Assert.Null(reply);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/ --filter ServerQueryServiceTests`
Expected: FAIL — `ServerQueryService` does not exist (compile error).

- [ ] **Step 3: Create `ServerQueryService`**

Create `src/RustPlusBot.Features.Commands/Servers/ServerQueryService.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;

namespace RustPlusBot.Features.Commands.Servers;

/// <summary>
/// Runs an in-game command handler for the slash-command path. Builds a synthetic context (no in-game
/// caller, no args) and delegates to the existing handler so slash and in-game replies are identical.
/// </summary>
internal sealed class ServerQueryService
{
    private readonly Dictionary<string, ICommandHandler> _handlers;

    /// <summary>Initializes the service from the registered handlers.</summary>
    /// <param name="handlers">The available in-game command handlers.</param>
    public ServerQueryService(IEnumerable<ICommandHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        _handlers = handlers.ToDictionary(h => h.Name, StringComparer.Ordinal);
    }

    /// <summary>Runs the named handler against a server and returns its localized reply.</summary>
    /// <param name="commandName">The handler name (e.g. "pop").</param>
    /// <param name="guildId">The owning guild snowflake.</param>
    /// <param name="serverId">The target server id.</param>
    /// <param name="culture">The guild culture.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The reply text, or null if the command is unknown or the handler produced no reply.</returns>
    public async Task<string?> RunAsync(
        string commandName,
        ulong guildId,
        Guid serverId,
        string culture,
        CancellationToken cancellationToken)
    {
        if (!_handlers.TryGetValue(commandName, out var handler))
        {
            return null;
        }

        var context = new CommandContext(guildId, serverId, culture, 0UL, string.Empty, []);
        return await handler.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/ --filter ServerQueryServiceTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Servers/ServerQueryService.cs \
        tests/RustPlusBot.Features.Commands.Tests/Servers/ServerQueryServiceTests.cs
git commit -m "feat(3c-ii): add ServerQueryService adapter over in-game handlers"
```

---

### Task 3: Slash surface — autocomplete handler, module, DI, registration test

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Servers/ServerAutocompleteHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Modules/ServerCommandModule.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs` (register `ServerResolver`, `ServerQueryService` scoped)
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs` (assert the two new services resolve)

**Interfaces:**

- Consumes: `ServerResolver.ResolveAsync`, `ServerQueryService.RunAsync`, `IWorkspaceStore.GetCultureAsync`, `IServerService.ListAsync`.
- Produces: the 7 slash commands and the `server` autocomplete. No type consumed by later tasks (this is the top of the stack).

- [ ] **Step 1: Create the autocomplete handler**

Create `src/RustPlusBot.Features.Commands/Servers/ServerAutocompleteHandler.cs`:

```csharp
using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Commands.Servers;

/// <summary>Offers the guild's registered servers (by name) as choices for the slash <c>server</c> option.</summary>
public sealed class ServerAutocompleteHandler : AutocompleteHandler
{
    /// <inheritdoc />
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(services);

        if (context.Guild is null)
        {
            return AutocompletionResult.FromSuccess();
        }

        var scope = services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var known = await servers.ListAsync(context.Guild.Id).ConfigureAwait(false);
            var results = known
                .Take(25)
                .Select(s => new AutocompleteResult(s.Name, s.Id.ToString()));
            return AutocompletionResult.FromSuccess(results);
        }
    }
}
```

Note: `AutocompleteResult` value is the server `Guid` string; `ServerResolver` parses it back. `CultureInfo` import kept only if needed — remove if the analyzer flags it as unused (the `.ToString()` of a `Guid` is culture-invariant, so the import may not be required; let the build tell you).

- [ ] **Step 2: Create the slash module**

Create `src/RustPlusBot.Features.Commands/Modules/ServerCommandModule.cs`:

```csharp
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Features.Commands.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>Read-only live-data slash commands (/pop, /time, /wipe, /online, /offline, /team, /alive).</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="logger">The logger.</param>
public sealed partial class ServerCommandModule(
    IServiceScopeFactory scopeFactory,
    ILogger<ServerCommandModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Shows the current player count.</summary>
    [SlashCommand("pop", "Show the current player count")]
    public Task PopAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("pop", server);

    /// <summary>Shows the in-game time.</summary>
    [SlashCommand("time", "Show the in-game time")]
    public Task TimeAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("time", server);

    /// <summary>Shows how long ago the server wiped.</summary>
    [SlashCommand("wipe", "Show how long ago the server wiped")]
    public Task WipeAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("wipe", server);

    /// <summary>Lists online teammates.</summary>
    [SlashCommand("online", "List online teammates")]
    public Task OnlineAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("online", server);

    /// <summary>Lists offline teammates.</summary>
    [SlashCommand("offline", "List offline teammates")]
    public Task OfflineAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("offline", server);

    /// <summary>Lists all team members.</summary>
    [SlashCommand("team", "List all team members")]
    public Task TeamAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("team", server);

    /// <summary>Shows how long each teammate has survived.</summary>
    [SlashCommand("alive", "Show how long each teammate has survived")]
    public Task AliveAsync(
        [Summary("server", "Which server (only needed if more than one)")]
        [Autocomplete(typeof(ServerAutocompleteHandler))]
        string? server = null) => RunAsync("alive", server);

    private async Task RunAsync(string commandName, string? server)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        try
        {
            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
                var resolver = scope.ServiceProvider.GetRequiredService<ServerResolver>();
                var query = scope.ServiceProvider.GetRequiredService<ServerQueryService>();

                var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);
                var resolution = await resolver.ResolveAsync(Context.Guild.Id, server, culture, CancellationToken.None)
                    .ConfigureAwait(false);
                if (resolution.ErrorMessage is { } error)
                {
                    await FollowupAsync(error, ephemeral: true).ConfigureAwait(false);
                    return;
                }

                var reply = await query.RunAsync(commandName, Context.Guild.Id, resolution.ServerId!.Value, culture,
                    CancellationToken.None).ConfigureAwait(false);
                await FollowupAsync(reply ?? "Something went wrong.", ephemeral: true).ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Broad catch: an interaction failure must not surface as a Discord "interaction failed".
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCommandFaulted(logger, ex, commandName);
            await FollowupAsync("Something went wrong.", ephemeral: true).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Slash data command '{Command}' faulted.")]
    private static partial void LogCommandFaulted(ILogger logger, Exception exception, string command);
}
```

- [ ] **Step 3: Register the two services**

In `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`, add to `AddCommands` after the `LeaderService` registration (line ~46), keeping the existing `using` for `RustPlusBot.Features.Commands.Servers`:

```csharp
        services.AddScoped<ServerResolver>();
        services.AddScoped<ServerQueryService>();
```

Add the namespace import at the top of the file:

```csharp
using RustPlusBot.Features.Commands.Servers;
```

(The autocomplete handler and the module are discovered via the already-registered `InteractionModuleAssembly`; they are instantiated by the InteractionService, not the DI container directly, so they need no `services.Add...` registration.)

- [ ] **Step 4: Update the registration test**

In `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`, inside `Dispatcher_and_handlers_resolve`, after the existing `CommandDispatcher` assertion inside the `using var scope` block, add:

```csharp
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServerResolver>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ServerQueryService>());
```

Add the import at the top of the test file:

```csharp
using RustPlusBot.Features.Commands.Servers;
```

(The handler count stays **12** — this slice adds no `ICommandHandler`.)

- [ ] **Step 5: Build and run the full Commands suite**

Run: `dotnet build src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj`
Expected: 0 warnings, 0 errors.

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/`
Expected: PASS — all existing tests plus the new `ServerResolverTests` (6), `ServerQueryServiceTests` (2), and the augmented `CommandRegistrationTests`.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Servers/ServerAutocompleteHandler.cs \
        src/RustPlusBot.Features.Commands/Modules/ServerCommandModule.cs \
        src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs \
        tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs
git commit -m "feat(3c-ii): add /pop /time /wipe /online /offline /team /alive slash commands"
```

---

### Task 4: Whole-suite verification + format gate

**Files:** none (verification only).

- [ ] **Step 1: Run the full test suite and read per-assembly counts**

Run: `dotnet test RustPlusBot.slnx`
Expected: All assemblies build and pass. Confirm the Commands assembly count rose by 8 (6 + 2 new tests) and no other assembly's count *dropped* (a drop means an assembly failed to build — e.g. a test double missing a new member; there are none expected here since no public interface changed).

- [ ] **Step 2: Verify no EF model drift**

Run: `git diff --name-only develop...HEAD`
Expected: Only files under `src/RustPlusBot.Features.Commands/` and `tests/RustPlusBot.Features.Commands.Tests/`. No `*Migrations*`, `*ModelSnapshot*`, `*DbContext*`, or `Domain/` entity files.

- [ ] **Step 3: Run the format gate**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Then: `git status --short`
Expected: If `jb` reordered anything, review the diff (member ordering / param wrapping only — no behavior change), then commit it:

```bash
git add -A
git commit -m "chore(3c-ii): apply jb ReformatAndReorder"
```

- [ ] **Step 4: Final build check**

Run: `dotnet build RustPlusBot.slnx`
Expected: 0 warnings, 0 errors.

---

## Self-Review

**Spec coverage:**

- 7 slash commands (`/pop`/`/time`/`/wipe`/`/online`/`/offline`/`/team`/`/alive`) → Task 3 module. ✓
- Ephemeral replies → `DeferAsync(ephemeral: true)` + `FollowupAsync(ephemeral: true)`. ✓
- `server` parameter + autocomplete → Task 3 (`[Autocomplete]` + `ServerAutocompleteHandler`). ✓
- Server targeting rule (0/1/many/explicit) → Task 1 `ServerResolver`. ✓
- Reuse existing handlers (no drift) → Task 2 `ServerQueryService` synthetic context. ✓
- Reuse existing localization keys; only new `command.server.*` → Task 1. ✓
- No new entity/migration/event/option/service/project → confirmed; Task 4 verifies no drift. ✓
- Error handling (guild guard, not-connected via handlers, resolution errors, unexpected exception) → Task 3 module + Task 1. ✓
- No cooldown → none added. ✓
- Testing: `ServerQueryService` light delegation + unknown-name guard (Task 2), `ServerResolver` 0/1/many/explicit-valid/unknown/unparseable (Task 1), module + autocomplete untested (Task 3), registration tripwire (Task 3). ✓

**Placeholder scan:** No TBD/TODO. Every code step shows full code. The one soft note (the `CultureInfo` import in the autocomplete handler "remove if unused") is a concrete instruction, not a placeholder.

**Type consistency:** `ServerResolution(Guid? ServerId, string? Name, string? ErrorMessage)` defined in Task 1, consumed in Task 3 as `resolution.ErrorMessage` / `resolution.ServerId!.Value`. ✓ `ServerQueryService.RunAsync(string, ulong, Guid, string, CancellationToken)` defined in Task 2, called in Task 3 with `(commandName, guildId, serverId, culture, ct)`. ✓ `ServerResolver.ResolveAsync(ulong, string?, string, CancellationToken)` defined Task 1, called Task 3. ✓ `CommandContext` 6-arg shape matches the codebase. ✓
