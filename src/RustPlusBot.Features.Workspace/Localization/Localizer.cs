namespace RustPlusBot.Features.Workspace.Localization;

/// <summary>Workspace localizer backed by the shared <see cref="RustPlusBot.Localization.DictionaryLocalizer"/>.</summary>
/// <param name="catalog">The string catalog.</param>
internal sealed class Localizer(LocalizationCatalog catalog)
    : RustPlusBot.Localization.DictionaryLocalizer(catalog.Strings), ILocalizer;
