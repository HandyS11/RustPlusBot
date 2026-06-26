using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Validation;

/// <summary>Options that control dataset validation thresholds.</summary>
/// <param name="MinItemCount">The minimum number of items the dataset must contain.</param>
internal sealed record ValidationOptions(int MinItemCount);

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

        return errors;
    }
}
