# Subsystem 1a — Discord Workspace Provisioning & Configuration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The bot owns and provisions its own Discord categories/channels/messages via a declarative,
self-healing reconciler — replacing the foundation's `/server` and `/bind` commands — with `/setup`
provisioning, a settings/i18n surface, and a per-server category created from a stub `ServerRegistered`
event (the real FCM trigger lands in 1b).

**Architecture:** A new `RustPlusBot.Features.Workspace` project holds a *registry* of `ChannelSpec` /
`MessageSpec` (contributed per subsystem via DI), an `IWorkspaceGateway` seam over Discord.Net, and a
`WorkspaceReconciler` that converges desired (registry) against actual (stored snowflakes + live
Discord) using a **resolve → adopt → create** algorithm under a per-guild lock. Persistence gains three
`Provisioned*` tables; the reconciler is fully unit-testable against an in-memory gateway fake.

**Tech Stack:** .NET 10, C# (nullable, implicit usings, `TreatWarningsAsErrors`, strict analyzers —
NetAnalyzers/Roslynator/Sonar), Discord.Net 3.20, EF Core 10 + Persistord over SQLite, xUnit + NSubstitute.

---

## Conventions every task follows

- **Library async code uses `.ConfigureAwait(false)`** (matches the codebase). Public types/members need
  `/// <summary>` XML docs (CS1591 is an error); keep implementation types `internal sealed` to avoid the
  doc burden — tests reach them via `InternalsVisibleTo` (set up in Task 1).
- **Per-task gate (every "run tests" + before every commit):** from the repo root run
  `dotnet build RustPlusBot.slnx` (0 warnings / 0 errors under the analyzers) then
  `dotnet test RustPlusBot.slnx`. A task is not done until both are green.
- **Commit** at the end of each task with the message shown. Branch is `feat/workspace-provisioning`
  (already created off `develop`).
- **Canonical type names** (used across tasks — do not rename): `ProvisionedCategory`,
  `ProvisionedChannel`, `ProvisionedMessage`, `IWorkspaceStore`, `WorkspaceStore`, `WorkspaceScope`,
  `ChannelPermissionProfile`, `ChannelSpec`, `MessageSpec`, `MessageRenderContext`, `MessagePayload`,
  `IChannelSpecProvider`, `IMessageSpecProvider`, `IMessageRenderer`, `IWorkspaceRegistry`,
  `WorkspaceRegistry`, `IWorkspaceGateway`, `DiscordWorkspaceGateway`, `IProvisioningLock`,
  `ProvisioningLock`, `IWorkspaceReconciler`, `WorkspaceReconciler`, `ReconcileResult`, `ReconcileStatus`,
  `IWorkspaceTeardownService`, `WorkspaceTeardownService`, `ILocalizer`, `Localizer`,
  `LocalizationCatalog`, `WorkspaceChannelKeys`, `WorkspaceMessageKeys`, `WorkspaceOptions`,
  `ServerRegisteredEvent`, `InteractionModuleAssembly`.

---

## Task 1: Scaffold the Workspace project and its test project

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/RustPlusBot.Features.Workspace.csproj`
- Create: `src/RustPlusBot.Features.Workspace/AssemblyInfo.cs`
- Create: `tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
- Create: `tests/RustPlusBot.Features.Workspace.Tests/Placeholder.cs`
- Modify: `RustPlusBot.slnx`

- [ ] **Step 1: Create the feature project file**

`src/RustPlusBot.Features.Workspace/RustPlusBot.Features.Workspace.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\RustPlusBot.Discord\RustPlusBot.Discord.csproj" />
    <ProjectReference Include="..\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
    <ProjectReference Include="..\RustPlusBot.Domain\RustPlusBot.Domain.csproj" />
    <ProjectReference Include="..\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Discord.Net" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Expose internals to the test project**

`src/RustPlusBot.Features.Workspace/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RustPlusBot.Features.Workspace.Tests")]
```

- [ ] **Step 3: Create the test project file**

`tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    <PackageReference Include="Microsoft.Extensions.Hosting" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\RustPlusBot.Features.Workspace\RustPlusBot.Features.Workspace.csproj" />
    <ProjectReference Include="..\..\src\RustPlusBot.Persistence\RustPlusBot.Persistence.csproj" />
    <ProjectReference Include="..\..\src\RustPlusBot.Domain\RustPlusBot.Domain.csproj" />
    <ProjectReference Include="..\..\src\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Add a placeholder test so the project compiles and runs**

`tests/RustPlusBot.Features.Workspace.Tests/Placeholder.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Tests;

public sealed class Placeholder
{
    [Fact]
    public void ProjectCompiles() => Assert.True(true);
}
```

- [ ] **Step 5: Register both projects in the solution**

In `RustPlusBot.slnx`, add to the `/src/` folder:

```xml
    <Project Path="src/RustPlusBot.Features.Workspace/RustPlusBot.Features.Workspace.csproj" />
```

and to the `/tests/` folder:

```xml
    <Project Path="tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj" />
```

- [ ] **Step 6: Build and test**

Run: `dotnet build RustPlusBot.slnx` then `dotnet test RustPlusBot.slnx`
Expected: build 0/0; all tests pass (including the new `ProjectCompiles`).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Workspace tests/RustPlusBot.Features.Workspace.Tests RustPlusBot.slnx
git commit -m "chore(workspace): scaffold Features.Workspace project and tests"
```

---

## Task 2: Add the provisioning domain entities

**Files:**

- Create: `src/RustPlusBot.Domain/Workspace/ProvisionedCategory.cs`
- Create: `src/RustPlusBot.Domain/Workspace/ProvisionedChannel.cs`
- Create: `src/RustPlusBot.Domain/Workspace/ProvisionedMessage.cs`

These are plain entities (no behavior); no unit test of their own — they are exercised by the store and
migration tests in Tasks 4–5.

- [ ] **Step 1: ProvisionedCategory**

`src/RustPlusBot.Domain/Workspace/ProvisionedCategory.cs`:

```csharp
namespace RustPlusBot.Domain.Workspace;

/// <summary>A Discord category the bot has provisioned. One per scope (global or per-server).</summary>
public sealed class ProvisionedCategory
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this category belongs to, or <c>null</c> for the global category.</summary>
    public Guid? RustServerId { get; set; }

    /// <summary>The provisioned Discord category snowflake.</summary>
    public ulong DiscordCategoryId { get; set; }

    /// <summary>When the record was first created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 2: ProvisionedChannel**

`src/RustPlusBot.Domain/Workspace/ProvisionedChannel.cs`:

```csharp
namespace RustPlusBot.Domain.Workspace;

/// <summary>A Discord text channel the bot has provisioned, identified by its stable spec key.</summary>
public sealed class ProvisionedChannel
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this channel belongs to, or <c>null</c> for a global channel.</summary>
    public Guid? RustServerId { get; set; }

    /// <summary>The stable spec key (e.g. "information", "setup", "settings", "info").</summary>
    public string ChannelKey { get; set; } = string.Empty;

    /// <summary>The provisioned Discord channel snowflake.</summary>
    public ulong DiscordChannelId { get; set; }

    /// <summary>When the record was first created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
```

- [ ] **Step 3: ProvisionedMessage**

`src/RustPlusBot.Domain/Workspace/ProvisionedMessage.cs`:

```csharp
namespace RustPlusBot.Domain.Workspace;

/// <summary>An anchored bot message, edited in place rather than re-posted.</summary>
public sealed class ProvisionedMessage
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The server this message belongs to, or <c>null</c> for a global message.</summary>
    public Guid? RustServerId { get; set; }

    /// <summary>The stable spec key (e.g. "information.main", "settings.main", "server.info").</summary>
    public string MessageKey { get; set; } = string.Empty;

    /// <summary>The channel the message lives in.</summary>
    public ulong DiscordChannelId { get; set; }

    /// <summary>The anchored message snowflake.</summary>
    public ulong DiscordMessageId { get; set; }

    /// <summary>When the record was first created (UTC).</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When the message was last edited in place (UTC).</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/RustPlusBot.Domain/RustPlusBot.Domain.csproj`
Expected: build 0/0.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Domain/Workspace
git commit -m "feat(domain): add ProvisionedCategory/Channel/Message entities"
```

---

## Task 3: Remove the irrelevant foundation pieces (`/server`, `/bind`, ChannelBinding)

This task deletes the now-irrelevant code. After it, `BotDbContext` still references `ChannelBindings`
in the model snapshot — that is fixed by the migration in Task 4, so the build at the end of *this* task
is expected to pass because we remove the configuration and DbSet here too.

**Files:**

- Delete: `src/RustPlusBot.Domain/Guilds/ChannelBinding.cs`
- Delete: `src/RustPlusBot.Domain/Guilds/BoundFeature.cs`
- Delete: `src/RustPlusBot.Persistence/Bindings/BindingService.cs`
- Delete: `src/RustPlusBot.Persistence/Bindings/IBindingService.cs`
- Delete: `src/RustPlusBot.Persistence/Configurations/ChannelBindingConfiguration.cs`
- Delete: `src/RustPlusBot.Discord/Modules/BindModule.cs`
- Delete: `src/RustPlusBot.Discord/Modules/ServerModule.cs`
- Delete: `tests/RustPlusBot.Persistence.Tests/Bindings/BindingServiceTests.cs`
- Modify: `src/RustPlusBot.Persistence/BotDbContext.cs`
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`

- [ ] **Step 1: Delete the files**

```bash
git rm src/RustPlusBot.Domain/Guilds/ChannelBinding.cs \
       src/RustPlusBot.Domain/Guilds/BoundFeature.cs \
       src/RustPlusBot.Persistence/Bindings/BindingService.cs \
       src/RustPlusBot.Persistence/Bindings/IBindingService.cs \
       src/RustPlusBot.Persistence/Configurations/ChannelBindingConfiguration.cs \
       src/RustPlusBot.Discord/Modules/BindModule.cs \
       src/RustPlusBot.Discord/Modules/ServerModule.cs \
       tests/RustPlusBot.Persistence.Tests/Bindings/BindingServiceTests.cs
```

- [ ] **Step 2: Remove ChannelBinding from `BotDbContext`**

In `src/RustPlusBot.Persistence/BotDbContext.cs`, delete the `using RustPlusBot.Domain.Guilds;` line only
if no other type from that namespace is used (note: `GuildSettings` is also in `RustPlusBot.Domain.Guilds`,
so **keep** the using). Delete the `ChannelBindings` DbSet property and its doc comment:

```csharp
    /// <summary>Channel-to-feature bindings.</summary>
    public DbSet<ChannelBinding> ChannelBindings => Set<ChannelBinding>();
```

and remove the `.ApplyConfiguration(new ChannelBindingConfiguration())` line from `OnModelCreating`.

- [ ] **Step 3: Remove the binding service registration**

In `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`, delete the
`using RustPlusBot.Persistence.Bindings;` line and the registration line
`services.AddScoped<IBindingService, BindingService>();`.

- [ ] **Step 4: Build and test**

Run: `dotnet build RustPlusBot.slnx` then `dotnet test RustPlusBot.slnx`
Expected: build 0/0. Tests pass. (The `BotDbContextModelSnapshot` still lists `ChannelBindings`, but EF
only compares it during migration generation, not at build/test time, so this is green now and corrected
in Task 4.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: remove /server, /bind, and ChannelBinding (superseded by workspace provisioning)"
```

---

## Task 4: Provisioning EF configurations, DbSets, and migration

**Files:**

- Create: `src/RustPlusBot.Persistence/Configurations/ProvisionedCategoryConfiguration.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/ProvisionedChannelConfiguration.cs`
- Create: `src/RustPlusBot.Persistence/Configurations/ProvisionedMessageConfiguration.cs`
- Modify: `src/RustPlusBot.Persistence/BotDbContext.cs`
- Create (generated): `src/RustPlusBot.Persistence/Migrations/<timestamp>_WorkspaceProvisioning.cs` (+ Designer)
- Test: `tests/RustPlusBot.Persistence.Tests/Workspace/ProvisioningSchemaTests.cs`

- [ ] **Step 1: Write the failing schema test**

`tests/RustPlusBot.Persistence.Tests/Workspace/ProvisioningSchemaTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Tests.Workspace;

public sealed class ProvisioningSchemaTests
{
    [Fact]
    public async Task RemovingServer_CascadeDeletesItsProvisioningRows()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        var server = new RustServer { GuildId = 1UL, Name = "S", Ip = "1.1.1.1", Port = 1 };
        context.RustServers.Add(server);
        context.ProvisionedCategories.Add(new ProvisionedCategory
        {
            GuildId = 1UL, RustServerId = server.Id, DiscordCategoryId = 100UL, CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        context.RustServers.Remove(server);
        await context.SaveChangesAsync();

        Assert.Empty(await context.ProvisionedCategories.ToListAsync());
    }

    [Fact]
    public async Task GlobalCategory_HasNullServerId_AndPersists()
    {
        var (context, connection) = SqliteContextFixture.Create();
        await using var _ = context;
        await using var __ = connection;

        context.ProvisionedCategories.Add(new ProvisionedCategory
        {
            GuildId = 1UL, RustServerId = null, DiscordCategoryId = 200UL, CreatedAt = DateTimeOffset.UnixEpoch,
        });
        await context.SaveChangesAsync();

        var loaded = await context.ProvisionedCategories.SingleAsync();
        Assert.Null(loaded.RustServerId);
        Assert.Equal(200UL, loaded.DiscordCategoryId);
    }
}
```

- [ ] **Step 2: Run it to verify it fails**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj`
Expected: FAIL to compile — `ProvisionedCategories` is not a member of `BotDbContext`.

- [ ] **Step 3: Add the EF configurations**

`src/RustPlusBot.Persistence/Configurations/ProvisionedCategoryConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ProvisionedCategoryConfiguration : IEntityTypeConfiguration<ProvisionedCategory>
{
    public void Configure(EntityTypeBuilder<ProvisionedCategory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.GuildId, c.RustServerId }).IsUnique();
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(c => c.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`src/RustPlusBot.Persistence/Configurations/ProvisionedChannelConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ProvisionedChannelConfiguration : IEntityTypeConfiguration<ProvisionedChannel>
{
    public void Configure(EntityTypeBuilder<ProvisionedChannel> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ChannelKey).IsRequired().HasMaxLength(64);
        builder.HasIndex(c => new { c.GuildId, c.RustServerId, c.ChannelKey }).IsUnique();
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(c => c.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

`src/RustPlusBot.Persistence/Configurations/ProvisionedMessageConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Configurations;

internal sealed class ProvisionedMessageConfiguration : IEntityTypeConfiguration<ProvisionedMessage>
{
    public void Configure(EntityTypeBuilder<ProvisionedMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.HasKey(m => m.Id);
        builder.Property(m => m.MessageKey).IsRequired().HasMaxLength(64);
        builder.HasIndex(m => new { m.GuildId, m.RustServerId, m.MessageKey }).IsUnique();
        builder.HasOne<RustServer>()
            .WithMany()
            .HasForeignKey(m => m.RustServerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Wire DbSets + configurations into `BotDbContext`**

In `src/RustPlusBot.Persistence/BotDbContext.cs` add `using RustPlusBot.Domain.Workspace;`, add the three
DbSets after `EventSubscriptions`:

```csharp
    /// <summary>Provisioned Discord categories (global + per-server).</summary>
    public DbSet<ProvisionedCategory> ProvisionedCategories => Set<ProvisionedCategory>();

    /// <summary>Provisioned Discord channels, keyed by spec key.</summary>
    public DbSet<ProvisionedChannel> ProvisionedChannels => Set<ProvisionedChannel>();

    /// <summary>Anchored bot messages, edited in place.</summary>
    public DbSet<ProvisionedMessage> ProvisionedMessages => Set<ProvisionedMessage>();
```

and append to the `OnModelCreating` chain:

```csharp
            .ApplyConfiguration(new ProvisionedCategoryConfiguration())
            .ApplyConfiguration(new ProvisionedChannelConfiguration())
            .ApplyConfiguration(new ProvisionedMessageConfiguration());
```

- [ ] **Step 5: Generate the migration**

Run from the repo root:

```bash
dotnet ef migrations add WorkspaceProvisioning \
  --project src/RustPlusBot.Persistence \
  --startup-project src/RustPlusBot.Host
```

Expected: creates `Migrations/<timestamp>_WorkspaceProvisioning.cs` (+ Designer) and updates
`BotDbContextModelSnapshot.cs`. Open the generated `Up` and confirm it **drops** `ChannelBindings` and
**creates** `ProvisionedCategories`, `ProvisionedChannels`, `ProvisionedMessages` with the unique indexes
and cascade FKs. If `dotnet ef` is not installed, run `dotnet tool restore` first (the repo has a
`.config/dotnet-tools.json`).

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj`
Expected: PASS — both new schema tests green; existing persistence tests still green (migrations apply
cleanly in `SqliteContextFixture`).

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build RustPlusBot.slnx`
Expected: 0/0.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat(persistence): provisioning schema + WorkspaceProvisioning migration"
```

---

## Task 5: `IWorkspaceStore` + `WorkspaceStore` (provisioning persistence)

**Files:**

- Create: `src/RustPlusBot.Persistence/Workspace/IWorkspaceStore.cs`
- Create: `src/RustPlusBot.Persistence/Workspace/WorkspaceStore.cs`
- Modify: `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Persistence.Tests/Workspace/WorkspaceStoreTests.cs`

The store does read-modify-write upserts keyed by scope/spec-key (mirroring the old `BindingService`
upsert), sets `CreatedAt`/`UpdatedAt` from `IClock`, and exposes the queries teardown/startup need.

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Persistence.Tests/Workspace/WorkspaceStoreTests.cs`:

```csharp
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Persistence.Tests.Workspace;

public sealed class WorkspaceStoreTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static WorkspaceStore NewStore(out BotDbContext context, out IDisposable cleanup)
    {
        var (ctx, connection) = SqliteContextFixture.Create();
        context = ctx;
        cleanup = connection;
        return new WorkspaceStore(ctx, new FixedClock(DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public async Task SaveCategory_IsUpsertByScope()
    {
        var store = NewStore(out _, out var cleanup);
        using var _ = cleanup;

        await store.SaveCategoryAsync(new ProvisionedCategory { GuildId = 1, RustServerId = null, DiscordCategoryId = 10 });
        await store.SaveCategoryAsync(new ProvisionedCategory { GuildId = 1, RustServerId = null, DiscordCategoryId = 20 });

        var loaded = await store.GetCategoryAsync(1, null);
        Assert.NotNull(loaded);
        Assert.Equal(20UL, loaded!.DiscordCategoryId);
    }

    [Fact]
    public async Task SaveChannel_UpsertByKey_AndScopeIsolated()
    {
        var store = NewStore(out _, out var cleanup);
        using var _ = cleanup;

        await store.SaveChannelAsync(new ProvisionedChannel { GuildId = 1, RustServerId = null, ChannelKey = "information", DiscordChannelId = 5 });
        await store.SaveChannelAsync(new ProvisionedChannel { GuildId = 1, RustServerId = null, ChannelKey = "information", DiscordChannelId = 6 });
        await store.SaveChannelAsync(new ProvisionedChannel { GuildId = 2, RustServerId = null, ChannelKey = "information", DiscordChannelId = 7 });

        var g1 = await store.GetChannelsAsync(1, null);
        Assert.Single(g1);
        Assert.Equal(6UL, g1[0].DiscordChannelId);
        var g2 = await store.GetChannelsAsync(2, null);
        Assert.Single(g2);
        Assert.Equal(7UL, g2[0].DiscordChannelId);
    }

    [Fact]
    public async Task SaveMessage_UpsertByKey_SetsTimestamps()
    {
        var store = NewStore(out _, out var cleanup);
        using var _ = cleanup;

        await store.SaveMessageAsync(new ProvisionedMessage { GuildId = 1, MessageKey = "information.main", DiscordChannelId = 5, DiscordMessageId = 100 });
        await store.SaveMessageAsync(new ProvisionedMessage { GuildId = 1, MessageKey = "information.main", DiscordChannelId = 5, DiscordMessageId = 101 });

        var loaded = await store.GetMessageAsync(1, null, "information.main");
        Assert.NotNull(loaded);
        Assert.Equal(101UL, loaded!.DiscordMessageId);
        Assert.Equal(DateTimeOffset.UnixEpoch, loaded.UpdatedAt);
    }

    [Fact]
    public async Task Culture_DefaultsToEn_AndRoundTrips()
    {
        var store = NewStore(out _, out var cleanup);
        using var _ = cleanup;

        Assert.Equal("en", await store.GetCultureAsync(1));
        await store.SetCultureAsync(1, "fr");
        Assert.Equal("fr", await store.GetCultureAsync(1));
    }

    [Fact]
    public async Task DeleteScope_RemovesOnlyThatScope()
    {
        var store = NewStore(out _, out var cleanup);
        using var _ = cleanup;
        var serverId = Guid.NewGuid();

        await store.SaveCategoryAsync(new ProvisionedCategory { GuildId = 1, RustServerId = null, DiscordCategoryId = 10 });
        await store.SaveChannelAsync(new ProvisionedChannel { GuildId = 1, RustServerId = null, ChannelKey = "information", DiscordChannelId = 5 });
        await store.SaveCategoryAsync(new ProvisionedCategory { GuildId = 1, RustServerId = serverId, DiscordCategoryId = 11 });

        await store.DeleteScopeAsync(1, null);

        Assert.Null(await store.GetCategoryAsync(1, null));
        Assert.Empty(await store.GetChannelsAsync(1, null));
        Assert.NotNull(await store.GetCategoryAsync(1, serverId));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj`
Expected: FAIL to compile — `WorkspaceStore` / `IWorkspaceStore` do not exist.

- [ ] **Step 3: Define the interface**

`src/RustPlusBot.Persistence/Workspace/IWorkspaceStore.cs`:

```csharp
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Workspace;

/// <summary>Persistence for the bot's provisioned categories, channels, and anchored messages.</summary>
public interface IWorkspaceStore
{
    /// <summary>Gets the category for a scope (<paramref name="serverId"/> null = global), or null.</summary>
    Task<ProvisionedCategory?> GetCategoryAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default);

    /// <summary>Upserts the category for its scope (keyed by guild + server).</summary>
    Task SaveCategoryAsync(ProvisionedCategory category, CancellationToken cancellationToken = default);

    /// <summary>Gets all provisioned channels for a scope.</summary>
    Task<IReadOnlyList<ProvisionedChannel>> GetChannelsAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default);

    /// <summary>Upserts a channel for its scope (keyed by guild + server + channel key).</summary>
    Task SaveChannelAsync(ProvisionedChannel channel, CancellationToken cancellationToken = default);

    /// <summary>Gets the anchored message for a scope + message key, or null.</summary>
    Task<ProvisionedMessage?> GetMessageAsync(ulong guildId, Guid? serverId, string messageKey, CancellationToken cancellationToken = default);

    /// <summary>Upserts an anchored message (keyed by guild + server + message key).</summary>
    Task SaveMessageAsync(ProvisionedMessage message, CancellationToken cancellationToken = default);

    /// <summary>Gets a guild's BCP-47 culture, defaulting to "en".</summary>
    Task<string> GetCultureAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Sets a guild's culture (upserting the GuildSettings row).</summary>
    Task SetCultureAsync(ulong guildId, string culture, CancellationToken cancellationToken = default);

    /// <summary>Deletes all provisioning rows (category + channels + messages) for one scope.</summary>
    Task DeleteScopeAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default);

    /// <summary>Gets every provisioned category in a guild (all scopes).</summary>
    Task<IReadOnlyList<ProvisionedCategory>> GetAllCategoriesAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Gets the distinct guild ids that have any provisioned category (for startup reconcile).</summary>
    Task<IReadOnlyList<ulong>> GetProvisionedGuildIdsAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement the store**

`src/RustPlusBot.Persistence/Workspace/WorkspaceStore.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Workspace;

namespace RustPlusBot.Persistence.Workspace;

/// <summary>EF Core implementation of <see cref="IWorkspaceStore"/> over <see cref="BotDbContext"/>.</summary>
/// <param name="context">The bot database context.</param>
/// <param name="clock">The clock used for create/update timestamps.</param>
public sealed class WorkspaceStore(BotDbContext context, IClock clock) : IWorkspaceStore
{
    /// <inheritdoc />
    public Task<ProvisionedCategory?> GetCategoryAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default) =>
        context.ProvisionedCategories
            .SingleOrDefaultAsync(c => c.GuildId == guildId && c.RustServerId == serverId, cancellationToken);

    /// <inheritdoc />
    public async Task SaveCategoryAsync(ProvisionedCategory category, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(category);
        var existing = await context.ProvisionedCategories
            .SingleOrDefaultAsync(c => c.GuildId == category.GuildId && c.RustServerId == category.RustServerId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            category.CreatedAt = clock.UtcNow;
            context.ProvisionedCategories.Add(category);
        }
        else
        {
            existing.DiscordCategoryId = category.DiscordCategoryId;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProvisionedChannel>> GetChannelsAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default) =>
        await context.ProvisionedChannels
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task SaveChannelAsync(ProvisionedChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        var existing = await context.ProvisionedChannels
            .SingleOrDefaultAsync(
                c => c.GuildId == channel.GuildId && c.RustServerId == channel.RustServerId && c.ChannelKey == channel.ChannelKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            channel.CreatedAt = clock.UtcNow;
            context.ProvisionedChannels.Add(channel);
        }
        else
        {
            existing.DiscordChannelId = channel.DiscordChannelId;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ProvisionedMessage?> GetMessageAsync(ulong guildId, Guid? serverId, string messageKey, CancellationToken cancellationToken = default) =>
        context.ProvisionedMessages
            .SingleOrDefaultAsync(m => m.GuildId == guildId && m.RustServerId == serverId && m.MessageKey == messageKey, cancellationToken);

    /// <inheritdoc />
    public async Task SaveMessageAsync(ProvisionedMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        var existing = await context.ProvisionedMessages
            .SingleOrDefaultAsync(
                m => m.GuildId == message.GuildId && m.RustServerId == message.RustServerId && m.MessageKey == message.MessageKey,
                cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            message.CreatedAt = clock.UtcNow;
            message.UpdatedAt = clock.UtcNow;
            context.ProvisionedMessages.Add(message);
        }
        else
        {
            existing.DiscordChannelId = message.DiscordChannelId;
            existing.DiscordMessageId = message.DiscordMessageId;
            existing.UpdatedAt = clock.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> GetCultureAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var settings = await context.GuildSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);
        return settings?.Culture ?? "en";
    }

    /// <inheritdoc />
    public async Task SetCultureAsync(ulong guildId, string culture, CancellationToken cancellationToken = default)
    {
        var settings = await context.GuildSettings
            .SingleOrDefaultAsync(s => s.GuildId == guildId, cancellationToken)
            .ConfigureAwait(false);

        if (settings is null)
        {
            context.GuildSettings.Add(new GuildSettings { GuildId = guildId, Culture = culture });
        }
        else
        {
            settings.Culture = culture;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteScopeAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default)
    {
        await context.ProvisionedMessages
            .Where(m => m.GuildId == guildId && m.RustServerId == serverId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.ProvisionedChannels
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
        await context.ProvisionedCategories
            .Where(c => c.GuildId == guildId && c.RustServerId == serverId)
            .ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProvisionedCategory>> GetAllCategoriesAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        await context.ProvisionedCategories
            .Where(c => c.GuildId == guildId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ulong>> GetProvisionedGuildIdsAsync(CancellationToken cancellationToken = default) =>
        await context.ProvisionedCategories
            .Select(c => c.GuildId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
}
```

- [ ] **Step 5: Register the store**

In `src/RustPlusBot.Persistence/PersistenceServiceCollectionExtensions.cs` add
`using RustPlusBot.Persistence.Workspace;` and, alongside the other `AddScoped` calls:

```csharp
        services.AddScoped<IWorkspaceStore, WorkspaceStore>();
```

- [ ] **Step 6: Run tests + build**

Run: `dotnet test tests/RustPlusBot.Persistence.Tests/RustPlusBot.Persistence.Tests.csproj` then
`dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0. (`ExecuteDeleteAsync` runs on SQLite.)

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(persistence): IWorkspaceStore + WorkspaceStore upsert/teardown queries"
```

---

## Task 6: Localization seam (`ILocalizer`, `Localizer`, `LocalizationCatalog`)

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Localization/ILocalizer.cs`
- Create: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`
- Create: `src/RustPlusBot.Features.Workspace/Localization/Localizer.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Localization/LocalizerTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Workspace.Tests/Localization/LocalizerTests.cs`:

```csharp
using RustPlusBot.Features.Workspace.Localization;

namespace RustPlusBot.Features.Workspace.Tests.Localization;

public sealed class LocalizerTests
{
    private static Localizer NewLocalizer() => new(LocalizationCatalog.Default);

    [Fact]
    public void Get_ReturnsCultureSpecificValue()
    {
        var localizer = NewLocalizer();
        Assert.Equal("information", localizer.Get("channel.information.name", "en"));
        Assert.Equal("informations", localizer.Get("channel.information.name", "fr"));
    }

    [Fact]
    public void Get_FallsBackToEnglish_WhenCultureMissing()
    {
        var localizer = NewLocalizer();
        Assert.Equal("information", localizer.Get("channel.information.name", "de"));
    }

    [Fact]
    public void Get_ReturnsKey_WhenMissingEverywhere()
    {
        var localizer = NewLocalizer();
        Assert.Equal("nope.key", localizer.Get("nope.key", "en"));
    }

    [Fact]
    public void Get_NormalizesRegionVariants()
    {
        var localizer = NewLocalizer();
        Assert.Equal("information", localizer.Get("channel.information.name", "en-US"));
    }

    [Fact]
    public void Get_WithArgs_Formats()
    {
        var localizer = NewLocalizer();
        // information.servers expects one numeric arg
        Assert.Contains("3", localizer.Get("information.servers", "en", 3), StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile.

- [ ] **Step 3: Define the interface**

`src/RustPlusBot.Features.Workspace/Localization/ILocalizer.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Localization;

/// <summary>Resolves localized strings by key and BCP-47 culture, falling back to English.</summary>
internal interface ILocalizer
{
    /// <summary>Gets the localized string for a key, or the key itself if not found.</summary>
    string Get(string key, string culture);

    /// <summary>Gets the localized, <see cref="string.Format(IFormatProvider, string, object?[])"/>-applied string.</summary>
    string Get(string key, string culture, params object[] args);
}
```

- [ ] **Step 4: Add the catalog**

`src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Localization;

/// <summary>The in-memory string catalog: culture -> (key -> value). English is the fallback.</summary>
internal sealed class LocalizationCatalog
{
    /// <summary>culture -> key -> value.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings { get; init; }

    /// <summary>The built-in EN/FR catalog.</summary>
    public static LocalizationCatalog Default { get; } = new()
    {
        Strings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category.global.name"] = "RustPlusBot",
                ["channel.information.name"] = "information",
                ["channel.setup.name"] = "setup",
                ["channel.settings.name"] = "settings",
                ["channel.info.name"] = "info",
                ["information.title"] = "RustPlusBot",
                ["information.body"] = "Connect your Rust+ account in #setup, then pair a server in-game to begin.",
                ["information.servers"] = "Servers registered: {0}",
                ["setup.title"] = "Connect your Rust+ account",
                ["setup.body"] = "Account connection arrives in the next update. Once connected, pair a server in-game and its channels appear automatically.",
                ["settings.title"] = "Settings",
                ["settings.body"] = "Configure the bot for this server.",
                ["settings.language.label"] = "Language",
                ["server.info.title"] = "{0}",
                ["server.info.endpoint"] = "Endpoint: {0}:{1}",
            },
            ["fr"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category.global.name"] = "RustPlusBot",
                ["channel.information.name"] = "informations",
                ["channel.setup.name"] = "configuration",
                ["channel.settings.name"] = "parametres",
                ["channel.info.name"] = "info",
                ["information.title"] = "RustPlusBot",
                ["information.body"] = "Connectez votre compte Rust+ dans #configuration, puis appairez un serveur en jeu.",
                ["information.servers"] = "Serveurs enregistres : {0}",
                ["setup.title"] = "Connectez votre compte Rust+",
                ["setup.body"] = "La connexion de compte arrive dans la prochaine mise a jour.",
                ["settings.title"] = "Parametres",
                ["settings.body"] = "Configurez le bot pour ce serveur.",
                ["settings.language.label"] = "Langue",
                ["server.info.title"] = "{0}",
                ["server.info.endpoint"] = "Adresse : {0}:{1}",
            },
        },
    };
}
```

- [ ] **Step 5: Implement the localizer**

`src/RustPlusBot.Features.Workspace/Localization/Localizer.cs`:

```csharp
using System.Globalization;

namespace RustPlusBot.Features.Workspace.Localization;

/// <summary>Dictionary-backed <see cref="ILocalizer"/> with English fallback and region normalization.</summary>
/// <param name="catalog">The string catalog.</param>
internal sealed class Localizer(LocalizationCatalog catalog) : ILocalizer
{
    private const string FallbackCulture = "en";

    /// <inheritdoc />
    public string Get(string key, string culture)
    {
        var normalized = Normalize(culture);
        if (catalog.Strings.TryGetValue(normalized, out var map) && map.TryGetValue(key, out var value))
        {
            return value;
        }

        if (catalog.Strings.TryGetValue(FallbackCulture, out var fallback) && fallback.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    /// <inheritdoc />
    public string Get(string key, string culture, params object[] args)
    {
        var format = Get(key, culture);
        var provider = ResolveFormatProvider(Normalize(culture));
        return string.Format(provider, format, args);
    }

    private static string Normalize(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return FallbackCulture;
        }

        var dash = culture.IndexOf('-', StringComparison.Ordinal);
        var primary = dash >= 0 ? culture[..dash] : culture;
        return primary.ToLowerInvariant();
    }

    private static IFormatProvider ResolveFormatProvider(string culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
```

- [ ] **Step 6: Run tests + build**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
then `dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(workspace): i18n localization seam with EN/FR catalog"
```

---

## Task 7: Registry types and `WorkspaceRegistry`

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Registry/WorkspaceScope.cs`
- Create: `src/RustPlusBot.Features.Workspace/Registry/ChannelPermissionProfile.cs`
- Create: `src/RustPlusBot.Features.Workspace/Registry/ChannelSpec.cs`
- Create: `src/RustPlusBot.Features.Workspace/Registry/MessageSpec.cs`
- Create: `src/RustPlusBot.Features.Workspace/Registry/MessageRenderContext.cs`
- Create: `src/RustPlusBot.Features.Workspace/Registry/IChannelSpecProvider.cs`
- Create: `src/RustPlusBot.Features.Workspace/Registry/IMessageSpecProvider.cs`
- Create: `src/RustPlusBot.Features.Workspace/Registry/IMessageRenderer.cs`
- Create: `src/RustPlusBot.Features.Workspace/Registry/IWorkspaceRegistry.cs`
- Create: `src/RustPlusBot.Features.Workspace/Registry/WorkspaceRegistry.cs`
- Create: `src/RustPlusBot.Features.Workspace/Gateway/MessagePayload.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Registry/WorkspaceRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/RustPlusBot.Features.Workspace.Tests/Registry/WorkspaceRegistryTests.cs`:

```csharp
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Registry;

public sealed class WorkspaceRegistryTests
{
    private sealed class ProviderA : IChannelSpecProvider
    {
        public IEnumerable<ChannelSpec> GetChannelSpecs() =>
        [
            new(WorkspaceScope.Global, "b", "b.name", ChannelPermissionProfile.ReadOnly, 1),
            new(WorkspaceScope.Global, "a", "a.name", ChannelPermissionProfile.ReadOnly, 0),
        ];
    }

    private sealed class ProviderB : IChannelSpecProvider
    {
        public IEnumerable<ChannelSpec> GetChannelSpecs() =>
        [
            new(WorkspaceScope.PerServer, "info", "info.name", ChannelPermissionProfile.ReadOnly, 0),
        ];
    }

    [Fact]
    public void ChannelSpecs_AggregateAcrossProviders_OrderedByOrder()
    {
        var registry = new WorkspaceRegistry([new ProviderA(), new ProviderB()], []);

        var global = registry.GetChannelSpecs(WorkspaceScope.Global);
        Assert.Equal(["a", "b"], global.Select(s => s.Key));

        var perServer = registry.GetChannelSpecs(WorkspaceScope.PerServer);
        Assert.Equal(["info"], perServer.Select(s => s.Key));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile.

- [ ] **Step 3: Add the enums and records**

`src/RustPlusBot.Features.Workspace/Registry/WorkspaceScope.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Which desired-state scope a spec belongs to.</summary>
internal enum WorkspaceScope
{
    /// <summary>The single global RustPlusBot category per guild.</summary>
    Global = 0,

    /// <summary>A per-registered-server category.</summary>
    PerServer = 1,
}
```

`src/RustPlusBot.Features.Workspace/Registry/ChannelPermissionProfile.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>The permission shape applied to a provisioned channel.</summary>
internal enum ChannelPermissionProfile
{
    /// <summary>Members can view but not send; the bot manages content.</summary>
    ReadOnly = 0,

    /// <summary>Members can send (reserved for later chat/command channels).</summary>
    Interactive = 1,
}
```

`src/RustPlusBot.Features.Workspace/Registry/ChannelSpec.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Declarative description of a channel the bot should keep provisioned.</summary>
/// <param name="Scope">Global or per-server.</param>
/// <param name="Key">Stable key (persisted as ChannelKey).</param>
/// <param name="NameKey">i18n key resolving to the channel name.</param>
/// <param name="Permissions">Permission profile to apply.</param>
/// <param name="Order">Sort order within the category.</param>
internal sealed record ChannelSpec(
    WorkspaceScope Scope,
    string Key,
    string NameKey,
    ChannelPermissionProfile Permissions,
    int Order);
```

`src/RustPlusBot.Features.Workspace/Registry/MessageSpec.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Declarative description of an anchored message the bot keeps in a channel.</summary>
/// <param name="Scope">Global or per-server.</param>
/// <param name="Key">Stable key (persisted as MessageKey); also the renderer lookup key.</param>
/// <param name="ChannelKey">The <see cref="ChannelSpec.Key"/> of the channel it lives in.</param>
internal sealed record MessageSpec(WorkspaceScope Scope, string Key, string ChannelKey);
```

`src/RustPlusBot.Features.Workspace/Registry/MessageRenderContext.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Context handed to a renderer to build a message payload.</summary>
/// <param name="GuildId">The guild being rendered for.</param>
/// <param name="ServerId">The server scope, or null for global messages.</param>
/// <param name="Culture">The guild's BCP-47 culture.</param>
internal sealed record MessageRenderContext(ulong GuildId, Guid? ServerId, string Culture);
```

`src/RustPlusBot.Features.Workspace/Gateway/MessagePayload.cs`:

```csharp
using Discord;

namespace RustPlusBot.Features.Workspace.Gateway;

/// <summary>A renderable message: optional text, embed, and components.</summary>
/// <param name="Text">Plain content, or null.</param>
/// <param name="Embed">An embed, or null.</param>
/// <param name="Components">Message components (buttons/selects), or null.</param>
internal sealed record MessagePayload(string? Text, Embed? Embed, MessageComponent? Components);
```

- [ ] **Step 4: Add the provider/renderer/registry contracts**

`src/RustPlusBot.Features.Workspace/Registry/IChannelSpecProvider.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>A subsystem's contribution of channel specs. Implementations are aggregated via DI.</summary>
internal interface IChannelSpecProvider
{
    /// <summary>The channel specs this subsystem contributes.</summary>
    IEnumerable<ChannelSpec> GetChannelSpecs();
}
```

`src/RustPlusBot.Features.Workspace/Registry/IMessageSpecProvider.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>A subsystem's contribution of message specs. Implementations are aggregated via DI.</summary>
internal interface IMessageSpecProvider
{
    /// <summary>The message specs this subsystem contributes.</summary>
    IEnumerable<MessageSpec> GetMessageSpecs();
}
```

`src/RustPlusBot.Features.Workspace/Registry/IMessageRenderer.cs`:

```csharp
using RustPlusBot.Features.Workspace.Gateway;

namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Renders the payload for one message key. Looked up by <see cref="MessageKey"/>.</summary>
internal interface IMessageRenderer
{
    /// <summary>The message key this renderer produces (matches <see cref="MessageSpec.Key"/>).</summary>
    string MessageKey { get; }

    /// <summary>Builds the current payload for the given context.</summary>
    ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken);
}
```

`src/RustPlusBot.Features.Workspace/Registry/IWorkspaceRegistry.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Aggregated, ordered view of all contributed channel/message specs.</summary>
internal interface IWorkspaceRegistry
{
    /// <summary>Channel specs for a scope, ordered by <see cref="ChannelSpec.Order"/>.</summary>
    IReadOnlyList<ChannelSpec> GetChannelSpecs(WorkspaceScope scope);

    /// <summary>Message specs for a scope.</summary>
    IReadOnlyList<MessageSpec> GetMessageSpecs(WorkspaceScope scope);
}
```

`src/RustPlusBot.Features.Workspace/Registry/WorkspaceRegistry.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Registry;

/// <summary>Aggregates contributed spec providers into ordered, scope-filtered views.</summary>
/// <param name="channelProviders">All channel spec providers.</param>
/// <param name="messageProviders">All message spec providers.</param>
internal sealed class WorkspaceRegistry(
    IEnumerable<IChannelSpecProvider> channelProviders,
    IEnumerable<IMessageSpecProvider> messageProviders) : IWorkspaceRegistry
{
    private readonly List<ChannelSpec> _channels = channelProviders.SelectMany(p => p.GetChannelSpecs()).ToList();
    private readonly List<MessageSpec> _messages = messageProviders.SelectMany(p => p.GetMessageSpecs()).ToList();

    /// <inheritdoc />
    public IReadOnlyList<ChannelSpec> GetChannelSpecs(WorkspaceScope scope) =>
        _channels.Where(s => s.Scope == scope).OrderBy(s => s.Order).ThenBy(s => s.Key, StringComparer.Ordinal).ToList();

    /// <inheritdoc />
    public IReadOnlyList<MessageSpec> GetMessageSpecs(WorkspaceScope scope) =>
        _messages.Where(s => s.Scope == scope).ToList();
}
```

- [ ] **Step 5: Run tests + build**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
then `dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(workspace): spec registry (channel/message specs, providers, renderer seam)"
```

---

## Task 8: `IWorkspaceGateway` seam + in-memory fake

The fake lives in the test project and is the test double every reconciler test uses. No production
Discord impl yet (that is Task 19); this task defines the contract and the fake.

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Gateway/IWorkspaceGateway.cs`
- Create: `tests/RustPlusBot.Features.Workspace.Tests/Fakes/FakeWorkspaceGateway.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Fakes/FakeWorkspaceGatewayTests.cs`

- [ ] **Step 1: Define the gateway interface**

`src/RustPlusBot.Features.Workspace/Gateway/IWorkspaceGateway.cs`:

```csharp
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Gateway;

/// <summary>Primitive Discord operations the reconciler orchestrates. Implemented over Discord.Net
/// in production and by an in-memory fake in tests. Existence checks are cache reads (sync);
/// message existence and mutations are async REST calls.</summary>
internal interface IWorkspaceGateway
{
    /// <summary>True if a category with this snowflake currently exists in the guild.</summary>
    bool CategoryExists(ulong guildId, ulong categoryId);

    /// <summary>Finds a category by name, or null.</summary>
    Task<ulong?> FindCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken);

    /// <summary>Creates a category and returns its snowflake.</summary>
    Task<ulong> CreateCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken);

    /// <summary>True if a text channel with this snowflake currently exists in the guild.</summary>
    bool ChannelExists(ulong guildId, ulong channelId);

    /// <summary>Finds a text channel by name under a category, or null.</summary>
    Task<ulong?> FindChannelAsync(ulong guildId, ulong categoryId, string name, CancellationToken cancellationToken);

    /// <summary>Creates a text channel under a category with the given profile; returns its snowflake.</summary>
    Task<ulong> CreateChannelAsync(ulong guildId, ulong categoryId, string name, ChannelPermissionProfile profile, CancellationToken cancellationToken);

    /// <summary>Re-applies parent + name + permission profile to an existing channel (adopt/heal path).</summary>
    Task ApplyChannelSettingsAsync(ulong guildId, ulong channelId, ulong categoryId, string name, ChannelPermissionProfile profile, CancellationToken cancellationToken);

    /// <summary>True if the message still exists in the channel.</summary>
    Task<bool> MessageExistsAsync(ulong guildId, ulong channelId, ulong messageId, CancellationToken cancellationToken);

    /// <summary>Posts a new message and returns its snowflake.</summary>
    Task<ulong> PostMessageAsync(ulong guildId, ulong channelId, MessagePayload payload, CancellationToken cancellationToken);

    /// <summary>Edits an existing message in place.</summary>
    Task EditMessageAsync(ulong guildId, ulong channelId, ulong messageId, MessagePayload payload, CancellationToken cancellationToken);

    /// <summary>Deletes a channel by snowflake (no-op if already gone).</summary>
    Task DeleteChannelAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken);

    /// <summary>Deletes a category by snowflake (no-op if already gone).</summary>
    Task DeleteCategoryAsync(ulong guildId, ulong categoryId, CancellationToken cancellationToken);

    /// <summary>Returns the names of required bot guild permissions that are missing (empty = all present).</summary>
    IReadOnlyList<string> GetMissingBotPermissions(ulong guildId);
}
```

- [ ] **Step 2: Implement the in-memory fake**

`tests/RustPlusBot.Features.Workspace.Tests/Fakes/FakeWorkspaceGateway.cs`:

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Fakes;

/// <summary>Deterministic in-memory <see cref="IWorkspaceGateway"/> for reconciler tests.</summary>
internal sealed class FakeWorkspaceGateway : IWorkspaceGateway
{
    private sealed record Category(ulong Id, string Name);
    private sealed record Channel(ulong Id, ulong CategoryId, string Name, ChannelPermissionProfile Profile);
    private sealed record Message(ulong Id, ulong ChannelId, MessagePayload Payload);

    private readonly ConcurrentDictionary<ulong, Category> _categories = new();
    private readonly ConcurrentDictionary<ulong, Channel> _channels = new();
    private readonly ConcurrentDictionary<ulong, Message> _messages = new();
    private ulong _nextId = 1000;

    public IReadOnlyList<string> MissingPermissions { get; set; } = [];
    public int CreatedCategories { get; private set; }
    public int CreatedChannels { get; private set; }
    public int PostedMessages { get; private set; }
    public int EditedMessages { get; private set; }

    private ulong NextId() => Interlocked.Increment(ref _nextId);

    // --- test helpers (simulate external drift) ---
    public void ExternallyDeleteChannel(ulong channelId) => _channels.TryRemove(channelId, out _);
    public void ExternallyDeleteCategory(ulong categoryId) => _categories.TryRemove(categoryId, out _);
    public void ExternallyDeleteMessage(ulong messageId) => _messages.TryRemove(messageId, out _);
    public MessagePayload? GetMessagePayload(ulong messageId) => _messages.TryGetValue(messageId, out var m) ? m.Payload : null;
    public IReadOnlyCollection<ulong> ChannelIds => _channels.Keys.ToList();
    public IReadOnlyCollection<ulong> CategoryIds => _categories.Keys.ToList();
    public ChannelPermissionProfile ProfileOf(ulong channelId) => _channels[channelId].Profile;

    public bool CategoryExists(ulong guildId, ulong categoryId) => _categories.ContainsKey(categoryId);

    public Task<ulong?> FindCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken)
    {
        var match = _categories.Values.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match is null ? (ulong?)null : match.Id);
    }

    public Task<ulong> CreateCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken)
    {
        var id = NextId();
        _categories[id] = new Category(id, name);
        CreatedCategories++;
        return Task.FromResult(id);
    }

    public bool ChannelExists(ulong guildId, ulong channelId) => _channels.ContainsKey(channelId);

    public Task<ulong?> FindChannelAsync(ulong guildId, ulong categoryId, string name, CancellationToken cancellationToken)
    {
        var match = _channels.Values.FirstOrDefault(c =>
            c.CategoryId == categoryId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match is null ? (ulong?)null : match.Id);
    }

    public Task<ulong> CreateChannelAsync(ulong guildId, ulong categoryId, string name, ChannelPermissionProfile profile, CancellationToken cancellationToken)
    {
        var id = NextId();
        _channels[id] = new Channel(id, categoryId, name, profile);
        CreatedChannels++;
        return Task.FromResult(id);
    }

    public Task ApplyChannelSettingsAsync(ulong guildId, ulong channelId, ulong categoryId, string name, ChannelPermissionProfile profile, CancellationToken cancellationToken)
    {
        _channels[channelId] = new Channel(channelId, categoryId, name, profile);
        return Task.CompletedTask;
    }

    public Task<bool> MessageExistsAsync(ulong guildId, ulong channelId, ulong messageId, CancellationToken cancellationToken) =>
        Task.FromResult(_messages.ContainsKey(messageId));

    public Task<ulong> PostMessageAsync(ulong guildId, ulong channelId, MessagePayload payload, CancellationToken cancellationToken)
    {
        var id = NextId();
        _messages[id] = new Message(id, channelId, payload);
        PostedMessages++;
        return Task.FromResult(id);
    }

    public Task EditMessageAsync(ulong guildId, ulong channelId, ulong messageId, MessagePayload payload, CancellationToken cancellationToken)
    {
        _messages[messageId] = new Message(messageId, channelId, payload);
        EditedMessages++;
        return Task.CompletedTask;
    }

    public Task DeleteChannelAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken)
    {
        _channels.TryRemove(channelId, out _);
        return Task.CompletedTask;
    }

    public Task DeleteCategoryAsync(ulong guildId, ulong categoryId, CancellationToken cancellationToken)
    {
        _categories.TryRemove(categoryId, out _);
        return Task.CompletedTask;
    }

    public IReadOnlyList<string> GetMissingBotPermissions(ulong guildId) => MissingPermissions;
}
```

- [ ] **Step 3: Smoke-test the fake**

`tests/RustPlusBot.Features.Workspace.Tests/Fakes/FakeWorkspaceGatewayTests.cs`:

```csharp
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Tests.Fakes;

namespace RustPlusBot.Features.Workspace.Tests.Fakes;

public sealed class FakeWorkspaceGatewayTests
{
    [Fact]
    public async Task CreateAndFind_RoundTrips()
    {
        var gateway = new FakeWorkspaceGateway();
        var categoryId = await gateway.CreateCategoryAsync(1, "RustPlusBot", default);
        Assert.True(gateway.CategoryExists(1, categoryId));
        Assert.Equal(categoryId, await gateway.FindCategoryAsync(1, "rustplusbot", default));

        var channelId = await gateway.CreateChannelAsync(1, categoryId, "information", ChannelPermissionProfile.ReadOnly, default);
        Assert.Equal(channelId, await gateway.FindChannelAsync(1, categoryId, "information", default));

        gateway.ExternallyDeleteChannel(channelId);
        Assert.False(gateway.ChannelExists(1, channelId));
    }
}
```

- [ ] **Step 4: Run tests + build**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
then `dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(workspace): IWorkspaceGateway seam + in-memory test fake"
```

---

## Task 9: Per-guild `ProvisioningLock`

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Reconciler/IProvisioningLock.cs`
- Create: `src/RustPlusBot.Features.Workspace/Reconciler/ProvisioningLock.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/ProvisioningLockTests.cs`

- [ ] **Step 1: Write the failing test**

`tests/RustPlusBot.Features.Workspace.Tests/Reconciler/ProvisioningLockTests.cs`:

```csharp
using RustPlusBot.Features.Workspace.Reconciler;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class ProvisioningLockTests
{
    [Fact]
    public async Task SameGuild_SerializesHolders()
    {
        var sut = new ProvisioningLock();
        var handle = await sut.AcquireAsync(1);

        var second = sut.AcquireAsync(1);
        Assert.False(second.IsCompleted); // blocked while first is held

        handle.Dispose();
        await second; // now succeeds
        ((IDisposable)await second).Dispose();
    }

    [Fact]
    public async Task DifferentGuilds_DoNotBlock()
    {
        var sut = new ProvisioningLock();
        using var a = await sut.AcquireAsync(1);
        using var b = await sut.AcquireAsync(2); // does not block
        Assert.NotNull(b);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile.

- [ ] **Step 3: Define the interface**

`src/RustPlusBot.Features.Workspace/Reconciler/IProvisioningLock.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Serializes reconciliation per guild so concurrent triggers cannot interleave.</summary>
internal interface IProvisioningLock
{
    /// <summary>Acquires the guild's lock; dispose the result to release.</summary>
    Task<IDisposable> AcquireAsync(ulong guildId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement it**

`src/RustPlusBot.Features.Workspace/Reconciler/ProvisioningLock.cs`:

```csharp
using System.Collections.Concurrent;

namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Per-guild <see cref="SemaphoreSlim"/>-backed lock. Registered as a singleton.</summary>
internal sealed class ProvisioningLock : IProvisioningLock
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _locks = new();

    /// <inheritdoc />
    public async Task<IDisposable> AcquireAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var semaphore = _locks.GetOrAdd(guildId, static _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
```

- [ ] **Step 5: Run tests + build**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
then `dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(workspace): per-guild ProvisioningLock"
```

---

## Task 10: `WorkspaceReconciler` — converge engine + fresh global provision

This task delivers the complete resolve→adopt→create converge algorithm (categories, channels,
messages) plus the per-guild lock and bot-permission pre-flight. Tasks 11–16 add characterization tests
that prove the emergent properties (idempotency, adopt, self-heal, edit-in-place, per-server) against
this same implementation; they should pass without production changes (fix the reconciler if any is red).

**Files:**

- Modify: `src/RustPlusBot.Persistence/Servers/IServerService.cs` (add `GetAsync`)
- Modify: `src/RustPlusBot.Persistence/Servers/ServerService.cs` (add `GetAsync`)
- Create: `src/RustPlusBot.Features.Workspace/Reconciler/ReconcileResult.cs`
- Create: `src/RustPlusBot.Features.Workspace/Reconciler/IWorkspaceReconciler.cs`
- Create: `src/RustPlusBot.Features.Workspace/Reconciler/WorkspaceReconciler.cs`
- Create: `tests/RustPlusBot.Features.Workspace.Tests/Fakes/FakeWorkspaceStore.cs`
- Create: `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/ReconcilerHarness.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerProvisionTests.cs`

- [ ] **Step 1: Add `IServerService.GetAsync`**

In `src/RustPlusBot.Persistence/Servers/IServerService.cs` add:

```csharp
    /// <summary>Gets a server by id within a guild, or null.</summary>
    /// <param name="guildId">Owning Discord guild snowflake.</param>
    /// <param name="serverId">The server id.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The server, or null if not found in this guild.</returns>
    Task<RustServer?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
```

In `src/RustPlusBot.Persistence/Servers/ServerService.cs` add (it already has `using Microsoft.EntityFrameworkCore;`):

```csharp
    /// <inheritdoc />
    public Task<RustServer?> GetAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default) =>
        context.RustServers.SingleOrDefaultAsync(s => s.GuildId == guildId && s.Id == serverId, cancellationToken);
```

- [ ] **Step 2: Write the failing fresh-provision test (plus harness + fake store)**

`tests/RustPlusBot.Features.Workspace.Tests/Fakes/FakeWorkspaceStore.cs`:

```csharp
using System.Collections.Concurrent;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Tests.Fakes;

/// <summary>In-memory <see cref="IWorkspaceStore"/> for reconciler unit tests.</summary>
internal sealed class FakeWorkspaceStore : IWorkspaceStore
{
    private static string Scope(ulong g, Guid? s) => $"{g}:{s?.ToString() ?? "global"}";

    private readonly ConcurrentDictionary<string, ProvisionedCategory> _categories = new();
    private readonly ConcurrentDictionary<string, ProvisionedChannel> _channels = new(); // key: scope|channelKey
    private readonly ConcurrentDictionary<string, ProvisionedMessage> _messages = new(); // key: scope|messageKey
    private readonly ConcurrentDictionary<ulong, string> _cultures = new();

    public Task<ProvisionedCategory?> GetCategoryAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_categories.GetValueOrDefault(Scope(guildId, serverId)));

    public Task SaveCategoryAsync(ProvisionedCategory category, CancellationToken cancellationToken = default)
    {
        _categories[Scope(category.GuildId, category.RustServerId)] = category;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProvisionedChannel>> GetChannelsAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default)
    {
        var prefix = Scope(guildId, serverId) + "|";
        IReadOnlyList<ProvisionedChannel> list = _channels
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => kv.Value).ToList();
        return Task.FromResult(list);
    }

    public Task SaveChannelAsync(ProvisionedChannel channel, CancellationToken cancellationToken = default)
    {
        _channels[$"{Scope(channel.GuildId, channel.RustServerId)}|{channel.ChannelKey}"] = channel;
        return Task.CompletedTask;
    }

    public Task<ProvisionedMessage?> GetMessageAsync(ulong guildId, Guid? serverId, string messageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_messages.GetValueOrDefault($"{Scope(guildId, serverId)}|{messageKey}"));

    public Task SaveMessageAsync(ProvisionedMessage message, CancellationToken cancellationToken = default)
    {
        _messages[$"{Scope(message.GuildId, message.RustServerId)}|{message.MessageKey}"] = message;
        return Task.CompletedTask;
    }

    public Task<string> GetCultureAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cultures.GetValueOrDefault(guildId, "en"));

    public Task SetCultureAsync(ulong guildId, string culture, CancellationToken cancellationToken = default)
    {
        _cultures[guildId] = culture;
        return Task.CompletedTask;
    }

    public Task DeleteScopeAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken = default)
    {
        _categories.TryRemove(Scope(guildId, serverId), out _);
        var prefix = Scope(guildId, serverId) + "|";
        foreach (var key in _channels.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _channels.TryRemove(key, out _);
        }

        foreach (var key in _messages.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _messages.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProvisionedCategory>> GetAllCategoriesAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProvisionedCategory> list = _categories.Values.Where(c => c.GuildId == guildId).ToList();
        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<ulong>> GetProvisionedGuildIdsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ulong> list = _categories.Values.Select(c => c.GuildId).Distinct().ToList();
        return Task.FromResult(list);
    }
}
```

`tests/RustPlusBot.Features.Workspace.Tests/Reconciler/ReconcilerHarness.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Tests.Fakes;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

/// <summary>Builds a WorkspaceReconciler wired with fakes for tests.</summary>
internal sealed class ReconcilerHarness
{
    public FakeWorkspaceGateway Gateway { get; } = new();
    public FakeWorkspaceStore Store { get; } = new();
    public IServerService Servers { get; } = Substitute.For<IServerService>();

    private readonly List<IChannelSpecProvider> _channelProviders = [];
    private readonly List<IMessageSpecProvider> _messageProviders = [];
    private readonly List<IMessageRenderer> _renderers = [];

    public ReconcilerHarness WithChannel(WorkspaceScope scope, string key, string nameKey, int order = 0)
    {
        _channelProviders.Add(new StubChannelProvider([new ChannelSpec(scope, key, nameKey, ChannelPermissionProfile.ReadOnly, order)]));
        return this;
    }

    public ReconcilerHarness WithMessage(WorkspaceScope scope, string key, string channelKey, string text)
    {
        _messageProviders.Add(new StubMessageProvider([new MessageSpec(scope, key, channelKey)]));
        _renderers.Add(new StubRenderer(key, text));
        return this;
    }

    public WorkspaceReconciler Build()
    {
        var registry = new WorkspaceRegistry(_channelProviders, _messageProviders);
        return new WorkspaceReconciler(
            registry, Gateway, Store, _renderers, Servers,
            new Localizer(LocalizationCatalog.Default), new ProvisioningLock(),
            NullLogger<WorkspaceReconciler>.Instance);
    }

    private sealed class StubChannelProvider(IEnumerable<ChannelSpec> specs) : IChannelSpecProvider
    {
        public IEnumerable<ChannelSpec> GetChannelSpecs() => specs;
    }

    private sealed class StubMessageProvider(IEnumerable<MessageSpec> specs) : IMessageSpecProvider
    {
        public IEnumerable<MessageSpec> GetMessageSpecs() => specs;
    }

    private sealed class StubRenderer(string key, string text) : IMessageRenderer
    {
        public string MessageKey { get; } = key;
        public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new MessagePayload(text, null, null));
    }
}
```

`tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerProvisionTests.cs`:

```csharp
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerProvisionTests
{
    private static ReconcilerHarness GlobalHarness() => new ReconcilerHarness()
        .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
        .WithChannel(WorkspaceScope.Global, "setup", "channel.setup.name", 1)
        .WithChannel(WorkspaceScope.Global, "settings", "channel.settings.name", 2)
        .WithMessage(WorkspaceScope.Global, "information.main", "information", "hello");

    [Fact]
    public async Task ReconcileGlobal_FreshGuild_CreatesCategoryChannelsAndMessage()
    {
        var harness = GlobalHarness();
        var sut = harness.Build();

        var result = await sut.ReconcileGlobalAsync(1);

        Assert.Equal(ReconcileStatus.Provisioned, result.Status);
        Assert.Equal(1, harness.Gateway.CreatedCategories);
        Assert.Equal(3, harness.Gateway.CreatedChannels);
        Assert.Equal(1, harness.Gateway.PostedMessages);
        Assert.NotNull(await harness.Store.GetCategoryAsync(1, null));
        Assert.Equal(3, (await harness.Store.GetChannelsAsync(1, null)).Count);
        Assert.NotNull(await harness.Store.GetMessageAsync(1, null, "information.main"));
    }

    [Fact]
    public async Task ReconcileGlobal_MissingBotPermissions_DoesNothing()
    {
        var harness = GlobalHarness();
        harness.Gateway.MissingPermissions = ["Manage Channels"];
        var sut = harness.Build();

        var result = await sut.ReconcileGlobalAsync(1);

        Assert.Equal(ReconcileStatus.MissingPermissions, result.Status);
        Assert.Equal(["Manage Channels"], result.MissingPermissions);
        Assert.Equal(0, harness.Gateway.CreatedCategories);
        Assert.Equal(0, harness.Gateway.CreatedChannels);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile — reconciler types missing.

- [ ] **Step 4: Add the result + interface**

`src/RustPlusBot.Features.Workspace/Reconciler/ReconcileResult.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Outcome of a reconcile.</summary>
internal enum ReconcileStatus
{
    /// <summary>The scope was converged.</summary>
    Provisioned = 0,

    /// <summary>The bot lacks required guild permissions; nothing was changed.</summary>
    MissingPermissions = 1,
}

/// <summary>Result of a reconcile, including any missing permissions.</summary>
/// <param name="Status">The outcome.</param>
/// <param name="MissingPermissions">Missing permission names when <see cref="ReconcileStatus.MissingPermissions"/>.</param>
internal sealed record ReconcileResult(ReconcileStatus Status, IReadOnlyList<string> MissingPermissions)
{
    /// <summary>A successful provision result.</summary>
    public static ReconcileResult Provisioned { get; } = new(ReconcileStatus.Provisioned, []);

    /// <summary>Builds a missing-permissions result.</summary>
    public static ReconcileResult Missing(IReadOnlyList<string> permissions) => new(ReconcileStatus.MissingPermissions, permissions);
}
```

`src/RustPlusBot.Features.Workspace/Reconciler/IWorkspaceReconciler.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Converges a guild's Discord workspace to the desired state described by the registry.</summary>
internal interface IWorkspaceReconciler
{
    /// <summary>Reconciles the global RustPlusBot category and its channels/messages.</summary>
    Task<ReconcileResult> ReconcileGlobalAsync(ulong guildId, CancellationToken cancellationToken = default);

    /// <summary>Reconciles a single server's category and its channels/messages.</summary>
    Task<ReconcileResult> ReconcileServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Implement the reconciler**

`src/RustPlusBot.Features.Workspace/Reconciler/WorkspaceReconciler.cs`:

```csharp
using Microsoft.Extensions.Logging;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Servers;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Reconciler;

/// <summary>Desired-state reconciler. resolve -> adopt -> create, serialized per guild.</summary>
internal sealed class WorkspaceReconciler(
    IWorkspaceRegistry registry,
    IWorkspaceGateway gateway,
    IWorkspaceStore store,
    IEnumerable<IMessageRenderer> renderers,
    IServerService servers,
    ILocalizer localizer,
    IProvisioningLock provisioningLock,
    ILogger<WorkspaceReconciler> logger) : IWorkspaceReconciler
{
    private readonly Dictionary<string, IMessageRenderer> _renderers =
        renderers.ToDictionary(r => r.MessageKey, StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<ReconcileResult> ReconcileGlobalAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);

        var missing = gateway.GetMissingBotPermissions(guildId);
        if (missing.Count > 0)
        {
            return ReconcileResult.Missing(missing);
        }

        var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        var categoryName = localizer.Get("category.global.name", culture);
        await ReconcileScopeAsync(guildId, null, categoryName, culture, WorkspaceScope.Global, cancellationToken).ConfigureAwait(false);
        return ReconcileResult.Provisioned;
    }

    /// <inheritdoc />
    public async Task<ReconcileResult> ReconcileServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);

        var missing = gateway.GetMissingBotPermissions(guildId);
        if (missing.Count > 0)
        {
            return ReconcileResult.Missing(missing);
        }

        var server = await servers.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            logger.LogWarning("ReconcileServer skipped: server {ServerId} not found in guild {GuildId}.", serverId, guildId);
            return ReconcileResult.Provisioned;
        }

        var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        await ReconcileScopeAsync(guildId, serverId, server.Name, culture, WorkspaceScope.PerServer, cancellationToken).ConfigureAwait(false);
        return ReconcileResult.Provisioned;
    }

    private async Task ReconcileScopeAsync(ulong guildId, Guid? serverId, string categoryName, string culture, WorkspaceScope scope, CancellationToken cancellationToken)
    {
        var categoryId = await EnsureCategoryAsync(guildId, serverId, categoryName, cancellationToken).ConfigureAwait(false);
        var channelIds = await EnsureChannelsAsync(guildId, serverId, categoryId, culture, scope, cancellationToken).ConfigureAwait(false);
        await EnsureMessagesAsync(guildId, serverId, channelIds, culture, scope, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ulong> EnsureCategoryAsync(ulong guildId, Guid? serverId, string name, CancellationToken cancellationToken)
    {
        var record = await store.GetCategoryAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (record is not null && gateway.CategoryExists(guildId, record.DiscordCategoryId))
        {
            return record.DiscordCategoryId;
        }

        var adopted = await gateway.FindCategoryAsync(guildId, name, cancellationToken).ConfigureAwait(false);
        var categoryId = adopted ?? await gateway.CreateCategoryAsync(guildId, name, cancellationToken).ConfigureAwait(false);
        await store.SaveCategoryAsync(
            new ProvisionedCategory { GuildId = guildId, RustServerId = serverId, DiscordCategoryId = categoryId },
            cancellationToken).ConfigureAwait(false);
        return categoryId;
    }

    private async Task<Dictionary<string, ulong>> EnsureChannelsAsync(ulong guildId, Guid? serverId, ulong categoryId, string culture, WorkspaceScope scope, CancellationToken cancellationToken)
    {
        var existing = (await store.GetChannelsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(c => c.ChannelKey, StringComparer.Ordinal);
        var result = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var specs = registry.GetChannelSpecs(scope);

        foreach (var spec in specs)
        {
            var name = localizer.Get(spec.NameKey, culture);
            ulong channelId;

            if (existing.TryGetValue(spec.Key, out var rec) && gateway.ChannelExists(guildId, rec.DiscordChannelId))
            {
                channelId = rec.DiscordChannelId;
                await gateway.ApplyChannelSettingsAsync(guildId, channelId, categoryId, name, spec.Permissions, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var adopted = await gateway.FindChannelAsync(guildId, categoryId, name, cancellationToken).ConfigureAwait(false);
                if (adopted is ulong adoptedId)
                {
                    channelId = adoptedId;
                    await gateway.ApplyChannelSettingsAsync(guildId, channelId, categoryId, name, spec.Permissions, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    channelId = await gateway.CreateChannelAsync(guildId, categoryId, name, spec.Permissions, cancellationToken).ConfigureAwait(false);
                }

                await store.SaveChannelAsync(
                    new ProvisionedChannel { GuildId = guildId, RustServerId = serverId, ChannelKey = spec.Key, DiscordChannelId = channelId },
                    cancellationToken).ConfigureAwait(false);
            }

            result[spec.Key] = channelId;
        }

        var registryKeys = specs.Select(s => s.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var orphan in existing.Keys.Where(k => !registryKeys.Contains(k)))
        {
            logger.LogInformation("Retaining provisioned channel '{Key}' no longer in the registry (guild {GuildId}).", orphan, guildId);
        }

        return result;
    }

    private async Task EnsureMessagesAsync(ulong guildId, Guid? serverId, IReadOnlyDictionary<string, ulong> channelIds, string culture, WorkspaceScope scope, CancellationToken cancellationToken)
    {
        foreach (var spec in registry.GetMessageSpecs(scope))
        {
            if (!channelIds.TryGetValue(spec.ChannelKey, out var channelId) || !_renderers.TryGetValue(spec.Key, out var renderer))
            {
                continue;
            }

            var payload = await renderer.RenderAsync(new MessageRenderContext(guildId, serverId, culture), cancellationToken).ConfigureAwait(false);
            var record = await store.GetMessageAsync(guildId, serverId, spec.Key, cancellationToken).ConfigureAwait(false);

            var canEditInPlace = record is not null
                && record.DiscordChannelId == channelId
                && await gateway.MessageExistsAsync(guildId, channelId, record.DiscordMessageId, cancellationToken).ConfigureAwait(false);

            if (canEditInPlace)
            {
                await gateway.EditMessageAsync(guildId, channelId, record!.DiscordMessageId, payload, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var messageId = await gateway.PostMessageAsync(guildId, channelId, payload, cancellationToken).ConfigureAwait(false);
                await store.SaveMessageAsync(
                    new ProvisionedMessage { GuildId = guildId, RustServerId = serverId, MessageKey = spec.Key, DiscordChannelId = channelId, DiscordMessageId = messageId },
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
then `dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(workspace): WorkspaceReconciler converge engine + fresh provision"
```

---

## Tasks 11–15: Reconciler characterization tests

Each adds tests to a new file proving an emergent property of the Task 10 reconciler. Expected: **PASS
without production changes.** If a test is red, the bug is in `WorkspaceReconciler` — fix it there, keep
the test. Each task ends with `dotnet test … && dotnet build RustPlusBot.slnx` then a commit.

### Task 11: Idempotent re-run (no duplicates)

**Test:** `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerIdempotencyTests.cs`

- [ ] **Step 1: Add the test**

```csharp
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerIdempotencyTests
{
    private static ReconcilerHarness Harness() => new ReconcilerHarness()
        .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
        .WithMessage(WorkspaceScope.Global, "information.main", "information", "hello");

    [Fact]
    public async Task RunTwice_DoesNotDuplicate()
    {
        var harness = Harness();
        var sut = harness.Build();

        await sut.ReconcileGlobalAsync(1);
        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(1, harness.Gateway.CreatedCategories);
        Assert.Equal(1, harness.Gateway.CreatedChannels);
        Assert.Equal(1, harness.Gateway.PostedMessages);
        Assert.Equal(1, harness.Gateway.EditedMessages); // second run edits the anchored message in place
        Assert.Single(harness.Gateway.CategoryIds);
        Assert.Single(harness.Gateway.ChannelIds);
    }
}
```

- [ ] **Step 2:** Run tests + build. Expected: PASS; 0/0.
- [ ] **Step 3:** Commit: `test(workspace): reconciler is idempotent on re-run`.

### Task 12: Adopt-by-name when the stored snowflake is gone

**Test:** `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerAdoptTests.cs`

- [ ] **Step 1: Add the test**

```csharp
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerAdoptTests
{
    [Fact]
    public async Task StoredChannelGone_ButSameNamedExists_RebindsNotRecreates()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        // Simulate: the channel row still points at an id that no longer exists, but a same-named
        // channel exists under the category (e.g. recreated out of band).
        var channel = (await harness.Store.GetChannelsAsync(1, null))[0];
        harness.Gateway.ExternallyDeleteChannel(channel.DiscordChannelId);
        var category = await harness.Store.GetCategoryAsync(1, null);
        await harness.Gateway.CreateChannelAsync(1, category!.DiscordCategoryId, "information", ChannelPermissionProfile.ReadOnly, default);
        var createdBefore = harness.Gateway.CreatedChannels;

        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(createdBefore, harness.Gateway.CreatedChannels); // adopted, not created
        var rebound = (await harness.Store.GetChannelsAsync(1, null))[0];
        Assert.True(harness.Gateway.ChannelExists(1, rebound.DiscordChannelId));
    }
}
```

- [ ] **Step 2:** Run tests + build. Expected: PASS; 0/0.
- [ ] **Step 3:** Commit: `test(workspace): reconciler adopts same-named channel on drift`.

### Task 13: Self-heal a deleted channel

**Test:** `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerSelfHealTests.cs`

- [ ] **Step 1: Add the test**

```csharp
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerSelfHealTests
{
    [Fact]
    public async Task DeletedChannel_IsRecreatedOnReconcile()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithMessage(WorkspaceScope.Global, "information.main", "information", "hello");
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var channel = (await harness.Store.GetChannelsAsync(1, null))[0];
        harness.Gateway.ExternallyDeleteChannel(channel.DiscordChannelId);

        await sut.ReconcileGlobalAsync(1);

        var healed = (await harness.Store.GetChannelsAsync(1, null))[0];
        Assert.True(harness.Gateway.ChannelExists(1, healed.DiscordChannelId));
        Assert.Equal(2, harness.Gateway.CreatedChannels); // original + heal (no same-named adopt available)
    }
}
```

- [ ] **Step 2:** Run tests + build. Expected: PASS; 0/0.
- [ ] **Step 3:** Commit: `test(workspace): reconciler self-heals a deleted channel`.

### Task 14: Messages edit-in-place vs repost; orphan spec retained

**Test:** `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerMessageTests.cs`

- [ ] **Step 1: Add the test**

```csharp
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerMessageTests
{
    [Fact]
    public async Task DeletedMessage_IsReposted_NotEdited()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithMessage(WorkspaceScope.Global, "information.main", "information", "hello");
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var message = await harness.Store.GetMessageAsync(1, null, "information.main");
        harness.Gateway.ExternallyDeleteMessage(message!.DiscordMessageId);

        await sut.ReconcileGlobalAsync(1);

        Assert.Equal(2, harness.Gateway.PostedMessages); // reposted because the anchor was gone
    }

    [Fact]
    public async Task ChannelKeyRemovedFromRegistry_RetainsExistingRecord()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithChannel(WorkspaceScope.Global, "settings", "channel.settings.name", 1);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        // Re-run with a registry that no longer contains "settings", reusing the SAME store + gateway
        // so the prior "settings" record is still present.
        var sut2 = new ReconcilerBuilderReusing(harness)
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .Build();

        await sut2.ReconcileGlobalAsync(1);

        // The orphaned "settings" channel record is retained, not deleted.
        var channels = await harness.Store.GetChannelsAsync(1, null);
        Assert.Contains(channels, c => c.ChannelKey == "settings");
    }
}
```

> **Note:** the second test needs a builder that reuses an existing harness's store/gateway. Add this
> helper to `ReconcilerHarness.cs` (Task 10 file) as a sibling class:

```csharp
internal sealed class ReconcilerBuilderReusing(ReconcilerHarness source)
{
    private readonly List<IChannelSpecProvider> _channelProviders = [];
    private readonly List<IMessageSpecProvider> _messageProviders = [];
    private readonly List<IMessageRenderer> _renderers = [];

    public ReconcilerBuilderReusing WithChannel(WorkspaceScope scope, string key, string nameKey, int order = 0)
    {
        _channelProviders.Add(new ListChannelProvider([new ChannelSpec(scope, key, nameKey, ChannelPermissionProfile.ReadOnly, order)]));
        return this;
    }

    public WorkspaceReconciler Build() => new(
        new WorkspaceRegistry(_channelProviders, _messageProviders),
        source.Gateway, source.Store, _renderers, source.Servers,
        new Localizer(LocalizationCatalog.Default), new ProvisioningLock(),
        NullLogger<WorkspaceReconciler>.Instance);

    private sealed class ListChannelProvider(IEnumerable<ChannelSpec> specs) : IChannelSpecProvider
    {
        public IEnumerable<ChannelSpec> GetChannelSpecs() => specs;
    }
}
```

> Add the matching `using` directives at the top of `ReconcilerHarness.cs`:
> `using Microsoft.Extensions.Logging.Abstractions;`, `using RustPlusBot.Features.Workspace.Localization;`,
> `using RustPlusBot.Features.Workspace.Reconciler;`, `using RustPlusBot.Features.Workspace.Registry;`
> (already present from Task 10).

- [ ] **Step 2:** Run tests + build. Expected: PASS; 0/0.
- [ ] **Step 3:** Commit: `test(workspace): message repost-vs-edit and orphan-spec retention`.

### Task 15: Concurrency — two reconciles converge once

**Test:** `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerConcurrencyTests.cs`

- [ ] **Step 1: Add the test**

```csharp
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerConcurrencyTests
{
    [Fact]
    public async Task ConcurrentReconciles_CreateOneCategory()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();

        await Task.WhenAll(sut.ReconcileGlobalAsync(1), sut.ReconcileGlobalAsync(1));

        Assert.Equal(1, harness.Gateway.CreatedCategories);
        Assert.Single(harness.Gateway.CategoryIds);
        Assert.Single(harness.Gateway.ChannelIds);
    }
}
```

> **Note:** the `FakeWorkspaceStore`/`FakeWorkspaceGateway` use `ConcurrentDictionary`, and
> `ProvisioningLock` serializes per guild, so the two runs cannot both create. If this is flaky, the lock
> is not being held across the whole converge — verify `ReconcileGlobalAsync` acquires before any gateway
> call.

- [ ] **Step 2:** Run tests + build. Expected: PASS; 0/0.
- [ ] **Step 3:** Commit: `test(workspace): concurrent reconciles converge once`.

---

## Task 16: `ReconcileServer` — per-server category named after the server

**Test:** `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerServerTests.cs`

- [ ] **Step 1: Add the test**

```csharp
using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerServerTests
{
    [Fact]
    public async Task ReconcileServer_CreatesCategoryNamedAfterServer_ScopedToServer()
    {
        var serverId = Guid.NewGuid();
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0)
            .WithMessage(WorkspaceScope.PerServer, "server.info", "info", "info");
        harness.Servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "Rustopia EU", Ip = "1.1.1.1", Port = 28015 });
        var sut = harness.Build();

        var result = await sut.ReconcileServerAsync(1, serverId);

        Assert.Equal(ReconcileStatus.Provisioned, result.Status);
        var category = await harness.Store.GetCategoryAsync(1, serverId);
        Assert.NotNull(category);
        Assert.Equal(category!.DiscordCategoryId, await harness.Gateway.FindCategoryAsync(1, "Rustopia EU", default));
        var channels = await harness.Store.GetChannelsAsync(1, serverId);
        Assert.Single(channels);
        Assert.Equal(serverId, channels[0].RustServerId);
        Assert.Empty(await harness.Store.GetChannelsAsync(1, null)); // global scope untouched
    }

    [Fact]
    public async Task ReconcileServer_UnknownServer_IsNoOp()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0);
        harness.Servers.GetAsync(1, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((RustServer?)null);
        var sut = harness.Build();

        var result = await sut.ReconcileServerAsync(1, Guid.NewGuid());

        Assert.Equal(ReconcileStatus.Skipped, result.Status);
        Assert.Equal(0, harness.Gateway.CreatedCategories);
    }
}
```

- [ ] **Step 2:** Run tests + build. Expected: PASS; 0/0 (the Task 10 reconciler already implements
  `ReconcileServerAsync`).
- [ ] **Step 3:** Commit: `test(workspace): per-server category provisioning`.

---

## Task 17: Channel/message keys, real renderers, and spec providers

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`
- Create: `src/RustPlusBot.Features.Workspace/Specs/GlobalWorkspaceSpecProvider.cs`
- Create: `src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`
- Create: `src/RustPlusBot.Features.Workspace/Messages/InformationMessageRenderer.cs`
- Create: `src/RustPlusBot.Features.Workspace/Messages/SetupMessageRenderer.cs`
- Create: `src/RustPlusBot.Features.Workspace/Messages/SettingsMessageRenderer.cs`
- Create: `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`

- [ ] **Step 1: Write the failing renderer tests**

`tests/RustPlusBot.Features.Workspace.Tests/Messages/RendererTests.cs`:

```csharp
using Discord;
using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Tests.Messages;

public sealed class RendererTests
{
    private static readonly Localizer Loc = new(LocalizationCatalog.Default);
    private static readonly MessageRenderContext Global = new(1, null, "en");

    [Fact]
    public async Task Information_ShowsServerCount()
    {
        var servers = Substitute.For<IServerService>();
        servers.ListAsync(1, Arg.Any<CancellationToken>())
            .Returns(new List<RustServer> { new() { Name = "A" }, new() { Name = "B" }, new() { Name = "C" } });
        var renderer = new InformationMessageRenderer(servers, Loc);

        var payload = await renderer.RenderAsync(Global, default);

        Assert.NotNull(payload.Embed);
        Assert.Contains("3", payload.Embed!.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Settings_HasLanguageSelectMenu()
    {
        var renderer = new SettingsMessageRenderer(Loc);

        var payload = await renderer.RenderAsync(Global, default);

        Assert.NotNull(payload.Components);
        var selects = payload.Components!.Components.SelectMany(r => r.Components).OfType<SelectMenuComponent>();
        Assert.Contains(selects, s => s.CustomId == "workspace:settings:culture");
    }

    [Fact]
    public async Task ServerInfo_TitleIsServerName_AndEndpointShown()
    {
        var serverId = Guid.NewGuid();
        var servers = Substitute.For<IServerService>();
        servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "Rustopia EU", Ip = "1.2.3.4", Port = 28015 });
        var renderer = new ServerInfoMessageRenderer(servers, Loc);

        var payload = await renderer.RenderAsync(new MessageRenderContext(1, serverId, "en"), default);

        Assert.Equal("Rustopia EU", payload.Embed!.Title);
        Assert.Contains("1.2.3.4", payload.Embed.Description, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile.

- [ ] **Step 3: Add the keys**

`src/RustPlusBot.Features.Workspace/WorkspaceKeys.cs`:

```csharp
namespace RustPlusBot.Features.Workspace;

/// <summary>Stable channel keys persisted as <c>ProvisionedChannel.ChannelKey</c>.</summary>
internal static class WorkspaceChannelKeys
{
    public const string Information = "information";
    public const string Setup = "setup";
    public const string Settings = "settings";
    public const string ServerInfo = "info";
}

/// <summary>Stable message keys persisted as <c>ProvisionedMessage.MessageKey</c>.</summary>
internal static class WorkspaceMessageKeys
{
    public const string InformationMain = "information.main";
    public const string SetupMain = "setup.main";
    public const string SettingsMain = "settings.main";
    public const string ServerInfo = "server.info";
}
```

- [ ] **Step 4: Add the renderers**

`src/RustPlusBot.Features.Workspace/Messages/InformationMessageRenderer.cs`:

```csharp
using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the global #information status/help embed.</summary>
/// <param name="servers">Used for the registered-server count.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class InformationMessageRenderer(IServerService servers, ILocalizer localizer) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.InformationMain;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var count = (await servers.ListAsync(context.GuildId, cancellationToken).ConfigureAwait(false)).Count;
        var description = localizer.Get("information.body", context.Culture)
            + "\n\n"
            + localizer.Get("information.servers", context.Culture, count);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("information.title", context.Culture))
            .WithDescription(description)
            .WithColor(Color.Orange)
            .Build();
        return new MessagePayload(null, embed, null);
    }
}
```

`src/RustPlusBot.Features.Workspace/Messages/SetupMessageRenderer.cs`:

```csharp
using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the global #setup instructions (the interactive button arrives in 1b).</summary>
/// <param name="localizer">String resolution.</param>
internal sealed class SetupMessageRenderer(ILocalizer localizer) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.SetupMain;

    /// <inheritdoc />
    public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("setup.title", context.Culture))
            .WithDescription(localizer.Get("setup.body", context.Culture))
            .WithColor(Color.Blue)
            .Build();
        return ValueTask.FromResult(new MessagePayload(null, embed, null));
    }
}
```

`src/RustPlusBot.Features.Workspace/Messages/SettingsMessageRenderer.cs`:

```csharp
using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders the global #settings message with the language selector.</summary>
/// <param name="localizer">String resolution.</param>
internal sealed class SettingsMessageRenderer(ILocalizer localizer) : IMessageRenderer
{
    /// <summary>Custom id of the language select menu, handled by the settings component module.</summary>
    public const string LanguageSelectId = "workspace:settings:culture";

    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.SettingsMain;

    /// <inheritdoc />
    public ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("settings.title", context.Culture))
            .WithDescription(localizer.Get("settings.body", context.Culture))
            .WithColor(Color.DarkGrey)
            .Build();

        var menu = new SelectMenuBuilder()
            .WithCustomId(LanguageSelectId)
            .WithPlaceholder(localizer.Get("settings.language.label", context.Culture))
            .AddOption("English", "en")
            .AddOption("Français", "fr");
        var components = new ComponentBuilder().WithSelectMenu(menu).Build();

        return ValueTask.FromResult(new MessagePayload(null, embed, components));
    }
}
```

`src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`:

```csharp
using System.Globalization;
using Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Messages;

/// <summary>Renders a server's #info identity embed (static in 1a; live status enriched in 1b).</summary>
/// <param name="servers">Server lookup.</param>
/// <param name="localizer">String resolution.</param>
internal sealed class ServerInfoMessageRenderer(IServerService servers, ILocalizer localizer) : IMessageRenderer
{
    /// <inheritdoc />
    public string MessageKey => WorkspaceMessageKeys.ServerInfo;

    /// <inheritdoc />
    public async ValueTask<MessagePayload> RenderAsync(MessageRenderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.ServerId is not Guid serverId)
        {
            return new MessagePayload(null, null, null);
        }

        var server = await servers.GetAsync(context.GuildId, serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            return new MessagePayload(null, null, null);
        }

        var port = server.Port.ToString(CultureInfo.InvariantCulture);
        var embed = new EmbedBuilder()
            .WithTitle(localizer.Get("server.info.title", context.Culture, server.Name))
            .WithDescription(localizer.Get("server.info.endpoint", context.Culture, server.Ip, port))
            .WithColor(Color.Green)
            .Build();
        return new MessagePayload(null, embed, null);
    }
}
```

- [ ] **Step 5: Add the spec providers**

`src/RustPlusBot.Features.Workspace/Specs/GlobalWorkspaceSpecProvider.cs`:

```csharp
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Specs;

/// <summary>Contributes the global category's channels and messages.</summary>
internal sealed class GlobalWorkspaceSpecProvider : IChannelSpecProvider, IMessageSpecProvider
{
    /// <inheritdoc />
    public IEnumerable<ChannelSpec> GetChannelSpecs() =>
    [
        new(WorkspaceScope.Global, WorkspaceChannelKeys.Information, "channel.information.name", ChannelPermissionProfile.ReadOnly, 0),
        new(WorkspaceScope.Global, WorkspaceChannelKeys.Setup, "channel.setup.name", ChannelPermissionProfile.ReadOnly, 1),
        new(WorkspaceScope.Global, WorkspaceChannelKeys.Settings, "channel.settings.name", ChannelPermissionProfile.ReadOnly, 2),
    ];

    /// <inheritdoc />
    public IEnumerable<MessageSpec> GetMessageSpecs() =>
    [
        new(WorkspaceScope.Global, WorkspaceMessageKeys.InformationMain, WorkspaceChannelKeys.Information),
        new(WorkspaceScope.Global, WorkspaceMessageKeys.SetupMain, WorkspaceChannelKeys.Setup),
        new(WorkspaceScope.Global, WorkspaceMessageKeys.SettingsMain, WorkspaceChannelKeys.Settings),
    ];
}
```

`src/RustPlusBot.Features.Workspace/Specs/ServerWorkspaceSpecProvider.cs`:

```csharp
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Specs;

/// <summary>Contributes the per-server category's channels and messages (just #info in 1a).</summary>
internal sealed class ServerWorkspaceSpecProvider : IChannelSpecProvider, IMessageSpecProvider
{
    /// <inheritdoc />
    public IEnumerable<ChannelSpec> GetChannelSpecs() =>
    [
        new(WorkspaceScope.PerServer, WorkspaceChannelKeys.ServerInfo, "channel.info.name", ChannelPermissionProfile.ReadOnly, 0),
    ];

    /// <inheritdoc />
    public IEnumerable<MessageSpec> GetMessageSpecs() =>
    [
        new(WorkspaceScope.PerServer, WorkspaceMessageKeys.ServerInfo, WorkspaceChannelKeys.ServerInfo),
    ];
}
```

- [ ] **Step 6: Run tests + build**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
then `dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(workspace): channel/message keys, renderers, and spec providers"
```

---

## Task 18: `WorkspaceTeardownService`

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Teardown/IWorkspaceTeardownService.cs`
- Create: `src/RustPlusBot.Features.Workspace/Teardown/WorkspaceTeardownService.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Teardown/WorkspaceTeardownServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/RustPlusBot.Features.Workspace.Tests/Teardown/WorkspaceTeardownServiceTests.cs`:

```csharp
using NSubstitute;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Features.Workspace.Tests.Reconciler;

namespace RustPlusBot.Features.Workspace.Tests.Teardown;

public sealed class WorkspaceTeardownServiceTests
{
    [Fact]
    public async Task ResetGuild_DeletesChannelsCategoriesAndRecords()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        await harness.Build().ReconcileGlobalAsync(1);

        var teardown = new WorkspaceTeardownService(harness.Gateway, harness.Store, new ProvisioningLock());
        await teardown.ResetGuildAsync(1);

        Assert.Empty(harness.Gateway.ChannelIds);
        Assert.Empty(harness.Gateway.CategoryIds);
        Assert.Null(await harness.Store.GetCategoryAsync(1, null));
        Assert.Empty(await harness.Store.GetChannelsAsync(1, null));
    }

    [Fact]
    public async Task RemoveServer_DeletesOnlyThatServerScope()
    {
        var serverId = Guid.NewGuid();
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0)
            .WithChannel(WorkspaceScope.PerServer, "info", "channel.info.name", 0);
        harness.Servers.GetAsync(1, serverId, Arg.Any<CancellationToken>())
            .Returns(new RustServer { Id = serverId, GuildId = 1, Name = "S", Ip = "1.1.1.1", Port = 1 });
        var reconciler = harness.Build();
        await reconciler.ReconcileGlobalAsync(1);
        await reconciler.ReconcileServerAsync(1, serverId);

        var teardown = new WorkspaceTeardownService(harness.Gateway, harness.Store, new ProvisioningLock());
        await teardown.RemoveServerAsync(1, serverId);

        Assert.Null(await harness.Store.GetCategoryAsync(1, serverId));
        Assert.NotNull(await harness.Store.GetCategoryAsync(1, null)); // global retained
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile.

- [ ] **Step 3: Define the interface**

`src/RustPlusBot.Features.Workspace/Teardown/IWorkspaceTeardownService.cs`:

```csharp
namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Removes provisioned Discord resources and their records.</summary>
internal interface IWorkspaceTeardownService
{
    /// <summary>Deletes one server's category, channels, and records.</summary>
    Task RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default);

    /// <summary>Deletes the entire workspace for a guild (all scopes) and clears all records.</summary>
    Task ResetGuildAsync(ulong guildId, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 4: Implement it**

`src/RustPlusBot.Features.Workspace/Teardown/WorkspaceTeardownService.cs`:

```csharp
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Teardown;

/// <summary>Deletes provisioned resources under the per-guild lock, then clears the records.</summary>
/// <param name="gateway">Discord operations.</param>
/// <param name="store">Provisioning persistence.</param>
/// <param name="provisioningLock">Per-guild serialization (shared with the reconciler).</param>
internal sealed class WorkspaceTeardownService(
    IWorkspaceGateway gateway,
    IWorkspaceStore store,
    IProvisioningLock provisioningLock) : IWorkspaceTeardownService
{
    /// <inheritdoc />
    public async Task RemoveServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);
        await DeleteScopeAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResetGuildAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);
        var categories = await store.GetAllCategoriesAsync(guildId, cancellationToken).ConfigureAwait(false);
        foreach (var category in categories)
        {
            await DeleteScopeAsync(guildId, category.RustServerId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task DeleteScopeAsync(ulong guildId, Guid? serverId, CancellationToken cancellationToken)
    {
        foreach (var channel in await store.GetChannelsAsync(guildId, serverId, cancellationToken).ConfigureAwait(false))
        {
            await gateway.DeleteChannelAsync(guildId, channel.DiscordChannelId, cancellationToken).ConfigureAwait(false);
        }

        var category = await store.GetCategoryAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (category is not null)
        {
            await gateway.DeleteCategoryAsync(guildId, category.DiscordCategoryId, cancellationToken).ConfigureAwait(false);
        }

        await store.DeleteScopeAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
    }
}
```

- [ ] **Step 5: Run tests + build**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
then `dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(workspace): teardown service (RemoveServer / ResetGuild)"
```

---

## Task 19: Cross-cutting wiring — event, options, and the interaction-module seam

The foundation's `DiscordBotService` only scans its own assembly for interaction modules. This task adds
a DI seam so feature assemblies (Workspace) contribute their modules, plus the `ServerRegisteredEvent`
contract and `WorkspaceOptions`. Verified by build (the modules/hosted service that consume these arrive
in later tasks).

**Files:**

- Create: `src/RustPlusBot.Abstractions/Events/ServerRegisteredEvent.cs`
- Create: `src/RustPlusBot.Discord/InteractionModuleAssembly.cs`
- Create: `src/RustPlusBot.Features.Workspace/WorkspaceOptions.cs`
- Modify: `src/RustPlusBot.Discord/DiscordBotService.cs`

- [ ] **Step 1: Add the event contract**

`src/RustPlusBot.Abstractions/Events/ServerRegisteredEvent.cs`:

```csharp
namespace RustPlusBot.Abstractions.Events;

/// <summary>Published when a Rust server is registered to a guild (stub trigger in 1a; FCM pairing in 1b).</summary>
/// <param name="GuildId">The owning guild snowflake.</param>
/// <param name="ServerId">The registered server's id.</param>
public sealed record ServerRegisteredEvent(ulong GuildId, Guid ServerId);
```

- [ ] **Step 2: Add the interaction-module assembly marker**

`src/RustPlusBot.Discord/InteractionModuleAssembly.cs`:

```csharp
using System.Reflection;

namespace RustPlusBot.Discord;

/// <summary>Registers an assembly whose Discord interaction modules should be loaded at startup.</summary>
/// <param name="Assembly">The assembly to scan for interaction modules.</param>
public sealed record InteractionModuleAssembly(Assembly Assembly);
```

- [ ] **Step 3: Load contributed module assemblies in `DiscordBotService`**

In `src/RustPlusBot.Discord/DiscordBotService.cs`, add `IEnumerable<InteractionModuleAssembly> moduleAssemblies`
to the primary constructor parameter list (after `IServiceProvider services`), and change the module-load
line in `StartAsync` from:

```csharp
        await interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services).ConfigureAwait(false);
```

to:

```csharp
        await interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), services).ConfigureAwait(false);
        foreach (var moduleAssembly in moduleAssemblies)
        {
            await interactions.AddModulesAsync(moduleAssembly.Assembly, services).ConfigureAwait(false);
        }
```

(When nothing is registered, the `IEnumerable` is empty and behavior is unchanged.)

- [ ] **Step 4: Add the options class**

`src/RustPlusBot.Features.Workspace/WorkspaceOptions.cs`:

```csharp
namespace RustPlusBot.Features.Workspace;

/// <summary>Workspace feature configuration, bound from the "Workspace" config section.</summary>
public sealed class WorkspaceOptions
{
    /// <summary>Enables dangerous developer commands (/workspace reset, /workspace simulate-server).</summary>
    public bool EnableDangerCommands { get; set; }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build RustPlusBot.slnx`
Expected: 0/0.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: ServerRegisteredEvent, WorkspaceOptions, and interaction-module assembly seam"
```

---

## Task 20: `DiscordWorkspaceGateway` (real Discord.Net implementation)

Not unit-tested (it touches the live gateway/REST); validated by the manual smoke checklist in Task 25.
Keep it a thin, faithful mapping of the `IWorkspaceGateway` primitives.

> **API note for the implementer:** these calls target Discord.Net 3.20. If any signature differs in the
> installed version, adjust the call while preserving the exact behavior described by the interface XML docs.

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Gateway/DiscordWorkspaceGateway.cs`

- [ ] **Step 1: Implement the gateway**

`src/RustPlusBot.Features.Workspace/Gateway/DiscordWorkspaceGateway.cs`:

```csharp
using Discord;
using Discord.WebSocket;
using RustPlusBot.Features.Workspace.Registry;

namespace RustPlusBot.Features.Workspace.Gateway;

/// <summary>Discord.Net-backed <see cref="IWorkspaceGateway"/>.</summary>
/// <param name="client">The socket client (cache for existence checks; REST for mutations).</param>
internal sealed class DiscordWorkspaceGateway(DiscordSocketClient client) : IWorkspaceGateway
{
    /// <inheritdoc />
    public bool CategoryExists(ulong guildId, ulong categoryId) =>
        client.GetGuild(guildId)?.GetCategoryChannel(categoryId) is not null;

    /// <inheritdoc />
    public Task<ulong?> FindCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken)
    {
        var match = client.GetGuild(guildId)?.CategoryChannels
            .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match?.Id);
    }

    /// <inheritdoc />
    public async Task<ulong> CreateCategoryAsync(ulong guildId, string name, CancellationToken cancellationToken)
    {
        var guild = GetGuild(guildId);
        var category = await guild.CreateCategoryChannelAsync(name).ConfigureAwait(false);
        return category.Id;
    }

    /// <inheritdoc />
    public bool ChannelExists(ulong guildId, ulong channelId) =>
        client.GetGuild(guildId)?.GetTextChannel(channelId) is not null;

    /// <inheritdoc />
    public Task<ulong?> FindChannelAsync(ulong guildId, ulong categoryId, string name, CancellationToken cancellationToken)
    {
        var match = client.GetGuild(guildId)?.TextChannels
            .FirstOrDefault(c => c.CategoryId == categoryId && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(match?.Id);
    }

    /// <inheritdoc />
    public async Task<ulong> CreateChannelAsync(ulong guildId, ulong categoryId, string name, ChannelPermissionProfile profile, CancellationToken cancellationToken)
    {
        var guild = GetGuild(guildId);
        var channel = await guild.CreateTextChannelAsync(name, props => props.CategoryId = categoryId).ConfigureAwait(false);
        await ApplyOverwritesAsync(guild, channel, profile).ConfigureAwait(false);
        return channel.Id;
    }

    /// <inheritdoc />
    public async Task ApplyChannelSettingsAsync(ulong guildId, ulong channelId, ulong categoryId, string name, ChannelPermissionProfile profile, CancellationToken cancellationToken)
    {
        var guild = client.GetGuild(guildId);
        var channel = guild?.GetTextChannel(channelId);
        if (guild is null || channel is null)
        {
            return;
        }

        if (channel.CategoryId != categoryId || !string.Equals(channel.Name, name, StringComparison.Ordinal))
        {
            await channel.ModifyAsync(props =>
            {
                props.CategoryId = categoryId;
                props.Name = name;
            }).ConfigureAwait(false);
        }

        await ApplyOverwritesAsync(guild, channel, profile).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> MessageExistsAsync(ulong guildId, ulong channelId, ulong messageId, CancellationToken cancellationToken)
    {
        var channel = client.GetGuild(guildId)?.GetTextChannel(channelId);
        if (channel is null)
        {
            return false;
        }

        var message = await channel.GetMessageAsync(messageId).ConfigureAwait(false);
        return message is not null;
    }

    /// <inheritdoc />
    public async Task<ulong> PostMessageAsync(ulong guildId, ulong channelId, MessagePayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var channel = client.GetGuild(guildId)?.GetTextChannel(channelId)
            ?? throw new InvalidOperationException($"Channel {channelId} not found in guild {guildId}.");
        var message = await channel.SendMessageAsync(text: payload.Text, embed: payload.Embed, components: payload.Components).ConfigureAwait(false);
        return message.Id;
    }

    /// <inheritdoc />
    public async Task EditMessageAsync(ulong guildId, ulong channelId, ulong messageId, MessagePayload payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var channel = client.GetGuild(guildId)?.GetTextChannel(channelId);
        if (channel is null)
        {
            return;
        }

        await channel.ModifyMessageAsync(messageId, props =>
        {
            props.Content = payload.Text;
            props.Embed = payload.Embed;
            props.Components = payload.Components;
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteChannelAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken)
    {
        var channel = client.GetGuild(guildId)?.GetChannel(channelId);
        if (channel is not null)
        {
            await channel.DeleteAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task DeleteCategoryAsync(ulong guildId, ulong categoryId, CancellationToken cancellationToken)
    {
        var category = client.GetGuild(guildId)?.GetCategoryChannel(categoryId);
        if (category is not null)
        {
            await category.DeleteAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetMissingBotPermissions(ulong guildId)
    {
        var guild = client.GetGuild(guildId);
        if (guild is null)
        {
            return ["Guild not available to the bot"];
        }

        var permissions = guild.CurrentUser.GuildPermissions;
        var missing = new List<string>();
        if (!permissions.ManageChannels) { missing.Add("Manage Channels"); }
        if (!permissions.ManageRoles) { missing.Add("Manage Roles"); }
        if (!permissions.SendMessages) { missing.Add("Send Messages"); }
        if (!permissions.EmbedLinks) { missing.Add("Embed Links"); }
        if (!permissions.ManageMessages) { missing.Add("Manage Messages"); }
        if (!permissions.ViewChannel) { missing.Add("View Channels"); }
        return missing;
    }

    private SocketGuild GetGuild(ulong guildId) =>
        client.GetGuild(guildId) ?? throw new InvalidOperationException($"Guild {guildId} not available to the bot.");

    private static Task ApplyOverwritesAsync(SocketGuild guild, ITextChannel channel, ChannelPermissionProfile profile)
    {
        var send = profile == ChannelPermissionProfile.Interactive ? PermValue.Allow : PermValue.Deny;
        var overwrite = new OverwritePermissions(viewChannel: PermValue.Allow, sendMessages: send);
        return channel.AddPermissionOverwriteAsync(guild.EveryoneRole, overwrite);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build RustPlusBot.slnx`
Expected: 0/0. (If a Discord.Net signature differs, adjust per the API note; do not change behavior.)

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(workspace): Discord.Net implementation of IWorkspaceGateway"
```

---

## Task 21: `AddWorkspace` DI registration + composition test

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs`

- [ ] **Step 1: Write the failing composition test**

`tests/RustPlusBot.Features.Workspace.Tests/WorkspaceRegistrationTests.cs`:

```csharp
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Features.Workspace;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence;

namespace RustPlusBot.Features.Workspace.Tests;

public sealed class WorkspaceRegistrationTests
{
    [Fact]
    public void ReconcilerAndTeardown_ResolveFromScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new DiscordSocketClient());
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IEventBus, InMemoryEventBus>();
        services.AddLogging();
        services.AddBotPersistence("DataSource=:memory:");
        services.AddWorkspace();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IWorkspaceTeardownService>());
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile — `AddWorkspace` missing.

- [ ] **Step 3: Implement `AddWorkspace`**

`src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Discord;
using RustPlusBot.Features.Workspace.Gateway;
using RustPlusBot.Features.Workspace.Localization;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Specs;
using RustPlusBot.Features.Workspace.Teardown;

namespace RustPlusBot.Features.Workspace;

/// <summary>DI registration for the workspace provisioning feature.</summary>
public static class WorkspaceServiceCollectionExtensions
{
    /// <summary>Registers the registry, gateway, reconciler, renderers, teardown, and module seam.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddWorkspace(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Spec registry (stateless singletons).
        services.AddSingleton<IChannelSpecProvider, GlobalWorkspaceSpecProvider>();
        services.AddSingleton<IMessageSpecProvider, GlobalWorkspaceSpecProvider>();
        services.AddSingleton<IChannelSpecProvider, ServerWorkspaceSpecProvider>();
        services.AddSingleton<IMessageSpecProvider, ServerWorkspaceSpecProvider>();
        services.AddSingleton<IWorkspaceRegistry, WorkspaceRegistry>();

        // Localization.
        services.AddSingleton(LocalizationCatalog.Default);
        services.AddSingleton<ILocalizer, Localizer>();

        // Gateway + per-guild lock.
        services.AddSingleton<IWorkspaceGateway, DiscordWorkspaceGateway>();
        services.AddSingleton<IProvisioningLock, ProvisioningLock>();

        // Renderers (scoped: some query the DbContext via IServerService).
        services.AddScoped<IMessageRenderer, InformationMessageRenderer>();
        services.AddScoped<IMessageRenderer, SetupMessageRenderer>();
        services.AddScoped<IMessageRenderer, SettingsMessageRenderer>();
        services.AddScoped<IMessageRenderer, ServerInfoMessageRenderer>();

        // Reconciler + teardown (scoped).
        services.AddScoped<IWorkspaceReconciler, WorkspaceReconciler>();
        services.AddScoped<IWorkspaceTeardownService, WorkspaceTeardownService>();

        // Options (Host binds the "Workspace" section; default = danger commands off).
        services.AddOptions<WorkspaceOptions>();

        // Contribute this assembly's interaction modules to the Discord layer.
        services.AddSingleton(new InteractionModuleAssembly(typeof(WorkspaceServiceCollectionExtensions).Assembly));

        // NOTE: the hosted service registration is added in Task 24:
        //   services.AddHostedService<WorkspaceHostedService>();

        return services;
    }
}
```

- [ ] **Step 4: Run tests + build**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
then `dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0. (`ValidateScopes = true` proves no captive-dependency lifetime bugs.)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(workspace): AddWorkspace DI registration + composition test"
```

---

## Task 22: `/setup` command module

Interaction modules are `public` (the InteractionService instantiates them) and reach services through a
per-interaction DI scope, mirroring the foundation's removed `BindModule`/`ServerModule`. They are
discovered via the `InteractionModuleAssembly` seam (Task 19/21), not registered individually. Module
behavior is verified by the manual smoke checklist in Task 25 (Discord context is impractical to unit
test); the services they call are already covered.

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Modules/SetupModule.cs`

- [ ] **Step 1: Implement the module**

`src/RustPlusBot.Features.Workspace/Modules/SetupModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Modules;

/// <summary>Provisions the bot's Discord workspace for the current guild.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class SetupModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Reconciles the global workspace and every known server's workspace.</summary>
    [SlashCommand("setup", "Provision the bot's channels for this server")]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task SetupAsync()
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
            var result = await reconciler.ReconcileGlobalAsync(Context.Guild.Id).ConfigureAwait(false);
            if (result.Status == ReconcileStatus.MissingPermissions)
            {
                await FollowupAsync(
                    $"I'm missing required permissions: {string.Join(", ", result.MissingPermissions)}.",
                    ephemeral: true).ConfigureAwait(false);
                return;
            }

            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            foreach (var server in await servers.ListAsync(Context.Guild.Id).ConfigureAwait(false))
            {
                await reconciler.ReconcileServerAsync(Context.Guild.Id, server.Id).ConfigureAwait(false);
            }

            await FollowupAsync("Workspace is ready.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build RustPlusBot.slnx`
Expected: 0/0.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(workspace): /setup command module"
```

---

## Task 23: Settings select handler + `/workspace` admin/dev commands

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Modules/SettingsComponentModule.cs`
- Create: `src/RustPlusBot.Features.Workspace/Modules/WorkspaceAdminModule.cs`

- [ ] **Step 1: Implement the settings select handler**

`src/RustPlusBot.Features.Workspace/Modules/SettingsComponentModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Workspace.Messages;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Modules;

/// <summary>Handles the language select menu in the #settings channel.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class SettingsComponentModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Persists the chosen culture and re-renders the workspace in the new language.</summary>
    /// <param name="selectedValues">The selected culture codes (expects exactly one).</param>
    [ComponentInteraction(SettingsMessageRenderer.LanguageSelectId)]
    [RequireUserPermission(GuildPermission.ManageGuild)]
    public async Task SetCultureAsync(string[] selectedValues)
    {
        ArgumentNullException.ThrowIfNull(selectedValues);
        if (Context.Guild is null)
        {
            await RespondAsync("This control must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var culture = selectedValues.Length > 0 ? selectedValues[0] : "en";
        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            await store.SetCultureAsync(Context.Guild.Id, culture).ConfigureAwait(false);

            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            await reconciler.ReconcileGlobalAsync(Context.Guild.Id).ConfigureAwait(false);

            await FollowupAsync($"Language set to `{culture}`.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }
}
```

- [ ] **Step 2: Implement the admin/dev module**

`src/RustPlusBot.Features.Workspace/Modules/WorkspaceAdminModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Teardown;
using RustPlusBot.Persistence.Servers;

namespace RustPlusBot.Features.Workspace.Modules;

/// <summary>Administrative and developer commands for the workspace.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
/// <param name="options">Workspace options (gates the dangerous commands).</param>
/// <param name="eventBus">Used to publish the stub <see cref="ServerRegisteredEvent"/>.</param>
[Group("workspace", "Workspace administration")]
[RequireUserPermission(GuildPermission.ManageGuild)]
public sealed class WorkspaceAdminModule(
    IServiceScopeFactory scopeFactory,
    IOptions<WorkspaceOptions> options,
    IEventBus eventBus) : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Custom id for the reset confirmation button.</summary>
    public const string ConfirmResetId = "workspace:reset:confirm";

    /// <summary>Prompts to delete the entire provisioned workspace (dev-gated).</summary>
    [SlashCommand("reset", "Delete ALL provisioned channels and records for this server (dangerous)")]
    public async Task ResetAsync()
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        var components = new ComponentBuilder()
            .WithButton("Confirm reset", ConfirmResetId, ButtonStyle.Danger)
            .Build();
        await RespondAsync(
            "This deletes every channel and category the bot provisioned here. Confirm?",
            components: components, ephemeral: true).ConfigureAwait(false);
    }

    /// <summary>Executes the workspace reset after confirmation.</summary>
    [ComponentInteraction(ConfirmResetId)]
    public async Task ConfirmResetAsync()
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
            await teardown.ResetGuildAsync(Context.Guild.Id).ConfigureAwait(false);
            await FollowupAsync("Workspace reset.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>Dev: registers a fake server and publishes a ServerRegisteredEvent to test provisioning.</summary>
    /// <param name="name">Server display name.</param>
    /// <param name="ip">Server host or ip.</param>
    /// <param name="port">Rust+ app port.</param>
    [SlashCommand("simulate-server", "Dev: register a fake server to test provisioning")]
    public async Task SimulateServerAsync(string name, string ip, int port)
    {
        if (!await EnsureEnabledAsync().ConfigureAwait(false))
        {
            return;
        }

        await DeferAsync(ephemeral: true).ConfigureAwait(false);
        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var servers = scope.ServiceProvider.GetRequiredService<IServerService>();
            var server = await servers.AddAsync(Context.Guild.Id, Context.User.Id, name, ip, port).ConfigureAwait(false);
            await eventBus.PublishAsync(new ServerRegisteredEvent(Context.Guild.Id, server.Id)).ConfigureAwait(false);
            await FollowupAsync($"Registered **{name}** and published ServerRegisteredEvent.", ephemeral: true).ConfigureAwait(false);
        }
        finally
        {
            await scope.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<bool> EnsureEnabledAsync()
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        if (!options.Value.EnableDangerCommands)
        {
            await RespondAsync("Developer commands are disabled.", ephemeral: true).ConfigureAwait(false);
            return false;
        }

        return true;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build RustPlusBot.slnx`
Expected: 0/0.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(workspace): settings language selector + /workspace reset & simulate-server"
```

---

## Task 24: Self-healing — `HealGuildAsync` + `WorkspaceHostedService`

The reconciler is refactored to expose lock-free cores so a single locked operation can heal a whole
guild without re-entering the (non-reentrant) per-guild semaphore. `HealGuildAsync` only converges an
**already-provisioned** workspace, so it cannot resurrect one cleared by `/workspace reset`. The hosted
service runs it on startup and on `ChannelDestroyed`, and consumes `ServerRegisteredEvent`.

**Files:**

- Modify: `src/RustPlusBot.Features.Workspace/Reconciler/IWorkspaceReconciler.cs`
- Modify: `src/RustPlusBot.Features.Workspace/Reconciler/WorkspaceReconciler.cs`
- Create: `src/RustPlusBot.Features.Workspace/Hosting/WorkspaceHostedService.cs`
- Modify: `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerHealTests.cs`

- [ ] **Step 1: Write the failing heal tests**

`tests/RustPlusBot.Features.Workspace.Tests/Reconciler/WorkspaceReconcilerHealTests.cs`:

```csharp
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Features.Workspace.Registry;
using RustPlusBot.Features.Workspace.Teardown;

namespace RustPlusBot.Features.Workspace.Tests.Reconciler;

public sealed class WorkspaceReconcilerHealTests
{
    [Fact]
    public async Task HealGuild_RecreatesDeletedChannel_WhenProvisioned()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var channel = (await harness.Store.GetChannelsAsync(1, null))[0];
        harness.Gateway.ExternallyDeleteChannel(channel.DiscordChannelId);

        await sut.HealGuildAsync(1);

        Assert.True(harness.Gateway.ChannelExists(1, (await harness.Store.GetChannelsAsync(1, null))[0].DiscordChannelId));
    }

    [Fact]
    public async Task HealGuild_DoesNotResurrect_AfterReset()
    {
        var harness = new ReconcilerHarness()
            .WithChannel(WorkspaceScope.Global, "information", "channel.information.name", 0);
        var sut = harness.Build();
        await sut.ReconcileGlobalAsync(1);

        var teardown = new WorkspaceTeardownService(harness.Gateway, harness.Store, new ProvisioningLock());
        await teardown.ResetGuildAsync(1);

        await sut.HealGuildAsync(1);

        Assert.Empty(harness.Gateway.CategoryIds);
        Assert.Empty(harness.Gateway.ChannelIds);
        Assert.Null(await harness.Store.GetCategoryAsync(1, null));
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: FAIL to compile — `HealGuildAsync` missing.

- [ ] **Step 3: Add `HealGuildAsync` to the interface**

In `src/RustPlusBot.Features.Workspace/Reconciler/IWorkspaceReconciler.cs` add:

```csharp
    /// <summary>Converges an already-provisioned guild (global + all servers). No-op if not provisioned.</summary>
    Task HealGuildAsync(ulong guildId, CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Refactor the reconciler to lock-free cores + heal**

In `src/RustPlusBot.Features.Workspace/Reconciler/WorkspaceReconciler.cs`, **replace** the two public
methods `ReconcileGlobalAsync` and `ReconcileServerAsync` with the following members (the private
`ReconcileScopeAsync`, `EnsureCategoryAsync`, `EnsureChannelsAsync`, `EnsureMessagesAsync` are unchanged):

```csharp
    /// <inheritdoc />
    public async Task<ReconcileResult> ReconcileGlobalAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);
        var missing = gateway.GetMissingBotPermissions(guildId);
        if (missing.Count > 0)
        {
            return ReconcileResult.Missing(missing);
        }

        await ReconcileGlobalCoreAsync(guildId, cancellationToken).ConfigureAwait(false);
        return ReconcileResult.Provisioned;
    }

    /// <inheritdoc />
    public async Task<ReconcileResult> ReconcileServerAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);
        var missing = gateway.GetMissingBotPermissions(guildId);
        if (missing.Count > 0)
        {
            return ReconcileResult.Missing(missing);
        }

        await ReconcileServerCoreAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        return ReconcileResult.Provisioned;
    }

    /// <inheritdoc />
    public async Task HealGuildAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        using var handle = await provisioningLock.AcquireAsync(guildId, cancellationToken).ConfigureAwait(false);

        // Only heal an already-provisioned workspace; never resurrect one cleared by reset.
        if (await store.GetCategoryAsync(guildId, null, cancellationToken).ConfigureAwait(false) is null)
        {
            return;
        }

        if (gateway.GetMissingBotPermissions(guildId).Count > 0)
        {
            return;
        }

        await ReconcileGlobalCoreAsync(guildId, cancellationToken).ConfigureAwait(false);
        foreach (var server in await servers.ListAsync(guildId, cancellationToken).ConfigureAwait(false))
        {
            await ReconcileServerCoreAsync(guildId, server.Id, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ReconcileGlobalCoreAsync(ulong guildId, CancellationToken cancellationToken)
    {
        var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        var categoryName = localizer.Get("category.global.name", culture);
        await ReconcileScopeAsync(guildId, null, categoryName, culture, WorkspaceScope.Global, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReconcileServerCoreAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        var server = await servers.GetAsync(guildId, serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            logger.LogWarning("ReconcileServer skipped: server {ServerId} not found in guild {GuildId}.", serverId, guildId);
            return;
        }

        var culture = await store.GetCultureAsync(guildId, cancellationToken).ConfigureAwait(false);
        await ReconcileScopeAsync(guildId, serverId, server.Name, culture, WorkspaceScope.PerServer, cancellationToken).ConfigureAwait(false);
    }
```

- [ ] **Step 5: Run the heal tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests/RustPlusBot.Features.Workspace.Tests.csproj`
Expected: PASS (heal tests green; all earlier reconciler tests still green — the public method contracts
are unchanged).

- [ ] **Step 6: Implement the hosted service**

`src/RustPlusBot.Features.Workspace/Hosting/WorkspaceHostedService.cs`:

```csharp
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Workspace.Reconciler;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Hosting;

/// <summary>Runs startup reconcile, self-heals on channel deletion, and reacts to server registration.</summary>
/// <param name="client">The socket client (for Ready and ChannelDestroyed).</param>
/// <param name="eventBus">The in-process event bus.</param>
/// <param name="scopeFactory">Creates scopes for the scoped reconciler/store.</param>
/// <param name="logger">The logger.</param>
internal sealed class WorkspaceHostedService(
    DiscordSocketClient client,
    IEventBus eventBus,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkspaceHostedService> logger) : IHostedService
{
    private readonly CancellationTokenSource _cts = new();
    private Task? _eventLoop;
    private bool _startupDone;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        client.Ready += OnReadyAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
        _eventLoop = Task.Run(() => ConsumeServerRegisteredAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        client.Ready -= OnReadyAsync;
        client.ChannelDestroyed -= OnChannelDestroyedAsync;
        await _cts.CancelAsync().ConfigureAwait(false);
        if (_eventLoop is not null)
        {
            try
            {
                await _eventLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }

        _cts.Dispose();
    }

    private async Task OnReadyAsync()
    {
        if (_startupDone)
        {
            return;
        }

        _startupDone = true;
        await using var scope = scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
        var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
        foreach (var guildId in await store.GetProvisionedGuildIdsAsync().ConfigureAwait(false))
        {
            await reconciler.HealGuildAsync(guildId).ConfigureAwait(false);
        }
    }

    private async Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        if (channel is not SocketGuildChannel guildChannel)
        {
            return;
        }

        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
            await reconciler.HealGuildAsync(guildChannel.Guild.Id).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Self-heal failed for guild {GuildId}.", guildChannel.Guild.Id);
        }
    }

    private async Task ConsumeServerRegisteredAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var registered in eventBus.SubscribeAsync<ServerRegisteredEvent>(cancellationToken).ConfigureAwait(false))
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var reconciler = scope.ServiceProvider.GetRequiredService<IWorkspaceReconciler>();
                await reconciler.ReconcileServerAsync(registered.GuildId, registered.ServerId, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ServerRegistered consumer faulted.");
        }
    }
}
```

- [ ] **Step 7: Register the hosted service**

In `src/RustPlusBot.Features.Workspace/WorkspaceServiceCollectionExtensions.cs`, replace the
`// NOTE: the hosted service registration is added in Task 24` comment block with:

```csharp
        services.AddHostedService<Hosting.WorkspaceHostedService>();
```

- [ ] **Step 8: Run tests + build**

Run: `dotnet test RustPlusBot.slnx` then `dotnet build RustPlusBot.slnx`
Expected: PASS; 0/0. The composition test from Task 21 now also constructs the hosted service registration
graph correctly.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(workspace): HealGuild self-heal + hosted service (startup, ChannelDestroyed, ServerRegistered)"
```

---

## Task 25: Host wiring, configuration, and final verification

**Files:**

- Modify: `src/RustPlusBot.Host/RustPlusBot.Host.csproj`
- Modify: `src/RustPlusBot.Host/Program.cs`
- Modify: `src/RustPlusBot.Host/appsettings.json`
- Modify: `src/RustPlusBot.Host/appsettings.Development.json`
- Modify: `docs/development/running-locally.md`

- [ ] **Step 1: Reference the feature project from the Host**

In `src/RustPlusBot.Host/RustPlusBot.Host.csproj`, add to the `ProjectReference` ItemGroup:

```xml
    <ProjectReference Include="..\RustPlusBot.Features.Workspace\RustPlusBot.Features.Workspace.csproj" />
```

- [ ] **Step 2: Wire `AddWorkspace` + options in `Program.cs`**

In `src/RustPlusBot.Host/Program.cs`, add `using RustPlusBot.Features.Workspace;` and, after the
`builder.Services.AddDiscordBot();` line:

```csharp
builder.Services.AddOptions<WorkspaceOptions>()
    .Bind(builder.Configuration.GetSection("Workspace"));
builder.Services.AddWorkspace();
```

- [ ] **Step 3: Add the Workspace config sections**

In `src/RustPlusBot.Host/appsettings.json`, add a top-level `"Workspace"` section:

```json
  "Workspace": {
    "EnableDangerCommands": false
  }
```

In `src/RustPlusBot.Host/appsettings.Development.json`, add:

```json
  "Workspace": {
    "EnableDangerCommands": true
  }
```

(Match the existing JSON structure/commas in each file.)

- [ ] **Step 4: Document the new flow**

In `docs/development/running-locally.md`, replace any mention of `/server` and `/bind` with the new flow:

```markdown
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
```

- [ ] **Step 5: Full build + test**

Run: `dotnet build RustPlusBot.slnx` then `dotnet test RustPlusBot.slnx`
Expected: build 0/0 under the analyzers; **all** tests pass.

- [ ] **Step 6: Manual smoke test (interactive — not automated)**

With a real bot token in `appsettings.Development.json` (`Discord:Token`) and the bot invited with the
permissions above, run `dotnet run --project src/RustPlusBot.Host` and verify:

1. `/setup` → a **RustPlusBot** category appears with `#information`/`#setup`/`#settings`; members can read
   but not type; embeds render.
2. Run `/setup` again → no duplicate channels/categories/messages.
3. Delete `#information` in Discord → it reappears within a moment (self-heal).
4. Pick **Français** in `#settings` → the embeds re-render in French.
5. `/workspace simulate-server name:Test ip:1.1.1.1 port:28015` → a **Test** category with `#info` appears,
   showing the static identity embed.
6. `/workspace reset` → confirm → all bot categories/channels are deleted and do **not** reappear.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat(host): wire AddWorkspace, config flag, and update running-locally docs"
```

- [ ] **Step 8: Open the PR (optional, when ready)**

```bash
git push -u origin feat/workspace-provisioning
gh pr create --base develop --title "Subsystem 1a: Discord workspace provisioning & configuration" \
  --body "Implements the workspace provisioning reconciler, settings/i18n surface, and per-server category provisioning from a stub ServerRegistered trigger. Removes /server and /bind. See docs/superpowers/specs/2026-06-14-rustplusbot-1a-workspace-provisioning-design.md."
```

---

## Done

Subsystem 1a is complete: the bot provisions and self-heals its own Discord workspace, configuration
lives in in-channel components, and per-server categories are created from the `ServerRegisteredEvent`
stub — ready for **1b** (credentials + FCM pairing) to fire the real event and add the `#setup` connect
button.
