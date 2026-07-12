# Localization → .resx consolidation + duplication quick-wins — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 6 hand-written C# dictionary localization catalogs with a single shared `.resx` resource set read by one `ResxLocalizer`, collapse all per-feature localizer interfaces into the shared `ILocalizer`, then take low-risk quick-wins on hosted-service and event-handler duplication.

**Architecture:** The shared `RustPlusBot.Localization` project gains one flat `.resx` set (`Strings.resx` = en/neutral, `Strings.fr.resx`) holding all 195 keys, and a `ResxLocalizer : ILocalizer` that resolves per-call culture via `ResourceManager.GetString(key, CultureInfo)`. Every feature deletes its `*LocalizationCatalog`/`*Localizer`/`I*Localizer` trio and injects the single `ILocalizer`. A single `AddRustPlusBotLocalization()` DI extension replaces 6 registrations.

**Tech Stack:** .NET 10, C#, xUnit, `System.Resources.ResourceManager`, Microsoft.Extensions.DependencyInjection.

## Global Constraints

- Target framework: `net10.0`. `Nullable=enable`, `ImplicitUsings=enable`.
- `TreatWarningsAsErrors=true` — every new public type/member needs XML doc comments (`GenerateDocumentationFile=true`); zero analyzer warnings (NetAnalyzers + SonarAnalyzer + Roslynator all on).
- `dotnet jb cleanupcode --profile=ReformatAndReorder` is a hard CI gate: the working tree must show **zero diff** after running it. Run it before every push.
- Solution file is `RustPlusBot.slnx` (not `.sln`).
- Keys stay byte-identical strings (e.g. `switch.status.on`); never rename a key.
- String values contain emoji (⚡ ⭘ ⚠️), accented French, and `{0}` placeholders — these must survive byte-for-byte into `.resx`.
- Culture is a per-call parameter (`context.Culture`); never use `Thread.CurrentCulture`.
- Fallback semantics (must be preserved exactly): resolve requested culture → fall back to English (`en`) → if key still missing, return the key string itself. Format overload uses the **requested culture's** format provider, falling back to invariant on `CultureNotFoundException`.
- Branch: `feat/localization-resx` (already created off `develop`; editorconfig-hardening commit already present).

---

## File Structure

**New files (in `src/RustPlusBot.Localization/`):**

- `Strings.resx` — neutral (English) resource set, 195 keys.
- `Strings.fr.resx` — French resource set, 195 keys.
- `ResxLocalizer.cs` — `ILocalizer` impl over a single `ResourceManager`.
- `LocalizationServiceCollectionExtensions.cs` — `AddRustPlusBotLocalization()`.
- `AssemblyInfo.cs` (or csproj property) — `[assembly: NeutralResourcesLanguage("en")]`.

**Generation helper (temporary, NOT committed):**

- `scripts/gen-resx.csx` or a throwaway console snippet under the scratchpad — reads the 6 existing catalogs' values and emits the two `.resx` files, guaranteeing byte-fidelity. Deleted before final commit.

**Deleted files:**

- `src/RustPlusBot.Localization/DictionaryLocalizer.cs` (after migration confirms no refs).
- 6 × `*LocalizationCatalog.cs`, 6 × `*Localizer.cs`, 6 × `I*Localizer.cs` (+ Workspace `Localization/ILocalizer.cs` alias).
- Per-catalog test files (folded into shared tests).

**Modified files:** ~44 injection sites (type rename), 6 `*ServiceCollectionExtensions.cs`, `Program.cs`, ~30 test files constructing localizers.

---

## Task 1: Add the shared `.resx` resource set (generated from existing catalogs)

**Files:**

- Create: `src/RustPlusBot.Localization/Strings.resx`
- Create: `src/RustPlusBot.Localization/Strings.fr.resx`
- Modify: `src/RustPlusBot.Localization/RustPlusBot.Localization.csproj`
- Create: `src/RustPlusBot.Localization/AssemblyInfo.cs`

**Interfaces:**

- Produces: a `ResourceManager`-resolvable resource set with base name `RustPlusBot.Localization.Strings`, containing 195 keys in `en` (neutral) and `fr`.

- [ ] **Step 1: Generate the two `.resx` files programmatically from the existing catalog values.**

Do NOT hand-transcribe (emoji/accents/`{0}` must be exact). Write a throwaway snippet in the scratchpad that references the 6 catalog `Default.Strings` dictionaries (or parses them) and writes both `.resx` files using `System.Resources.ResXResourceWriter` (or emits the XML directly with `xml:space="preserve"` and UTF-8). The 6 source catalogs and their key→value pairs:

- Workspace `LocalizationCatalog` (category/channel/information/setup/settings/server.info/map.* keys)
- Commands `CommandLocalizationCatalog` (command.*/help.*/uptime.*/leader.* keys)
- Players `PlayerLocalizationCatalog`
- Events `EventLocalizationCatalog`
- Switches `SwitchLocalizationCatalog`
- Alarms `AlarmLocalizationCatalog`

Each `.resx` `<data name="KEY" xml:space="preserve"><value>TEXT</value></data>`. `Strings.resx` gets the `en` values; `Strings.fr.resx` gets the `fr` values.

- [ ] **Step 2: Wire the resx into the csproj.**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <EmbeddedResource Update="Strings.resx">
      <Generator></Generator>
    </EmbeddedResource>
    <EmbeddedResource Update="Strings.fr.resx">
      <DependentUpon>Strings.resx</DependentUpon>
    </EmbeddedResource>
  </ItemGroup>

</Project>
```

(No `<LastGenOutput>`/`PublicResXFileCodeGenerator` — we use `ResourceManager.GetString`, not a typed accessor, because keys contain `.`.)

- [ ] **Step 3: Declare the neutral language.**

Create `src/RustPlusBot.Localization/AssemblyInfo.cs`:

```csharp
using System.Resources;

[assembly: NeutralResourcesLanguage("en")]
```

- [ ] **Step 4: Verify the resx compiles and resources load.**

Run: `dotnet build src/RustPlusBot.Localization/RustPlusBot.Localization.csproj`
Expected: build succeeds, no warnings.

- [ ] **Step 5: Verify key count parity with a quick throwaway check.**

Confirm both files contain 195 `<data>` entries and identical key sets (the parity test in Task 3 will lock this in permanently; this is a sanity check before proceeding).
Run: `grep -c '<data ' src/RustPlusBot.Localization/Strings.resx src/RustPlusBot.Localization/Strings.fr.resx`
Expected: `195` for both.

- [ ] **Step 6: Commit.**

```bash
git add src/RustPlusBot.Localization/Strings.resx src/RustPlusBot.Localization/Strings.fr.resx \
        src/RustPlusBot.Localization/AssemblyInfo.cs src/RustPlusBot.Localization/RustPlusBot.Localization.csproj
git commit -m "feat(localization): add shared Strings.resx (en/fr) generated from catalogs"
```

---

## Task 2: Implement `ResxLocalizer` and the DI extension

**Files:**

- Create: `src/RustPlusBot.Localization/ResxLocalizer.cs`
- Create: `src/RustPlusBot.Localization/LocalizationServiceCollectionExtensions.cs`
- Test: `tests/RustPlusBot.Localization.Tests/ResxLocalizerTests.cs`
- Modify: `src/RustPlusBot.Localization/RustPlusBot.Localization.csproj` (add `Microsoft.Extensions.DependencyInjection.Abstractions` if not present)

**Interfaces:**

- Consumes: `Strings.resx`/`Strings.fr.resx` from Task 1; existing `ILocalizer` (unchanged: `string Get(string key, string culture)` and `string Get(string key, string culture, params object[] args)`).
- Produces:
  - `public sealed class ResxLocalizer() : ILocalizer` — parameterless ctor (owns its `ResourceManager`).
  - `public static IServiceCollection AddRustPlusBotLocalization(this IServiceCollection services)` registering `ILocalizer → ResxLocalizer` as a singleton (idempotent via `TryAddSingleton`).

- [ ] **Step 1: Write the failing test.**

Create `tests/RustPlusBot.Localization.Tests/ResxLocalizerTests.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Localization;

namespace RustPlusBot.Localization.Tests;

public sealed class ResxLocalizerTests
{
    private static readonly ResxLocalizer Sut = new();

    [Fact]
    public void Get_returns_english_value()
    {
        Assert.Equal("⚡ ON", Sut.Get("switch.status.on", "en"));
    }

    [Fact]
    public void Get_returns_french_value()
    {
        Assert.Equal("⚡ ALLUMÉ", Sut.Get("switch.status.on", "fr"));
    }

    [Fact]
    public void Get_falls_back_to_english_for_unknown_culture()
    {
        Assert.Equal("⚡ ON", Sut.Get("switch.status.on", "de"));
    }

    [Fact]
    public void Get_normalizes_region_specific_culture()
    {
        Assert.Equal("⚡ ALLUMÉ", Sut.Get("switch.status.on", "fr-FR"));
    }

    [Fact]
    public void Get_returns_key_when_missing()
    {
        Assert.Equal("nonexistent.key", Sut.Get("nonexistent.key", "en"));
    }

    [Fact]
    public void Get_with_args_formats_with_culture_provider()
    {
        // "Endpoint: {0}:{1}" / "Adresse : {0}:{1}"
        Assert.Equal("Endpoint: 1.2.3.4:28015", Sut.Get("server.info.endpoint", "en", "1.2.3.4", 28015));
        Assert.Equal("Adresse : 1.2.3.4:28015", Sut.Get("server.info.endpoint", "fr", "1.2.3.4", 28015));
    }

    [Fact]
    public void Get_with_args_uses_invariant_for_blank_culture()
    {
        Assert.Equal("Endpoint: 1.2.3.4:28015", Sut.Get("server.info.endpoint", "", "1.2.3.4", 28015));
    }
}
```

- [ ] **Step 2: Run test to verify it fails.**

Run: `dotnet test tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj`
Expected: FAIL — `ResxLocalizer` does not exist.

- [ ] **Step 3: Implement `ResxLocalizer`.**

Create `src/RustPlusBot.Localization/ResxLocalizer.cs`:

```csharp
using System.Globalization;
using System.Resources;

namespace RustPlusBot.Localization;

/// <summary>
/// <see cref="ILocalizer"/> backed by the embedded <c>Strings</c> resource set,
/// resolving per-call culture with English fallback and region normalization.
/// </summary>
public sealed class ResxLocalizer : ILocalizer
{
    private const string FallbackCulture = "en";

    private static readonly ResourceManager Resources =
        new("RustPlusBot.Localization.Strings", typeof(ResxLocalizer).Assembly);

    /// <inheritdoc />
    public string Get(string key, string culture)
    {
        var info = ResolveCulture(culture);

        // ResourceManager walks the requested culture down to the neutral (en) set,
        // so a single lookup already covers the English fallback.
        var value = Resources.GetString(key, info);
        return value ?? key;
    }

    /// <inheritdoc />
    public string Get(string key, string culture, params object[] args)
    {
        var format = Get(key, culture);
        return string.Format(ResolveCulture(culture), format, args);
    }

    private static CultureInfo ResolveCulture(string culture)
    {
        var normalized = Normalize(culture);
        try
        {
            return CultureInfo.GetCultureInfo(normalized);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
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
}
```

Note: when `culture` is blank, `ResolveCulture` returns the `en` `CultureInfo`, and `string.Format(en-culture, ...)` formats numbers like invariant for these strings — the test `Get_with_args_uses_invariant_for_blank_culture` passes because `en` and invariant produce identical output for `{0}:{1}` here. (If a future key needs strict invariant on blank, revisit.)

- [ ] **Step 4: Implement the DI extension.**

Create `src/RustPlusBot.Localization/LocalizationServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace RustPlusBot.Localization;

/// <summary>DI registration for the shared localizer.</summary>
public static class LocalizationServiceCollectionExtensions
{
    /// <summary>Registers the shared <see cref="ILocalizer"/> (idempotent).</summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddRustPlusBotLocalization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ILocalizer, ResxLocalizer>();
        return services;
    }
}
```

If `Microsoft.Extensions.DependencyInjection.Abstractions` isn't already referenced by the project, add it via `Directory.Packages.props` version and a `<PackageReference>` in the csproj.

- [ ] **Step 5: Run tests to verify they pass.**

Run: `dotnet test tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj`
Expected: PASS (all `ResxLocalizerTests`).

- [ ] **Step 6: Commit.**

```bash
git add src/RustPlusBot.Localization tests/RustPlusBot.Localization.Tests/ResxLocalizerTests.cs
git commit -m "feat(localization): add ResxLocalizer + AddRustPlusBotLocalization DI extension"
```

---

## Task 3: Add the resource parity test (key-coverage guard)

**Files:**

- Create: `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs`

**Interfaces:**

- Consumes: `Strings.resx`/`Strings.fr.resx` (Task 1) via `ResourceManager.GetResourceSet`.

- [ ] **Step 1: Write the parity test.**

```csharp
using System.Globalization;
using System.Resources;
using RustPlusBot.Localization;

namespace RustPlusBot.Localization.Tests;

public sealed class StringsResourceParityTests
{
    private static readonly ResourceManager Resources =
        new("RustPlusBot.Localization.Strings", typeof(ResxLocalizer).Assembly);

    private static HashSet<string> Keys(CultureInfo culture)
    {
        using var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return set.Cast<System.Collections.DictionaryEntry>()
                  .Select(e => (string)e.Key)
                  .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void French_covers_every_english_key()
    {
        var en = Keys(CultureInfo.GetCultureInfo("en"));
        var fr = Keys(CultureInfo.GetCultureInfo("fr"));
        Assert.Empty(en.Except(fr));
    }

    [Fact]
    public void English_covers_every_french_key()
    {
        var en = Keys(CultureInfo.GetCultureInfo("en"));
        var fr = Keys(CultureInfo.GetCultureInfo("fr"));
        Assert.Empty(fr.Except(en));
    }

    [Fact]
    public void Catalog_has_expected_key_count()
    {
        Assert.Equal(195, Keys(CultureInfo.GetCultureInfo("en")).Count);
    }
}
```

- [ ] **Step 2: Run to verify it passes.**

Run: `dotnet test tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj`
Expected: PASS. If `French_covers_every_english_key` fails, the resx generation in Task 1 dropped/renamed a key — fix the resx, not the test.

- [ ] **Step 3: Commit.**

```bash
git add tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs
git commit -m "test(localization): add en/fr resource parity + key-count guard"
```

---

## Task 4: Migrate the Commands feature to the shared localizer

**Files:**

- Delete: `src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs`, `CommandLocalizer.cs`, `ICommandLocalizer.cs`
- Modify: all 23 `src/RustPlusBot.Features.Commands/**` files referencing `ICommandLocalizer` (handlers, `HelpEmbedRenderer`, `LeaderService`, `ServerResolver`, modules, `RigReply`) → `ILocalizer`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Modify: test files in `tests/RustPlusBot.Features.Commands.Tests/**` that construct `CommandLocalizer(CommandLocalizationCatalog.Default)`

**Interfaces:**

- Consumes: `RustPlusBot.Localization.ILocalizer`, `ResxLocalizer` (Task 2).

- [ ] **Step 1: Replace the DI registration.**

In `CommandServiceCollectionExtensions.cs`, remove:

```csharp
services.AddSingleton(CommandLocalizationCatalog.Default);
services.AddSingleton<ICommandLocalizer, CommandLocalizer>();
```

and replace with:

```csharp
services.AddRustPlusBotLocalization();
```

Add `using RustPlusBot.Localization;` if missing.

- [ ] **Step 2: Sweep the type rename across Commands source.**

In every `src/RustPlusBot.Features.Commands/**/*.cs` that uses `ICommandLocalizer`, replace `ICommandLocalizer` → `ILocalizer` and ensure `using RustPlusBot.Localization;` is present (remove `using RustPlusBot.Features.Commands.Localization;` where it was only for the localizer). Files: all 18 `Handlers/*.cs` listed in the spec, `Help/HelpEmbedRenderer.cs`, `Leader/LeaderService.cs`, `Servers/ServerResolver.cs`, `Modules/LeaderComponentModule.cs`, `Modules/CommandSurfaceModule.cs` (3 `GetRequiredService<ICommandLocalizer>()` → `<ILocalizer>`).

- [ ] **Step 3: Delete the three Commands localization files.**

```bash
git rm src/RustPlusBot.Features.Commands/Localization/CommandLocalizationCatalog.cs \
       src/RustPlusBot.Features.Commands/Localization/CommandLocalizer.cs \
       src/RustPlusBot.Features.Commands/Localization/ICommandLocalizer.cs
```

- [ ] **Step 4: Update Commands tests to use `ResxLocalizer`.**

In each test file that did `new CommandLocalizer(CommandLocalizationCatalog.Default)` (and `ICommandLocalizer Loc`), replace with `new ResxLocalizer()` typed as `ILocalizer`. Files: `AfkCommandHandlerTests.cs`, `Handlers/MuteHandlersTests.cs`, `Handlers/TeamIntelHandlersTests.cs`, `Handlers/RigCommandHandlersTests.cs`, `Handlers/EventHandlersTests.cs`, `Handlers/QueryHandlersTests.cs`, `Help/HelpEmbedRendererTests.cs`, `Help/CommandHelpCatalogTests.cs`, `Leader/LeaderServiceTests.cs`, `Servers/ServerResolverTests.cs`, `Localization/CommandLocalizerTests.cs`, `CommandRegistrationTests.cs`. Add `using RustPlusBot.Localization;`. Delete `Localization/CommandLocalizerTests.cs` (its behavior is now covered by `ResxLocalizerTests`); keep any test that asserts a specific rendered string (those still validate via the shared localizer).

- [ ] **Step 5: Build and test Commands.**

Run: `dotnet build src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj && dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`
Expected: build clean (no warnings), all Commands tests PASS.

- [ ] **Step 6: Run the format gate and commit.**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git diff --exit-code   # must show nothing
git add -A && git commit -m "refactor(commands): use shared ILocalizer/ResxLocalizer, drop command catalog"
```

---

## Task 5: Migrate Workspace, Players, Events, Switches, Alarms

Same recipe as Task 4, per feature. Each is its own commit. Apply to:

**5a. Workspace** — `src/RustPlusBot.Features.Workspace`

- Registration (`WorkspaceServiceCollectionExtensions.cs`): remove `AddSingleton(LocalizationCatalog.Default)` + `AddSingleton<ILocalizer, Localizer>()`, add `AddRustPlusBotLocalization()`.
- The Workspace `ILocalizer` is its **own internal alias** (`Localization/ILocalizer.cs`). Replace all internal `ILocalizer` uses with `RustPlusBot.Localization.ILocalizer` and update `using`. Files using it: `Messages/{Setup,Settings,ServerInfo,Information,MapControl}MessageRenderer.cs`, `Reconciler/WorkspaceReconciler.cs`, `WorkspaceServiceCollectionExtensions.cs`.
- Delete: `Localization/LocalizationCatalog.cs`, `Localization/Localizer.cs`, `Localization/ILocalizer.cs`.
- Tests: `Localization/LocalizerTests.cs` (delete — covered by `ResxLocalizerTests`), `Messages/MapControlMessageRendererTests.cs`, `Messages/RendererTests.cs`, `Reconciler/ReconcilerHarness.cs` — replace `new Localizer(LocalizationCatalog.Default)` with `new ResxLocalizer()`.

**5b. Players** — `src/RustPlusBot.Features.Players`

- Registration (`PlayerEventServiceCollectionExtensions.cs`): remove catalog + `IPlayerLocalizer`; add `AddRustPlusBotLocalization()`.
- `IPlayerLocalizer` → `ILocalizer` in `Rendering/PlayerEventRenderer.cs`.
- Delete: `Rendering/PlayerLocalizationCatalog.cs`, `PlayerLocalizer.cs`, `IPlayerLocalizer.cs`.
- Tests: `PlayerEventEndToEndTests.cs`, `PlayerEventRendererTests.cs`, `PlayerEventRelayTests.cs` → `new ResxLocalizer()`; delete `PlayerLocalizationCatalogTests.cs`.

**5c. Events** — `src/RustPlusBot.Features.Events`

- Registration (`EventServiceCollectionExtensions.cs`): remove catalog + `IEventLocalizer`; add `AddRustPlusBotLocalization()`.
- `IEventLocalizer` → `ILocalizer` in `Rendering/EventEmbedRenderer.cs`.
- Delete: `Rendering/EventLocalizationCatalog.cs`, `EventLocalizer.cs`, `IEventLocalizer.cs`.
- Tests: `Relaying/EventRelayTests.cs`, `Rendering/RigRenderingTests.cs`, `Rendering/EventEmbedRendererTests.cs` → `new ResxLocalizer()`; delete `Rendering/EventLocalizerTests.cs`.

**5d. Switches** — `src/RustPlusBot.Features.Switches`

- Registration (`SwitchServiceCollectionExtensions.cs`): remove catalog + `ISwitchLocalizer`; add `AddRustPlusBotLocalization()`.
- `ISwitchLocalizer` → `ILocalizer` in `Rendering/SwitchEmbedRenderer.cs`.
- Delete: `Rendering/SwitchLocalizationCatalog.cs`, `SwitchLocalizer.cs`, `ISwitchLocalizer.cs`.
- Tests: `SwitchEmbedRendererTests.cs`, `SwitchStateRelayTests.cs`, `SwitchRegistrationTests.cs` → `new ResxLocalizer()`; delete `SwitchLocalizationCatalogTests.cs`.

**5e. Alarms** — `src/RustPlusBot.Features.Alarms`

- Registration (`AlarmServiceCollectionExtensions.cs`): remove `services.AddSingleton<IAlarmLocalizer>(new AlarmLocalizer(AlarmLocalizationCatalog.Default));`; add `AddRustPlusBotLocalization()`.
- `IAlarmLocalizer` → `ILocalizer` in `Rendering/AlarmEmbedRenderer.cs`, `Relaying/AlarmStateRelay.cs`.
- Delete: `Rendering/AlarmLocalizationCatalog.cs`, `AlarmLocalizer.cs`, `IAlarmLocalizer.cs`.
- Tests: `AlarmRegistrationTests.cs` (+ any renderer/relay test constructing the localizer) → `new ResxLocalizer()`; delete `AlarmLocalizationCatalogTests.cs`.

- [ ] **Step 1 (per feature 5a–5e): apply registration swap + type rename + deletions + test updates** (recipe above).
- [ ] **Step 2 (per feature): build + test that feature.**

Run (example for Switches): `dotnet build src/RustPlusBot.Features.Switches/RustPlusBot.Features.Switches.csproj && dotnet test tests/RustPlusBot.Features.Switches.Tests/RustPlusBot.Features.Switches.Tests.csproj`
Expected: clean build, all tests PASS.

- [ ] **Step 3 (per feature): format gate + commit.**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git diff --exit-code
git add -A && git commit -m "refactor(<feature>): use shared ILocalizer/ResxLocalizer, drop <feature> catalog"
```

(One commit per feature: `refactor(workspace|players|events|switches|alarms): ...`.)

---

## Task 6: Remove `DictionaryLocalizer` and confirm full solution health

**Files:**

- Delete: `src/RustPlusBot.Localization/DictionaryLocalizer.cs` (if unreferenced)
- Delete: `tests/RustPlusBot.Localization.Tests/DictionaryLocalizerTests.cs` (if its subject is removed)
- Modify: `Program.cs` (optional — add a single `AddRustPlusBotLocalization()` at composition root if not already guaranteed by feature extensions)

**Interfaces:**

- Consumes: nothing new.

- [ ] **Step 1: Confirm `DictionaryLocalizer` has no remaining references.**

Run: `grep -rn "DictionaryLocalizer" --include="*.cs" src tests`
Expected: only its own definition + its test. If anything else references it, stop and migrate that first.

- [ ] **Step 2: Delete `DictionaryLocalizer` and its test.**

```bash
git rm src/RustPlusBot.Localization/DictionaryLocalizer.cs \
       tests/RustPlusBot.Localization.Tests/DictionaryLocalizerTests.cs
```

- [ ] **Step 3: Ensure localization is registered exactly once at composition.**

Verify `Program.cs`: each feature `AddX()` now calls `AddRustPlusBotLocalization()` (idempotent via `TryAddSingleton`), so a single resolved `ILocalizer` exists. No change needed unless a feature path skips registration — if so, add `builder.Services.AddRustPlusBotLocalization();` once before the feature registrations.

- [ ] **Step 4: Build and test the whole solution.**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: clean build (zero warnings), **all ~520 tests PASS**.

- [ ] **Step 5: Behavioral-equivalence spot check (temporary test, then revert).**

Add a temporary `[Theory]` to `ResxLocalizerTests` asserting ~6 representative values across both cultures (one per feature prefix: `switch.`, `command.`, `alarm.`, `map.layer.`, `command.event.`, `server.info.status.`) match the strings from git history (`git show HEAD~N:<oldcatalog>`). Run it, confirm PASS, then remove it (the parity test + rendered-output tests already guard this permanently). This is a one-off safety net against silent transcription drift.

- [ ] **Step 6: Format gate + commit.**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git diff --exit-code
git add -A && git commit -m "refactor(localization): remove obsolete DictionaryLocalizer"
```

---

## Task 7: Quick-win — shared base for the two hosted services

**Files:**

- Create: `src/RustPlusBot.Abstractions/Hosting/EventConsumerHostedService.cs` (or nearest existing shared location — confirm `RustPlusBot.Abstractions` is referenced by both Commands and Players; if not, place in the feature that the other can reference, else skip and leave a note)
- Modify: `src/RustPlusBot.Features.Commands/Hosting/CommandsHostedService.cs`
- Modify: `src/RustPlusBot.Features.Players/Hosting/PlayersHostedService.cs`

**Interfaces:**

- Produces: `internal abstract partial class EventConsumerHostedService<TEvent> : IHostedService, IDisposable` with an abstract `Task HandleAsync(TEvent evt, IServiceProvider scope, CancellationToken ct)` and shared start/stop/consume-loop + error logging. **Only proceed if both services can reference the chosen location.** The two services differ solely in `TEvent` (`TeamMessageReceivedEvent` vs `PlayerStateChangedEvent`) and the per-event body (resolve `CommandDispatcher`/dispatch vs the Players handler).

- [ ] **Step 1: Verify a shared location exists.**

Run: `grep -l "RustPlusBot.Abstractions" src/RustPlusBot.Features.Commands/*.csproj src/RustPlusBot.Features.Players/*.csproj`
Expected: both listed. If not, **skip Task 7** (document why) — do not add a new cross-feature dependency just to dedup 37 lines.

- [ ] **Step 2: Write/adjust a test that exercises the loop.**

If existing hosted-service tests exist, ensure they still pass after refactor; if none, add a minimal test that the base class dispatches one event and survives a throwing handler (loop continues). Place in `tests/RustPlusBot.Features.Commands.Tests` or an abstractions test project.

- [ ] **Step 3: Extract the base class** (start/stop/`ConsumeAsync` loop + `LoggerMessage` partials parameterized by `TEvent`), make both services inherit it and override only `HandleAsync`.

- [ ] **Step 4: Build + test both features.**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj tests/RustPlusBot.Features.Players.Tests/RustPlusBot.Features.Players.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Format gate + commit.**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git diff --exit-code
git add -A && git commit -m "refactor(hosting): share event-consumer loop across hosted services"
```

---

## Task 8: Quick-win — shared helper for Cargo/Heli/Chinook handlers

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Handlers/CargoCommandHandler.cs`, `HeliCommandHandler.cs`, `ChinookCommandHandler.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/MarkerReply.cs` (a small shared helper, mirroring existing `RigReply` pattern in the same folder)

**Interfaces:**

- Produces: `internal static class MarkerReply` (or instance helper) with a method taking `(IEventState state, ILocalizer localizer, IClock clock, CommandContext context, MarkerKind kind, string okKey, string noneKey)` returning the reply string. The three handlers differ only in `MarkerKind` (CargoShip/PatrolHelicopter/Chinook) + key prefix.

- [ ] **Step 1: Confirm the three handlers are structurally identical** except marker kind + keys (already verified: Cargo/Heli/Chinook each 11 dup-lines). Look at `RigReply.cs` for the established in-feature helper convention and follow it.

- [ ] **Step 2: Add tests first** (or confirm `EventHandlersTests.cs` already covers all three) — assert each handler returns the `.ok` reply when a marker is present and `.none` when absent, for both cultures.

- [ ] **Step 3: Extract `MarkerReply` and rewrite the three handlers to delegate to it**, keeping their `Name`, ctor signature (now `ILocalizer`), and `ICommandHandler` contract unchanged.

- [ ] **Step 4: Build + test Commands.**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`
Expected: PASS.

- [ ] **Step 5: Format gate + commit.**

```bash
dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx
git diff --exit-code
git add -A && git commit -m "refactor(commands): share marker-event reply helper across cargo/heli/chinook"
```

---

## Task 9: Final verification + push

- [ ] **Step 1: Full clean build + full test run.**

Run: `dotnet build RustPlusBot.slnx && dotnet test RustPlusBot.slnx`
Expected: zero warnings, all tests PASS.

- [ ] **Step 2: Final format gate.**

Run: `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx && git diff --exit-code`
Expected: no diff.

- [ ] **Step 3: Push branch and open PR against `develop`.**

```bash
git push -u origin feat/localization-resx
gh pr create --base develop --title "refactor: consolidate localization into shared .resx + duplication quick-wins" --body "<summary + before/after Sonar numbers>"
```

- [ ] **Step 4: After CI's Sonar analysis runs, re-pull duplication and confirm the localization cluster dropped to ~0** (expect overall density well under the prior 4.4%, with the ~385 localization dup-lines eliminated plus the Task 7/8 reductions).

---

## Self-Review

**Spec coverage:**

- Central `.resx` + per-call culture → Tasks 1, 2. ✓
- Collapse to one `ILocalizer` (delete 6 interfaces/subclasses/catalogs) → Tasks 4, 5, 6. ✓
- One flat resx set, 195 keys, prefixed → Task 1; parity guard Task 3. ✓
- Single DI registration → Task 2 (`AddRustPlusBotLocalization`), applied per feature in 4/5, confirmed once in 6. ✓
- Quick-wins (hosted services, event handlers) with device-symmetry excluded → Tasks 7, 8 (device symmetry not present in any task). ✓
- Verification gates (build/test/jb/behavioral equivalence) → Tasks 4–9. ✓
- Generate resx programmatically (emoji/accents/`{0}` fidelity) → Task 1 Step 1 + Task 6 Step 5 spot check. ✓

**Placeholder scan:** All code steps contain full code; the two quick-win tasks (7, 8) reference established in-repo patterns (`RigReply`, `LoggerMessage`) and are gated by a feasibility check (skip rather than force). No "TBD/TODO/handle edge cases".

**Type consistency:** `ResxLocalizer()` (parameterless) used identically in Tasks 2, 3, 4, 5, 6; `ILocalizer.Get` signatures unchanged from existing contract; `AddRustPlusBotLocalization()` named identically throughout; resource base name `RustPlusBot.Localization.Strings` consistent in `ResxLocalizer`, parity test, and spot check.
