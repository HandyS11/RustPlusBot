# Subsystem 6d — Smelting Calculator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a smelter-keyed smelting calculator — in-game `!smelt <smelter>` and Discord `/smelt <smelter>` — that lists every conversion a smelter performs (input → output, quantity/probability, wood/fuel, time), sourced offline from `rustlabsSmeltingData.json`.

**Architecture:** A fourth mirror of the 6a/6b/6c Item-Database pattern. Smelting data is natively keyed by smelter, and the same input name appears under different item ids across smelters, so it does **not** hang off `ItemRecord`; instead it gets its own top-level `Smelters` table resolved by name — exactly like 6c's `RaidTargets`. New generator source + validator rules regenerate the embedded `item-data.json` (schema **v3 → v4**); a pure formatter + in-game handler + slash command + EN/FR strings surface it. No new entity, channel, migration, option, or hosted service.

**Tech Stack:** C# / .NET, `System.Text.Json` (generator + loader), xUnit, Discord.Net interactions, `.resx` localization, the maintainer CLI `tools/RustPlusBot.ItemData.Generator`.

## Global Constraints

- **Branch:** `feat/item-database-4` off `develop` (no worktrees — cut a plain `feat/*` branch in the main checkout).
- **Schema discipline:** the emitted dataset `SchemaVersion` and `EmbeddedItemDatabase.ExpectedSchemaVersion` must match; a mismatch hard-fails the loader at startup. The bump to **4** and the bundle regeneration land together (Task 4).
- **Provenance:** `SmeltingAsOf = 2023-11-05` (the rustplusplus snapshot date for `rustlabsSmeltingData.json`; it is older than the `2024-09-07` of the other rustlabs sources because that file has not changed upstream since then).
- **Formatting:** all string building uses `CultureInfo.InvariantCulture` via `string.Create` (the existing `DurabilityLine` discipline).
- **Localization parity:** every new key exists in BOTH `Strings.resx` (EN) and `Strings.fr.resx` (FR), with **identical key sets**. `StringsResourceParityTests.Catalog_has_expected_key_count` is a COUNT TRIPWIRE — currently **241**. 6d adds 3 keys total: `command.smelt.ok` (Task 7 → 242) and `help.smelt` + `help.slash.smelt` (Task 8 → 244). Bump the assertion in the same task that adds the keys, or the test goes red.
- **Naming:** internal types are `Smelt*` / `Smelter`; the command is `/smelt` and `!smelt`. New PUBLIC types (`Smelter`, `SmeltConversion`, `SmeltMatch`, `SmeltLookup`) need XML docs (`-warnaserror` + analyzers reject undocumented public members and unused fields).
- **Source data path:** `~/Dev/rustplusplus/src/staticFiles` (the generator default).
- **Build/test races:** parallel builds race on `.git/config` hooksPath — ALWAYS pass `-maxcpucount:1` to `dotnet build` and `dotnet test`. Read per-assembly test counts (a broken build silently DROPS an assembly's tests). Tests are plain xUnit `Assert.*` (+ NSubstitute) — NO FluentAssertions.
- **Gates (must pass before "done"):** `dotnet build RustPlusBot.slnx -maxcpucount:1` clean (0 warnings, `-warnaserror`); `dotnet tool restore` then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` produces **no diff** (hard CI gate); full `dotnet test RustPlusBot.slnx -maxcpucount:1` suite green.
- **Docs are gitignored:** never `git add` anything under `docs/` — this plan and the spec are local-only.
- **TDD, DRY, YAGNI, frequent commits.** Co-author trailer on commits: `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## File Structure

**Schema (leaf project `src/RustPlusBot.Features.ItemData`)**

- Modify `Data/ItemDataset.cs` — add `Smelter` + `SmeltConversion` records; add `Smelters` to `ItemDataset`; add `SmeltingAsOf` to `DatasetSources`.
- Modify `EmbeddedItemDatabase.cs` — version bump, `Smelters` field + id index, `ResolveSmelter`.
- Modify `IItemDatabase.cs` — add `ResolveSmelter`.
- Create `Lookup/SmeltMatch.cs` — Found/Ambiguous/NotFound discriminated union.
- Create `Lookup/SmeltLookup.cs` — name/id resolution over smelters (wraps the shared `NameMatcher`).

**Generator (`tools/RustPlusBot.ItemData.Generator`)**

- Create `Sources/ISmeltingSource.cs` + `Sources/OfflineSmeltingSource.cs` — read `rustlabsSmeltingData.json` → `IReadOnlyList<Smelter>`.
- Modify `Validation/DatasetValidator.cs` — `MinSmelterCount` + per-conversion checks.
- Modify `Program.cs` — wire the source, stamp `SmeltingAsOf`, bump emitted version to 4, add the floor.
- Modify `README.md` — source-file table + provenance + notes.
- Regenerate `src/RustPlusBot.Features.ItemData/Data/item-data.json` (embedded bundle).

**Commands (`src/RustPlusBot.Features.Commands`)**

- Modify `Formatting/DurationFormat.cs` — **already exists** (`Compact(TimeSpan)`, minute-flooring, ~10 callers — do NOT reuse it for smelt: it renders `3.33s` as `0m`). Add a new sub-minute `Seconds(double)` method that `DurabilityLine` + `SmeltLine` share (DRY).
- Modify `Formatting/DurabilityLine.cs` — replace its private `FormatTime` with `DurationFormat.Seconds`.
- Create `Formatting/SmeltLine.cs` — pure formatter for the smelter card.
- Create `Handlers/SmeltCommandHandler.cs` — in-game `!smelt`.
- Modify `Modules/ItemCommandModule.cs` — `/smelt` + `RespondForSmeltAsync`.
- Modify `CommandServiceCollectionExtensions.cs` — register the handler.
- Modify `Help/CommandHelpCatalog.cs` — in-game + slash rows.

**Localization (`src/RustPlusBot.Localization`)**

- Modify `Strings.resx` + `Strings.fr.resx` — `command.smelt.ok`, `help.smelt`, `help.slash.smelt`.

**Tests (mirror existing siblings)**

- Modify `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs` — constructor fan-out + smelter checks.
- Create `tests/RustPlusBot.ItemData.Generator.Tests/OfflineSmeltingSourceTests.cs`.
- Create `tests/RustPlusBot.Features.ItemData.Tests/SmeltLookupTests.cs`.
- Modify `tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs` — bundle smoke + `SmeltingAsOf`.
- Create `tests/RustPlusBot.Features.Commands.Tests/Formatting/SmeltFormatterTests.cs`.
- Create `tests/RustPlusBot.Features.Commands.Tests/Handlers/SmeltHandlerTests.cs`.
- Modify `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs` — count 26→27 + `smelt`.
- Modify `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs` — `HandlerNames` += `smelt`.

---

## Task 1: Schema v4 records + members (compile-green, bundle unchanged)

Add the schema shapes and fix every constructor call site so the solution compiles and the suite stays green. The emitted/expected schema version stays **3** in this task (the bundle is regenerated in Task 4); the new members are additive and the existing schema-3 bundle deserializes with `Smelters` = null (handled later) and `SmeltingAsOf` = default.

**Files:**

- Modify: `src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`
- Modify: `tools/RustPlusBot.ItemData.Generator/Program.cs`
- Modify (test): `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`

**Interfaces:**

- Produces: `Smelter(string Key, string Name, IReadOnlyList<SmeltConversion> Conversions)`; `SmeltConversion(int InputId, int OutputId, int OutputQuantity, double OutputProbability, double WoodQuantity, double TimeSeconds)`; `ItemDataset` gains 5th positional `IReadOnlyList<Smelter> Smelters`; `DatasetSources` gains 8th positional `DateOnly SmeltingAsOf`.

- [ ] **Step 1: Add the records and members to `ItemDataset.cs`**

Append these two records at the end of `src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`:

```csharp
/// <summary>One smelter (furnace, oven, refinery, cooker) and the conversions it performs.</summary>
/// <param name="Key">The smelter's Rust item id, as a string.</param>
/// <param name="Name">The display name (e.g. "Furnace", "Camp Fire").</param>
/// <param name="Conversions">The input → output conversions, in source order (always non-empty).</param>
public sealed record Smelter(string Key, string Name, IReadOnlyList<SmeltConversion> Conversions);

/// <summary>One input → output conversion within a smelter.</summary>
/// <param name="InputId">The smeltable input item id (resolves via the item spine).</param>
/// <param name="OutputId">The produced item id (resolves via the item spine).</param>
/// <param name="OutputQuantity">Units produced per smelt.</param>
/// <param name="OutputProbability">The probability (0..1] of producing the output.</param>
/// <param name="WoodQuantity">Wood (fuel) consumed per smelt; 0 means electric / no fuel.</param>
/// <param name="TimeSeconds">Seconds per smelt.</param>
public sealed record SmeltConversion(
    int InputId,
    int OutputId,
    int OutputQuantity,
    double OutputProbability,
    double WoodQuantity,
    double TimeSeconds);
```

Add the 5th member to `ItemDataset` (update its XML doc too):

```csharp
/// <param name="Smelters">Every known smelter and the conversions it performs.</param>
public sealed record ItemDataset(
    int SchemaVersion,
    DatasetSources Sources,
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<RaidTarget> RaidTargets,
    IReadOnlyList<Smelter> Smelters);
```

Add the 8th member to `DatasetSources` (update its XML doc too):

```csharp
/// <param name="SmeltingAsOf">Smelting data source date.</param>
public sealed record DatasetSources(
    DateOnly NamesAsOf,
    DateOnly RecycleAsOf,
    DateOnly CraftAsOf,
    DateOnly ResearchAsOf,
    DateOnly DecayAsOf,
    DateOnly UpkeepAsOf,
    DateOnly DurabilityAsOf,
    DateOnly SmeltingAsOf);
```

- [ ] **Step 2: Update the generator `Program.cs` to compile (keep schema 3, empty smelters)**

In `tools/RustPlusBot.ItemData.Generator/Program.cs`, add the provenance constant beside the others (after `DurabilityAsOf`):

```csharp
    private static readonly DateOnly SmeltingAsOf = new(2023, 11, 5);
```

Update the `DatasetSources` construction (currently 7 dates) and the `ItemDataset` construction (currently 4 args) — pass `[]` for smelters for now:

```csharp
        var dataset = new ItemDataset(
            3,
            new DatasetSources(NamesAsOf, RecycleAsOf, CraftAsOf, ResearchAsOf, DecayAsOf, UpkeepAsOf, DurabilityAsOf,
                SmeltingAsOf),
            items,
            raidTargets,
            []);
```

- [ ] **Step 3: Fix the broken constructor call sites in `DatasetValidatorTests.cs`**

Adding the positional members breaks every `new DatasetSources(...)` and `new ItemDataset(...)` in `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`. Update `Good()` and `WithRaid` (add the 8th date and the 5th `[]` smelters arg):

```csharp
    private static ItemDataset Good() => new(1,
        new DatasetSources(new(2026, 4, 8), new(2024, 9, 7), new(2024, 9, 7), new(2024, 9, 7), new(2024, 9, 7),
            new(2024, 9, 7), new(2024, 9, 7), new(2023, 11, 5)),
        [
            new ItemRecord(1, "AK-47", 1, 3600,
                new RecycleYield([new YieldEntry(2, 4, 1.0)]), null, null, null, null),
            new ItemRecord(2, "Metal Fragments", 1000, null, null, null, null, null, null),
        ],
        [],
        []);

    private static ItemDataset WithRaid(params RaidTarget[] raid) =>
        new(3, Good().Sources, Good().Items, raid, []);
```

Then add `, []` as the final argument to EVERY remaining `new ItemDataset(...)` in that file (the `UnresolvableYieldId_isError`, `UnresolvableCraftIngredientId_isError`, `UnresolvableUpkeepId_isError`, `UpkeepMinGreaterThanMax_isError`, and `NegativeDecay_isError` tests each construct a bad `ItemDataset(1, …)` / `ItemDataset(2, …)` with 4 args — they now need the 5th `[]`). For example:

```csharp
        var bad = new ItemDataset(1, Good().Sources,
            [
                new ItemRecord(1, "AK-47", 1, null,
                    new RecycleYield([new YieldEntry(99999, 4, 1.0)]), null, null, null, null),
            ],
            [],
            []);
```

- [ ] **Step 4: Build and run the full suite to verify green**

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: build succeeds (0 errors).

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: PASS (all existing validator/source tests green; no behavior changed).

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs tools/RustPlusBot.ItemData.Generator/Program.cs tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs
git commit -m "feat(itemdata): add Smelter/SmeltConversion schema (v4 members, bundle unchanged)"
```

---

## Task 2: Offline smelting source (generator transform)

TDD the offline transform that reads `rustlabsSmeltingData.json` and projects each smelter's conversion list. Synthetic JSON only — no dependency on the real file.

**Files:**

- Create: `tools/RustPlusBot.ItemData.Generator/Sources/ISmeltingSource.cs`
- Create: `tools/RustPlusBot.ItemData.Generator/Sources/OfflineSmeltingSource.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/OfflineSmeltingSourceTests.cs`

**Interfaces:**

- Consumes: `Smelter`, `SmeltConversion` (Task 1).
- Produces: `ISmeltingSource.LoadSmelters(IReadOnlyDictionary<int, string> names) → IReadOnlyList<Smelter>`; `OfflineSmeltingSource(string SmeltingFilePath)`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.ItemData.Generator.Tests/OfflineSmeltingSourceTests.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineSmeltingSourceTests
{
    private static OfflineSmeltingSource SourceWith(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return new OfflineSmeltingSource(path);
    }

    [Fact]
    public void LoadSmelters_ProjectsConversions_ResolvingSmelterNameAndIds()
    {
        const string json = """
                            {
                              "100": [
                                {"fromId":"10","woodQuantity":1.67,"toId":"20","toQuantity":1,"toProbability":1,"time":3.33,"timeString":"3.33 sec"},
                                {"fromId":"11","woodQuantity":1,"toId":"21","toQuantity":1,"toProbability":0.75,"time":2,"timeString":"2 sec"}
                              ]
                            }
                            """;
        var names = new Dictionary<int, string>
        {
            [100] = "Furnace", [10] = "Metal Ore", [20] = "Metal Fragments", [11] = "Wood", [21] = "Charcoal",
        };

        var smelters = SourceWith(json).LoadSmelters(names);

        var furnace = Assert.Single(smelters);
        Assert.Equal("Furnace", furnace.Name);
        Assert.Equal("100", furnace.Key);
        Assert.Equal(2, furnace.Conversions.Count);
        var first = furnace.Conversions[0];
        Assert.Equal(10, first.InputId);
        Assert.Equal(20, first.OutputId);
        Assert.Equal(1, first.OutputQuantity);
        Assert.Equal(1.67, first.WoodQuantity);
        Assert.Equal(3.33, first.TimeSeconds);
        Assert.Equal(0.75, furnace.Conversions[1].OutputProbability);
    }

    [Fact]
    public void LoadSmelters_DropsSmelterWithUnknownId()
    {
        const string json =
            """{"999":[{"fromId":"10","woodQuantity":1,"toId":"20","toQuantity":1,"toProbability":1,"time":2}]}""";
        var names = new Dictionary<int, string> { [10] = "Metal Ore", [20] = "Metal Fragments" };
        Assert.Empty(SourceWith(json).LoadSmelters(names));
    }

    [Fact]
    public void LoadSmelters_DropsConversionWithUnknownInputOrOutput()
    {
        const string json =
            """{"100":[{"fromId":"10","woodQuantity":1,"toId":"999","toQuantity":1,"toProbability":1,"time":2}]}""";
        var names = new Dictionary<int, string> { [100] = "Furnace", [10] = "Metal Ore" };
        Assert.Empty(SourceWith(json).LoadSmelters(names)); // smelter dropped: no valid conversions remain
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj --filter "FullyQualifiedName~OfflineSmeltingSourceTests"`
Expected: FAIL to compile — `OfflineSmeltingSource` / `ISmeltingSource` do not exist.

- [ ] **Step 3: Create the interface**

Create `tools/RustPlusBot.ItemData.Generator/Sources/ISmeltingSource.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides per-smelter smelting/cooking conversion data from RustLabs.</summary>
internal interface ISmeltingSource
{
    /// <summary>Loads smelters, resolving smelter and input/output names via <paramref name="names"/>.</summary>
    /// <param name="names">Item id → display name, for the smelter id and each conversion's ids.</param>
    /// <returns>Every smelter with at least one resolvable conversion.</returns>
    IReadOnlyList<Smelter> LoadSmelters(IReadOnlyDictionary<int, string> names);
}
```

- [ ] **Step 4: Create the offline implementation**

Create `tools/RustPlusBot.ItemData.Generator/Sources/OfflineSmeltingSource.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads smelting data from the offline RustLabs JSON file, keyed by smelter id.</summary>
/// <param name="SmeltingFilePath">Path to the <c>rustlabsSmeltingData.json</c> file.</param>
internal sealed class OfflineSmeltingSource(string SmeltingFilePath) : ISmeltingSource
{
    /// <inheritdoc/>
    public IReadOnlyList<Smelter> LoadSmelters(IReadOnlyDictionary<int, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        using var stream = File.OpenRead(SmeltingFilePath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var smelters = new List<Smelter>();
        foreach (var prop in root.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var smelterId) ||
                !names.TryGetValue(smelterId, out var smelterName))
            {
                continue; // unknown smelter id — drop
            }

            var conversions = ReadConversions(prop.Value, names);
            if (conversions.Count > 0)
            {
                smelters.Add(new Smelter(prop.Name, smelterName, conversions));
            }
        }

        return smelters;
    }

    private static List<SmeltConversion> ReadConversions(JsonElement rows, IReadOnlyDictionary<int, string> names)
    {
        var conversions = new List<SmeltConversion>();
        foreach (var row in rows.EnumerateArray())
        {
            if (ReadId(row, "fromId") is not { } inputId || !names.ContainsKey(inputId) ||
                ReadId(row, "toId") is not { } outputId || !names.ContainsKey(outputId))
            {
                continue; // orphan id with no item name — drop
            }

            conversions.Add(new SmeltConversion(
                inputId,
                outputId,
                ReadInt(row, "toQuantity") ?? 1,
                ReadDouble(row, "toProbability") ?? 1,
                ReadDouble(row, "woodQuantity") ?? 0,
                ReadDouble(row, "time") ?? 0));
        }

        return conversions;
    }

    private static int? ReadId(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.String when int.TryParse(p.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var v) => v,
            JsonValueKind.Number => p.GetInt32(),
            _ => null,
        };
    }

    private static int? ReadInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static double? ReadDouble(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj --filter "FullyQualifiedName~OfflineSmeltingSourceTests"`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Sources/ISmeltingSource.cs tools/RustPlusBot.ItemData.Generator/Sources/OfflineSmeltingSource.cs tests/RustPlusBot.ItemData.Generator.Tests/OfflineSmeltingSourceTests.cs
git commit -m "feat(itemdata): offline smelting source (per-smelter conversion transform)"
```

---

## Task 3: Validator smelter checks

TDD the dataset validation rules that guard against upstream smelting drift.

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`

**Interfaces:**

- Consumes: `Smelter`, `SmeltConversion` (Task 1).
- Produces: `ValidationOptions` gains `int MinSmelterCount = 0`; `DatasetValidator.Validate` emits smelter errors.

- [ ] **Step 1: Write the failing tests**

Add a helper and tests to `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs` (place the helper next to `WithRaid`):

```csharp
    private static ItemDataset WithSmelters(params Smelter[] smelters) =>
        new(4, Good().Sources, Good().Items, [], smelters);

    [Fact]
    public void SmelterConversion_UnknownInputId_isError()
    {
        var bad = WithSmelters(new Smelter("100", "Furnace",
            [new SmeltConversion(424242, 2, 1, 1, 1, 3)]));
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("424242", StringComparison.Ordinal));
    }

    [Fact]
    public void SmelterConversion_NonPositiveTime_isError()
    {
        var bad = WithSmelters(new Smelter("100", "Furnace",
            [new SmeltConversion(1, 2, 1, 1, 1, 0)]));
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("time", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SmelterConversion_ProbabilityOutOfRange_isError()
    {
        var bad = WithSmelters(new Smelter("100", "Furnace",
            [new SmeltConversion(1, 2, 1, 1.5, 1, 3)]));
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("probability", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TooFewSmelters_isError()
    {
        var errors = DatasetValidator.Validate(Good(), new ValidationOptions(MinItemCount: 1, MinSmelterCount: 8));
        Assert.Contains(errors, e => e.Contains("smelter count", StringComparison.OrdinalIgnoreCase));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj --filter "FullyQualifiedName~DatasetValidatorTests"`
Expected: FAIL — `MinSmelterCount` does not exist (compile error), and no smelter errors are emitted.

- [ ] **Step 3: Add `MinSmelterCount` to `ValidationOptions`**

In `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`, extend the options record:

```csharp
/// <summary>Options that control dataset validation thresholds.</summary>
/// <param name="MinItemCount">The minimum number of items the dataset must contain.</param>
/// <param name="MinRaidTargetCount">The minimum number of raid targets the dataset must contain.</param>
/// <param name="MinSmelterCount">The minimum number of smelters the dataset must contain.</param>
internal sealed record ValidationOptions(int MinItemCount, int MinRaidTargetCount = 0, int MinSmelterCount = 0);
```

- [ ] **Step 4: Add the smelter checks**

In the same file, insert this block immediately before `return errors;` at the end of `Validate`:

```csharp
        var smelters = dataset.Smelters ?? [];
        if (smelters.Count < options.MinSmelterCount)
        {
            errors.Add($"smelter count {smelters.Count} below minimum {options.MinSmelterCount}");
        }

        foreach (var smelter in smelters)
        {
            if (smelter.Conversions.Count == 0)
            {
                errors.Add($"smelter {smelter.Name}: has no conversions");
            }

            foreach (var c in smelter.Conversions)
            {
                if (!ids.Contains(c.InputId))
                {
                    errors.Add($"smelter {smelter.Name}: conversion references unknown input id {c.InputId}");
                }

                if (!ids.Contains(c.OutputId))
                {
                    errors.Add($"smelter {smelter.Name}: conversion references unknown output id {c.OutputId}");
                }

                if (c.OutputQuantity <= 0)
                {
                    errors.Add($"smelter {smelter.Name}: non-positive output quantity {c.OutputQuantity}");
                }

                if (c.TimeSeconds <= 0)
                {
                    errors.Add($"smelter {smelter.Name}: non-positive time {c.TimeSeconds}");
                }

                if (c.WoodQuantity < 0)
                {
                    errors.Add($"smelter {smelter.Name}: negative wood quantity {c.WoodQuantity}");
                }

                if (c.OutputProbability is <= 0 or > 1)
                {
                    errors.Add(
                        $"smelter {smelter.Name}: output probability {c.OutputProbability} out of range (0,1]");
                }
            }
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj --filter "FullyQualifiedName~DatasetValidatorTests"`
Expected: PASS (existing + 4 new smelter tests).

- [ ] **Step 6: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs
git commit -m "feat(itemdata): validate smelter conversions (ids, time, probability, floor)"
```

---

## Task 4: Wire generator, bump to v4, regenerate the embedded bundle

Wire the source into `Program.cs`, bump the emitted schema to 4 with the smelter floor, regenerate `item-data.json`, and bump the loader's expected version to match. This task is the atomic version cutover — it ends green because the new bundle (v4, with smelters) and the loader (expects 4) move together.

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Program.cs`
- Modify: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`
- Regenerate: `src/RustPlusBot.Features.ItemData/Data/item-data.json`

**Interfaces:**

- Consumes: `OfflineSmeltingSource.LoadSmelters` (Task 2), `ValidationOptions.MinSmelterCount` (Task 3).
- Produces: a schema-4 bundle whose `Smelters` is populated; `EmbeddedItemDatabase.ExpectedSchemaVersion == 4`.

- [ ] **Step 1: Wire the source and bump the emitted version in `Program.cs`**

In `tools/RustPlusBot.ItemData.Generator/Program.cs`, instantiate the source next to `durabilitySource`:

```csharp
        var smeltingSource = new OfflineSmeltingSource(
            Path.Combine(rustplusDir, "rustlabsSmeltingData.json"));
```

Load the smelters after `raidTargets` is loaded:

```csharp
        var smelters = smeltingSource.LoadSmelters(names);
        Console.WriteLine($"Loaded {smelters.Count} smelters");
```

Replace the temporary `dataset` construction from Task 1 (schema 3, `[]` smelters) with the real one (schema **4**, real smelters):

```csharp
        var dataset = new ItemDataset(
            4,
            new DatasetSources(NamesAsOf, RecycleAsOf, CraftAsOf, ResearchAsOf, DecayAsOf, UpkeepAsOf, DurabilityAsOf,
                SmeltingAsOf),
            items,
            raidTargets,
            smelters);
```

Add the smelter floor to the validation options:

```csharp
        var validationOptions = new ValidationOptions(MinItemCount: minItems, MinRaidTargetCount: 300,
            MinSmelterCount: 8);
```

- [ ] **Step 2: Bump the loader's expected schema version**

In `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`, change:

```csharp
    private const int ExpectedSchemaVersion = 4;
```

- [ ] **Step 3: Regenerate the embedded bundle**

Run:

```bash
dotnet run --project tools/RustPlusBot.ItemData.Generator -- \
  --out src/RustPlusBot.Features.ItemData/Data/item-data.json \
  --rustplusplus ~/Dev/rustplusplus/src/staticFiles \
  --min-items 1000
```

Expected stdout includes `Loaded 10 smelters` and `Emitted <N> items to …`; exit code 0. (If it exits non-zero, the bundle is left untouched — read the stderr validation errors before retrying; do not hand-edit `item-data.json`.)

- [ ] **Step 4: Verify the regenerated bundle is schema 4 with smelters**

Run: `grep -m1 '"schemaVersion"' src/RustPlusBot.Features.ItemData/Data/item-data.json`
Expected: `"schemaVersion": 4,`

Run: `grep -c '"conversions"' src/RustPlusBot.Features.ItemData/Data/item-data.json`
Expected: `10` (one per smelter).

- [ ] **Step 5: Build and run the ItemData + generator suites**

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: build succeeds.

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests/RustPlusBot.Features.ItemData.Tests.csproj`
Expected: PASS — the loader now expects 4 and the embedded bundle is 4 (no schema-mismatch throw).

- [ ] **Step 6: Commit (includes the regenerated bundle)**

```bash
git add tools/RustPlusBot.ItemData.Generator/Program.cs src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs src/RustPlusBot.Features.ItemData/Data/item-data.json
git commit -m "feat(itemdata): emit Smelters table, bump schema v3->v4, regenerate bundle"
```

---

## Task 5: Smelter lookup (`ResolveSmelter`)

TDD name/id resolution over smelters, mirroring `RaidLookup`/`RaidMatch`, and expose it on `IItemDatabase`.

**Files:**

- Create: `src/RustPlusBot.Features.ItemData/Lookup/SmeltMatch.cs`
- Create: `src/RustPlusBot.Features.ItemData/Lookup/SmeltLookup.cs`
- Modify: `src/RustPlusBot.Features.ItemData/IItemDatabase.cs`
- Modify: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`
- Test: `tests/RustPlusBot.Features.ItemData.Tests/SmeltLookupTests.cs`
- Test: `tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs`

**Interfaces:**

- Consumes: `Smelter` (Task 1); `NameMatcher.Resolve` + `NameMatchKind` (existing, in `Lookup`).
- Produces: `SmeltMatch` (Found/Ambiguous/NotFound); `SmeltLookup.Resolve(string, Func<int, Smelter?>, IReadOnlyList<Smelter>, int cap = 10)`; `IItemDatabase.ResolveSmelter(string) → SmeltMatch`.

- [ ] **Step 1: Write the failing lookup tests**

Create `tests/RustPlusBot.Features.ItemData.Tests/SmeltLookupTests.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class SmeltLookupTests
{
    private static readonly Smelter Furnace = new("100", "Furnace", []);
    private static readonly Smelter Large = new("101", "Large Furnace", []);
    private static readonly Smelter Camp = new("102", "Camp Fire", []);
    private static readonly IReadOnlyList<Smelter> All = [Furnace, Large, Camp];

    private static Smelter? ById(int id) => All.FirstOrDefault(s => int.TryParse(s.Key, out var k) && k == id);

    private static SmeltMatch Resolve(string q) => SmeltLookup.Resolve(q, ById, All);

    [Fact]
    public void NumericInput_ResolvesById()
    {
        var found = Assert.IsType<SmeltMatch.Found>(Resolve("102"));
        Assert.Equal("Camp Fire", found.Smelter.Name);
    }

    [Fact]
    public void ExactName_BeatsSubstring()
    {
        var found = Assert.IsType<SmeltMatch.Found>(Resolve("furnace")); // exact "Furnace" wins over "Large Furnace"
        Assert.Equal("Furnace", found.Smelter.Name);
    }

    [Fact]
    public void UniqueSubstring_Found()
    {
        var found = Assert.IsType<SmeltMatch.Found>(Resolve("camp"));
        Assert.Equal("Camp Fire", found.Smelter.Name);
    }

    [Fact]
    public void MultipleSubstring_Ambiguous()
    {
        var amb = Assert.IsType<SmeltMatch.Ambiguous>(Resolve("fur")); // Furnace + Large Furnace both contain "fur"
        Assert.True(amb.Candidates.Count >= 2);
    }

    [Fact]
    public void NoMatch_NotFound() => Assert.IsType<SmeltMatch.NotFound>(Resolve("zzzzz"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_NotFound(string q) => Assert.IsType<SmeltMatch.NotFound>(Resolve(q));
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests/RustPlusBot.Features.ItemData.Tests.csproj --filter "FullyQualifiedName~SmeltLookupTests"`
Expected: FAIL to compile — `SmeltLookup` / `SmeltMatch` do not exist.

- [ ] **Step 3: Create `SmeltMatch`**

Create `src/RustPlusBot.Features.ItemData/Lookup/SmeltMatch.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The outcome of resolving a user query to a smelter.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "Discriminated-union pattern: nested sealed records are the intended public surface.")]
public abstract record SmeltMatch
{
    private SmeltMatch() { }

    /// <summary>Exactly one smelter resolved.</summary>
    /// <param name="Smelter">The resolved smelter.</param>
    public sealed record Found(Smelter Smelter) : SmeltMatch;

    /// <summary>Several smelters matched; present candidates for disambiguation.</summary>
    /// <param name="Candidates">The candidate smelters, capped and ranked.</param>
    public sealed record Ambiguous(IReadOnlyList<Smelter> Candidates) : SmeltMatch;

    /// <summary>No smelter matched.</summary>
    public sealed record NotFound : SmeltMatch;
}
```

- [ ] **Step 4: Create `SmeltLookup`**

Create `src/RustPlusBot.Features.ItemData/Lookup/SmeltLookup.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name-or-id resolution over smelters. Exact name beats substring.</summary>
public static class SmeltLookup
{
    private static readonly IComparer<Smelter> ByName =
        Comparer<Smelter>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name));

    /// <summary>Resolves a user query to a smelter.</summary>
    /// <param name="query">The raw user input (smelter name or item id).</param>
    /// <param name="byId">Looks up a smelter by item id.</param>
    /// <param name="all">All smelters, for name matching.</param>
    /// <param name="cap">The maximum number of ambiguous candidates to return.</param>
    /// <returns>A <see cref="SmeltMatch"/> describing the outcome.</returns>
    public static SmeltMatch Resolve(string query,
        Func<int, Smelter?> byId,
        IReadOnlyList<Smelter> all,
        int cap = 10)
    {
        var (kind, single, candidates) = NameMatcher.Resolve(query, byId, all, s => s.Name, ByName, cap);
        return kind switch
        {
            NameMatchKind.Found => new SmeltMatch.Found(single!),
            NameMatchKind.Ambiguous => new SmeltMatch.Ambiguous(candidates),
            _ => new SmeltMatch.NotFound(),
        };
    }
}
```

- [ ] **Step 5: Add `ResolveSmelter` to the interface**

In `src/RustPlusBot.Features.ItemData/IItemDatabase.cs`, add after `ResolveRaidTarget`:

```csharp
    /// <summary>Resolves a user query (smelter name or id) to a smelter.</summary>
    /// <param name="query">The raw user input.</param>
    /// <returns>A match describing the outcome.</returns>
    SmeltMatch ResolveSmelter(string query);
```

- [ ] **Step 6: Implement it in `EmbeddedItemDatabase`**

In `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`, add the fields beside the raid fields (after `RaidById`):

```csharp
    private static readonly IReadOnlyList<Smelter> Smelters = Dataset.Smelters ?? [];

    private static readonly FrozenDictionary<int, Smelter> SmelterById = IndexSmelterById(Smelters);
```

Add the method beside `ResolveRaidTarget`:

```csharp
    /// <inheritdoc />
    public SmeltMatch ResolveSmelter(string query) =>
        SmeltLookup.Resolve(query, SmelterById.GetValueOrDefault, Smelters);
```

Add the index builder beside `IndexRaidById`:

```csharp
    /// <summary>Builds a frozen id→smelter index keyed by the smelter's item id.</summary>
    /// <param name="smelters">All smelters.</param>
    /// <returns>A frozen dictionary keyed by smelter item id.</returns>
    internal static FrozenDictionary<int, Smelter> IndexSmelterById(IReadOnlyList<Smelter> smelters) =>
        smelters.GroupBy(s => int.Parse(s.Key, CultureInfo.InvariantCulture))
            .ToFrozenDictionary(g => g.Key, g => g.Last());
```

- [ ] **Step 7: Add the real-bundle smoke tests**

Append to `tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs` (the file already imports `RustPlusBot.Features.ItemData.Lookup`):

```csharp
    [Fact]
    public void ResolveSmelter_Furnace_ListsMetalFragmentsOutput()
    {
        var found = Assert.IsType<SmeltMatch.Found>(_db.ResolveSmelter("Furnace"));
        Assert.Equal("Furnace", found.Smelter.Name);
        Assert.Contains(found.Smelter.Conversions, c => c.OutputId == 69511070); // Metal Fragments
    }

    [Fact]
    public void Sources_SmeltingAsOf_IsPopulated()
    {
        Assert.Equal(new DateOnly(2023, 11, 5), _db.Sources.SmeltingAsOf);
    }
```

- [ ] **Step 8: Run the lookup + smoke tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests/RustPlusBot.Features.ItemData.Tests.csproj --filter "FullyQualifiedName~SmeltLookupTests|FullyQualifiedName~EmbeddedItemDatabaseTests"`
Expected: PASS (lookup tests + the two new smoke tests + existing ones).

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Features.ItemData/Lookup/SmeltMatch.cs src/RustPlusBot.Features.ItemData/Lookup/SmeltLookup.cs src/RustPlusBot.Features.ItemData/IItemDatabase.cs src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs tests/RustPlusBot.Features.ItemData.Tests/SmeltLookupTests.cs tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs
git commit -m "feat(itemdata): ResolveSmelter lookup + bundle smoke tests"
```

---

## Task 6: Shared duration formatter + `SmeltLine`

`DurationFormat` ALREADY EXISTS (`src/RustPlusBot.Features.Commands/Formatting/DurationFormat.cs`) with `Compact(TimeSpan span)` — a minute-flooring formatter ("Xd Yh"/"Yh Zm"/"Zm") used by ~10 callers (ItemLine, DecayLine, Afk/Alive/Uptime/Wipe handlers, …). **Do NOT reuse `Compact` for smelt** — its smallest unit is whole minutes, so `3.33s` renders as `0m` (this is the exact bug the 6c fix-wave fixed for durability). Instead add a NEW sub-minute `Seconds(double)` method to that existing class, move `DurabilityLine`'s private `FormatTime` body into it, and have both `DurabilityLine` and `SmeltLine` call it (DRY — avoids a duplicated method the Sonar duplication gate would flag). Then TDD the smelter-card formatter on top of it.

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Formatting/DurationFormat.cs` (add `Seconds`)
- Modify: `src/RustPlusBot.Features.Commands/Formatting/DurabilityLine.cs`
- Create: `src/RustPlusBot.Features.Commands/Formatting/SmeltLine.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Formatting/SmeltFormatterTests.cs`

**Interfaces:**

- Consumes: `Smelter`, `SmeltConversion` (Task 1); `IItemNameResolver.Resolve(int) → string` (existing; unknown id → `"Item {id}"`).
- Produces: `DurationFormat.Seconds(double seconds) → string` (new method on the existing class; `Compact(TimeSpan)` is left untouched); `SmeltLine.Format(Smelter, IItemNameResolver) → string`.

- [ ] **Step 1: Add the `Seconds` method to the existing `DurationFormat`**

In `src/RustPlusBot.Features.Commands/Formatting/DurationFormat.cs`, leave `Compact(TimeSpan)` exactly as-is and add this method beside it (inside the class):

```csharp
    /// <summary>Renders a sub-minute-aware duration: "&lt;n&gt;s" under a minute, else "&lt;m&gt;m &lt;s&gt;s".</summary>
    /// <param name="seconds">The duration in seconds.</param>
    /// <returns>A compact duration string.</returns>
    public static string Seconds(double seconds)
    {
        if (seconds < 60)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{seconds:0.#}s");
        }

        var span = TimeSpan.FromSeconds(seconds);
        var minutes = (int)span.TotalMinutes;
        return span.Seconds == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes}m")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes}m {span.Seconds}s");
    }
```

- [ ] **Step 2: Point `DurabilityLine` at the shared helper**

In `src/RustPlusBot.Features.Commands/Formatting/DurabilityLine.cs`, replace the call to the private method (line that builds `time`) with `DurationFormat.Seconds(t)`:

```csharp
        var time = cost.TimeSeconds is { } t and > 0
            ? string.Create(CultureInfo.InvariantCulture, $" ({DurationFormat.Seconds(t)})")
            : string.Empty;
```

Then DELETE the now-unused private `FormatTime` method from `DurabilityLine.cs`.

- [ ] **Step 3: Verify the durability tests still pass (no behavior change)**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~DurabilityFormatterTests"`
Expected: PASS (existing assertions for `"11.5s"` and `"2m 55s"` unchanged).

- [ ] **Step 4: Write the failing `SmeltLine` tests**

Create `tests/RustPlusBot.Features.Commands.Tests/Formatting/SmeltFormatterTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class SmeltFormatterTests
{
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void SmeltLine_RendersHeader_Arrow_FuelAndTime()
    {
        var smelter = new Smelter("100", "Furnace", [new SmeltConversion(1, 2, 1, 1, 1.67, 3.33)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.StartsWith("Furnace:", line, StringComparison.Ordinal);
        Assert.Contains("→", line, StringComparison.Ordinal);
        Assert.Contains("1.67 wood", line, StringComparison.Ordinal);
        Assert.Contains("3.3s", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SmeltLine_ShowsQuantityPrefix_WhenAboveOne()
    {
        var smelter = new Smelter("100", "Furnace", [new SmeltConversion(1, 2, 15, 1, 5, 10)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.Contains("15× ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SmeltLine_OmitsQuantityPrefix_WhenOne()
    {
        var smelter = new Smelter("100", "Furnace", [new SmeltConversion(1, 2, 1, 1, 5, 10)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.DoesNotContain("1× ", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SmeltLine_ShowsProbability_WhenBelowOne()
    {
        var smelter = new Smelter("100", "Furnace", [new SmeltConversion(1, 2, 1, 0.75, 1, 2)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.Contains("(75%)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void SmeltLine_ShowsNoFuel_WhenWoodZero()
    {
        var smelter = new Smelter("100", "Electric Furnace", [new SmeltConversion(1, 2, 1, 1, 0, 2)]);
        var line = SmeltLine.Format(smelter, _names);
        Assert.Contains("no fuel", line, StringComparison.Ordinal);
        Assert.DoesNotContain("wood", line, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 5: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~SmeltFormatterTests"`
Expected: FAIL to compile — `SmeltLine` does not exist.

- [ ] **Step 6: Create `SmeltLine`**

Create `src/RustPlusBot.Features.Commands/Formatting/SmeltLine.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the multi-line smelting reply for a smelter, one row per conversion (source order).</summary>
internal static class SmeltLine
{
    /// <summary>Lists each conversion a smelter performs.</summary>
    /// <param name="smelter">The smelter (with at least one conversion).</param>
    /// <param name="names">Resolves input/output item ids to names.</param>
    public static string Format(Smelter smelter, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(smelter);
        ArgumentNullException.ThrowIfNull(names);

        var lines = smelter.Conversions.Select(c => FormatConversion(c, names));
        return string.Create(CultureInfo.InvariantCulture, $"{smelter.Name}:\n{string.Join("\n", lines)}");
    }

    private static string FormatConversion(SmeltConversion conversion, IItemNameResolver names)
    {
        var input = names.Resolve(conversion.InputId);
        var output = names.Resolve(conversion.OutputId);
        var quantity = conversion.OutputQuantity > 1
            ? string.Create(CultureInfo.InvariantCulture, $"{conversion.OutputQuantity}× ")
            : string.Empty;
        var probability = conversion.OutputProbability < 1
            ? string.Create(CultureInfo.InvariantCulture, $" ({conversion.OutputProbability:0.#%})")
            : string.Empty;
        var fuel = conversion.WoodQuantity > 0
            ? string.Create(CultureInfo.InvariantCulture, $"{conversion.WoodQuantity:0.##} wood")
            : "no fuel";
        return string.Create(CultureInfo.InvariantCulture,
            $"{input} → {quantity}{output}{probability} — {DurationFormat.Seconds(conversion.TimeSeconds)} · {fuel}");
    }
}
```

- [ ] **Step 7: Run all formatting tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~SmeltFormatterTests|FullyQualifiedName~DurabilityFormatterTests"`
Expected: PASS (5 smelt + 5 durability).

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Formatting/DurationFormat.cs src/RustPlusBot.Features.Commands/Formatting/DurabilityLine.cs src/RustPlusBot.Features.Commands/Formatting/SmeltLine.cs tests/RustPlusBot.Features.Commands.Tests/Formatting/SmeltFormatterTests.cs
git commit -m "feat(commands): SmeltLine formatter + shared DurationFormat (DRY)"
```

---

## Task 7: In-game `!smelt` handler + registration

TDD the in-game command handler, register it, and add the localized OK key.

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/SmeltCommandHandler.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx`
- Modify (test): `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs` (count tripwire 241 → 242)
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/SmeltHandlerTests.cs`
- Modify (test): `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`

**Interfaces:**

- Consumes: `IItemDatabase.ResolveSmelter` (Task 5); `SmeltMatch` (Task 5); `SmeltLine.Format` (Task 6); `ICommandHandler` (existing); `ILocalizer.Get` (existing).
- Produces: `SmeltCommandHandler` with `Name => "smelt"`; localization key `command.smelt.ok`.

- [ ] **Step 1: Add the `command.smelt.ok` key to both resx files**

In `src/RustPlusBot.Localization/Strings.resx`, add beside `command.durability.ok` (value is the bare passthrough, matching its siblings):

```xml
  <data name="command.smelt.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
```

In `src/RustPlusBot.Localization/Strings.fr.resx`, add the same entry:

```xml
  <data name="command.smelt.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
```

- [ ] **Step 1b: Bump the key-count tripwire 241 → 242**

Adding one key breaks `StringsResourceParityTests.Catalog_has_expected_key_count`. In `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs`, update the assertion:

```csharp
        Assert.Equal(242, EnglishKeys().Count);
```

Run: `dotnet test tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj -maxcpucount:1`
Expected: PASS — count is 242, and the EN/FR parity tests stay green (the key was added to both files).

- [ ] **Step 2: Write the failing handler tests**

Create `tests/RustPlusBot.Features.Commands.Tests/Handlers/SmeltHandlerTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class SmeltHandlerTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly ILocalizer _loc = new ResxLocalizer();
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Name_is_smelt()
        => Assert.Equal("smelt", new SmeltCommandHandler(_db, _names, _loc).Name);

    [Fact]
    public async Task Found_returnsConversions()
    {
        var reply = await new SmeltCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("Furnace"), CancellationToken.None);
        Assert.Contains("Furnace", reply, StringComparison.Ordinal);
        Assert.Contains("Metal Fragments", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NotFound_returnsMessage()
    {
        var reply = await new SmeltCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~SmeltHandlerTests"`
Expected: FAIL to compile — `SmeltCommandHandler` does not exist.

- [ ] **Step 4: Create the handler**

Create `src/RustPlusBot.Features.Commands/Handlers/SmeltCommandHandler.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!smelt — lists what a smelter converts and its fuel/time cost.</summary>
/// <param name="database">The item database.</param>
/// <param name="names">Resolves input/output item ids to names.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class SmeltCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "smelt";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.ResolveSmelter(query) switch
        {
            SmeltMatch.Found f =>
                localizer.Get("command.smelt.ok", context.Culture, SmeltLine.Format(f.Smelter, names)),
            SmeltMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
```

- [ ] **Step 5: Register the handler**

In `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`, add after the `DurabilityCommandHandler` registration:

```csharp
        services.AddScoped<ICommandHandler, SmeltCommandHandler>();
```

- [ ] **Step 6: Update the registration count test**

In `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`, change the count assertion from 26 to 27 and add a `smelt` assertion beside the `durability` one:

```csharp
        Assert.Equal(27, handlers.Count);
```

```csharp
        Assert.Contains(handlers, h => h.Name == "smelt");
```

- [ ] **Step 7: Run the handler + registration tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~SmeltHandlerTests|FullyQualifiedName~CommandRegistrationTests"`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers/SmeltCommandHandler.cs src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs src/RustPlusBot.Localization/Strings.resx src/RustPlusBot.Localization/Strings.fr.resx tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs tests/RustPlusBot.Features.Commands.Tests/Handlers/SmeltHandlerTests.cs tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs
git commit -m "feat(commands): in-game !smelt handler + registration"
```

---

## Task 8: Slash `/smelt` + help catalog

Add the Discord slash command, its help-catalog rows, and the help text in both languages. Coverage is via the existing `CommandHelpCatalogTests` drift guards (Discord modules are not unit-tested in this repo).

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx`
- Modify (test): `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs` (count tripwire 242 → 244)
- Modify (test): `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs`

**Interfaces:**

- Consumes: `IItemDatabase.ResolveSmelter` (Task 5); `SmeltMatch` (Task 5); `SmeltLine.Format` (Task 6); `DatasetSources.SmeltingAsOf` (Task 1).
- Produces: `/smelt` slash command; in-game + slash help rows `help.smelt` / `help.slash.smelt`.

- [ ] **Step 1: Add the help text to both resx files**

In `src/RustPlusBot.Localization/Strings.resx`, add `help.smelt` beside `help.durability` and `help.slash.smelt` beside `help.slash.durability`:

```xml
  <data name="help.smelt" xml:space="preserve">
    <value>What a smelter converts, with fuel and time</value>
  </data>
```

```xml
  <data name="help.slash.smelt" xml:space="preserve">
    <value>Show what a smelter converts and its fuel/time cost.</value>
  </data>
```

In `src/RustPlusBot.Localization/Strings.fr.resx`, add the French equivalents:

```xml
  <data name="help.smelt" xml:space="preserve">
    <value>Ce qu'un fondeur transforme, avec carburant et temps</value>
  </data>
```

```xml
  <data name="help.slash.smelt" xml:space="preserve">
    <value>Affiche ce qu'un fondeur transforme et son coût en carburant/temps.</value>
  </data>
```

- [ ] **Step 1b: Bump the key-count tripwire 242 → 244**

Adding two keys breaks `StringsResourceParityTests.Catalog_has_expected_key_count` (now at 242 after Task 7). In `tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs`, update the assertion:

```csharp
        Assert.Equal(244, EnglishKeys().Count);
```

Run: `dotnet test tests/RustPlusBot.Localization.Tests/RustPlusBot.Localization.Tests.csproj -maxcpucount:1`
Expected: PASS — count is 244; EN/FR parity stays green (both keys added to both files).

- [ ] **Step 2: Add the catalog rows**

In `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`, add the in-game row after the `durability` row in the `InGame` list:

```csharp
        new("smelt", CommandGroup.ItemDb, "help.smelt"),
```

And the slash row after the `durability` row in the `Slash` list:

```csharp
        new("smelt", CommandGroup.ItemDb, "help.slash.smelt"),
```

- [ ] **Step 3: Update the help-catalog test's handler-name list**

`CommandHelpCatalogTests.InGameCatalogHasNoEntryWithoutAHandler` checks every catalog in-game entry against `HandlerNames`. In `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs`, add `"smelt"` to the `HandlerNames` array (and bump the doc comment from 20 to 21):

```csharp
    /// <summary>The 21 registered in-game handler names (see AddCommands / CommandRegistrationTests).</summary>
    private static readonly string[] HandlerNames =
    [
        "mute", "unmute", "uptime", "pop", "wipe", "time",
        "online", "offline", "team", "steamid", "alive", "afk", "prox",
        "item", "recycle", "craft", "research", "decay", "upkeep", "durability", "smelt",
    ];
```

- [ ] **Step 4: Run the help-catalog tests to verify the new rows are consistent**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter "FullyQualifiedName~CommandHelpCatalogTests"`
Expected: PASS — `EveryDescriptionKeyResolvesInEnglishAndFrench` confirms `help.smelt` + `help.slash.smelt` resolve in EN and FR; the no-orphan-entry guard passes now that `smelt` is in `HandlerNames`.

- [ ] **Step 5: Add the slash command + `RespondForSmeltAsync` to `ItemCommandModule`**

In `src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`, add the slash method after `DurabilityAsync`:

```csharp
    /// <summary>Shows what a smelter converts and its fuel/time cost.</summary>
    /// <param name="smelter">The smelter name or id.</param>
    [SlashCommand("smelt", "Show what a smelter converts and its fuel/time cost")]
    public Task SmeltAsync([Summary("smelter", "Furnace, Camp Fire, Electric Furnace, …")] string smelter) =>
        RespondForSmeltAsync(smelter);
```

Add the helper after `RespondForRaidAsync` (mirrors it, swapping the resolve/format/footer to smelting):

```csharp
    private async Task RespondForSmeltAsync(string query)
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
            var names = scope.ServiceProvider.GetRequiredService<IItemNameResolver>();
            var loc = scope.ServiceProvider.GetRequiredService<ILocalizer>();
            var workspace = scope.ServiceProvider.GetRequiredService<IWorkspaceStore>();
            var culture = await workspace.GetCultureAsync(Context.Guild.Id).ConfigureAwait(false);

            var text = db.ResolveSmelter(query) switch
            {
                SmeltMatch.Found f => loc.Get("command.smelt.ok", culture, SmeltLine.Format(f.Smelter, names)),
                SmeltMatch.Ambiguous a => loc.Get("command.item.ambiguous", culture,
                    string.Join(", ", a.Candidates.Select(c => c.Name))),
                _ => loc.Get("command.item.notfound", culture, query),
            };

            var embed = new EmbedBuilder()
                .WithDescription(text)
                .WithFooter($"data as of {db.Sources.SmeltingAsOf:yyyy-MM-dd}")
                .Build();
            await RespondAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
        }
    }
```

Update the module's class XML summary to mention `/smelt` (currently lists through `/durability`):

```csharp
/// <summary>The /item, /recycle, /craft, /research, /decay, /upkeep, /durability, and /smelt slash commands.</summary>
```

(The `SmeltMatch` type is in `RustPlusBot.Features.ItemData.Lookup`, already imported by this file.)

- [ ] **Step 6: Build and run the full Commands suite**

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: build succeeds.

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`
Expected: PASS (whole Commands suite, including the help-catalog drift guards).

- [ ] **Step 7: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs src/RustPlusBot.Localization/Strings.resx src/RustPlusBot.Localization/Strings.fr.resx tests/RustPlusBot.Localization.Tests/StringsResourceParityTests.cs tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs
git commit -m "feat(commands): /smelt slash command + help catalog rows"
```

---

## Task 9: Generator README + final gates

Document the new source and run the full project gates.

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/README.md`

- [ ] **Step 1: Update the README**

In `tools/RustPlusBot.ItemData.Generator/README.md`:

Add the command list calculator (line ~5) — append `, and /smelt` to the calculators sentence.

Add a row to the source-file table (after the `rustlabsDurabilityData.json` row):

```markdown
   | `rustlabsSmeltingData.json` | smelting/cooking conversions (per smelter) |
```

Add `SmeltingAsOf` to the provenance list in the "Provenance dates" section:

```markdown
[`Program.cs`](Program.cs) (`NamesAsOf`, `RecycleAsOf`, `CraftAsOf`,
`ResearchAsOf`, `DecayAsOf`, `UpkeepAsOf`, `DurabilityAsOf`, `SmeltingAsOf`).
```

Add a Notes bullet describing the smelting projection:

```markdown
- Smelting data (`rustlabsSmeltingData.json`, ~28 KB) is keyed by **smelter**
  and projected into a dedicated `Smelters` table (`OfflineSmeltingSource`),
  resolved by name like raid targets. Its provenance (`SmeltingAsOf`,
  2023-11-05) lags the other rustlabs sources because the upstream file has not
  changed since then.
```

- [ ] **Step 2: Run the format gate (must produce no diff)**

Run: `dotnet tool restore` (restores the local jb/stryker/ef tools), then `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder`
Then: `git status --porcelain`
Expected: empty output (no reformatting diff). If the tool changed files, review and `git add` them, then re-run to confirm idempotence.

- [ ] **Step 3: Run the full build + test suite**

Run: `dotnet build RustPlusBot.slnx -maxcpucount:1`
Expected: build succeeds, 0 warnings/errors (`-warnaserror`).

Run: `dotnet test RustPlusBot.slnx -maxcpucount:1`
Expected: ALL tests green across every project (16 assemblies). Read the per-assembly counts — a dropped assembly means a broken build, not "fewer tests".

- [ ] **Step 4: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/README.md
git commit -m "docs(itemdata): document smelting source in generator README"
```

(If Step 2 produced reformatting changes, include them in this commit.)

- [ ] **Step 5: Push and open the PR**

```bash
git push -u origin feat/item-database-4
gh pr create --base develop --title "Subsystem 6d: Smelting calculator (/smelt + in-game, schema v4)" --body "Item-DB calculators pt.4. Smelter-keyed /smelt + in-game !smelt over a new Smelters table (schema v3→v4), sourced offline from rustlabsSmeltingData.json (10 smelters). Mirrors 6c. New ISmeltingSource + validator floor; bundle regenerated; EN/FR keys; /help +2 rows; full TDD.

🤖 Generated with [Claude Code](https://claude.com/claude-code)"
```

---

## Self-Review notes (verified against the spec)

- **Spec §3.1 schema** → Task 1 (records, members, version members). **§6 generator source** → Task 2. **§6 validator** → Task 3. **§6 wiring + bundle regen + §3.1 version bump** → Task 4. **§5 lookup** → Task 5. **§4.3 formatter** → Task 6. **§4.1 in-game** → Task 7. **§4.2 slash + §4.4 help** → Task 8. **§6 README** → Task 9.
- **Type consistency:** `Smelter`/`SmeltConversion` field names and order are identical everywhere they appear (Tasks 1, 2, 3, 5, 6). `ResolveSmelter`, `SmeltMatch.Found.Smelter`, `SmeltLine.Format(Smelter, IItemNameResolver)`, `command.smelt.ok` are used consistently across tasks.
- **Fan-out coverage:** the positional-record additions break `DatasetValidatorTests` (fixed in Task 1), the handler count in `CommandRegistrationTests` (fixed in Task 7), and `CommandHelpCatalogTests.HandlerNames` (fixed in Task 8) — each fixed in the task that introduces the change.
- **Green-at-each-commit:** schema version stays 3 until the bundle is regenerated in Task 4, where the emitted version and `ExpectedSchemaVersion` flip to 4 together.
