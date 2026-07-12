# SonarQube Smells + Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Drive SonarQube code smells 8 → 0 and coverage 61.2% → ≥80% on `develop`, without the structural switch/alarm/storage dedup.

**Architecture:** Three workstreams — (A) refactor the 5 `S3776`/`S1192` smell sites with behavior-preserving extractions, (B) add coverage exclusions for untestable boilerplate in `Sonar.yml`, (C) add real xUnit tests for the highest-yield testable-but-uncovered logic. Smell refactors in `tools/` are paired with the tests that cover the newly-extracted, individually-testable methods.

**Tech Stack:** C# / .NET 10, xUnit, SonarAnalyzer.CSharp, coverlet (opencover), Discord.Net.

## Global Constraints

- Solution file: `RustPlusBot.slnx` (no `.sln`).
- **`-maxcpucount:1` is MANDATORY on EVERY `dotnet build` and `dotnet test` command in this plan** (append it even where a step's text omits it). `Directory.Build.props` has a `ConfigureGitHooks` target that runs `BeforeTargets="Build"`; parallel multi-project builds RACE on `.git/config` and fail intermittently. A "missing" assembly's tests silently drop from the total — always read per-assembly counts.
- Run `dotnet tool restore` once before any `dotnet jb`/`dotnet ef` (jb, stryker, ef, docfx are local tools).
- `Directory.Build.props`: `TreatWarningsAsErrors=true`, `GenerateDocumentationFile=true`, `AnalysisLevel=latest-all`. New **public** members need XML doc comments; prefer `private`/`internal` for extracted helpers (CA1515). Known analyzer nits to pre-empt: CA1305/CA1307/CA1310 (use `CultureInfo.InvariantCulture` / `StringComparison.Ordinal`), CA1031 (justify broad catch with the existing `#pragma`+comment pattern).
- Tests are plain xUnit `Assert.*` (+ **NSubstitute** where a mock is needed). **NO FluentAssertions.**
- Localization parity gate: `StringsResourceParityTests.Catalog_has_expected_key_count` asserts **260** keys (EN == FR). This plan adds **no** localization keys (Task 1 reuses existing `command.item.*` keys), so parity stays 260 — do not add/remove resx keys.
- Hard CI gate: `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` must produce **no** git diff (`.githooks/pre-push` runs it automatically).
- Branch off `develop` directly in the main checkout (no worktrees): `git switch -c feat/sonar-smells-coverage develop`.
- Refactors in this plan are **behavior-preserving**. Existing tests must stay green; do not change public behavior. Baseline is **739 tests across 16 assemblies** green.
- `docs/superpowers/` is gitignored — never `git add` this plan or the spec.
- Coverage is measured by `dotnet test RustPlusBot.slnx -maxcpucount:1 --collect:"XPlat Code Coverage;Format=opencover"`; Sonar coverage exclusions live ONLY in the `sonar.coverage.exclusions` arg of `.github/workflows/Sonar.yml`.

---

### Task 0: Branch setup

**Files:** none (git only)

- [ ] **Step 1: Create the feature branch off develop**

```bash
cd /home/handys11/Dev/RustPlusBot
git switch -c feat/sonar-smells-coverage develop
```

- [ ] **Step 2: Confirm a clean baseline build + test**

Run: `dotnet build RustPlusBot.slnx -c Debug`
Expected: build succeeds, 0 warnings, 0 errors.

Run: `dotnet test RustPlusBot.slnx -c Debug`
Expected: all tests pass (baseline is green; memory notes ~739 tests/16 projects).

---

### Task 1: `ItemCommandModule` — constants + consolidation (smells S1192 ×3)

Kills the 3 `csharpsquid:S1192` smells and removes this file's 45 duplicated lines by collapsing the four near-identical `RespondFor*Async` methods into one shared embed helper. This file is in `**/Modules/**` (coverage-excluded), so verification is build + analyzer + careful diff review — there is no unit-test seam for `InteractionModuleBase`.

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`

**Interfaces:**

- Consumes: `IItemDatabase` (`Resolve`, `ResolveRaidTarget`, `ResolveSmelter`, `ResolveCctv`, `Sources`), `IItemNameResolver`, `ILocalizer.Get`, the `*Line.Format` formatters — all unchanged.
- Produces: no new public surface. New `private const` fields and one new `private` helper method.

- [ ] **Step 1: Add the three string constants**

At the top of the class body (after the opening brace of `ItemCommandModule`), add:

```csharp
private const string MustBeUsedInServer = "This command must be used in a server.";
private const string AmbiguousKey = "command.item.ambiguous";
private const string NotFoundKey = "command.item.notfound";
```

- [ ] **Step 2: Add a shared embed helper and two text helpers**

Add these private members (the helper cannot be named `RespondAsync` — that is the base-class method):

```csharp
private async Task RespondWithEmbedAsync(
    Func<IItemDatabase, IItemNameResolver, ILocalizer, string, string> describe,
    Func<IItemDatabase, DateOnly> asOf)
{
    if (Context.Guild is null)
    {
        await RespondAsync(MustBeUsedInServer, ephemeral: true).ConfigureAwait(false);
        return;
    }

    var scope = scopeFactory.CreateAsyncScope();
    await using (scope.ConfigureAwait(false))
    {
        var db = scope.ServiceProvider.GetRequiredService<IItemDatabase>();
        var names = scope.ServiceProvider.GetRequiredService<IItemNameResolver>();
        var loc = scope.ServiceProvider.GetRequiredService<ILocalizer>();
        var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
        var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);

        var embed = new EmbedBuilder()
            .WithDescription(describe(db, names, loc, culture))
            .WithFooter($"data as of {asOf(db):yyyy-MM-dd}")
            .Build();
        await RespondAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
    }
}

private static string Ambiguous(ILocalizer loc, string culture, IEnumerable<string> candidates) =>
    loc.Get(AmbiguousKey, culture, string.Join(", ", candidates));

private static string NotFound(ILocalizer loc, string culture, string query) =>
    loc.Get(NotFoundKey, culture, query);
```

- [ ] **Step 3: Rewrite the four `RespondFor*Async` methods as thin describe-delegates**

Replace `RespondForAsync` body:

```csharp
private Task RespondForAsync(
    string query,
    Func<IItemDatabase, DateOnly> dateSelector,
    Func<IItemDatabase, IItemNameResolver, ItemRecord, ILocalizer, string, string> onFound) =>
    RespondWithEmbedAsync(
        (db, names, loc, culture) => db.Resolve(query) switch
        {
            ItemMatch.Found f => onFound(db, names, f.Item, loc, culture),
            ItemMatch.Ambiguous a => Ambiguous(loc, culture, a.Candidates.Select(c => c.Name)),
            _ => NotFound(loc, culture, query),
        },
        dateSelector);
```

Replace `RespondForRaidAsync`:

```csharp
private Task RespondForRaidAsync(string query) =>
    RespondWithEmbedAsync(
        (db, names, loc, culture) => db.ResolveRaidTarget(query) switch
        {
            RaidMatch.Found f => loc.Get("command.durability.ok", culture, DurabilityLine.Format(f.Target, names)),
            RaidMatch.Ambiguous a => Ambiguous(loc, culture, a.Candidates.Select(c => c.Name)),
            _ => NotFound(loc, culture, query),
        },
        db => db.Sources.DurabilityAsOf);
```

Replace `RespondForSmeltAsync`:

```csharp
private Task RespondForSmeltAsync(string query) =>
    RespondWithEmbedAsync(
        (db, names, loc, culture) => db.ResolveSmelter(query) switch
        {
            SmeltMatch.Found f => loc.Get("command.smelt.ok", culture, SmeltLine.Format(f.Smelter, names)),
            SmeltMatch.Ambiguous a => Ambiguous(loc, culture, a.Candidates.Select(c => c.Name)),
            _ => NotFound(loc, culture, query),
        },
        db => db.Sources.SmeltingAsOf);
```

Replace `RespondForCctvAsync` (note: it does not use `names`; ignore that delegate parameter):

```csharp
private Task RespondForCctvAsync(string query) =>
    RespondWithEmbedAsync(
        (db, _, loc, culture) => db.ResolveCctv(query) switch
        {
            CctvMatch.Found f => RenderEmbed(f.Monument, loc, culture),
            CctvMatch.Ambiguous a => Ambiguous(loc, culture, a.Candidates.Select(c => c.Name)),
            _ => NotFound(loc, culture, query),
        },
        db => db.Sources.CctvAsOf);
```

Leave the `[SlashCommand]` methods and `RenderEmbed` unchanged.

- [ ] **Step 4: Build and verify the smells are gone**

Run: `dotnet build src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj -c Debug`
Expected: 0 warnings, 0 errors (no `S1192` analyzer warnings from this file).

- [ ] **Step 5: Format**

Run: `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder --include="**/ItemCommandModule.cs"`
Expected: no git diff after (or apply what it changes).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs
git commit -m "refactor(commands): consolidate ItemCommandModule responders, extract constants (S1192)"
```

---

### Task 2: `DatasetValidator` — extract per-rule methods (smell S3776 63→15) + complete rule coverage

Splits the monolithic `Validate` into one private method per rule (drops cognitive complexity from 63 to ~0) and adds the missing rule tests so the extracted methods are fully covered. `DatasetValidator` is in `tools/` (not coverage-excluded).

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`

**Interfaces:**

- Consumes: `ItemDataset`, `ValidationOptions` (unchanged signatures).
- Produces: `DatasetValidator.Validate(ItemDataset, ValidationOptions)` unchanged public signature; new `private static void Validate*(...)` helpers each appending to a `List<string> errors`.

- [ ] **Step 1: Add the currently-missing rule tests (they should pass against the existing code too — these are characterization tests)**

Append to `DatasetValidatorTests`:

```csharp
[Fact]
public void SmelterWithNoConversions_isError()
{
    var bad = WithSmelters(new Smelter("100", "Furnace", []));
    var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
    Assert.Contains(errors, e => e.Contains("no conversions", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void SmelterConversion_UnknownOutputId_isError()
{
    var bad = WithSmelters(new Smelter("100", "Furnace",
        [new SmeltConversion(1, 424242, 1, 1, 1, 3)]));
    var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
    Assert.Contains(errors, e => e.Contains("424242", StringComparison.Ordinal));
}

[Fact]
public void SmelterConversion_NonPositiveOutputQuantity_isError()
{
    var bad = WithSmelters(new Smelter("100", "Furnace",
        [new SmeltConversion(1, 2, 0, 1, 1, 3)]));
    var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
    Assert.Contains(errors, e => e.Contains("output quantity", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void SmelterConversion_NegativeWood_isError()
{
    var bad = WithSmelters(new Smelter("100", "Furnace",
        [new SmeltConversion(1, 2, 1, 1, -5, 3)]));
    var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
    Assert.Contains(errors, e => e.Contains("wood", StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void MonumentWithEmptyName_isError()
{
    var ds = WithCctv(new CctvMonument(" ", ["DOME1"], false));
    var errors = DatasetValidator.Validate(ds, new ValidationOptions(MinItemCount: 1, MinCctvCount: 1));
    Assert.Contains(errors, e => e.Contains("empty name", StringComparison.OrdinalIgnoreCase));
}
```

> Note: confirm the `SmeltConversion` positional record order against `RustPlusBot.Features.ItemData.Data.SmeltConversion` before finalizing (the existing tests use `new SmeltConversion(InputId, OutputId, OutputQuantity, OutputProbability, WoodQuantity, TimeSeconds)`-style ordering — match it exactly). Adjust the constructed values so only the field under test is invalid.

- [ ] **Step 2: Run the new tests against the un-refactored validator**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: all pass (these characterize existing behavior). If any fail, fix the test's constructed value, not the validator.

- [ ] **Step 3: Refactor `Validate` into per-rule helpers**

Replace the `Validate` method body and add helpers. New `Validate`:

```csharp
public static IReadOnlyList<string> Validate(ItemDataset dataset, ValidationOptions options)
{
    var errors = new List<string>();
    var ids = new HashSet<int>(dataset.Items.Select(i => i.Id));

    ValidateItemCount(dataset, options, errors);
    ValidateRecycleReferences(dataset, ids, errors);
    ValidateCraftReferences(dataset, ids, errors);
    ValidateUpkeep(dataset, ids, errors);
    ValidateDecay(dataset, errors);
    ValidateRaidTargets(dataset, ids, options, errors);
    ValidateSmelters(dataset, ids, options, errors);
    ValidateCctv(dataset, options, errors);

    return errors;
}
```

Then move each existing block verbatim into a matching `private static void` helper, e.g.:

```csharp
private static void ValidateItemCount(ItemDataset dataset, ValidationOptions options, List<string> errors)
{
    if (dataset.Items.Count < options.MinItemCount)
    {
        errors.Add($"item count {dataset.Items.Count} below minimum {options.MinItemCount}");
    }
}

private static void ValidateRecycleReferences(ItemDataset dataset, HashSet<int> ids, List<string> errors)
{
    foreach (var item in dataset.Items.Where(i => i.Recycle is not null))
    {
        foreach (var entry in item.Recycle!.Recycler.Where(e => !ids.Contains(e.ItemId)))
        {
            errors.Add($"item {item.Id} ({item.Name}): recycle yield references unknown id {entry.ItemId}");
        }
    }
}
```

Continue for `ValidateCraftReferences`, `ValidateUpkeep`, `ValidateDecay`, `ValidateRaidTargets`, `ValidateSmelters`, `ValidateCctv` — each is the corresponding block from the original method, unchanged in logic, taking `(dataset, [ids], [options], errors)` as needed.

- [ ] **Step 4: Run tests — all green, behavior unchanged**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: all pass (old + new).

- [ ] **Step 5: Build to confirm S3776 cleared**

Run: `dotnet build tools/RustPlusBot.ItemData.Generator/RustPlusBot.ItemData.Generator.csproj -c Debug`
Expected: 0 warnings (no `S3776` on `DatasetValidator`).

- [ ] **Step 6: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder --include="**/DatasetValidator.cs;**/DatasetValidatorTests.cs"
git add tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs
git commit -m "refactor(generator): split DatasetValidator into per-rule checks (S3776) + cover all rules"
```

---

### Task 3: Generator `Program.cs` — extract helpers (smell S3776 24→15)

`tools/.../Program.cs` is a composition root (will be coverage-excluded in Task 6), so this is a smell-only refactor verified by build + a manual run.

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Program.cs`

**Interfaces:**

- Produces: `private static (string OutPath, string RustplusDir, int MinItems)? ParseArgs(string[] args)` returning `null` when `--out` is absent; `private static List<ItemRecord> BuildItems(...)`. `Main` keeps its `int` return.

- [ ] **Step 1: Extract argument parsing**

Add:

```csharp
private static (string OutPath, string RustplusDir, int MinItems)? ParseArgs(string[] args)
{
    var argList = args.ToList();
    var rustplusDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Dev/rustplusplus/src/staticFiles");
    var minItems = 1000;

    var rustIdx = argList.IndexOf("--rustplusplus");
    if (rustIdx >= 0 && rustIdx + 1 < argList.Count)
    {
        rustplusDir = ExpandHome(argList[rustIdx + 1]);
    }

    var minIdx = argList.IndexOf("--min-items");
    if (minIdx >= 0 && minIdx + 1 < argList.Count)
    {
        minItems = int.Parse(argList[minIdx + 1], System.Globalization.CultureInfo.InvariantCulture);
    }

    var outIdx = argList.IndexOf("--out");
    if (outIdx < 0 || outIdx + 1 >= argList.Count)
    {
        return null;
    }

    return (argList[outIdx + 1], rustplusDir, minItems);
}
```

- [ ] **Step 2: Extract item assembly**

Move the `names.Select(kv => { ... }).ToList()` block (lines ~111-125) into:

```csharp
private static List<ItemRecord> BuildItems(
    IReadOnlyDictionary<int, string> names,
    IReadOnlyDictionary<int, int> stackSizes,
    IReadOnlyDictionary<int, int> despawnSeconds,
    IReadOnlyDictionary<int, RecycleYield> recycleYields,
    IReadOnlyDictionary<int, CraftRecipe> craftRecipes,
    IReadOnlyDictionary<int, ResearchCost> researchCosts,
    IReadOnlyDictionary<int, DecayInfo> decayInfos,
    IReadOnlyDictionary<int, UpkeepCost> upkeepCosts) =>
    names.Select(kv =>
        {
            var id = kv.Key;
            var stackSize = stackSizes.TryGetValue(id, out var ss) ? ss : 1;
            var despawn = despawnSeconds.TryGetValue(id, out var ds) ? (int?)ds : null;
            var recycle = recycleYields.TryGetValue(id, out var ry) ? ry : null;
            var craft = craftRecipes.TryGetValue(id, out var cr) ? cr : null;
            var research = researchCosts.TryGetValue(id, out var rc) ? rc : null;
            var decay = decayInfos.TryGetValue(id, out var di) ? di : null;
            var upkeep = upkeepCosts.TryGetValue(id, out var uc) ? uc : null;
            return new ItemRecord(id, kv.Value, stackSize, despawn, recycle, craft, research, decay, upkeep);
        })
        .ToList();
```

> Confirm the exact dictionary value types against the source loaders' return types before finalizing the parameter types.

- [ ] **Step 3: Rewrite `Main` to use the helpers**

`Main` becomes: `ParseArgs` → if `null`, print usage and `return 1` → construct sources → load → `BuildItems(...)` → build `ItemDataset` → `DatasetValidator.Validate` → emit. The orphan-report `Console.WriteLine` lines may stay inline or move into a small `ReportOrphans` helper if complexity is still ≥15.

- [ ] **Step 4: Build + smoke-run**

Run: `dotnet build tools/RustPlusBot.ItemData.Generator/RustPlusBot.ItemData.Generator.csproj -c Debug`
Expected: 0 warnings (no `S3776` on `Main`).

Run: `dotnet run --project tools/RustPlusBot.ItemData.Generator -- 2>&1 | head -1`
Expected: prints the `Usage: generator --out <path> ...` line and exits 1 (no `--out`), confirming arg handling preserved.

- [ ] **Step 5: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder --include="**/Generator/Program.cs"
git add tools/RustPlusBot.ItemData.Generator/Program.cs
git commit -m "refactor(generator): extract ParseArgs/BuildItems from Main (S3776)"
```

---

### Task 4: `OfflineRustLabsSource` — extract parsers (smells S3776 17→15 & 23→15) + cover recycle/craft/research loaders

Refactors `LoadRecycleYields` and `LoadCraftRecipes` to drop complexity, and adds tests for the three currently-untested loaders (`LoadRecycleYields`, `LoadCraftRecipes`, `LoadResearchCosts` — the ~65 uncovered lines at 33.9%).

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Sources/OfflineRustLabsSource.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/OfflineRustLabsSourceTests.cs`

**Interfaces:**

- Consumes: existing constructor `OfflineRustLabsSource(recycle, craft, research, decay, upkeep)` (file paths).
- Produces: public loader signatures unchanged; new `private static` parse helpers (e.g. `ParseYieldEntries(JsonElement)`, `ParseIngredients(JsonElement)`).

- [ ] **Step 1: Write failing/characterization tests for the three loaders**

Append to `OfflineRustLabsSourceTests` (extend the `SourceWith` helper or add focused builders — recycle/craft/research are the 1st/2nd/3rd constructor paths):

```csharp
private static OfflineRustLabsSource SourceForRecycle(string recycleJson)
{
    var path = Path.GetTempFileName();
    File.WriteAllText(path, recycleJson);
    return new OfflineRustLabsSource(path, path, path, path, path);
}

[Fact]
public void LoadRecycleYields_parsesEntries()
{
    const string json = """{"100":{"recycler":{"yield":[{"id":"200","quantity":4,"probability":1.0}]}}}""";
    var result = SourceForRecycle(json).LoadRecycleYields();
    var yield = Assert.Single(result[100].Recycler);
    Assert.Equal(200, yield.ItemId);
    Assert.Equal(4, yield.Quantity);
}

[Fact]
public void LoadRecycleYields_skipsNullRecycler()
{
    const string json = """{"100":{"recycler":null}}""";
    Assert.Empty(SourceForRecycle(json).LoadRecycleYields());
}

[Fact]
public void LoadCraftRecipes_parsesIngredientsTimeAndWorkbench()
{
    const string json =
        """{"100":{"ingredients":[{"id":"200","quantity":50}],"time":30,"workbench":"-41896755"}}""";
    var result = SourceForRecycle(json).LoadCraftRecipes();
    var recipe = result[100];
    var ing = Assert.Single(recipe.Ingredients);
    Assert.Equal(200, ing.ItemId);
    Assert.Equal(50, ing.Quantity);
    Assert.Equal(2, recipe.WorkbenchLevel); // "-41896755" maps to workbench 2
}

[Fact]
public void LoadCraftRecipes_skipsRecipeWithNoIngredients()
{
    const string json = """{"100":{"ingredients":[],"time":30}}""";
    Assert.Empty(SourceForRecycle(json).LoadCraftRecipes());
}

[Fact]
public void LoadResearchCosts_parsesScrap()
{
    const string json = """{"100":{"researchTable":75}}""";
    var result = SourceForRecycle(json).LoadResearchCosts();
    Assert.Equal(75, result[100].Scrap);
}
```

> Confirm property names (`Recycler`, `Ingredients`, `WorkbenchLevel`, `Scrap`, `ItemId`, `Quantity`) against the records in `RustPlusBot.Features.ItemData.Data` before finalizing; adjust accessors to match.

- [ ] **Step 2: Run them against current code**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj --filter "FullyQualifiedName~OfflineRustLabsSourceTests"`
Expected: pass (they characterize existing behavior). Fix any accessor mismatches in the tests.

- [ ] **Step 3: Extract parse helpers to drop complexity**

In `LoadRecycleYields`, extract the inner `yield`-array loop into:

```csharp
private static List<YieldEntry> ParseYieldEntries(JsonElement yieldEl)
{
    var entries = new List<YieldEntry>();
    foreach (var entry in yieldEl.EnumerateArray())
    {
        var entryIdStr = entry.GetProperty("id").GetString();
        if (entryIdStr is null ||
            !int.TryParse(entryIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId))
        {
            continue;
        }

        entries.Add(new YieldEntry(entryId, entry.GetProperty("quantity").GetInt32(),
            entry.GetProperty("probability").GetDouble()));
    }

    return entries;
}
```

In `LoadCraftRecipes`, extract the ingredient loop into `ParseIngredients(JsonElement ingredientsEl)` and the workbench lookup into `ReadWorkbenchLevel(JsonElement prop)`, mirroring the existing logic exactly. The public loaders keep their `foreach (prop in doc.RootElement.EnumerateObject())` shells but call the helpers, dropping each method under 15.

- [ ] **Step 4: Run tests — green**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: all pass.

- [ ] **Step 5: Build to confirm both S3776 cleared**

Run: `dotnet build tools/RustPlusBot.ItemData.Generator/RustPlusBot.ItemData.Generator.csproj -c Debug`
Expected: 0 warnings.

- [ ] **Step 6: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder --include="**/OfflineRustLabsSource.cs;**/OfflineRustLabsSourceTests.cs"
git add tools/RustPlusBot.ItemData.Generator/Sources/OfflineRustLabsSource.cs tests/RustPlusBot.ItemData.Generator.Tests/OfflineRustLabsSourceTests.cs
git commit -m "refactor(generator): extract RustLabs parsers (S3776) + cover recycle/craft/research loaders"
```

---

### Task 5: `ConnectionSupervisor.PollMarkersAsync` — extract marker-delta publish (smell S3776 16→15)

Only 1 over threshold; extracting the added/removed diff + publish block removes the nested LINQ lambdas and the `if`, dropping complexity below 15. Production code, behavior-preserving.

**Files:**

- Modify: `src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs`

**Interfaces:**

- Produces: `private async Task PublishMarkerDeltaAsync((ulong Guild, Guid Server) key, MapDimensions? dims, IReadOnlyList<MapMarkerSnapshot> previous, IReadOnlyList<MapMarkerSnapshot> current, CancellationToken ct)`.

- [ ] **Step 1: Add the helper method**

```csharp
private async Task PublishMarkerDeltaAsync(
    (ulong Guild, Guid Server) key,
    MapDimensions? dims,
    IReadOnlyList<MapMarkerSnapshot> previous,
    IReadOnlyList<MapMarkerSnapshot> current,
    CancellationToken ct)
{
    var added = current.Where(c => previous.All(p => p.Id != c.Id)).ToList();
    var removed = previous.Where(p => current.All(c => c.Id != p.Id)).ToList();
    if (added.Count > 0 || removed.Count > 0)
    {
        await eventBus.PublishAsync(
                new MapMarkersChangedEvent(key.Guild, key.Server, dims, added, removed), ct)
            .ConfigureAwait(false);
    }
}
```

- [ ] **Step 2: Replace the inline `else` block in `PollMarkersAsync`**

Change the `else { ... }` (lines ~597-607) to:

```csharp
else
{
    await PublishMarkerDeltaAsync(key, dims, previous, current, ct).ConfigureAwait(false);
    previous = current;
}
```

- [ ] **Step 3: Build — behavior preserved, smell cleared**

Run: `dotnet build src/RustPlusBot.Features.Connections/RustPlusBot.Features.Connections.csproj -c Debug`
Expected: 0 warnings (no `S3776` on `PollMarkersAsync`).

- [ ] **Step 4: Run the Connections tests (guard against regression)**

Run: `dotnet test tests/RustPlusBot.Features.Connections.Tests/RustPlusBot.Features.Connections.Tests.csproj`
Expected: all pass (this file is ~70% covered; the marker-delta path is exercised by existing tests).

- [ ] **Step 5: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder --include="**/ConnectionSupervisor.cs"
git add src/RustPlusBot.Features.Connections/Supervisor/ConnectionSupervisor.cs
git commit -m "refactor(connections): extract PublishMarkerDeltaAsync from PollMarkersAsync (S3776)"
```

---

### Task 6: Coverage exclusions in `Sonar.yml` (Part B)

Removes untestable boilerplate from the coverage denominator. These files stay in smell/dup analysis.

**Files:**

- Modify: `.github/workflows/Sonar.yml:63`

- [ ] **Step 1: Enumerate the exact files each glob matches (avoid over-matching tested classes)**

Run:

```bash
cd /home/handys11/Dev/RustPlusBot
echo "== Program.cs =="; find src tools -name Program.cs
echo "== Options POCOs =="; find src -name "*Options.cs" -not -path "*/obj/*"
echo "== ServiceCollectionExtensions =="; find src -name "*ServiceCollectionExtensions.cs" -not -path "*/obj/*"
echo "== Modules/Modals =="; find src -path "*/Modules/*.cs" -not -path "*/obj/*"
echo "== DesignTime =="; find src -name "DesignTimeDbContextFactory.cs"
```

Cross-check each `*Options.cs` hit against the test projects (`grep -rl "OptionsTests" tests`). **`CommandOptions` has `CommandOptionsTests`** — if any matched Options class is tested, exclude that specific class by path instead of via the broad `**/*Options.cs` glob, so covered lines are not dropped from the numerator. Record the final file list.

- [ ] **Step 2: Update the `sonar.coverage.exclusions` argument**

In `.github/workflows/Sonar.yml`, change:

```
/d:sonar.coverage.exclusions="**/tests/**"
```

to (drop or path-pin any Options glob that over-matches a tested class, per Step 1):

```
/d:sonar.coverage.exclusions="**/tests/**,**/Program.cs,**/Modules/**,**/*ServiceCollectionExtensions.cs,**/DesignTimeDbContextFactory.cs,**/DiscordOptions.cs,**/MapOptions.cs,**/WorkspaceOptions.cs"
```

(Use explicit Options-class paths rather than `**/*Options.cs` to protect `CommandOptions`. Adjust the Options list to exactly the untested POCOs found in Step 1.)

- [ ] **Step 3: Validate the YAML is still well-formed**

Run: `python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/Sonar.yml')); print('ok')"`
Expected: `ok`.

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/Sonar.yml
git commit -m "ci(sonar): exclude composition roots, DI wiring, Discord modules, options POCOs from coverage"
```

---

### Task 7: Production coverage push to ≥80% (measured)

This is a measured, iterative task: generate a local coverage report **with the Task 6 exclusions applied**, then write tests against the highest-yield uncovered testable files until the project clears 80%. Exact assertions depend on each target's API, which the implementer reads at write-time; follow the patterns established in Tasks 2 and 4 (construct real inputs, mock collaborators with the project's existing test doubles, assert on observable output/published events).

**Files (test projects to extend — pick by measured yield):**

- `tests/RustPlusBot.Features.Connections.Tests/` — `ConnectionSupervisor` uncovered branches (162 uncovered; biggest single real win).
- `tests/RustPlusBot.Features.Map.Tests/`, `...Events.Tests/`, `...Switches.Tests/`, `...StorageMonitors.Tests/`, `...Alarms.Tests/`, `...Chat.Tests/`, `...Players.Tests/` — hosted services (these already have partial coverage, so the seam exists).
- Channel posters / messengers / gateways: `DiscordChannelMessenger`, `DiscordWorkspaceGateway`, `Discord*ChannelPoster`, `DiscordTeamChatWebhookPoster`.
- Low-coverage command handlers: `CraftCommandHandler`, `ResearchCommandHandler`.

- [ ] **Step 1: Produce a baseline coverage report with exclusions applied**

Run:

```bash
cd /home/handys11/Dev/RustPlusBot
rm -rf ./TestResults
dotnet test RustPlusBot.slnx -c Debug --collect:"XPlat Code Coverage;Format=opencover" --results-directory ./TestResults
```

Then summarize per-file line coverage from the produced `coverage.opencover.xml` files, **excluding** the Task-6 globs (Program.cs, Modules/**, ServiceCollectionExtensions, DesignTime, the named Options POCOs), to get the post-exclusion project number and the ranked list of remaining uncovered testable files.

- [ ] **Step 2: Confirm exclusions already put the project near ~74%**

Expected: post-exclusion line coverage ≈ 72–76%. Record the exact figure and the top ~10 uncovered testable files by uncovered-line count.

- [ ] **Step 3: Write tests for the top targets, highest uncovered-line-count first**

For each target, in its feature test project, following the Task 2/4 pattern:

1. Read the target file and its constructor dependencies.
2. Construct the system under test with the project's existing fakes/mocks (search the test project for an existing test of a sibling class to copy the setup).
3. Write `[Fact]`/`[Theory]` tests asserting observable behavior (returned values, published `eventBus` events, messages sent via the Discord seam) for each currently-uncovered branch.
4. Run that project's tests green before moving on.

Start with `ConnectionSupervisor` (largest yield), then hosted services, then posters/handlers. Commit after each file's tests are green:

```bash
git add tests/<project>/<NewTests>.cs
git commit -m "test(<area>): cover <ClassName> <behaviors>"
```

- [ ] **Step 4: Re-measure after each batch; stop at ≥80%**

Run the Step-1 command again, recompute the post-exclusion project coverage, and continue Step 3 until it reports **≥80%**. Do not write speculative tests past the threshold (YAGNI). Skip `RustPlusSocketSource` (raw socket; low yield-per-effort) unless still short.

- [ ] **Step 5: Final commit for this task**

```bash
git commit --allow-empty -m "test: reach >=80% line coverage (post-exclusion)"
```

---

### Task 8: Whole-solution verification

**Files:** none (verification only)

- [ ] **Step 1: Clean build, zero warnings**

Run: `dotnet build RustPlusBot.slnx -c Release`
Expected: build succeeds, 0 warnings, 0 errors (warnings are errors).

- [ ] **Step 2: Full test run green**

Run: `dotnet test RustPlusBot.slnx -c Release`
Expected: all tests pass, no hangs.

- [ ] **Step 3: Format gate clean**

Run: `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` then `git status --porcelain`
Expected: no diff / empty porcelain output.

- [ ] **Step 4: Confirm all 8 smells are gone locally**

Run: `dotnet build RustPlusBot.slnx -c Debug 2>&1 | grep -E "S1192|S3776" || echo "no S1192/S3776"`
Expected: `no S1192/S3776`.

- [ ] **Step 5: Confirm coverage target**

Re-run the Task 7 Step-1 coverage command; confirm post-exclusion project line coverage is **≥80%**.

- [ ] **Step 6: Push the branch and open a PR (only when the user asks)**

Per repo workflow, open a PR into `develop`. Sonar re-analyzes on merge to `develop` (Community Edition analyzes only the long-lived branch), so the published numbers update post-merge. Note in the PR body the locally-measured smells=0 and coverage figure.

---

## Self-Review

**Spec coverage check:**

- Spec Part A (all 8 smells) → Tasks 1 (S1192 ×3), 2 (S3776 validator), 3 (S3776 generator Main), 4 (S3776 ×2 sources), 5 (S3776 supervisor). ✅ All 8 mapped.
- Spec Part B (coverage exclusions) → Task 6. ✅
- Spec Part C (real tests) → Tasks 2, 4 (generator, concrete) + Task 7 (production push, measured). ✅
- Spec constraints (slnx, warnings-as-errors, jb gate, docs gitignored) → Global Constraints + Task 8. ✅
- Duplication "modest movement" → incidental via Task 1 consolidation; no dedicated task (matches the locked scope). ✅

**Placeholder scan:** Task 7 is intentionally measured (read-then-test), not pre-scripted, because exact assertions depend on per-file APIs the implementer reads at write-time; it carries concrete targets, the pattern to follow, and a hard ≥80% gate rather than vague "add tests." Tasks 1–6 contain literal code. The `> Note:` callouts (record-field-order confirmations) are verification reminders, not missing content.

**Type consistency:** Helper names are referenced consistently (`RespondWithEmbedAsync`, `Ambiguous`/`NotFound`, `Validate*`, `ParseYieldEntries`/`ParseIngredients`, `PublishMarkerDeltaAsync`, `ParseArgs`/`BuildItems`). Match record field/accessor names (`Recycler`, `Ingredients`, `WorkbenchLevel`, `Scrap`, `SmeltConversion` ctor order) against `RustPlusBot.Features.ItemData.Data` at implementation time — flagged in the relevant tasks.
