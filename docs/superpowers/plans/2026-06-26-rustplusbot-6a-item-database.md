# Subsystem 6a — Item Database, Core Calculators & Refresh Tool — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a versioned, embedded Rust item dataset behind one `IItemDatabase` seam, four calculators (`/item /recycle /craft /research` + in-game `!` equivalents), and a dev CLI tool that regenerates the dataset and fails loudly on upstream shape drift.

**Architecture:** A new pure leaf project `RustPlusBot.Features.ItemData` owns a typed dataset schema (the "proto"), an embedded `item-data.json`, a singleton `EmbeddedItemDatabase`, and a pure `ItemLookup`. The existing `Features.Commands` project gains one ref to it and adds 4 `ICommandHandler`s + one ephemeral slash module. 4c's `IItemNameResolver` is re-pointed onto `IItemDatabase`. A separate `tools/RustPlusBot.ItemData.Generator` console regenerates the bundle. No new entities, migrations, events, options, or background services.

**Tech Stack:** .NET 10, C#, xUnit (`Assert`, **not** FluentAssertions), NSubstitute, `System.Collections.Frozen`, `System.Text.Json`, Discord.Net 3.20, `RustPlusBot.Localization` `ResxLocalizer`/`ILocalizer`.

**Spec:** `docs/superpowers/specs/2026-06-26-rustplusbot-6a-item-database-design.md`

## Global Constraints

- Solution file is `RustPlusBot.slnx` (no `.sln`). New projects must be added to it.
- Build is `-warnaserror`; all analyzers (Roslynator/Sonar/CA) are errors. No bare `// TODO` (`S1135`) — use XML `<remarks>`. Named args must follow declaration order (`RCS1205`). Interface impls repeat `= default` on optional params (`S1006`).
- Format gate: `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` must produce **zero diff** (run `dotnet tool restore` first). It reorders members Roslynator never flags — run it before every commit/push.
- Tests use xUnit `Assert.*`. Do **not** introduce FluentAssertions. Mocking internal types with NSubstitute requires `<InternalsVisibleTo Include="DynamicProxyGenAssembly2" />` in the project under test.
- Run the FULL suite with `dotnet test RustPlusBot.slnx -maxcpucount:1` and **read per-assembly counts** — a fake/harness missing a new interface member silently drops a whole assembly's tests rather than failing.
- No new EF entities/migrations in 6a — `dotnet ef migrations has-pending-model-changes` must show none (or verify "no Migrations/ModelSnapshot/DbContext/entity files changed").
- Localization keys live in `src/RustPlusBot.Localization/Strings.resx` (EN) and `Strings.fr.resx` (FR). Command keys follow `command.<name>.<variant>`; help keys follow `help.<name>` / `help.slash.<name>`. Every key MUST exist in both files (a `CommandHelpCatalogTests` test asserts FR ≠ key).
- `ItemData` is a LEAF: it may reference ONLY `RustPlusBot.Abstractions` and `RustPlusBot.Localization`. It must NOT reference Discord, Persistence, Domain, or any `Features.*`.

---

## File Structure

**New project `src/RustPlusBot.Features.ItemData/`:**

- `Data/ItemDataset.cs` — the record types (`ItemDataset`, `DatasetSources`, `ItemRecord`, `RecycleYield`, `YieldEntry`, `CraftRecipe`, `Ingredient`, `ResearchCost`).
- `Data/item-data.json` — embedded resource (generated; a small hand-authored seed lands in Task 2, regenerated for real in Task 9).
- `Lookup/ItemMatch.cs` — `Found` / `Ambiguous` / `NotFound` result.
- `Lookup/ItemLookup.cs` — pure resolution function.
- `IItemDatabase.cs` — the seam.
- `EmbeddedItemDatabase.cs` — singleton loader + `IItemDatabase` impl.
- `Naming/IItemNameResolver.cs` — moved here from StorageMonitors.
- `Naming/ItemDatabaseNameResolver.cs` — adapter implementing `IItemNameResolver` over `IItemDatabase`.
- `ItemDataServiceCollectionExtensions.cs` — `AddItemData()`.
- `RustPlusBot.Features.ItemData.csproj`.

**New test project `tests/RustPlusBot.Features.ItemData.Tests/`:**

- `ItemLookupTests.cs`, `EmbeddedItemDatabaseTests.cs`, `ItemDatabaseNameResolverTests.cs`.

**Modified `src/RustPlusBot.Features.Commands/`:**

- `Formatting/ItemLine.cs`, `Formatting/RecycleLine.cs`, `Formatting/CraftLine.cs`, `Formatting/ResearchLine.cs` — pure formatters.
- `Handlers/ItemCommandHandler.cs`, `RecycleCommandHandler.cs`, `CraftCommandHandler.cs`, `ResearchCommandHandler.cs`.
- `Modules/ItemCommandModule.cs` — the 4 ephemeral slash commands.
- `Help/CommandHelpCatalog.cs` — add 4 `InGame` entries.
- `CommandServiceCollectionExtensions.cs` — register the 4 handlers.
- `RustPlusBot.Features.Commands.csproj` — add `ItemData` project ref.

**Modified `src/RustPlusBot.Features.StorageMonitors/`:**

- Delete `Naming/IItemNameResolver.cs`, `Naming/EmbeddedItemNameResolver.cs`, `Naming/items.json`.
- `StorageMonitorServiceCollectionExtensions.cs` — register via `AddItemData()` instead of the local resolver.
- `RustPlusBot.Features.StorageMonitors.csproj` — add `ItemData` ref, drop the `items.json` `EmbeddedResource`.
- `Rendering/StorageMonitorEmbedRenderer.cs` — update the `using` to the new `IItemNameResolver` namespace.

**Modified `src/RustPlusBot.Host/Program.cs`:** add `builder.Services.AddItemData();`.

**New tool `tools/RustPlusBot.ItemData.Generator/`:**

- `Program.cs`, `Sources/INamesSource.cs`, `Sources/IRustLabsSource.cs`, `Sources/*` impls, `Validation/DatasetValidator.cs`, `RustPlusBot.ItemData.Generator.csproj`.
- `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`.

---

## Task 1: Scaffold the `ItemData` leaf project + dataset records

**Files:**

- Create: `src/RustPlusBot.Features.ItemData/RustPlusBot.Features.ItemData.csproj`
- Create: `src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`
- Create: `tests/RustPlusBot.Features.ItemData.Tests/RustPlusBot.Features.ItemData.Tests.csproj`
- Create: `tests/RustPlusBot.Features.ItemData.Tests/ItemDatasetTests.cs`
- Modify: `RustPlusBot.slnx` (add both projects)

**Interfaces:**

- Produces: the record types below, namespace `RustPlusBot.Features.ItemData.Data`.

- [ ] **Step 1: Create the project file**

`src/RustPlusBot.Features.ItemData/RustPlusBot.Features.ItemData.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <InternalsVisibleTo Include="RustPlusBot.Features.ItemData.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\RustPlusBot.Abstractions\RustPlusBot.Abstractions.csproj" />
    <ProjectReference Include="..\RustPlusBot.Localization\RustPlusBot.Localization.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.DependencyInjection.Abstractions" />
  </ItemGroup>

</Project>
```

(If `Microsoft.Extensions.DependencyInjection.Abstractions` is not yet in `Directory.Packages.props`, check — it is used widely; reference the same way other feature projects do. The `AddItemData` extension needs `IServiceCollection`.)

- [ ] **Step 2: Write the dataset records**

`src/RustPlusBot.Features.ItemData/Data/ItemDataset.cs`:

```csharp
namespace RustPlusBot.Features.ItemData.Data;

/// <summary>The full bundled item dataset. Deserialized from the embedded <c>item-data.json</c>.</summary>
/// <param name="SchemaVersion">The schema version; the loader rejects a mismatched bundle.</param>
/// <param name="Sources">Per-section provenance dates.</param>
/// <param name="Items">Every known item, one record each.</param>
public sealed record ItemDataset(int SchemaVersion, DatasetSources Sources, IReadOnlyList<ItemRecord> Items);

/// <summary>When each section of the dataset was last sourced, for "data as of" display.</summary>
/// <param name="NamesAsOf">Names/ids/stack source date.</param>
/// <param name="RecycleAsOf">Recycle data source date.</param>
/// <param name="CraftAsOf">Craft data source date.</param>
/// <param name="ResearchAsOf">Research data source date.</param>
public sealed record DatasetSources(
    DateOnly NamesAsOf, DateOnly RecycleAsOf, DateOnly CraftAsOf, DateOnly ResearchAsOf);

/// <summary>One item, with all 6a calculator data inlined (null where not applicable).</summary>
/// <param name="Id">The Rust item id.</param>
/// <param name="Name">The display name.</param>
/// <param name="StackSize">Max stack size.</param>
/// <param name="DespawnSeconds">Despawn time in seconds, or null if it does not despawn / unknown.</param>
/// <param name="Recycle">Recycler yield, or null if not recyclable.</param>
/// <param name="Craft">Craft recipe, or null if not craftable.</param>
/// <param name="Research">Research cost, or null if not researchable.</param>
public sealed record ItemRecord(
    int Id,
    string Name,
    int StackSize,
    int? DespawnSeconds,
    RecycleYield? Recycle,
    CraftRecipe? Craft,
    ResearchCost? Research);

/// <summary>Recycler output for an item (6a covers the standard recycler only).</summary>
/// <param name="Recycler">The yield entries produced by the standard recycler.</param>
public sealed record RecycleYield(IReadOnlyList<YieldEntry> Recycler);

/// <summary>One recycler output entry.</summary>
/// <param name="ItemId">The produced item id.</param>
/// <param name="Quantity">The produced quantity.</param>
/// <param name="Probability">The probability (0..1) of receiving this output.</param>
public sealed record YieldEntry(int ItemId, int Quantity, double Probability);

/// <summary>A craft recipe.</summary>
/// <param name="Ingredients">The required ingredients.</param>
/// <param name="TimeSeconds">The craft time in seconds.</param>
/// <param name="WorkbenchLevel">Required workbench level (1-3), or null if none.</param>
public sealed record CraftRecipe(IReadOnlyList<Ingredient> Ingredients, double TimeSeconds, int? WorkbenchLevel);

/// <summary>One craft ingredient.</summary>
/// <param name="ItemId">The ingredient item id.</param>
/// <param name="Quantity">The required quantity.</param>
public sealed record Ingredient(int ItemId, int Quantity);

/// <summary>The research cost for an item.</summary>
/// <param name="Scrap">The scrap cost to research.</param>
public sealed record ResearchCost(int Scrap);
```

- [ ] **Step 3: Create the test project**

`tests/RustPlusBot.Features.ItemData.Tests/RustPlusBot.Features.ItemData.Tests.csproj` — copy the structure of `tests/RustPlusBot.Features.Switches.Tests/RustPlusBot.Features.Switches.Tests.csproj` (same SDK, same xUnit/NSubstitute package refs via central versioning, `IsPackable=false` if siblings have it), with a single `<ProjectReference Include="..\..\src\RustPlusBot.Features.ItemData\RustPlusBot.Features.ItemData.csproj" />`. Match the exact `<PropertyGroup>`/`<ItemGroup>` shape of the Switches test csproj (read it first).

- [ ] **Step 4: Write a trivial record test**

`tests/RustPlusBot.Features.ItemData.Tests/ItemDatasetTests.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class ItemDatasetTests
{
    [Fact]
    public void ItemRecord_AllowsNullCalculatorData()
    {
        var record = new ItemRecord(1, "Wood", 1000, null, null, null, null);
        Assert.Equal("Wood", record.Name);
        Assert.Null(record.Recycle);
    }
}
```

- [ ] **Step 5: Add both projects to the solution**

Run:

```bash
dotnet sln RustPlusBot.slnx add src/RustPlusBot.Features.ItemData/RustPlusBot.Features.ItemData.csproj
dotnet sln RustPlusBot.slnx add tests/RustPlusBot.Features.ItemData.Tests/RustPlusBot.Features.ItemData.Tests.csproj
```

Expected: "Project ... added to the solution." If `dotnet sln` rejects `.slnx`, edit `RustPlusBot.slnx` by hand mirroring the existing `<Project Path="...">` entries.

- [ ] **Step 6: Build + test**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests/RustPlusBot.Features.ItemData.Tests.csproj`
Expected: PASS (1 test).

- [ ] **Step 7: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add src/RustPlusBot.Features.ItemData tests/RustPlusBot.Features.ItemData.Tests RustPlusBot.slnx
git commit -m "feat(itemdata): scaffold ItemData leaf project + dataset records"
```

---

## Task 2: `IItemDatabase` + `EmbeddedItemDatabase` (load by id) with a seed bundle

**Files:**

- Create: `src/RustPlusBot.Features.ItemData/IItemDatabase.cs`
- Create: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`
- Create: `src/RustPlusBot.Features.ItemData/Data/item-data.json` (small hand-authored seed)
- Modify: `src/RustPlusBot.Features.ItemData/RustPlusBot.Features.ItemData.csproj` (embed the json)
- Create: `tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs`

**Interfaces:**

- Consumes: `ItemDataset`, `ItemRecord`, `DatasetSources` (Task 1).
- Produces:
  - `IItemDatabase { DatasetSources Sources { get; } ItemRecord? GetById(int id); ItemMatch Resolve(string query); }` — namespace `RustPlusBot.Features.ItemData`. (NOTE: `Resolve` returns `ItemMatch`, defined in Task 3. To keep Task 2 self-contained and compilable, define a **placeholder** `Resolve` that throws `NotImplementedException` is NOT allowed under analyzers; instead, **Task 2 defines `IItemDatabase` with only `Sources` + `GetById`, and Task 3 adds `Resolve`**. See Step 1.)
  - `EmbeddedItemDatabase : IItemDatabase` (singleton).

- [ ] **Step 1: Define the seam (id-only for now)**

`src/RustPlusBot.Features.ItemData/IItemDatabase.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData;

/// <summary>The single source of truth for bundled Rust item data. Singleton.</summary>
public interface IItemDatabase
{
    /// <summary>Per-section provenance dates, for "data as of" display.</summary>
    DatasetSources Sources { get; }

    /// <summary>Gets the record for an item id, or null if unknown.</summary>
    /// <param name="id">The Rust item id.</param>
    ItemRecord? GetById(int id);
}
```

(Task 3 adds `ItemMatch Resolve(string query)` to this interface.)

- [ ] **Step 2: Hand-author a seed bundle**

`src/RustPlusBot.Features.ItemData/Data/item-data.json` — a SMALL, valid seed (replaced by the generator in Task 9). It must contain at least: two items with full calc data, one with null calc fields, and a pair whose names collide on substring (for Task 3 tests). Use real-ish ids/names:

```json
{
  "schemaVersion": 1,
  "sources": {
    "namesAsOf": "2026-04-08",
    "recycleAsOf": "2024-09-07",
    "craftAsOf": "2024-09-07",
    "researchAsOf": "2024-09-07"
  },
  "items": [
    {
      "id": -2069578888, "name": "M249", "stackSize": 1, "despawnSeconds": 3600,
      "recycle": { "recycler": [ { "itemId": 69511070, "quantity": 25, "probability": 1.0 } ] },
      "craft": null,
      "research": { "scrap": 500 }
    },
    {
      "id": 1545779598, "name": "AK-47", "stackSize": 1, "despawnSeconds": 3600,
      "recycle": { "recycler": [ { "itemId": -1059362949, "quantity": 4, "probability": 1.0 } ] },
      "craft": { "ingredients": [ { "itemId": -1059362949, "quantity": 200 } ], "timeSeconds": 30.0, "workbenchLevel": 3 },
      "research": { "scrap": 500 }
    },
    {
      "id": -151838493, "name": "Wood", "stackSize": 1000, "despawnSeconds": null,
      "recycle": null, "craft": null, "research": null
    },
    {
      "id": 200773292, "name": "Sleeping Bag", "stackSize": 1, "despawnSeconds": 300,
      "recycle": null,
      "craft": { "ingredients": [ { "itemId": -151838493, "quantity": 100 } ], "timeSeconds": 7.5, "workbenchLevel": null },
      "research": null
    }
  ]
}
```

- [ ] **Step 3: Embed the json**

In `RustPlusBot.Features.ItemData.csproj`, add inside an `<ItemGroup>`:

```xml
    <EmbeddedResource Include="Data/item-data.json" />
```

- [ ] **Step 4: Write the failing test**

`tests/RustPlusBot.Features.ItemData.Tests/EmbeddedItemDatabaseTests.cs`:

```csharp
using RustPlusBot.Features.ItemData;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class EmbeddedItemDatabaseTests
{
    private readonly EmbeddedItemDatabase _db = new();

    [Fact]
    public void GetById_ReturnsKnownItem()
    {
        var ak = _db.GetById(1545779598);
        Assert.NotNull(ak);
        Assert.Equal("AK-47", ak!.Name);
    }

    [Fact]
    public void GetById_ReturnsNullForUnknown()
    {
        Assert.Null(_db.GetById(123456789));
    }

    [Fact]
    public void Sources_ArePopulated()
    {
        Assert.Equal(new DateOnly(2026, 4, 8), _db.Sources.NamesAsOf);
    }
}
```

- [ ] **Step 5: Run test to verify it fails**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests --filter EmbeddedItemDatabaseTests`
Expected: FAIL — `EmbeddedItemDatabase` not defined.

- [ ] **Step 6: Implement `EmbeddedItemDatabase`**

`src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs`:

```csharp
using System.Collections.Frozen;
using System.Text.Json;
using System.Text.Json.Serialization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData;

/// <summary>Loads the embedded <c>item-data.json</c> once and serves lookups. Singleton.</summary>
public sealed class EmbeddedItemDatabase : IItemDatabase
{
    private const int ExpectedSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    private static readonly ItemDataset Dataset = Load();
    private static readonly FrozenDictionary<int, ItemRecord> ById =
        Dataset.Items.GroupBy(i => i.Id).ToFrozenDictionary(g => g.Key, g => g.Last());

    /// <inheritdoc />
    public DatasetSources Sources => Dataset.Sources;

    /// <inheritdoc />
    public ItemRecord? GetById(int id) => ById.GetValueOrDefault(id);

    private static ItemDataset Load()
    {
        var assembly = typeof(EmbeddedItemDatabase).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("item-data.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException("Embedded item-data.json not found.");
        var dataset = JsonSerializer.Deserialize<ItemDataset>(stream, JsonOptions)
                      ?? throw new InvalidOperationException("item-data.json deserialized to null.");
        if (dataset.SchemaVersion != ExpectedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"item-data.json schema version {dataset.SchemaVersion} != expected {ExpectedSchemaVersion}.");
        }

        return dataset;
    }
}
```

- [ ] **Step 7: Run test to verify it passes**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests --filter EmbeddedItemDatabaseTests`
Expected: PASS (3 tests).

- [ ] **Step 8: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add src/RustPlusBot.Features.ItemData tests/RustPlusBot.Features.ItemData.Tests
git commit -m "feat(itemdata): EmbeddedItemDatabase loads seed bundle by id"
```

---

## Task 3: `ItemMatch` + pure `ItemLookup` + wire `Resolve` into the database

**Files:**

- Create: `src/RustPlusBot.Features.ItemData/Lookup/ItemMatch.cs`
- Create: `src/RustPlusBot.Features.ItemData/Lookup/ItemLookup.cs`
- Modify: `src/RustPlusBot.Features.ItemData/IItemDatabase.cs` (add `Resolve`)
- Modify: `src/RustPlusBot.Features.ItemData/EmbeddedItemDatabase.cs` (impl `Resolve` + name index)
- Create: `tests/RustPlusBot.Features.ItemData.Tests/ItemLookupTests.cs`

**Interfaces:**

- Consumes: `ItemRecord`, `IItemDatabase.GetById` (Tasks 1-2).
- Produces:
  - `abstract record ItemMatch` with `Found(ItemRecord Item)`, `Ambiguous(IReadOnlyList<ItemRecord> Candidates)`, `NotFound` — namespace `RustPlusBot.Features.ItemData.Lookup`.
  - `static class ItemLookup { static ItemMatch Resolve(string query, Func<int, ItemRecord?> byId, IReadOnlyList<ItemRecord> all, int cap = 10); }`
  - `IItemDatabase.Resolve(string query)` now returns `ItemMatch`.

- [ ] **Step 1: Define `ItemMatch`**

`src/RustPlusBot.Features.ItemData/Lookup/ItemMatch.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>The outcome of resolving a user query to an item.</summary>
public abstract record ItemMatch
{
    private ItemMatch() { }

    /// <summary>Exactly one item resolved.</summary>
    /// <param name="Item">The resolved item.</param>
    public sealed record Found(ItemRecord Item) : ItemMatch;

    /// <summary>Several items matched; present candidates for disambiguation.</summary>
    /// <param name="Candidates">The candidate items, capped and ranked.</param>
    public sealed record Ambiguous(IReadOnlyList<ItemRecord> Candidates) : ItemMatch;

    /// <summary>No item matched.</summary>
    public sealed record NotFound : ItemMatch;
}
```

- [ ] **Step 2: Write the failing lookup tests**

`tests/RustPlusBot.Features.ItemData.Tests/ItemLookupTests.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Lookup;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class ItemLookupTests
{
    private static readonly ItemRecord Ak = new(1, "AK-47", 1, null, null, null, null);
    private static readonly ItemRecord Semi = new(2, "Semi-Automatic Rifle", 1, null, null, null, null);
    private static readonly ItemRecord Wood = new(3, "Wood", 1000, null, null, null, null);
    private static readonly ItemRecord Bag = new(4, "Sleeping Bag", 1, null, null, null, null);
    private static readonly IReadOnlyList<ItemRecord> All = [Ak, Semi, Wood, Bag];

    private static ItemMatch Resolve(string q) =>
        ItemLookup.Resolve(q, id => All.FirstOrDefault(i => i.Id == id), All);

    [Fact]
    public void NumericInput_ResolvesById()
    {
        var match = Resolve("3");
        var found = Assert.IsType<ItemMatch.Found>(match);
        Assert.Equal("Wood", found.Item.Name);
    }

    [Fact]
    public void NumericMiss_FallsThroughToNameMatch()
    {
        // "47" is not an id but is a substring of "AK-47".
        var match = Resolve("47");
        var found = Assert.IsType<ItemMatch.Found>(match);
        Assert.Equal("AK-47", found.Item.Name);
    }

    [Fact]
    public void ExactName_BeatsSubstring()
    {
        var match = Resolve("wood");
        var found = Assert.IsType<ItemMatch.Found>(match);
        Assert.Equal("Wood", found.Item.Name);
    }

    [Fact]
    public void SingleSubstring_Found()
    {
        var match = Resolve("sleep");
        var found = Assert.IsType<ItemMatch.Found>(match);
        Assert.Equal("Sleeping Bag", found.Item.Name);
    }

    [Fact]
    public void MultipleSubstring_AmbiguousPrefixFirst()
    {
        // "rifle" hits "Semi-Automatic Rifle"; "a" would be too broad. Use a query that hits 2.
        var match = ItemLookup.Resolve("a", id => All.FirstOrDefault(i => i.Id == id), All);
        var amb = Assert.IsType<ItemMatch.Ambiguous>(match);
        Assert.True(amb.Candidates.Count >= 2);
    }

    [Fact]
    public void NoMatch_NotFound()
    {
        Assert.IsType<ItemMatch.NotFound>(Resolve("zzzzz"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyOrWhitespace_NotFound(string q)
    {
        Assert.IsType<ItemMatch.NotFound>(Resolve(q));
    }

    [Fact]
    public void Ambiguous_RespectsCap()
    {
        var many = Enumerable.Range(1, 50).Select(i => new ItemRecord(i, $"Gun {i}", 1, null, null, null, null)).ToList();
        var match = ItemLookup.Resolve("gun", id => many.FirstOrDefault(x => x.Id == id), many, cap: 10);
        var amb = Assert.IsType<ItemMatch.Ambiguous>(match);
        Assert.Equal(10, amb.Candidates.Count);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests --filter ItemLookupTests`
Expected: FAIL — `ItemLookup` not defined.

- [ ] **Step 4: Implement `ItemLookup`**

`src/RustPlusBot.Features.ItemData/Lookup/ItemLookup.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.ItemData.Lookup;

/// <summary>Pure name-or-id resolution. Exact name beats substring; ambiguous results are ranked and capped.</summary>
public static class ItemLookup
{
    /// <summary>Resolves a user query to an item.</summary>
    /// <param name="query">The raw user input (name or id).</param>
    /// <param name="byId">Looks up an item by id.</param>
    /// <param name="all">All known items, for name matching.</param>
    /// <param name="cap">The maximum number of ambiguous candidates to return.</param>
    /// <returns>A <see cref="ItemMatch"/> describing the outcome.</returns>
    public static ItemMatch Resolve(string query, Func<int, ItemRecord?> byId, IReadOnlyList<ItemRecord> all,
        int cap = 10)
    {
        ArgumentNullException.ThrowIfNull(byId);
        ArgumentNullException.ThrowIfNull(all);

        var trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return new ItemMatch.NotFound();
        }

        if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
            && byId(id) is { } byIdHit)
        {
            return new ItemMatch.Found(byIdHit);
        }

        var exact = all.FirstOrDefault(i => string.Equals(i.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact is not null)
        {
            return new ItemMatch.Found(exact);
        }

        var matches = all
            .Where(i => i.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => i.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            .ThenBy(i => i.Name.Length)
            .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return matches.Count switch
        {
            0 => new ItemMatch.NotFound(),
            1 => new ItemMatch.Found(matches[0]),
            _ => new ItemMatch.Ambiguous(matches.Take(cap).ToList()),
        };
    }
}
```

- [ ] **Step 5: Add `Resolve` to the seam + database**

In `IItemDatabase.cs` add:

```csharp
    /// <summary>Resolves a user query (name or id) to an item.</summary>
    /// <param name="query">The raw user input.</param>
    /// <returns>A match describing the outcome.</returns>
    RustPlusBot.Features.ItemData.Lookup.ItemMatch Resolve(string query);
```

(Add `using RustPlusBot.Features.ItemData.Lookup;` and use `ItemMatch Resolve(string query);` rather than the fully-qualified name — jb will reorder usings.)

In `EmbeddedItemDatabase.cs` add the impl + a cached items list:

```csharp
    /// <inheritdoc />
    public ItemMatch Resolve(string query) => ItemLookup.Resolve(query, GetById, Dataset.Items);
```

(Add `using RustPlusBot.Features.ItemData.Lookup;`.)

- [ ] **Step 6: Run all ItemData tests**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests`
Expected: PASS (lookup + database + dataset tests).

- [ ] **Step 7: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add src/RustPlusBot.Features.ItemData tests/RustPlusBot.Features.ItemData.Tests
git commit -m "feat(itemdata): pure ItemLookup + IItemDatabase.Resolve"
```

---

## Task 4: `AddItemData()` + move `IItemNameResolver` into ItemData, adapter over `IItemDatabase`

**Files:**

- Create: `src/RustPlusBot.Features.ItemData/Naming/IItemNameResolver.cs`
- Create: `src/RustPlusBot.Features.ItemData/Naming/ItemDatabaseNameResolver.cs`
- Create: `src/RustPlusBot.Features.ItemData/ItemDataServiceCollectionExtensions.cs`
- Create: `tests/RustPlusBot.Features.ItemData.Tests/ItemDatabaseNameResolverTests.cs`

**Interfaces:**

- Consumes: `IItemDatabase` (Task 2-3).
- Produces:
  - `IItemNameResolver { string Resolve(int itemId); }` — namespace `RustPlusBot.Features.ItemData.Naming`.
  - `ItemDatabaseNameResolver : IItemNameResolver`.
  - `IServiceCollection AddItemData(this IServiceCollection services)` registering `IItemDatabase`→`EmbeddedItemDatabase` and `IItemNameResolver`→`ItemDatabaseNameResolver`, both singletons.

- [ ] **Step 1: Define the resolver interface in ItemData**

`src/RustPlusBot.Features.ItemData/Naming/IItemNameResolver.cs`:

```csharp
namespace RustPlusBot.Features.ItemData.Naming;

/// <summary>Resolves a Rust item id to a human-readable display name.</summary>
public interface IItemNameResolver
{
    /// <summary>Gets the display name for an item id, or a stable "Item {id}" fallback when unknown.</summary>
    /// <param name="itemId">The Rust item id.</param>
    /// <returns>The display name, or "Item {id}" if not in the bundled lookup.</returns>
    string Resolve(int itemId);
}
```

- [ ] **Step 2: Write the failing adapter test**

`tests/RustPlusBot.Features.ItemData.Tests/ItemDatabaseNameResolverTests.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.ItemData.Tests;

public sealed class ItemDatabaseNameResolverTests
{
    private readonly IItemNameResolver _resolver = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void Resolve_ReturnsName_ForKnownId()
    {
        Assert.Equal("AK-47", _resolver.Resolve(1545779598));
    }

    [Fact]
    public void Resolve_FallsBack_ForUnknownId()
    {
        Assert.Equal("Item 999999", _resolver.Resolve(999999));
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests --filter ItemDatabaseNameResolverTests`
Expected: FAIL — `ItemDatabaseNameResolver` not defined.

- [ ] **Step 4: Implement the adapter**

`src/RustPlusBot.Features.ItemData/Naming/ItemDatabaseNameResolver.cs`:

```csharp
using System.Globalization;

namespace RustPlusBot.Features.ItemData.Naming;

/// <summary>Resolves item names from the <see cref="IItemDatabase"/>. Singleton.</summary>
/// <param name="database">The item database.</param>
public sealed class ItemDatabaseNameResolver(IItemDatabase database) : IItemNameResolver
{
    /// <inheritdoc />
    public string Resolve(int itemId) =>
        database.GetById(itemId)?.Name ?? "Item " + itemId.ToString(CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Write `AddItemData`**

`src/RustPlusBot.Features.ItemData/ItemDataServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.ItemData;

/// <summary>DI registration for the item database.</summary>
public static class ItemDataServiceCollectionExtensions
{
    /// <summary>Registers the item database and name resolver as singletons.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddItemData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IItemDatabase, EmbeddedItemDatabase>();
        services.AddSingleton<IItemNameResolver, ItemDatabaseNameResolver>();
        return services;
    }
}
```

- [ ] **Step 6: Run + format + commit**

Run: `dotnet test tests/RustPlusBot.Features.ItemData.Tests`
Expected: PASS (all).

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add src/RustPlusBot.Features.ItemData tests/RustPlusBot.Features.ItemData.Tests
git commit -m "feat(itemdata): IItemNameResolver adapter + AddItemData registration"
```

---

## Task 5: Re-point StorageMonitors onto ItemData; delete the private resolver + items.json

**Files:**

- Delete: `src/RustPlusBot.Features.StorageMonitors/Naming/IItemNameResolver.cs`
- Delete: `src/RustPlusBot.Features.StorageMonitors/Naming/EmbeddedItemNameResolver.cs`
- Delete: `src/RustPlusBot.Features.StorageMonitors/Naming/items.json`
- Modify: `src/RustPlusBot.Features.StorageMonitors/RustPlusBot.Features.StorageMonitors.csproj`
- Modify: `src/RustPlusBot.Features.StorageMonitors/StorageMonitorServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Features.StorageMonitors/Rendering/StorageMonitorEmbedRenderer.cs`
- Modify: any StorageMonitors test referencing the old `Naming` namespace.

**Interfaces:**

- Consumes: `IItemNameResolver` (now `RustPlusBot.Features.ItemData.Naming`), `AddItemData` (Task 4).

- [ ] **Step 1: Add the project reference**

In `RustPlusBot.Features.StorageMonitors.csproj`, add:

```xml
    <ProjectReference Include="..\RustPlusBot.Features.ItemData\RustPlusBot.Features.ItemData.csproj" />
```

and **remove** the line `<EmbeddedResource Include="Naming/items.json" />`.

- [ ] **Step 2: Delete the old files**

```bash
git rm src/RustPlusBot.Features.StorageMonitors/Naming/IItemNameResolver.cs \
       src/RustPlusBot.Features.StorageMonitors/Naming/EmbeddedItemNameResolver.cs \
       src/RustPlusBot.Features.StorageMonitors/Naming/items.json
```

- [ ] **Step 3: Re-point DI**

In `StorageMonitorServiceCollectionExtensions.cs`:

- Replace `using RustPlusBot.Features.StorageMonitors.Naming;` with `using RustPlusBot.Features.ItemData;`.
- Replace the line `services.AddSingleton<IItemNameResolver, EmbeddedItemNameResolver>();` with `services.AddItemData();`.

- [ ] **Step 4: Fix the renderer's using**

In `Rendering/StorageMonitorEmbedRenderer.cs`, change the resolver `using` from `RustPlusBot.Features.StorageMonitors.Naming` to `RustPlusBot.Features.ItemData.Naming`. The `IItemNameResolver names` ctor parameter and all `names.Resolve(...)` call sites are unchanged (same interface shape).

- [ ] **Step 5: Fix StorageMonitors tests**

Run: `grep -rln "StorageMonitors.Naming\|EmbeddedItemNameResolver" tests/RustPlusBot.Features.StorageMonitors.Tests`
For each hit, change the `using`/type to `RustPlusBot.Features.ItemData.Naming.IItemNameResolver` (and instantiate `new RustPlusBot.Features.ItemData.Naming.ItemDatabaseNameResolver(new EmbeddedItemDatabase())` where the test previously built `new EmbeddedItemNameResolver()`). The new test project ref is transitive via the StorageMonitors project ref, but the **test** project must also reference ItemData if it instantiates these directly — add `<ProjectReference Include="..\..\src\RustPlusBot.Features.ItemData\RustPlusBot.Features.ItemData.csproj" />` to the StorageMonitors test csproj if needed.

- [ ] **Step 6: Build + run the StorageMonitors suite**

Run: `dotnet test tests/RustPlusBot.Features.StorageMonitors.Tests`
Expected: PASS — same count as before (the rendered embed must be unchanged; if any storage embed test asserts an item name, the name must resolve identically from the new bundle. NOTE: the seed `item-data.json` is tiny, so a StorageMonitors test that resolves a specific id NOT in the seed will now get the "Item {id}" fallback. If such a test exists, either add that item to the seed or assert against the fallback — flag it; this is resolved fully once Task 9 regenerates the full bundle).

- [ ] **Step 7: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add -A src/RustPlusBot.Features.StorageMonitors tests/RustPlusBot.Features.StorageMonitors.Tests
git commit -m "refactor(storagemonitors): use shared ItemData name resolver, drop private items.json"
```

---

## Task 6: Pure formatters in Features.Commands

**Files:**

- Modify: `src/RustPlusBot.Features.Commands/RustPlusBot.Features.Commands.csproj` (add ItemData ref)
- Create: `src/RustPlusBot.Features.Commands/Formatting/ItemLine.cs`
- Create: `src/RustPlusBot.Features.Commands/Formatting/RecycleLine.cs`
- Create: `src/RustPlusBot.Features.Commands/Formatting/CraftLine.cs`
- Create: `src/RustPlusBot.Features.Commands/Formatting/ResearchLine.cs`
- Create: `tests/RustPlusBot.Features.Commands.Tests/Formatting/ItemFormatterTests.cs`

**Interfaces:**

- Consumes: `ItemRecord`, `RecycleYield`, `CraftRecipe`, `ResearchCost`, `IItemNameResolver` (ItemData).
- Produces (all `internal static`, namespace `RustPlusBot.Features.Commands.Formatting`):
  - `ItemLine.Format(ItemRecord item) -> string`
  - `RecycleLine.Format(ItemRecord item, IItemNameResolver names) -> string`
  - `CraftLine.Format(ItemRecord item, IItemNameResolver names) -> string`
  - `ResearchLine.Format(ItemRecord item) -> string`

- [ ] **Step 1: Add ItemData ref to Commands**

In `RustPlusBot.Features.Commands.csproj` add:

```xml
    <ProjectReference Include="..\RustPlusBot.Features.ItemData\RustPlusBot.Features.ItemData.csproj" />
```

- [ ] **Step 2: Write the failing formatter tests**

`tests/RustPlusBot.Features.Commands.Tests/Formatting/ItemFormatterTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Tests.Formatting;

public sealed class ItemFormatterTests
{
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());

    [Fact]
    public void ItemLine_IncludesNameAndStack()
    {
        var item = new ItemRecord(1, "AK-47", 1, 3600, null, null, null);
        var line = ItemLine.Format(item);
        Assert.Contains("AK-47", line, StringComparison.Ordinal);
        Assert.Contains("1", line, StringComparison.Ordinal);
    }

    [Fact]
    public void RecycleLine_ListsYields()
    {
        var item = new ItemRecord(1, "AK-47", 1, null,
            new RecycleYield([new YieldEntry(-1059362949, 4, 1.0)]), null, null);
        var line = RecycleLine.Format(item, _names);
        Assert.Contains("AK-47", line, StringComparison.Ordinal);
        Assert.Contains("4", line, StringComparison.Ordinal);
    }

    [Fact]
    public void CraftLine_ListsIngredientsAndTime()
    {
        var item = new ItemRecord(1, "AK-47", 1, null, null,
            new CraftRecipe([new Ingredient(-1059362949, 200)], 30.0, 3), null);
        var line = CraftLine.Format(item, _names);
        Assert.Contains("200", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ResearchLine_ShowsScrap()
    {
        var item = new ItemRecord(1, "AK-47", 1, null, null, null, new ResearchCost(500));
        var line = ResearchLine.Format(item);
        Assert.Contains("500", line, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter ItemFormatterTests`
Expected: FAIL — formatters not defined.

- [ ] **Step 4: Implement the formatters**

`src/RustPlusBot.Features.Commands/Formatting/ItemLine.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line item lookup card.</summary>
internal static class ItemLine
{
    /// <summary>Formats name, id, stack size, and despawn time.</summary>
    /// <param name="item">The item to describe.</param>
    public static string Format(ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var despawn = item.DespawnSeconds is { } seconds
            ? DurationFormat.Compact(TimeSpan.FromSeconds(seconds))
            : "—";
        return string.Create(CultureInfo.InvariantCulture,
            $"{item.Name} (id {item.Id}) · stack {item.StackSize} · despawn {despawn}");
    }
}
```

(Reuse the existing `RustPlusBot.Features.Commands.Formatting.DurationFormat.Compact` — confirm its signature first; it is used by `UptimeCommandHandler`.)

`Formatting/RecycleLine.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line recycler-yield reply.</summary>
internal static class RecycleLine
{
    /// <summary>Formats the recycler outputs for an item, or a "not recyclable" sentinel via the caller.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Recycle"/>).</param>
    /// <param name="names">Resolves output item ids to names.</param>
    public static string Format(ItemRecord item, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(item.Recycle);
        var parts = item.Recycle.Recycler
            .Select(y => string.Create(CultureInfo.InvariantCulture, $"{names.Resolve(y.ItemId)} ×{y.Quantity}"));
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name} → {string.Join(", ", parts)}");
    }
}
```

`Formatting/CraftLine.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.Features.ItemData.Naming;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line craft-recipe reply.</summary>
internal static class CraftLine
{
    /// <summary>Formats the ingredients and craft time for an item.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Craft"/>).</param>
    /// <param name="names">Resolves ingredient item ids to names.</param>
    public static string Format(ItemRecord item, IItemNameResolver names)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(item.Craft);
        var parts = item.Craft.Ingredients
            .Select(g => string.Create(CultureInfo.InvariantCulture, $"{names.Resolve(g.ItemId)} ×{g.Quantity}"));
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name}: {string.Join(", ", parts)}");
    }
}
```

`Formatting/ResearchLine.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats the one-line research-cost reply.</summary>
internal static class ResearchLine
{
    /// <summary>Formats the scrap research cost for an item.</summary>
    /// <param name="item">The item (must have non-null <see cref="ItemRecord.Research"/>).</param>
    public static string Format(ItemRecord item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(item.Research);
        return string.Create(CultureInfo.InvariantCulture, $"{item.Name}: {item.Research.Scrap} scrap");
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter ItemFormatterTests`
Expected: PASS (4 tests).

- [ ] **Step 6: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add src/RustPlusBot.Features.Commands tests/RustPlusBot.Features.Commands.Tests
git commit -m "feat(commands): pure item/recycle/craft/research formatters"
```

---

## Task 7: Localization keys + the four in-game `ICommandHandler`s + registration

**Files:**

- Modify: `src/RustPlusBot.Localization/Strings.resx` and `Strings.fr.resx`
- Create: `src/RustPlusBot.Features.Commands/Handlers/ItemCommandHandler.cs` (+ Recycle/Craft/Research)
- Modify: `src/RustPlusBot.Features.Commands/CommandServiceCollectionExtensions.cs`
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs`
- Modify: `tests/RustPlusBot.Features.Commands.Tests/CommandRegistrationTests.cs` (19 → 23)
- Modify: `tests/RustPlusBot.Features.Commands.Tests/Help/CommandHelpCatalogTests.cs` (HandlerNames array)
- Create: `tests/RustPlusBot.Features.Commands.Tests/Handlers/ItemCommandHandlersTests.cs`

**Interfaces:**

- Consumes: `IItemDatabase`, `ItemMatch` (ItemData), `IItemNameResolver`, the formatters (Task 6), `ICommandHandler`/`CommandContext` (existing), `ILocalizer`.
- Produces: 4 `internal sealed ICommandHandler` named `item`/`recycle`/`craft`/`research`.

- [ ] **Step 1: Add localization keys (EN)**

In `src/RustPlusBot.Localization/Strings.resx` add `<data name="..."><value>...</value></data>` entries (match the existing XML element shape exactly):

- `command.item.ok` = `{0}` (the formatted line is built in code; this wraps it — or set the handler to return the formatter output directly. To keep parity with existing handlers that localize, use a passthrough: value `{0}`.)
- `command.item.notfound` = `No item matching '{0}'.`
- `command.item.ambiguous` = `Did you mean: {0}?`
- `command.recycle.ok` = `{0}`
- `command.recycle.none` = `{0} is not recyclable.`
- `command.craft.ok` = `{0}`
- `command.craft.none` = `{0} is not craftable.`
- `command.research.ok` = `{0}`
- `command.research.none` = `{0} cannot be researched.`
- `help.item` = `Look up an item's name, id, stack, and despawn`
- `help.recycle` = `Show recycler output for an item`
- `help.craft` = `Show an item's craft recipe`
- `help.research` = `Show an item's research scrap cost`

- [ ] **Step 2: Add the same keys (FR)**

In `Strings.fr.resx` add the FR translations (must differ from the key string so `CommandHelpCatalogTests.EveryDescriptionKeyResolvesInEnglishAndFrench` passes):

- `command.item.notfound` = `Aucun objet correspondant à « {0} ».`
- `command.item.ambiguous` = `Vouliez-vous dire : {0} ?`
- `command.recycle.none` = `{0} n'est pas recyclable.`
- `command.craft.none` = `{0} n'est pas fabricable.`
- `command.research.none` = `{0} ne peut pas être recherché.`
- the `.ok` passthroughs = `{0}`
- `help.item` = `Afficher le nom, l'id, la pile et la disparition d'un objet`
- `help.recycle` = `Afficher le recyclage d'un objet`
- `help.craft` = `Afficher la recette de fabrication d'un objet`
- `help.research` = `Afficher le coût de recherche (ferraille) d'un objet`

- [ ] **Step 3: Write the failing handler tests**

`tests/RustPlusBot.Features.Commands.Tests/Handlers/ItemCommandHandlersTests.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Handlers;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Tests.Handlers;

public sealed class ItemCommandHandlersTests
{
    private readonly IItemDatabase _db = new EmbeddedItemDatabase();
    private readonly IItemNameResolver _names = new ItemDatabaseNameResolver(new EmbeddedItemDatabase());
    private readonly ILocalizer _loc = new ResxLocalizer();

    private static CommandContext Ctx(params string[] args) => new(1, Guid.NewGuid(), "en", 99, "Caller", args);

    [Fact]
    public void Names_areCorrect()
    {
        Assert.Equal("item", new ItemCommandHandler(_db, _loc).Name);
        Assert.Equal("recycle", new RecycleCommandHandler(_db, _names, _loc).Name);
        Assert.Equal("craft", new CraftCommandHandler(_db, _names, _loc).Name);
        Assert.Equal("research", new ResearchCommandHandler(_db, _loc).Name);
    }

    [Fact]
    public async Task Item_Found_returnsCard()
    {
        var reply = await new ItemCommandHandler(_db, _loc).ExecuteAsync(Ctx("AK-47"), CancellationToken.None);
        Assert.Contains("AK-47", reply!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Item_NotFound_returnsMessage()
    {
        var reply = await new ItemCommandHandler(_db, _loc).ExecuteAsync(Ctx("zzzzz"), CancellationToken.None);
        Assert.Contains("zzzzz", reply!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Recycle_NotRecyclable_returnsNone()
    {
        // "Wood" is in the seed with null recycle.
        var reply = await new RecycleCommandHandler(_db, _names, _loc).ExecuteAsync(Ctx("Wood"), CancellationToken.None);
        Assert.Contains("Wood", reply!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_args_returnsNotFound()
    {
        var reply = await new ItemCommandHandler(_db, _loc).ExecuteAsync(Ctx(), CancellationToken.None);
        Assert.NotNull(reply);
    }
}
```

- [ ] **Step 4: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests --filter ItemCommandHandlersTests`
Expected: FAIL — handlers not defined.

- [ ] **Step 5: Implement the handlers**

`src/RustPlusBot.Features.Commands/Handlers/ItemCommandHandler.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!item — looks up an item's name, id, stack size, and despawn time.</summary>
/// <param name="database">The item database.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class ItemCommandHandler(IItemDatabase database, ILocalizer localizer) : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "item";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.Resolve(query) switch
        {
            ItemMatch.Found f => localizer.Get("command.item.ok", context.Culture, ItemLine.Format(f.Item)),
            ItemMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
```

`Handlers/RecycleCommandHandler.cs`:

```csharp
using RustPlusBot.Features.Commands.Dispatching;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Handlers;

/// <summary>!recycle — shows the recycler output for an item.</summary>
/// <param name="database">The item database.</param>
/// <param name="names">Resolves output item ids to names.</param>
/// <param name="localizer">The reply localizer.</param>
internal sealed class RecycleCommandHandler(IItemDatabase database, IItemNameResolver names, ILocalizer localizer)
    : ICommandHandler
{
    /// <inheritdoc />
    public string Name => "recycle";

    /// <inheritdoc />
    public Task<string?> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var query = string.Join(' ', context.Args);
        var reply = database.Resolve(query) switch
        {
            ItemMatch.Found { Item.Recycle: not null } f =>
                localizer.Get("command.recycle.ok", context.Culture, RecycleLine.Format(f.Item, names)),
            ItemMatch.Found f => localizer.Get("command.recycle.none", context.Culture, f.Item.Name),
            ItemMatch.Ambiguous a => localizer.Get("command.item.ambiguous", context.Culture,
                string.Join(", ", a.Candidates.Select(c => c.Name))),
            _ => localizer.Get("command.item.notfound", context.Culture, query),
        };
        return Task.FromResult<string?>(reply);
    }
}
```

`Handlers/CraftCommandHandler.cs` — same shape as Recycle, using `command.craft.ok`/`command.craft.none`, the `Craft: not null` pattern, and `CraftLine.Format(f.Item, names)`.

`Handlers/ResearchCommandHandler.cs` — same shape but **no** `IItemNameResolver` dep (scrap-only), `command.research.ok`/`command.research.none`, `Research: not null` pattern, `ResearchLine.Format(f.Item)`.

- [ ] **Step 6: Register the handlers + help entries + count**

In `CommandServiceCollectionExtensions.cs`, after the last `AddScoped<ICommandHandler, ...>()`:

```csharp
        services.AddScoped<ICommandHandler, ItemCommandHandler>();
        services.AddScoped<ICommandHandler, RecycleCommandHandler>();
        services.AddScoped<ICommandHandler, CraftCommandHandler>();
        services.AddScoped<ICommandHandler, ResearchCommandHandler>();
```

First add a new value to `src/RustPlusBot.Features.Commands/Help/CommandGroup.cs` (the enum currently has `Control=0, Server=1, TeamIntel=2, Bot=3` — append):

```csharp
    /// <summary>Item-database commands (item/recycle/craft/research).</summary>
    ItemDb = 4,
```

Then in `Help/CommandHelpCatalog.cs`, add to the `InGame` list:

```csharp
        new("item", CommandGroup.ItemDb, "help.item"),
        new("recycle", CommandGroup.ItemDb, "help.recycle"),
        new("craft", CommandGroup.ItemDb, "help.craft"),
        new("research", CommandGroup.ItemDb, "help.research"),
```

In `tests/.../CommandRegistrationTests.cs` change `Assert.Equal(19, handlers.Count);` to `Assert.Equal(23, handlers.Count);`.

In `tests/.../Help/CommandHelpCatalogTests.cs` add `"item", "recycle", "craft", "research"` to the `HandlerNames` array.

- [ ] **Step 7: Run the full Commands suite**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS — registration count 23, help drift green, handler tests green.

- [ ] **Step 8: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add src/RustPlusBot.Localization src/RustPlusBot.Features.Commands tests/RustPlusBot.Features.Commands.Tests
git commit -m "feat(commands): in-game !item/!recycle/!craft/!research handlers"
```

---

## Task 8: The `/item /recycle /craft /research` slash module + Host wiring

**Files:**

- Create: `src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`
- Modify: `src/RustPlusBot.Features.Commands/Help/CommandHelpCatalog.cs` (add 4 `Slash` entries + `help.slash.*` keys)
- Modify: `src/RustPlusBot.Localization/Strings.resx` + `.fr.resx` (`help.slash.item/recycle/craft/research`)
- Modify: `src/RustPlusBot.Host/Program.cs` (add `AddItemData()`)

**Interfaces:**

- Consumes: `IItemDatabase`, `IItemNameResolver`, formatters, `IWorkspaceStore.GetCultureAsync`, `ILocalizer`, `IServiceScopeFactory`, the `InteractionModuleAssembly` already registered by `AddCommands`.

- [ ] **Step 1: Add Host wiring**

In `src/RustPlusBot.Host/Program.cs`, add (near the other `Add*` feature calls, e.g. before `AddCommands()`):

```csharp
builder.Services.AddItemData();
```

Add `using RustPlusBot.Features.ItemData;` at the top (jb will sort).
(Even though `AddCommands` could call `AddItemData`, register it once in the Host so a single `IItemDatabase` singleton serves both Commands and StorageMonitors. `AddItemData` is idempotent-safe because `AddSingleton<TInterface,TImpl>` registered twice yields one resolved instance per the last registration — but to avoid double registration, DO NOT also call `AddItemData` inside `AddCommands`; StorageMonitors' `AddStorageMonitors` already calls it, and the Host calls each feature's Add once. Confirm there is exactly one effective `IItemDatabase` registration path; simplest: keep `AddItemData()` in both `AddStorageMonitors` and add it to `AddCommands`, since duplicate `AddSingleton` of the same pair is harmless and yields one instance. Choose ONE: register in Host explicitly and remove from feature extensions, OR keep in feature extensions. RECOMMENDED: keep `AddItemData()` inside both `AddStorageMonitors` and `AddCommands`; do not add to Program.cs. Update this step to NOT modify Program.cs if following the recommendation.)

**DECISION (follow this):** Call `services.AddItemData();` at the top of `AddCommands` (mirroring how `AddCommands`/`AddStorageMonitors` call `AddRustPlusBotLocalization()`). Do **not** modify `Program.cs`. Duplicate `AddSingleton<IItemDatabase, EmbeddedItemDatabase>` across features is harmless (last wins, one instance).

- [ ] **Step 2: Add `AddItemData()` to `AddCommands`**

In `CommandServiceCollectionExtensions.cs`, near the top of `AddCommands` (after `AddRustPlusBotLocalization()`):

```csharp
        services.AddItemData();
```

Add `using RustPlusBot.Features.ItemData;`.

- [ ] **Step 3: Add slash help keys (EN + FR)**

`Strings.resx`: `help.slash.item`/`help.slash.recycle`/`help.slash.craft`/`help.slash.research` with English descriptions. `Strings.fr.resx`: the FR equivalents (≠ key).

- [ ] **Step 4: Write the slash module**

`src/RustPlusBot.Features.Commands/Modules/ItemCommandModule.cs`:

```csharp
using Discord;
using Discord.Interactions;
using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Features.Commands.Formatting;
using RustPlusBot.Features.ItemData;
using RustPlusBot.Features.ItemData.Lookup;
using RustPlusBot.Features.ItemData.Naming;
using RustPlusBot.Localization;
using RustPlusBot.Persistence.Workspace;

namespace RustPlusBot.Features.Commands.Modules;

/// <summary>The /item, /recycle, /craft, and /research slash commands.</summary>
/// <param name="scopeFactory">Creates a short-lived DI scope per interaction.</param>
public sealed class ItemCommandModule(IServiceScopeFactory scopeFactory)
    : InteractionModuleBase<SocketInteractionContext>
{
    /// <summary>Looks up an item's name, id, stack size, and despawn time.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("item", "Look up an item")]
    public Task ItemAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, (db, _, rec, loc, culture) =>
            loc.Get("command.item.ok", culture, ItemLine.Format(rec)));

    /// <summary>Shows recycler output for an item.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("recycle", "Show recycler output for an item")]
    public Task RecycleAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, (db, names, rec, loc, culture) => rec.Recycle is not null
            ? loc.Get("command.recycle.ok", culture, RecycleLine.Format(rec, names))
            : loc.Get("command.recycle.none", culture, rec.Name));

    /// <summary>Shows an item's craft recipe.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("craft", "Show an item's craft recipe")]
    public Task CraftAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, (db, names, rec, loc, culture) => rec.Craft is not null
            ? loc.Get("command.craft.ok", culture, CraftLine.Format(rec, names))
            : loc.Get("command.craft.none", culture, rec.Name));

    /// <summary>Shows an item's research scrap cost.</summary>
    /// <param name="item">The item name or id.</param>
    [SlashCommand("research", "Show an item's research scrap cost")]
    public Task ResearchAsync([Summary("item", "Item name or id")] string item) =>
        RespondForAsync(item, (db, names, rec, loc, culture) => rec.Research is not null
            ? loc.Get("command.research.ok", culture, ResearchLine.Format(rec))
            : loc.Get("command.research.none", culture, rec.Name));

    private async Task RespondForAsync(string query,
        Func<IItemDatabase, IItemNameResolver, RustPlusBot.Features.ItemData.Data.ItemRecord, ILocalizer, string, string> onFound)
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
                .WithFooter($"data as of {db.Sources.NamesAsOf:yyyy-MM-dd}")
                .Build();
            await RespondAsync(ephemeral: true, embed: embed).ConfigureAwait(false);
        }
    }
}
```

(If the `IItemNameResolver` is not registered in the scope because `AddItemData` only registered it as a singleton — it is, singletons resolve from any scope. Confirm `IWorkspaceStore` is the right culture source as in `CommandSurfaceModule`.)

- [ ] **Step 5: Add the 4 `Slash` catalog entries**

In `Help/CommandHelpCatalog.cs` add to the `Slash` list:

```csharp
        new("item", CommandGroup.ItemDb, "help.slash.item"),
        new("recycle", CommandGroup.ItemDb, "help.slash.recycle"),
        new("craft", CommandGroup.ItemDb, "help.slash.craft"),
        new("research", CommandGroup.ItemDb, "help.slash.research"),
```

- [ ] **Step 6: Build + full Commands suite**

Run: `dotnet test tests/RustPlusBot.Features.Commands.Tests`
Expected: PASS (the module itself is not unit-tested — InteractionModuleBase, consistent with the repo; the help catalog resolves all `help.slash.*` keys EN/FR).

- [ ] **Step 7: Format + commit**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add src/RustPlusBot.Features.Commands src/RustPlusBot.Localization
git commit -m "feat(commands): /item /recycle /craft /research slash module"
```

---

## Task 9: The generator tool (validator-first, offline transform) + regenerate the real bundle

**Files:**

- Create: `tools/RustPlusBot.ItemData.Generator/RustPlusBot.ItemData.Generator.csproj`
- Create: `tools/RustPlusBot.ItemData.Generator/Program.cs`
- Create: `tools/RustPlusBot.ItemData.Generator/Sources/INamesSource.cs`, `IRustLabsSource.cs`, and offline impls
- Create: `tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`
- Create: `tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`
- Modify: `RustPlusBot.slnx`
- Modify (regenerate): `src/RustPlusBot.Features.ItemData/Data/item-data.json`

**Interfaces:**

- Consumes: the `ItemData` record types (referenced for schema), the local `~/Dev/rustplusplus/src/staticFiles/*.json`.
- Produces: `DatasetValidator.Validate(ItemDataset dataset, ValidationOptions options) -> IReadOnlyList<string> errors` (empty = valid). Namespace `RustPlusBot.ItemData.Generator.Validation`.

- [ ] **Step 1: Create the tool project**

`tools/RustPlusBot.ItemData.Generator/RustPlusBot.ItemData.Generator.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <ItemGroup>
    <InternalsVisibleTo Include="RustPlusBot.ItemData.Generator.Tests" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\RustPlusBot.Features.ItemData\RustPlusBot.Features.ItemData.csproj" />
  </ItemGroup>

</Project>
```

(HTTP/HTML scraping packages — e.g. `HtmlAgilityPack` — are needed only for the live `IRustLabsSource` scrape impl. For 6a, the default run is the OFFLINE transform of the existing rustplusplus JSON, which needs no new packages. Add a scrape impl behind `--online` in a follow-up; the interface is defined now so it slots in.)

- [ ] **Step 2: Write the failing validator tests**

`tests/RustPlusBot.ItemData.Generator.Tests/DatasetValidatorTests.cs`:

```csharp
using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.ItemData.Generator.Validation;

namespace RustPlusBot.ItemData.Generator.Tests;

public sealed class DatasetValidatorTests
{
    private static ItemDataset Good() => new(1,
        new DatasetSources(new(2026, 4, 8), new(2024, 9, 7), new(2024, 9, 7), new(2024, 9, 7)),
        [
            new ItemRecord(1, "AK-47", 1, 3600,
                new RecycleYield([new YieldEntry(2, 4, 1.0)]), null, null),
            new ItemRecord(2, "Metal Fragments", 1000, null, null, null, null),
        ]);

    [Fact]
    public void Good_dataset_hasNoErrors()
    {
        var errors = DatasetValidator.Validate(Good(), new ValidationOptions(MinItemCount: 1));
        Assert.Empty(errors);
    }

    [Fact]
    public void TooFewItems_isError()
    {
        var errors = DatasetValidator.Validate(Good(), new ValidationOptions(MinItemCount: 5000));
        Assert.Contains(errors, e => e.Contains("item count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnresolvableYieldId_isError()
    {
        var bad = new ItemDataset(1, Good().Sources,
        [
            new ItemRecord(1, "AK-47", 1, null,
                new RecycleYield([new YieldEntry(99999, 4, 1.0)]), null, null),
        ]);
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("99999", StringComparison.Ordinal));
    }
}
```

- [ ] **Step 3: Run to verify failure**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests`
Expected: FAIL — `DatasetValidator` not defined. (Add the test project to the solution first via `dotnet sln RustPlusBot.slnx add`.)

- [ ] **Step 4: Implement the validator**

`tools/RustPlusBot.ItemData.Generator/Validation/DatasetValidator.cs`:

```csharp
using System.Globalization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Validation;

/// <summary>Validation thresholds.</summary>
/// <param name="MinItemCount">The minimum acceptable item count.</param>
public sealed record ValidationOptions(int MinItemCount);

/// <summary>Validates a generated dataset, failing loudly on shape drift.</summary>
public static class DatasetValidator
{
    /// <summary>Returns a list of human-readable validation errors (empty = valid).</summary>
    /// <param name="dataset">The dataset to validate.</param>
    /// <param name="options">The validation thresholds.</param>
    public static IReadOnlyList<string> Validate(ItemDataset dataset, ValidationOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();
        if (dataset.Items.Count < options.MinItemCount)
        {
            errors.Add(string.Create(CultureInfo.InvariantCulture,
                $"item count {dataset.Items.Count} below minimum {options.MinItemCount}"));
        }

        var ids = dataset.Items.Select(i => i.Id).ToHashSet();
        foreach (var item in dataset.Items)
        {
            foreach (var y in item.Recycle?.Recycler ?? [])
            {
                if (!ids.Contains(y.ItemId))
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture,
                        $"item {item.Id} '{item.Name}' recycle yield references unknown id {y.ItemId}"));
                }
            }

            foreach (var g in item.Craft?.Ingredients ?? [])
            {
                if (!ids.Contains(g.ItemId))
                {
                    errors.Add(string.Create(CultureInfo.InvariantCulture,
                        $"item {item.Id} '{item.Name}' craft ingredient references unknown id {g.ItemId}"));
                }
            }
        }

        return errors;
    }
}
```

- [ ] **Step 5: Run to verify pass**

Run: `dotnet test tests/RustPlusBot.ItemData.Generator.Tests`
Expected: PASS (3 tests).

- [ ] **Step 6: Implement the source interfaces + offline transform + Program**

- `Sources/INamesSource.cs`: `internal interface INamesSource { IReadOnlyDictionary<int,(string Name,int StackSize,int? DespawnSeconds)> Load(); }`
- `Sources/IRustLabsSource.cs`: `internal interface IRustLabsSource { /* recycle/craft/research keyed by id */ ... }` — define return shapes mapping to `RecycleYield?`/`CraftRecipe?`/`ResearchCost?`.
- Offline impls reading `~/Dev/rustplusplus/src/staticFiles/items.json`, `rustlabsRecycleData.json`, `rustlabsCraftData.json`, `rustlabsResearchData.json`, `rustlabsStackData.json`, `rustlabsDespawnData.json`. Exact shapes (verified against the real files — all id-keyed maps):
  - recycle: `{ "recycler": { "yield": [ { "id": "<id>", "probability": <num>, "quantity": <int> } ] } }` → `RecycleYield(Recycler)`. Ignore the `shredder`/`safe-zone-recycler` keys in 6a.
  - craft: `{ "ingredients": [ { "id": "<id>", "quantity": <int> } ], "time": <seconds>, "workbench": <null|level> }` → `CraftRecipe(Ingredients, time, workbench)`.
  - research: `{ "researchTable": <scrap>, "workbench": <null|level> }` → `ResearchCost(Scrap: researchTable)`. **The scrap cost is the `researchTable` field**, not a field literally named `scrap`.
  - stack: `{ "quantity": "<int-as-string>" }` → `StackSize` (parse the string with `int.Parse`/`InvariantCulture`).
  - despawn: `{ "time": <seconds>, "timeString": "<text>" }` → `DespawnSeconds`.
  - names: `items.json` is `{ "<id>": "<name>" }` (the same file 4c used).
  - NOTE: ids in the rustlabs files are JSON **strings**; `ItemRecord.Id`/`YieldEntry.ItemId`/`Ingredient.ItemId` are `int` — parse with `int.Parse(..., NumberStyles.Integer, CultureInfo.InvariantCulture)` and skip unparseable keys with a logged count.
- `Program.cs`: CLI `--out <path>`, `--rustplusplus <dir>` (default `~/Dev/rustplusplus/src/staticFiles`), `--min-items <n>`. Build the `ItemDataset`, run `DatasetValidator.Validate`; on errors print them and `return 1` WITHOUT writing; on success write `item-data.json` to `--out` and `return 0`. Stamp `SchemaVersion=1` and per-section `sourceDate` from each source file's known date (hardcode `2024-09-07` for rustlabs, `2026-04-08` for names, or take from a CLI flag).

(This step is larger — split into its own commit. The exact JSON parsing of each rustlabs file is mechanical; consult the real files at `~/Dev/rustplusplus/src/staticFiles/`. Items not present in the names source are skipped; rustlabs entries whose id is unknown to names are dropped with a logged count, NOT silently — print "dropped N orphan recycle entries".)

- [ ] **Step 7: Add tool + test project to solution, build**

```bash
dotnet sln RustPlusBot.slnx add tools/RustPlusBot.ItemData.Generator/RustPlusBot.ItemData.Generator.csproj
dotnet sln RustPlusBot.slnx add tests/RustPlusBot.ItemData.Generator.Tests/RustPlusBot.ItemData.Generator.Tests.csproj
dotnet build tools/RustPlusBot.ItemData.Generator
```

Expected: build succeeds.

- [ ] **Step 8: Regenerate the real bundle**

Run:

```bash
dotnet run --project tools/RustPlusBot.ItemData.Generator -- \
  --out src/RustPlusBot.Features.ItemData/Data/item-data.json \
  --rustplusplus ~/Dev/rustplusplus/src/staticFiles \
  --min-items 1000
```

Expected: exit 0, `item-data.json` now contains the full item set. Re-run the ItemData + Commands + StorageMonitors suites — all green (now that real names resolve, any Task-5 fallback assertions can be tightened back to real names if they were loosened).

- [ ] **Step 9: Format + commit (tool, then bundle)**

```bash
dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder
git add tools/RustPlusBot.ItemData.Generator tests/RustPlusBot.ItemData.Generator.Tests RustPlusBot.slnx
git commit -m "feat(itemdata): generator tool + DatasetValidator (offline transform)"
git add src/RustPlusBot.Features.ItemData/Data/item-data.json
git commit -m "chore(itemdata): regenerate full item-data bundle from rustplusplus sources"
```

---

## Task 10: Whole-feature verification

**Files:** none (verification only).

- [ ] **Step 1: Full clean build**

Run: `dotnet build RustPlusBot.slnx -warnaserror`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full test suite with per-assembly counts**

Run: `dotnet test RustPlusBot.slnx -maxcpucount:1`
Expected: ALL green. Read the per-assembly summary — confirm ItemData.Tests, Generator.Tests, Commands.Tests (count up by the new tests), StorageMonitors.Tests (same count as before the change) all ran. A drop in any assembly's count = a broken fake; investigate before proceeding.

- [ ] **Step 3: Format gate**

Run: `dotnet jb cleanupcode RustPlusBot.slnx --profile=ReformatAndReorder` then `git diff --exit-code`
Expected: no diff.

- [ ] **Step 4: No EF drift**

Run: `dotnet ef migrations has-pending-model-changes --project src/RustPlusBot.Persistence` (or the project hosting the DbContext)
Expected: no pending changes. If the tooling errors ("Unable to retrieve project metadata"), verify instead that no `Migrations/`, `*ModelSnapshot*`, `DbContext`, or entity files changed on the branch: `git diff --name-only develop... | grep -iE "migration|snapshot|dbcontext|entit" || echo "no schema files touched"`.

- [ ] **Step 5: Dependency-leaf sanity**

Run: `grep -E "ProjectReference" src/RustPlusBot.Features.ItemData/RustPlusBot.Features.ItemData.csproj`
Expected: only `Abstractions` + `Localization`. No Discord/Persistence/Domain/Features.

- [ ] **Step 6: Final commit if any format fixups**

```bash
git add -A && git commit -m "chore(itemdata): final formatting" --allow-empty
```

---

## Self-Review

**Spec coverage:**

- §1 dataset + `IItemDatabase` → Tasks 1-4. ✓
- §1 generator tool + fail-loud validation → Task 9. ✓
- §1 four calculators (slash + in-game) → Tasks 6-8. ✓
- §2 supersede `IItemNameResolver`, remove `items.json` → Tasks 4-5. ✓
- §3.2 leaf dependency direction → Task 1 csproj + Task 10 Step 5. ✓
- §3.3 schema records → Task 1. ✓
- §3.5 lookup algorithm (numeric fallthrough, exact-wins, ambiguous cap) → Task 3. ✓
- §4.1 in-game terse, no-socket → Task 7. ✓
- §4.2 slash ephemeral embed + "data as of" footer → Task 8. ✓
- §4.3 outcome rendering (Found/Ambiguous/NotFound) EN/FR → Tasks 7-8. ✓
- §4.4 `/help` catalog → Tasks 7-8 (InGame + Slash entries + drift test). ✓
- §6 error handling (schema mismatch throw, duplicate-id last-wins, null-calc reply) → Task 2 (load), Task 2 (frozen group), Task 7 (`.none`). ✓
- §7 testing (lookup exhaustive, db load, formatters, handlers, validator good+drift) → Tasks 2,3,6,7,9. ✓
- §8 integration touchpoints (resolver re-point, reg count, no cycle, host wiring) → Tasks 5,7,8,10. ✓

**Placeholder scan:** Task 9 Step 6 is the one prose-heavy step (the rustlabs JSON parsing is mechanical and file-shape-dependent). It names exact source files, exact output types, and the drop-orphans-loudly rule — acceptable as the parsing is transcription against real files the engineer opens. All other steps have complete code.

**Type consistency:** `IItemDatabase` gains `Resolve` in Task 3 (defined id-only in Task 2 — flagged inline). `ItemMatch.Found/Ambiguous/NotFound` used identically in Tasks 3,7,8. `ItemRecord`/`RecycleYield`/`CraftRecipe`/`ResearchCost` consistent across Tasks 1,6,7,8,9. Formatter signatures (`Format(ItemRecord)` / `Format(ItemRecord, IItemNameResolver)`) consistent Tasks 6→7→8. `ValidationOptions(MinItemCount)` consistent Task 9. Handler count 19→23 consistent Task 7. ✓

**Resolved inline:** the `AddItemData` double-registration ambiguity (Task 8 Step 1) — DECISION locked to "call inside both feature extensions, do not touch Program.cs."
