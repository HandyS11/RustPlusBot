# Subsystem 6b — Item DB Calculators pt.2 (Decay + Upkeep) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the 6a item dataset with per-item **decay** and **upkeep** data and ship `/decay`, `/upkeep` (and in-game `!decay`, `!upkeep`) calculators — a faithful mirror of 6a's `/recycle`/`/craft`/`/research`.

**Architecture:** Add two nullable fields to `ItemRecord`, bump the bundle `SchemaVersion`, teach the offline generator to load decay/upkeep + regenerate the embedded `item-data.json`, then add two pure formatters + two in-game handlers + two slash commands + EN/FR strings in `Features.Commands`. No new entities, migrations, events, options, or background services — static read-only data, zero EF drift.

**Tech Stack:** .NET 10, C# (positional records), System.Text.Json, xUnit + NSubstitute, Discord.Net interactions, ResX localization.

## Global Constraints

- Solution file is `RustPlusBot.slnx` (no `.sln`). Build with `dotnet build RustPlusBot.slnx`.
- Build is `0 warnings / 0 errors` under `-warnaserror` and strict Roslynator/Sonar analyzers.
- Format gate (hard CI): `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` must produce **zero diff**. Run `dotnet tool restore` first. Run it before every push; it reorders members Roslynator never flags.
- Tests: `dotnet test RustPlusBot.slnx`. Read **per-assembly counts** — a fake missing a new interface member silently drops a whole assembly's tests. Baseline is **586 tests / 16 assemblies** (post-6a).
- All user-facing reply/label strings are EN + FR via the shared `RustPlusBot.Localization` `Strings.resx` / `Strings.fr.resx` + `ILocalizer`. Every `help.*` description key must resolve (non-identity) in both `en` and `fr` (enforced by `CommandHelpCatalogTests`).
- No new EF migration / `ModelSnapshot` / `DbContext` / Domain-entity changes on this branch (verify at the end).
- Reference data lives at `~/Dev/rustplusplus/src/staticFiles/` (`rustlabsDecayData.json`, `rustlabsUpkeepData.json`).
- Branch: `feat/item-database-2` off `develop`.

**Data facts (verified against the current source files — rely on these):**

- Decay file shape: `{ "items": { "<id>": { decay, decayString, decayOutside, decayInside, decayUnderwater, hp, hpString } } }`. 137 entries; every entry has integer `decay` + `hp`; the `*Outside/Inside/Underwater` variant fields are **all null** in current data (modelled for forward-compat). All 137 ids resolve to item names.
- Upkeep file shape: `{ "items": { "<id>": [ { id: "<resourceId>", quantity: "<n | a–b>" } ] } }`. 26 entries (building blocks); quantity is `"1"` or an **en-dash** range like `"8–25"` (U+2013) — every value matches `^\d+([–-]\d+)?$`. All 26 ids and all cost ids resolve to item names.
- Both files are wrapped in a top-level `"items"` object — read `root.items`, **not** `root` (unlike the flat 6a recycle/craft/research files).
- Concrete ids for tests: decay `15388698` = "Stone Barricade" (decay 900s, hp 100); upkeep `1729120840` = "Wooden Door" (cost Wood `-151838493`).

---

## Task 1: Extend the dataset schema (records + every construction site)

Add `DecayInfo`, `UpkeepCost`, `UpkeepEntry`; add `Decay` + `Upkeep` to `ItemRecord`; add `DecayAsOf` + `UpkeepAsOf` to `DatasetSources`. Fix every construction site so the whole solution compiles and all existing tests still pass. **Schema version stays 1 and the bundle is NOT regenerated in this task** — the new fields are nullable/defaulted, so the existing version-1 `item-data.json` still loads.

**Files:**

- Modify: `src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`
- Modify (construction sites — append `, null, null` to each `new ItemRecord(...)`, append `, new(2024, 9, 7), new(2024, 9, 7)` to each `new DatasetSources(...)`):
  - `tools/RustPlusBot.ItemData.Generator/Program.cs:94,100`
  - `tests/RustPlusBot.Features.Commands.Tests/Formatting/ItemFormatterTests.cs:15,24,34,43`
  - `tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs:53,54`
  - `tests/RustPlusBot.Features.ItemData.Tests/ItemDatasetTests.cs:10`
  - `tests/RustPlusBot.Features.ItemData.Tests/ItemLookupTests.cs:67,68,82,83,84,117`
  - `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs:10(sources),12,14,39,52`
- Test: `tests/RustPlusBot.Features.ItemData.Tests/ItemDatasetTests.cs`

**Interfaces:**

- Produces: `ItemRecord(int Id, string Name, int StackSize, int? DespawnSeconds, RecycleYield? Recycle, CraftRecipe? Craft, ResearchCost? Research, DecayInfo? Decay, UpkeepCost? Upkeep)`; `DecayInfo(int? Seconds, int? OutsideSeconds, int? InsideSeconds, int? UnderwaterSeconds, int? Hp)`; `UpkeepCost(IReadOnlyList<UpkeepEntry> Entries)`; `UpkeepEntry(int ItemId, int QuantityMin, int QuantityMax)`; `DatasetSources(DateOnly NamesAsOf, DateOnly RecycleAsOf, DateOnly CraftAsOf, DateOnly ResearchAsOf, DateOnly DecayAsOf, DateOnly UpkeepAsOf)`.

- [ ] **Step 1: Write the failing test**

In `ItemDatasetTests.cs`, replace the body with a test that exercises the new fields:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class ItemDatasetTests
{
    [Fact]
    public void ItemRecord_AllowsNullCalculatorData()
    {
        var record = new ItemRecord(1, "Wood", 1000, null, null, null, null, null, null);
        Assert.Equal("Wood", record.Name);
        Assert.Null(record.Recycle);
        Assert.Null(record.Decay);
        Assert.Null(record.Upkeep);
    }

    [Fact]
    public void ItemRecord_CarriesDecayAndUpkeep()
    {
        var record = new ItemRecord(
            2, "Wooden Door", 1, null, null, null, null,
            new DecayInfo(900, null, null, null, 100),
            new UpkeepCost([new UpkeepEntry(-151838493, 8, 25)]));

        Assert.Equal(900, record.Decay!.Seconds);
        Assert.Equal(100, record.Decay.Hp);
        Assert.Equal(-151838493, record.Upkeep!.Entries[0].ItemId);
        Assert.Equal(8, record.Upkeep.Entries[0].QuantityMin);
        Assert.Equal(25, record.Upkeep.Entries[0].QuantityMax);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests/RustPlusBot.Features.ItemData.Tests.csproj`
Expected: COMPILE FAILURE — `ItemRecord` does not take 9 args; `DecayInfo`/`UpkeepCost`/`UpkeepEntry` do not exist.

- [ ] **Step 3: Extend the schema records**

In `src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`, update `DatasetSources` and `ItemRecord` and append the three new records:

```csharp
/// <summary>When each section of the dataset was last sourced, for "data as of" display.</summary>
/// <param name="NamesAsOf">Names/ids/stack source date.</param>
/// <param name="RecycleAsOf">Recycle data source date.</param>
/// <param name="CraftAsOf">Craft data source date.</param>
/// <param name="ResearchAsOf">Research data source date.</param>
/// <param name="DecayAsOf">Decay data source date.</param>
/// <param name="UpkeepAsOf">Upkeep data source date.</param>
public sealed record DatasetSources(
    DateOnly NamesAsOf,
    DateOnly RecycleAsOf,
    DateOnly CraftAsOf,
    DateOnly ResearchAsOf,
    DateOnly DecayAsOf,
    DateOnly UpkeepAsOf);
```

```csharp
/// <summary>One item, with all calculator data inlined (null where not applicable).</summary>
/// <param name="Id">The Rust item id.</param>
/// <param name="Name">The display name.</param>
/// <param name="StackSize">Max stack size.</param>
/// <param name="DespawnSeconds">Despawn time in seconds, or null if it does not despawn / unknown.</param>
/// <param name="Recycle">Recycler yield, or null if not recyclable.</param>
/// <param name="Craft">Craft recipe, or null if not craftable.</param>
/// <param name="Research">Research cost, or null if not researchable.</param>
/// <param name="Decay">Decay timing, or null if the item does not decay / unknown.</param>
/// <param name="Upkeep">Upkeep cost, or null if the item has no upkeep.</param>
public sealed record ItemRecord(
    int Id,
    string Name,
    int StackSize,
    int? DespawnSeconds,
    RecycleYield? Recycle,
    CraftRecipe? Craft,
    ResearchCost? Research,
    DecayInfo? Decay,
    UpkeepCost? Upkeep);
```

Append after `ResearchCost`:

```csharp
/// <summary>Decay timing for an item/deployable/building block. All fields nullable — a field is
/// populated only when RustLabs provides it. <paramref name="Seconds"/> is the base/default decay.</summary>
/// <param name="Seconds">Base decay time in seconds.</param>
/// <param name="OutsideSeconds">Decay time when placed outside, if distinct.</param>
/// <param name="InsideSeconds">Decay time when placed inside, if distinct.</param>
/// <param name="UnderwaterSeconds">Decay time when underwater, if distinct.</param>
/// <param name="Hp">The item's hit points.</param>
public sealed record DecayInfo(
    int? Seconds, int? OutsideSeconds, int? InsideSeconds, int? UnderwaterSeconds, int? Hp);

/// <summary>The upkeep cost to maintain a building block.</summary>
/// <param name="Entries">The per-resource upkeep cost entries.</param>
public sealed record UpkeepCost(IReadOnlyList<UpkeepEntry> Entries);

/// <summary>One upkeep resource cost. <paramref name="QuantityMin"/> equals
/// <paramref name="QuantityMax"/> for a single (non-range) quantity.</summary>
/// <param name="ItemId">The resource item id.</param>
/// <param name="QuantityMin">The lower bound of the cost.</param>
/// <param name="QuantityMax">The upper bound of the cost.</param>
public sealed record UpkeepEntry(int ItemId, int QuantityMin, int QuantityMax);
```

- [ ] **Step 4: Fix every construction site so the solution compiles**

Append `, null, null` to each `new ItemRecord(...)` listed in **Files** (the production site `Program.cs:94` and all test sites). Append `, new(2024, 9, 7), new(2024, 9, 7)` to each `new DatasetSources(...)` (`Program.cs:100`, `DatasetValidatorTests.cs:10`). Example for `EmbeddedItemDatabaseTests.cs:53-54`:

```csharp
var first = new ItemRecord(42, "First", 1, null, null, null, null, null, null);
var last = new ItemRecord(42, "Last", 1, null, null, null, null, null, null);
```

> Note: the two `Parse_*SchemaVersion*` literal-JSON tests in `EmbeddedItemDatabaseTests.cs` call `Parse(stream, 1)` with explicit version args and version-1 JSON — they need **no** change (the 2 missing source dates deserialize to `default(DateOnly)`, which the tests do not assert on).

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test RustPlusBot.slnx`
Expected: PASS — full suite green (588 tests: 586 baseline + 2 new `ItemDatasetTests`), every assembly builds.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs tools/RustPlusBot.ItemData.Generator/Program.cs tests/
git commit -m "feat(itemdata): add Decay + Upkeep schema fields (6b)"
```

---

## Task 2: Upkeep quantity parser (pure, loud-fail)

A pure helper that parses an upkeep quantity string (`"1"` or `"8–25"`) into `(min, max)`, throwing on anything else.

**Files:**

- Create: `tools/RustPlusBot.ItemData.Generator/Sources/UpkeepQuantity.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/UpkeepQuantityTests.cs`

**Interfaces:**

- Produces: `internal static class UpkeepQuantity { public static (int Min, int Max) Parse(string raw); }` — throws `FormatException` on unparseable input.

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class UpkeepQuantityTests
{
    [Fact]
    public void Single_value_parses_to_equal_min_max()
    {
        var (min, max) = UpkeepQuantity.Parse("1");
        Assert.Equal(1, min);
        Assert.Equal(1, max);
    }

    [Fact]
    public void EnDash_range_parses_min_and_max()
    {
        var (min, max) = UpkeepQuantity.Parse("8–25"); // 8–25
        Assert.Equal(8, min);
        Assert.Equal(25, max);
    }

    [Fact]
    public void Hyphen_range_parses_min_and_max()
    {
        var (min, max) = UpkeepQuantity.Parse("20-67");
        Assert.Equal(20, min);
        Assert.Equal(67, max);
    }

    [Fact]
    public void Whitespace_is_trimmed()
    {
        var (min, max) = UpkeepQuantity.Parse("  5–17 ");
        Assert.Equal(5, min);
        Assert.Equal(17, max);
    }

    [Theory]
    [InlineData("")]
    [InlineData("lots")]
    [InlineData("8–")]
    [InlineData("a–b")]
    public void Unparseable_throws(string raw)
    {
        Assert.Throws<FormatException>(() => UpkeepQuantity.Parse(raw));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: COMPILE FAILURE — `UpkeepQuantity` does not exist.

- [ ] **Step 3: Implement the parser**

```csharp
using System.Globalization;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Parses a RustLabs upkeep quantity string (e.g. <c>"1"</c> or <c>"8–25"</c>).</summary>
internal static class UpkeepQuantity
{
    /// <summary>Parses <paramref name="raw"/> into a (min, max) pair. A single value yields min == max.</summary>
    /// <param name="raw">The quantity string: a number or an en-dash/hyphen range.</param>
    /// <returns>The parsed lower and upper bounds.</returns>
    /// <exception cref="FormatException">The string is neither a number nor a number range.</exception>
    public static (int Min, int Max) Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var trimmed = raw.Trim();
        var dash = trimmed.IndexOfAny(['–', '-']);
        if (dash < 0)
        {
            var single = int.Parse(trimmed, CultureInfo.InvariantCulture);
            return (single, single);
        }

        var min = int.Parse(trimmed[..dash], CultureInfo.InvariantCulture);
        var max = int.Parse(trimmed[(dash + 1)..], CultureInfo.InvariantCulture);
        return (min, max);
    }
}
```

> `int.Parse` throws `FormatException` on `""`, `"lots"`, `"8–"`, `"a–b"` — this is the intended loud failure.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: PASS — 8 new tests green.

- [ ] **Step 5: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Sources/UpkeepQuantity.cs tests/RustPlusBot.ItemData.Generator.Tests/UpkeepQuantityTests.cs
git commit -m "feat(generator): add upkeep quantity parser (6b)"
```

---

## Task 3: Generator decay + upkeep loaders

Teach `IRustLabsSource` + `OfflineRustLabsSource` to load decay and upkeep from the two `"items"`-wrapped files.

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Sources/IRustLabsSource.cs`
- Modify: `tools/RustPlusBot.ItemData.Generator/Sources/OfflineRustLabsSource.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/OfflineRustLabsSourceTests.cs`

**Interfaces:**

- Consumes: `UpkeepQuantity.Parse` (Task 2); `DecayInfo`, `UpkeepCost`, `UpkeepEntry` (Task 1).
- Produces: on `IRustLabsSource`: `IReadOnlyDictionary<int, DecayInfo> LoadDecay();` and `IReadOnlyDictionary<int, UpkeepCost> LoadUpkeep();`. `OfflineRustLabsSource` primary ctor gains two params: `(string RecycleFilePath, string CraftFilePath, string ResearchFilePath, string DecayFilePath, string UpkeepFilePath)`.

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineRustLabsSourceTests
{
    private static OfflineRustLabsSource SourceWith(string decayJson, string upkeepJson)
    {
        var decayPath = Path.GetTempFileName();
        var upkeepPath = Path.GetTempFileName();
        File.WriteAllText(decayPath, decayJson);
        File.WriteAllText(upkeepPath, upkeepJson);
        // Recycle/craft/research paths are unused by these two loaders; point them at the upkeep temp file.
        return new OfflineRustLabsSource(upkeepPath, upkeepPath, upkeepPath, decayPath, upkeepPath);
    }

    [Fact]
    public void LoadDecay_readsItemsWrapper_andSecondsAndHp()
    {
        const string decay =
            """{"items":{"15388698":{"decay":900,"decayString":"15 min","decayOutside":null,"decayInside":null,"decayUnderwater":null,"hp":100,"hpString":"100"}}}""";
        var result = SourceWith(decay, """{"items":{}}""").LoadDecay();

        Assert.True(result.ContainsKey(15388698));
        Assert.Equal(900, result[15388698].Seconds);
        Assert.Equal(100, result[15388698].Hp);
        Assert.Null(result[15388698].OutsideSeconds);
    }

    [Fact]
    public void LoadUpkeep_readsItemsWrapper_andParsesRange()
    {
        const string upkeep =
            """{"items":{"1729120840":[{"id":"-151838493","quantity":"8–25"}]}}""";
        var result = SourceWith("""{"items":{}}""", upkeep).LoadUpkeep();

        Assert.True(result.ContainsKey(1729120840));
        var entry = Assert.Single(result[1729120840].Entries);
        Assert.Equal(-151838493, entry.ItemId);
        Assert.Equal(8, entry.QuantityMin);
        Assert.Equal(25, entry.QuantityMax);
    }

    [Fact]
    public void LoadUpkeep_singleQuantity_hasEqualMinMax()
    {
        const string upkeep = """{"items":{"671706427":[{"id":"317398316","quantity":"1"}]}}""";
        var entry = Assert.Single(SourceWith("""{"items":{}}""", upkeep).LoadUpkeep()[671706427].Entries);
        Assert.Equal(1, entry.QuantityMin);
        Assert.Equal(1, entry.QuantityMax);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: COMPILE FAILURE — `OfflineRustLabsSource` ctor takes 3 args; `LoadDecay`/`LoadUpkeep` do not exist.

- [ ] **Step 3: Extend the interface**

In `IRustLabsSource.cs`, add inside the interface:

```csharp
    /// <summary>Loads decay info keyed by item id.</summary>
    IReadOnlyDictionary<int, DecayInfo> LoadDecay();

    /// <summary>Loads upkeep costs keyed by item id.</summary>
    IReadOnlyDictionary<int, UpkeepCost> LoadUpkeep();
```

- [ ] **Step 4: Implement the loaders**

In `OfflineRustLabsSource.cs`, change the primary ctor signature to add the two paths and update the doc comment:

```csharp
internal sealed class OfflineRustLabsSource(
    string RecycleFilePath,
    string CraftFilePath,
    string ResearchFilePath,
    string DecayFilePath,
    string UpkeepFilePath) : IRustLabsSource
```

Append these two methods to the class:

```csharp
    /// <inheritdoc/>
    public IReadOnlyDictionary<int, DecayInfo> LoadDecay()
    {
        using var stream = File.OpenRead(DecayFilePath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, DecayInfo>();
        if (!doc.RootElement.TryGetProperty("items", out var itemsEl))
        {
            return result;
        }

        foreach (var prop in itemsEl.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            result[id] = new DecayInfo(
                ReadInt(prop.Value, "decay"),
                ReadInt(prop.Value, "decayOutside"),
                ReadInt(prop.Value, "decayInside"),
                ReadInt(prop.Value, "decayUnderwater"),
                ReadInt(prop.Value, "hp"));
        }

        return result;

        static int? ReadInt(JsonElement element, string name) =>
            element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
                ? p.GetInt32()
                : null;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<int, UpkeepCost> LoadUpkeep()
    {
        using var stream = File.OpenRead(UpkeepFilePath);
        using var doc = JsonDocument.Parse(stream);

        var result = new Dictionary<int, UpkeepCost>();
        if (!doc.RootElement.TryGetProperty("items", out var itemsEl))
        {
            return result;
        }

        foreach (var prop in itemsEl.EnumerateObject())
        {
            if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var entries = new List<UpkeepEntry>();
            foreach (var entry in prop.Value.EnumerateArray())
            {
                var entryIdStr = entry.GetProperty("id").GetString();
                if (entryIdStr is null ||
                    !int.TryParse(entryIdStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entryId))
                {
                    continue;
                }

                var quantity = entry.GetProperty("quantity").GetString()
                               ?? throw new InvalidOperationException($"upkeep {id}: null quantity");
                var (min, max) = UpkeepQuantity.Parse(quantity);
                entries.Add(new UpkeepEntry(entryId, min, max));
            }

            if (entries.Count > 0)
            {
                result[id] = new UpkeepCost(entries);
            }
        }

        return result;
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: PASS — 3 new loader tests green. (The `Program.cs` `OfflineRustLabsSource` construction does not yet pass the two new paths — it is updated in Task 5; the generator project still compiles because Task 1 left `Program.cs` constructing the source with 3 args, which is now a compile error. **Fix that in this task's Step 6** — see note.)

> **Build note:** changing the ctor breaks `Program.cs:60-63` immediately. To keep the generator compiling after Step 4, update that call in this task to pass the two new paths (they are wired into the load flow in Task 5):
>
> ```csharp
> var rustLabsSource = new OfflineRustLabsSource(
>     Path.Combine(rustplusDir, "rustlabsRecycleData.json"),
>     Path.Combine(rustplusDir, "rustlabsCraftData.json"),
>     Path.Combine(rustplusDir, "rustlabsResearchData.json"),
>     Path.Combine(rustplusDir, "rustlabsDecayData.json"),
>     Path.Combine(rustplusDir, "rustlabsUpkeepData.json"));
> ```

- [ ] **Step 6: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Sources/ tests/RustPlusBot.ItemData.Generator.Tests/OfflineRustLabsSourceTests.cs
git commit -m "feat(generator): load decay + upkeep from rustlabs files (6b)"
```

---

## Task 4: DatasetValidator decay + upkeep rules

Add referential + sanity checks: every upkeep cost id resolves; `Min <= Max`; decay seconds/hp non-negative.

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`

**Interfaces:**

- Consumes: `ItemRecord.Upkeep`/`.Decay`, `UpkeepEntry`, `DecayInfo` (Task 1).

- [ ] **Step 1: Write the failing tests**

Append to `DatasetValidatorTests.cs`:

```csharp
    /// <summary>An upkeep entry that references an unknown item id should produce an error.</summary>
    [Fact]
    public void UnresolvableUpkeepId_isError()
    {
        var bad = new ItemDataset(2, Good().Sources,
        [
            new ItemRecord(1, "Wooden Door", 1, null, null, null, null, null,
                new UpkeepCost([new UpkeepEntry(77777, 8, 25)])),
        ]);
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("77777", StringComparison.Ordinal));
    }

    /// <summary>An upkeep entry with min greater than max should produce an error.</summary>
    [Fact]
    public void UpkeepMinGreaterThanMax_isError()
    {
        var bad = new ItemDataset(2, Good().Sources,
        [
            new ItemRecord(1, "Wooden Door", 1, null, null, null, null, null,
                new UpkeepCost([new UpkeepEntry(1, 25, 8)])),
        ]);
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("upkeep", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Negative decay seconds should produce an error.</summary>
    [Fact]
    public void NegativeDecay_isError()
    {
        var bad = new ItemDataset(2, Good().Sources,
        [
            new ItemRecord(1, "Stone Barricade", 1, null, null, null, null,
                new DecayInfo(-900, null, null, null, 100), null),
        ]);
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("decay", StringComparison.OrdinalIgnoreCase));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: FAIL — `Assert.Contains` finds no matching error (validator does not yet check upkeep/decay).

- [ ] **Step 3: Add the validation rules**

In `DatasetValidator.Validate`, before `return errors;`, insert:

```csharp
        foreach (var item in dataset.Items.Where(i => i.Upkeep is not null))
        {
            foreach (var entry in item.Upkeep!.Entries)
            {
                if (!ids.Contains(entry.ItemId))
                {
                    errors.Add(
                        $"item {item.Id} ({item.Name}): upkeep references unknown id {entry.ItemId}");
                }

                if (entry.QuantityMin > entry.QuantityMax)
                {
                    errors.Add(
                        $"item {item.Id} ({item.Name}): upkeep quantity min {entry.QuantityMin} > max {entry.QuantityMax}");
                }
            }
        }

        foreach (var item in dataset.Items.Where(i => i.Decay is not null))
        {
            var decay = item.Decay!;
            if (decay.Seconds is < 0 || decay.OutsideSeconds is < 0 || decay.InsideSeconds is < 0 ||
                decay.UnderwaterSeconds is < 0 || decay.Hp is < 0)
            {
                errors.Add($"item {item.Id} ({item.Name}): decay has a negative value");
            }
        }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj`
Expected: PASS — 3 new validator tests green; `Good_dataset_hasNoErrors` still green.

- [ ] **Step 5: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs
git commit -m "feat(generator): validate decay + upkeep (6b)"
```

---

## Task 5: Wire generation, bump schema version, regenerate the bundle (cutover)

Inline decay/upkeep onto each `ItemRecord`, stamp the two new source dates, emit `SchemaVersion = 2`, bump `ExpectedSchemaVersion` to 2, and regenerate the embedded `item-data.json`. This is the atomic cutover — after it the bundle is version 2 and carries decay/upkeep.

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Program.cs`
- Modify: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs:12` (`ExpectedSchemaVersion`)
- Regenerate: `src/RustPlusBot.Features.ItemData/Data/item-data.json`
- Test: `tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs`

**Interfaces:**

- Consumes: `IRustLabsSource.LoadDecay`/`LoadUpkeep` (Task 3); `DatasetSources` 6-arg ctor + `ItemRecord` 9-arg ctor (Task 1).

- [ ] **Step 1: Write the failing integration test**

Append to `EmbeddedItemDatabaseTests.cs` (these assert against the **regenerated** bundle, so they fail until Step 4 regenerates it):

```csharp
    [Fact]
    public void GetById_StoneBarricade_HasDecay()
    {
        var rec = _db.GetById(15388698);
        Assert.NotNull(rec);
        Assert.NotNull(rec!.Decay);
        Assert.Equal(900, rec.Decay!.Seconds);
        Assert.Equal(100, rec.Decay.Hp);
    }

    [Fact]
    public void GetById_WoodenDoor_HasUpkeep()
    {
        var rec = _db.GetById(1729120840);
        Assert.NotNull(rec);
        Assert.NotNull(rec!.Upkeep);
        Assert.NotEmpty(rec.Upkeep!.Entries);
    }

    [Fact]
    public void Sources_DecayAndUpkeep_ArePopulated()
    {
        Assert.Equal(new DateOnly(2024, 9, 7), _db.Sources.DecayAsOf);
        Assert.Equal(new DateOnly(2024, 9, 7), _db.Sources.UpkeepAsOf);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests/RustPlusBot.Features.ItemData.Tests.csproj`
Expected: FAIL — current embedded bundle is still version 1 with no decay/upkeep (`rec.Decay` is null; `DecayAsOf` is `0001-01-01`).

- [ ] **Step 3: Wire the generator + bump versions**

In `Program.cs`, add the two date constants beside the others:

```csharp
    private static readonly DateOnly DecayAsOf = new(2024, 9, 7);
    private static readonly DateOnly UpkeepAsOf = new(2024, 9, 7);
```

Load the two dictionaries (after the `researchCosts` line):

```csharp
        var decayInfos = rustLabsSource.LoadDecay();
        var upkeepCosts = rustLabsSource.LoadUpkeep();
```

Add orphan logging (after the existing orphan block):

```csharp
        var orphanDecay = decayInfos.Keys.Count(k => !nameIds.Contains(k));
        var orphanUpkeep = upkeepCosts.Keys.Count(k => !nameIds.Contains(k));
        Console.WriteLine($"dropped {orphanDecay} orphan decay entries with no item name");
        Console.WriteLine($"dropped {orphanUpkeep} orphan upkeep entries with no item name");
```

Inline the two fields in the `Select` projection and pass them to `ItemRecord`:

```csharp
                var decay = decayInfos.TryGetValue(id, out var di) ? di : null;
                var upkeep = upkeepCosts.TryGetValue(id, out var uc) ? uc : null;
                return new ItemRecord(id, name, stackSize, despawn, recycle, craft, research, decay, upkeep);
```

Update the dataset construction (version → 2, sources → 6 dates):

```csharp
        var dataset = new ItemDataset(
            2,
            new DatasetSources(NamesAsOf, RecycleAsOf, CraftAsOf, ResearchAsOf, DecayAsOf, UpkeepAsOf),
            items);
```

In `EmbeddedItemDatabase.cs`, bump the constant:

```csharp
    private const int ExpectedSchemaVersion = 2;
```

- [ ] **Step 4: Regenerate the embedded bundle**

Run:

```bash
dotnet run --project tools/RustPlusBot.ItemData.Generator -- \
  --out src/RustPlusBot.Features.ItemData/Data/item-data.json \
  --rustplusplus ~/Dev/rustplusplus/src/staticFiles \
  --min-items 1000
```

Expected: exit `0`, console logs `dropped 0 orphan decay entries…` and `dropped 0 orphan upkeep entries…` (all 137 decay + 26 upkeep ids resolve), and `Emitted <n> items`. Confirm the file changed and now contains `"schemaVersion": 2` and at least one `"decay"` / `"upkeep"` object:

```bash
git diff --stat src/RustPlusBot.Features.ItemData/Data/item-data.json
grep -c '"schemaVersion": 2' src/RustPlusBot.Features.ItemData/Data/item-data.json
```

- [ ] **Step 5: Run the full ItemData + Commands suites to verify green**

Run: `dotnet test RustPlusBot.slnx`
Expected: PASS — the 3 new `EmbeddedItemDatabaseTests` green; all existing 6a ItemData tests (AK resolves, `NamesAsOf`, lookup) still green against the regenerated bundle; no assembly dropped.

- [ ] **Step 6: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Program.cs src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs src/RustPlusBot.Features.ItemData/Data/item-data.json tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs
git commit -m "feat(itemdata): generate decay + upkeep into v2 bundle (6b)"
```

---

## Task 6: `DecayLine` formatter

Pure one-line decay formatter.

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Formatting/DecayLine.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Formatting/DecayFormatterTests.cs`

**Interfaces:**

- Consumes: `ItemRecord.Decay`, `DecayInfo` (Task 1); `DurationFormat.Compact` (existing).
- Produces: `internal static class DecayLine { public static string Format(ItemRecord item); }`.

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class DecayFormatterTests
{
    [Fact]
    public void DecayLine_ShowsBaseDecayAndHp()
    {
        var item = new ItemRecord(1, "Stone Barricade", 1, null, null, null, null,
            new DecayInfo(900, null, null, null, 100), null);
        var line = DecayLine.Format(item);
        Assert.Contains("Stone Barricade", line, StringComparison.Ordinal);
        Assert.Contains("15m", line, StringComparison.Ordinal);
        Assert.Contains("100", line, StringComparison.Ordinal);
    }

    [Fact]
    public void DecayLine_ShowsVariantsWhenPresent()
    {
        var item = new ItemRecord(1, "Wall", 1, null, null, null, null,
            new DecayInfo(null, 3600, 7200, null, 250), null);
        var line = DecayLine.Format(item);
        Assert.Contains("outside", line, StringComparison.Ordinal);
        Assert.Contains("inside", line, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter DecayFormatterTests`
Expected: COMPILE FAILURE — `DecayLine` does not exist.

- [ ] **Step 3: Implement the formatter**

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line decay reply.</summary>
internal static class DecayLine
{
    /// <summary>Formats base decay, any present environment variant, and HP for an item.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Decay"/>).</param>
    public static string Format(ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Decay);
        var decay = item.Decay;

        var parts = new List<string>();
        Add(parts, null, decay.Seconds);
        Add(parts, "outside", decay.OutsideSeconds);
        Add(parts, "inside", decay.InsideSeconds);
        Add(parts, "underwater", decay.UnderwaterSeconds);

        var decayPart = parts.Count > 0 ? string.Join(", ", parts) : "—";
        var hp = decay.Hp is { } h
            ? string.Create(CultureInfo.InvariantCulture, $" · {h} HP")
            : string.Empty;
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name} decays in {decayPart}{hp}");

        static void Add(List<string> into, string? label, int? seconds)
        {
            if (seconds is not { } s)
            {
                return;
            }

            var compact = DurationFormat.Compact(TimeSpan.FromSeconds(s));
            into.Add(label is null
                ? compact
                : string.Create(CultureInfo.InvariantCulture, $"{label} {compact}"));
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter DecayFormatterTests`
Expected: PASS — 2 tests green.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Formatting/DecayLine.cs tests/RustPlusBot.Features.Commands.Tests/Formatting/DecayFormatterTests.cs
git commit -m "feat(commands): add DecayLine formatter (6b)"
```

---

## Task 7: `UpkeepLine` formatter

Pure one-line upkeep formatter; resolves resource ids to names; renders ranges as `min–max`. No period label (deliberately omitted pending RustLabs verification of the period semantics — see spec §8).

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Formatting/UpkeepLine.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Formatting/UpkeepFormatterTests.cs`

**Interfaces:**

- Consumes: `ItemRecord.Upkeep`, `UpkeepCost`, `UpkeepEntry` (Task 1); `IItemNameResolver` (existing).
- Produces: `internal static class UpkeepLine { public static string Format(ItemRecord item, IItemNameResolver names); }`.

- [ ] **Step 1: Write the failing test**

```csharp
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class UpkeepFormatterTests
{
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void UpkeepLine_RendersRange()
    {
        var item = new ItemRecord(1, "Wooden Door", 1, null, null, null, null, null,
            new UpkeepCost([new UpkeepEntry(-151838493, 8, 25)]));
        var line = UpkeepLine.Format(item, _names);
        Assert.Contains("Wooden Door", line, StringComparison.Ordinal);
        Assert.Contains("8–25", line, StringComparison.Ordinal); // 8–25
        Assert.Contains("Wood", line, StringComparison.Ordinal);
    }

    [Fact]
    public void UpkeepLine_RendersSingleQuantityWithoutRange()
    {
        var item = new ItemRecord(1, "Reinforced Glass Window", 1, null, null, null, null, null,
            new UpkeepCost([new UpkeepEntry(317398316, 1, 1)]));
        var line = UpkeepLine.Format(item, _names);
        Assert.DoesNotContain("–", line, StringComparison.Ordinal); // no en-dash for a single value
        Assert.Contains("1", line, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter UpkeepFormatterTests`
Expected: COMPILE FAILURE — `UpkeepLine` does not exist.

- [ ] **Step 3: Implement the formatter**

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line upkeep-cost reply.</summary>
internal static class UpkeepLine
{
    /// <summary>Formats the per-resource upkeep cost for a building block.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Upkeep"/>).</param>
    /// <param name="names">Resolves resource item ids to names.</param>
    public static string Format(ItemRecord item, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(item.Upkeep);

        var parts = item.Upkeep.Entries.Select(e =>
        {
            var quantity = e.QuantityMin == e.QuantityMax
                ? e.QuantityMin.ToString(CultureInfo.InvariantCulture)
                : string.Create(CultureInfo.InvariantCulture, $"{e.QuantityMin}–{e.QuantityMax}");
            return string.Create(CultureInfo.InvariantCulture, $"{quantity} {names.Resolve(e.ItemId)}");
        });
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name} upkeep: {string.Join(", ", parts)}");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter UpkeepFormatterTests`
Expected: PASS — 2 tests green.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Formatting/UpkeepLine.cs tests/RustPlusBot.Features.Commands.Tests/Formatting/UpkeepFormatterTests.cs
git commit -m "feat(commands): add UpkeepLine formatter (6b)"
```

---

## Task 8: `!decay` in-game handler + registration + help + strings

Add the `decay` handler, register it, add its help-catalog entry + EN/FR strings, and move the registration-count / golden-handler-list tripwires.

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/DecayCommandHandler.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs` (register after `ResearchCommandHandler`)
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs` (add `decay` to `InGame`)
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx` (`command.decay.ok/none`, `help.decay`)
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs` (count `23`→`24`, add `decay` assert)
- Modify: `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs` (add `decay` to `HandlerNames`)
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/DecayUpkeepHandlersTests.cs`

**Interfaces:**

- Consumes: `IItemDatabase`, `ILocalizer`, `ItemMatch`, `CommandContext`, `ICommandHandler` (existing); `DecayLine.Format` (Task 6).
- Produces: `internal sealed class DecayCommandHandler(IItemDatabase database, ILocalizer localizer) : ICommandHandler` with `Name => "decay"`.

- [ ] **Step 1: Write the failing test**

Create `DecayUpkeepHandlersTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class DecayUpkeepHandlersTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly ILocalizer _loc = new ResxLocalizer();
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Decay_name_is_decay()
        => Assert.Equal("decay", new DecayCommandHandler(_db, _loc).Name);

    [Fact]
    public async Task Decay_Found_returnsDecayLine()
    {
        var reply = await new DecayCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Stone Barricade"), CancellationToken.None);
        Assert.Contains("Stone Barricade", reply, StringComparison.Ordinal);
        Assert.Contains("15m", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decay_NoDecayData_returnsNone()
    {
        // "Assault Rifle" (id 1545779598) is in the bundle with null decay.
        var reply = await new DecayCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("Assault Rifle"), CancellationToken.None);
        Assert.Contains("Assault Rifle", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decay_NotFound_returnsMessage()
    {
        var reply = await new DecayCommandHandler(_db, _loc)
            .ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Decay_French_returnsFrenchNone()
    {
        var fr = new CommandContext(1, Guid.NewGuid(), "fr", 99, "Caller", ["Assault Rifle"]);
        var reply = await new DecayCommandHandler(_db, _loc).ExecuteAsync(fr, CancellationToken.None);
        Assert.Contains("dégrade", reply, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter DecayUpkeepHandlersTests`
Expected: COMPILE FAILURE — `DecayCommandHandler` does not exist.

- [ ] **Step 3: Implement the handler**

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!decay — shows an item's decay time and HP.</summary>
/// <param name="database">The item database.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class DecayCommandHandler(IItemDatabase database, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "decay";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.Resolve(query) switch
        {
            ItemMatch.Found { Item.Decay: not null } f =>
                localizer.Get("command.decay.ok", context.Culture, DecayLine.Format(f.Item)),
            ItemMatch.Found f => localizer.Get("command.decay.none", context.Culture, f.Item.Name),
            ItemMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
```

- [ ] **Step 4: Register the handler, add help entry, add strings, move tripwires**

Register (in `CommandServiceCollectionExtensions.cs`, after the `ResearchCommandHandler` line):

```csharp
        services.AddScoped<ICommandHandler, DecayCommandHandler>();
```

Add to `CommandHelpCatalog.InGame` (after the `research` entry):

```csharp
        new("decay", CommandGroup.ItemDb, "help.decay"),
```

Add the EN strings to `Strings.resx` (preserve alphabetical `name=` ordering — `command.decay.*` goes just before `command.item.*`; `help.decay` goes just before `help.item`):

```xml
  <data name="command.decay.none" xml:space="preserve">
    <value>{0} does not decay.</value>
  </data>
  <data name="command.decay.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
```

```xml
  <data name="help.decay" xml:space="preserve">
    <value>Decay time for an item</value>
  </data>
```

Add the FR strings to `Strings.fr.resx` (same positions):

```xml
  <data name="command.decay.none" xml:space="preserve">
    <value>{0} ne se dégrade pas.</value>
  </data>
  <data name="command.decay.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
```

```xml
  <data name="help.decay" xml:space="preserve">
    <value>Temps de dégradation d'un objet</value>
  </data>
```

In `CommandRegistrationTests.cs`: change `Assert.Equal(23, handlers.Count);` → `Assert.Equal(24, handlers.Count);` and add `Assert.Contains(handlers, h => h.Name == "decay");`.

In `CommandHelpCatalogTests.cs`: add `"decay"` to the `HandlerNames` array and update the `<summary>` count comment.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`
Expected: PASS — `DecayUpkeepHandlersTests` green; `CommandRegistrationTests` green at count 24; `CommandHelpCatalogTests` green (every handler has an entry, every key resolves EN/FR).

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/ src/RustPlusBot.Localization/ tests/RustPlusBot.Features.Commands.Tests/
git commit -m "feat(commands): add !decay in-game handler (6b)"
```

---

## Task 9: `!upkeep` in-game handler + registration + help + strings

Mirror of Task 8 for upkeep (the handler also takes `IItemNameResolver`).

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/UpkeepCommandHandler.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs` (register after `DecayCommandHandler`)
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs` (add `upkeep` to `InGame`)
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx` (`command.upkeep.ok/none`, `help.upkeep`)
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs` (count `24`→`25`, add `upkeep` assert)
- Modify: `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs` (add `upkeep` to `HandlerNames`)
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/DecayUpkeepHandlersTests.cs` (append upkeep cases)

**Interfaces:**

- Consumes: `IItemDatabase`, `IItemNameResolver`, `ILocalizer`, `ItemMatch`, `CommandContext`, `ICommandHandler` (existing); `UpkeepLine.Format` (Task 7).
- Produces: `internal sealed class UpkeepCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer) : ICommandHandler` with `Name => "upkeep"`.

- [ ] **Step 1: Write the failing test**

Append to `DecayUpkeepHandlersTests.cs`:

```csharp
    [Fact]
    public void Upkeep_name_is_upkeep()
        => Assert.Equal("upkeep", new UpkeepCommandHandler(_db, _names, _loc).Name);

    [Fact]
    public async Task Upkeep_Found_returnsUpkeepLine()
    {
        var reply = await new UpkeepCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("Wooden Door"), CancellationToken.None);
        Assert.Contains("Wooden Door", reply, StringComparison.Ordinal);
        Assert.Contains("upkeep", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Upkeep_NoUpkeepData_returnsNone()
    {
        var reply = await new UpkeepCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("Assault Rifle"), CancellationToken.None);
        Assert.Contains("Assault Rifle", reply, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upkeep_French_returnsFrenchNone()
    {
        var fr = new CommandContext(1, Guid.NewGuid(), "fr", 99, "Caller", ["Assault Rifle"]);
        var reply = await new UpkeepCommandHandler(_db, _names, _loc).ExecuteAsync(fr, CancellationToken.None);
        Assert.Contains("entretien", reply, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj --filter DecayUpkeepHandlersTests`
Expected: COMPILE FAILURE — `UpkeepCommandHandler` does not exist.

- [ ] **Step 3: Implement the handler**

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!upkeep — shows a building block's upkeep cost.</summary>
/// <param name="database">The item database.</param>
/// <param name="names">Resolves resource ids to names.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class UpkeepCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "upkeep";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.Resolve(query) switch
        {
            ItemMatch.Found { Item.Upkeep: not null } f =>
                localizer.Get("command.upkeep.ok", context.Culture, UpkeepLine.Format(f.Item, names)),
            ItemMatch.Found f => localizer.Get("command.upkeep.none", context.Culture, f.Item.Name),
            ItemMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
```

- [ ] **Step 4: Register, add help entry, add strings, move tripwires**

Register (after `DecayCommandHandler`):

```csharp
        services.AddScoped<ICommandHandler, UpkeepCommandHandler>();
```

Add to `CommandHelpCatalog.InGame` (after the `decay` entry):

```csharp
        new("upkeep", CommandGroup.ItemDb, "help.upkeep"),
```

EN `Strings.resx` (`command.upkeep.*` after `command.research.*`; `help.upkeep` after `help.team` / before `help.uptime` per alphabetical order):

```xml
  <data name="command.upkeep.none" xml:space="preserve">
    <value>{0} has no upkeep cost.</value>
  </data>
  <data name="command.upkeep.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
```

```xml
  <data name="help.upkeep" xml:space="preserve">
    <value>Upkeep cost for a building block</value>
  </data>
```

FR `Strings.fr.resx`:

```xml
  <data name="command.upkeep.none" xml:space="preserve">
    <value>{0} n'a pas de coût d'entretien.</value>
  </data>
  <data name="command.upkeep.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
```

```xml
  <data name="help.upkeep" xml:space="preserve">
    <value>Coût d'entretien d'un bloc de construction</value>
  </data>
```

`CommandRegistrationTests.cs`: `Assert.Equal(24, …)` → `Assert.Equal(25, …)`; add `Assert.Contains(handlers, h => h.Name == "upkeep");`.

`CommandHelpCatalogTests.cs`: add `"upkeep"` to `HandlerNames`; update the count comment.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests/RustPlusBot.Features.Commands.Tests.csproj`
Expected: PASS — all handler/catalog/registration tests green at count 25.

- [ ] **Step 6: Commit**

```bash
git add src/RustPlusBot.Features.Commands/ src/RustPlusBot.Localization/ tests/RustPlusBot.Features.Commands.Tests/
git commit -m "feat(commands): add !upkeep in-game handler (6b)"
```

---

## Task 10: `/decay` + `/upkeep` slash commands + per-section footer date

Add the two slash commands to `ItemCommandModule` and generalise the embed footer so each command shows its own section date. Add their `Slash` help-catalog entries + `help.slash.*` strings.

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs` (add `decay`/`upkeep` to `Slash`)
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `Strings.fr.resx` (`help.slash.decay`, `help.slash.upkeep`)

**Interfaces:**

- Consumes: `DecayLine.Format` (Task 6), `UpkeepLine.Format` (Task 7), `DatasetSources.DecayAsOf`/`UpkeepAsOf` (Task 1), existing `RespondForAsync`.

> Interaction modules (`InteractionModuleBase`) are not unit-tested in this repo (repo convention — see `CommandSurfaceModule`); the tested logic lives in the Task 6/7 formatters and Task 8/9 handlers. This task's gate is a clean build + the full suite staying green.

- [ ] **Step 1: Generalise `RespondForAsync` to take a date selector**

In `ItemCommandModule.cs`, change the helper signature and footer:

```csharp
    private async Task RespondForAsync(
        string query,
        Func<IItemDatabase, DateOnly> dateSelector,
        Func<IItemDatabase, IItemNameResolver, ItemRecord, ILocalizer, string, string> onFound)
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

            var text = db.Resolve(query) switch
            {
                ItemMatch.Found f => onFound(db, names, f.Item, loc, culture),
                ItemMatch.Ambiguous a => loc.Get("command.item.ambiguous", culture,
                    string.Join(", ", a.Candidates.Select(c => c.Name))),
                _ => loc.Get("command.item.notfound", culture, query),
            };

            var embed = new EmbedBuilder()
                .WithDescription(text)
                .WithFooter($"data as of {dateSelector(db):yyyy-MM-dd}")
                .Build();
            await RespondAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
        }
    }
```

Update the four existing slash commands to pass their section date selector:

```csharp
    [SlashCommand("item", "Look up an item")]
    public Task ItemAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.NamesAsOf, (_, _, rec, loc, culture) =>
            loc.Get("command.item.ok", culture, ItemLine.Format(rec)));

    [SlashCommand("recycle", "Show recycler output for an item")]
    public Task RecycleAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.RecycleAsOf, (_, names, rec, loc, culture) => rec.Recycle is not null
            ? loc.Get("command.recycle.ok", culture, RecycleLine.Format(rec, names))
            : loc.Get("command.recycle.none", culture, rec.Name));

    [SlashCommand("craft", "Show an item's craft recipe")]
    public Task CraftAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.CraftAsOf, (_, names, rec, loc, culture) => rec.Craft is not null
            ? loc.Get("command.craft.ok", culture, CraftLine.Format(rec, names))
            : loc.Get("command.craft.none", culture, rec.Name));

    [SlashCommand("research", "Show an item's research scrap cost")]
    public Task ResearchAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.ResearchAsOf, (_, _, rec, loc, culture) => rec.Research is not null
            ? loc.Get("command.research.ok", culture, ResearchLine.Format(rec))
            : loc.Get("command.research.none", culture, rec.Name));
```

- [ ] **Step 2: Add the two new slash commands**

After `ResearchAsync`, add:

```csharp
    /// <summary>Shows an item's decay time.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("decay", "Show an item's decay time")]
    public Task DecayAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.DecayAsOf, (_, _, rec, loc, culture) => rec.Decay is not null
            ? loc.Get("command.decay.ok", culture, DecayLine.Format(rec))
            : loc.Get("command.decay.none", culture, rec.Name));

    /// <summary>Shows a building block's upkeep cost.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("upkeep", "Show a building block's upkeep cost")]
    public Task UpkeepAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, db => db.Sources.UpkeepAsOf, (_, names, rec, loc, culture) => rec.Upkeep is not null
            ? loc.Get("command.upkeep.ok", culture, UpkeepLine.Format(rec, names))
            : loc.Get("command.upkeep.none", culture, rec.Name));
```

- [ ] **Step 3: Add `Slash` help-catalog entries + `help.slash.*` strings**

In `CommandHelpCatalog.Slash` (after the `research` entry):

```csharp
        new("decay", CommandGroup.ItemDb, "help.slash.decay"),
        new("upkeep", CommandGroup.ItemDb, "help.slash.upkeep"),
```

EN `Strings.resx` (`help.slash.decay` before `help.slash.help`; `help.slash.upkeep` after `help.slash.research`/before `help.slash.uptime` — keep alphabetical):

```xml
  <data name="help.slash.decay" xml:space="preserve">
    <value>Show an item's decay time</value>
  </data>
```

```xml
  <data name="help.slash.upkeep" xml:space="preserve">
    <value>Show a building block's upkeep cost</value>
  </data>
```

FR `Strings.fr.resx`:

```xml
  <data name="help.slash.decay" xml:space="preserve">
    <value>Affiche le temps de dégradation d'un objet</value>
  </data>
```

```xml
  <data name="help.slash.upkeep" xml:space="preserve">
    <value>Affiche le coût d'entretien d'un bloc de construction</value>
  </data>
```

- [ ] **Step 4: Build + run the full suite**

Run: `dotnet build RustPlusBot.slnx` then `dotnet test RustPlusBot.slnx`
Expected: build `0/0`; full suite green — `CommandHelpCatalogTests.EveryDescriptionKeyResolvesInEnglishAndFrench` passes for the new `help.slash.*` keys.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs src/RustPlusBot.Localization/
git commit -m "feat(commands): add /decay + /upkeep slash commands + per-section footer (6b)"
```

---

## Task 11: Final verification gate

Confirm all global gates pass and there is no EF drift before handing the branch off for review.

**Files:** none (verification only; commit any jb reformat).

- [ ] **Step 1: Full build under strict analyzers**

Run: `dotnet build RustPlusBot.slnx`
Expected: `0 Warning(s) / 0 Error(s)`.

- [ ] **Step 2: Full test suite, per-assembly counts**

Run: `dotnet test RustPlusBot.slnx`
Expected: all assemblies green. ItemData +3, Generator +11 (8 parser + 3 loader, minus none) … actually: Generator.Tests +11 (8 `UpkeepQuantityTests` + 3 `OfflineRustLabsSourceTests`) + 3 `DatasetValidatorTests`; Commands.Tests +4 `DecayFormatterTests`/`UpkeepFormatterTests` (2+2) + ~9 `DecayUpkeepHandlersTests`; ItemData.Tests +2 `ItemDatasetTests` +3 `EmbeddedItemDatabaseTests`. Confirm **no assembly's count dropped** versus baseline.

- [ ] **Step 3: Format gate (zero diff)**

Run:

```bash
dotnet tool restore
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git status --short
```

Expected: if jb reordered members, commit the result; re-running must produce a clean `git status`.

```bash
git add -A && git commit -m "style: jb ReformatAndReorder (6b)" || echo "no reformat needed"
```

- [ ] **Step 4: Confirm no EF / entity drift**

Run:

```bash
git diff --name-only develop... | grep -E 'Migrations/|ModelSnapshot|DbContext|Domain/.*\.cs' || echo "no EF/entity changes (expected)"
```

Expected: prints `no EF/entity changes (expected)` — 6b touches only ItemData, the generator, Commands, and Localization.

- [ ] **Step 5: Confirm the bundle was regenerated**

Run:

```bash
git diff --name-only develop... | grep 'item-data.json' && grep -c '"schemaVersion": 2' src/RustPlusBot.Features.ItemData/Data/item-data.json
```

Expected: `item-data.json` is in the changed set and contains `"schemaVersion": 2`.

- [ ] **Step 6: Update the generator README**

Add `rustlabsDecayData.json` + `rustlabsUpkeepData.json` to the source-files list and decay/upkeep to the validation description in `tools/RustPlusBot.ItemData.Generator/README.md` (and the `Features.ItemData/README.md` calculator list if it enumerates the calculators). Commit:

```bash
git add tools/RustPlusBot.ItemData.Generator/README.md src/RustPlusBot.Features.ItemData/README.md
git commit -m "docs: document decay + upkeep in item-data READMEs (6b)"
```

---

## Self-Review

**Spec coverage:**

- Schema (`DecayInfo`/`UpkeepCost`/`UpkeepEntry` on `ItemRecord`, `DecayAsOf`/`UpkeepAsOf`, version bump) → Tasks 1, 5. ✓
- Generator loaders + validator + regen → Tasks 2, 3, 4, 5. ✓
- In-game `!decay`/`!upkeep` → Tasks 8, 9. ✓
- Slash `/decay`/`/upkeep` + per-section footer → Task 10. ✓
- `/help` ItemDb rows (InGame + Slash) + drift guards → Tasks 8, 9, 10. ✓
- EN/FR resx → Tasks 8, 9, 10. ✓
- Structured upkeep min/max, loud-fail parse → Tasks 1, 2, 3, 4. ✓
- All decay variants modelled, formatter prints non-null → Tasks 1, 6. ✓
- No new entities/migrations/events/options/services; gates → Task 11. ✓
- Upkeep period label deliberately omitted pending verification (spec §8) → Task 7 note. ✓
- Deferred (durability/smelting/cctv/4c integration) → not in plan. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code; every command shows expected output. ✓

**Type consistency:** `ItemRecord` 9-arg shape, `DatasetSources` 6-arg shape, `DecayInfo(int? Seconds, int? OutsideSeconds, int? InsideSeconds, int? UnderwaterSeconds, int? Hp)`, `UpkeepCost(IReadOnlyList<UpkeepEntry>)`, `UpkeepEntry(int ItemId, int QuantityMin, int QuantityMax)`, `UpkeepQuantity.Parse → (int Min, int Max)`, `DecayLine.Format(ItemRecord)`, `UpkeepLine.Format(ItemRecord, IItemNameResolver)`, `DecayCommandHandler(IItemDatabase, ILocalizer)`, `UpkeepCommandHandler(IItemDatabase, IItemNameResolver, ILocalizer)` — used consistently across tasks. ✓
