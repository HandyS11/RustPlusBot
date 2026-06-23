using RustPlusBot.Localization;

namespace RustPlusBot.Features.Players.Rendering;

/// <summary>Player-event localizer backed by the shared <see cref="DictionaryLocalizer"/>.</summary>
/// <param name="catalog">The string catalog.</param>
internal sealed class PlayerLocalizer(PlayerLocalizationCatalog catalog)
    : DictionaryLocalizer(catalog.Strings), IPlayerLocalizer;
