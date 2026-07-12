# RustMaps Monument Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 35 vendored monument PNGs with runtime-rasterized SVGs from `RustMapsApi.Assets 1.0.0-beta.3`, with full Rust+ token coverage and a once-per-token Info log for monuments that have no icon.

**Architecture:** A new pure `MonumentTokenMap` (Rust+ token → `RustMapsApi.V4.Models.MonumentType`) and a new DI-singleton `MonumentIconSource` (package SVG → Svg.Skia raster → cached `Image<Rgba32>`) replace `MonumentIconMap` and the monument/rig halves of the static `MapIcons`. `MapRenderer` gets the source constructor-injected. Non-monument PNGs (cargo, ch47, patrol, player, vendor) stay in `MapIcons`.

**Tech Stack:** .NET 10, xunit, NSubstitute, SixLabors.ImageSharp 3.1.12 (unchanged), RustMapsApi(.Assets) 1.0.0-beta.3, Svg.Skia 5.1.1, SkiaSharp 3.119.4 (+ Linux NoDependencies natives).

**Spec:** `docs/superpowers/specs/2026-07-11-rustmaps-monument-icons-design.md`

## Global Constraints

- Solution file is `RustPlusBot.slnx` (there is no `.sln`). Run `dotnet tool restore` once at branch start.
- **`-maxcpucount:1` is MANDATORY on EVERY `dotnet build` AND `dotnet test`** — ConfigureGitHooks races on `.git/config`, and a broken build silently DROPS an assembly's tests (reports 0 for it, looks "passing"). Always read per-assembly test counts.
- Build must pass `-warnaserror` (0 warnings). CA1305/CA1307/CA1310 → use `CultureInfo.InvariantCulture` / `StringComparison.Ordinal`.
- Tests: plain xUnit `Assert.*` + NSubstitute only — NO FluentAssertions. Test projects have a global `using Xunit` (do not add `using Xunit;` in files).
- Central package management: versions ONLY in `Directory.Packages.props`; csproj files use versionless `<PackageReference>`.
- NEVER bump SixLabors packages (ImageSharp stays 3.1.12, Drawing stays 2.1.7 — v4/v3 are paid-license and hard-fail the build).
- `dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --no-build --verbosity=ERROR` is a hard CI gate — it must produce zero diff before the branch is done.
- All public types/members need XML doc comments (repo convention, analyzer-enforced).
- `docs/superpowers/**` is gitignored — never `git add` the spec or this plan.
- Branch: `feat/rustmaps-monument-icons` off `develop`, plain branch in the main checkout (no worktrees).
- Every commit message ends with: `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`

---

### Task 1: Branch + package plumbing

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: `RustMapsApi.V4.Models.MonumentType` (enum), `RustMapsApi.V4.Assets.MonumentAssets` / `MonumentAssetSource` / `IMonumentAssetSource` / `MonumentAsset`, `Svg.Skia.SKSvg`, `SkiaSharp.*` — available to `RustPlusBot.Features.Map` and (transitively) its test project. Later tasks rely on these compiling.

Background: `RustMapsApi.Assets 1.0.0-beta.3` requires `RustMapsApi >= 1.0.0-beta.3`. The beta.2 → beta.3 bump is verified safe (zero public-API diff between the two packages' XML docs). Svg.Skia 5.1.1 depends on SkiaSharp 3.119.2 and bundles HarfBuzz natives for all platforms, but NOT SkiaSharp Linux natives — hence the explicit `SkiaSharp.NativeAssets.Linux.NoDependencies`. SkiaSharp is also referenced directly, pinned to 3.119.4, so managed and native binaries match versions.

- [ ] **Step 1: Create the branch**

```bash
git checkout develop && git pull && git checkout -b feat/rustmaps-monument-icons
```

- [ ] **Step 2: Add/bump package versions in `Directory.Packages.props`**

In the `<!-- Packages -->` ItemGroup, change the RustMapsApi line and add the Assets package right after it:

```xml
    <PackageVersion Include="RustMapsApi" Version="1.0.0-beta.3" />
    <PackageVersion Include="RustMapsApi.Assets" Version="1.0.0-beta.3" />
```

After the two SixLabors lines, add (keeping alphabetical-ish order — before the SQLitePCLRaw comment block):

```xml
    <PackageVersion Include="SkiaSharp" Version="3.119.4" />
    <PackageVersion Include="SkiaSharp.NativeAssets.Linux.NoDependencies" Version="3.119.4" />
```

After the SQLitePCLRaw line (end of the Packages group):

```xml
    <PackageVersion Include="Svg.Skia" Version="5.1.1" />
```

- [ ] **Step 3: Reference the packages from Features.Map**

In `src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj`, extend the PackageReference ItemGroup:

```xml
  <ItemGroup>
    <PackageReference Include="RustMapsApi" />
    <PackageReference Include="RustMapsApi.Assets" />
    <PackageReference Include="SixLabors.ImageSharp" />
    <PackageReference Include="SixLabors.ImageSharp.Drawing" />
    <PackageReference Include="SkiaSharp" />
    <PackageReference Include="SkiaSharp.NativeAssets.Linux.NoDependencies" />
    <PackageReference Include="Svg.Skia" />
  </ItemGroup>
```

- [ ] **Step 4: Build and run the existing Map tests**

```bash
dotnet build RustPlusBot.slnx
dotnet test tests/RustPlusBot.Features.Map.Tests
```

Expected: build succeeds (proves `RustMapsGenerationDriver` compiles against RustMapsApi beta.3); all existing Map tests pass.

- [ ] **Step 5: Commit**

```bash
git add Directory.Packages.props src/RustPlusBot.Features.Map/RustPlusBot.Features.Map.csproj
git commit -m "$(cat <<'EOF'
Map icons: add RustMapsApi.Assets + Svg.Skia, bump RustMapsApi to beta.3

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: MonumentTokenMap (token → MonumentType)

**Files:**
- Create: `src/RustPlusBot.Features.Map/Assets/MonumentTokenMap.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MonumentTokenMapTests.cs`

**Interfaces:**
- Consumes: `RustMapsApi.V4.Models.MonumentType`, `RustMapsApi.V4.Assets.MonumentAssets` (Task 1 packages).
- Produces: `public static MonumentType? MonumentTokenMap.TypeFor(string? token)` and `internal static IReadOnlyCollection<string> MonumentTokenMap.KnownTokens` (test project sees internals via existing `InternalsVisibleTo`). Tasks 3–4 call `TypeFor`; tests iterate `KnownTokens`.

Note: `MonumentIconMap.cs` (the old PNG-key map) stays alive until Task 4 — `MapIcons` still compiles against it.

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Features.Map.Tests/MonumentTokenMapTests.cs`:

```csharp
using RustMapsApi.V4.Assets;
using RustMapsApi.V4.Models;
using RustPlusBot.Features.Map.Assets;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MonumentTokenMapTests
{
    [Theory]
    [InlineData("airfield_display_name", MonumentType.Airfield)]
    [InlineData("dome_monument_name", MonumentType.SphereTank)]
    [InlineData("mining_outpost_display_name", MonumentType.Warehouse)]
    [InlineData("bandit_camp", MonumentType.BanditTown)]
    [InlineData("radtown", MonumentType.Radtown)]
    [InlineData("jungle_ziggurat", MonumentType.JungleZigguratA)]
    [InlineData("train_tunnel_display_name", MonumentType.TunnelEntrance)]
    [InlineData("train_tunnel_link_display_name", MonumentType.TunnelEntranceTransition)]
    [InlineData("arctic_base_b", MonumentType.ArcticResearchBaseA)]
    [InlineData("stables_a", MonumentType.StablesA)]
    public void TypeFor_maps_known_tokens(string token, MonumentType expected) =>
        Assert.Equal(expected, MonumentTokenMap.TypeFor(token));

    [Fact]
    public void TypeFor_maps_rig_tokens()
    {
        Assert.Equal(MonumentType.OilrigSmall, MonumentTokenMap.TypeFor("oil_rig_small"));
        Assert.Equal(MonumentType.OilrigLarge, MonumentTokenMap.TypeFor("large_oil_rig"));
    }

    [Theory]
    [InlineData("swamp_a")]
    [InlineData("swamp_b")]
    [InlineData("swamp_c")]
    public void TypeFor_matches_swamps_by_prefix(string token) =>
        Assert.Equal(MonumentType.SwampC, MonumentTokenMap.TypeFor(token));

    [Theory]
    [InlineData("underwater_lab")]
    [InlineData("underwater_lab_d")]
    public void TypeFor_matches_underwater_labs_by_prefix(string token) =>
        Assert.Equal(MonumentType.UnderwaterA, MonumentTokenMap.TypeFor(token));

    [Theory]
    [InlineData("definitely_not_a_monument")]
    [InlineData("")]
    [InlineData(null)]
    public void TypeFor_returns_null_for_unknown_or_empty(string? token) =>
        Assert.Null(MonumentTokenMap.TypeFor(token));

    [Fact]
    public void Every_mapped_type_has_a_package_asset()
    {
        // Drift guard: fails loud if a RustMapsApi.Assets update drops art we depend on.
        foreach (var token in MonumentTokenMap.KnownTokens)
        {
            var type = MonumentTokenMap.TypeFor(token);
            Assert.NotNull(type);
            Assert.True(MonumentAssets.HasAsset(type.Value),
                $"{token} → {type} has no asset in RustMapsApi.Assets");
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test tests/RustPlusBot.Features.Map.Tests --filter MonumentTokenMapTests
```

Expected: compile error `CS0103: The name 'MonumentTokenMap' does not exist` (a build failure IS the failing state here).

- [ ] **Step 3: Implement `MonumentTokenMap`**

Create `src/RustPlusBot.Features.Map/Assets/MonumentTokenMap.cs`:

```csharp
using RustMapsApi.V4.Models;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// Maps a Rust+ monument protobuf token to the RustMaps <see cref="MonumentType"/> whose icon
/// represents it. Unknown tokens return null; the caller decides how to report them.
/// </summary>
/// <remarks>
/// Token list sourced from the Rust+ app protocol as catalogued by the previous render map plus the
/// rustplusplus and rustplus.py bot projects. Swamps and underwater labs have no fixed token — Rust+
/// sends prefab names — so those two families match by prefix. Several <see cref="MonumentType"/>
/// values share one asset (e.g. both harbors → Harbor); where the exact variant is unknowable from
/// the token, the pick is arbitrary but fixed.
/// </remarks>
public static class MonumentTokenMap
{
    private static readonly Dictionary<string, MonumentType> Map = new(StringComparer.Ordinal)
    {
        ["AbandonedMilitaryBase"] = MonumentType.MilitaryBaseA,
        ["airfield_display_name"] = MonumentType.Airfield,
        ["arctic_base_a"] = MonumentType.ArcticResearchBaseA,
        ["arctic_base_b"] = MonumentType.ArcticResearchBaseA,
        ["bandit_camp"] = MonumentType.BanditTown,
        ["dome_monument_name"] = MonumentType.SphereTank,
        ["excavator"] = MonumentType.Excavator,
        ["ferryterminal"] = MonumentType.FerryTerminal1,
        ["fishing_village_display_name"] = MonumentType.FishingVillageA,
        ["large_fishing_village_display_name"] = MonumentType.FishingVillageB,
        ["gas_station"] = MonumentType.Gasstation,
        ["harbor_display_name"] = MonumentType.HarborLarge,
        ["harbor_2_display_name"] = MonumentType.HarborSmall,
        ["jungle_ziggurat"] = MonumentType.JungleZigguratA,
        ["junkyard_display_name"] = MonumentType.Junkyard,
        ["large_oil_rig"] = MonumentType.OilrigLarge,
        ["oil_rig_small"] = MonumentType.OilrigSmall,
        ["launchsite"] = MonumentType.LaunchSite,
        ["lighthouse_display_name"] = MonumentType.Lighthouse,
        ["military_tunnels_display_name"] = MonumentType.MilitaryTunnels,
        ["mining_outpost_display_name"] = MonumentType.Warehouse,
        ["mining_quarry_hqm_display_name"] = MonumentType.HqmQuarry,
        ["mining_quarry_stone_display_name"] = MonumentType.StoneQuarry,
        ["mining_quarry_sulfur_display_name"] = MonumentType.SulfurQuarry,
        ["missile_silo_monument"] = MonumentType.NuclearMissileSilo,
        ["outpost"] = MonumentType.Outpost,
        ["power_plant_display_name"] = MonumentType.Powerplant,
        ["radtown"] = MonumentType.Radtown,
        ["satellite_dish_display_name"] = MonumentType.SatelliteDish,
        ["sewer_display_name"] = MonumentType.SewerBranch,
        ["stables_a"] = MonumentType.StablesA,
        ["stables_b"] = MonumentType.StablesB,
        ["supermarket"] = MonumentType.Supermarket,
        ["train_tunnel_display_name"] = MonumentType.TunnelEntrance,
        ["train_tunnel_link_display_name"] = MonumentType.TunnelEntranceTransition,
        ["train_yard_display_name"] = MonumentType.Trainyard,
        ["water_treatment_plant_display_name"] = MonumentType.WaterTreatment,
    };

    /// <summary>All exactly-mapped tokens (excludes the prefix families); exposed for drift-guard tests.</summary>
    internal static IReadOnlyCollection<string> KnownTokens => Map.Keys;

    /// <summary>Gets the RustMaps monument type for a Rust+ token, or null when unmapped.</summary>
    /// <param name="token">The Rust+ monument protobuf token (or prefab name).</param>
    /// <returns>The monument type whose asset represents the token, or null.</returns>
    public static MonumentType? TypeFor(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        if (Map.TryGetValue(token, out var type))
        {
            return type;
        }

        // Rust+ sends prefab names (not fixed tokens) for these families.
        if (token.StartsWith("swamp", StringComparison.Ordinal))
        {
            return MonumentType.SwampC;
        }

        return token.StartsWith("underwater_lab", StringComparison.Ordinal) ? MonumentType.UnderwaterA : null;
    }
}
```

- [ ] **Step 4: Run to verify pass**

```bash
dotnet test tests/RustPlusBot.Features.Map.Tests --filter MonumentTokenMapTests
```

Expected: all MonumentTokenMapTests PASS (existing tests untouched).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Map/Assets/MonumentTokenMap.cs tests/RustPlusBot.Features.Map.Tests/MonumentTokenMapTests.cs
git commit -m "$(cat <<'EOF'
Map icons: MonumentTokenMap (Rust+ token → RustMaps MonumentType) with drift guard

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: MonumentIconSource (SVG → cached Image<Rgba32> + log-once)

**Files:**
- Create: `src/RustPlusBot.Features.Map/Assets/MonumentIconSource.cs`
- Test: `tests/RustPlusBot.Features.Map.Tests/MonumentIconSourceTests.cs`

**Interfaces:**
- Consumes: `MonumentTokenMap.TypeFor(string?)` (Task 2); `IMonumentAssetSource.TryGetAsset(MonumentType, out MonumentAsset)`, `MonumentAsset.OpenStream()`, `new MonumentAssetSource()` (package); `Svg.Skia.SKSvg`; `RigKind` (namespace `RustPlusBot.Abstractions.Events`).
- Produces: `public sealed partial class MonumentIconSource(IMonumentAssetSource assets, ILogger<MonumentIconSource> logger)` with `public Image<Rgba32>? Monument(string token, int size)` and `public Image<Rgba32>? Rig(RigKind kind, int size)`. Task 4 injects this into `MapRenderer` and registers it in DI.

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Features.Map.Tests/MonumentIconSourceTests.cs`:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RustMapsApi.V4.Assets;
using RustPlusBot.Abstractions.Events;
using RustPlusBot.Features.Map.Assets;

namespace RustPlusBot.Features.Map.Tests;

public sealed class MonumentIconSourceTests
{
    private static MonumentIconSource CreateSource() =>
        new(new MonumentAssetSource(), NullLogger<MonumentIconSource>.Instance);

    [Fact]
    public void Monument_rasterizes_known_token_at_requested_size()
    {
        var icon = CreateSource().Monument("launchsite", 30);

        Assert.NotNull(icon);
        Assert.Equal(30, icon!.Width);
        Assert.Equal(30, icon.Height);
    }

    [Fact]
    public void Monument_caches_per_type_and_size()
    {
        var source = CreateSource();

        Assert.Same(source.Monument("launchsite", 30), source.Monument("launchsite", 30));
        Assert.NotSame(source.Monument("launchsite", 30), source.Monument("launchsite", 24));
    }

    [Fact]
    public void Monument_returns_null_and_logs_once_for_unknown_token()
    {
        var logger = new RecordingLogger<MonumentIconSource>();
        var source = new MonumentIconSource(new MonumentAssetSource(), logger);

        Assert.Null(source.Monument("definitely_not_a_monument", 30));
        Assert.Null(source.Monument("definitely_not_a_monument", 30));
        Assert.Null(source.Monument("definitely_not_a_monument", 24));

        Assert.Equal(1, logger.Count(LogLevel.Information));
    }

    [Theory]
    [InlineData(RigKind.Small)]
    [InlineData(RigKind.Large)]
    public void Rig_resolves_for_known_kinds(RigKind kind) =>
        Assert.NotNull(CreateSource().Rig(kind, 30));

    [Fact]
    public void Every_known_token_rasterizes()
    {
        // SVG-engine smoke test across the whole mapping: every token must yield a real image.
        var source = CreateSource();
        foreach (var token in MonumentTokenMap.KnownTokens)
        {
            Assert.True(source.Monument(token, 30) is not null, $"{token} produced no icon");
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly List<LogLevel> _entries = [];

        public int Count(LogLevel level) => _entries.Count(l => l == level);

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => _entries.Add(logLevel);
    }
}
```

- [ ] **Step 2: Run to verify failure**

```bash
dotnet test tests/RustPlusBot.Features.Map.Tests --filter MonumentIconSourceTests
```

Expected: compile error `CS0103: The name 'MonumentIconSource' does not exist`.

- [ ] **Step 3: Implement `MonumentIconSource`**

Create `src/RustPlusBot.Features.Map/Assets/MonumentIconSource.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using RustMapsApi.V4.Assets;
using RustMapsApi.V4.Models;
using RustPlusBot.Abstractions.Events;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SkiaSharp;
using Svg.Skia;

namespace RustPlusBot.Features.Map.Assets;

/// <summary>
/// Serves monument icons from the RustMaps asset package: resolves a Rust+ token to a
/// <see cref="MonumentType"/>, rasterizes the SVG at the requested size, and caches the result.
/// Cached images are immutable inputs the renderer draws from; never mutate them.
/// Register as a singleton.
/// </summary>
/// <param name="assets">The RustMaps monument asset source.</param>
/// <param name="logger">Logs tokens that resolve to no icon, once per token per process.</param>
public sealed partial class MonumentIconSource(IMonumentAssetSource assets, ILogger<MonumentIconSource> logger)
{
    private readonly ConcurrentDictionary<(MonumentType Type, int Size), Image<Rgba32>?> _cache = new();
    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);

    /// <summary>Gets the icon for a monument token scaled to a square box, or null when no icon exists.</summary>
    /// <param name="token">The Rust+ monument protobuf token (or prefab name).</param>
    /// <param name="size">The box edge length in pixels.</param>
    /// <returns>The cached icon, or null (reported once per token).</returns>
    public Image<Rgba32>? Monument(string token, int size)
    {
        var type = MonumentTokenMap.TypeFor(token);
        if (type is null)
        {
            ReportOnce(token, type: null);
            return null;
        }

        return For(type.Value, token, size);
    }

    /// <summary>Gets the rig icon scaled to a square box, or null for an unknown kind.</summary>
    /// <param name="kind">Which rig.</param>
    /// <param name="size">The box edge length in pixels.</param>
    /// <returns>The cached rig icon, or null.</returns>
    public Image<Rgba32>? Rig(RigKind kind, int size) => kind switch
    {
        RigKind.Small => For(MonumentType.OilrigSmall, "oil_rig_small", size),
        RigKind.Large => For(MonumentType.OilrigLarge, "large_oil_rig", size),
        _ => null,
    };

    private Image<Rgba32>? For(MonumentType type, string token, int size)
    {
        var image = _cache.GetOrAdd((type, size), key => Rasterize(key.Type, key.Size));
        if (image is null)
        {
            ReportOnce(token, type);
        }

        return image;
    }

    private void ReportOnce(string token, MonumentType? type)
    {
        if (_reported.TryAdd(token, 0))
        {
            LogNoIcon(token, type);
        }
    }

    private Image<Rgba32>? Rasterize(MonumentType type, int size)
    {
        if (!assets.TryGetAsset(type, out var asset))
        {
            return null;
        }

        try
        {
            using var svg = new SKSvg();
            using (var stream = asset.OpenStream())
            {
                if (svg.Load(stream) is null)
                {
                    return null;
                }
            }

            var bounds = svg.Picture!.CullRect;
            using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);
                canvas.Scale(size / bounds.Width, size / bounds.Height);
                canvas.Translate(-bounds.Left, -bounds.Top);
                canvas.DrawPicture(svg.Picture);
            }

            using var skImage = SKImage.FromBitmap(bitmap);
            using var png = skImage.Encode(SKEncodedImageFormat.Png, 100);
            using var ms = png.AsStream();
            return Image.Load<Rgba32>(ms);
        }
#pragma warning disable CA1031 // Broad catch: a bad SVG must degrade to "no icon", never break a render.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRasterFailed(type, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "No monument icon for token \"{Token}\" (mapped type: {Type}); skipped.")]
    private partial void LogNoIcon(string token, MonumentType? type);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Rasterizing the {Type} icon failed; the monument renders without an icon.")]
    private partial void LogRasterFailed(MonumentType type, Exception ex);
}
```

Notes for the implementer:
- The null result of a failed/missing rasterization is cached, so it is not retried on every render; the Info line still fires only once per token thanks to `_reported`.
- The SVGs are 40×40 viewBox; scaling `size / CullRect` fills the square box exactly, so width == height == size.
- If the repo's analyzers complain about the `#pragma` id or LoggerMessage needs an `EventId`, mirror whatever `RustMapsGenerationDriver.cs` does — it is the in-repo reference for the `[LoggerMessage]` pattern.

- [ ] **Step 4: Run to verify pass**

```bash
dotnet test tests/RustPlusBot.Features.Map.Tests --filter MonumentIconSourceTests
```

Expected: all MonumentIconSourceTests PASS. If SkiaSharp throws `DllNotFoundException` for `libSkiaSharp`, the NativeAssets package from Task 1 is missing/mismatched — fix that, don't work around it.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Map/Assets/MonumentIconSource.cs tests/RustPlusBot.Features.Map.Tests/MonumentIconSourceTests.cs
git commit -m "$(cat <<'EOF'
Map icons: MonumentIconSource — Svg.Skia raster of RustMaps assets, cached, log-once misses

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Wire the renderer, slim MapIcons, delete old assets, register DI

**Files:**
- Modify: `src/RustPlusBot.Features.Map/Rendering/MapRenderer.cs` (class decl ~line 17-22, Render pragma block ~lines 52-64, `DrawMonuments` ~line 214, `DrawRigs` ~line 231)
- Modify: `src/RustPlusBot.Features.Map/Assets/MapIcons.cs` (delete monument/rig members)
- Delete: `src/RustPlusBot.Features.Map/Assets/MonumentIconMap.cs` + 35 monument PNGs
- Modify: `src/RustPlusBot.Features.Map/MapServiceCollectionExtensions.cs:29`
- Modify: `tests/RustPlusBot.Features.Map.Tests/MapRendererTests.cs`, `MapComposerTests.cs`, `MapIconsTests.cs`

**Interfaces:**
- Consumes: `MonumentIconSource` (Task 3): `.Monument(string, int)`, `.Rig(RigKind, int)`; `new MonumentAssetSource()` for tests.
- Produces: `MapRenderer` constructor becomes `MapRenderer(MonumentIconSource monumentIcons)`; DI adds `AddRustMapsAssets()` + `MonumentIconSource` singleton. `MapIcons.Monument`/`MapIcons.Rig`/`MonumentIconMap` cease to exist.

- [ ] **Step 1: Update `MapRenderer` to inject the icon source**

Class declaration gains a primary constructor (keep the existing `<summary>`, add the param doc):

```csharp
/// <summary>
/// Renders the base map tile plus overlay layers to PNG bytes.
/// Stateless per render; safe to register and use as a singleton.
/// </summary>
/// <param name="monumentIcons">Serves monument and rig icons from the RustMaps asset package.</param>
public sealed class MapRenderer(MonumentIconSource monumentIcons)
```

On `Render(...)`: remove the `#pragma warning disable CA1822, S2325` / `#pragma warning restore CA1822, S2325` lines and the `<remarks>Kept as an instance method …</remarks>` doc line — the method now genuinely uses instance state (call sites inside `Render` don't change syntactically).

Replace `DrawMonuments` and `DrawRigs` (drop `static`, call the injected source):

```csharp
    private void DrawMonuments(Image<Rgba32> image, IReadOnlyList<MonumentPlacement> monuments)
    {
        // One Mutate for the whole layer (see DrawMarkers): monuments can be numerous, so a single
        // pipeline beats one Mutate per monument.
        image.Mutate(ctx =>
        {
            foreach (var monument in monuments)
            {
                var icon = monumentIcons.Monument(monument.Token, MapRenderStyle.MonumentIconSize);
                if (icon is not null)
                {
                    ctx.DrawImage(icon, CenterAt(monument.PixelX, monument.PixelY, icon), 1f);
                }
            }
        });
    }

    private void DrawRigs(Image<Rgba32> image, IReadOnlyList<RigPlacement> rigs)
    {
        image.Mutate(ctx =>
        {
            foreach (var rig in rigs)
            {
                var icon = monumentIcons.Rig(rig.Kind, MapRenderStyle.MonumentIconSize);
                if (icon is null)
                {
                    continue;
                }

                ctx.DrawImage(icon, CenterAt(rig.PixelX, rig.PixelY, icon), 1f);

                if (rig.Active)
                {
                    // Active rigs are in their combat window: ring them in red to flag the danger.
                    var radius = (Math.Max(icon.Width, icon.Height) / 2f) + ActiveRingWidth;
                    var ring = new EllipsePolygon(rig.PixelX, rig.PixelY, radius);
                    ctx.Draw(Color.Red, ActiveRingWidth, ring);
                }
            }
        });
    }
```

- [ ] **Step 2: Slim `MapIcons` to the vendored non-monument icons**

In `src/RustPlusBot.Features.Map/Assets/MapIcons.cs`, delete: both `Monument(...)` overloads, both `Rig(...)` overloads (including their `#pragma warning disable RCS1163, IDE0060` blocks), and the now-unused `using RustPlusBot.Abstractions.Events;`. Update the class doc header to:

```csharp
/// <summary>
/// Loads vendored icon assets (embedded PNGs) once and serves them by marker kind.
/// Cached images are immutable inputs the renderer draws from; never mutate them.
/// Monument and rig icons live in <see cref="MonumentIconSource"/>.
/// </summary>
/// <remarks>
/// <c>MapIcons.Marker</c> includes a <c>TravellingVendor</c> arm that returns the embedded vendor.png.
/// <see cref="Vendor"/> is retained as a bridge accessor for callers that reference it directly.
/// </remarks>
```

Everything else (`Marker` x2, `Vendor`, `Player` x2, `KeyFor`, `Load`, `Scaled`, cache) stays.

- [ ] **Step 3: Delete the superseded map + monument PNGs**

```bash
git rm src/RustPlusBot.Features.Map/Assets/MonumentIconMap.cs
cd src/RustPlusBot.Features.Map/Assets/icons
git rm airfield.png arcticresearch.png banditcamp.png dome.png excavator.png ferryterminal.png \
  fishingvillage.png fishingvillagelarge.png gasstation.png harbour.png harbour2.png hqmquarry.png \
  junkyard.png largeoilrig.png launchsite.png lighthouse.png militarybase.png militarytunnel.png \
  miningoutpost.png missilesilo.png oilrig.png outpost.png powerplant.png powerstation.png \
  satellitedish.png sewerbranch.png stable.png stonequarry.png sulfurquarry.png supermarket.png \
  swamp.png traintunnel.png trainyard.png watertreatment.png waterwell.png
cd -
```

Keepers (must survive): `cargo.png`, `ch47.png`, `patrol.png`, `player.png`, `vendor.png`, `LiberationSans-Regular.ttf`. The csproj `EmbeddedResource` glob (`Assets/icons/*.png`) needs no change.

- [ ] **Step 4: Register in DI**

In `src/RustPlusBot.Features.Map/MapServiceCollectionExtensions.cs`, replace line 29 (`services.AddSingleton<MapRenderer>();`) with:

```csharp
        services.AddRustMapsAssets();
        services.AddSingleton<MonumentIconSource>();
        services.AddSingleton<MapRenderer>();
```

(`AddRustMapsAssets` lives in the `Microsoft.Extensions.DependencyInjection` namespace — no new using needed. Add `using RustPlusBot.Features.Map.Assets;` for `MonumentIconSource`.)

- [ ] **Step 5: Update the three test files**

`MapRendererTests.cs` — add usings + a factory helper, and replace all 8 `new MapRenderer()` with `CreateRenderer()`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using RustMapsApi.V4.Assets;
using RustPlusBot.Features.Map.Assets;
```

```csharp
    private static MapRenderer CreateRenderer() =>
        new(new MonumentIconSource(new MonumentAssetSource(), NullLogger<MonumentIconSource>.Instance));
```

(`sed -i 's/new MapRenderer()/CreateRenderer()/' tests/RustPlusBot.Features.Map.Tests/MapRendererTests.cs` covers the call sites.) The assertions themselves stay valid: `launchsite` and `oil_rig_small` are mapped tokens, and icons still fit the `MonumentIconSize` box.

`MapComposerTests.cs` — same usings; in `Build(...)` replace `new MapRenderer()` with:

```csharp
        return new MapComposer(new BaseMapCache([source]), events, rigs, query,
            new MapRenderer(new MonumentIconSource(new MonumentAssetSource(), NullLogger<MonumentIconSource>.Instance)),
            ScopeFactory(settingsStore));
```

`MapIconsTests.cs` — delete the tests `Monument_resolves_known_token`, `Monument_returns_null_for_unknown_token`, `MonumentIconMap_maps_rig_tokens_to_icons`, `Rig_resolves_for_known_kinds`, `Sized_monument_icon_is_scaled_down_from_native` (their subjects moved to `MonumentIconSource`, already covered by Tasks 2–3 tests). Keep `Marker_resolves_for_known_kinds`, `Player_resolves_to_vendored_icon`, `Sized_marker_icon_fits_the_requested_box`, `Sized_icons_are_cached_per_size`. Drop the now-unused `using RustPlusBot.Abstractions.Events;` if nothing else in the file needs it.

- [ ] **Step 6: Run the full Map test project**

```bash
dotnet test tests/RustPlusBot.Features.Map.Tests
```

Expected: PASS across all files (renderer draws the new red-badge icons; pixel-bounds assertions still hold).

- [ ] **Step 7: Commit**

```bash
git add -A src/RustPlusBot.Features.Map tests/RustPlusBot.Features.Map.Tests
git commit -m "$(cat <<'EOF'
Map icons: render monuments/rigs from RustMaps assets; drop 35 vendored PNGs

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Full-solution verification + format gate

**Files:**
- Possibly modified by the formatter: any touched `.cs` file.

**Interfaces:**
- Consumes: everything above.
- Produces: a branch that passes the same gates CI runs.

- [ ] **Step 1: Full build + test suite**

```bash
dotnet build RustPlusBot.slnx --configuration Release
dotnet test RustPlusBot.slnx --configuration Release --no-build
```

Expected: 0 errors/warnings-as-errors; every test project green (baseline was 916 tests — expect a few net-new from Tasks 2–3 minus the 5 removed MapIcons tests).

- [ ] **Step 2: Formatter gate**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile="ReformatAndReorder" --no-build --verbosity=ERROR
git status --short
```

Expected: `git status` clean. If the formatter changed files, inspect + `git add -u && git commit` with message `Map icons: formatter pass` (+ Co-Authored-By footer), then re-run to confirm zero diff.

- [ ] **Step 3: Runtime smoke (recommended)**

Boot the bot against the test guild and eyeball the `#map` render: monuments show red-badge RustMaps icons; grep the log for `No monument icon for token` to catch real-world tokens the table misses (that Info line is the feature's whole point — expect possible hits for jungle ruins or other unmapped prefabs; each is a one-line dictionary fix).

- [ ] **Step 4: Hand off**

Implementation complete → use superpowers:finishing-a-development-branch (PR to `develop`). PR body should mention: full token coverage, the beta.3 bump, the SkiaSharp native dependency, and that unmapped tokens now self-report via Info logs.

---

## Self-review (done at plan time)

- **Spec coverage**: packages/bump (Task 1), token map + prefix rules + drift guard (Task 2), icon source + rasterization + caching + log-once Info + raster-failure Warning (Task 3), renderer injection + rig path + MapIcons diet + PNG/`MonumentIconMap` deletion + DI + test updates (Task 4), full gates + live smoke (Task 5). Error-handling section of the spec maps to Task 3 Step 3 notes.
- **Placeholders**: none — all code complete.
- **Type consistency**: `MonumentTokenMap.TypeFor(string?)` / `KnownTokens` used identically in Tasks 2–4; `MonumentIconSource(IMonumentAssetSource, ILogger<MonumentIconSource>)` ctor consistent across Tasks 3–4; enum member names verified by reflection against the published beta.3 DLL.
