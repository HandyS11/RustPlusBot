# Subsystem 6c — Durability (raid-cost) Calculator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a `/durability` + `!durability` raid-cost calculator that lists the explosives needed to destroy any target (deployable item, building block, or vehicle), with quantity, sulfur, and time.

**Architecture:** A dedicated `RaidTargets` table is added to the embedded item dataset (Approach A — `ItemRecord` untouched). The generator trims the 19.3 MB `rustlabsDurabilityData.json` to the `explosive` tool group (~1 MB projection). A generalized name-matcher backs both the existing item lookup and a new raid-target lookup. Commands mirror the 6b decay/upkeep pair.

**Tech Stack:** C# / .NET, System.Text.Json, xUnit, Discord.Net interactions, `.resx` localization, JetBrains cleanupcode.

## Global Constraints

- Solution is `RustPlusBot.slnx` (no `.sln`). Build/test/format against it.
- `dotnet jb cleanupcode --profile=ReformatAndReorder` is a hard CI gate — it must produce **no diff**.
- TDD: failing test first, minimal implementation, green, commit. Frequent commits.
- No new EF entities, migrations, events, options, or hosted services (static read-only data).
- New `Raid*` schema types live in `RustPlusBot.Features.ItemData/Data/ItemDataset.cs`, namespace `RustPlusBot.Features.ItemData.Data`, and must be **public** (consumed by Commands, generator, tests).
- Never bundle `rustlabsDurabilityData.json` raw — keep only `group == "explosive"`.
- Branch: `feat/item-database-3` off `develop` (no worktrees — work in the main checkout).
- The bundle regeneration command (used in Task 4):

  ```bash
  dotnet run --project tools/RustPlusBot.ItemData.Generator -- \
    --out src/RustPlusBot.Features.ItemData/Data/item-data.json \
    --rustplusplus ~/Dev/rustplusplus/src/staticFiles \
    --min-items 1000
  ```

---

## Setup (before Task 1)

- [ ] **Create the branch**

```bash
git checkout develop && git pull --ff-only
git checkout -b feat/item-database-3
```

---

## File Structure

**Schema (`src/RustPlusBot.Features.ItemData/`)**

- `Data/ItemDataset.cs` — MODIFY: add `RaidTarget`/`RaidCost`/`RaidTargetKind`, `ItemDataset.RaidTargets`, `DatasetSources.DurabilityAsOf`.
- `Lookup/NameMatcher.cs` — CREATE: generic name/id matcher core.
- `Lookup/RaidMatch.cs` — CREATE: raid-target discriminated union.
- `Lookup/RaidLookup.cs` — CREATE: raid lookup over `RaidTarget` names.
- `Lookup/ItemLookup.cs` — MODIFY: delegate to `NameMatcher` (behavior preserved).
- `IItemDatabase.cs` — MODIFY: add `ResolveRaidTarget`.
- `EmbeddedItemDatabase.cs` — MODIFY: raid index + `ResolveRaidTarget`; bump `ExpectedSchemaVersion` to 3 (Task 4).
- `Data/item-data.json` — REGENERATE (Task 4): schema v3 + `raidTargets`.

**Generator (`tools/RustPlusBot.ItemData.Generator/`)**

- `Sources/IDurabilitySource.cs` — CREATE.
- `Sources/OfflineDurabilitySource.cs` — CREATE: explosive-only projection of the 3 sections.
- `Validation/DatasetValidator.cs` — MODIFY: raid-target floor + tool-id + quantity checks.
- `Program.cs` — MODIFY: wire source, stamp `DurabilityAsOf`, emit `RaidTargets`, bump version (Task 4).
- `README.md` — MODIFY (Task 8): document the new source + provenance.

**Commands (`src/RustPlusBot.Features.Commands/`)**

- `Formatting/DurabilityLine.cs` — CREATE.
- `Handlers/DurabilityCommandHandler.cs` — CREATE.
- `Modules/ItemCommandModule.cs` — MODIFY: `/durability` + `RespondForRaidAsync`.
- `CommandServiceCollectionExtensions.cs` — MODIFY: register handler.
- `Help/CommandHelpCatalog.cs` — MODIFY: InGame + Slash rows.

**Localization (`src/RustPlusBot.Localization/`)**

- `Strings.resx` / `Strings.fr.resx` — MODIFY: `command.durability.ok`, `help.durability`, `help.slash.durability`.

**Tests**

- `tests/RustPlusBot.Features.ItemData.Tests/ItemDatasetTests.cs` — MODIFY.
- `tests/RustPlusBot.Features.ItemData.Tests/RaidLookupTests.cs` — CREATE.
- `tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs` — MODIFY (Task 4).
- `tests/RustPlusBot.ItemData.Generator.Tests/OfflineDurabilitySourceTests.cs` — CREATE.
- `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs` — MODIFY.
- `tests/RustPlusBot.Features.Commands.Tests/Formatting/DurabilityFormatterTests.cs` — CREATE.
- `tests/RustPlusBot.Features.Commands.Tests/Handlers/DurabilityHandlerTests.cs` — CREATE.
- `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs` — MODIFY.
- `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs` — MODIFY.

---

### Task 1: Schema — `RaidTarget`/`RaidCost`/`RaidTargetKind` + dataset members

Adds the new records and the `RaidTargets` / `DurabilityAsOf` members, then makes every construction site compile. The emitted schema version stays **2** and raid data stays **empty** here — the runtime keeps loading today's v2 bundle (its missing `raidTargets`/`durabilityAsOf` deserialize to null/default, which we coalesce). Real data + the v3 bump land in Task 4.

**Files:**

- Modify: `src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`
- Modify: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs:21` (coalesce nulls)
- Modify: `tools/RustPlusBot.ItemData.Generator/Program.cs:11-16,111-114`
- Modify: `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs` (5 construction sites)
- Test: `tests/RustPlusBot.Features.ItemData.Tests/ItemDatasetTests.cs`

**Interfaces:**

- Produces: `RaidTarget(string Key, string Name, RaidTargetKind Kind, IReadOnlyList<RaidCost> Costs)`; `RaidCost(int ToolId, string? Side, string? Caption, double Quantity, double? TimeSeconds, int? Sulfur, int? Fuel)`; `enum RaidTargetKind { Item, BuildingBlock, Vehicle }`; `ItemDataset(int, DatasetSources, IReadOnlyList<ItemRecord> Items, IReadOnlyList<RaidTarget> RaidTargets)`; `DatasetSources(..., DateOnly DurabilityAsOf)`.

- [ ] **Step 1: Write the failing test**

In `tests/RustPlusBot.Features.ItemData.Tests/ItemDatasetTests.cs`, add:

```csharp
    [Fact]
    public void RaidTarget_CarriesExplosiveCosts()
    {
        var target = new RaidTarget("Stone Wall", "Stone Wall", RaidTargetKind.BuildingBlock,
        [
            new RaidCost(1248356124, "both", null, 2, 11.5, 4400, 120),
        ]);

        Assert.Equal(RaidTargetKind.BuildingBlock, target.Kind);
        var cost = Assert.Single(target.Costs);
        Assert.Equal(1248356124, cost.ToolId);
        Assert.Equal(4400, cost.Sulfur);
        Assert.Equal(2, cost.Quantity);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests --filter RaidTarget_CarriesExplosiveCosts`
Expected: FAIL — `RaidTarget` / `RaidCost` / `RaidTargetKind` do not exist (compile error).

- [ ] **Step 3: Add the schema types and members**

In `src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`, change `ItemDataset` and `DatasetSources` and append the new types:

```csharp
/// <summary>The full bundled item dataset. Deserialized from the embedded <c>item-data.json</c>.</summary>
/// <param name="SchemaVersion">The schema version; the loader rejects a mismatched bundle.</param>
/// <param name="Sources">Per-section provenance dates.</param>
/// <param name="Items">Every known item, one record each.</param>
/// <param name="RaidTargets">Every raid target (item/building-block/vehicle) and its explosive cost.</param>
public sealed record ItemDataset(
    int SchemaVersion,
    DatasetSources Sources,
    IReadOnlyList<ItemRecord> Items,
    IReadOnlyList<RaidTarget> RaidTargets);
```

Add `DurabilityAsOf` as the final `DatasetSources` parameter (keep the existing six, add the seventh):

```csharp
public sealed record DatasetSources(
    DateOnly NamesAsOf,
    DateOnly RecycleAsOf,
    DateOnly CraftAsOf,
    DateOnly ResearchAsOf,
    DateOnly DecayAsOf,
    DateOnly UpkeepAsOf,
    DateOnly DurabilityAsOf);
```

Append at the end of the file:

```csharp
/// <summary>The kind of raid target, mapping to the three sections of the RustLabs durability source.</summary>
public enum RaidTargetKind
{
    /// <summary>A deployable item (resolves to a known item id).</summary>
    Item,

    /// <summary>A building block (wall, door, floor) — name-keyed, not an item.</summary>
    BuildingBlock,

    /// <summary>A vehicle or NPC target — name-keyed, not an item.</summary>
    Vehicle,
}

/// <summary>One raid target and the explosive cost to destroy it.</summary>
/// <param name="Key">The item id as a string (<see cref="RaidTargetKind.Item"/>) or the target name otherwise.</param>
/// <param name="Name">The display name.</param>
/// <param name="Kind">The target kind.</param>
/// <param name="Costs">The per-explosive cost entries (always non-empty).</param>
public sealed record RaidTarget(string Key, string Name, RaidTargetKind Kind, IReadOnlyList<RaidCost> Costs);

/// <summary>One explosive's cost against a target. Fields are null where RustLabs omits them.</summary>
/// <param name="ToolId">The explosive item id (resolves via the item spine).</param>
/// <param name="Side">The building-block face: "soft", "hard", "both", or null.</param>
/// <param name="Caption">A sub-label (e.g. ammo variant or placement note), or null.</param>
/// <param name="Quantity">Units of the tool required.</param>
/// <param name="TimeSeconds">Total time in seconds, or null.</param>
/// <param name="Sulfur">Total sulfur cost, or null.</param>
/// <param name="Fuel">Total low-grade fuel cost, or null.</param>
public sealed record RaidCost(
    int ToolId,
    string? Side,
    string? Caption,
    double Quantity,
    double? TimeSeconds,
    int? Sulfur,
    int? Fuel);
```

- [ ] **Step 4: Make `EmbeddedItemDatabase` tolerate a v2 bundle (null raid list)**

In `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`, immediately after line 21 (`ById` field), add a coalesced raid field so the still-v2 bundle (no `raidTargets`) loads cleanly:

```csharp
    private static readonly IReadOnlyList<RaidTarget> Raid = Dataset.RaidTargets ?? [];
```

(The `ResolveRaidTarget` method and id-index arrive in Task 3 — this field just stops `Raid` being null between now and then.)

- [ ] **Step 5: Update the generator to emit the new (empty) member and a provenance date**

In `tools/RustPlusBot.ItemData.Generator/Program.cs`, add the provenance constant after line 16 (`UpkeepAsOf`):

```csharp
    private static readonly DateOnly DurabilityAsOf = new(2024, 9, 7);
```

Replace the dataset construction at lines 111-114 with (still version `2`, empty raid list — real data in Task 4):

```csharp
        var dataset = new ItemDataset(
            2,
            new DatasetSources(NamesAsOf, RecycleAsOf, CraftAsOf, ResearchAsOf, DecayAsOf, UpkeepAsOf, DurabilityAsOf),
            items,
            []);
```

- [ ] **Step 6: Fix the validator-test construction sites**

In `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`:

Update `Good()` (line 9) to add the seventh `DatasetSources` date and the empty `RaidTargets` argument:

```csharp
    private static ItemDataset Good() => new(1,
        new DatasetSources(new(2026, 4, 8), new(2024, 9, 7), new(2024, 9, 7), new(2024, 9, 7), new(2024, 9, 7),
            new(2024, 9, 7), new(2024, 9, 7)),
        [
            new ItemRecord(1, "AK-47", 1, 3600,
                new RecycleYield([new YieldEntry(2, 4, 1.0)]), null, null, null, null),
            new ItemRecord(2, "Metal Fragments", 1000, null, null, null, null, null, null),
        ],
        []);
```

For each of the five `new ItemDataset(N, Good().Sources, [ ... ])` blocks (lines 38, 51, 64, 77, 90), add a trailing `[]` argument after the items list. Example for the first one:

```csharp
        var bad = new ItemDataset(1, Good().Sources,
        [
            new ItemRecord(1, "AK-47", 1, null,
                new RecycleYield([new YieldEntry(99999, 4, 1.0)]), null, null, null, null),
        ],
        []);
```

Apply the identical `[]`-after-items change to the other four.

- [ ] **Step 7: Run tests to verify green**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests tests/RustPlusBot.ItemData.Generator.Tests`
Expected: PASS (including `RaidTarget_CarriesExplosiveCosts`).

- [ ] **Step 8: Commit**

```bash
git add src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs \
  src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs \
  tools/RustPlusBot.ItemData.Generator/Program.cs \
  tests/RustPlusBot.Features.ItemData.Tests/ItemDatasetTests.cs \
  tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs
git commit -m "feat(6c): add RaidTarget schema + DurabilityAsOf provenance (empty, v2)"
```

---

### Task 2: Generator — `OfflineDurabilitySource` (explosive-only projection)

**Files:**

- Create: `tools/RustPlusBot.ItemData.Generator/Sources/IDurabilitySource.cs`
- Create: `tools/RustPlusBot.ItemData.Generator/Sources/OfflineDurabilitySource.cs`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/OfflineDurabilitySourceTests.cs`

**Interfaces:**

- Consumes: `RaidTarget`, `RaidCost`, `RaidTargetKind` (Task 1).
- Produces: `IDurabilitySource.LoadRaidTargets(IReadOnlyDictionary<int, string> names) → IReadOnlyList<RaidTarget>`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.ItemData.Generator.Tests/OfflineDurabilitySourceTests.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.ItemData.Generator.Sources;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class OfflineDurabilitySourceTests
{
    private static OfflineDurabilitySource SourceWith(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return new OfflineDurabilitySource(path);
    }

    [Fact]
    public void LoadRaidTargets_KeepsExplosiveOnly_AndProjectsAllThreeKinds()
    {
        const string json = """
        {
          "items": { "100": [
            {"group":"explosive","which":null,"toolId":"1248356124","caption":null,"quantity":1,"time":10,"fuel":60,"sulfur":2200},
            {"group":"guns","which":null,"toolId":"99","caption":"AK","quantity":500,"time":120,"fuel":null,"sulfur":null}
          ]},
          "buildingBlocks": { "Stone Wall": [
            {"group":"explosive","which":"soft","toolId":"1248356124","caption":null,"quantity":2,"time":11.5,"fuel":120,"sulfur":4400}
          ]},
          "other": { "Bradley APC": [
            {"group":"explosive","which":null,"toolId":"-742865266","caption":null,"quantity":7,"time":30,"fuel":null,"sulfur":9800}
          ]}
        }
        """;
        var names = new Dictionary<int, string>
        {
            [100] = "Tool Cupboard", [1248356124] = "Timed Explosive Charge", [-742865266] = "Rocket",
        };

        var targets = SourceWith(json).LoadRaidTargets(names);

        var tc = Assert.Single(targets, t => t.Kind == RaidTargetKind.Item);
        Assert.Equal("Tool Cupboard", tc.Name);
        Assert.Equal("100", tc.Key);
        var cost = Assert.Single(tc.Costs); // the "guns" row is filtered out
        Assert.Equal(1248356124, cost.ToolId);
        Assert.Equal(2200, cost.Sulfur);

        var wall = Assert.Single(targets, t => t.Kind == RaidTargetKind.BuildingBlock);
        Assert.Equal("Stone Wall", wall.Name);
        Assert.Equal("soft", Assert.Single(wall.Costs).Side);

        Assert.Single(targets, t => t.Kind == RaidTargetKind.Vehicle);
    }

    [Fact]
    public void LoadRaidTargets_DropsItemWithNoName()
    {
        const string json =
            """{"items":{"999":[{"group":"explosive","which":null,"caption":null,"toolId":"1","quantity":1,"time":1,"sulfur":1,"fuel":null}]}}""";
        var targets = SourceWith(json).LoadRaidTargets(new Dictionary<int, string>());
        Assert.Empty(targets);
    }

    [Fact]
    public void LoadRaidTargets_DropsTargetWithNoExplosiveRows()
    {
        const string json =
            """{"buildingBlocks":{"Twig Wall":[{"group":"melee","which":null,"caption":null,"toolId":"1","quantity":1,"time":1,"sulfur":null,"fuel":null}]}}""";
        var targets = SourceWith(json).LoadRaidTargets(new Dictionary<int, string>());
        Assert.Empty(targets);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests --filter OfflineDurabilitySourceTests`
Expected: FAIL — `OfflineDurabilitySource` / `IDurabilitySource` do not exist.

- [ ] **Step 3: Create the interface**

Create `tools/RustPlusBot.ItemData.Generator/Sources/IDurabilitySource.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Provides raid-target durability data (explosive costs) from RustLabs.</summary>
internal interface IDurabilitySource
{
    /// <summary>Loads raid targets, resolving item-kind target names via <paramref name="names"/>.</summary>
    /// <param name="names">Item id → display name, for the item-kind section.</param>
    /// <returns>Every target with at least one explosive cost.</returns>
    IReadOnlyList<RaidTarget> LoadRaidTargets(IReadOnlyDictionary<int, string> names);
}
```

- [ ] **Step 4: Create the offline implementation**

Create `tools/RustPlusBot.ItemData.Generator/Sources/OfflineDurabilitySource.cs`:

```csharp
using System.Globalization;
using System.Text.Json;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads raid durability data from the offline RustLabs JSON file, keeping only explosives.</summary>
/// <param name="DurabilityFilePath">Path to the <c>rustlabsDurabilityData.json</c> file.</param>
internal sealed class OfflineDurabilitySource(string DurabilityFilePath) : IDurabilitySource
{
    private const string RaidGroup = "explosive";

    /// <inheritdoc/>
    public IReadOnlyList<RaidTarget> LoadRaidTargets(IReadOnlyDictionary<int, string> names)
    {
        ArgumentNullException.ThrowIfNull(names);

        using var stream = File.OpenRead(DurabilityFilePath);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var targets = new List<RaidTarget>();
        AddSection(root, "items", RaidTargetKind.Item, names, targets);
        AddSection(root, "buildingBlocks", RaidTargetKind.BuildingBlock, names, targets);
        AddSection(root, "other", RaidTargetKind.Vehicle, names, targets);
        return targets;
    }

    private static void AddSection(JsonElement root, string section, RaidTargetKind kind,
        IReadOnlyDictionary<int, string> names, List<RaidTarget> into)
    {
        if (!root.TryGetProperty(section, out var sectionEl) || sectionEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in sectionEl.EnumerateObject())
        {
            string key;
            string name;
            if (kind == RaidTargetKind.Item)
            {
                if (!int.TryParse(prop.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ||
                    !names.TryGetValue(id, out var itemName))
                {
                    continue; // orphan id with no item name — drop
                }

                key = prop.Name;
                name = itemName;
            }
            else
            {
                key = prop.Name;
                name = prop.Name;
            }

            var costs = ReadCosts(prop.Value);
            if (costs.Count > 0)
            {
                into.Add(new RaidTarget(key, name, kind, costs));
            }
        }
    }

    private static List<RaidCost> ReadCosts(JsonElement rows)
    {
        var costs = new List<RaidCost>();
        foreach (var row in rows.EnumerateArray())
        {
            if (ReadString(row, "group") != RaidGroup)
            {
                continue;
            }

            var toolStr = ReadString(row, "toolId");
            if (toolStr is null ||
                !int.TryParse(toolStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var toolId))
            {
                continue;
            }

            if (ReadDouble(row, "quantity") is not { } quantity)
            {
                continue;
            }

            costs.Add(new RaidCost(
                toolId,
                ReadString(row, "which"),
                ReadString(row, "caption"),
                quantity,
                ReadDouble(row, "time"),
                ReadInt(row, "sulfur"),
                ReadInt(row, "fuel")));
        }

        return costs;
    }

    private static string? ReadString(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? ReadInt(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetInt32() : null;

    private static double? ReadDouble(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDouble() : null;
}
```

- [ ] **Step 5: Run tests to verify green**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests --filter OfflineDurabilitySourceTests`
Expected: PASS (all three).

- [ ] **Step 6: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Sources/IDurabilitySource.cs \
  tools/RustPlusBot.ItemData.Generator/Sources/OfflineDurabilitySource.cs \
  tests/RustPlusBot.ItemData.Generator.Tests/OfflineDurabilitySourceTests.cs
git commit -m "feat(6c): generator durability source (explosive-only projection)"
```

---

### Task 3: Lookup — generalized `NameMatcher` + `RaidLookup`/`RaidMatch` + `ResolveRaidTarget`

Extracts the item lookup's matching algorithm into a generic `NameMatcher`, refactors `ItemLookup` to delegate (behavior preserved — existing `ItemLookupTests` is the safety net), and adds the parallel raid lookup wired onto `IItemDatabase`.

**Files:**

- Create: `src/RustPlusBot.Features.ItemData/Lookup/NameMatcher.cs`
- Create: `src/RustPlusBot.Features.ItemData/Lookup/RaidMatch.cs`
- Create: `src/RustPlusBot.Features.ItemData/Lookup/RaidLookup.cs`
- Modify: `src/RustPlusBot.Features.ItemData/Lookup/ItemLookup.cs`
- Modify: `src/RustPlusBot.Features.ItemData/IItemDatabase.cs`
- Modify: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`
- Test: `tests/RustPlusBot.Features.ItemData.Tests/RaidLookupTests.cs`

**Interfaces:**

- Consumes: `RaidTarget` (Task 1); `ItemMatch` (existing).
- Produces: `RaidMatch { Found(RaidTarget Target) | Ambiguous(IReadOnlyList<RaidTarget> Candidates) | NotFound }`; `RaidLookup.Resolve(string, Func<int, RaidTarget?>, IReadOnlyList<RaidTarget>, int cap = 10) → RaidMatch`; `IItemDatabase.ResolveRaidTarget(string) → RaidMatch`; `NameMatcher.Resolve<T>(string?, Func<int,T?>, IReadOnlyList<T>, Func<T,string>, IComparer<T>, int) → (NameMatchKind, T?, IReadOnlyList<T>)` (internal).

- [ ] **Step 1: Write the failing raid-lookup tests**

Create `tests/RustPlusBot.Features.ItemData.Tests/RaidLookupTests.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class RaidLookupTests
{
    private static readonly RaidTarget Wall = new("Stone Wall", "Stone Wall", RaidTargetKind.BuildingBlock, []);
    private static readonly RaidTarget Door = new("Sheet Metal Door", "Sheet Metal Door", RaidTargetKind.BuildingBlock, []);
    private static readonly RaidTarget Tc = new("100", "Tool Cupboard", RaidTargetKind.Item, []);
    private static readonly IReadOnlyList<RaidTarget> All = [Wall, Door, Tc];

    private static RaidTarget? ById(int id) =>
        All.FirstOrDefault(t => t.Kind == RaidTargetKind.Item && int.TryParse(t.Key, out var k) && k == id);

    private static RaidMatch Resolve(string q) => RaidLookup.Resolve(q, ById, All);

    [Fact]
    public void NumericInput_ResolvesItemTargetById()
    {
        var found = Assert.IsType<RaidMatch.Found>(Resolve("100"));
        Assert.Equal("Tool Cupboard", found.Target.Name);
    }

    [Fact]
    public void ExactBlockName_Found()
    {
        var found = Assert.IsType<RaidMatch.Found>(Resolve("stone wall"));
        Assert.Equal("Stone Wall", found.Target.Name);
    }

    [Fact]
    public void Substring_Found()
    {
        var found = Assert.IsType<RaidMatch.Found>(Resolve("cupboard"));
        Assert.Equal("Tool Cupboard", found.Target.Name);
    }

    [Fact]
    public void MultipleSubstring_Ambiguous()
    {
        var amb = Assert.IsType<RaidMatch.Ambiguous>(Resolve("o")); // Stone, Door, Tool all contain 'o'
        Assert.True(amb.Candidates.Count >= 2);
    }

    [Fact]
    public void NoMatch_NotFound() => Assert.IsType<RaidMatch.NotFound>(Resolve("zzzzz"));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_NotFound(string q) => Assert.IsType<RaidMatch.NotFound>(Resolve(q));
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests --filter RaidLookupTests`
Expected: FAIL — `RaidLookup` / `RaidMatch` do not exist.

- [ ] **Step 3: Create the generic matcher**

Create `src/RustPlusBot.Features.ItemData/Lookup/NameMatcher.cs`:

```csharp
using System.Globalization;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The kind of outcome produced by <see cref="NameMatcher"/>.</summary>
internal enum NameMatchKind
{
    /// <summary>No candidate matched.</summary>
    NotFound,

    /// <summary>Exactly one candidate resolved.</summary>
    Found,

    /// <summary>Several candidates matched; present for disambiguation.</summary>
    Ambiguous,
}

/// <summary>Pure, generic name-or-id resolution shared by the item and raid-target lookups.
/// Exact name beats substring; an exact-name collision is ranked by <c>exactTieBreak</c>.</summary>
internal static class NameMatcher
{
    public static (NameMatchKind Kind, T? Single, IReadOnlyList<T> Candidates) Resolve<T>(
        string? query,
        Func<int, T?> byId,
        IReadOnlyList<T> all,
        Func<T, string> name,
        IComparer<T> exactTieBreak,
        int cap)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(byId);
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(exactTieBreak);

        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return (NameMatchKind.NotFound, null, []);
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && byId(id) is { } byIdHit)
        {
            return (NameMatchKind.Found, byIdHit, []);
        }

        var exact = all
            .Where(i => string.Equals(name(i), trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i, exactTieBreak)
            .ToList();
        if (exact.Count == 1)
        {
            return (NameMatchKind.Found, exact[0], []);
        }

        if (exact.Count > 1)
        {
            return (NameMatchKind.Ambiguous, null, [.. exact.Take(cap)]);
        }

        var matches = all
            .Where(i => name(i).Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => name(i).StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .ThenBy(i => name(i).Length)
            .ThenBy(i => name(i), StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count switch
        {
            0 => (NameMatchKind.NotFound, null, []),
            1 => (NameMatchKind.Found, matches[0], []),
            _ => (NameMatchKind.Ambiguous, null, [.. matches.Take(cap)]),
        };
    }
}
```

- [ ] **Step 4: Refactor `ItemLookup` to delegate (behavior preserved)**

Replace the body of `src/RustPlusBot.Features.ItemData/Lookup/ItemLookup.cs` with:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name-or-id resolution. Exact name beats substring; ambiguous results are ranked and capped.</summary>
public static class ItemLookup
{
    private static readonly IComparer<ItemRecord> ById =
        Comparer<ItemRecord>.Create((a, b) => a.Id.CompareTo(b.Id));

    /// <summary>Resolves a user query to an item.</summary>
    /// <param name="query">The raw user input (name or id).</param>
    /// <param name="byId">Looks up an item by id.</param>
    /// <param name="all">All known items, for name matching.</param>
    /// <param name="cap">The maximum number of ambiguous candidates to return.</param>
    /// <returns>A <see cref="ItemMatch"/> describing the outcome.</returns>
    public static ItemMatch Resolve(string query,
        Func<int, ItemRecord?> byId,
        IReadOnlyList<ItemRecord> all,
        int cap = 10)
    {
        var (kind, single, candidates) = NameMatcher.Resolve(query, byId, all, i => i.Name, ById, cap);
        return kind switch
        {
            NameMatchKind.Found => new ItemMatch.Found(single!),
            NameMatchKind.Ambiguous => new ItemMatch.Ambiguous(candidates),
            _ => new ItemMatch.NotFound(),
        };
    }
}
```

- [ ] **Step 5: Create `RaidMatch` and `RaidLookup`**

Create `src/RustPlusBot.Features.ItemData/Lookup/RaidMatch.cs`:

```csharp
using System.Diagnostics.CodeAnalysis;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The outcome of resolving a user query to a raid target.</summary>
[SuppressMessage("Design", "CA1034:Nested types should not be visible",
    Justification = "Discriminated-union pattern: nested sealed records are the intended public surface.")]
public abstract record RaidMatch
{
    private RaidMatch() { }

    /// <summary>Exactly one raid target resolved.</summary>
    /// <param name="Target">The resolved target.</param>
    public sealed record Found(RaidTarget Target) : RaidMatch;

    /// <summary>Several targets matched; present candidates for disambiguation.</summary>
    /// <param name="Candidates">The candidate targets, capped and ranked.</param>
    public sealed record Ambiguous(IReadOnlyList<RaidTarget> Candidates) : RaidMatch;

    /// <summary>No target matched.</summary>
    public sealed record NotFound : RaidMatch;
}
```

Create `src/RustPlusBot.Features.ItemData/Lookup/RaidLookup.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name-or-id resolution over raid targets. Exact name beats substring.</summary>
public static class RaidLookup
{
    private static readonly IComparer<RaidTarget> ByName =
        Comparer<RaidTarget>.Create((a, b) => string.CompareOrdinal(a.Name, b.Name));

    /// <summary>Resolves a user query to a raid target.</summary>
    /// <param name="query">The raw user input (target name or item id).</param>
    /// <param name="byId">Looks up an item-kind target by id.</param>
    /// <param name="all">All raid targets, for name matching.</param>
    /// <param name="cap">The maximum number of ambiguous candidates to return.</param>
    /// <returns>A <see cref="RaidMatch"/> describing the outcome.</returns>
    public static RaidMatch Resolve(string query,
        Func<int, RaidTarget?> byId,
        IReadOnlyList<RaidTarget> all,
        int cap = 10)
    {
        var (kind, single, candidates) = NameMatcher.Resolve(query, byId, all, t => t.Name, ByName, cap);
        return kind switch
        {
            NameMatchKind.Found => new RaidMatch.Found(single!),
            NameMatchKind.Ambiguous => new RaidMatch.Ambiguous(candidates),
            _ => new RaidMatch.NotFound(),
        };
    }
}
```

- [ ] **Step 6: Add `ResolveRaidTarget` to the interface**

In `src/RustPlusBot.Features.ItemData/IItemDatabase.cs`, add after the `Resolve` method:

```csharp
    /// <summary>Resolves a user query (target name or id) to a raid target.</summary>
    /// <param name="query">The raw user input.</param>
    /// <returns>A match describing the outcome.</returns>
    RaidMatch ResolveRaidTarget(string query);
```

- [ ] **Step 7: Implement it on `EmbeddedItemDatabase`**

In `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`, add `using System.Globalization;` at the top. Replace the `Raid` field added in Task 1 with the field plus an id index, and add the method next to `Resolve`:

```csharp
    private static readonly IReadOnlyList<RaidTarget> Raid = Dataset.RaidTargets ?? [];

    private static readonly FrozenDictionary<int, RaidTarget> RaidById = IndexRaidById(Raid);
```

```csharp
    /// <inheritdoc />
    public RaidMatch ResolveRaidTarget(string query) => RaidLookup.Resolve(query, RaidById.GetValueOrDefault, Raid);
```

And add the index helper next to `IndexById`:

```csharp
    /// <summary>Builds a frozen id→target index from the item-kind raid targets.</summary>
    /// <param name="targets">All raid targets.</param>
    /// <returns>A frozen dictionary keyed by item id (item-kind targets only).</returns>
    internal static FrozenDictionary<int, RaidTarget> IndexRaidById(IReadOnlyList<RaidTarget> targets) =>
        targets.Where(t => t.Kind == RaidTargetKind.Item)
            .GroupBy(t => int.Parse(t.Key, CultureInfo.InvariantCulture))
            .ToFrozenDictionary(g => g.Key, g => g.Last());
```

- [ ] **Step 8: Run the lookup + database tests**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests`
Expected: PASS — `RaidLookupTests` green AND the existing `ItemLookupTests` (including `ExactNameCollision_ReturnsAmbiguousOrderedById`) still green, proving the refactor preserved behavior.

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Features.ItemData/Lookup/NameMatcher.cs \
  src/RustPlusBot.Features.ItemData/Lookup/RaidMatch.cs \
  src/RustPlusBot.Features.ItemData/Lookup/RaidLookup.cs \
  src/RustPlusBot.Features.ItemData/Lookup/ItemLookup.cs \
  src/RustPlusBot.Features.ItemData/IItemDatabase.cs \
  src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs \
  tests/RustPlusBot.Features.ItemData.Tests/RaidLookupTests.cs
git commit -m "feat(6c): generalize name matcher + RaidLookup/ResolveRaidTarget"
```

---

### Task 4: Validator raid checks + wire generator + regenerate bundle (schema v3)

Atomically bumps the schema to 3: the validator gains raid checks, the generator wires the durability source and emits real `RaidTargets` at version 3, the runtime loader expects 3, and the bundle is regenerated.

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`
- Modify: `tools/RustPlusBot.ItemData.Generator/Program.cs`
- Modify: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs:12`
- Regenerate: `src/RustPlusBot.Features.ItemData/Data/item-data.json`
- Test: `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`
- Test: `tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs`

**Interfaces:**

- Consumes: `IDurabilitySource`/`OfflineDurabilitySource` (Task 2); `ResolveRaidTarget` (Task 3).
- Produces: `ValidationOptions(int MinItemCount, int MinRaidTargetCount = 0)`; a v3 bundle with `raidTargets`.

- [ ] **Step 1: Write the failing validator tests**

In `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`, add a helper and three tests. Add this helper at the top of the class:

```csharp
    private static ItemDataset WithRaid(params RaidTarget[] raid) =>
        new(3, Good().Sources, Good().Items, raid);
```

```csharp
    [Fact]
    public void RaidCost_UnknownToolId_isError()
    {
        var bad = WithRaid(new RaidTarget("Stone Wall", "Stone Wall", RaidTargetKind.BuildingBlock,
            [new RaidCost(424242, null, null, 2, 11.5, 4400, 120)]));
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("424242", StringComparison.Ordinal));
    }

    [Fact]
    public void RaidCost_NonPositiveQuantity_isError()
    {
        var bad = WithRaid(new RaidTarget("Stone Wall", "Stone Wall", RaidTargetKind.BuildingBlock,
            [new RaidCost(1, null, null, 0, 1, 1, null)]));
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("quantity", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TooFewRaidTargets_isError()
    {
        var errors = DatasetValidator.Validate(Good(), new ValidationOptions(MinItemCount: 1, MinRaidTargetCount: 300));
        Assert.Contains(errors, e => e.Contains("raid target count", StringComparison.OrdinalIgnoreCase));
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests --filter DatasetValidatorTests`
Expected: FAIL — `ValidationOptions` has no `MinRaidTargetCount`; raid checks absent.

- [ ] **Step 3: Add the validator raid checks**

In `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`, change `ValidationOptions`:

```csharp
/// <summary>Options that control dataset validation thresholds.</summary>
/// <param name="MinItemCount">The minimum number of items the dataset must contain.</param>
/// <param name="MinRaidTargetCount">The minimum number of raid targets the dataset must contain.</param>
internal sealed record ValidationOptions(int MinItemCount, int MinRaidTargetCount = 0);
```

In `Validate`, before `return errors;`, add:

```csharp
        var raid = dataset.RaidTargets ?? [];
        if (raid.Count < options.MinRaidTargetCount)
        {
            errors.Add($"raid target count {raid.Count} below minimum {options.MinRaidTargetCount}");
        }

        foreach (var target in raid)
        {
            foreach (var cost in target.Costs)
            {
                if (!ids.Contains(cost.ToolId))
                {
                    errors.Add($"raid target {target.Name}: cost references unknown tool id {cost.ToolId}");
                }

                if (cost.Quantity <= 0)
                {
                    errors.Add($"raid target {target.Name}: non-positive quantity {cost.Quantity}");
                }
            }
        }
```

- [ ] **Step 4: Run validator tests to verify green**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests --filter DatasetValidatorTests`
Expected: PASS.

- [ ] **Step 5: Wire the durability source into the generator (emit v3)**

In `tools/RustPlusBot.ItemData.Generator/Program.cs`, after the `rustLabsSource` declaration (line 67), add:

```csharp
        var durabilitySource = new OfflineDurabilitySource(
            Path.Combine(rustplusDir, "rustlabsDurabilityData.json"));
```

After `var upkeepCosts = rustLabsSource.LoadUpkeep();` (line 78), add:

```csharp
        var raidTargets = durabilitySource.LoadRaidTargets(names);
        Console.WriteLine($"Loaded {raidTargets.Count} raid targets");
```

Change the dataset construction (the lines set in Task 1) to emit version `3` and the real raid list:

```csharp
        var dataset = new ItemDataset(
            3,
            new DatasetSources(NamesAsOf, RecycleAsOf, CraftAsOf, ResearchAsOf, DecayAsOf, UpkeepAsOf, DurabilityAsOf),
            items,
            raidTargets);
```

Change the validation options to enforce the raid floor:

```csharp
        var validationOptions = new ValidationOptions(MinItemCount: minItems, MinRaidTargetCount: 300);
```

- [ ] **Step 6: Bump the runtime loader's expected schema version**

In `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs:12`, change:

```csharp
    private const int ExpectedSchemaVersion = 3;
```

- [ ] **Step 7: Regenerate the bundle**

Run:

```bash
dotnet run --project tools/RustPlusBot.ItemData.Generator -- \
  --out src/RustPlusBot.Features.ItemData/Data/item-data.json \
  --rustplusplus ~/Dev/rustplusplus/src/staticFiles \
  --min-items 1000
```

Expected: exit 0, prints `Loaded <N> raid targets` (N ≈ 416) and `Emitted <M> items`. Confirm the bundle is v3 and carries raid targets:

```bash
grep -c '"schemaVersion": 3' src/RustPlusBot.Features.ItemData/Data/item-data.json
grep -c '"raidTargets"' src/RustPlusBot.Features.ItemData/Data/item-data.json
```

Expected: both print `1`.

- [ ] **Step 8: Write the bundle smoke tests**

In `tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs`, add `using RustPlusBot.Features.ItemData.Lookup;` and these tests:

```csharp
    [Fact]
    public void ResolveRaidTarget_StoneWall_ListsTimedExplosiveCharge()
    {
        var found = Assert.IsType<RaidMatch.Found>(_db.ResolveRaidTarget("Stone Wall"));
        Assert.Equal("Stone Wall", found.Target.Name);
        Assert.Contains(found.Target.Costs, c => c.ToolId == 1248356124); // Timed Explosive Charge (C4)
    }

    [Fact]
    public void Sources_DurabilityAsOf_IsPopulated()
    {
        Assert.Equal(new DateOnly(2024, 9, 7), _db.Sources.DurabilityAsOf);
    }
```

- [ ] **Step 9: Run the full ItemData + Generator suites**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests tests/RustPlusBot.ItemData.Generator.Tests`
Expected: PASS (raid smoke + `DurabilityAsOf` populated; existing v2-version `Parse_*` tests still pass since they pass an explicit `expectedSchemaVersion`).

- [ ] **Step 10: Commit**

```bash
git add tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs \
  tools/RustPlusBot.ItemData.Generator/Program.cs \
  src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs \
  src/RustPlusBot.Features.ItemData/Data/item-data.json \
  tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs \
  tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs
git commit -m "feat(6c): validator raid checks + regenerate bundle (schema v3, ~416 raid targets)"
```

---

### Task 5: Formatter — `DurabilityLine`

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Formatting/DurabilityLine.cs`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Formatting/DurabilityFormatterTests.cs`

**Interfaces:**

- Consumes: `RaidTarget`/`RaidCost` (Task 1); `IItemNameResolver.Resolve(int) → string`; `DurationFormat.Compact(TimeSpan) → string` (existing in `Features.Commands/Formatting`).
- Produces: `DurabilityLine.Format(RaidTarget target, IItemNameResolver names) → string`.

- [ ] **Step 1: Write the failing tests**

Create `tests/RustPlusBot.Features.Commands.Tests/Formatting/DurabilityFormatterTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class DurabilityFormatterTests
{
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void DurabilityLine_SortsBySulfurAscending_AndAnnotatesSide()
    {
        var target = new RaidTarget("Stone Wall", "Stone Wall", RaidTargetKind.BuildingBlock,
        [
            new RaidCost(1, "soft", null, 4, 18, 5600, 120),
            new RaidCost(2, null, null, 2, 11.5, 4400, 120),
        ]);

        var line = DurabilityLine.Format(target, _names);

        Assert.Contains("Stone Wall", line, StringComparison.Ordinal);
        Assert.True(
            line.IndexOf("4400", StringComparison.Ordinal) < line.IndexOf("5600", StringComparison.Ordinal),
            "cheaper (4400 sulfur) cost should sort before 5600");
        Assert.Contains("(soft)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void DurabilityLine_NullSulfur_SortsLast_AndOmitsSulfur()
    {
        var target = new RaidTarget("X", "X", RaidTargetKind.Item,
        [
            new RaidCost(1, null, null, 1, null, null, 50),  // no sulfur → sorts last
            new RaidCost(2, null, null, 1, 10, 1400, 30),
        ]);

        var line = DurabilityLine.Format(target, _names);

        // tool id 2 (1400 sulfur) appears before tool id 1 (no sulfur)
        Assert.True(line.IndexOf("Item 2", StringComparison.Ordinal) < line.IndexOf("Item 1", StringComparison.Ordinal));
    }

    [Fact]
    public void DurabilityLine_RoundsQuantityUp_AndShowsCaption()
    {
        var target = new RaidTarget("X", "X", RaidTargetKind.Item,
            [new RaidCost(1, null, "Semi-Automatic Rifle", 172.5, 70, 4325, null)]);

        var line = DurabilityLine.Format(target, _names);

        Assert.Contains("×173", line, StringComparison.Ordinal); // 172.5 rounded up
        Assert.Contains("Semi-Automatic Rifle", line, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter DurabilityFormatterTests`
Expected: FAIL — `DurabilityLine` does not exist.

- [ ] **Step 3: Create the formatter**

Create `src/RustPlusBot.Features.Commands/Formatting/DurabilityLine.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the multi-line durability (raid-cost) reply for a target.</summary>
internal static class DurabilityLine
{
    /// <summary>Lists each explosive cost for a target, cheapest by sulfur first.</summary>
    /// <param name="target">The raid target (with at least one cost).</param>
    /// <param name="names">Resolves tool item ids to names.</param>
    public static string Format(RaidTarget target, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(names);

        var lines = target.Costs
            .OrderBy(c => c.Sulfur ?? int.MaxValue)
            .ThenBy(c => c.Quantity)
            .Select(c => FormatCost(c, names));
        return string.Create(CultureInfo.InvariantCulture, $"{target.Name}:\n{string.Join("\n", lines)}");
    }

    private static string FormatCost(RaidCost cost, IItemNameResolver names)
    {
        var quantity = (int)Math.Ceiling(cost.Quantity);
        var tool = names.Resolve(cost.ToolId);
        var side = cost.Side is "soft" or "hard"
            ? string.Create(CultureInfo.InvariantCulture, $" ({cost.Side})")
            : string.Empty;
        var sulfur = cost.Sulfur is { } s
            ? string.Create(CultureInfo.InvariantCulture, $" — {s} sulfur")
            : string.Empty;
        var time = cost.TimeSeconds is { } t and > 0
            ? string.Create(CultureInfo.InvariantCulture, $" ({DurationFormat.Compact(TimeSpan.FromSeconds(t))})")
            : string.Empty;
        var caption = string.IsNullOrEmpty(cost.Caption)
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $" · {cost.Caption}");
        return string.Create(CultureInfo.InvariantCulture, $"{tool} ×{quantity}{side}{sulfur}{time}{caption}");
    }
}
```

- [ ] **Step 4: Run to verify green**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter DurabilityFormatterTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Formatting/DurabilityLine.cs \
  tests/RustPlusBot.Features.Commands.Tests/Formatting/DurabilityFormatterTests.cs
git commit -m "feat(6c): DurabilityLine formatter (sulfur-sorted, side + caption)"
```

---

### Task 6: In-game `!durability` handler + registration + InGame help + localization

Adds the handler and everything that must change in lockstep to keep the registration-count and help-drift tests green: DI registration, the InGame help-catalog row, and the `command.durability.ok` / `help.durability` strings.

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Handlers/DurabilityCommandHandler.cs`
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs:53`
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs:33`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Handlers/DurabilityHandlerTests.cs`
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs:54,73`
- Modify: `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs:9-14`

**Interfaces:**

- Consumes: `IItemDatabase.ResolveRaidTarget` (Task 3); `DurabilityLine.Format` (Task 5); `RaidMatch` (Task 3); `ILocalizer`, `IItemNameResolver`, `ICommandHandler`, `CommandContext`.
- Produces: `DurabilityCommandHandler` with `Name => "durability"`.

- [ ] **Step 1: Add the localization strings**

In `src/RustPlusBot.Localization/Strings.resx`, add (keep alphabetical neighbours — `command.durability.ok` goes just after `command.decay.ok`, `help.durability` just after `help.decay`):

```xml
  <data name="command.durability.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
```

```xml
  <data name="help.durability" xml:space="preserve">
    <value>Explosives needed to destroy a target</value>
  </data>
```

In `src/RustPlusBot.Localization/Strings.fr.resx`, add the matching keys:

```xml
  <data name="command.durability.ok" xml:space="preserve">
    <value>{0}</value>
  </data>
```

```xml
  <data name="help.durability" xml:space="preserve">
    <value>Explosifs nécessaires pour détruire une cible</value>
  </data>
```

- [ ] **Step 2: Write the failing handler tests**

Create `tests/RustPlusBot.Features.Commands.Tests/Handlers/DurabilityHandlerTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class DurabilityHandlerTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly ILocalizer _loc = new ResxLocalizer();
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Name_is_durability()
        => Assert.Equal("durability", new DurabilityCommandHandler(_db, _names, _loc).Name);

    [Fact]
    public async Task Found_returnsRaidCosts()
    {
        var reply = await new DurabilityCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("Stone Wall"), CancellationToken.None);
        Assert.Contains("Stone Wall", reply, StringComparison.Ordinal);
        Assert.Contains("sulfur", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NotFound_returnsMessage()
    {
        var reply = await new DurabilityCommandHandler(_db, _names, _loc)
            .ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter DurabilityHandlerTests`
Expected: FAIL — `DurabilityCommandHandler` does not exist.

- [ ] **Step 4: Create the handler**

Create `src/RustPlusBot.Features.Commands/Handlers/DurabilityCommandHandler.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!durability — lists the explosives needed to destroy a target.</summary>
/// <param name="database">The item database.</param>
/// <param name="names">Resolves tool ids to names.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class DurabilityCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "durability";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.ResolveRaidTarget(query) switch
        {
            RaidMatch.Found f =>
                localizer.Get("command.durability.ok", context.Culture, DurabilityLine.Format(f.Target, names)),
            RaidMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
```

- [ ] **Step 5: Register the handler**

In `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`, after line 53 (`UpkeepCommandHandler`), add:

```csharp
        services.AddScoped<ICommandHandler, DurabilityCommandHandler>();
```

- [ ] **Step 6: Add the InGame help-catalog row**

In `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`, in the `InGame` list after the `upkeep` entry (line 33), add:

```csharp
        new("durability", CommandGroup.ItemDb, "help.durability"),
```

- [ ] **Step 7: Update the count + drift tests**

In `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs`, change line 54 from `Assert.Equal(25, handlers.Count);` to:

```csharp
        Assert.Equal(26, handlers.Count);
```

and add after the `upkeep` assertion (line 73):

```csharp
        Assert.Contains(handlers, h => h.Name == "durability");
```

In `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs`, add `"durability"` to the `HandlerNames` array (after `"upkeep"`):

```csharp
        "item", "recycle", "craft", "research", "decay", "upkeep", "durability",
```

- [ ] **Step 8: Run the Commands suite**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS — `DurabilityHandlerTests`, `CommandRegistrationTests` (26 handlers), and `CommandHelpCatalogTests` (every handler has a catalog entry; every description key resolves EN+FR) all green.

- [ ] **Step 9: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Handlers/DurabilityCommandHandler.cs \
  src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs \
  src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs \
  src/RustPlusBot.Localization/Strings.resx src/RustPlusBot.Localization/Strings.fr.resx \
  tests/RustPlusBot.Features.Commands.Tests/Handlers/DurabilityHandlerTests.cs \
  tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs \
  tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs
git commit -m "feat(6c): in-game !durability handler + registration + InGame help"
```

---

### Task 7: Slash `/durability` + Slash help + localization

The slash module mirrors `RespondForAsync` with a raid-specific helper. There is no unit test for the module (consistent with the codebase — `ItemCommandModule` has no existing module test; it requires a live Discord interaction context). Coverage comes from the handler, formatter, and lookup tests; the slash path reuses the same `ResolveRaidTarget` + `DurabilityLine`. This is a deliberate, stated gap, not an omission.

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs:47`
- Modify: `src/RustPlusBot.Localization/Strings.resx`, `Strings.fr.resx`
- Test: `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs` (drift only — already covers Slash)

**Interfaces:**

- Consumes: `IItemDatabase.ResolveRaidTarget`, `RaidMatch`, `DurabilityLine.Format`, `IItemNameResolver`, `ILocalizer`, `IWorkspaceStore`, `DatasetSources.DurabilityAsOf`.

- [ ] **Step 1: Add the slash localization strings**

In `src/RustPlusBot.Localization/Strings.resx`, after `help.slash.decay`, add:

```xml
  <data name="help.slash.durability" xml:space="preserve">
    <value>Show the explosives needed to destroy a target.</value>
  </data>
```

In `src/RustPlusBot.Localization/Strings.fr.resx`, add:

```xml
  <data name="help.slash.durability" xml:space="preserve">
    <value>Affiche les explosifs nécessaires pour détruire une cible.</value>
  </data>
```

- [ ] **Step 2: Add the Slash help-catalog row**

In `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`, in the `Slash` list after the `upkeep` entry (line 47), add:

```csharp
        new("durability", CommandGroup.ItemDb, "help.slash.durability"),
```

- [ ] **Step 3: Add the slash command + raid responder**

In `src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`, add `using RustPlusBot.Features.ItemData.Lookup;` to the usings. After the `UpkeepAsync` method (line 64), add:

```csharp
    /// <summary>Lists the explosives needed to destroy a target.</summary>
    /// <param name="target">The item, building block, or vehicle name.</param>
    [SlashCommand("durability", "Show the explosives needed to destroy a target")]
    public Task DurabilityAsync([Summary("target", "Item, wall/door, or vehicle name")] string target) =>
        RespondForRaidAsync(target);
```

After the `RespondForAsync` helper (before the closing brace of the class), add the raid-specific responder:

```csharp
    private async Task RespondForRaidAsync(string query)
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

            var text = db.ResolveRaidTarget(query) switch
            {
                RaidMatch.Found f => loc.Get("command.durability.ok", culture, DurabilityLine.Format(f.Target, names)),
                RaidMatch.Ambiguous a => loc.Get("command.item.ambiguous", culture,
                    string.Join(", ", a.Candidates.Select(c => c.Name))),
                _ => loc.Get("command.item.notfound", culture, query),
            };

            var embed = new EmbedBuilder()
                .WithDescription(text)
                .WithFooter($"data as of {db.Sources.DurabilityAsOf:yyyy-MM-dd}")
                .Build();
            await RespondAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
        }
    }
```

- [ ] **Step 4: Build + run the Commands suite**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS — `CommandHelpCatalogTests.EveryDescriptionKeyResolvesInEnglishAndFrench` now also covers the new Slash `help.slash.durability` key.

- [ ] **Step 5: Commit**

```bash
git add src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs \
  src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs \
  src/RustPlusBot.Localization/Strings.resx src/RustPlusBot.Localization/Strings.fr.resx
git commit -m "feat(6c): /durability slash command + Slash help"
```

---

### Task 8: README + final gates

**Files:**

- Modify: `tools/RustPlusBot.ItemData.Generator/README.md`

- [ ] **Step 1: Update the generator README**

In `tools/RustPlusBot.ItemData.Generator/README.md`:

Add `/durability` to the calculator list in the opening sentence (line 5). Add a row to the source-files table (after the upkeep row):

```markdown
   | `rustlabsDurabilityData.json` | raid cost (explosives only — trimmed) |
```

Extend the "What it does" validation description (item 3) to mention raid targets, and add `DurabilityAsOf` to the provenance section. In the schema/projection description, note that durability is trimmed to the `explosive` tool group and projected into `RaidTargets` (item/building-block/vehicle), never bundled raw (the source is ~19 MB).

- [ ] **Step 2: Commit the README**

```bash
git add tools/RustPlusBot.ItemData.Generator/README.md
git commit -m "docs(6c): document durability source + raid projection in generator README"
```

- [ ] **Step 3: Run the formatter gate**

Run: `dotnet jb cleanupcode --profile=ReformatAndReorder RustPlusBot.slnx`
Then: `git status --porcelain`
Expected: empty output (no diff). If cleanupcode changed anything, review and commit:

```bash
git add -A && git commit -m "style(6c): apply cleanupcode"
```

- [ ] **Step 4: Run the full solution test suite**

Run: `dotnet test RustPlusBot.slnx`
Expected: PASS — all projects green.

- [ ] **Step 5: Final verification of the bundle**

Run:

```bash
grep -c '"schemaVersion": 3' src/RustPlusBot.Features.ItemData/Data/item-data.json
git status --porcelain
```

Expected: `1`, and a clean working tree (the regenerated bundle was committed in Task 4).

---

## Self-Review

**Spec coverage** (each spec section → task):

- §2 In scope: `RaidTarget`/`RaidCost`/`RaidTargetKind` + `RaidTargets` + `DurabilityAsOf` + schema bump → Tasks 1, 4. ✓
- §3.1 Schema (drop `quantityTypeId`/HP; `double Quantity`; `Side` nullable) → Task 1. ✓
- §3.2 Source data (3 sections, item-id vs name keys, explosive filter) → Task 2. ✓
- §4.1 In-game `!durability` (3-arg ctor with `IItemNameResolver`) → Task 6. ✓
- §4.2 Slash `/durability` + `RespondForRaidAsync` → Task 7. ✓
- §4.3 `DurabilityLine` (sulfur-sorted, side, caption, quantity round-up) → Task 5. ✓
- §4.4 `/help` rows (InGame + Slash) + drift guard → Tasks 6, 7. ✓
- §5 `ResolveRaidTarget` + generalized matcher → Task 3. ✓
- §6 Generator source + validator (floor + tool-id + quantity) + regen + READMEs → Tasks 2, 4, 8. ✓
- §7 Error handling (null-guards, no found-but-empty, never-clobber) → Tasks 2, 4, 5, 6. ✓
- §8 Testing (generator/lookup/formatter/handler/bundle) → Tasks 2–6. ✓
- §9 Integration touchpoints (loader round-trip, DI scope, registration, help drift, resx, README, `DatasetSources` arity) → Tasks 1, 4, 6, 7, 8. ✓
- §10 Gates → Task 8. ✓
- §11 Decisions (durability only; Approach A; explosive group; `/durability` name; all rows; drop `quantityTypeId`/HP) → reflected throughout. ✓
- Deferred (smelting, non-explosive groups, cctv, live-scrape) → not in plan. ✓

**Placeholder scan:** No TBD/TODO; every code step shows full code; every command shows expected output. The Task 7 "no module test" is an explicit, justified gap (matches the existing codebase, which has no `ItemCommandModule` test), not a silent omission. ✓

**Type consistency:** `RaidTarget(string Key, string Name, RaidTargetKind Kind, IReadOnlyList<RaidCost> Costs)`, `RaidCost(int ToolId, string? Side, string? Caption, double Quantity, double? TimeSeconds, int? Sulfur, int? Fuel)`, `DatasetSources(...7 dates incl. DurabilityAsOf)`, `ItemDataset(int, DatasetSources, IReadOnlyList<ItemRecord>, IReadOnlyList<RaidTarget>)`, `IDurabilitySource.LoadRaidTargets(IReadOnlyDictionary<int,string>)`, `RaidLookup.Resolve(string, Func<int,RaidTarget?>, IReadOnlyList<RaidTarget>, int)`, `RaidMatch.Found/Ambiguous/NotFound`, `IItemDatabase.ResolveRaidTarget(string)`, `DurabilityLine.Format(RaidTarget, IItemNameResolver)`, `DurabilityCommandHandler(IItemDatabase, IItemNameResolver, ILocalizer)`, `ValidationOptions(int MinItemCount, int MinRaidTargetCount = 0)` — used consistently across tasks. ✓
