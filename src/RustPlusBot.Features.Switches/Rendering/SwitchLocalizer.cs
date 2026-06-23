using RustPlusBot.Localization;

namespace RustPlusBot.Features.Switches.Rendering;

/// <summary>Smart-switch localizer backed by the shared <see cref="DictionaryLocalizer"/>.</summary>
/// <param name="catalog">The string catalog.</param>
internal sealed class SwitchLocalizer(SwitchLocalizationCatalog catalog)
    : DictionaryLocalizer(catalog.Strings), ISwitchLocalizer;
