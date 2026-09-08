using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.ItemData.Generator.Validation;

namespace RustPlusBot.ItemData.Generator.Tests;

/// <summary>Unit tests for <see cref="DatasetValidator"/>.</summary>
public sealed class DatasetValidatorTests
{
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

    private static ItemDataset WithRaid(params RaidTarget[] raid) =>
        new(3, Good().Sources, Good().Items, raid, [], []);

    private static ItemDataset WithSmelters(params Smelter[] smelters) =>
        new(4, Good().Sources, Good().Items, [], smelters, []);

    private static ItemDataset WithCctv(params CctvMonument[] cctv) =>
        new(5, Good().Sources, Good().Items, [], [], cctv);

    /// <summary>A dataset with all referential constraints satisfied should produce no errors.</summary>
    [Fact]
    public void Good_dataset_hasNoErrors()
    {
        var errors = DatasetValidator.Validate(Good(), new ValidationOptions(MinItemCount: 1));
        Assert.Empty(errors);
    }

    /// <summary>A dataset with fewer items than the minimum should produce an error.</summary>
    [Fact]
    public void TooFewItems_isError()
    {
        var errors = DatasetValidator.Validate(Good(), new ValidationOptions(MinItemCount: 5000));
        Assert.Contains(errors, e => e.Contains("item count", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A recycle yield that references an unknown item id should produce an error.</summary>
    [Fact]
    public void UnresolvableYieldId_isError()
    {
        var bad = new ItemDataset(1, Good().Sources,
            [
                new ItemRecord(1, "AK-47", 1, null,
                    new RecycleYield([new YieldEntry(99999, 4, 1.0)]), null, null, null, null),
            ],
            [],
            [],
            []);
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("99999", StringComparison.Ordinal));
    }

    /// <summary>A craft ingredient that references an unknown item id should produce an error.</summary>
    [Fact]
    public void UnresolvableCraftIngredientId_isError()
    {
        var bad = new ItemDataset(1, Good().Sources,
            [
                new ItemRecord(1, "AK-47", 1, null, null,
                    new CraftRecipe([new Ingredient(88888, 100)], 30.0, 3), null, null, null),
            ],
            [],
            [],
            []);
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("88888", StringComparison.Ordinal));
    }

    /// <summary>An upkeep entry that references an unknown item id should produce an error.</summary>
    [Fact]
    public void UnresolvableUpkeepId_isError()
    {
        var bad = new ItemDataset(2, Good().Sources,
            [
                new ItemRecord(1, "Wooden Door", 1, null, null, null, null, null,
                    new UpkeepCost([new UpkeepEntry(77777, 8, 25)])),
            ],
            [],
            [],
            []);
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
            ],
            [],
            [],
            []);
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
            ],
            [],
            [],
            []);
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("decay", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A raid cost that references an unknown tool id should produce an error.</summary>
    [Fact]
    public void RaidCost_UnknownToolId_isError()
    {
        var bad = WithRaid(new RaidTarget("Stone Wall", "Stone Wall", RaidTargetKind.BuildingBlock,
            [new RaidCost(424242, null, null, 2, 11.5, 4400, 120)]));
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("424242", StringComparison.Ordinal));
    }

    /// <summary>A raid cost with a non-positive quantity should produce an error.</summary>
    [Fact]
    public void RaidCost_NonPositiveQuantity_isError()
    {
        var bad = WithRaid(new RaidTarget("Stone Wall", "Stone Wall", RaidTargetKind.BuildingBlock,
            [new RaidCost(1, null, null, 0, 1, 1, null)]));
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("quantity", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Too few raid targets should produce an error when a floor is set.</summary>
    [Fact]
    public void TooFewRaidTargets_isError()
    {
        var errors = DatasetValidator.Validate(Good(), new ValidationOptions(MinItemCount: 1, MinRaidTargetCount: 300));
        Assert.Contains(errors, e => e.Contains("raid target count", StringComparison.OrdinalIgnoreCase));
    }

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

    /// <summary>A smelter whose conversions are all internally consistent should produce no errors.</summary>
    [Fact]
    public void GoodSmelter_hasNoErrors()
    {
        var ds = WithSmelters(new Smelter("100", "Furnace",
            [new SmeltConversion(1, 2, 1, 1, 1, 3)]));
        var errors = DatasetValidator.Validate(ds, new ValidationOptions(MinItemCount: 1));
        Assert.Empty(errors);
    }

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
}
