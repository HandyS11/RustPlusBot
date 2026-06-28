using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Validation;

/// <summary>Options that control dataset validation thresholds.</summary>
/// <param name="MinItemCount">The minimum number of items the dataset must contain.</param>
/// <param name="MinRaidTargetCount">The minimum number of raid targets the dataset must contain.</param>
/// <param name="MinSmelterCount">The minimum number of smelters the dataset must contain.</param>
internal sealed record ValidationOptions(int MinItemCount, int MinRaidTargetCount = 0, int MinSmelterCount = 0);

/// <summary>Validates an <see cref="ItemDataset"/> for structural integrity.</summary>
internal static class DatasetValidator
{
    /// <summary>
    /// Validates the given <paramref name="dataset"/> against the supplied <paramref name="options"/>.
    /// </summary>
    /// <param name="dataset">The dataset to validate.</param>
    /// <param name="options">Thresholds that control which checks are performed.</param>
    /// <returns>
    /// An empty list when the dataset is valid; otherwise a list of human-readable error messages.
    /// </returns>
    public static IReadOnlyList<string> Validate(ItemDataset dataset, ValidationOptions options)
    {
        var errors = new List<string>();
        var ids = new HashSet<int>(dataset.Items.Select(i => i.Id));

        if (dataset.Items.Count < options.MinItemCount)
        {
            errors.Add(
                $"item count {dataset.Items.Count} below minimum {options.MinItemCount}");
        }

        foreach (var item in dataset.Items.Where(i => i.Recycle is not null))
        {
            foreach (var entry in item.Recycle!.Recycler.Where(e => !ids.Contains(e.ItemId)))
            {
                errors.Add(
                    $"item {item.Id} ({item.Name}): recycle yield references unknown id {entry.ItemId}");
            }
        }

        foreach (var item in dataset.Items.Where(i => i.Craft is not null))
        {
            foreach (var ingredient in item.Craft!.Ingredients.Where(ing => !ids.Contains(ing.ItemId)))
            {
                errors.Add(
                    $"item {item.Id} ({item.Name}): craft ingredient references unknown id {ingredient.ItemId}");
            }
        }

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

        return errors;
    }
}
