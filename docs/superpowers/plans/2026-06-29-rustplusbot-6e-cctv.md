# Subsystem 6e — CCTV Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a `/cctv <monument>` (Discord, fixed dropdown) and `!cctv <monument>` (in-game, fuzzy) lookup that lists a monument's Computer Station camera codes — the micro-slice that closes subsystem 6.

**Architecture:** Mirror the 6c/6d "dedicated name-resolved table" shape. CCTV data is **not item-keyed**: a new top-level `Cctv` table on `ItemDataset` (schema v4→v5), sourced offline from rustplusplus `cctv.json` by a new generator source, resolved by name via the shared `NameMatcher`, surfaced by a pure formatter + an in-game handler + a slash command. A drift-guard test reconciles the static slash `[Choice]` dropdown against the generated monument set.

**Tech Stack:** C# / .NET 10, xUnit, Discord.Net 3.20.1 (`Discord.Interactions`), System.Text.Json, embedded-resource bundle, ResX localization.

## Global Constraints

- Solution file is `RustPlusBot.slnx` (not `.sln`). Build with `dotnet build RustPlusBot.slnx`.
- `dotnet jb cleanupcode --profile=ReformatAndReorder` is a hard CI gate — it must produce **no diff**. Run it before the final commit.
- TDD throughout: write the failing test, see it fail, implement minimally, see it pass, commit.
- Do **not** localize game data (monument names) or Discord command metadata (names/descriptions/choices) — those stay English; only **reply bodies** are localized via `ILocalizer`/ResX.
- Formatters under `Features.Commands/Formatting` are **pure** (no `ILocalizer`, no I/O).
- Branch: cut `feat/item-database-5` off `develop` in the main checkout (no worktrees).
- `docs/` is gitignored — never `git add` anything under `docs/`. The generator `README.md` under `tools/` **is** tracked.
- Schema/`DatasetSources`/`ItemDataset` are positional records; adding a member fans out to every constructor call site (compile-time). The bundle's embedded schema version and `EmbeddedItemDatabase.ExpectedSchemaVersion` must move together (Task 4) or the loader throws at static init.

**Preamble (run once before Task 1):**

```bash
cd /home/handys11/Dev/RustPlusBot
git checkout develop && git pull --ff-only
git checkout -b feat/item-database-5
```

---

### Task 1: Schema — `CctvMonument` record, `Cctv` member, `CctvAsOf` provenance

Add the data types and fix every constructor call site so the solution compiles and all existing tests stay green. Keep `ExpectedSchemaVersion = 4` and the bundle untouched — the v4 bundle simply deserializes `Cctv` as null (no code reads it yet).

**Files:**

- Modify: `src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`
- Modify: `tools/RustPlusBot.ItemData.Generator/Program.cs`
- Modify: `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`
- Test: `tests/RustPlusBot.Features.ItemData.Tests/CctvMonumentTests.cs` (create)

**Interfaces:**

- Produces: `record CctvMonument(string Name, IReadOnlyList<string> Codes, bool Dynamic)`; `ItemDataset` gains 6th positional member `IReadOnlyList<CctvMonument> Cctv`; `DatasetSources` gains 9th positional member `DateOnly CctvAsOf`.

- [ ] **Step 1: Write the failing test**

Create `tests/RustPlusBot.Features.ItemData.Tests/CctvMonumentTests.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class CctvMonumentTests
{
    [Fact]
    public void CctvMonument_ExposesNameCodesAndDynamic()
    {
        var monument = new CctvMonument("Dome", ["DOME1", "DOMETOP"], false);
        Assert.Equal("Dome", monument.Name);
        Assert.Equal(2, monument.Codes.Count);
        Assert.Equal("DOME1", monument.Codes[0]);
        Assert.False(monument.Dynamic);
    }
}
```

- [ ] **Step 2: Run test to verify it fails (compile error)**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests --filter CctvMonumentTests`
Expected: FAIL — `CctvMonument` does not exist.

- [ ] **Step 3: Add the record and the two new positional members**

In `src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`, add `Cctv` to `ItemDataset`:

```csharp
public sealed record ItemDataset(
    int SchemaVersion,
    DatasetSources Sources,
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<RaidTarget> RaidTargets,
    IReadOnlyList<Smelter> Smelters,
    IReadOnlyList<CctvMonument> Cctv);
```

Update its XML doc with `/// <param name="Cctv">Every monument and its Computer Station CCTV codes.</param>`.

Add `CctvAsOf` to `DatasetSources` (and its `/// <param name="CctvAsOf">CCTV codes source date.</param>`):

```csharp
public sealed record DatasetSources(
    DateOnly NamesAsOf,
    DateOnly RecycleAsOf,
    DateOnly CraftAsOf,
    DateOnly ResearchAsOf,
    DateOnly DecayAsOf,
    DateOnly UpkeepAsOf,
    DateOnly DurabilityAsOf,
    DateOnly SmeltingAsOf,
    DateOnly CctvAsOf);
```

Add the new record at the end of the file:

```csharp
/// <summary>One monument and the CCTV codes for its Computer Station cameras.</summary>
/// <param name="Name">The monument display name (e.g. "Small Oil Rig", "Underwater Labs").</param>
/// <param name="Codes">The camera codes, in source order; always non-empty. Wildcard codes keep
/// literal asterisks (e.g. "COMPOUND******").</param>
/// <param name="Dynamic">True when the codes contain a per-map numerical wildcard (the asterisks).</param>
public sealed record CctvMonument(string Name, IReadOnlyList<string> Codes, bool Dynamic);
```

- [ ] **Step 4: Fix the generator call site so it compiles (schema stays 4)**

In `tools/RustPlusBot.ItemData.Generator/Program.cs`, add the provenance constant beside `SmeltingAsOf`:

```csharp
    private static readonly DateOnly CctvAsOf = new(2025, 11, 12);
```

(This is the real upstream `cctv.json` last-change date — Task 4 reuses it unchanged; only the schema bump and source wiring change there.)

Update the dataset construction (still schema `4`, pass empty `Cctv`):

```csharp
        var dataset = new ItemDataset(
            4,
            new DatasetSources(NamesAsOf, RecycleAsOf, CraftAsOf, ResearchAsOf, DecayAsOf, UpkeepAsOf, DurabilityAsOf,
                SmeltingAsOf, CctvAsOf),
            items,
            raidTargets,
            smelters,
            []);
```

- [ ] **Step 5: Fix the validator test call sites so they compile**

In `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`:

Update `Good()` — add `CctvAsOf` to the `DatasetSources` and a trailing `[]` to the `ItemDataset`:

```csharp
    private static ItemDataset Good() => new(1,
        new DatasetSources(new(2026, 4, 8), new(2024, 9, 7), new(2024, 9, 7), new(2024, 9, 7), new(2024, 9, 7),
            new(2024, 9, 7), new(2024, 9, 7), new(2023, 11, 5), new(2025, 11, 12)),
        [
            new ItemRecord(1, "AK-47", 1, 3600,
                new RecycleYield([new YieldEntry(2, 4, 1.0)]), null, null, null, null),
            new ItemRecord(2, "Metal Fragments", 1000, null, null, null, null, null, null),
        ],
        [],
        [],
        []);
```

Update `WithRaid` and `WithSmelters` to pass the new trailing `[]`:

```csharp
    private static ItemDataset WithRaid(params RaidTarget[] raid) =>
        new(3, Good().Sources, Good().Items, raid, [], []);

    private static ItemDataset WithSmelters(params Smelter[] smelters) =>
        new(4, Good().Sources, Good().Items, [], smelters, []);
```

Add a trailing `[]` to each inline `new ItemDataset(...)` in the file (the three at the original lines ~46, ~61, ~76, ~91, ~106 — every `new ItemDataset(... , [], [])` becomes `... , [], [], []`). Find them:

```bash
grep -n "new ItemDataset(" tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs
```

Each ends with two `[]` arguments (raid, smelters); add a third `[]` (cctv) to each.

- [ ] **Step 6: Run the new test + full affected suites to verify green**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests tests/RustPlusBot.ItemData.Generator.Tests`
Expected: PASS (CctvMonumentTests passes; validator tests still pass; build clean).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs \
        tools/RustPlusBot.ItemData.Generator/Program.cs \
        tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs \
        tests/RustPlusBot.Features.ItemData.Tests/CctvMonumentTests.cs
git commit -m "feat(6e): add CctvMonument schema + CctvAsOf provenance"
```

---

### Task 2: Generator source — `ICctvSource` + `OfflineCctvSource`

Read `cctv.json`, un-escape the markdown-escaped asterisks (`\*` → `*`), preserve code order, carry the `dynamic` flag, drop monuments with no codes.

**Files:**

- Create: `tools/RustPlusBot.ItemData.Generator/Sources/ICctvSource.cs`
- Create: `tools/RustPlusBot.ItemData.Generator/Sources/OfflineCctvSource.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/OfflineCctvSourceTests.cs` (create)

**Interfaces:**

- Consumes: `CctvMonument` (Task 1).
- Produces: `interface ICctvSource { IReadOnlyList<CctvMonument> LoadMonuments(); }`; `class OfflineCctvSource(string CctvFilePath) : ICctvSource`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.ItemData.Generator.Tests/OfflineCctvSourceTests.cs`:

```csharp
using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineCctvSourceTests
{
    private static OfflineCctvSource SourceWith(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return new OfflineCctvSource(path);
    }

    [Fact]
    public void LoadMonuments_ProjectsCodesInOrder_AndDynamicFlag()
    {
        const string json = """
                            {
                              "Small Oil Rig": { "codes": ["OILRIG1HELI", "OILRIG1DOCK"], "dynamic": false }
                            }
                            """;
        var monument = Assert.Single(SourceWith(json).LoadMonuments());
        Assert.Equal("Small Oil Rig", monument.Name);
        Assert.Equal(["OILRIG1HELI", "OILRIG1DOCK"], monument.Codes);
        Assert.False(monument.Dynamic);
    }

    [Fact]
    public void LoadMonuments_UnescapesAsterisks_PreservingCount()
    {
        const string json = """
                            {
                              "Underwater Labs": { "codes": ["AUXPOWER\\*\\*\\*\\*"], "dynamic": true }
                            }
                            """;
        var monument = Assert.Single(SourceWith(json).LoadMonuments());
        Assert.True(monument.Dynamic);
        Assert.Equal("AUXPOWER****", monument.Codes[0]);
    }

    [Fact]
    public void LoadMonuments_DropsMonumentWithNoCodes()
    {
        const string json = """{ "Empty": { "codes": [], "dynamic": false } }""";
        Assert.Empty(SourceWith(json).LoadMonuments());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests --filter OfflineCctvSourceTests`
Expected: FAIL — `OfflineCctvSource` does not exist.

- [ ] **Step 3: Write the interface**

Create `tools/RustPlusBot.ItemData.Generator/Sources/ICctvSource.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides monument CCTV camera codes from the rustplusplus static data.</summary>
internal interface ICctvSource
{
    /// <summary>Loads every monument with at least one camera code.</summary>
    /// <returns>The monuments, in source order, with un-escaped codes.</returns>
    IReadOnlyList<CctvMonument> LoadMonuments();
}
```

- [ ] **Step 4: Write the offline implementation**

Create `tools/RustPlusBot.ItemData.Generator/Sources/OfflineCctvSource.cs`:

```csharp
using System.Text.Json;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads monument CCTV codes from the offline <c>cctv.json</c> file, keyed by monument name.</summary>
/// <param name="CctvFilePath">Path to the <c>cctv.json</c> file.</param>
internal sealed class OfflineCctvSource(string CctvFilePath) : ICctvSource
{
    /// <inheritdoc/>
    public IReadOnlyList<CctvMonument> LoadMonuments()
    {
        using var stream = File.OpenRead(CctvFilePath);
        using var doc = JsonDocument.Parse(stream);

        var monuments = new List<CctvMonument>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var codes = ReadCodes(prop.Value);
            if (codes.Count == 0)
            {
                continue; // monument with no codes — drop
            }

            var dynamic = prop.Value.TryGetProperty("dynamic", out var d) && d.ValueKind == JsonValueKind.True;
            monuments.Add(new CctvMonument(prop.Name, codes, dynamic));
        }

        return monuments;
    }

    private static List<string> ReadCodes(JsonElement entry)
    {
        var codes = new List<string>();
        if (!entry.TryGetProperty("codes", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return codes;
        }

        foreach (var element in arr.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } raw)
            {
                codes.Add(Unescape(raw));
            }
        }

        return codes;
    }

    // cctv.json stores codes markdown-escaped for Discord ("COMPOUND\*\*..."); strip the escape.
    private static string Unescape(string code) => code.Replace("\\*", "*", StringComparison.Ordinal);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests --filter OfflineCctvSourceTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Sources/ICctvSource.cs \
        tools/RustPlusBot.ItemData.Generator/Sources/OfflineCctvSource.cs \
        tests/RustPlusBot.ItemData.Generator.Tests/OfflineCctvSourceTests.cs
git commit -m "feat(6e): add offline CCTV generator source"
```

---

### Task 3: Validator — `MinCctvCount` + CCTV structural checks

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`

**Interfaces:**

- Consumes: `ItemDataset.Cctv`, `CctvMonument` (Task 1).
- Produces: `ValidationOptions` gains `int MinCctvCount = 0`.

- [ ] **Step 1: Write the failing tests**

In `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`, add a `WithCctv` builder beside `WithSmelters`:

```csharp
    private static ItemDataset WithCctv(params CctvMonument[] cctv) =>
        new(5, Good().Sources, Good().Items, [], [], cctv);
```

Add these tests (place beside the smelter tests):

```csharp
    [Fact]
    public void GoodCctv_hasNoErrors()
    {
        var ds = WithCctv(new CctvMonument("Dome", ["DOME1"], false));
        var errors = DatasetValidator.Validate(ds, new ValidationOptions(MinItemCount: 1, MinCctvCount: 1));
        Assert.Empty(errors);
    }

    [Fact]
    public void TooFewMonuments_isError()
    {
        var ds = WithCctv(new CctvMonument("Dome", ["DOME1"], false));
        var errors = DatasetValidator.Validate(ds, new ValidationOptions(MinItemCount: 1, MinCctvCount: 8));
        Assert.Contains(errors, e => e.Contains("cctv monument count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MonumentWithNoCodes_isError()
    {
        var ds = WithCctv(new CctvMonument("Dome", [], false));
        var errors = DatasetValidator.Validate(ds, new ValidationOptions(MinItemCount: 1, MinCctvCount: 1));
        Assert.Contains(errors, e => e.Contains("no codes", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MonumentWithEmptyCode_isError()
    {
        var ds = WithCctv(new CctvMonument("Dome", ["  "], false));
        var errors = DatasetValidator.Validate(ds, new ValidationOptions(MinItemCount: 1, MinCctvCount: 1));
        Assert.Contains(errors, e => e.Contains("empty code", StringComparison.OrdinalIgnoreCase));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests --filter DatasetValidatorTests`
Expected: FAIL — `MinCctvCount` not a member of `ValidationOptions`; CCTV errors not produced.

- [ ] **Step 3: Add the option + checks**

In `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`, extend `ValidationOptions`:

```csharp
internal sealed record ValidationOptions(int MinItemCount, int MinRaidTargetCount = 0, int MinSmelterCount = 0,
    int MinCctvCount = 0);
```

Add its `/// <param name="MinCctvCount">The minimum number of CCTV monuments the dataset must contain.</param>`.

Append the CCTV block at the end of `Validate`, just before `return errors;`:

```csharp
        var cctv = dataset.Cctv ?? [];
        if (cctv.Count < options.MinCctvCount)
        {
            errors.Add($"cctv monument count {cctv.Count} below minimum {options.MinCctvCount}");
        }

        foreach (var monument in cctv)
        {
            if (string.IsNullOrWhiteSpace(monument.Name))
            {
                errors.Add("cctv monument has an empty name");
            }

            if (monument.Codes.Count == 0)
            {
                errors.Add($"cctv monument {monument.Name}: has no codes");
            }

            foreach (var code in monument.Codes.Where(string.IsNullOrWhiteSpace))
            {
                errors.Add($"cctv monument {monument.Name}: has an empty code");
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests --filter DatasetValidatorTests`
Expected: PASS (existing + 4 new).

- [ ] **Step 5: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs \
        tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs
git commit -m "feat(6e): validate CCTV monuments in the generator"
```

---

### Task 4: Wire the generator + regenerate the v5 bundle + bump the loader

Wire the CCTV source into `Program.cs`, stamp the real provenance, bump the emitted schema to 5, regenerate `item-data.json`, then bump `EmbeddedItemDatabase.ExpectedSchemaVersion` to 5. The regen and the loader bump land **together** so the loader never rejects the bundle.

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Program.cs`
- Modify: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`
- Modify (generated): `src/RustPlusBot.Features.ItemData/Data/item-data.json`
- Test: `tests/RustPlusBot.Features.ItemData.Tests/CctvBundleTests.cs` (create)

**Interfaces:**

- Consumes: `OfflineCctvSource` (Task 2), `ValidationOptions.MinCctvCount` (Task 3).

- [ ] **Step 1: Wire the source and bump the emitted schema**

In `tools/RustPlusBot.ItemData.Generator/Program.cs` (the `CctvAsOf = new(2025, 11, 12)` constant is already correct from Task 1 — leave it as-is):

After the `smeltingSource` line, build the CCTV source:

```csharp
        var cctvSource = new OfflineCctvSource(Path.Combine(rustplusDir, "cctv.json"));
```

After the `smelters` load + log lines, load monuments:

```csharp
        var cctvMonuments = cctvSource.LoadMonuments();
        Console.WriteLine($"Loaded {cctvMonuments.Count} cctv monuments");
```

Change the dataset construction to schema `5` and pass the monuments:

```csharp
        var dataset = new ItemDataset(
            5,
            new DatasetSources(NamesAsOf, RecycleAsOf, CraftAsOf, ResearchAsOf, DecayAsOf, UpkeepAsOf, DurabilityAsOf,
                SmeltingAsOf, CctvAsOf),
            items,
            raidTargets,
            smelters,
            cctvMonuments);
```

Add the floor to `validationOptions`:

```csharp
        var validationOptions = new ValidationOptions(MinItemCount: minItems, MinRaidTargetCount: 300,
            MinSmelterCount: 8, MinCctvCount: 8);
```

- [ ] **Step 2: Run the generator to regenerate the bundle**

Run:

```bash
dotnet run --project tools/RustPlusBot.ItemData.Generator -- \
  --out src/RustPlusBot.Features.ItemData/Data/item-data.json \
  --rustplusplus ~/Dev/rustplusplus/src/staticFiles
```

Expected: prints `Loaded 11 cctv monuments` (or the current count) and `Emitted <N> items …`, exit 0. Confirm the new section landed:

```bash
grep -c "OILRIG1HELI" src/RustPlusBot.Features.ItemData/Data/item-data.json
grep -o '"schemaVersion": *5' src/RustPlusBot.Features.ItemData/Data/item-data.json
```

Expected: the first prints `1`; the second prints `"schemaVersion": 5`.

- [ ] **Step 3: Bump the loader's expected schema version**

In `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`:

```csharp
    private const int ExpectedSchemaVersion = 5;
```

- [ ] **Step 4: Write the bundle smoke test**

Create `tests/RustPlusBot.Features.ItemData.Tests/CctvBundleTests.cs`:

```csharp
using RustPlusBot.Features.ItemData;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class CctvBundleTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();

    [Fact]
    public void Bundle_LoadsAtLeastEightMonuments() =>
        Assert.True(_db.CctvMonuments.Count >= 8);

    [Fact]
    public void Bundle_HasCctvProvenanceDate() =>
        Assert.Equal(new DateOnly(2025, 11, 12), _db.Sources.CctvAsOf);
}
```

> This test references `IItemDatabase.CctvMonuments`, added in Task 5. If executing strictly in order, this step's test will not compile until Task 5 — so either (a) implement the `CctvMonuments` accessor (Task 5, Step 3 body) now, or (b) run only the existing suite here and let Task 5 turn this test green. Recommended: do (a) — pull the accessor forward — since the bundle is the natural place to first read it.

If choosing (a), apply the `IItemDatabase` + `EmbeddedItemDatabase` accessor changes from Task 5 Step 3 (the `CctvMonuments` property only) before running.

- [ ] **Step 5: Run the smoke test + full ItemData suite**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests`
Expected: PASS — bundle loads as v5 (no schema-mismatch throw), `CctvMonuments` populated, provenance is `2025-11-12`.

- [ ] **Step 6: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Program.cs \
        src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs \
        src/RustPlusBot.Features.ItemData/Data/item-data.json \
        tests/RustPlusBot.Features.ItemData.Tests/CctvBundleTests.cs
git commit -m "feat(6e): regenerate v5 bundle with CCTV table + bump loader"
```

---

### Task 5: Lookup — `CctvMatch`, `CctvLookup`, `IItemDatabase.ResolveCctv` + `CctvMonuments`

**Files:**

- Create: `src/RustPlusBot.Features.ItemData/Lookup/CctvMatch.cs`
- Create: `src/RustPlusBot.Features.ItemData/Lookup/CctvLookup.cs`
- Modify: `src/RustPlusBot.Features.ItemData/IItemDatabase.cs`
- Modify: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`
- Test: `tests/RustPlusBot.Features.ItemData.Tests/CctvLookupTests.cs` (create)

**Interfaces:**

- Consumes: `NameMatcher.Resolve<T>` (internal), `CctvMonument`.
- Produces: `abstract record CctvMatch { Found(CctvMonument) | Ambiguous(IReadOnlyList<CctvMonument>) | NotFound }`; `static CctvMatch CctvLookup.Resolve(string query, IReadOnlyList<CctvMonument> all, int cap = 10)`; `IItemDatabase` gains `IReadOnlyList<CctvMonument> CctvMonuments { get; }` and `CctvMatch ResolveCctv(string query)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Features.ItemData.Tests/CctvLookupTests.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class CctvLookupTests
{
    private static readonly CctvMonument Dome = new("Dome", ["DOME1"], false);
    private static readonly CctvMonument SmallRig = new("Small Oil Rig", ["OILRIG1HELI"], false);
    private static readonly CctvMonument LargeRig = new("Large Oil Rig", ["OILRIG2HELI"], false);
    private static readonly IReadOnlyList<CctvMonument> All = [Dome, SmallRig, LargeRig];

    private static CctvMatch Resolve(string q) => CctvLookup.Resolve(q, All);

    [Fact]
    public void ExactName_Found()
    {
        var found = Assert.IsType<CctvMatch.Found>(Resolve("Dome"));
        Assert.Equal("Dome", found.Monument.Name);
    }

    [Fact]
    public void UniqueSubstring_Found()
    {
        var found = Assert.IsType<CctvMatch.Found>(Resolve("small"));
        Assert.Equal("Small Oil Rig", found.Monument.Name);
    }

    [Fact]
    public void MultipleSubstring_Ambiguous()
    {
        var amb = Assert.IsType<CctvMatch.Ambiguous>(Resolve("oil rig"));
        Assert.True(amb.Candidates.Count >= 2);
    }

    [Fact]
    public void NoMatch_NotFound() => Assert.IsType<CctvMatch.NotFound>(Resolve("zzzzz"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_NotFound(string q) => Assert.IsType<CctvMatch.NotFound>(Resolve(q));
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests --filter CctvLookupTests`
Expected: FAIL — `CctvLookup` / `CctvMatch` do not exist.

- [ ] **Step 3: Implement the union, the lookup, and the interface members**

Create `src/RustPlusBot.Features.ItemData/Lookup/CctvMatch.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The outcome of resolving a user query to a CCTV monument.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "Discriminated-union pattern: nested sealed records are the intended public surface.")]
public abstract record CctvMatch
{
    private CctvMatch() { }

    /// <summary>Exactly one monument resolved.</summary>
    /// <param name="Monument">The resolved monument.</param>
    public sealed record Found(CctvMonument Monument) : CctvMatch;

    /// <summary>Several monuments matched; present candidates for disambiguation.</summary>
    /// <param name="Candidates">The candidate monuments, capped and ranked.</param>
    public sealed record Ambiguous(IReadOnlyList<CctvMonument> Candidates) : CctvMatch;

    /// <summary>No monument matched.</summary>
    public sealed record NotFound : CctvMatch;
}
```

Create `src/RustPlusBot.Features.ItemData/Lookup/CctvLookup.cs` (CCTV has no ids, so the matcher's id lookup is a constant null):

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name resolution over CCTV monuments. Exact name beats substring.</summary>
public static class CctvLookup
{
    private static readonly IComparer<CctvMonument> ByName =
        Comparer<CctvMonument>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name));

    /// <summary>Resolves a user query to a CCTV monument.</summary>
    /// <param name="query">The raw user input (monument name).</param>
    /// <param name="all">All monuments, for name matching.</param>
    /// <param name="cap">The maximum number of ambiguous candidates to return.</param>
    /// <returns>A <see cref="CctvMatch"/> describing the outcome.</returns>
    public static CctvMatch Resolve(string query, IReadOnlyList<CctvMonument> all, int cap = 10)
    {
        var (kind, single, candidates) =
            NameMatcher.Resolve(query, static _ => (CctvMonument?)null, all, m => m.Name, ByName, cap);
        return kind switch
        {
            NameMatchKind.Found => new CctvMatch.Found(single!),
            NameMatchKind.Ambiguous => new CctvMatch.Ambiguous(candidates),
            _ => new CctvMatch.NotFound(),
        };
    }
}
```

In `src/RustPlusBot.Features.ItemData/IItemDatabase.cs`, add the two members inside the interface:

```csharp
    /// <summary>All CCTV monuments, in dataset order (used to reconcile the fixed slash dropdown).</summary>
    IReadOnlyList<CctvMonument> CctvMonuments { get; }

    /// <summary>Resolves a user query (monument name) to a CCTV monument.</summary>
    /// <param name="query">The raw user input.</param>
    /// <returns>A match describing the outcome.</returns>
    CctvMatch ResolveCctv(string query);
```

In `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`, add the backing field (beside `Smelters`):

```csharp
    private static readonly IReadOnlyList<CctvMonument> CctvList = Dataset.Cctv ?? [];
```

and the two members (beside `ResolveSmelter`):

```csharp
    /// <inheritdoc />
    public IReadOnlyList<CctvMonument> CctvMonuments => CctvList;

    /// <inheritdoc />
    public CctvMatch ResolveCctv(string query) => CctvLookup.Resolve(query, CctvList);
```

> If you already pulled the accessor forward in Task 4 (option a), only add `ResolveCctv` + `CctvList` here and skip what already exists.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests`
Expected: PASS (CctvLookupTests + CctvBundleTests + existing).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.ItemData/Lookup/CctvMatch.cs \
        src/RustPlusBot.Features.ItemData/Lookup/CctvLookup.cs \
        src/RustPlusBot.Features.ItemData/IItemDatabase.cs \
        src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs \
        tests/RustPlusBot.Features.ItemData.Tests/CctvLookupTests.cs
git commit -m "feat(6e): add CCTV lookup (ResolveCctv + CctvMonuments)"
```

---

### Task 6: Formatter + in-game `!cctv` handler + EN/FR strings + help/registration

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Formatting/CctvLine.cs`
- Create: `src/RustPlusBot.Features.Commands/Handlers/CctvCommandHandler.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx`
- Modify: `src/RustPlusBot.Localization/Strings.fr.resx`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Formatting/CctvFormatterTests.cs` (create)
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/CctvHandlerTests.cs` (create)
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`
- Modify: `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs`

**Interfaces:**

- Consumes: `CctvMonument`, `IItemDatabase.ResolveCctv`, `CctvMatch`, `ILocalizer`, `CommandContext`, `ICommandHandler`.
- Produces: `static string CctvLine.Format(CctvMonument monument)`; `class CctvCommandHandler(IItemDatabase, ILocalizer) : ICommandHandler` with `Name => "cctv"`.

- [ ] **Step 1: Write the failing formatter + handler tests**

Create `tests/RustPlusBot.Features.Commands.Tests/Formatting/CctvFormatterTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class CctvFormatterTests
{
    [Fact]
    public void CctvLine_RendersHeaderThenCodesInOrder()
    {
        var monument = new CctvMonument("Small Oil Rig", ["OILRIG1HELI", "OILRIG1DOCK"], false);
        var line = CctvLine.Format(monument);
        Assert.Equal("Small Oil Rig CCTV:\nOILRIG1HELI\nOILRIG1DOCK", line);
    }

    [Fact]
    public void CctvLine_DoesNotAppendNote_EvenWhenDynamic()
    {
        var monument = new CctvMonument("Underwater Labs", ["LAB****"], true);
        var line = CctvLine.Format(monument);
        Assert.Equal("Underwater Labs CCTV:\nLAB****", line);
    }
}
```

Create `tests/RustPlusBot.Features.Commands.Tests/Handlers/CctvHandlerTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class CctvHandlerTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly ILocalizer _loc = new ResxLocalizer();

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Name_is_cctv() => Assert.Equal("cctv", new CctvCommandHandler(_db, _loc).Name);

    [Fact]
    public async Task Found_returnsCodes()
    {
        var reply = await new CctvCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Small", "Oil", "Rig"), CancellationToken.None);
        Assert.Contains("Small Oil Rig CCTV:", reply, StringComparison.Ordinal);
        Assert.Contains("OILRIG1HELI", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DynamicMonument_appendsNote()
    {
        var reply = await new CctvCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Underwater", "Labs"), CancellationToken.None);
        Assert.Contains("every map", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonDynamicMonument_hasNoNote()
    {
        var reply = await new CctvCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Dome"), CancellationToken.None);
        Assert.DoesNotContain("every map", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotFound_returnsMessage()
    {
        var reply = await new CctvCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter "CctvFormatterTests|CctvHandlerTests"`
Expected: FAIL — `CctvLine` / `CctvCommandHandler` do not exist.

- [ ] **Step 3: Add the EN/FR resx strings**

In `src/RustPlusBot.Localization/Strings.resx`, add after the `command.smelt.ok` `<data>` node:

```xml
  <data name="command.cctv.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
  <data name="command.cctv.note" xml:space="preserve">
    <value>* = a numerical code that differs on every map.</value>
  </data>
```

After the `help.smelt` node:

```xml
  <data name="help.cctv" xml:space="preserve">
    <value>CCTV camera codes for a monument</value>
  </data>
```

After the `help.slash.smelt` node:

```xml
  <data name="help.slash.cctv" xml:space="preserve">
    <value>Show the CCTV camera codes for a monument.</value>
  </data>
```

In `src/RustPlusBot.Localization/Strings.fr.resx`, mirror with French values:

```xml
  <data name="command.cctv.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
  <data name="command.cctv.note" xml:space="preserve">
    <value>* = un code numérique différent pour chaque carte.</value>
  </data>
```

```xml
  <data name="help.cctv" xml:space="preserve">
    <value>Codes de vidéosurveillance d'un monument</value>
  </data>
```

```xml
  <data name="help.slash.cctv" xml:space="preserve">
    <value>Afficher les codes de vidéosurveillance d'un monument.</value>
  </data>
```

- [ ] **Step 4: Add the formatter**

Create `src/RustPlusBot.Features.Commands/Formatting/CctvLine.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats a monument's CCTV reply: a header line then one code per line (source order).</summary>
internal static class CctvLine
{
    /// <summary>Lists a monument's camera codes under a header. The wildcard note is added by the
    /// caller (it is localized and surface-specific), not here.</summary>
    /// <param name="monument">The monument (with at least one code).</param>
    public static string Format(CctvMonument monument)
    {
        ArgumentNullException.ThrowIfNull(monument);
        return string.Create(CultureInfo.InvariantCulture,
            $"{monument.Name} CCTV:\n{string.Join("\n", monument.Codes)}");
    }
}
```

- [ ] **Step 5: Add the in-game handler**

Create `src/RustPlusBot.Features.Commands/Handlers/CctvCommandHandler.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!cctv — lists a monument's Computer Station CCTV camera codes.</summary>
/// <param name="database">The item database.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class CctvCommandHandler(IItemDatabase database, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "cctv";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.ResolveCctv(query) switch
        {
            CctvMatch.Found f => Render(f.Monument, localizer, context.Culture),
            CctvMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }

    private static string Render(CctvMonument monument, ILocalizer localizer, string culture)
    {
        var body = localizer.Get("command.cctv.ok", culture, CctvLine.Format(monument));
        return monument.Dynamic
            ? string.Concat(body, "\n\n", localizer.Get("command.cctv.note", culture))
            : body;
    }
}
```

- [ ] **Step 6: Register the handler + add help-catalog row**

In `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`, add after the `SmeltCommandHandler` registration:

```csharp
        services.AddScoped<ICommandHandler, CctvCommandHandler>();
```

In `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`, add to the `InGame` list after `new("smelt", …)`:

```csharp
        new("cctv", CommandGroup.ItemDb, "help.cctv"),
```

and to the `Slash` list after `new("smelt", …)`:

```csharp
        new("cctv", CommandGroup.ItemDb, "help.slash.cctv"),
```

- [ ] **Step 7: Update the registration + help-catalog drift tests**

In `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`, bump the count and add the assertion:

```csharp
        Assert.Equal(28, handlers.Count);
```

and beside the `smelt` assertion:

```csharp
        Assert.Contains(handlers, h => h.Name == "cctv");
```

In `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs`, add `"cctv"` to the `HandlerNames` array (append to the item-db row):

```csharp
        "item", "recycle", "craft", "research", "decay", "upkeep", "durability", "smelt", "cctv",
```

- [ ] **Step 8: Bump the localization parity key-count tripwire**

The four new ResX keys (`command.cctv.ok`, `command.cctv.note`, `help.cctv`, `help.slash.cctv`) raise the catalog count by 4. In `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs`, the `Catalog_has_expected_key_count` test asserts the total; bump it from 244 to 248:

```csharp
        Assert.Equal(248, EnglishKeys().Count);
```

(The other parity test in this file already asserts EN and FR key sets are identical — adding all four keys to **both** `.resx` files in Step 3 keeps it green. If the count is not exactly 248 when you run it, you added the wrong number of keys — reconcile before moving on; do not "adjust to match".)

- [ ] **Step 9: Run all affected tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests tests/RustPlusBot.Localization.Tests`
Expected: PASS — formatter, handler, registration (28 handlers), help-catalog (incl. `help.cctv`/`help.slash.cctv` resolving in EN+FR), and parity (248 keys, EN/FR identical) all green.

- [ ] **Step 10: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Formatting/CctvLine.cs \
        src/RustPlusBot.Features.Commands/Handlers/CctvCommandHandler.cs \
        src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs \
        src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs \
        src/RustPlusBot.Localization/Strings.resx \
        src/RustPlusBot.Localization/Strings.fr.resx \
        tests/RustPlusBot.Features.Commands.Tests/Formatting/CctvFormatterTests.cs \
        tests/RustPlusBot.Features.Commands.Tests/Handlers/CctvHandlerTests.cs \
        tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs \
        tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs \
        tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs
git commit -m "feat(6e): add !cctv in-game handler, formatter, EN/FR strings"
```

---

### Task 7: Slash `/cctv` dropdown + drift-guard test

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Modules/CctvChoiceDriftTests.cs` (create)

**Interfaces:**

- Consumes: `IItemDatabase.ResolveCctv` + `CctvMonuments`, `CctvMatch`, `CctvLine`, `Discord.Interactions.ChoiceAttribute`.
- Produces: `/cctv` slash command with 11 `[Choice]`s; `RespondForCctvAsync(string)`.

- [ ] **Step 1: Write the failing drift-guard test**

Create `tests/RustPlusBot.Features.Commands.Tests/Modules/CctvChoiceDriftTests.cs`:

```csharp
using System.Reflection;
using Discord.Interactions;
using RustPlusBot.Features.Commands.Modules;
using RustPlusBot.Features.ItemData;

namespace RustPlusBot.Features.Commands.Tests.Modules;

public sealed class CctvChoiceDriftTests
{
    private static IReadOnlyList<string> ChoiceValues()
    {
        var parameter = typeof(ItemCommandModule)
            .GetMethod(nameof(ItemCommandModule.CctvAsync))!
            .GetParameters()[0];
        return parameter.GetCustomAttributes<ChoiceAttribute>()
            .Select(c => (string)c.Value!)
            .ToList();
    }

    [Fact]
    public void EveryChoiceResolvesToAMonument()
    {
        var db = new EmbeddedItemDatabase();
        foreach (var value in ChoiceValues())
        {
            Assert.IsType<RustPlusBot.Features.ItemData.Lookup.CctvMatch.Found>(db.ResolveCctv(value));
        }
    }

    [Fact]
    public void ChoiceSetEqualsMonumentSet()
    {
        var db = new EmbeddedItemDatabase();
        var monuments = db.CctvMonuments.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        var choices = ChoiceValues().ToHashSet(StringComparer.Ordinal);
        Assert.Equal(monuments, choices);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter CctvChoiceDriftTests`
Expected: FAIL — `ItemCommandModule.CctvAsync` does not exist.

- [ ] **Step 3: Add the slash command + responder to the module**

In `src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`, add the imports if missing (top of file already has `using Discord.Interactions;`, `RustPlusBot.Features.ItemData.Lookup`, `RustPlusBot.Features.ItemData.Data`):

Add the command after `SmeltAsync`:

```csharp
    /// <summary>Shows a monument's Computer Station CCTV camera codes.</summary>
    /// <param name="monument">The monument to look up.</param>
    [SlashCommand("cctv", "Show the CCTV camera codes for a monument")]
    public Task CctvAsync(
        [Summary("monument", "The monument to look up")]
        [Choice("Abandoned Military Base", "Abandoned Military Base")]
        [Choice("Airfield", "Airfield")]
        [Choice("Bandit Camp", "Bandit Camp")]
        [Choice("Dome", "Dome")]
        [Choice("Large Oil Rig", "Large Oil Rig")]
        [Choice("Missile Silo", "Missile Silo")]
        [Choice("Outpost", "Outpost")]
        [Choice("Small Oil Rig", "Small Oil Rig")]
        [Choice("Underwater Labs", "Underwater Labs")]
        [Choice("Cargo Ship", "Cargo Ship")]
        [Choice("Ferry Terminal", "Ferry Terminal")]
        string monument) => RespondForCctvAsync(monument);
```

Add the responder beside `RespondForSmeltAsync`:

```csharp
    private async Task RespondForCctvAsync(string query)
    {
        if (Context.Guild is null)
        {
            await RespondAsync("This command must be used in a server.", ephemeral: true).ConfigureAwait(false);
            return;
        }

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<IItemDatabase>();
            var loc = scope.ServiceProvider.GetRequiredService<ILocalizer>();
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);

            var text = db.ResolveCctv(query) switch
            {
                CctvMatch.Found f => RenderEmbed(f.Monument, loc, culture),
                CctvMatch.Ambiguous a => loc.Get("command.item.ambiguous", culture,
                    string.Join(", ", a.Candidates.Select(c => c.Name))),
                _ => loc.Get("command.item.notfound", culture, query),
            };

            var embed = new EmbedBuilder()
                .WithDescription(text)
                .WithFooter($"data as of {db.Sources.CctvAsOf:yyyy-MM-dd}")
                .Build();
            await RespondAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
        }
    }

    // Discord-only presentation: fence the codes so wildcard asterisks render literally and the
    // codes are copy-clean; the localized wildcard note rides outside the fence as prose.
    private static string RenderEmbed(CctvMonument monument, ILocalizer loc, string culture)
    {
        var fenced = loc.Get("command.cctv.ok", culture,
            string.Concat("```\n", CctvLine.Format(monument), "\n```"));
        return monument.Dynamic
            ? string.Concat(fenced, "\n\n", loc.Get("command.cctv.note", culture))
            : fenced;
    }
```

- [ ] **Step 4: Run the drift-guard test + full Commands suite**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS — both choices resolve and the choice set equals the dataset monument set (11 each).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs \
        tests/RustPlusBot.Features.Commands.Tests/Modules/CctvChoiceDriftTests.cs
git commit -m "feat(6e): add /cctv slash dropdown + choice drift guard"
```

---

### Task 8: Docs + full-suite + format gate

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/README.md`
- Modify: `docs/product/feature-catalog.md` (gitignored — edit, do **not** `git add`)

- [ ] **Step 1: Update the generator README**

In `tools/RustPlusBot.ItemData.Generator/README.md`:

Add a row to the source-file table (after the `rustlabsSmeltingData.json` row):

```markdown
   | `cctv.json` | monument CCTV camera codes |
```

Update the calculator list in the opening paragraph to include `/cctv`. In the **Provenance dates** section, add `CctvAsOf` to the list of constants. Add a note bullet under **Notes**:

```markdown
- CCTV data (`cctv.json`, ~2 KB) is keyed by **monument name** and projected into a dedicated
  `Cctv` table (`OfflineCctvSource`), resolved by name. Codes are un-escaped (`\*` → `*`) during the
  transform. Its provenance (`CctvAsOf`, 2025-11-12) is the upstream last-change date for the file.
```

- [ ] **Step 2: Update the feature catalog (local-only)**

In `docs/product/feature-catalog.md`, mark `/cctv` adopted and subsystem 6 complete: change the `|`/cctv`| CCTV codes for a monument | ⏸ Defer | **6** | Static monument data |` row's status from `⏸ Defer` to `✅ Adopt`, and note subsystem 6 is fully shipped. Do **not** `git add` this file.

- [ ] **Step 3: Run the format gate**

Run: `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx`
Then: `git status --porcelain`
Expected: **no diff** from the cleanup (clean working tree apart from the README). If cleanup changed files, review, re-run tests, and amend.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test RustPlusBot.slnx`
Expected: PASS — entire suite green.

- [ ] **Step 5: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/README.md
git commit -m "docs(6e): document CCTV source in the generator README"
```

- [ ] **Step 6: Open the PR**

```bash
git push -u origin feat/item-database-5
gh pr create --base develop --title "Subsystem 6e: CCTV codes (/cctv + in-game, schema v5)" \
  --body "Closes subsystem 6. Adds monument→CCTV-codes lookup: \`/cctv\` (fixed dropdown) + \`!cctv\` (fuzzy), a dedicated \`Cctv\` table (schema v4→v5) sourced from rustplusplus \`cctv.json\`, with a slash-choice⇆dataset drift guard. See docs/superpowers/specs/2026-06-29-rustplusbot-6e-cctv-design.md."
```

---

## Self-Review

**Spec coverage:**

- §2 schema (CctvMonument, Cctv member, CctvAsOf) → Task 1 ✓
- §3.2 source un-escape + dynamic flag → Task 2 ✓
- §6 generator wiring + validator + regen + provenance 2025-11-12 → Tasks 2/3/4 ✓
- §5 ResolveCctv + CctvMatch + CctvLookup → Task 5 ✓ (plus `CctvMonuments` accessor for the drift guard — a deliberate addition beyond the spec's resolve-only surface)
- §4.1 in-game `!cctv` (2-arg handler, no name resolver) → Task 6 ✓
- §4.2 slash `/cctv` fixed dropdown + fenced codes + footer → Task 7 ✓
- §4.3 formatter header + codes, note via template/`command.cctv.note`, fence wraps `{0}` → Tasks 6/7 ✓
- §4.4 /help rows → Task 6 ✓
- §6 drift guard both directions → Task 7 ✓
- §8 tests (generator, lookup, formatter, handler, drift, bundle smoke) → Tasks 2–7 ✓
- §10 gates → Task 8 ✓

**Placeholder scan:** No TBD/TODO. `CctvAsOf = 2025-11-12` (the real upstream date) is set once in Task 1 and reused unchanged in Task 4.

**Tripwire scan (added during pre-flight against the live codebase):** three count assertions guard registration/help/localization. `CommandRegistrationTests` 27→28 (Task 6 Step 7); `CommandHelpCatalogTests.HandlerNames` += `cctv` (Task 6 Step 7); `StringsResourceParityTests.Catalog_has_expected_key_count` 244→248 for the four new keys (Task 6 Step 8). The 6d ledger flagged the parity tripwire as the recurring miss — it is now an explicit step.

**Type consistency:** `CctvMonument(Name, Codes, Dynamic)`, `CctvMatch.Found(Monument)`, `CctvLookup.Resolve(query, all, cap)` (no `byId` param — CCTV has no ids), `IItemDatabase.{CctvMonuments, ResolveCctv}`, `CctvLine.Format(monument)`, `command.cctv.ok` / `command.cctv.note`, `help.cctv` / `help.slash.cctv` — names match across Tasks 1–7. `DatasetSources.CctvAsOf` consumed by the module footer and the bundle smoke test consistently.

**Note on intentional minor duplication:** the dynamic-note append (`+ "\n\n" + command.cctv.note`) appears in both `CctvCommandHandler.Render` and `ItemCommandModule.RenderEmbed`. This is deliberate: the fence wraps the codes on the Discord path but not in-game, so the bodies differ; extracting a shared helper would require passing `ILocalizer` into the (pure) formatter. The duplication mirrors the existing per-command resolve-switch duplication between handlers and the module.
