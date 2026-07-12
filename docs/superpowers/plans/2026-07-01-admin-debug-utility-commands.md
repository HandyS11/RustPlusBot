# Admin debug/utility commands — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add six operator-facing slash commands — `/ping`, `/status`, `/workspace repair`, `/workspace rebuild`, `/workspace purge`, `/admin reset-database` — reusing the existing gating model.

**Architecture:** Two new pure services carry the real logic and the tests (`DatabaseMaintenanceService` in Persistence; `GuildPurgeService` in Features.Workspace). Three thin Discord interaction modules invoke them: a new `DiagnosticsModule` and `MaintenanceModule` in Features.Commands, plus new commands on the existing `WorkspaceAdminModule`. Modules are coverage-excluded per repo convention, so they are build-verified, not unit-tested.

**Tech Stack:** C# / .NET 10, Discord.Net (`InteractionModuleBase<SocketInteractionContext>`), EF Core 10 (SQLite default), xUnit + NSubstitute, in-memory SQLite test harness.

## Global Constraints

- Solution file is `RustPlusBot.slnx` (no `.sln`). Build/test target the slnx or individual csproj.
- **Hard CI gate:** `dotnet jb cleanupcode --profile=ReformatAndReorder` must produce **zero** diff. Run it before the final commit of each task.
- Sonar quality gate is enforced; do not introduce new code smells.
- Destructive commands reuse the existing gate: `[RequireUserPermission(GuildPermission.ManageGuild)]` **and** a runtime `EnableDangerCommands` flag. No new bot-owner concept.
- Localization: `/ping`, `/status`, and all new admin commands use **plain English** (matching `/workspace reset`, `/setup`). Do **not** add `.resx` keys.
- Interaction modules are auto-discovered from assemblies already registered via `InteractionModuleAssembly` (Features.Commands and Features.Workspace). New modules in those assemblies need no extra registration; new **services** and **options** do.
- xUnit style: `[Fact]`/`[Theory]`, plain `Assert.*` (not FluentAssertions). NSubstitute for fakes. `<Using Include="Xunit" />` is set in test projects (no `using Xunit;` needed).

## Setup (do once, before Task 1)

- [ ] **Create the feature branch off `develop`** (main checkout, no worktree):

```bash
cd /home/handys11/Dev/RustPlusBot
git checkout develop
git pull --ff-only
git checkout -b feat/admin-utility-commands
```

---

### Task 1: `DatabaseMaintenanceService` (whole-DB row wipe)

**Files:**

- Create: `src/RustPlusBot.Persistence/Maintenance/IDatabaseMaintenanceService.cs`
- Create: `src/RustPlusBot.Persistence/Maintenance/DatabaseMaintenanceService.cs`
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs` (register the service)
- Test: `tests/RustPlusBot.Persistence.Tests/Maintenance/DatabaseMaintenanceServiceTests.cs`

**Interfaces:**

- Consumes: `BotDbContext` (scoped), `context.Model.GetEntityTypes()`, `context.Database` (`DatabaseFacade`).
- Produces: `IDatabaseMaintenanceService.ClearAllAsync(CancellationToken)` — deletes every row in every table, keeps the schema. Consumed by Task 4.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Persistence.Tests/Maintenance/DatabaseMaintenanceServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Persistence.Maintenance;

namespace RustPlusBot.Persistence.Tests.Maintenance;

public sealed class DatabaseMaintenanceServiceTests
{
    [Fact]
    public async Task ClearAllAsync_EmptiesEveryTable_AndKeepsSchema()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        context.RustServers.Add(new RustServer { GuildId = 1, Name = "A", Ip = "a", Port = 1 });
        context.RustServers.Add(new RustServer { GuildId = 2, Name = "B", Ip = "b", Port = 2 });
        context.GuildSettings.Add(new GuildSettings { GuildId = 1, Culture = "en" });
        context.GuildSettings.Add(new GuildSettings { GuildId = 2, Culture = "fr" });
        await context.SaveChangesAsync();

        var service = new DatabaseMaintenanceService(context);
        await service.ClearAllAsync();

        Assert.Empty(await context.RustServers.ToListAsync());
        Assert.Empty(await context.GuildSettings.ToListAsync());

        // Schema still exists: a fresh insert succeeds.
        context.GuildSettings.Add(new GuildSettings { GuildId = 3, Culture = "en" });
        await context.SaveChangesAsync();
        Assert.Single(await context.GuildSettings.ToListAsync());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj --filter DatabaseMaintenanceServiceTests`
Expected: FAIL — `DatabaseMaintenanceService` / `IDatabaseMaintenanceService` do not exist (compile error).

- [ ] **Step 3: Write the interface**

Create `src/RustPlusBot.Persistence/Maintenance/IDatabaseMaintenanceService.cs`:

```csharp
namespace RustPlusBot.Persistence.Maintenance;

/// <summary>Database-wide maintenance operations.</summary>
public interface IDatabaseMaintenanceService
{
    /// <summary>Deletes every row in every table across all guilds, preserving the schema.</summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when all rows have been deleted.</returns>
    Task ClearAllAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Write the implementation**

Create `src/RustPlusBot.Persistence/Maintenance/DatabaseMaintenanceService.cs`:

```csharp
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace RustPlusBot.Persistence.Maintenance;

/// <summary>Clears every table's rows while keeping the schema (a live-safe "factory reset").</summary>
/// <param name="context">The bot database context.</param>
public sealed class DatabaseMaintenanceService(BotDbContext context) : IDatabaseMaintenanceService
{
    /// <inheritdoc />
    public async Task ClearAllAsync(CancellationToken cancellationToken = default)
    {
        var tables = context.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Keep one connection open across every statement so the FK pragma persists
        // (with per-statement connections the pragma would reset before the DELETEs).
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF", cancellationToken)
                .ConfigureAwait(false);

            foreach (var table in tables)
            {
                // Table names come from the EF model (never user input); the identifier guard keeps
                // the raw statement demonstrably injection-safe for the Sonar gate.
                if (!IsSafeIdentifier(table!))
                {
                    continue;
                }

                var sql = string.Create(CultureInfo.InvariantCulture, $"DELETE FROM \"{table}\"");
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            }

            await context.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON", cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static bool IsSafeIdentifier(string identifier) =>
        identifier.All(c => char.IsLetterOrDigit(c) || c == '_');
}
```

- [ ] **Step 5: Register the service**

In `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`, inside `AddBotPersistence`, next to `services.AddScoped<IServerService, ServerService>();` add:

```csharp
services.AddScoped<IDatabaseMaintenanceService, DatabaseMaintenanceService>();
```

Add the using if not present at the top of the file:

```csharp
using RustPlusBot.Persistence.Maintenance;
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj --filter DatabaseMaintenanceServiceTests`
Expected: PASS.

- [ ] **Step 7: Format + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Persistence/Maintenance tests/RustPlusBot.Persistence.Tests/Maintenance src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs
git commit -m "feat: add DatabaseMaintenanceService (clear all rows, keep schema)"
```

---

### Task 2: `GuildPurgeService` (per-guild data purge)

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Teardown/IGuildPurgeService.cs`
- Create: `src/RustPlusBot.Features.Workspace/Teardown/GuildPurgeService.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs` (register the service)
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Teardown/GuildPurgeServiceTests.cs`

**Interfaces:**

- Consumes: `IWorkspaceTeardownService.ResetGuildAsync(ulong, CancellationToken)` (internal, existing), `IServerService.ListAsync` / `IServerService.RemoveAsync` (public, existing — `RemoveAsync` cascades all per-server rows including credentials), `BotDbContext` (for guild-keyed deletes).
- Produces: `IGuildPurgeService.PurgeGuildAsync(ulong guildId, CancellationToken)` — teardown + delete this guild's servers (cascade) + guild-keyed rows (`EventSubscription`, `PairedEntity`, `GuildSettings`). Consumed by Task 5.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Teardown/GuildPurgeServiceTests.cs`:

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Entities;
using RustPlusBot.Domain.Events;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Switches;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Tests.Teardown;

public sealed class GuildPurgeServiceTests
{
    private static BotDbContext NewContext(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connection).Options;
        var context = new BotDbContext(options);
        context.Database.Migrate();
        return context;
    }

    [Fact]
    public async Task PurgeGuild_RemovesTargetGuildRows_AndLeavesOtherGuildIntact()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        await using var _ = connection;
        await using var context = NewContext(connection);

        var serverA = new RustServer { GuildId = 1, Name = "A", Ip = "a", Port = 1 };
        var serverB = new RustServer { GuildId = 2, Name = "B", Ip = "b", Port = 2 };
        context.RustServers.AddRange(serverA, serverB);
        context.SmartSwitches.Add(new SmartSwitch { GuildId = 1, ServerId = serverA.Id, EntityId = 10, Name = "sw" });
        context.ConnectionStates.Add(new ConnectionState { RustServerId = serverA.Id, GuildId = 1, Status = ConnectionStatus.Connected });
        context.EventSubscriptions.Add(new EventSubscription { GuildId = 1, RustServerId = serverA.Id, EventKey = "cargo" });
        context.EventSubscriptions.Add(new EventSubscription { GuildId = 2, RustServerId = serverB.Id, EventKey = "cargo" });
        context.PairedEntities.Add(new PairedEntity { GuildId = 1, RustServerId = serverA.Id, EntityId = 5, Name = "dev" });
        context.GuildSettings.Add(new GuildSettings { GuildId = 1, Culture = "en" });
        context.GuildSettings.Add(new GuildSettings { GuildId = 2, Culture = "fr" });
        await context.SaveChangesAsync();

        var teardown = Substitute.For<IWorkspaceTeardownService>();
        var service = new GuildPurgeService(context, new ServerService(context), teardown);

        await service.PurgeGuildAsync(1);

        await teardown.Received(1).ResetGuildAsync(1, Arg.Any<CancellationToken>());
        Assert.Empty(await context.RustServers.Where(s => s.GuildId == 1).ToListAsync());
        Assert.Empty(await context.SmartSwitches.ToListAsync());
        Assert.Empty(await context.ConnectionStates.ToListAsync());
        Assert.Empty(await context.EventSubscriptions.Where(e => e.GuildId == 1).ToListAsync());
        Assert.Empty(await context.PairedEntities.Where(p => p.GuildId == 1).ToListAsync());
        Assert.Empty(await context.GuildSettings.Where(g => g.GuildId == 1).ToListAsync());

        // Guild 2 untouched.
        Assert.Single(await context.RustServers.Where(s => s.GuildId == 2).ToListAsync());
        Assert.Single(await context.EventSubscriptions.Where(e => e.GuildId == 2).ToListAsync());
        Assert.Single(await context.GuildSettings.Where(g => g.GuildId == 2).ToListAsync());
    }
}
```

> Note: verify the `SmartSwitch` property names (`ServerId`, `EntityId`, `Name`) against `src/RustPlusBot.Domain/Switches/SmartSwitch.cs` before running; adjust the seed if they differ. The assertions on `RustServers`/`ConnectionStates`/`EventSubscriptions`/`PairedEntities`/`GuildSettings` use verified property names.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj --filter GuildPurgeServiceTests`
Expected: FAIL — `GuildPurgeService` / `IGuildPurgeService` do not exist (compile error).

- [ ] **Step 3: Write the interface**

Create `src/RustPlusBot.Features.Workspace/Teardown/IGuildPurgeService.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Deletes all of a guild's data: provisioned channels plus its domain rows.</summary>
internal interface IGuildPurgeService
{
    /// <summary>Purges one guild back to a just-joined state (channels + servers + guild-scoped rows).</summary>
    /// <param name="guildId">The guild to purge.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that completes when the guild's data has been removed.</returns>
    Task PurgeGuildAsync(ulong guildId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Write the implementation**

Create `src/RustPlusBot.Features.Workspace/Teardown/GuildPurgeService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Purges a guild: tears down provisioned channels, then deletes its domain rows.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="servers">Server management (RemoveAsync cascades all per-server rows).</param>
/// <param name="teardown">Removes provisioned Discord channels/categories/messages.</param>
internal sealed class GuildPurgeService(
    BotDbContext context,
    IServerService servers,
    IWorkspaceTeardownService teardown) : IGuildPurgeService
{
    /// <inheritdoc />
    public async Task PurgeGuildAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        // 1) Delete provisioned Discord channels/categories/messages (Discord side + records).
        await teardown.ResetGuildAsync(guildId, cancellationToken).ConfigureAwait(false);

        // 2) Remove each server; the RustServer FK cascade clears its per-server rows
        //    (connection state, command/map settings, switches, alarms, storage monitors, credentials).
        var known = await servers.ListAsync(guildId, cancellationToken).ConfigureAwait(false);
        foreach (var server in known)
        {
            await servers.RemoveAsync(guildId, server.Id, cancellationToken).ConfigureAwait(false);
        }

        // 3) Delete guild-keyed rows that have no cascade FK to RustServer.
        await context.EventSubscriptions.Where(e => e.GuildId == guildId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.PairedEntities.Where(p => p.GuildId == guildId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.GuildSettings.Where(g => g.GuildId == guildId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Register the service**

In `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`, next to the teardown registrations (after the `IServerWorkspaceRemover` line), add:

```csharp
services.AddScoped<IGuildPurgeService, GuildPurgeService>();
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj --filter GuildPurgeServiceTests`
Expected: PASS.

- [ ] **Step 7: Format + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Workspace/Teardown tests/RustPlusBot.Features.Workspace.Tests/Teardown/GuildPurgeServiceTests.cs src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs
git commit -m "feat: add GuildPurgeService (per-guild data purge)"
```

---

### Task 3: `DiagnosticsModule` — `/ping` and `/status`

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Modules/DiagnosticsModule.cs`

**Interfaces:**

- Consumes: `BotUptime` (internal singleton, existing), `IServerService.ListAsync`, `IConnectionStore.GetStateAsync` (existing), `DurationFormat.Compact` (existing), `Context.Client.Latency` / `Context.Client.Guilds` (Discord.Net).
- Produces: two auto-discovered slash commands. No service surface.

This module is thin Discord I/O and is coverage-excluded per repo convention (like `CommandSurfaceModule`), so it is build-verified rather than unit-tested.

- [ ] **Step 1: Write the module**

Create `src/RustPlusBot.Features.Commands/Modules/DiagnosticsModule.cs`:

```csharp
using System.Diagnostics;
using System.Globalization;
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Persistence.Connections;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>Read-only diagnostics: /ping and /status.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class DiagnosticsModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Reports Discord gateway latency and the REST ack round-trip.</summary>
    [SlashCommand("ping", "Show the bot's Discord latency")]
    public async Task PingAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        stopwatch.Stop();

        var text = string.Create(CultureInfo.InvariantCulture,
            $"Pong! Gateway: {Context.Client.Latency} ms · Response: {stopwatch.ElapsedMilliseconds} ms");
        await FollowupAsync(text, ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Shows uptime, latency, per-server connection status, and counts.</summary>
    [SlashCommand("status", "Show bot health and connection status")]
    public async Task StatusAsync()
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
            var uptime = scope.ServiceProvider.GetRequiredService<BotUptime>();
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var connections = scope.ServiceProvider.GetRequiredService<IConnectionStore>();

            var known = await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false);

            var embed = new EmbedBuilder()
                .WithTitle("Bot status")
                .AddField("Uptime", DurationFormat.Compact(uptime.Elapsed), inline: true)
                .AddField("Gateway latency",
                    string.Create(CultureInfo.InvariantCulture, $"{Context.Client.Latency} ms"), inline: true)
                .AddField("Guilds",
                    Context.Client.Guilds.Count.ToString(CultureInfo.InvariantCulture), inline: true)
                .AddField("Servers (this guild)",
                    known.Count.ToString(CultureInfo.InvariantCulture), inline: true);

            foreach (var server in known)
            {
                var state = await connections.GetStateAsync(Context.Guild.Id, server.Id).ConfigureAwait(false);
                var line = state is null
                    ? "unknown"
                    : string.Create(CultureInfo.InvariantCulture,
                        $"{state.Status} · {(state.PlayerCount?.ToString(CultureInfo.InvariantCulture) ?? "?")} players");
                embed.AddField(server.Name, line);
            }

            await FollowupAsync(ephemeral: true, embed: embed.Build()).ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 2: Build to verify the module compiles and is discoverable**

Run: `dotnet build src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj`
Expected: Build succeeded, 0 errors. (`DiagnosticsModule` lives in the already-registered Features.Commands assembly, so `InteractionService` auto-discovers `/ping` and `/status`.)

- [ ] **Step 3: Format + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Commands/Modules/DiagnosticsModule.cs
git commit -m "feat: add /ping and /status diagnostics commands"
```

---

### Task 4: `MaintenanceModule` — `/admin reset-database`

**Files:**

- Create: `src/RustPlusBot.Features.Commands/MaintenanceOptions.cs`
- Create: `src/RustPlusBot.Features.Commands/Modules/MaintenanceModule.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs` (register options)
- Modify: `src/RustPlusBot.Host/Program.cs` (bind options to the `Workspace` section)

**Interfaces:**

- Consumes: `IDatabaseMaintenanceService.ClearAllAsync` (Task 1), `IOptions<MaintenanceOptions>`.
- Produces: `/admin reset-database` (auto-discovered). `MaintenanceOptions.EnableDangerCommands` bound to the same `Workspace` config section, so one operator flag governs all danger commands.

Module is thin Discord I/O → build-verified, not unit-tested.

- [ ] **Step 1: Write the options type**

Create `src/RustPlusBot.Features.Commands/MaintenanceOptions.cs`:

```csharp
namespace RustPlusBot.Features.Commands;

/// <summary>
/// Gates the dangerous maintenance command (/admin reset-database). Bound to the same "Workspace"
/// config section as WorkspaceOptions, so a single EnableDangerCommands flag governs all danger commands.
/// </summary>
public sealed class MaintenanceOptions
{
    /// <summary>Enables the dangerous /admin reset-database command.</summary>
    public bool EnableDangerCommands { get; set; }
}
```

- [ ] **Step 2: Write the module**

Create `src/RustPlusBot.Features.Commands/Modules/MaintenanceModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RustPlusBot.Persistence.Maintenance;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>Dangerous, danger-gated bot maintenance commands.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="options">Gates the command behind the danger flag.</param>
[Group("admin", "Bot maintenance (dangerous)")]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class MaintenanceModule(
    IServiceScopeFactory scopeFactory,
    IOptions<MaintenanceOptions> options) : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Wipes all bot data across all guilds after a typed confirmation.</summary>
    /// <param name="confirm">Must equal the literal "RESET" to proceed.</param>
    [SlashCommand("reset-database", "Wipe ALL bot data across ALL servers (dangerous)")]
    public async Task ResetDatabaseAsync(
        [Summary("confirm", "Type RESET to confirm")] string confirm)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!options.Value.EnableDangerCommands)
        {
            await RespondAsync("Developer commands are disabled.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        if (!string.Equals(confirm, "RESET", StringComparison.Ordinal))
        {
            await RespondAsync("Type `RESET` exactly to confirm the database wipe.", ephemeral: true)
                .ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var maintenance = scope.ServiceProvider.GetRequiredService<IDatabaseMaintenanceService>();
            await maintenance.ClearAllAsync().ConfigureAwait(false);
            await FollowupAsync("Database cleared. **Restart the bot** for a clean state.", ephemeral: true)
                .ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 3: Register the options in AddCommands**

In `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`, after `services.AddSingleton<BotUptime>();` add:

```csharp
services.AddOptions<MaintenanceOptions>();
```

- [ ] **Step 4: Bind the options in the Host**

In `src/RustPlusBot.Host/Program.cs`, immediately after `builder.Services.AddCommands();` (line 73) add:

```csharp
builder.Services.AddOptions<MaintenanceOptions>()
    .Bind(builder.Configuration.GetSection("Workspace"));
```

Add the using at the top of `Program.cs` if not already present:

```csharp
using RustPlusBot.Features.Commands;
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build RustPlusBot.slnx`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Format + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Commands/MaintenanceOptions.cs src/RustPlusBot.Features.Commands/Modules/MaintenanceModule.cs src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs src/RustPlusBot.Host/Program.cs
git commit -m "feat: add /admin reset-database command"
```

---

### Task 5: Extend `WorkspaceAdminModule` — `/workspace repair`, `rebuild`, `purge`

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/Modules/WorkspaceAdminModule.cs`

**Interfaces:**

- Consumes: `IWorkspaceReconciler.HealGuildAsync` / `ReconcileGlobalAsync` / `ReconcileServerAsync` (internal, existing), `IWorkspaceTeardownService.ResetGuildAsync` (internal, existing), `IServerService.ListAsync` (existing), `IGuildPurgeService.PurgeGuildAsync` (Task 2), the existing `EnsureEnabledAsync` gate.
- Produces: three new commands + two confirm-button callbacks on the existing `[Group("workspace")]` module.

Module is thin Discord I/O → build-verified. The logic it calls (`HealGuildAsync`, `GuildPurgeService`) is already covered by tests.

- [ ] **Step 1: Add the new usings and command-id constants**

In `src/RustPlusBot.Features.Workspace/Modules/WorkspaceAdminModule.cs`, ensure these usings are present at the top (add the missing ones):

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence.Servers;
```

Below the existing `ConfirmResetId` constant, add:

```csharp
    /// <summary>Custom id for the rebuild confirmation button.</summary>
    public const string ConfirmRebuildId = "workspace:rebuild:confirm";

    /// <summary>Custom id for the purge confirmation button.</summary>
    public const string ConfirmPurgeId = "workspace:purge:confirm";
```

- [ ] **Step 2: Add `/workspace repair` (non-destructive, no danger gate)**

Add this method to the class (after `SimulateServerAsync`):

```csharp
    /// <summary>Recreates any missing categories/channels/messages without deleting data.</summary>
    [SlashCommand("repair", "Recreate any missing bot channels without deleting data")]
    public async Task RepairAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            await reconciler.HealGuildAsync(Context.Guild.Id).ConfigureAwait(false);
            await FollowupAsync("Workspace repaired.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
```

- [ ] **Step 3: Add `/workspace rebuild` (danger-gated) + its confirm callback**

Add these two methods:

```csharp
    /// <summary>Prompts to delete and re-provision the entire workspace (dev-gated).</summary>
    [SlashCommand("rebuild", "Delete and re-create all the bot's channels here (dangerous)")]
    public async Task RebuildAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        var components = new ComponentBuilder()
            .WithButton("Confirm rebuild", ConfirmRebuildId, ButtonStyle.Danger)
            .Build();
        await RespondAsync(
            "This deletes every provisioned channel and re-creates them from scratch. Confirm?",
            ephemeral: true, components: components).ConfigureAwait(false);
    }

    /// <summary>Executes the rebuild after confirmation.</summary>
    [ComponentInteraction(ConfirmRebuildId)]
    public async Task ConfirmRebuildAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var teardown = scope.ServiceProvider.GetRequiredService<IWorkspaceTeardownService>();
            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();

            await teardown.ResetGuildAsync(Context.Guild.Id).ConfigureAwait(false);

            var result = await reconciler.ReconcileGlobalAsync(Context.Guild.Id).ConfigureAwait(false);
            if (result.Status == ReconcileStatus.MissingPermissions)
            {
                await FollowupAsync(
                    $"I'm missing required permissions: {string.Join(", ", result.MissingPermissions)}.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            foreach (var server in await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false))
            {
                await reconciler.ReconcileServerAsync(Context.Guild.Id, server.Id).ConfigureAwait(false);
            }

            await FollowupAsync("Workspace rebuilt.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
```

- [ ] **Step 4: Add `/workspace purge` (danger-gated) + its confirm callback**

Add these two methods:

```csharp
    /// <summary>Prompts to delete all of this guild's data (dev-gated).</summary>
    [SlashCommand("purge", "Delete ALL of this server's bot data (servers, settings, channels) (dangerous)")]
    public async Task PurgeAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        var components = new ComponentBuilder()
            .WithButton("Confirm purge", ConfirmPurgeId, ButtonStyle.Danger)
            .Build();
        await RespondAsync(
            "This deletes every server, setting, and channel the bot stores for this Discord server. Confirm?",
            ephemeral: true, components: components).ConfigureAwait(false);
    }

    /// <summary>Executes the purge after confirmation.</summary>
    [ComponentInteraction(ConfirmPurgeId)]
    public async Task ConfirmPurgeAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var purge = scope.ServiceProvider.GetRequiredService<IGuildPurgeService>();
            await purge.PurgeGuildAsync(Context.Guild.Id).ConfigureAwait(false);
            await FollowupAsync("Guild data purged.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
```

- [ ] **Step 5: Build + run the full test suite**

Run: `dotnet build RustPlusBot.slnx`
Expected: Build succeeded, 0 errors.

Run: `dotnet test RustPlusBot.slnx`
Expected: All tests pass (including Task 1 and Task 2's new tests).

- [ ] **Step 6: Format + commit**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git add src/RustPlusBot.Features.Workspace/Modules/WorkspaceAdminModule.cs
git commit -m "feat: add /workspace repair, rebuild, and purge commands"
```

---

## Manual verification (after all tasks)

Interaction modules are not unit-tested. Before opening the PR, verify in a test guild:

1. `/ping` → returns gateway + response ms.
2. `/status` → embed with uptime, latency, guild/server counts, and a line per server.
3. `/workspace repair` → deleting a bot channel then running repair recreates it.
4. With `Workspace:EnableDangerCommands=true`: `/workspace rebuild` (confirm) re-creates channels; `/workspace purge` (confirm) clears the guild; `/admin reset-database RESET` wipes the DB and prompts a restart.
5. With the flag `false` (default): rebuild/purge/reset-database all reply "Developer commands are disabled."

## Self-review notes

- **Spec coverage:** `/ping`, `/status` (Task 3); `/workspace repair`, `rebuild` (Task 5); `/workspace purge` → `GuildPurgeService` (Task 2 + Task 5); `/admin reset-database` → `DatabaseMaintenanceService` (Task 1 + Task 4). Authorization (ManageGuild + danger flag) reused; localization = plain English; DB reset = clear-rows-keep-schema. All spec items mapped.
- **Danger flag:** `MaintenanceOptions` binds the same `Workspace` section, so one `EnableDangerCommands` flag governs `/workspace rebuild|purge|reset|simulate-server` and `/admin reset-database` — matching the spec's "reuse the existing model."
- **Cascade correctness:** `RemoveAsync` cascade-deletes ConnectionState, ServerCommandSettings, ServerMapSettings, SmartSwitch, SmartAlarm, SmartStorageMonitor, PlayerCredential, and per-server Provisioned* rows (verified in configurations). `GuildPurgeService` explicitly deletes the only non-cascaded guild-keyed rows: EventSubscription, PairedEntity, GuildSettings. The Task 2 test guards this.
