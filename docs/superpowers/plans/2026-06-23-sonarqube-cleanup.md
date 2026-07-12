# SonarQube Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate the 82 SonarQube maintainability smells and cut duplication density (8.4% → target <3%) by extracting the genuine shared abstractions, sequenced low-risk → high-risk with the test suite green at every commit.

**Architecture:** Three structural extractions (shared `DictionaryLocalizer`, a generic `CachingChannelLocator`, a Discord-poster boilerplate helper) replace the per-feature copy-paste families; the remaining smells are fixed mechanically (auto-format, literal consts, namespace correction, parameter bundling, complexity extraction).

**Tech Stack:** .NET 10, C#, xUnit, Discord.Net, EF Core, SonarQube/SonarCloud, `dotnet format`.

## Global Constraints

- Target framework: **net10.0** (do not change).
- **Never** bump SixLabors.ImageSharp to v4 / Drawing to v3 (paid license hard-fails build).
- All code is `internal` with XML doc comments on public/internal members (existing convention).
- `docs/` is gitignored — plan/spec files are local-only, never `git add` them.
- No worktrees: work on `feat/sonar-cleanup` cut from `develop` in the main checkout.
- The full suite is **520 tests**; it must stay green after every commit.
- Use `dotnet build RustPlusBot.slnx` and `dotnet test RustPlusBot.slnx` for verification.
- `dotnet format` analyzer fixes must be scoped; do not let it reformat unrelated files.
- **ReSharper formatting is a CI gate.** CI runs `dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --no-build --verbosity=ERROR` and fails if it produces changes. **Every task MUST run the ReSharper cleanup (see the shared verification block below) and commit its result before pushing**, or CI rejects the PR. `jb` is the `jetbrains.resharper.globaltools` dotnet tool already in `.config/dotnet-tools.json`.
- The solution file is **`RustPlusBot.slnx`** (XML format) — there is no `.sln`.

### Shared verification block (run before EVERY commit step)

Each task's final verification is this sequence (the per-task steps reference it as "run the shared verification block"):

```bash
# 1. restore the local tools once per session if not already
dotnet tool restore
# 2. apply the project's enforced ReSharper formatting (must run AFTER a build so analysis is accurate)
dotnet build RustPlusBot.slnx
dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --verbosity=ERROR
# 3. rebuild + full test after cleanup (cleanup can re-order usings / reformat)
dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx
```

Expected: `jb cleanupcode` exits 0; the build is clean; all 520 tests pass. Stage cleanup's edits together with the task's own edits in the same commit so the tree matches what CI will see.

---

## Task 0: Branch setup

**Files:** none (git only).

- [ ] **Step 1: Cut the branch from develop**

```bash
git switch develop && git pull --ff-only
git switch -c feat/sonar-cleanup
```

- [ ] **Step 2: Confirm baseline green**

Run: `dotnet test RustPlusBot.slnx`
Expected: all 520 tests pass.

---

## Task 1: Exclude EF migrations & obj from duplication analysis

**Files:**

- Modify: `.github/workflows/Sonar.yml` (add `sonar.cpd.exclusions` + verify `sonar.exclusions`)

**Interfaces:**

- Produces: a Sonar scanner config that stops counting auto-generated EF migrations and `obj/` toward duplication/smells.

- [ ] **Step 1: Inspect the current scanner invocation**

Run: `grep -nE "sonar\.|dotnet-sonarscanner|/d:|/k:" .github/workflows/Sonar.yml`
Expected: see the `/k:` project key and any existing `/d:sonar.*` args.

- [ ] **Step 2: Add CPD + smell exclusions to the begin step**

Add these analysis properties to the `dotnet-sonarscanner begin` arguments (match the existing `/d:` style already in the file):

```
/d:sonar.cpd.exclusions="**/Migrations/*.cs"
/d:sonar.exclusions="**/Migrations/*.cs,**/obj/**,**/bin/**"
```

If a `/d:sonar.exclusions=` already exists, append the migration/obj globs to it rather than adding a second one (last-wins would clobber).

- [ ] **Step 3: Validate the workflow YAML parses**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/Sonar.yml')); print('ok')"`
Expected: `ok`

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/Sonar.yml
git commit -m "chore(sonar): exclude EF migrations and obj from analysis

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 2: Zero-risk analyzer auto-fixes

**Files:**

- Modify: the ~60 files flagged by `IDE0007/0028/0042/0045/0066/0230/0290/0300/0305` (see spec findings; half are under `tests/`).

**Interfaces:**

- Produces: no API change — style-only edits. Later tasks are unaffected.

- [ ] **Step 1: Run the analyzer formatter across the solution**

Run: `dotnet format RustPlusBot.slnx --severity info --diagnostics IDE0007 IDE0028 IDE0042 IDE0045 IDE0066 IDE0230 IDE0290 IDE0300 IDE0305`
Expected: edits applied; exit 0.

- [ ] **Step 2: Review the diff is style-only**

Run: `git diff --stat`
Expected: only the flagged files changed; no logic edits. Spot-check 3 files with `git diff <file>` to confirm collection-initializer/`var`/switch-expression rewrites only.

- [ ] **Step 3: Build + full test run**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: build succeeds, 520 tests pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "style: apply analyzer auto-fixes (collection init, var, switch expr, UTF-8 literals)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 3: Extract repeated-literal constants (S1192)

**Files:**

- Modify: `src/RustPlusBot.Features.Alarms/Modules/AlarmComponentModule.cs` (line ~26, "That control wasn't valid." ×6)
- Modify: `src/RustPlusBot.Features.Switches/Modules/SwitchComponentModule.cs` (line ~29, same literal ×6)
- Modify: `src/RustPlusBot.Features.Pairing/Modules/CredentialModule.cs` (line ~27, "This control must be used in a server." ×4)
- Modify: `src/RustPlusBot.Features.Workspace/Localization/LocalizationCatalog.cs` (line ~16, "RustPlusBot" ×4)

**Interfaces:**

- Produces: no API change. Constants are `private const`.

- [ ] **Step 1: Add the constant to each module and replace usages**

In each file, add a `private const string` at the top of the class and replace the inline literals. Example for `AlarmComponentModule.cs`:

```csharp
private const string InvalidControlMessage = "That control wasn't valid.";
```

Then replace each occurrence of the string literal with `InvalidControlMessage`. Repeat per file with an appropriately named constant:

- `SwitchComponentModule.cs` → `InvalidControlMessage = "That control wasn't valid."`
- `CredentialModule.cs` → `ServerOnlyMessage = "This control must be used in a server."`
- `LocalizationCatalog.cs` → `ProductName = "RustPlusBot"`

- [ ] **Step 2: Build + test**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: build succeeds, 520 tests pass.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor: extract repeated string literals into constants (S1192)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 4: Correct Abstractions namespace (IDE0130) — high blast radius

**Files:**

- Modify (declarations): 8 files in `src/RustPlusBot.Abstractions/Connections/` — `IRustServerQuery.cs`, `MapDimensions.cs`, `MapMarkerSnapshot.cs`, `MarkerKind.cs`, `MonumentSnapshot.cs`, `ServerInfoSnapshot.cs`, `ServerTimeSnapshot.cs`, `TeamInfoSnapshot.cs`. Each currently declares `namespace RustPlusBot.Features.Connections.Listening;` → change to `namespace RustPlusBot.Abstractions.Connections;`.
- Modify (consumers): ~85 files across `src/` and `tests/` that have `using RustPlusBot.Features.Connections.Listening;`.

**WARNING:** This `using` is shared by two real namespaces — the relocated Abstractions types AND types that genuinely live in `RustPlusBot.Features.Connections.Listening` (e.g. `TeamStateTracker`, `TeamMemberSnapshot`, the socket source). A blind find/replace will break files that need the *real* Connections.Listening namespace. The safe approach: **add** the new `using` everywhere the moved types are referenced, then let the compiler tell you which files no longer need the old `using`. Do NOT delete the old `using` blindly.

**Interfaces:**

- Produces: `RustPlusBot.Abstractions.Connections.{IRustServerQuery, ServerInfoSnapshot, ServerTimeSnapshot, MapDimensions, MapMarkerSnapshot, MonumentSnapshot, MarkerKind, TeamInfoSnapshot}`.

- [ ] **Step 1: Change the 8 namespace declarations**

In each of the 8 files above, replace the namespace line:

```csharp
namespace RustPlusBot.Features.Connections.Listening;
```

with:

```csharp
namespace RustPlusBot.Abstractions.Connections;
```

- [ ] **Step 2: Build to surface every break**

Run: `dotnet build RustPlusBot.slnx 2>&1 | grep -E "error CS0246|error CS0234" | sort -u`
Expected: a list of files/types that can no longer resolve the moved types.

- [ ] **Step 3: Add the new using to each broken file**

For every file in the Step 2 output, add `using RustPlusBot.Abstractions.Connections;` (alphabetically among the existing usings). Keep the existing `using RustPlusBot.Features.Connections.Listening;` if the file also uses real Connections.Listening types.

- [ ] **Step 4: Rebuild until clean**

Run: `dotnet build RustPlusBot.slnx`
Expected: build succeeds with 0 errors. Repeat Step 3 for any remaining unresolved files.

- [ ] **Step 5: Remove now-redundant usings**

Run: `dotnet format RustPlusBot.slnx --severity info --diagnostics IDE0005`
Expected: removes `using RustPlusBot.Features.Connections.Listening;` from files that no longer need it (IDE0005 = unnecessary using). Build again to confirm.

- [ ] **Step 6: Full test run**

Run: `dotnet test RustPlusBot.slnx`
Expected: 520 tests pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor: move Abstractions.Connections types to matching namespace (IDE0130)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 5: Extract shared localization into RustPlusBot.Localization (Core 1)

**Files:**

- Create: `src/RustPlusBot.Localization/RustPlusBot.Localization.csproj`
- Create: `src/RustPlusBot.Localization/ILocalizer.cs`
- Create: `src/RustPlusBot.Localization/DictionaryLocalizer.cs`
- Create: `tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj`
- Create: `tests/RustPlusBot.Localization.Tests/DictionaryLocalizerTests.cs`
- Modify: `RustPlusBot.slnx` (add both projects)
- Delete: `Localizer.cs`/`ILocalizer.cs` (Workspace), `SwitchLocalizer.cs`/`ISwitchLocalizer.cs`, `EventLocalizer.cs`/`IEventLocalizer.cs`, `PlayerLocalizer.cs`/`IPlayerLocalizer.cs`, `CommandLocalizer.cs`/`ICommandLocalizer.cs`, `AlarmLocalizer.cs`/`IAlarmLocalizer.cs`
- Modify (DI + project refs): `WorkspaceServiceCollectionExtensions.cs:32-33`, `CommandServiceCollectionExtensions.cs:26-27`, `EventServiceCollectionExtensions.cs:25-26`, `PlayerEventServiceCollectionExtensions.cs:19-20`, `SwitchServiceCollectionExtensions.cs:21-22`, `AlarmServiceCollectionExtensions.cs:21`, and each feature `.csproj`
- Modify: any consumer referencing `IXLocalizer` (relays, renderers, reconciler) to use `RustPlusBot.Localization.ILocalizer`
- Modify/Delete: feature-specific localizer test files (`SwitchLocalizerTests`, etc. if present) → retarget to the shared type

**Interfaces:**

- Produces:
  - `namespace RustPlusBot.Localization; public interface ILocalizer { string Get(string key, string culture); string Get(string key, string culture, params object[] args); }`
  - `public sealed class DictionaryLocalizer(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> strings) : ILocalizer` (English fallback, region normalization, invariant-culture format fallback).
- Consumes: each feature's existing `XLocalizationCatalog.Default.Strings` (shape `IReadOnlyDictionary<string, IReadOnlyDictionary<string,string>>`, already uniform across catalogs).

- [ ] **Step 1: Write the failing test (new test project)**

Create `tests/RustPlusBot.Localization.Tests/DictionaryLocalizerTests.cs`:

```csharp
using RustPlusBot.Localization;

namespace RustPlusBot.Localization.Tests;

public class DictionaryLocalizerTests
{
    private static DictionaryLocalizer Build() => new(
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["en"] = new Dictionary<string, string> { ["greet"] = "Hello {0}", ["bye"] = "Bye" },
            ["fr"] = new Dictionary<string, string> { ["greet"] = "Bonjour {0}" },
        });

    [Fact]
    public void Get_ReturnsCultureValue() => Assert.Equal("Bonjour {0}", Build().Get("greet", "fr"));

    [Fact]
    public void Get_NormalizesRegion() => Assert.Equal("Bonjour {0}", Build().Get("greet", "fr-FR"));

    [Fact]
    public void Get_FallsBackToEnglish() => Assert.Equal("Bye", Build().Get("bye", "fr"));

    [Fact]
    public void Get_ReturnsKeyWhenMissing() => Assert.Equal("nope", Build().Get("nope", "en"));

    [Fact]
    public void Get_FormatsArgs() => Assert.Equal("Hello world", Build().Get("greet", "en", "world"));

    [Fact]
    public void Get_BlankCulture_UsesEnglish() => Assert.Equal("Bye", Build().Get("bye", ""));
}
```

Create the test `.csproj` mirroring an existing test project (e.g. copy `tests/RustPlusBot.Features.Workspace.Tests/*.csproj` structure), referencing `src/RustPlusBot.Localization/RustPlusBot.Localization.csproj`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Localization.Tests`
Expected: FAIL — `RustPlusBot.Localization` / `DictionaryLocalizer` does not exist.

- [ ] **Step 3: Create the project, ILocalizer, and DictionaryLocalizer**

Create `src/RustPlusBot.Localization/RustPlusBot.Localization.csproj` (copy an existing leaf src `.csproj` like `RustPlusBot.Abstractions.csproj`; no extra package refs needed).

Create `src/RustPlusBot.Localization/ILocalizer.cs`:

```csharp
namespace RustPlusBot.Localization;

/// <summary>Resolves localized strings by key and BCP-47 culture, falling back to English.</summary>
public interface ILocalizer
{
    /// <summary>Gets the localized string for a key, or the key itself if not found.</summary>
    string Get(string key, string culture);

    /// <summary>Gets the localized, <see cref="string.Format(IFormatProvider, string, object?[])"/>-applied string.</summary>
    string Get(string key, string culture, params object[] args);
}
```

Create `src/RustPlusBot.Localization/DictionaryLocalizer.cs` (lift the body verbatim from the existing `Localizer.cs`, swapping the injected catalog for the dictionary):

```csharp
using System.Globalization;

namespace RustPlusBot.Localization;

/// <summary>Dictionary-backed <see cref="ILocalizer"/> with English fallback and region normalization.</summary>
/// <param name="strings">Culture → (key → value) string table.</param>
public sealed class DictionaryLocalizer(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> strings) : ILocalizer
{
    private const string FallbackCulture = "en";

    /// <inheritdoc />
    public string Get(string key, string culture)
    {
        var normalized = Normalize(culture);
        if (strings.TryGetValue(normalized, out var map) && map.TryGetValue(key, out var value))
        {
            return value;
        }

        if (strings.TryGetValue(FallbackCulture, out var fallback) &&
            fallback.TryGetValue(key, out var fallbackValue))
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

    private static CultureInfo ResolveFormatProvider(string culture)
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

Add both projects to the solution:

```bash
dotnet sln RustPlusBot.slnx add src/RustPlusBot.Localization/RustPlusBot.Localization.csproj
dotnet sln RustPlusBot.slnx add tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Localization.Tests`
Expected: 6 tests pass.

- [ ] **Step 5: Commit the shared primitive**

```bash
git add -A
git commit -m "feat(localization): add shared RustPlusBot.Localization (ILocalizer + DictionaryLocalizer)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

- [ ] **Step 6: Migrate each feature off its private localizer**

For each feature (Workspace, Commands, Events, Players, Switches, Alarms):

1. Add a project reference to `RustPlusBot.Localization` in the feature `.csproj`.
2. Delete the feature's `XLocalizer.cs` and `IXLocalizer.cs`.
3. Replace every `IXLocalizer` usage in that feature (relays, renderers, reconciler, modules) with `RustPlusBot.Localization.ILocalizer` (add the `using`).
4. Change the DI registration to construct the shared localizer from the catalog. Replace, e.g. in `WorkspaceServiceCollectionExtensions.cs:32-33`:

```csharp
services.AddSingleton(LocalizationCatalog.Default);
services.AddSingleton<RustPlusBot.Localization.ILocalizer>(
    sp => new RustPlusBot.Localization.DictionaryLocalizer(
        sp.GetRequiredService<LocalizationCatalog>().Strings));
```

Apply the equivalent at each registration site (`CommandServiceCollectionExtensions.cs:26-27`, `EventServiceCollectionExtensions.cs:25-26`, `PlayerEventServiceCollectionExtensions.cs:19-20`, `SwitchServiceCollectionExtensions.cs:21-22`, `AlarmServiceCollectionExtensions.cs:21`). Note Alarms already uses an instance form — keep it as `new DictionaryLocalizer(AlarmLocalizationCatalog.Default.Strings)`.

**CRITICAL — DI collision is real.** `Program.cs` composes ALL features into one `builder.Services` (`AddWorkspace`, `AddCommands`, `AddEvents`, `AddSwitches`, `AddAlarms` confirmed; verify `AddPlayerEvents`/Players wiring). A plain `AddSingleton<ILocalizer, DictionaryLocalizer>()` per feature would collide — last registration wins and every feature would resolve the *same* (wrong) localizer. The shared `ILocalizer` interface MUST NOT be registered as a bare singleton.

**Required approach:** do NOT register the shared `ILocalizer` open-typed. Instead, each feature's consumers (relay/renderer/reconciler) take the shared `RustPlusBot.Localization.ILocalizer` in their constructor, and the feature registers it via a factory bound to that feature's own catalog, scoped so only that feature's services receive it. Two safe options — pick the one matching the existing registration style:

1. **Keyed services** (.NET 8+): register `services.AddKeyedSingleton<ILocalizer>("workspace", (sp, _) => new DictionaryLocalizer(sp.GetRequiredService<LocalizationCatalog>().Strings));` and have each consumer use `[FromKeyedServices("workspace")]`. Cleanest if the codebase already uses keyed DI.
2. **Construct inline at the consumer's registration** (no shared `ILocalizer` in the container at all): when registering each feature's relay/renderer, build the localizer explicitly, e.g. `services.AddSingleton<WorkspaceReconciler>(sp => new WorkspaceReconciler(..., new DictionaryLocalizer(sp.GetRequiredService<LocalizationCatalog>().Strings), ...));`. Verbose but zero collision risk.

Verify via the existing per-feature registration tests (`WorkspaceRegistrationTests`, `CommandRegistrationTests`, `EventRegistrationTests`, `PlayerEventRegistrationTests`, `SwitchRegistrationTests`, `AlarmRegistrationTests`) that each feature still resolves and returns its OWN strings — add an assertion that resolves a known key unique to each feature's catalog if the registration tests don't already cover localized output.

- [ ] **Step 7: Retarget any feature localizer tests**

If files like `tests/.../SwitchLocalizerTests.cs` exist, point them at `DictionaryLocalizer` (or delete if fully covered by `DictionaryLocalizerTests`). Run `grep -rln "Localizer" tests` to find them.

- [ ] **Step 8: Build + full test run**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: build succeeds; all tests pass (count may shift slightly as feature localizer tests fold into the shared suite).

- [ ] **Step 9: Commit the migration**

```bash
git add -A
git commit -m "refactor(localization): replace 6 per-feature localizers with shared DictionaryLocalizer

Resolves the in-code 'consolidate into a shared project' TODO.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 6: Generic CachingChannelLocator (Core 2)

**Files:**

- Create: `src/RustPlusBot.Features.Workspace/Locating/CachingChannelLocator.cs`
- Modify: `EventChannelLocator.cs`, `MapChannelLocator.cs`, `SwitchChannelLocator.cs`, `AlarmChannelLocator.cs` → become thin subclasses/bindings over the shared base, each supplying only its `WorkspaceChannelKeys.X`
- Test: `tests/RustPlusBot.Features.Workspace.Tests/Locating/CachingChannelLocatorTests.cs` (new) + existing locator tests retained

**Interfaces:**

- Consumes: `IServiceScopeFactory`, `RustPlusBot.Abstractions.Time.IClock`, `RustPlusBot.Persistence.Workspace.IWorkspaceStore.GetChannelsByKeyAsync(string key, CancellationToken)`, `WorkspaceChannelKeys`.
- Produces: `internal abstract class CachingChannelLocator(IServiceScopeFactory scopeFactory, IClock clock, string channelKey)` exposing `Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken)` and implementing `IDisposable`. Each `IXChannelLocator` is satisfied by a sealed subclass passing its key.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.Workspace.Tests/Locating/CachingChannelLocatorTests.cs` using the existing fakes (`FakeWorkspaceStore`, a test clock). Model it on whatever the current `EventChannelLocator`/`SwitchChannelLocator` tests assert (cache hit returns id; TTL expiry triggers refresh; unknown (guild,server) returns null). Find them first:

Run: `grep -rln "ChannelLocator" tests`

Write a test that constructs a concrete subclass with a known key, seeds the fake store, and asserts `GetChannelIdAsync` returns the seeded channel and `null` for an unknown server.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Workspace.Tests --filter CachingChannelLocator`
Expected: FAIL — type does not exist.

- [ ] **Step 3: Implement the shared base**

Create `CachingChannelLocator.cs` by lifting the body shared by `EventChannelLocator`/`SwitchChannelLocator` (identical TTL cache + `SemaphoreSlim` gate + `(GuildId, ServerId) → channelId` map), parameterizing the `WorkspaceChannelKeys.X` constant via a constructor `string channelKey` argument:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Caches a provisioned channel set per key and resolves (guild, server) → channel id.</summary>
internal abstract class CachingChannelLocator(IServiceScopeFactory scopeFactory, IClock clock, string channelKey)
    : IDisposable
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private DateTimeOffset _builtAt = DateTimeOffset.MinValue;
    private Dictionary<(ulong GuildId, Guid ServerId), ulong> _byServer = new();

    public void Dispose() => _refreshGate.Dispose();

    public async Task<ulong?> GetChannelIdAsync(ulong guildId, Guid serverId, CancellationToken cancellationToken)
    {
        await EnsureFreshAsync(cancellationToken).ConfigureAwait(false);
        return _byServer.TryGetValue((guildId, serverId), out var id) ? id : null;
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
                var rows = await store.GetChannelsByKeyAsync(channelKey, cancellationToken).ConfigureAwait(false);

                var byServer = new Dictionary<(ulong GuildId, Guid ServerId), ulong>();
                foreach (var row in rows)
                {
                    if (row.RustServerId is not { } serverId)
                    {
                        continue;
                    }

                    byServer[(row.GuildId, serverId)] = row.DiscordChannelId;
                }

                _byServer = byServer;
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

- [ ] **Step 4: Reduce each locator to a thin subclass**

Rewrite each existing locator. Example `EventChannelLocator.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Time;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Workspace.Locating;

/// <summary>Resolves the #events channel id for a (guild, server).</summary>
internal sealed class EventChannelLocator(IServiceScopeFactory scopeFactory, IClock clock)
    : CachingChannelLocator(scopeFactory, clock, WorkspaceChannelKeys.ServerEvents), IEventChannelLocator;
```

Apply the same pattern to `SwitchChannelLocator` (`ServerSwitches`), `MapChannelLocator` (its existing key), and `AlarmChannelLocator` (its existing key). Keep each `IXChannelLocator` interface — the subclass implements it; `GetChannelIdAsync` is inherited.

- [ ] **Step 5: Build + full test run**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: build succeeds; all tests pass (existing per-feature locator tests still green against the subclasses).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(workspace): collapse channel locators onto shared CachingChannelLocator

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 7: Discord poster boilerplate helper (Core 3)

**Files:**

- Create: `src/RustPlusBot.Discord/Posting/DiscordChannelMessenger.cs`
- Modify: `src/RustPlusBot.Features.Switches/Posting/DiscordSwitchChannelPoster.cs`, `src/RustPlusBot.Features.Alarms/Posting/DiscordAlarmChannelPoster.cs` (the ensure-by-id family)
- Modify: `src/RustPlusBot.Features.Events/Posting/DiscordEventChannelPoster.cs`, `src/RustPlusBot.Features.Players/Posting/DiscordPlayerChannelPoster.cs` (the fire-and-forget family)
- Modify: feature `.csproj` files to reference `RustPlusBot.Discord` if not already
- Test: `tests/RustPlusBot.Discord.Tests/Posting/DiscordChannelMessengerTests.cs` (new) — only if `RustPlusBot.Discord` has a test project; otherwise rely on existing poster integration coverage

**Interfaces:**

- Produces in `namespace RustPlusBot.Discord.Posting`:
  - `Task<ulong?> EnsureAsync(DiscordSocketClient client, ulong channelId, ulong? messageId, Embed embed, MessageComponent components, ILogger logger, CancellationToken ct)` — channel fetch + edit-or-repost-by-id self-heal (incl. 404→repost), broad-catch→null, `OperationCanceledException` rethrow.
  - `Task PostAsync(DiscordSocketClient client, ulong channelId, Embed embed, ILogger logger, CancellationToken ct)` — fire-and-forget post, broad-catch→swallow, cancellation rethrow.
- Consumes: `Discord.WebSocket.DiscordSocketClient`, `Discord.Embed`, `Discord.MessageComponent`, `Microsoft.Extensions.Logging.ILogger`.

- [ ] **Step 1: Confirm whether RustPlusBot.Discord has a test project**

Run: `ls tests | grep -i Discord || echo "no discord test project"`
If none exists, the verification for this task is the existing poster behaviour exercised by feature tests + the full suite; skip the messenger-specific test steps and verify via Step 5.

- [ ] **Step 2: Write the failing test (only if a Discord test project exists)**

If a test project exists, write `DiscordChannelMessengerTests` covering: cancellation rethrows; a Discord exception in `PostAsync` is swallowed (no throw); `EnsureAsync` returns the posted id on the repost path. Use the existing test doubles for `DiscordSocketClient` if the codebase has them; if the client cannot be faked (sealed), skip and rely on the feature posters' existing tests.

- [ ] **Step 3: Implement DiscordChannelMessenger**

Create `src/RustPlusBot.Discord/Posting/DiscordChannelMessenger.cs` extracting the common boilerplate. Lift the exact bodies from the current posters: the `EnsureAsync` body from `DiscordSwitchChannelPoster` (including the inner 404 try/catch and the `OperationCanceledException`/broad-catch structure) and the `PostAsync` body from `DiscordEventChannelPoster`. Logging is delegated to the passed-in `ILogger` (callers keep their own `LoggerMessage` source-gen methods, or pass messages — keep it simple: log via `logger.LogWarning`/`LogDebug` inside the helper to avoid each caller re-declaring partial methods).

```csharp
using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace RustPlusBot.Discord.Posting;

/// <summary>Shared Discord channel post/edit boilerplate: fetch, options, self-heal, broad-catch.</summary>
public static class DiscordChannelMessenger
{
    /// <summary>Edits the message by id (self-healing on 404 by reposting) or posts a new one. Returns the message id, or null on failure.</summary>
    public static async Task<ulong?> EnsureAsync(
        DiscordSocketClient client, ulong channelId, ulong? messageId,
        Embed embed, MessageComponent components, ILogger logger, CancellationToken cancellationToken)
    {
        // (verbatim body lifted from DiscordSwitchChannelPoster.EnsureAsync, logging via `logger`)
    }

    /// <summary>Posts an embed fire-and-forget; Discord hiccups are logged and swallowed.</summary>
    public static async Task PostAsync(
        DiscordSocketClient client, ulong channelId, Embed embed, ILogger logger, CancellationToken cancellationToken)
    {
        // (verbatim body lifted from DiscordEventChannelPoster.PostAsync, logging via `logger`)
    }
}
```

Fill the two bodies with the exact code currently in `DiscordSwitchChannelPoster`/`DiscordEventChannelPoster`, replacing the `LogXxx(...)` source-gen calls with `logger.LogWarning(...)`/`logger.LogDebug(...)` using the same message text.

- [ ] **Step 4: Delegate each poster to the helper**

Rewrite the four posters to one-line delegates. Example `DiscordEventChannelPoster.cs`:

```csharp
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using RustPlusBot.Discord.Posting;

namespace RustPlusBot.Features.Events.Posting;

internal sealed class DiscordEventChannelPoster(
    DiscordSocketClient client, ILogger<DiscordEventChannelPoster> logger) : IEventChannelPoster
{
    public Task PostAsync(ulong channelId, Embed embed, CancellationToken cancellationToken)
        => DiscordChannelMessenger.PostAsync(client, channelId, embed, logger, cancellationToken);
}
```

Switch/Alarm posters delegate to `EnsureAsync` the same way. Remove the now-unused `partial` + `[LoggerMessage]` members. Add the `RustPlusBot.Discord` project reference to each feature `.csproj` that lacks it.

- [ ] **Step 5: Build + full test run**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: build succeeds; all tests pass.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(discord): extract shared poster boilerplate into DiscordChannelMessenger

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 8: Reduce constructor/method parameters (S107)

**Files (each its own commit-worthy unit, but grouped):**

- `src/RustPlusBot.Features.Map/Hosting/MapHostedService.cs` (9 params)
- `src/RustPlusBot.Features.Alarms/Relaying/AlarmStateRelay.cs` (8)
- `src/RustPlusBot.Features.Events/Relaying/EventRelay.cs` (8)
- `src/RustPlusBot.Features.Events/Hosting/EventsHostedService.cs` (8)
- `src/RustPlusBot.Features.Workspace/Reconciler/WorkspaceReconciler.cs` (8)
- `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs` (8)
- `src/RustPlusBot.Features.Connections/Listening/TeamStateTracker.cs` (8-param `UpdateAfk` method)

**Interfaces:**

- Produces: each over-7 constructor reduced to ≤7 by bundling cohesive collaborators into a `record`. Public API unchanged from callers' view (DI resolves the bundle).

- [ ] **Step 1: Bundle MapHostedService dependencies**

The 9 params split into a natural rendering-pipeline group (`composer`, `cache`, `locator`, `poster`) and infra (`eventBus`, `clock`, `options`, `scopeFactory`, `logger`). Introduce:

```csharp
internal sealed record MapPipeline(
    MapComposer Composer, BaseMapCache Cache, IMapChannelLocator Locator, IMapChannelPoster Poster);
```

Register it in `MapServiceCollectionExtensions` (`services.AddSingleton<MapPipeline>();` — DI fills it from the already-registered components). Change the ctor to `(IEventBus eventBus, MapPipeline pipeline, IClock clock, IOptions<MapOptions> options, IServiceScopeFactory scopeFactory, ILogger<MapHostedService> logger)` (6 params) and update field access (`composer` → `pipeline.Composer`, etc.).

- [ ] **Step 2: Apply the same bundling to the other 5 constructors**

For each, group the cohesive collaborators into a `record` and inject the bundle so the param count drops to ≤7:

- `AlarmStateRelay`: bundle `{ IAlarmChannelLocator Locator, IAlarmChannelPoster Poster, ITeamChatSender TeamChatSender }` → `AlarmRelayChannels`.
- `EventRelay`: bundle `{ IEventChannelLocator Locator, IEventChannelPoster Poster, ITeamChatSender TeamChatSender }` → `EventRelayChannels`.
- `EventsHostedService`: bundle `{ EventStateStore Store, RigStateStore RigStore }` → `EventStores`.
- `WorkspaceReconciler`: bundle `{ IWorkspaceRegistry Registry, IWorkspaceGateway Gateway, IWorkspaceStore Store }` → `WorkspaceBackends`.
- `ConnectionSupervisor`: bundle `{ IUserDmSender DmSender, ICredentialProtector Protector }` → `ConnectionSecurity` (or fold `protector`+`dmSender` since both are notification/security concerns).

Register each bundle as a singleton in the matching service-collection extension. Update field references throughout each class.

- [ ] **Step 3: Fix TeamStateTracker.UpdateAfk (8-param private method)**

`UpdateAfk(transitions, id, was, now, clock, threshold, epsilon, diedThisPoll)` — bundle the polling parameters into a `record AfkPollContext(DateTimeOffset Clock, TimeSpan Threshold, float Epsilon, bool DiedThisPoll)` and pass `(transitions, id, was, now, context)` (5 params). Update the single call site.

- [ ] **Step 4: Build + full test run**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: build succeeds; all tests pass. The per-feature registration tests confirm DI still resolves every service.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: bundle cohesive dependencies to satisfy S107 parameter limit

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 9: Reduce cognitive complexity (S3776)

**Files:**

- `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs:184` (`DrawPlayers`, complexity 19)
- `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs:529` (complexity 16)
- `src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs:29` (complexity 16)

**Interfaces:**

- Produces: same observable behaviour; complexity ≤15 each via private helper extraction. No signature changes to callers.

- [ ] **Step 1: Refactor MapRenderer.DrawPlayers**

Extract the per-player work into private static helpers so the loop body is flat: `DrawPlayerIcon(ctx, player, icon)` (the icon-vs-dot branch) and `DrawPlayerLabel(ctx, player)` (the suffix/label/color + `RichTextOptions`). The loop becomes:

```csharp
foreach (var player in players)
{
    DrawPlayerIcon(ctx, player, icon);
    DrawPlayerLabel(ctx, player);
}
```

Keep `CenterAt` as-is. This removes the nested branching that drives the score.

- [ ] **Step 2: Refactor ConnectionSupervisor method at line 529**

Read the method: `sed -n '510,575p' src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`. Extract the deepest guard/branch cluster (the part contributing the nested `if`/`else`/`catch` weight) into a private method with a descriptive name. Target: drop from 16 to ≤15 — one extraction is enough; do the smallest change that clears the threshold.

- [ ] **Step 3: Refactor ServerInfoMessageRenderer at line 29**

Read: `sed -n '20,90p' src/RustPlusBot.Features.Workspace/Messages/ServerInfoMessageRenderer.cs`. Extract the field-formatting branches (the per-field conditional appends) into a private helper. Target ≤15.

- [ ] **Step 4: Build + full test run**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: build succeeds; all tests pass (renderer/supervisor tests confirm behaviour preserved).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor: extract helpers to reduce cognitive complexity below 15 (S3776)

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>"
```

---

## Task 10: Verify against SonarQube & finish the branch

**Files:** none (verification + integration).

- [ ] **Step 1: Final full build + test**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: build clean, 520 tests pass.

- [ ] **Step 2: Push and let CI run the Sonar analysis**

Push the branch and open the PR(s) per the spec's 2-PR strategy (mechanical: Tasks 1–4, 8, 9; structural: Tasks 5–7) or a single PR if preferred. CI's `Sonar.yml` runs the analysis on the PR.

- [ ] **Step 3: Re-query SonarQube and confirm the drop (evidence before claiming done)**

After the PR analysis completes, re-run the MCP queries used to build this plan:

- `search_sonar_issues_in_projects` (MAINTAINABILITY, OPEN/CONFIRMED) — expect ≪82.
- `search_duplicated_files` — expect overall density well below 8.4% and the localizer/locator/poster clones gone.

Report the actual before/after numbers. Do not claim success without them.

- [ ] **Step 4: Finish the branch**

Use the `superpowers:finishing-a-development-branch` skill to merge/PR per user choice.

---

## Self-Review notes

- **Spec coverage:** Task 1↔EF exclusion; Task 2↔2A auto-fixes; Task 3↔2B literals; Task 4↔2C namespace; Task 5↔Core 1; Task 6↔Core 2; Task 7↔Core 3; Task 8↔2D S107; Task 9↔2E S3776; Task 10↔verification. All spec sections covered.
- **Known risk (flagged for executor):** Task 4 (namespace) and Task 5 Step 6 (per-feature `ILocalizer` DI collision) are the two places most likely to need iteration — both have explicit cautions inline.
- **Type consistency:** `DictionaryLocalizer(IReadOnlyDictionary<string, IReadOnlyDictionary<string,string>>)` matches the catalogs' `Strings` shape verified in source; `CachingChannelLocator.GetChannelIdAsync` signature matches the existing `IXChannelLocator` members; bundle record names are used consistently within each task.
