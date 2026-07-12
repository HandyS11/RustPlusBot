# Server-Pairing Confirmation in #setup — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** New-server FCM pairings post an Accept/Dismiss prompt in the guild's #setup channel instead of auto-running the full provisioning + connect cycle; the cycle fires only on Accept.

**Architecture:** A new singleton `ServerPairingCoordinator` in Features.Pairing (exact mirror of `SwitchPairingCoordinator`) holds pending pairings in-memory and replays today's persist path verbatim on Accept. Features.Workspace contributes a new guild-scoped `ISetupChannelLocator`. `PairingHandler` splits its server path: existing endpoint → today's silent credential upsert; new endpoint → coordinator prompt.

**Tech Stack:** .NET / C#, Discord.Net interactions, EF Core (sqlite in tests), xUnit + NSubstitute, ResX localization.

**Spec:** `docs/superpowers/specs/2026-07-12-server-pairing-confirmation-design.md`

## Global Constraints

- Solution file is `RustPlusBot.slnx` (there is no `.sln`).
- Hard CI format gate: `dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --no-build --verbosity=ERROR` must leave `git diff` empty. Run `dotnet tool restore` first if `jb` is missing.
- Branch: `feat/server-pairing-confirmation` cut from `develop` in the main checkout — **no git worktrees**.
- `docs/product/` and `docs/superpowers/` are gitignored — **never `git add`** the spec or this plan.
- Every type/member gets XML doc comments (`///`) matching the density of neighboring files; library code uses `.ConfigureAwait(false)` on every await.
- All git commits end with the trailer: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`
- Do not add new NuGet packages. Never touch SixLabors package versions.
- Pending state is in-memory only — no DB migration anywhere in this plan.
- Endpoint identity is `(guildId, ip, port)` — matching `ServerService.ResolveOrCreateByEndpointAsync` (verified NOT owner-scoped, see `src/RustPlusBot.Persistence/Servers/ServerService.cs:122-124`).

---

### Task 0: Branch

- [ ] **Step 0.1: Create the branch**

```bash
cd /home/handys11/Dev/RustPlusBot
git checkout develop && git pull && git checkout -b feat/server-pairing-confirmation
```

Expected: `Switched to a new branch 'feat/server-pairing-confirmation'`

---

### Task 1: `ISetupChannelLocator` (Features.Workspace)

The existing `CachingChannelLocator` base maps only rows with a non-null `RustServerId` (per-server channels) — #setup is a **global** channel (`RustServerId == null`), so this is a standalone sibling, not a subclass.

**Files:**
- Create: `src/RustPlusBot.Features.Workspace/Locating/ISetupChannelLocator.cs`
- Create: `src/RustPlusBot.Features.Workspace/Locating/SetupChannelLocator.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs` (locator block, near line 67-72)
- Modify: `tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Locating/SetupChannelLocatorTests.cs`

**Interfaces:**
- Consumes: `IWorkspaceStore.GetChannelsByKeyAsync(string key, CancellationToken)` → `IReadOnlyList<ProvisionedChannel>`; `WorkspaceChannelKeys.Setup` (= `"setup"`, internal); `IClock.UtcNow`.
- Produces: `public interface ISetupChannelLocator { Task<ulong?> GetChannelIdAsync(ulong guildId, CancellationToken cancellationToken); }` — consumed by Task 4's coordinator and Task 4's registration test.

- [ ] **Step 1.1: Write the failing test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Locating/SetupChannelLocatorTests.cs` (mirrors `AlarmChannelLocatorTests` in the same directory):

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Locating;

public sealed class SetupChannelLocatorTests
{
    private static (SetupChannelLocator Locator, ServiceProvider Provider, string ConnectionString, IClock Clock)
        CreateLocator()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch);

        var cs = $"DataSource=setup-locator-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(cs);
        keepAlive.Open();
        using (var seed = new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options))
        {
            seed.Database.Migrate();
        }

        var services = new ServiceCollection();
        services.AddSingleton(keepAlive);
        services.AddSingleton(clock);
        services.AddScoped(_ => new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(cs).Options));
        services.AddScoped<IWorkspaceStore, WorkspaceStore>();
        var provider = services.BuildServiceProvider();

        var locator = new SetupChannelLocator(provider.GetRequiredService<IServiceScopeFactory>(), clock);
        return (locator, provider, cs, clock);
    }

    private static async Task SeedAsync(string connectionString)
    {
        await using var context =
            new BotDbContext(new DbContextOptionsBuilder<BotDbContext>().UseSqlite(connectionString).Options);

        // The global #setup channel row (RustServerId null) the locator must resolve.
        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = 10UL,
            RustServerId = null,
            ChannelKey = WorkspaceChannelKeys.Setup,
            DiscordChannelId = 777UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        // A server-scoped row under the same key must be skipped (defensive; cannot occur in practice).
        var server = new RustServer
        {
            GuildId = 10UL, Name = "S", Ip = "1.1.1.1", Port = 28015
        };
        context.RustServers.Add(server);
        await context.SaveChangesAsync();
        context.ProvisionedChannels.Add(new ProvisionedChannel
        {
            GuildId = 10UL,
            RustServerId = server.Id,
            ChannelKey = WorkspaceChannelKeys.Setup,
            DiscordChannelId = 888UL,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_global_setup_channel()
    {
        var (locator, provider, cs, _) = CreateLocator();
        await using var _p = provider;
        using var _l = locator;
        await SeedAsync(cs);

        var id = await locator.GetChannelIdAsync(10UL, CancellationToken.None);

        Assert.Equal(777UL, id);
    }

    [Fact]
    public async Task GetChannelIdAsync_returns_null_when_unprovisioned()
    {
        var (locator, provider, _, _) = CreateLocator();
        await using var _p = provider;
        using var _l = locator;

        var id = await locator.GetChannelIdAsync(10UL, CancellationToken.None);

        Assert.Null(id);
    }

    [Fact]
    public async Task GetChannelIdAsync_serves_cached_entries_until_ttl_expires()
    {
        var (locator, provider, cs, clock) = CreateLocator();
        await using var _p = provider;
        using var _l = locator;

        // First read builds an empty cache.
        Assert.Null(await locator.GetChannelIdAsync(10UL, CancellationToken.None));

        await SeedAsync(cs);

        // Within the TTL the stale empty cache is served.
        Assert.Null(await locator.GetChannelIdAsync(10UL, CancellationToken.None));

        // Past the TTL the cache refreshes and finds the row.
        clock.UtcNow.Returns(DateTimeOffset.UnixEpoch + TimeSpan.FromSeconds(31));
        Assert.Equal(777UL, await locator.GetChannelIdAsync(10UL, CancellationToken.None));
    }
}
```

- [ ] **Step 1.2: Run the test to verify it fails**

```bash
dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter "FullyQualifiedName~SetupChannelLocatorTests"
```

Expected: build FAILURE — `SetupChannelLocator` does not exist.

- [ ] **Step 1.3: Write the interface and implementation**

Create `src/RustPlusBot.Features.Workspace/Locating/ISetupChannelLocator.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the guild-global #setup channel (used to post server-pairing prompts).</summary>
public interface ISetupChannelLocator
{
    /// <summary>Gets the Discord channel id of #setup for <paramref name="guildId"/>, or null.</summary>
    /// <param name="guildId">The guild snowflake.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The Discord channel id, or null if not provisioned.</returns>
    Task<ulong?> GetChannelIdAsync(ulong guildId, CancellationToken cancellationToken);
}
```

Create `src/RustPlusBot.Features.Workspace/Locating/SetupChannelLocator.cs`. `CachingChannelLocator` keys by (guild, server) and skips null-server rows, so this is a guild-keyed sibling with the same TTL/refresh shape:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Caches the provisioned global #setup channel per guild. Sibling of <see cref="CachingChannelLocator"/>;
/// separate because #setup rows carry a null <c>RustServerId</c>, which the per-server base skips.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped workspace store.</param>
/// <param name="clock">Drives the cache TTL.</param>
internal sealed class SetupChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : ISetupChannelLocator, IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateTimeOffset _builtAt = DateTimeOffset.MinValue;
    private Dictionary<ulong, ulong> _byGuild = [];

    /// <inheritdoc />
    public void Dispose() => _refreshGate.Dispose();

    /// <inheritdoc />
    public async Task<ulong?> GetChannelIdAsync(ulong guildId, CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        return _byGuild.TryGetValue(guildId, out var id) ? id : null;
    }

    private async Task EnsureFreshAsync(CancellationToken cancellationToken)
    {
        if (clock.UtcNow - _builtAt < CacheTtl)
        {
            return;
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (clock.UtcNow - _builtAt < CacheTtl)
            {
                return;
            }

            var scope = scopeFactory.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
                var rows = await store.GetChannelsByKeyAsync(WorkspaceChannelKeys.Setup, cancellationToken)
                    .ConfigureAwait(false);

                Dictionary<ulong, ulong> byGuild = [];
                foreach (var row in rows)
                {
                    // #setup is global-scope: only rows without a server id belong to it.
                    if (row.RustServerId is not null)
                    {
                        continue;
                    }

                    byGuild[row.GuildId] = row.DiscordChannelId;
                }

                _byGuild = byGuild;
                _builtAt = clock.UtcNow;
            }
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}
```

- [ ] **Step 1.4: Register in DI**

In `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`, in the locator block (after the `IStorageMonitorChannelLocator` line, currently line 72), add:

```csharp
services.AddSingleton<ISetupChannelLocator, SetupChannelLocator>();
```

In `tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs`, add to the existing registration-assertion test method (alongside its existing `Assert.Contains(services, ...)` lines):

```csharp
Assert.Contains(services, d => d.ServiceType == typeof(ISetupChannelLocator));
```

(Add `using RustPlusBot.Features.Workspace.Locating;` if the file doesn't already have it.)

- [ ] **Step 1.5: Run the tests to verify they pass**

```bash
dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter "FullyQualifiedName~SetupChannelLocatorTests|FullyQualifiedName~WorkspaceRegistrationTests"
```

Expected: PASS (3 locator tests + registration test).

- [ ] **Step 1.6: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests
git commit -m "feat(workspace): add ISetupChannelLocator for the global #setup channel

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Localization keys + `ServerPairingComponentIds` + `ServerPairingPromptRenderer`

**Files:**
- Modify: `src/RustPlusBot.Localization/Strings.resx`
- Modify: `src/RustPlusBot.Localization/Strings.fr.resx`
- Modify: `src/RustPlusBot.Features.Pairing/RustPlusBot.Features.Pairing.csproj`
- Create: `src/RustPlusBot.Features.Pairing/Rendering/ServerPairingComponentIds.cs`
- Create: `src/RustPlusBot.Features.Pairing/Rendering/ServerPairingPromptRenderer.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/ServerPairingPromptRendererTests.cs`

**Interfaces:**
- Consumes: `ILocalizer.Get(key, culture)` / `Get(key, culture, params object[] args)` (`RustPlusBot.Localization`).
- Produces (used by Tasks 4-6):
  - `ServerPairingComponentIds.AcceptPrefix` (= `"server:accept:"`), `.DismissPrefix` (= `"server:dismiss:"`), `static string Tail(string ip, int port)`, `static bool TryParseTail(string? tail, out string ip, out int port)`.
  - `ServerPairingPromptRenderer.RenderPrompt(string serverName, string ip, int port, string culture)` → `(Embed Embed, MessageComponent Components)`; `.RenderAdded(string serverName, string culture)` → same tuple (no buttons).

- [ ] **Step 2.1: Add the resx keys**

In `src/RustPlusBot.Localization/Strings.resx`, locate the alphabetical insertion point (`grep -n 'data name="s' src/RustPlusBot.Localization/Strings.resx | head -20` — insert the block before the first key sorting after `server.prompt`, e.g. before `settings.*`/`setup.*`/`smelt.*` keys) and add:

```xml
  <data name="server.prompt.accept" xml:space="preserve">
    <value>Accept</value>
  </data>
  <data name="server.prompt.added.body" xml:space="preserve">
    <value>{0} was added. Its channels are being created and the bot is connecting.</value>
  </data>
  <data name="server.prompt.added.title" xml:space="preserve">
    <value>Server added</value>
  </data>
  <data name="server.prompt.body" xml:space="preserve">
    <value>Detected a new server pairing: {0} ({1}:{2}). Add it?</value>
  </data>
  <data name="server.prompt.dismiss" xml:space="preserve">
    <value>Dismiss</value>
  </data>
  <data name="server.prompt.title" xml:space="preserve">
    <value>New server detected</value>
  </data>
```

In `src/RustPlusBot.Localization/Strings.fr.resx`, at the same alphabetical position, add:

```xml
  <data name="server.prompt.accept" xml:space="preserve">
    <value>Accepter</value>
  </data>
  <data name="server.prompt.added.body" xml:space="preserve">
    <value>{0} a été ajouté. Ses salons sont en cours de création et le bot se connecte.</value>
  </data>
  <data name="server.prompt.added.title" xml:space="preserve">
    <value>Serveur ajouté</value>
  </data>
  <data name="server.prompt.body" xml:space="preserve">
    <value>Nouvel appairage de serveur détecté : {0} ({1}:{2}). L'ajouter ?</value>
  </data>
  <data name="server.prompt.dismiss" xml:space="preserve">
    <value>Ignorer</value>
  </data>
  <data name="server.prompt.title" xml:space="preserve">
    <value>Nouveau serveur détecté</value>
  </data>
```

- [ ] **Step 2.2: Add the Localization project reference**

In `src/RustPlusBot.Features.Pairing/RustPlusBot.Features.Pairing.csproj`, add to the `ProjectReference` ItemGroup (it currently references Discord, Persistence, Domain, Abstractions, Features.Workspace):

```xml
    <ProjectReference Include="..\RustPlusBot.Localization\RustPlusBot.Localization.csproj" />
```

- [ ] **Step 2.3: Write the failing renderer test**

Create `tests/RustPlusBot.Features.Pairing.Tests/ServerPairingPromptRendererTests.cs`:

```csharp
using Discord;
using RustPlusBot.Features.Pairing.Rendering;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class ServerPairingPromptRendererTests
{
    private static ServerPairingPromptRenderer Create() => new(new ResxLocalizer());

    [Fact]
    public void RenderPrompt_shows_server_identity_and_carries_endpoint_tail()
    {
        var (embed, components) = Create().RenderPrompt("Rustopia", "1.2.3.4", 28015, "en");

        Assert.Equal("New server detected", embed.Title);
        Assert.Contains("Rustopia", embed.Description, StringComparison.Ordinal);
        Assert.Contains("1.2.3.4:28015", embed.Description, StringComparison.Ordinal);

        var buttons = components.Components.OfType<ActionRowComponent>().SelectMany(r => r.Components)
            .OfType<ButtonComponent>().ToList();
        Assert.Contains(buttons, b => b.CustomId == $"{ServerPairingComponentIds.AcceptPrefix}1.2.3.4:28015");
        Assert.Contains(buttons, b => b.CustomId == $"{ServerPairingComponentIds.DismissPrefix}1.2.3.4:28015");
    }

    [Fact]
    public void RenderPrompt_french_uses_french_copy()
    {
        var (embed, _) = Create().RenderPrompt("Rustopia", "1.2.3.4", 28015, "fr");

        Assert.Equal("Nouveau serveur détecté", embed.Title);
    }

    [Fact]
    public void RenderAdded_has_no_buttons()
    {
        var (embed, components) = Create().RenderAdded("Rustopia", "en");

        Assert.Equal("Server added", embed.Title);
        Assert.Contains("Rustopia", embed.Description, StringComparison.Ordinal);
        Assert.Empty(components.Components);
    }

    [Theory]
    [InlineData("1.2.3.4:28015", "1.2.3.4", 28015)]
    [InlineData("play.example.com:28083", "play.example.com", 28083)]
    [InlineData("2001:db8::1:28015", "2001:db8::1", 28015)]
    public void TryParseTail_round_trips_endpoints(string tail, string expectedIp, int expectedPort)
    {
        Assert.True(ServerPairingComponentIds.TryParseTail(tail, out var ip, out var port));
        Assert.Equal(expectedIp, ip);
        Assert.Equal(expectedPort, port);
        Assert.Equal(tail, ServerPairingComponentIds.Tail(ip, port));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-port")]
    [InlineData(":28015")]
    [InlineData("1.2.3.4:")]
    [InlineData("1.2.3.4:notaport")]
    [InlineData("1.2.3.4:0")]
    [InlineData("1.2.3.4:70000")]
    public void TryParseTail_rejects_invalid_tails(string? tail)
    {
        Assert.False(ServerPairingComponentIds.TryParseTail(tail, out _, out _));
    }
}
```

- [ ] **Step 2.4: Run the test to verify it fails**

```bash
dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter "FullyQualifiedName~ServerPairingPromptRendererTests"
```

Expected: build FAILURE — the Rendering types do not exist.

- [ ] **Step 2.5: Implement the component ids and renderer**

Create `src/RustPlusBot.Features.Pairing/Rendering/ServerPairingComponentIds.cs`:

```csharp
using System.Globalization;

namespace RustPlusBot.Features.Pairing.Rendering;

/// <summary>Custom ids for the server-pairing prompt in #setup. Tails encode "{ip}:{port}".</summary>
internal static class ServerPairingComponentIds
{
    /// <summary>Prompt Accept button; tail "{ip}:{port}".</summary>
    public const string AcceptPrefix = "server:accept:";

    /// <summary>Prompt Dismiss button; tail "{ip}:{port}".</summary>
    public const string DismissPrefix = "server:dismiss:";

    /// <summary>Builds the "{ip}:{port}" custom-id tail for an endpoint.</summary>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <returns>The custom-id tail.</returns>
    public static string Tail(string ip, int port) =>
        ip + ":" + port.ToString(CultureInfo.InvariantCulture);

    /// <summary>Parses an "{ip}:{port}" tail. Splits on the last ':' so IPv6 hosts keep their colons.</summary>
    /// <param name="tail">The custom-id tail.</param>
    /// <param name="ip">The parsed host or ip.</param>
    /// <param name="port">The parsed port (1-65535).</param>
    /// <returns>True when the tail carried a well-formed endpoint.</returns>
    public static bool TryParseTail(string? tail, out string ip, out int port)
    {
        ip = string.Empty;
        port = 0;
        if (string.IsNullOrEmpty(tail))
        {
            return false;
        }

        var sep = tail.LastIndexOf(':');
        if (sep <= 0 || sep == tail.Length - 1)
        {
            return false;
        }

        if (!int.TryParse(tail[(sep + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port)
            || port is < 1 or > 65535)
        {
            return false;
        }

        ip = tail[..sep];
        return true;
    }
}
```

Create `src/RustPlusBot.Features.Pairing/Rendering/ServerPairingPromptRenderer.cs`:

```csharp
using Discord;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Pairing.Rendering;

/// <summary>Renders the "New server detected — Add it?" prompt and the post-accept confirmation. Pure.</summary>
/// <param name="localizer">The shared localizer.</param>
internal sealed class ServerPairingPromptRenderer(ILocalizer localizer)
{
    /// <summary>Renders the transient server-pairing prompt with Accept/Dismiss buttons.</summary>
    /// <param name="serverName">The server's display name from the pairing.</param>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The prompt embed and Accept/Dismiss row.</returns>
    public (Embed Embed, MessageComponent Components) RenderPrompt(
        string serverName,
        string ip,
        int port,
        string culture)
    {
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.prompt.title", culture))
            .WithDescription(localizer.Get("server.prompt.body", culture, serverName, ip, port))
            .Build();

        var tail = ServerPairingComponentIds.Tail(ip, port);
        var components = new ComponentBuilder()
            .WithButton(localizer.Get("server.prompt.accept", culture), ServerPairingComponentIds.AcceptPrefix + tail,
                ButtonStyle.Success)
            .WithButton(localizer.Get("server.prompt.dismiss", culture),
                ServerPairingComponentIds.DismissPrefix + tail, ButtonStyle.Secondary)
            .Build();

        return (embed, components);
    }

    /// <summary>Renders the buttonless confirmation the prompt is edited into after Accept.</summary>
    /// <param name="serverName">The server's display name.</param>
    /// <param name="culture">The guild culture.</param>
    /// <returns>The confirmation embed and an empty component row.</returns>
    public (Embed Embed, MessageComponent Components) RenderAdded(string serverName, string culture)
    {
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.prompt.added.title", culture))
            .WithDescription(localizer.Get("server.prompt.added.body", culture, serverName))
            .Build();

        return (embed, new ComponentBuilder().Build());
    }
}
```

- [ ] **Step 2.6: Run the tests to verify they pass**

```bash
dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter "FullyQualifiedName~ServerPairingPromptRendererTests"
```

Expected: PASS (all renderer + tail tests).

- [ ] **Step 2.7: Commit**

```bash
git add src/RustPlusBot.Localization src/RustPlusBot.Features.Pairing tests/RustPlusBot.Features.Pairing.Tests
git commit -m "feat(pairing): server-pairing prompt renderer, component ids, localization

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Setup-channel poster + setup-missing owner notification

**Files:**
- Create: `src/RustPlusBot.Features.Pairing/Posting/ISetupChannelPoster.cs`
- Create: `src/RustPlusBot.Features.Pairing/Posting/DiscordSetupChannelPoster.cs`
- Modify: `src/RustPlusBot.Features.Pairing/Notifications/IOwnerNotifier.cs`
- Modify: `src/RustPlusBot.Features.Pairing/Notifications/DiscordOwnerNotifier.cs`
- Modify: `tests/RustPlusBot.Features.Pairing.Tests/Fakes/RecordingOwnerNotifier.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/DiscordOwnerNotifierTests.cs` (add one test)

**Interfaces:**
- Consumes: `DiscordChannelMessenger.EnsureAsync(ulong channelId, ulong? messageId, Embed embed, MessageComponent components, ILogger logger, CancellationToken ct)` → `Task<ulong?>` (`RustPlusBot.Discord.Posting`); `IUserDmSender.SendAsync(ulong userId, string text, CancellationToken ct)`.
- Produces (used by Task 4):
  - `internal interface ISetupChannelPoster { Task<ulong?> EnsureAsync(ulong channelId, ulong? messageId, Embed embed, MessageComponent components, CancellationToken cancellationToken); }`
  - `IOwnerNotifier.NotifySetupChannelMissingAsync(ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default)`

- [ ] **Step 3.1: Write the failing notifier test**

In `tests/RustPlusBot.Features.Pairing.Tests/DiscordOwnerNotifierTests.cs`, add this test alongside the existing one (mirror its arrange style — it substitutes `IUserDmSender` and asserts `SendAsync`):

```csharp
    [Fact]
    public async Task NotifySetupChannelMissing_dms_owner_with_setup_instructions()
    {
        var dm = Substitute.For<IUserDmSender>();
        var notifier = new DiscordOwnerNotifier(dm);

        await notifier.NotifySetupChannelMissingAsync(10UL, 99UL, CancellationToken.None);

        await dm.Received(1).SendAsync(99UL,
            Arg.Is<string>(m => m.Contains("/setup", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 3.2: Run the test to verify it fails**

```bash
dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter "FullyQualifiedName~DiscordOwnerNotifierTests"
```

Expected: build FAILURE — `NotifySetupChannelMissingAsync` does not exist.

- [ ] **Step 3.3: Implement notifier + poster**

In `src/RustPlusBot.Features.Pairing/Notifications/IOwnerNotifier.cs`, add to the interface:

```csharp
    /// <summary>Tells the owner a server pairing arrived but no #setup channel exists to prompt in.</summary>
    /// <param name="guildId">The guild the pairing belongs to.</param>
    /// <param name="ownerUserId">The Discord user to notify.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task NotifySetupChannelMissingAsync(ulong guildId, ulong ownerUserId, CancellationToken cancellationToken = default);
```

In `src/RustPlusBot.Features.Pairing/Notifications/DiscordOwnerNotifier.cs`, add:

```csharp
    /// <inheritdoc />
    public Task NotifySetupChannelMissingAsync(
        ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default) =>
        dmSender.SendAsync(
            ownerUserId,
            "A server pairing arrived but this guild has no #setup channel. Run /setup, then press \"Pair\" in-game again.",
            cancellationToken);
```

In `tests/RustPlusBot.Features.Pairing.Tests/Fakes/RecordingOwnerNotifier.cs`, add (the fake must keep compiling):

```csharp
    /// <summary>All (guild, owner) pairs told that #setup is missing.</summary>
    public ConcurrentBag<(ulong Guild, ulong Owner)> SetupMissing { get; } = [];

    /// <inheritdoc />
    public Task NotifySetupChannelMissingAsync(ulong guildId,
        ulong ownerUserId,
        CancellationToken cancellationToken = default)
    {
        SetupMissing.Add((guildId, ownerUserId));
        return Task.CompletedTask;
    }
```

Create `src/RustPlusBot.Features.Pairing/Posting/ISetupChannelPoster.cs`:

```csharp
namespace RustPlusBot.Features.Pairing.Posting;

/// <summary>Posts/edits server-pairing prompt messages in #setup by message id, self-healing a deleted message.</summary>
internal interface ISetupChannelPoster
{
    /// <summary>Edits the message at <paramref name="messageId"/> if present and found; otherwise posts a new one.</summary>
    /// <param name="channelId">The #setup channel id.</param>
    /// <param name="messageId">The known prompt message id, or null to post fresh.</param>
    /// <param name="embed">The embed to show.</param>
    /// <param name="components">The button row (may be empty).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The (possibly new) message id, or null on failure.</returns>
    Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        global::Discord.Embed embed,
        global::Discord.MessageComponent components,
        CancellationToken cancellationToken);
}
```

Create `src/RustPlusBot.Features.Pairing/Posting/DiscordSetupChannelPoster.cs`:

```csharp
using Discord;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Pairing.Posting;

/// <summary>Posts/edits server-pairing prompts in #setup by message id. Untested integration shim.</summary>
/// <param name="messenger">The shared gated channel messenger.</param>
/// <param name="logger">The logger.</param>
internal sealed class DiscordSetupChannelPoster(
    DiscordChannelMessenger messenger,
    ILogger<DiscordSetupChannelPoster> logger) : ISetupChannelPoster
{
    /// <inheritdoc />
    public Task<ulong?> EnsureAsync(
        ulong channelId,
        ulong? messageId,
        Embed embed,
        MessageComponent components,
        CancellationToken cancellationToken)
        => messenger.EnsureAsync(channelId, messageId, embed, components, logger, cancellationToken);
}
```

- [ ] **Step 3.4: Run the tests to verify they pass**

```bash
dotnet test tests/RustPlusBot.Features.Pairing.Tests
```

Expected: PASS — the whole Pairing test project stays green (proves the fake and all `IOwnerNotifier` consumers still compile).

- [ ] **Step 3.5: Commit**

```bash
git add src/RustPlusBot.Features.Pairing tests/RustPlusBot.Features.Pairing.Tests
git commit -m "feat(pairing): setup-channel poster + setup-missing owner notification

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: `ServerPairingCoordinator` + DI wiring

**Files:**
- Create: `src/RustPlusBot.Features.Pairing/Pairing/IServerPairingCoordinator.cs`
- Create: `src/RustPlusBot.Features.Pairing/Pairing/ServerPairingCoordinator.cs`
- Modify: `src/RustPlusBot.Features.Pairing/PairingServiceCollectionExtensions.cs`
- Modify: `tests/RustPlusBot.Features.Pairing.Tests/PairingRegistrationTests.cs`
- Test: `tests/RustPlusBot.Features.Pairing.Tests/ServerPairingCoordinatorTests.cs`

**Interfaces:**
- Consumes: `ISetupChannelLocator` (Task 1), `ServerPairingPromptRenderer`/`ServerPairingComponentIds` (Task 2), `ISetupChannelPoster` + `IOwnerNotifier.NotifySetupChannelMissingAsync` (Task 3), `IServerService.ResolveOrCreateByEndpointAsync` / `.SetFacepunchServerIdAsync`, `ICredentialStore.UpsertFromPairingAsync(StoreCredentialRequest, bool markActive, CancellationToken)`, `IEventBus.PublishAsync`, `IWorkspaceStore.GetCultureAsync`, `PairingNotification`.
- Produces (used by Tasks 5-6):

```csharp
internal enum ServerPairingAcceptOutcome { Added = 0, AlreadyAdded = 1, Expired = 2 }

internal interface IServerPairingCoordinator
{
    Task HandleDetectedAsync(ulong guildId, ulong ownerUserId, PairingNotification notification,
        CancellationToken cancellationToken);
    Task<ServerPairingAcceptOutcome> TryAcceptAsync(ulong guildId, string ip, int port,
        CancellationToken cancellationToken);
    bool TryDismiss(ulong guildId, string ip, int port);
}
```

Note: `TryAcceptAsync` takes no accepting-user id — the persisted `AddedByUserId` and credential owner must be the **pairing owner** held in pending state (their Rust+ token), exactly as today's handler path.

- [ ] **Step 4.1: Write the failing coordinator tests**

Create `tests/RustPlusBot.Features.Pairing.Tests/ServerPairingCoordinatorTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Posting;
using RustPlusBot.Features.Pairing.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Localization;
using RustPlusBot.Persistence;
using RustPlusBot.Persistence.Credentials;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class ServerPairingCoordinatorTests
{
    private static readonly Guid FpServer = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ICredentialProtector PassThrough()
    {
        var p = Substitute.For<ICredentialProtector>();
        p.Protect(Arg.Any<string>()).Returns(c => c.Arg<string>());
        return p;
    }

    private static PairingNotification ServerPairing(string ip = "1.2.3.4", int port = 28015, ulong steam = 7UL) =>
        new(PairingKind.Server, "Rustopia", ip, port, steam, "ptoken", FacepunchServerId: FpServer, EntityId: 0UL);

    private sealed record Harness(
        ServerPairingCoordinator Coordinator,
        BotDbContext Context,
        Microsoft.Data.Sqlite.SqliteConnection Connection,
        ISetupChannelLocator Locator,
        ISetupChannelPoster Poster,
        IOwnerNotifier Notifier,
        IEventBus Bus);

    private static Harness Create(ulong? channelId = 777UL)
    {
        var (context, connection) = TestDb.Create();

        var workspace = Substitute.For<IWorkspaceStore>();
        workspace.GetCultureAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns("en");

        var services = new ServiceCollection();
        services.AddScoped<IServerService>(_ => new ServerService(context));
        services.AddScoped<ICredentialStore>(_ => new CredentialStore(context, PassThrough()));
        services.AddScoped(_ => workspace);
        var provider = services.BuildServiceProvider();

        var locator = Substitute.For<ISetupChannelLocator>();
        locator.GetChannelIdAsync(Arg.Any<ulong>(), Arg.Any<CancellationToken>()).Returns(channelId);

        var poster = Substitute.For<ISetupChannelPoster>();
        poster.EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(), Arg.Any<global::Discord.Embed>(),
                Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>())
            .Returns(900UL);

        var notifier = Substitute.For<IOwnerNotifier>();
        var bus = Substitute.For<IEventBus>();
        var coordinator = new ServerPairingCoordinator(
            provider.GetRequiredService<IServiceScopeFactory>(), locator, poster,
            new ServerPairingPromptRenderer(new ResxLocalizer()), notifier, bus,
            NullLogger<ServerPairingCoordinator>.Instance);

        return new Harness(coordinator, context, connection, locator, poster, notifier, bus);
    }

    [Fact]
    public async Task Detected_posts_prompt_holds_pending_and_persists_nothing()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;

        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(777UL, null, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.True(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
        Assert.Empty(await h.Context.RustServers.ToListAsync());
        Assert.Empty(await h.Context.PlayerCredentials.ToListAsync());
        await h.Bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detected_again_while_pending_refreshes_without_reposting()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;

        await h.Coordinator.HandleDetectedAsync(10UL, 1UL, ServerPairing(steam: 1UL), CancellationToken.None);
        await h.Coordinator.HandleDetectedAsync(10UL, 2UL, ServerPairing(steam: 2UL), CancellationToken.None);

        await h.Poster.Received(1).EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());

        // Accepting proves the refreshed pairing (owner 2) won.
        await h.Coordinator.TryAcceptAsync(10UL, "1.2.3.4", 28015, CancellationToken.None);
        var server = await h.Context.RustServers.SingleAsync();
        Assert.Equal(2UL, server.AddedByUserId);
        var credential = await h.Context.PlayerCredentials.SingleAsync();
        Assert.Equal(2UL, credential.OwnerUserId);
    }

    [Fact]
    public async Task Detected_without_setup_channel_notifies_owner_and_drops()
    {
        var h = Create(channelId: null);
        await using var _ = h.Context;
        await using var __ = h.Connection;

        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        await h.Notifier.Received(1).NotifySetupChannelMissingAsync(10UL, 99UL, Arg.Any<CancellationToken>());
        Assert.False(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
        await h.Poster.DidNotReceive().EnsureAsync(Arg.Any<ulong>(), Arg.Any<ulong?>(),
            Arg.Any<global::Discord.Embed>(), Arg.Any<global::Discord.MessageComponent>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_persists_publishes_event_once_and_edits_prompt()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;
        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        var outcome = await h.Coordinator.TryAcceptAsync(10UL, "1.2.3.4", 28015, CancellationToken.None);

        Assert.Equal(ServerPairingAcceptOutcome.Added, outcome);
        var server = await h.Context.RustServers.SingleAsync();
        Assert.Equal("Rustopia", server.Name);
        Assert.Equal(FpServer, server.FacepunchServerId);
        var credential = await h.Context.PlayerCredentials.SingleAsync();
        Assert.Equal(server.Id, credential.RustServerId);
        Assert.Equal(CredentialStatus.Active, credential.Status);
        await h.Bus.Received(1).PublishAsync(
            Arg.Is<ServerRegisteredEvent>(e => e.GuildId == 10UL && e.ServerId == server.Id),
            Arg.Any<CancellationToken>());
        await h.Poster.Received(1).EnsureAsync(777UL, 900UL, Arg.Any<global::Discord.Embed>(),
            Arg.Any<global::Discord.MessageComponent>(), Arg.Any<CancellationToken>());
        Assert.False(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
    }

    [Fact]
    public async Task Accept_when_server_already_exists_upserts_credential_without_event()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;
        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        // Another path created the same endpoint while the prompt sat unanswered.
        await new ServerService(h.Context).AddAsync(10UL, 50UL, "Rustopia", "1.2.3.4", 28015);

        var outcome = await h.Coordinator.TryAcceptAsync(10UL, "1.2.3.4", 28015, CancellationToken.None);

        Assert.Equal(ServerPairingAcceptOutcome.AlreadyAdded, outcome);
        Assert.Single(await h.Context.RustServers.ToListAsync());
        var credential = await h.Context.PlayerCredentials.SingleAsync();
        Assert.Equal(CredentialStatus.Standby, credential.Status);
        await h.Bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Accept_without_pending_returns_expired_and_persists_nothing()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;

        var outcome = await h.Coordinator.TryAcceptAsync(10UL, "1.2.3.4", 28015, CancellationToken.None);

        Assert.Equal(ServerPairingAcceptOutcome.Expired, outcome);
        Assert.Empty(await h.Context.RustServers.ToListAsync());
        await h.Bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Dismiss_clears_pending_once()
    {
        var h = Create();
        await using var _ = h.Context;
        await using var __ = h.Connection;
        await h.Coordinator.HandleDetectedAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        Assert.True(h.Coordinator.TryDismiss(10UL, "1.2.3.4", 28015));
        Assert.False(h.Coordinator.TryDismiss(10UL, "1.2.3.4", 28015));
        Assert.False(h.Coordinator.HasPending(10UL, "1.2.3.4", 28015));
    }
}
```

- [ ] **Step 4.2: Run the tests to verify they fail**

```bash
dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter "FullyQualifiedName~ServerPairingCoordinatorTests"
```

Expected: build FAILURE — coordinator types do not exist.

- [ ] **Step 4.3: Implement the coordinator**

Create `src/RustPlusBot.Features.Pairing/Pairing/IServerPairingCoordinator.cs`:

```csharp
using RustPlusBot.Features.Pairing.Listening;

namespace RustPlusBot.Features.Pairing.Pairing;

/// <summary>The outcome of accepting a pending server pairing.</summary>
internal enum ServerPairingAcceptOutcome
{
    /// <summary>The server was created; the full registration cycle was triggered.</summary>
    Added = 0,

    /// <summary>The server already existed (race); the credential was upserted, no event fired.</summary>
    AlreadyAdded = 1,

    /// <summary>No pending pairing was held (restart or double-click); nothing was persisted.</summary>
    Expired = 2,
}

/// <summary>Coordinates the "Add this server?" #setup prompt for new-server pairings.</summary>
internal interface IServerPairingCoordinator
{
    /// <summary>Handles a new-server pairing: prompt in #setup and hold the notification pending.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="ownerUserId">The Discord user whose connected account produced the pairing.</param>
    /// <param name="notification">The transient pairing notification (holds the Rust+ token).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    Task HandleDetectedAsync(
        ulong guildId,
        ulong ownerUserId,
        PairingNotification notification,
        CancellationToken cancellationToken);

    /// <summary>Accepts a pending pairing: persist server + credential and trigger registration. Race-guarded.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The accept outcome.</returns>
    Task<ServerPairingAcceptOutcome> TryAcceptAsync(
        ulong guildId,
        string ip,
        int port,
        CancellationToken cancellationToken);

    /// <summary>Drops a pending pairing; returns whether one was held.</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <returns>True when a pending pairing was removed.</returns>
    bool TryDismiss(ulong guildId, string ip, int port);
}
```

Create `src/RustPlusBot.Features.Pairing/Pairing/ServerPairingCoordinator.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Pairing.Listening;
using RustPlusBot.Features.Pairing.Notifications;
using RustPlusBot.Features.Pairing.Posting;
using RustPlusBot.Features.Pairing.Rendering;
using RustPlusBot.Features.Workspace.Locating;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Pairing.Pairing;

/// <summary>Turns a new-server pairing into an "Add it?" prompt in #setup and, on Accept, a registered server.
/// Pending state is in-memory only (mirrors the entity coordinators): lost on restart, re-pair to re-prompt.</summary>
/// <param name="scopeFactory">Opens scopes for the scoped server/credential/workspace stores.</param>
/// <param name="locator">Resolves the guild's #setup channel id.</param>
/// <param name="poster">Posts/edits the prompt message.</param>
/// <param name="renderer">Renders the prompt and confirmation embeds.</param>
/// <param name="ownerNotifier">DMs the owner when no #setup channel exists.</param>
/// <param name="eventBus">Publishes ServerRegisteredEvent on accepted creation.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class ServerPairingCoordinator(
    IServiceScopeFactory scopeFactory,
    ISetupChannelLocator locator,
    ISetupChannelPoster poster,
    ServerPairingPromptRenderer renderer,
    IOwnerNotifier ownerNotifier,
    IEventBus eventBus,
    ILogger<ServerPairingCoordinator> logger) : IServerPairingCoordinator
{
    private readonly ConcurrentDictionary<(ulong Guild, string Ip, int Port), Pending> _pending = new();

    /// <summary>Gets whether a pairing is pending for the endpoint (test seam).</summary>
    /// <param name="guildId">The guild id.</param>
    /// <param name="ip">The server host or ip.</param>
    /// <param name="port">The Rust+ app port.</param>
    /// <returns>True when a prompt is pending.</returns>
    public bool HasPending(ulong guildId, string ip, int port) => _pending.ContainsKey((guildId, ip, port));

    /// <inheritdoc />
    public async Task HandleDetectedAsync(
        ulong guildId,
        ulong ownerUserId,
        PairingNotification notification,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var key = (guildId, notification.Ip, notification.Port);
        if (_pending.TryGetValue(key, out var existing))
        {
            // Repeat in-game "Pair" press: keep the prompt, refresh the held pairing (latest token wins).
            _pending[key] = new Pending(ownerUserId, notification, existing.MessageId);
            return;
        }

        var channelId = await locator.GetChannelIdAsync(guildId, cancellationToken).ConfigureAwait(false);
        if (channelId is not { } channel)
        {
            LogSetupChannelMissing(logger, guildId);
            await ownerNotifier.NotifySetupChannelMissingAsync(guildId, ownerUserId, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        var culture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        var (embed, components) =
            renderer.RenderPrompt(notification.ServerName, notification.Ip, notification.Port, culture);
        var messageId = await poster.EnsureAsync(channel, null, embed, components, cancellationToken)
            .ConfigureAwait(false);
        _pending[key] = new Pending(ownerUserId, notification, messageId);
    }

    /// <inheritdoc />
    public async Task<ServerPairingAcceptOutcome> TryAcceptAsync(
        ulong guildId,
        string ip,
        int port,
        CancellationToken cancellationToken)
    {
        if (!_pending.TryRemove((guildId, ip, port), out var pending))
        {
            return ServerPairingAcceptOutcome.Expired;
        }

        var notification = pending.Notification;
        RustServer server;
        bool created;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            // Replays the pre-confirmation registration path verbatim (see PairingHandler history).
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var credentials = scope.ServiceProvider.GetRequiredService<ICredentialStore>();
            (server, created) = await servers.ResolveOrCreateByEndpointAsync(
                    guildId, pending.OwnerUserId, notification.ServerName, notification.Ip, notification.Port,
                    cancellationToken)
                .ConfigureAwait(false);

            // Only backfill a real Facepunch GUID. Persisting Guid.Empty would make every server that paired
            // without one share the same id, breaking GUID-based entity-pairing attribution.
            if (notification.FacepunchServerId != Guid.Empty)
            {
                await servers.SetFacepunchServerIdAsync(server.Id, notification.FacepunchServerId, cancellationToken)
                    .ConfigureAwait(false);
            }

            await credentials.UpsertFromPairingAsync(
                new StoreCredentialRequest(guildId, server.Id, pending.OwnerUserId, notification.PlayerId,
                    notification.PlayerToken),
                markActive: created,
                cancellationToken).ConfigureAwait(false);
        }

        if (created)
        {
            await eventBus.PublishAsync(new ServerRegisteredEvent(guildId, server.Id), cancellationToken)
                .ConfigureAwait(false);
        }

        var channelId = await locator.GetChannelIdAsync(guildId, cancellationToken).ConfigureAwait(false);
        if (channelId is { } channel)
        {
            var culture = await GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
            var (embed, components) = renderer.RenderAdded(notification.ServerName, culture);
            _ = await poster.EnsureAsync(channel, pending.MessageId, embed, components, cancellationToken)
                .ConfigureAwait(false);
        }

        return created ? ServerPairingAcceptOutcome.Added : ServerPairingAcceptOutcome.AlreadyAdded;
    }

    /// <inheritdoc />
    public bool TryDismiss(ulong guildId, string ip, int port) => _pending.TryRemove((guildId, ip, port), out _);

    private async Task<string> GetCultureAsync(ulong guildId, CancellationToken ct)
    {
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            return await store.GetCultureAsync(guildId, ct).ConfigureAwait(false);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Server pairing for guild {GuildId} dropped: no #setup channel to prompt in.")]
    private static partial void LogSetupChannelMissing(ILogger logger, ulong guildId);

    private sealed record Pending(ulong OwnerUserId, PairingNotification Notification, ulong? MessageId);
}
```

- [ ] **Step 4.4: Register in DI + update the registration test**

In `src/RustPlusBot.Features.Pairing/PairingServiceCollectionExtensions.cs`:
- Add usings: `RustPlusBot.Features.Pairing.Posting;`, `RustPlusBot.Features.Pairing.Rendering;`, `RustPlusBot.Localization;`
- After the existing `services.AddScoped<IAccountDisconnectService, AccountDisconnectService>();` line, add:

```csharp
        services.AddRustPlusBotLocalization();
        services.AddSingleton<ServerPairingPromptRenderer>();
        services.AddSingleton<ISetupChannelPoster, DiscordSetupChannelPoster>();
        services.AddSingleton<IServerPairingCoordinator, ServerPairingCoordinator>();
```

In `tests/RustPlusBot.Features.Pairing.Tests/PairingRegistrationTests.cs`:
- Add usings: `RustPlusBot.Features.Pairing.Posting;`, `RustPlusBot.Features.Workspace.Locating;`
- After the `services.AddPairing();` line, add (later registrations win single-service resolution, so these substitutes shadow the Discord/Workspace-backed implementations the test host can't build):

```csharp
        // Shadow the Discord-backed poster and Workspace-backed locator with substitutes.
        services.AddSingleton(Substitute.For<ISetupChannelLocator>());
        services.AddSingleton(Substitute.For<ISetupChannelPoster>());
```

- Add to the assertions:

```csharp
        Assert.NotNull(provider.GetRequiredService<IServerPairingCoordinator>());
```

(also add `using RustPlusBot.Features.Pairing.Pairing;` if not already present — it is, via the existing usings list).

- [ ] **Step 4.5: Run the tests to verify they pass**

```bash
dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter "FullyQualifiedName~ServerPairingCoordinatorTests|FullyQualifiedName~PairingRegistrationTests"
```

Expected: PASS (7 coordinator tests + registration test).

- [ ] **Step 4.6: Commit**

```bash
git add src/RustPlusBot.Features.Pairing tests/RustPlusBot.Features.Pairing.Tests
git commit -m "feat(pairing): ServerPairingCoordinator with in-memory pending + DI wiring

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Gate `PairingHandler`'s server path behind the coordinator

**Files:**
- Modify: `src/RustPlusBot.Features.Pairing/Pairing/PairingHandler.cs`
- Modify: `tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs`

**Interfaces:**
- Consumes: `IServerPairingCoordinator.HandleDetectedAsync` (Task 4); `IServerService.GetByEndpointAsync(ulong guildId, string ip, int port, CancellationToken)` (already exists, `ServerService.cs:48`).
- Produces: `PairingHandler` primary-constructor order becomes `(IServerService servers, ICredentialStore credentials, IEventBus eventBus, IServerPairingCoordinator serverPairings, ILogger<PairingHandler> logger)`. Entity path unchanged.

- [ ] **Step 5.1: Rewrite the server-path tests**

In `tests/RustPlusBot.Features.Pairing.Tests/PairingHandlerTests.cs`:

Add usings `NSubstitute` is already there; add `RustPlusBot.Domain.Servers;`.

Replace the `CreateHandler` helper with one that also surfaces the coordinator substitute, and add a seeding helper:

```csharp
    private static (PairingHandler Handler, IServerPairingCoordinator Coordinator) CreateHandler(
        BotDbContext context, IEventBus bus)
    {
        var coordinator = Substitute.For<IServerPairingCoordinator>();
        var handler = new PairingHandler(new ServerService(context), new CredentialStore(context, PassThrough()),
            bus, coordinator, Microsoft.Extensions.Logging.Abstractions.NullLogger<PairingHandler>.Instance);
        return (handler, coordinator);
    }

    private static async Task<RustServer> SeedServerAsync(BotDbContext context, Guid? facepunchServerId = null)
    {
        var service = new ServerService(context);
        var server = await service.AddAsync(10UL, 99UL, "Rustopia", "1.2.3.4", 28015);
        if (facepunchServerId is { } fp)
        {
            await service.SetFacepunchServerIdAsync(server.Id, fp);
        }

        return server;
    }
```

Replace `ServerPairing_CreatesServerCredentialAndFiresEventOnce`, `SecondOwnerSameServer_AddsStandbyCredentialNoEvent`, and `ServerPairing_BackfillsFacepunchServerId` with:

```csharp
    [Fact]
    public async Task NewServerPairing_RoutesToCoordinator_PersistsNothing()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var (handler, coordinator) = CreateHandler(context, bus);

        await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        await coordinator.Received(1).HandleDetectedAsync(10UL, 99UL,
            Arg.Is<PairingNotification>(n => n.Ip == "1.2.3.4" && n.Port == 28015), Arg.Any<CancellationToken>());
        Assert.Empty(await context.RustServers.ToListAsync());
        Assert.Empty(await context.PlayerCredentials.ToListAsync());
        await bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingServerPairing_AddsStandbyCredential_NoEventNoPrompt()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var (handler, coordinator) = CreateHandler(context, bus);
        await SeedServerAsync(context);

        await handler.HandleAsync(10UL, 2UL, ServerPairing(steam: 2UL), CancellationToken.None);

        Assert.Single(await context.RustServers.ToListAsync());
        var credential = await context.PlayerCredentials.SingleAsync(c => c.OwnerUserId == 2UL);
        Assert.Equal(CredentialStatus.Standby, credential.Status);
        await bus.DidNotReceive().PublishAsync(Arg.Any<ServerRegisteredEvent>(), Arg.Any<CancellationToken>());
        await coordinator.DidNotReceive().HandleDetectedAsync(Arg.Any<ulong>(), Arg.Any<ulong>(),
            Arg.Any<PairingNotification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExistingServerPairing_BackfillsFacepunchServerId()
    {
        var (context, connection) = TestDb.Create();
        await using var _ = context;
        await using var __ = connection;
        var bus = Substitute.For<IEventBus>();
        var (handler, _) = CreateHandler(context, bus);
        await SeedServerAsync(context); // no Facepunch id yet

        await handler.HandleAsync(10UL, 99UL, ServerPairing(), CancellationToken.None);

        var server = await context.RustServers.SingleAsync();
        Assert.Equal(FpServer, server.FacepunchServerId);
    }
```

Update the four entity tests: they currently create the server via `handler.HandleAsync(10UL, 99UL, ServerPairing(), ...)` — replace that arrange line with `var server = await SeedServerAsync(context, FpServer);` (and drop the now-unneeded `var server = await context.RustServers.SingleAsync();` line where present; `EntityPairing_UnknownServer_DropsAndCreatesNothing` needs no seeding change). Destructure the new `CreateHandler` tuple everywhere: `var (handler, _) = CreateHandler(context, bus);`.

- [ ] **Step 5.2: Run the tests to verify they fail**

```bash
dotnet test tests/RustPlusBot.Features.Pairing.Tests --filter "FullyQualifiedName~PairingHandlerTests"
```

Expected: build FAILURE — `PairingHandler` has no coordinator parameter yet.

- [ ] **Step 5.3: Split the handler's server path**

In `src/RustPlusBot.Features.Pairing/Pairing/PairingHandler.cs`, change the class declaration and doc:

```csharp
/// <summary>Default <see cref="IPairingHandler"/>: known servers get a silent credential upsert; new servers
/// are routed to the #setup confirmation prompt; entity pairings fan out to their feature events.</summary>
/// <param name="servers">Server lookup/backfill.</param>
/// <param name="credentials">The credential pool store.</param>
/// <param name="eventBus">Publishes the entity paired events.</param>
/// <param name="serverPairings">Prompts for confirmation before a new server is registered.</param>
/// <param name="logger">The logger.</param>
internal sealed partial class PairingHandler(
    IServerService servers,
    ICredentialStore credentials,
    IEventBus eventBus,
    IServerPairingCoordinator serverPairings,
    ILogger<PairingHandler> logger) : IPairingHandler
```

Replace the server-path body of `HandleAsync` (everything after the entity branch, currently lines 34-56) with:

```csharp
        var existing = await servers.GetByEndpointAsync(guildId, notification.Ip, notification.Port, cancellationToken)
            .ConfigureAwait(false);
        if (existing is null)
        {
            // New server: nothing is persisted until the user accepts the #setup prompt.
            await serverPairings.HandleDetectedAsync(guildId, ownerUserId, notification, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // Only backfill a real Facepunch GUID. Persisting Guid.Empty would make every server that paired
        // without one share the same id, breaking GUID-based entity-pairing attribution.
        if (notification.FacepunchServerId != Guid.Empty)
        {
            await servers.SetFacepunchServerIdAsync(existing.Id, notification.FacepunchServerId, cancellationToken)
                .ConfigureAwait(false);
        }

        await credentials.UpsertFromPairingAsync(
            new StoreCredentialRequest(guildId, existing.Id, ownerUserId, notification.PlayerId,
                notification.PlayerToken),
            markActive: false,
            cancellationToken).ConfigureAwait(false);
```

(`ServerRegisteredEvent` is no longer published here — the coordinator owns it. The `RustPlusBot.Abstractions.Events` using stays for the entity events.)

- [ ] **Step 5.4: Run the full Pairing test project**

```bash
dotnet test tests/RustPlusBot.Features.Pairing.Tests
```

Expected: PASS — handler, coordinator, renderer, notifier, supervisor, registration all green.

- [ ] **Step 5.5: Commit**

```bash
git add src/RustPlusBot.Features.Pairing tests/RustPlusBot.Features.Pairing.Tests
git commit -m "feat(pairing): gate new-server pairing behind #setup confirmation

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: `ServerPairingComponentModule` (Accept/Dismiss buttons)

Discord interaction modules are coverage-excluded integration shims in this repo (see `SwitchComponentModule`) — no unit tests; the module stays thin. It is auto-discovered via the `InteractionModuleAssembly` already contributed by `AddPairing`.

**Files:**
- Create: `src/RustPlusBot.Features.Pairing/Modules/ServerPairingComponentModule.cs`

**Interfaces:**
- Consumes: `IServerPairingCoordinator` (Task 4), `ServerPairingComponentIds` (Task 2).
- Produces: component handlers for `server:accept:*` / `server:dismiss:*`.

- [ ] **Step 6.1: Write the module**

Create `src/RustPlusBot.Features.Pairing/Modules/ServerPairingComponentModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Pairing.Pairing;
using RustPlusBot.Features.Pairing.Rendering;

namespace RustPlusBot.Features.Pairing.Modules;

/// <summary>Thin handler for the #setup server-pairing prompt buttons. Any guild member.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class ServerPairingComponentModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const string InvalidControlMessage = "That control wasn't valid.";
    private const string ExpiredMessage = "This pairing expired — press \"Pair\" in-game again.";

    /// <summary>Accepts a pending server pairing and triggers the full registration cycle.</summary>
    /// <param name="tail">The "{ip}:{port}" custom-id tail.</param>
    [ComponentInteraction(ServerPairingComponentIds.AcceptPrefix + "*")]
    public async Task AcceptAsync(string tail)
    {
        if (!ServerPairingComponentIds.TryParseTail(tail, out var ip, out var port) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        ServerPairingAcceptOutcome outcome;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<IServerPairingCoordinator>();
            outcome = await coordinator.TryAcceptAsync(Context.Guild.Id, ip, port, CancellationToken.None)
                .ConfigureAwait(false);
        }

        if (outcome == ServerPairingAcceptOutcome.Expired)
        {
            // The prompt outlived its pending state (restart/double-click) — clean up the orphaned message.
            await DeletePromptMessageSafeAsync().ConfigureAwait(false);
        }

        await FollowupAsync(outcome switch
        {
            ServerPairingAcceptOutcome.Added => "Server added.",
            ServerPairingAcceptOutcome.AlreadyAdded => "That server was already added.",
            _ => ExpiredMessage,
        }, ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Dismisses a pending server pairing and removes the transient prompt message.</summary>
    /// <param name="tail">The "{ip}:{port}" custom-id tail.</param>
    [ComponentInteraction(ServerPairingComponentIds.DismissPrefix + "*")]
    public async Task DismissAsync(string tail)
    {
        if (!ServerPairingComponentIds.TryParseTail(tail, out var ip, out var port) || Context.Guild is null)
        {
            await RespondAsync(InvalidControlMessage, ephemeral: true).ConfigureAwait(false);
            return;
        }

        bool dismissed;
        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<IServerPairingCoordinator>();
            dismissed = coordinator.TryDismiss(Context.Guild.Id, ip, port);
        }

        // Delete the actual prompt message that hosts this button (the component interaction's source
        // message) — not the ephemeral interaction response. Best-effort: a delete failure is non-fatal.
        await DeletePromptMessageSafeAsync().ConfigureAwait(false);
        await RespondAsync(dismissed ? "Dismissed." : ExpiredMessage, ephemeral: true).ConfigureAwait(false);
    }

    private async Task DeletePromptMessageSafeAsync()
    {
        try
        {
            // The source message of a component interaction is the prompt that carries the button.
            if (Context.Interaction is IComponentInteraction component)
            {
                await component.Message.DeleteAsync().ConfigureAwait(false);
            }
        }
#pragma warning disable CA1031 // Best-effort prompt cleanup; a delete failure is non-fatal.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Ignore: the prompt is transient and harmless if it lingers.
            _ = ex;
        }
    }
}
```

- [ ] **Step 6.2: Build to verify it compiles**

```bash
dotnet build RustPlusBot.slnx
```

Expected: Build succeeded, 0 errors. (If a coverage/Sonar exclusion list enumerates module paths explicitly rather than by `**/Modules/**` pattern, check how `CredentialModule.cs` is excluded and extend the same way — pattern-based exclusion needs no change.)

- [ ] **Step 6.3: Commit**

```bash
git add src/RustPlusBot.Features.Pairing
git commit -m "feat(pairing): Accept/Dismiss component module for server prompts

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Full verification + format gate

- [ ] **Step 7.1: Full build and test run**

```bash
dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx --no-build
```

Expected: all projects build; full suite green (baseline before this feature: 947 passed + 1 skipped; this plan adds ~16 tests and rewrites 3).

- [ ] **Step 7.2: Format gate**

```bash
dotnet tool restore
dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --no-build --verbosity=ERROR
git status --porcelain
```

Expected: `git status --porcelain` prints nothing (no reformat diff). If files changed, review the diff, re-run the tests, and amend into a `style: apply ReformatAndReorder` commit:

```bash
git add -A ':!docs' && git commit -m "style: apply ReformatAndReorder

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 7.3: Done — hand back for review**

Do NOT push or open a PR — the user gates merges (and a live #setup smoke test is expected first, mirroring PRs #44/#46/#47).

---

## Manual smoke test (user-gated, after merge readiness)

1. Run the bot, `/setup` in a test guild, connect the account.
2. In-game: pair a **new** server → a "New server detected" prompt appears in #setup; **no channels are created and no connection starts**.
3. Press "Pair" again → no duplicate prompt.
4. Click **Accept** → prompt becomes "Server added", per-server category + 7 channels appear, bot connects.
5. Pair the same server again → silent (credential refresh), no prompt.
6. Pair another new server, click **Dismiss** → prompt deleted, nothing created.
7. Restart the bot with a prompt pending, click **Accept** → ephemeral "pairing expired" reply, prompt deleted.
```
