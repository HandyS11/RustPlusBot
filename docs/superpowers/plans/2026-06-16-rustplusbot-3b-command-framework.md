# Subsystem 3b — In-game `!command` framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the in-game `!command` framework (dispatcher, parser, cooldown, mute, per-guild-culture replies) and prove it with six commands — `!mute`/`!unmute`/`!uptime`/`!pop`/`!time`/`!wipe` — all driven from in-game team chat.

**Architecture:** New `RustPlusBot.Features.Commands` project consumes the existing `TeamMessageReceivedEvent` (bus fan-out, alongside the chat relay), parses a per-server-configurable prefix, dispatches to an `ICommandHandler`, and replies via the existing `ITeamChatSender`. Two new public seams: `IRustServerQuery` (read side of the live socket, on `ConnectionSupervisor`) and `IMuteStore` + `ServerCommandSettings` entity (shared with `Features.Chat`, which gates its Discord→game relay on mute).

**Tech Stack:** .NET 10, Discord.Net 3.20, RustPlusApi 2.0.0-beta.1, EF Core + SQLite (Persistord), xUnit + NSubstitute, in-process event bus, Generic Host.

**Spec:** `docs/superpowers/specs/2026-06-16-rustplusbot-3b-command-framework-design.md`

**Branch:** `feat/command-framework` off `develop`.

---

## File structure

**New project `src/RustPlusBot.Features.Commands/`:**

- `CommandServiceCollectionExtensions.cs` — `AddCommands()` DI.
- `CommandOptions.cs` — prefix fallback + cooldown window; `ValidateOnStart`.
- `Dispatching/CommandLine.cs` — pure parser `(prefix, rawLine) → (name, args[])?`.
- `Dispatching/CommandContext.cs` — immutable per-command carrier.
- `Dispatching/ICommandHandler.cs` — `Name` + `ExecuteAsync`.
- `Dispatching/CommandDispatcher.cs` — the pipeline.
- `Dispatching/CommandCooldown.cs` — singleton per-(server,name) IClock window.
- `Localization/ICommandLocalizer.cs` + `CommandLocalizer.cs` + `CommandLocalizationCatalog.cs`.
- `Querying/ServerInfoSnapshot.cs` + `ServerTimeSnapshot.cs` — DTOs returned by `IRustServerQuery` (defined here so Commands has no Connections-internal dep; Connections references this project... see note).
- `Handlers/MuteCommandHandler.cs`, `UnmuteCommandHandler.cs`, `UptimeCommandHandler.cs`, `PopCommandHandler.cs`, `TimeCommandHandler.cs`, `WipeCommandHandler.cs`.
- `Hosting/CommandsHostedService.cs` — subscription loop.
- `Hosting/BotUptime.cs` — process-start baseline (singleton, IClock).

> **DTO placement note:** `IRustServerQuery` is implemented in `Features.Connections` but consumed in `Features.Commands`, and its DTOs must be visible to both. Place `IRustServerQuery`, `ServerInfoSnapshot`, `ServerTimeSnapshot` in **`Features.Connections`** as **public** types (Connections is already referenced by the host; Commands will reference Connections — same as Chat references Connections for `ITeamChatSender`). So the `Querying/` DTO files above actually live under `src/RustPlusBot.Features.Connections/Listening/`. Commands depends on Connections (public seam only), never the reverse.

**`src/RustPlusBot.Features.Connections/`:**

- `Listening/IRustServerQuery.cs` (public), `Listening/ServerInfoSnapshot.cs` (public), `Listening/ServerTimeSnapshot.cs` (public) — new.
- `Listening/IRustServerConnection.cs` — add `GetServerInfoAsync` / `GetTimeAsync` (internal).
- `Listening/RustPlusSocketSource.cs` — map RustPlusApi → snapshots (untested shim).
- `Supervisor/ConnectionSupervisor.cs` — implement `IRustServerQuery`.
- `ConnectionServiceCollectionExtensions.cs` — register `IRustServerQuery` → same singleton.

**`src/RustPlusBot.Persistence/`:**

- `Commands/ServerCommandSettings.cs` — entity.
- `Commands/IMuteStore.cs` (public) + `Commands/MuteStore.cs`.
- `BotDbContext.cs` — `DbSet<ServerCommandSettings>` + model config (FK cascade to RustServer).
- `PersistenceServiceCollectionExtensions.cs` — register `IMuteStore`.
- `Migrations/` — `CommandSettings` migration.

**`src/RustPlusBot.Features.Chat/`:**

- `Inbound/TeamChatInboundProcessor.cs` — gate relay on `IMuteStore`.
- `ChatServiceCollectionExtensions.cs` — unchanged (IMuteStore comes from Persistence DI).

**`src/RustPlusBot.Host/Program.cs`** — `AddCommands()`.

**Tests:** `tests/RustPlusBot.Features.Commands.Tests/` (new), additions to `tests/RustPlusBot.Persistence.Tests/Commands/`, `tests/RustPlusBot.Features.Connections.Tests/`, `tests/RustPlusBot.Features.Chat.Tests/`.

> **Solution wiring:** the new `src` + `tests` projects must be added to `RustPlusBot.slnx` and the test project must reference `Features.Commands`, `Abstractions`, `Persistence`, NSubstitute, xUnit (copy a sibling test `.csproj`). Add `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` and a test `InternalsVisibleTo` to `Features.Commands.csproj` so NSubstitute can mock internal types and tests can see internal classes.

---

## Task 1: Scaffold the `Features.Commands` project + options

**Files:**

- Create: `src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj`
- Create: `src/RustPlusBot.Features.Commands/CommandOptions.cs`
- Create: `tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`
- Create: `tests/RustPlusBot.Features.Commands.Tests/CommandOptionsTests.cs`
- Modify: `RustPlusBot.slnx`

- [ ] **Step 1: Create the csproj files**

Copy `src/RustPlusBot.Features.Chat/RustPlusBot.Features.Chat.csproj` to the new path; change the assembly to `RustPlusBot.Features.Commands`. It must reference `RustPlusBot.Abstractions`, `RustPlusBot.Persistence`, `RustPlusBot.Features.Connections`, `RustPlusBot.Features.Workspace` (for nothing yet — drop if unused), and add:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="RustPlusBot.Features.Commands.Tests" />
  <InternalsVisibleTo Include="DynamicProxyGenAssembly2" />
</ItemGroup>
```

Copy `tests/RustPlusBot.Features.Chat.Tests/RustPlusBot.Features.Chat.Tests.csproj` for the test project; change references to point at `Features.Commands`.

- [ ] **Step 2: Add both projects to the solution**

Run: `dotnet sln RustPlusBot.slnx add src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`

(If `dotnet sln ... .slnx` is unsupported in this CLI, edit `RustPlusBot.slnx` by hand, mirroring an existing `<Project Path="..."/>` entry.)

- [ ] **Step 3: Write the failing options test**

Create `CommandOptionsTests.cs`:

```csharp
using RustPlusBot.Features.Commands;

namespace RustPlusBot.Features.Commands.Tests;

public sealed class CommandOptionsTests
{
    [Fact]
    public void Defaults_AreSensible()
    {
        var options = new CommandOptions();
        Assert.Equal("!", options.DefaultPrefix);
        Assert.True(options.Cooldown > TimeSpan.Zero);
    }
}
```

- [ ] **Step 4: Run to verify it fails to compile**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests -v q`
Expected: FAIL — `CommandOptions` does not exist.

- [ ] **Step 5: Implement `CommandOptions`**

Create `CommandOptions.cs`:

```csharp
namespace RustPlusBot.Features.Commands;

/// <summary>Configuration for the in-game command framework.</summary>
public sealed class CommandOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Commands";

    /// <summary>Fallback command prefix when a server has no configured prefix.</summary>
    public string DefaultPrefix { get; set; } = "!";

    /// <summary>Minimum interval between identical commands on one server.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromSeconds(4);
}
```

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests -v q`
Expected: PASS (1 test).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Commands tests/RustPlusBot.Features.Commands.Tests RustPlusBot.slnx
git commit -m "feat(commands): scaffold Features.Commands project + CommandOptions"
```

---

## Task 2: `CommandLine` parser

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Dispatching/CommandLine.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Dispatching/CommandLineTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using RustPlusBot.Features.Commands.Dispatching;

namespace RustPlusBot.Features.Commands.Tests.Dispatching;

public sealed class CommandLineTests
{
    [Fact]
    public void Parses_NameAndArgs()
    {
        var parsed = CommandLine.TryParse("!", "!pop now here", out var result);
        Assert.True(parsed);
        Assert.Equal("pop", result.Name);
        Assert.Equal(new[] { "now", "here" }, result.Args);
    }

    [Fact]
    public void NameIsLowercased()
    {
        Assert.True(CommandLine.TryParse("!", "!POP", out var result));
        Assert.Equal("pop", result.Name);
    }

    [Theory]
    [InlineData("pop")]            // no prefix
    [InlineData("!")]             // prefix only
    [InlineData("!   ")]          // prefix + whitespace
    [InlineData("")]
    [InlineData("  ")]
    public void Rejects_NonCommands(string line)
    {
        Assert.False(CommandLine.TryParse("!", line, out _));
    }

    [Fact]
    public void TrimsAndCollapsesWhitespace()
    {
        Assert.True(CommandLine.TryParse("!", "  !time   a    b ", out var result));
        Assert.Equal("time", result.Name);
        Assert.Equal(new[] { "a", "b" }, result.Args);
    }

    [Fact]
    public void Honors_CustomPrefix()
    {
        Assert.True(CommandLine.TryParse(".", ".pop", out var result));
        Assert.Equal("pop", result.Name);
        Assert.False(CommandLine.TryParse(".", "!pop", out _));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandLineTests -v q`
Expected: FAIL — `CommandLine` not defined.

- [ ] **Step 3: Implement `CommandLine`**

```csharp
namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>A parsed command line: a lowercase name plus zero or more whitespace-split args.</summary>
/// <param name="Name">The lowercase command name (without the prefix).</param>
/// <param name="Args">The whitespace-split arguments.</param>
public readonly record struct CommandLine(string Name, IReadOnlyList<string> Args)
{
    /// <summary>Parses <paramref name="rawLine"/> against <paramref name="prefix"/>.</summary>
    /// <param name="prefix">The command prefix (e.g. "!").</param>
    /// <param name="rawLine">The raw in-game line.</param>
    /// <param name="command">The parsed command on success.</param>
    /// <returns>True when the line is a command for this prefix.</returns>
    public static bool TryParse(string prefix, string rawLine, out CommandLine command)
    {
        command = default;
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(rawLine))
        {
            return false;
        }

        var trimmed = rawLine.Trim();
        if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var body = trimmed[prefix.Length..];
        var tokens = body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length == 0)
        {
            return false;
        }

        var name = tokens[0].ToLowerInvariant();
        var args = tokens.Length > 1 ? tokens[1..] : [];
        command = new CommandLine(name, args);
        return true;
    }
}
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandLineTests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Dispatching/CommandLine.cs tests/RustPlusBot.Features.Commands.Tests/Dispatching/CommandLineTests.cs
git commit -m "feat(commands): add CommandLine parser"
```

---

## Task 3: `CommandCooldown`

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Dispatching/CommandCooldown.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Dispatching/CommandCooldownTests.cs`

- [ ] **Step 1: Write the failing tests** (uses a fake IClock)

```csharp
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands;
using RustPlusBot.Features.Commands.Dispatching;

namespace RustPlusBot.Features.Commands.Tests.Dispatching;

public sealed class CommandCooldownTests
{
    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch;
    }

    private static CommandCooldown Make(TestClock clock) =>
        new(clock, new CommandOptions { Cooldown = TimeSpan.FromSeconds(4) });

    [Fact]
    public void FirstUse_IsAllowed()
    {
        Assert.True(Make(new TestClock()).TryConsume(Guid.NewGuid(), "pop"));
    }

    [Fact]
    public void WithinWindow_IsBlocked()
    {
        var clock = new TestClock();
        var cd = Make(clock);
        var server = Guid.NewGuid();
        Assert.True(cd.TryConsume(server, "pop"));
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        Assert.False(cd.TryConsume(server, "pop"));
    }

    [Fact]
    public void AfterWindow_IsAllowed()
    {
        var clock = new TestClock();
        var cd = Make(clock);
        var server = Guid.NewGuid();
        Assert.True(cd.TryConsume(server, "pop"));
        clock.UtcNow = clock.UtcNow.AddSeconds(5);
        Assert.True(cd.TryConsume(server, "pop"));
    }

    [Fact]
    public void DifferentCommand_OrServer_IsIndependent()
    {
        var clock = new TestClock();
        var cd = Make(clock);
        var server = Guid.NewGuid();
        Assert.True(cd.TryConsume(server, "pop"));
        Assert.True(cd.TryConsume(server, "time"));
        Assert.True(cd.TryConsume(Guid.NewGuid(), "pop"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandCooldownTests -v q`
Expected: FAIL — `CommandCooldown` not defined.

- [ ] **Step 3: Implement `CommandCooldown`**

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>Per-(server, command) cooldown. Singleton; in-memory; clock-driven.</summary>
/// <param name="clock">The clock.</param>
/// <param name="options">The command options carrying the cooldown window.</param>
internal sealed class CommandCooldown(IClock clock, CommandOptions options)
{
    private readonly ConcurrentDictionary<(Guid Server, string Name), DateTimeOffset> _last = new();

    /// <summary>Returns true if the command may run now (and records the run); false if on cooldown.</summary>
    /// <param name="serverId">The server id.</param>
    /// <param name="name">The lowercase command name.</param>
    public bool TryConsume(Guid serverId, string name)
    {
        var now = clock.UtcNow;
        var key = (serverId, name);
        var allowed = true;
        _last.AddOrUpdate(
            key,
            now,
            (_, previous) =>
            {
                if (now - previous < options.Cooldown)
                {
                    allowed = false;
                    return previous;
                }

                return now;
            });
        return allowed;
    }
}
```

> Note: `CommandOptions` is injected directly here for simplicity; the DI registration (Task 12) registers it as a singleton value alongside `IOptions<CommandOptions>`. If the codebase convention is `IOptions<T>`, inject `IOptions<CommandOptions>` and read `.Value` — check a sibling (`ConnectionOptions` usage) and match it.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandCooldownTests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Dispatching/CommandCooldown.cs tests/RustPlusBot.Features.Commands.Tests/Dispatching/CommandCooldownTests.cs
git commit -m "feat(commands): add per-(server,command) cooldown"
```

---

## Task 4: `ServerCommandSettings` entity + `IMuteStore` + migration

**Files:**

- Create: `src/RustPlusBot.Persistence/Commands/ServerCommandSettings.cs`
- Create: `src/RustPlusBot.Persistence/Commands/IMuteStore.cs`
- Create: `src/RustPlusBot.Persistence/Commands/MuteStore.cs`
- Modify: `src/RustPlusBot.Persistence/BotDbContext.cs`
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`
- Create: `tests/RustPlusBot.Persistence.Tests/Commands/ServerCommandSettingsSchemaTests.cs`
- Create: `tests/RustPlusBot.Persistence.Tests/Commands/MuteStoreTests.cs`

- [ ] **Step 1: Write the failing store + schema tests**

Use the existing in-memory shared-cache SQLite harness (copy the fixture sibling tests use — `ConnectionStoreTests` is the closest model: seed a `RustServer` first because of the FK). `MuteStoreTests`:

```csharp
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Persistence.Tests.Commands;

public sealed class MuteStoreTests
{
    // Use the same DbContext fixture pattern as ConnectionStoreTests.
    // Helper SeedServerAsync(context, guildId) inserts a RustServer and returns its Id.

    [Fact]
    public async Task GetMuted_DefaultsFalse_WhenNoRow()
    {
        await using var ctx = TestDb.NewContext();
        var serverId = await TestDb.SeedServerAsync(ctx, guildId: 1);
        var store = new MuteStore(ctx, TestDb.Clock);
        Assert.False(await store.GetMutedAsync(1, serverId, default));
    }

    [Fact]
    public async Task GetPrefix_DefaultsBang_WhenNoRow()
    {
        await using var ctx = TestDb.NewContext();
        var serverId = await TestDb.SeedServerAsync(ctx, guildId: 1);
        var store = new MuteStore(ctx, TestDb.Clock);
        Assert.Equal("!", await store.GetPrefixAsync(1, serverId, default));
    }

    [Fact]
    public async Task SetMuted_PersistsAndReadsBack()
    {
        await using var ctx = TestDb.NewContext();
        var serverId = await TestDb.SeedServerAsync(ctx, guildId: 1);
        var store = new MuteStore(ctx, TestDb.Clock);
        await store.SetMutedAsync(1, serverId, true, default);
        Assert.True(await store.GetMutedAsync(1, serverId, default));
        await store.SetMutedAsync(1, serverId, false, default);
        Assert.False(await store.GetMutedAsync(1, serverId, default));
    }
}
```

`ServerCommandSettingsSchemaTests`: assert the table maps, the `(GuildId, ServerId)` key, and that deleting the parent `RustServer` cascades the settings row away (mirror `ConnectionStateSchemaTests`).

> If `TestDb`/fixture helpers don't already expose `SeedServerAsync`/`Clock`, add them to the test project's existing DB helper (the same one `ConnectionStoreTests` uses) rather than inventing a new fixture.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter "MuteStoreTests|ServerCommandSettingsSchemaTests" -v q`
Expected: FAIL — types not defined.

- [ ] **Step 3: Create the entity**

```csharp
namespace RustPlusBot.Persistence.Commands;

/// <summary>Per-(guild, server) command configuration: trigger prefix and mute state.</summary>
public sealed class ServerCommandSettings
{
    /// <summary>The owning guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server id (FK to RustServer).</summary>
    public Guid ServerId { get; set; }

    /// <summary>The command trigger prefix.</summary>
    public string Prefix { get; set; } = "!";

    /// <summary>Whether all bot→game output is currently muted.</summary>
    public bool Muted { get; set; }
}
```

- [ ] **Step 4: Create the store interface + impl**

`IMuteStore.cs` (public):

```csharp
namespace RustPlusBot.Persistence.Commands;

/// <summary>Reads/writes per-(guild, server) command settings: mute state and trigger prefix.</summary>
public interface IMuteStore
{
    /// <summary>Gets whether bot→game output is muted (false when unset).</summary>
    Task<bool> GetMutedAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);

    /// <summary>Sets the mute state, creating the row if needed.</summary>
    Task SetMutedAsync(ulong guildId, Guid serverId, bool muted, CancellationToken cancellationToken);

    /// <summary>Gets the command prefix ("!" when unset).</summary>
    Task<string> GetPrefixAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

`MuteStore.cs` (mirror `WorkspaceStore`'s read-then-write upsert and `DbContext` injection; inject `IClock` only if you add a timestamp column — otherwise drop it and update the tests):

```csharp
using Microsoft.EntityFrameworkCore;

namespace RustPlusBot.Persistence.Commands;

/// <summary>EF-backed <see cref="IMuteStore"/>.</summary>
/// <param name="context">The DbContext.</param>
internal sealed class MuteStore(BotDbContext context) : IMuteStore
{
    public async Task<bool> GetMutedAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var row = await context.ServerCommandSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);
        return row?.Muted ?? false;
    }

    public async Task<string> GetPrefixAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var row = await context.ServerCommandSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);
        return row?.Prefix ?? "!";
    }

    public async Task SetMutedAsync(ulong guildId, Guid serverId, bool muted, CancellationToken cancellationToken)
    {
        var row = await context.ServerCommandSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId && s.ServerId == serverId, cancellationToken)
            .ConfigureAwait(false);
        if (row is null)
        {
            context.ServerCommandSettings.Add(new ServerCommandSettings
            {
                GuildId = guildId, ServerId = serverId, Muted = muted
            });
        }
        else
        {
            row.Muted = muted;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Wire the DbContext**

In `BotDbContext.cs` add `public DbSet<ServerCommandSettings> ServerCommandSettings => Set<ServerCommandSettings>();` and in `OnModelCreating` configure: key `new { e.GuildId, e.ServerId }`, `Prefix` max length 8, and an FK from `ServerId` to `RustServer.Id` with `OnDelete(DeleteBehavior.Cascade)` (mirror the `ConnectionState`→`RustServer` config from 1b-iii). Store `GuildId` as the project's existing `ulong`↔`long`/`TEXT` convention — copy how `RustServer.GuildId` / `ConnectionState` map it.

- [ ] **Step 6: Register the store**

In `PersistenceServiceCollectionExtensions.cs` add `services.AddScoped<IMuteStore, MuteStore>();` (match the lifetime of sibling stores like `IConnectionStore`).

- [ ] **Step 7: Add the migration**

Run: `dotnet ef migrations add CommandSettings --project src/RustPlusBot.Persistence`
Expected: a new `*_CommandSettings.cs` creating `ServerCommandSettings` with the FK + cascade. Inspect it: confirm the cascade and composite key.

- [ ] **Step 8: Run to verify tests pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests --filter "MuteStoreTests|ServerCommandSettingsSchemaTests" -v q`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Persistence tests/RustPlusBot.Persistence.Tests/Commands
git commit -m "feat(persistence): add ServerCommandSettings + IMuteStore + CommandSettings migration"
```

---

## Task 5: `IRustServerQuery` seam + snapshots + connection extension

**Files:**

- Create: `src/RustPlusBot.Features.Connections/Listening/ServerInfoSnapshot.cs`
- Create: `src/RustPlusBot.Features.Connections/Listening/ServerTimeSnapshot.cs`
- Create: `src/RustPlusBot.Features.Connections/Listening/IRustServerQuery.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/IRustServerConnection.cs`
- Modify: `src/RustPlusBot.Features.Connections/Listening/RustPlusSocketSource.cs` (both nested classes)
- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`
- Modify: `src/RustPlusBot.Features.Connections/ConnectionServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Connections.Tests/ServerQueryTests.cs`
- Modify: `tests/RustPlusBot.Features.Connections.Tests/Fakes/FakeRustSocketSource.cs` (add the new connection members)

- [ ] **Step 1: Create the public snapshots**

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A point-in-time view of a server's population and wipe, decoupled from RustPlusApi types.</summary>
/// <param name="Players">Current player count.</param>
/// <param name="MaxPlayers">Server slot cap.</param>
/// <param name="QueuedPlayers">Players waiting in queue.</param>
/// <param name="WipeTimeUtc">When the server last wiped, if known.</param>
public sealed record ServerInfoSnapshot(int Players, int MaxPlayers, int QueuedPlayers, DateTimeOffset? WipeTimeUtc);
```

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>A point-in-time view of in-game time, decoupled from RustPlusApi types.</summary>
/// <param name="TimeOfDay">In-game hour in [0,24).</param>
/// <param name="SunriseHour">Hour at which day begins.</param>
/// <param name="SunsetHour">Hour at which night begins.</param>
public sealed record ServerTimeSnapshot(double TimeOfDay, double SunriseHour, double SunsetHour);
```

- [ ] **Step 2: Create the public query seam**

```csharp
namespace RustPlusBot.Features.Connections.Listening;

/// <summary>Reads live data from a connected server's socket (implemented by the connection supervisor).</summary>
public interface IRustServerQuery
{
    /// <summary>Gets server info, or null when (<paramref name="guildId"/>, <paramref name="serverId"/>) has no live socket.</summary>
    Task<ServerInfoSnapshot?> GetServerInfoAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);

    /// <summary>Gets in-game time, or null when there is no live socket.</summary>
    Task<ServerTimeSnapshot?> GetTimeAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Extend the internal connection interface**

Add to `IRustServerConnection`:

```csharp
    /// <summary>Gets a server-info snapshot, or null on failure/timeout.</summary>
    Task<ServerInfoSnapshot?> GetServerInfoAsync(TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Gets an in-game time snapshot, or null on failure/timeout.</summary>
    Task<ServerTimeSnapshot?> GetTimeAsync(TimeSpan timeout, CancellationToken cancellationToken);
```

- [ ] **Step 4: Write the failing supervisor test (uses the existing fake source)**

`ServerQueryTests`:

```csharp
// Build a supervisor with FakeRustSocketSource configured to return a known snapshot.
// Connect one (guild, server). Then:
[Fact]
public async Task GetServerInfo_ReturnsSnapshot_WhenConnected() { /* assert players/max/queued */ }

[Fact]
public async Task GetServerInfo_ReturnsNull_WhenNoLiveSocket()
{
    // query an unconnected (guild, server) → null
}
```

Model the harness on `TeamChatSenderTests` (which already drives the supervisor + fake to a connected state). Use 30s CTS (the 3a flakiness lesson).

- [ ] **Step 5: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests --filter ServerQueryTests -v q`
Expected: FAIL.

- [ ] **Step 6: Implement on the supervisor**

Add `IRustServerQuery` to the supervisor's interface list and implement both methods by looking up `_liveSockets` (exactly like `SendAsync`): return `null` when absent, else delegate to the connection's `GetServerInfoAsync`/`GetTimeAsync` with the heartbeat timeout from `ConnectionOptions`, broad-catch → `null` + log.

```csharp
public async Task<ServerInfoSnapshot?> GetServerInfoAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
{
    if (!_liveSockets.TryGetValue((guildId, serverId), out var live))
    {
        return null;
    }

    return await live.Connection.GetServerInfoAsync(options.Value.HeartbeatTimeout, cancellationToken).ConfigureAwait(false);
}
// GetTimeAsync mirrors this.
```

- [ ] **Step 7: Implement the snapshot mapping in the real shim (`RustPlusSocketSource`)**

In `RustPlusServerConnection`, add the two methods mapping the RustPlusApi response → snapshot, mirroring the existing `GetInfoAsync` (timeout-CTS + `.WaitAsync` + broad-catch → null):

```csharp
public async Task<ServerInfoSnapshot?> GetServerInfoAsync(TimeSpan timeout, CancellationToken cancellationToken)
{
    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeoutCts.CancelAfter(timeout);
    try
    {
        var response = await _rustPlus.GetInfoAsync(timeoutCts.Token).WaitAsync(timeoutCts.Token).ConfigureAwait(false);
        if (!response.IsSuccess || response.Data is null)
        {
            return null;
        }

        var d = response.Data;
        // VERIFY field names/units against RustPlusApi 2.0.0-beta.1 ServerInfo:
        //   PlayerCount (uint?), MaxPlayers, QueuedPlayers, WipeTime (epoch seconds? DateTime?).
        return new ServerInfoSnapshot(
            (int)(d.PlayerCount ?? 0u),
            (int)d.MaxPlayers,
            (int)d.QueuedPlayers,
            ConvertWipeTime(d.WipeTime));
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
#pragma warning disable CA1031
    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
    {
        LogHeartbeatFailed(_logger, ex);
        return null;
    }
}
// GetTimeAsync: await _rustPlus.GetTimeAsync(...); map TimeOfDay/SunriseTime/SunsetTime (VERIFY names/units).
// ConvertWipeTime: private helper turning the API's wipe field into DateTimeOffset? (epoch→FromUnixTimeSeconds, or as-is).
```

Add the same two methods to the `RejectedConnection` nested class returning `Task.FromResult<ServerInfoSnapshot?>(null)` / `null`. **Never** log the token.

- [ ] **Step 8: Update the test fake**

Add `GetServerInfoAsync`/`GetTimeAsync` to `FakeRustSocketSource`'s connection (return configurable snapshots), so the Connections test assembly compiles.

- [ ] **Step 9: Register the seam**

In `ConnectionServiceCollectionExtensions.cs`, register `IRustServerQuery` to resolve the **same** `ConnectionSupervisor` singleton (use the existing forwarding pattern — the supervisor is already registered once and aliased to `IConnectionSupervisor`/`ITeamChatSender`; add `services.AddSingleton<IRustServerQuery>(sp => sp.GetRequiredService<ConnectionSupervisor>());` or extend the existing alias block).

- [ ] **Step 10: Run all Connections tests**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests -v q`
Expected: PASS (existing + new). Read the count.

- [ ] **Step 11: Commit**

```bash
git add src/RustPlusBot.Features.Connections tests/RustPlusBot.Features.Connections.Tests
git commit -m "feat(connections): add IRustServerQuery read seam + snapshot mapping"
```

---

## Task 6: `ICommandLocalizer` + EN/FR catalog

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Localization/ICommandLocalizer.cs`
- Create: `src/RustPlusBot.Features.Commands/Localization/CommandLocalizer.cs`
- Create: `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Localization/CommandLocalizerTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands.Tests.Localization;

public sealed class CommandLocalizerTests
{
    private static readonly CommandLocalizer Sut = new(new CommandLocalizationCatalog());

    [Fact]
    public void ReturnsFrench_WhenCultureFr() =>
        Assert.Equal("Bot mis en sourdine.", Sut.Get("command.mute.done", "fr"));

    [Fact]
    public void FallsBackToEnglish_WhenCultureUnknown() =>
        Assert.Equal("Bot muted.", Sut.Get("command.mute.done", "xx"));

    [Fact]
    public void FormatsArgs() =>
        Assert.Equal("Pop: 5/100 (2 queued)", Sut.Get("command.pop.ok", "en", 5, 100, 2));

    [Fact]
    public void NormalizesRegion() =>
        Assert.Equal("Bot mis en sourdine.", Sut.Get("command.mute.done", "fr-FR"));
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandLocalizerTests -v q`
Expected: FAIL.

- [ ] **Step 3: Implement the interface + localizer (copy Workspace's `Localizer`/`ILocalizer` shape — Normalize + English fallback + format provider)**

`ICommandLocalizer.cs`:

```csharp
namespace RustPlusBot.Features.Commands.Localization;

/// <summary>Resolves localized in-game reply strings by key and culture, falling back to English.</summary>
internal interface ICommandLocalizer
{
    /// <summary>Gets the localized string for a key.</summary>
    string Get(string key, string culture);

    /// <summary>Gets the localized, format-applied string.</summary>
    string Get(string key, string culture, params object[] args);
}
```

`CommandLocalizer.cs`: copy `src/RustPlusBot.Features.Workspace/Localization/Localizer.cs` verbatim (rename the class, depend on `CommandLocalizationCatalog`). Add `// TODO: consolidate localizers into a shared project (duplicated from Workspace.Localizer).`

- [ ] **Step 4: Implement the catalog**

`CommandLocalizationCatalog.cs` — an `IReadOnlyDictionary<string, IReadOnlyDictionary<string,string>>` keyed by `"en"`/`"fr"`. Keys (with `{0}`-style placeholders) — provide BOTH languages for each:

```
command.mute.done        en "Bot muted."                       fr "Bot mis en sourdine."
command.unmute.done      en "Bot unmuted."                     fr "Bot réactivé."
command.uptime.ok        en "Uptime: {0}"                      fr "Disponibilité : {0}"
command.uptime.ok.server en "Uptime: {0}, server: {1}"         fr "Disponibilité : {0}, serveur : {1}"
command.pop.ok           en "Pop: {0}/{1} ({2} queued)"        fr "Population : {0}/{1} ({2} en file)"
command.time.ok          en "Time: {0} — {1}"                  fr "Heure : {0} — {1}"
command.time.day         en "day"                              fr "jour"
command.time.night       en "night"                            fr "nuit"
command.wipe.ok          en "Wiped {0} ago"                    fr "Wipe il y a {0}"
command.notconnected     en "Not connected to the server."     fr "Non connecté au serveur."
command.wipe.unknown     en "Wipe time is unknown."            fr "Heure de wipe inconnue."
```

- [ ] **Step 5: Run to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandLocalizerTests -v q`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Localization tests/RustPlusBot.Features.Commands.Tests/Localization
git commit -m "feat(commands): add EN/FR command localizer + catalog"
```

---

## Task 7: `ICommandHandler` + `CommandContext` + `BotUptime`

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Dispatching/ICommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Dispatching/CommandContext.cs`
- Create: `src/RustPlusBot.Features.Commands/Hosting/BotUptime.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Hosting/BotUptimeTests.cs`

- [ ] **Step 1: Define `CommandContext` (no test — pure carrier)**

```csharp
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>Everything a command handler needs for one invocation.</summary>
/// <param name="GuildId">Owning guild.</param>
/// <param name="ServerId">Target server.</param>
/// <param name="Culture">Guild culture (e.g. "en"/"fr") for the reply.</param>
/// <param name="SenderSteamId">Steam id of the in-game caller.</param>
/// <param name="SenderName">In-game name of the caller.</param>
/// <param name="Args">Parsed command arguments.</param>
internal sealed record CommandContext(
    ulong GuildId,
    Guid ServerId,
    string Culture,
    ulong SenderSteamId,
    string SenderName,
    IReadOnlyList<string> Args);
```

- [ ] **Step 2: Define `ICommandHandler`**

```csharp
namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>One in-game command. The dispatcher resolves handlers by <see cref="Name"/>.</summary>
internal interface ICommandHandler
{
    /// <summary>The lowercase command name (without prefix), e.g. "pop".</summary>
    string Name { get; }

    /// <summary>Executes and returns the localized reply, or null for no reply.</summary>
    Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write the failing `BotUptime` test**

```csharp
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Hosting;

namespace RustPlusBot.Features.Commands.Tests.Hosting;

public sealed class BotUptimeTests
{
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch; }

    [Fact]
    public void Elapsed_IsDifferenceSinceConstruction()
    {
        var clock = new TestClock();
        var uptime = new BotUptime(clock); // captures start at construction
        clock.UtcNow = clock.UtcNow.AddMinutes(90);
        Assert.Equal(TimeSpan.FromMinutes(90), uptime.Elapsed);
    }
}
```

- [ ] **Step 4: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter BotUptimeTests -v q`
Expected: FAIL.

- [ ] **Step 5: Implement `BotUptime` (singleton)**

```csharp
using RustPlusBot.Abstractions.Time;

namespace RustPlusBot.Features.Commands.Hosting;

/// <summary>Process-uptime baseline, captured when the singleton is first constructed.</summary>
/// <param name="clock">The clock.</param>
internal sealed class BotUptime(IClock clock)
{
    private readonly DateTimeOffset _start = clock.UtcNow;

    /// <summary>How long the bot has been running.</summary>
    public TimeSpan Elapsed => clock.UtcNow - _start;
}
```

- [ ] **Step 6: Run to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter BotUptimeTests -v q`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Dispatching/ICommandHandler.cs src/RustPlusBot.Features.Commands/Dispatching/CommandContext.cs src/RustPlusBot.Features.Commands/Hosting/BotUptime.cs tests/RustPlusBot.Features.Commands.Tests/Hosting/BotUptimeTests.cs
git commit -m "feat(commands): add ICommandHandler, CommandContext, BotUptime"
```

---

## Task 8: Mute/Unmute handlers

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/MuteCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/UnmuteCommandHandler.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/MuteHandlersTests.cs`

- [ ] **Step 1: Write the failing tests** (NSubstitute the `IMuteStore` + a real `CommandLocalizer`)

```csharp
using NSubstitute;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class MuteHandlersTests
{
    private static readonly ICommandLocalizer Loc = new CommandLocalizer(new CommandLocalizationCatalog());
    private static CommandContext Ctx(string culture = "en") =>
        new(1, Guid.NewGuid(), culture, 7, "alice", []);

    [Fact]
    public async Task Mute_SetsMutedTrue_AndConfirms()
    {
        var store = Substitute.For<IMuteStore>();
        var handler = new MuteCommandHandler(store, Loc);
        var ctx = Ctx();
        var reply = await handler.ExecuteAsync(ctx, default);
        await store.Received(1).SetMutedAsync(ctx.GuildId, ctx.ServerId, true, Arg.Any<CancellationToken>());
        Assert.Equal("Bot muted.", reply);
        Assert.Equal("mute", handler.Name);
    }

    [Fact]
    public async Task Unmute_SetsMutedFalse_AndConfirms_InFrench()
    {
        var store = Substitute.For<IMuteStore>();
        var handler = new UnmuteCommandHandler(store, Loc);
        var ctx = Ctx("fr");
        var reply = await handler.ExecuteAsync(ctx, default);
        await store.Received(1).SetMutedAsync(ctx.GuildId, ctx.ServerId, false, Arg.Any<CancellationToken>());
        Assert.Equal("Bot réactivé.", reply);
        Assert.Equal("unmute", handler.Name);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter MuteHandlersTests -v q`
Expected: FAIL.

- [ ] **Step 3: Implement both handlers**

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Persistence.Commands;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!mute — silence all bot→game output.</summary>
internal sealed class MuteCommandHandler(IMuteStore store, ICommandLocalizer localizer) : ICommandHandler
{
    public string Name => "mute";

    public async Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        await store.SetMutedAsync(context.GuildId, context.ServerId, true, cancellationToken).ConfigureAwait(false);
        return localizer.Get("command.mute.done", context.Culture);
    }
}
```

`UnmuteCommandHandler` is identical with `Name => "unmute"`, `SetMutedAsync(..., false, ...)`, key `command.unmute.done`.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter MuteHandlersTests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers/MuteCommandHandler.cs src/RustPlusBot.Features.Commands/Handlers/UnmuteCommandHandler.cs tests/RustPlusBot.Features.Commands.Tests/Handlers/MuteHandlersTests.cs
git commit -m "feat(commands): add !mute / !unmute handlers"
```

---

## Task 9: Uptime / Pop / Wipe / Time handlers

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/UptimeCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/PopCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/WipeCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/TimeCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Formatting/DurationFormat.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/QueryHandlersTests.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Formatting/DurationFormatTests.cs`

- [ ] **Step 1: Write the failing `DurationFormat` tests**

```csharp
using RustPlusBot.Features.Commands.Formatting;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class DurationFormatTests
{
    [Theory]
    [InlineData(0, 0, 30, "30m")]
    [InlineData(0, 5, 0, "5h 0m")]
    [InlineData(2, 3, 0, "2d 3h")]
    public void Compact(int days, int hours, int minutes, string expected) =>
        Assert.Equal(expected, DurationFormat.Compact(new TimeSpan(days, hours, minutes, 0)));
}
```

- [ ] **Step 2: Run to verify it fails / Step 3: implement `DurationFormat.Compact`**

A small pure helper: days→`{d}d {h}h`, else hours→`{h}h {m}m`, else `{m}m`. Invariant culture.

```csharp
using System.Globalization;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats durations compactly for in-game replies.</summary>
internal static class DurationFormat
{
    /// <summary>Renders a duration as "Xd Yh" / "Yh Zm" / "Zm".</summary>
    public static string Compact(TimeSpan span)
    {
        if (span.TotalDays >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalDays}d {span.Hours}h");
        }

        if (span.TotalHours >= 1)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}h {span.Minutes}m");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalMinutes}m");
    }
}
```

- [ ] **Step 4: Write the failing handler tests** (NSubstitute `IRustServerQuery`; real localizer; fake `BotUptime` via a `TestClock`)

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Commands.Localization;
using RustPlusBot.Features.Connections.Listening;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class QueryHandlersTests
{
    private static readonly ICommandLocalizer Loc = new CommandLocalizer(new CommandLocalizationCatalog());
    private static CommandContext Ctx() => new(1, Guid.NewGuid(), "en", 7, "alice", []);
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch; }

    [Fact]
    public async Task Pop_FormatsPlayersMaxQueued()
    {
        var query = Substitute.For<IRustServerQuery>();
        var ctx = Ctx();
        query.GetServerInfoAsync(ctx.GuildId, ctx.ServerId, Arg.Any<CancellationToken>())
            .Returns(new ServerInfoSnapshot(5, 100, 2, null));
        var reply = await new PopCommandHandler(query, Loc).ExecuteAsync(ctx, default);
        Assert.Equal("Pop: 5/100 (2 queued)", reply);
    }

    [Fact]
    public async Task Pop_NotConnected_WhenNull()
    {
        var query = Substitute.For<IRustServerQuery>();
        query.GetServerInfoAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((ServerInfoSnapshot?)null);
        var reply = await new PopCommandHandler(query, Loc).ExecuteAsync(Ctx(), default);
        Assert.Equal("Not connected to the server.", reply);
    }

    [Fact]
    public async Task Wipe_ReportsAgo()
    {
        var clock = new TestClock { UtcNow = DateTimeOffset.UnixEpoch.AddDays(2) };
        var query = Substitute.For<IRustServerQuery>();
        var ctx = Ctx();
        query.GetServerInfoAsync(ctx.GuildId, ctx.ServerId, Arg.Any<CancellationToken>())
            .Returns(new ServerInfoSnapshot(1, 100, 0, DateTimeOffset.UnixEpoch));
        var reply = await new WipeCommandHandler(query, Loc, clock).ExecuteAsync(ctx, default);
        Assert.Equal("Wiped 2d 0h ago", reply);
    }

    [Fact]
    public async Task Time_ReportsDayOrNight()
    {
        var query = Substitute.For<IRustServerQuery>();
        var ctx = Ctx();
        query.GetTimeAsync(ctx.GuildId, ctx.ServerId, Arg.Any<CancellationToken>())
            .Returns(new ServerTimeSnapshot(12.0, 7.0, 20.0)); // noon → day
        var reply = await new TimeCommandHandler(query, Loc).ExecuteAsync(ctx, default);
        Assert.Contains("day", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Uptime_ReportsBotUptime()
    {
        var clock = new TestClock();
        var uptime = new BotUptime(clock);
        clock.UtcNow = clock.UtcNow.AddHours(3);
        var reply = await new UptimeCommandHandler(uptime, Loc).ExecuteAsync(Ctx(), default);
        Assert.Equal("Uptime: 3h 0m", reply);
    }
}
```

- [ ] **Step 5: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter QueryHandlersTests -v q`
Expected: FAIL.

- [ ] **Step 6: Implement the four handlers**

`PopCommandHandler` (Name `"pop"`): query info → null ⇒ `command.notconnected`; else `command.pop.ok` with `Players, MaxPlayers, QueuedPlayers`.

`WipeCommandHandler` (Name `"wipe"`, ctor takes `IRustServerQuery, ICommandLocalizer, IClock`): query info → null ⇒ `command.notconnected`; `WipeTimeUtc` null ⇒ `command.wipe.unknown`; else `command.wipe.ok` with `DurationFormat.Compact(clock.UtcNow - WipeTimeUtc.Value)`.

`TimeCommandHandler` (Name `"time"`): query time → null ⇒ `command.notconnected`; else compute day/night (`TimeOfDay` in `[SunriseHour, SunsetHour)` ⇒ day key, else night key), render `command.time.ok` with a `hh:mm` string from `TimeOfDay` (hours = `(int)TimeOfDay`, minutes = `(int)((TimeOfDay % 1) * 60)`, invariant) and the localized day/night word.

`UptimeCommandHandler` (Name `"uptime"`, ctor `BotUptime, ICommandLocalizer`): `command.uptime.ok` with `DurationFormat.Compact(uptime.Elapsed)`. (Per-connection uptime omitted in 3b — no `ConnectedSince` source; `command.uptime.ok.server` key is reserved for 3b-ii.)

- [ ] **Step 7: Run to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "QueryHandlersTests|DurationFormatTests" -v q`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers src/RustPlusBot.Features.Commands/Formatting tests/RustPlusBot.Features.Commands.Tests/Handlers/QueryHandlersTests.cs tests/RustPlusBot.Features.Commands.Tests/Formatting
git commit -m "feat(commands): add !uptime / !pop / !wipe / !time handlers"
```

---

## Task 10: `CommandDispatcher`

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Dispatching/CommandDispatcher.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Dispatching/CommandDispatcherTests.cs`

- [ ] **Step 1: Write the failing dispatcher tests**

Fakes: `ITeamChatSender`, `IMuteStore`, `IWorkspaceStore` (for culture/prefix... see note), a stub `ICommandHandler`, real `CommandCooldown` with a `TestClock`. The dispatcher takes the `TeamMessageReceivedEvent`.

> **Prefix source:** read the prefix from `IMuteStore.GetPrefixAsync` (the settings store), and the culture from `IWorkspaceStore.GetCultureAsync`. Both are already needed.

```csharp
using NSubstitute;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Persistence.Commands;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Tests.Dispatching;

public sealed class CommandDispatcherTests
{
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UnixEpoch; }
    private sealed class StubHandler(string name) : ICommandHandler
    {
        public int Calls { get; private set; }
        public string Name => name;
        public Task<string?> ExecuteAsync(CommandContext c, CancellationToken ct) { Calls++; return Task.FromResult<string?>("reply"); }
    }

    private static (CommandDispatcher sut, ITeamChatSender sender, IMuteStore mute, StubHandler handler) Build(
        bool muted = false, string prefix = "!")
    {
        var sender = Substitute.For<ITeamChatSender>();
        var mute = Substitute.For<IMuteStore>();
        mute.GetMutedAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(muted);
        mute.GetPrefixAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(prefix);
        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");
        var handler = new StubHandler("pop");
        var cooldown = new CommandCooldown(new TestClock(), new CommandOptions());
        var sut = new CommandDispatcher([handler], cooldown, mute, workspace, sender,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandDispatcher>.Instance);
        return (sut, sender, mute, handler);
    }

    private static TeamMessageReceivedEvent Evt(string msg, bool fromActive = false) =>
        new(1, Guid.NewGuid(), 7, "alice", msg, fromActive);

    [Fact]
    public async Task RunsHandler_AndSendsReply()
    {
        var (sut, sender, _, handler) = Build();
        await sut.DispatchAsync(Evt("!pop"), default);
        Assert.Equal(1, handler.Calls);
        await sender.Received(1).SendAsync(1, Arg.Any<Guid>(), "reply", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_FromActivePlayer()
    {
        var (sut, sender, _, handler) = Build();
        await sut.DispatchAsync(Evt("!pop", fromActive: true), default);
        Assert.Equal(0, handler.Calls);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Ignores_NonCommandLine()
    {
        var (sut, _, _, handler) = Build();
        await sut.DispatchAsync(Evt("hello team"), default);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Ignores_UnknownCommand()
    {
        var (sut, sender, _, handler) = Build();
        await sut.DispatchAsync(Evt("!nope"), default);
        Assert.Equal(0, handler.Calls);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drops_WhenMuted_AndNotMuteCommand()
    {
        var (sut, sender, _, handler) = Build(muted: true); // stub handler is "pop", not mute/unmute
        await sut.DispatchAsync(Evt("!pop"), default);
        Assert.Equal(0, handler.Calls);
        await sender.DidNotReceive().SendAsync(Arg.Any<ulong>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Honors_CustomPrefix()
    {
        var (sut, _, _, handler) = Build(prefix: ".");
        await sut.DispatchAsync(Evt(".pop"), default);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Cooldown_DropsSecondCall()
    {
        var (sut, sender, _, handler) = Build();
        var evt = Evt("!pop");
        await sut.DispatchAsync(evt, default);
        await sut.DispatchAsync(evt, default); // same server+command within window
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task NullReply_DoesNotSend()
    {
        // A handler returning null should not call the sender — covered by giving the stub a null path.
    }
}
```

> The mute-gate must treat `mute`/`unmute` as exempt. Since the stub is named `pop`, the muted test asserts it's dropped. Add (optional) a second test with a stub named `mute` proving it runs while muted, if desired.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandDispatcherTests -v q`
Expected: FAIL — `CommandDispatcher` not defined.

- [ ] **Step 3: Implement `CommandDispatcher`**

```csharp
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Connections.Listening;
using RustPlusBot.Persistence.Commands;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Dispatching;

/// <summary>Turns a received in-game line into a parsed, authorized, rate-limited command + reply.</summary>
internal sealed partial class CommandDispatcher
{
    private static readonly HashSet<string> MuteExempt = new(StringComparer.Ordinal) { "mute", "unmute" };

    private readonly IReadOnlyDictionary<string, ICommandHandler> _handlers;
    private readonly CommandCooldown _cooldown;
    private readonly IMuteStore _settings;
    private readonly IWorkspaceStore _workspace;
    private readonly ITeamChatSender _sender;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(
        IEnumerable<ICommandHandler> handlers,
        CommandCooldown cooldown,
        IMuteStore settings,
        IWorkspaceStore workspace,
        ITeamChatSender sender,
        ILogger<CommandDispatcher> logger)
    {
        _handlers = handlers.ToDictionary(h => h.Name, StringComparer.Ordinal);
        _cooldown = cooldown;
        _settings = settings;
        _workspace = workspace;
        _sender = sender;
        _logger = logger;
    }

    public async Task DispatchAsync(TeamMessageReceivedEvent evt, CancellationToken cancellationToken)
    {
        if (evt.FromActivePlayer)
        {
            return; // Never react to our own echoed replies.
        }

        var prefix = await _settings.GetPrefixAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false);
        if (!CommandLine.TryParse(prefix, evt.Message, out var line) ||
            !_handlers.TryGetValue(line.Name, out var handler))
        {
            return;
        }

        if (!MuteExempt.Contains(line.Name) &&
            await _settings.GetMutedAsync(evt.GuildId, evt.ServerId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (!_cooldown.TryConsume(evt.ServerId, line.Name))
        {
            return;
        }

        var culture = await _workspace.GetCultureAsync(evt.GuildId, cancellationToken).ConfigureAwait(false);
        var context = new CommandContext(evt.GuildId, evt.ServerId, culture, evt.SenderSteamId, evt.SenderName, line.Args);

        string? reply;
        try
        {
            reply = await handler.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // Broad catch: a faulting command must not crash the dispatch loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogHandlerFaulted(_logger, ex, line.Name);
            return;
        }

        if (!string.IsNullOrEmpty(reply))
        {
            await _sender.SendAsync(evt.GuildId, evt.ServerId, reply, cancellationToken).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Command handler '{Command}' faulted.")]
    private static partial void LogHandlerFaulted(ILogger logger, Exception exception, string command);
}
```

> Mute is checked **before** cooldown so a muted server never consumes a cooldown slot. Order matches the spec's pipeline.

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandDispatcherTests -v q`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Dispatching/CommandDispatcher.cs tests/RustPlusBot.Features.Commands.Tests/Dispatching/CommandDispatcherTests.cs
git commit -m "feat(commands): add CommandDispatcher pipeline"
```

---

## Task 11: `CommandsHostedService`

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Hosting/CommandsHostedService.cs`

- [ ] **Step 1: Implement (thin; copy `ChatHostedService`'s relay-loop half)**

Subscribe to `TeamMessageReceivedEvent` via `IEventBus.SubscribeAsync`, dispatch each to the **scoped** dispatcher. **Scope note:** `CommandDispatcher` depends on scoped stores (`IMuteStore`, `IWorkspaceStore`). The hosted service is a singleton, so it must create a DI scope per event (inject `IServiceScopeFactory`, resolve `CommandDispatcher` inside the scope) — mirror how `ConnectionSupervisor`/other singletons access scoped stores in this codebase. Broad-catch around each iteration so one bad event never kills the loop.

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Commands.Dispatching;

namespace RustPlusBot.Features.Commands.Hosting;

/// <summary>Consumes team messages from the bus and dispatches in-game commands.</summary>
internal sealed partial class CommandsHostedService(
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    ILogger<CommandsHostedService> logger) : IHostedService, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public void Dispose() => _cts.Dispose();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loop = Task.Run(() => ConsumeAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
#pragma warning disable VSTHRD003
                await _loop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException) { }
        }
    }

    private async Task ConsumeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in eventBus.SubscribeAsync<TeamMessageReceivedEvent>(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var dispatcher = scope.ServiceProvider.GetRequiredService<CommandDispatcher>();
                    await dispatcher.DispatchAsync(evt, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
#pragma warning disable CA1031 // Broad catch: one bad event must not kill the loop.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    LogDispatchFaulted(logger, ex);
                }
            }
        }
        catch (OperationCanceledException) { }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogLoopFaulted(logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Command dispatch faulted for one event.")]
    private static partial void LogDispatchFaulted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Command consume loop faulted.")]
    private static partial void LogLoopFaulted(ILogger logger, Exception exception);
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/RustPlusBot.Features.Commands -v q`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Hosting/CommandsHostedService.cs
git commit -m "feat(commands): add CommandsHostedService consume loop"
```

---

## Task 12: DI wiring + host registration

**Files:**

- Create: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Host/Program.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`

- [ ] **Step 1: Write the failing registration test** (mirror `ChatRegistrationTests` — build a `ServiceCollection`, add prerequisites + `AddCommands`, assert the dispatcher, handlers, cooldown, localizer, options, and hosted service resolve). Assert all 6 `ICommandHandler`s are present.

- [ ] **Step 2: Run to verify it fails**

- [ ] **Step 3: Implement `AddCommands`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.Commands.Hosting;
using RustPlusBot.Features.Commands.Localization;

namespace RustPlusBot.Features.Commands;

/// <summary>DI registration for the in-game command framework.</summary>
public static class CommandServiceCollectionExtensions
{
    /// <summary>Registers the dispatcher, parser, cooldown, localizer, handlers, and hosted service.</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same collection, for chaining.</returns>
    public static IServiceCollection AddCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<CommandOptions>().BindConfiguration(CommandOptions.SectionName).ValidateOnStart();
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CommandOptions>>().Value);

        services.AddSingleton<CommandCooldown>();
        services.AddSingleton<BotUptime>();
        services.AddSingleton<ICommandLocalizer, CommandLocalizer>();
        services.AddSingleton<CommandLocalizationCatalog>();

        services.AddScoped<ICommandHandler, MuteCommandHandler>();
        services.AddScoped<ICommandHandler, UnmuteCommandHandler>();
        services.AddScoped<ICommandHandler, UptimeCommandHandler>();
        services.AddScoped<ICommandHandler, PopCommandHandler>();
        services.AddScoped<ICommandHandler, WipeCommandHandler>();
        services.AddScoped<ICommandHandler, TimeCommandHandler>();

        services.AddScoped<CommandDispatcher>();
        services.AddHostedService<CommandsHostedService>();

        return services;
    }
}
```

> **Lifetime check:** `CommandDispatcher` + handlers are scoped (they use scoped stores); `CommandCooldown`/`BotUptime`/localizer are singletons. The hosted service resolves the dispatcher per-event in a scope (Task 11). If `CommandOptions` registration via `BindConfiguration` differs from this repo's convention, copy `ConnectionOptions`' exact `AddOptions(...).Validate(...).ValidateOnStart()` block.

- [ ] **Step 4: Register in the host**

In `Program.cs`, add `.AddCommands()` next to `.AddChat()` / `.AddConnections()`.

- [ ] **Step 5: Run the registration test + build host**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CommandRegistrationTests -v q` then `dotnet build src/RustPlusBot.Host -v q`
Expected: PASS + 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs src/RustPlusBot.Host/Program.cs tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs
git commit -m "feat(commands): wire AddCommands DI + host registration"
```

---

## Task 13: Gate the Discord→game relay on mute (`Features.Chat`)

**Files:**

- Modify: `src/RustPlusBot.Features.Chat/Inbound/TeamChatInboundProcessor.cs`
- Modify: `tests/RustPlusBot.Features.Chat.Tests/TeamChatInboundProcessorTests.cs`
- Possibly modify: `tests/RustPlusBot.Features.Chat.Tests/ChatRegistrationTests.cs` (register an `IMuteStore` substitute)

- [ ] **Step 1: Add a failing test** — a muted (guild,server) must NOT relay into the game:

```csharp
[Fact]
public async Task DoesNotRelay_WhenMuted()
{
    // locator resolves to (guild, server); muteStore.GetMutedAsync(...) returns true
    // → ProcessAsync returns Ignored and sender.SendAsync is never called.
}
```

And keep the existing happy-path test green by giving its `IMuteStore` substitute `GetMutedAsync(...) == false`.

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Chat.Tests --filter TeamChatInboundProcessorTests -v q`
Expected: FAIL (compile error — processor has no `IMuteStore`).

- [ ] **Step 3: Add the mute gate**

Inject `IMuteStore` into `TeamChatInboundProcessor`; after the locator resolves `target` (so we have `GuildId`/`ServerId`) and before `dedup.Record`/`sender.SendAsync`, check:

```csharp
if (await muteStore.GetMutedAsync(t.GuildId, t.ServerId, cancellationToken).ConfigureAwait(false))
{
    return InboundOutcome.Ignored;
}
```

- [ ] **Step 4: Update Chat tests' construction**

Every `new TeamChatInboundProcessor(...)` in tests gains an `IMuteStore` substitute (default `false`). If `ChatRegistrationTests` builds a real container, ensure `IMuteStore` is registered (it comes from `AddPersistence`; add it to that test's setup or substitute it).

- [ ] **Step 5: Run all Chat tests**

Run: `dotnet test tests/RustPlusBot.Features.Chat.Tests -v q`
Expected: PASS. Read the count — confirm no assembly dropped (the 3a lesson).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Chat/Inbound/TeamChatInboundProcessor.cs tests/RustPlusBot.Features.Chat.Tests
git commit -m "feat(chat): gate Discord→game relay on mute state"
```

---

## Task 14: Full-suite verification, format gate, docs

**Files:**

- Modify: `docs/running-locally.md` (if it enumerates features/intents) — note the command framework; no new intents.
- Modify: `docs/product/feature-catalog.md` — flip the 6 in-game rows + the relevant prefix/mute notes to ✔️ Done (subsystem 3b).

- [ ] **Step 1: Run the FULL test suite and read per-assembly counts**

Run: `dotnet test RustPlusBot.slnx -v q`
Expected: ALL pass. **Read each assembly's count** — a low total means an assembly didn't build (the 3a `FakeWorkspaceStore` lesson). Confirm Commands, Connections, Chat, Persistence, Workspace, Abstractions all report their tests.

- [ ] **Step 2: Build under strict analyzers**

Run: `dotnet build RustPlusBot.slnx -warnaserror -v q`
Expected: 0 warnings / 0 errors.

- [ ] **Step 3: Run the ReSharper format gate (the repo's real gate — NOT `dotnet format`)**

Run: `dotnet tool restore && dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Then `git diff` — review reorders; rebuild + retest if it touched code.

- [ ] **Step 4: Confirm no EF model drift**

Run: `dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence` (or the repo's equivalent check)
Expected: no pending changes (the `CommandSettings` migration covers the new entity).

- [ ] **Step 5: Update the feature catalog + docs**

Flip `!mute`/`!unmute`/`!uptime`/`!pop`/`!time`/`!wipe` rows in `docs/product/feature-catalog.md` to ✔️ Done; note 3b shipped the framework + thin slice and that team-intel/history commands are 3b-ii.

- [ ] **Step 6: Final commit**

```bash
git add docs
git commit -m "docs: mark 3b command-framework thin slice done in feature catalog"
```

- [ ] **Step 7: Open the PR**

```bash
git push -u origin feat/command-framework
gh pr create --base develop --title "Subsystem 3b: in-game !command framework (thin slice)" --body "<summary + test counts>"
```

---

## Self-review notes (for the executor)

- **Spec coverage:** framework (Tasks 2,3,7,10,11), 6 handlers (8,9), `IRustServerQuery` seam (5), `IMuteStore`+entity+migration (4), mute gating the relay (13), per-guild-culture replies (6 + dispatcher culture read in 10), configurable prefix (4 store + 2 parser + 10 dispatcher), cooldown (3 + 10), in-game-only / no new intents (12), DI (12), testing + format gate + no-drift (14). All spec §§1–9 map to a task.
- **Untested shim:** Task 5 Step 7 is the one integration shim — its RustPlusApi field names/units (`ServerInfo.MaxPlayers/QueuedPlayers/WipeTime`, the time type's `TimeOfDay/SunriseTime/SunsetTime`) are flagged VERIFY-against-the-DLL, consistent with prior subsystems. If a field is absent/typed differently, adjust the snapshot mapping (and only the mapping) — the seam shape stays.
- **Type consistency:** `IMuteStore` methods (`GetMutedAsync`/`SetMutedAsync`/`GetPrefixAsync`), `IRustServerQuery` (`GetServerInfoAsync`/`GetTimeAsync`), `ServerInfoSnapshot(Players,MaxPlayers,QueuedPlayers,WipeTimeUtc)`, `ServerTimeSnapshot(TimeOfDay,SunriseHour,SunsetHour)`, `ICommandHandler.Name/ExecuteAsync`, `CommandContext` fields, and the localizer keys are used identically across Tasks 4–13.
- **Lifetime gotcha:** dispatcher + handlers scoped; hosted service resolves them per-event in a DI scope (Task 11); cooldown/uptime/localizer singletons. Verify against the repo's existing singleton-accesses-scoped-store pattern before finalizing.
