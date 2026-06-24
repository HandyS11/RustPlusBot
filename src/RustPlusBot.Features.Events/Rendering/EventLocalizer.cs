using RustPlusBot.Localization;

namespace RustPlusBot.Features.Events.Rendering;

/// <summary>Live-event localizer backed by the shared <see cref="DictionaryLocalizer"/>.</summary>
/// <param name="catalog">The string catalog.</param>
internal sealed class EventLocalizer(EventLocalizationCatalog catalog)
    : DictionaryLocalizer(catalog.Strings), IEventLocalizer;
