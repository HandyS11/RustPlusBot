using RustPlusBot.Features.ItemData.Data;
using RustPlusBot.ItemData.Generator.Validation;

namespace RustPlusBot.ItemData.Generator.Tests;

/// <summary>Unit tests for <see cref="DatasetValidator"/>.</summary>
public sealed class DatasetValidatorTests
{
    private static ItemDataset Good() => new(1,
        new DatasetSources(new(2026, 4, 8), new(2024, 9, 7), new(2024, 9, 7), new(2024, 9, 7), new(2024, 9, 7),
            new(2024, 9, 7)),
        [
            new ItemRecord(1, "AK-47", 1, 3600,
                new RecycleYield([new YieldEntry(2, 4, 1.0)]), null, null, null, null),
            new ItemRecord(2, "Metal Fragments", 1000, null, null, null, null, null, null),
        ]);

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
        ]);
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
        ]);
        var errors = DatasetValidator.Validate(bad, new ValidationOptions(MinItemCount: 1));
        Assert.Contains(errors, e => e.Contains("88888", StringComparison.Ordinal));
    }
}
