using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Localization;

/// <summary>In-game reply localizer backed by the shared <see cref="DictionaryLocalizer"/>.</summary>
/// <param name="catalog">The string catalog.</param>
internal sealed class CommandLocalizer(CommandLocalizationCatalog catalog)
    : DictionaryLocalizer(catalog.Strings), ICommandLocalizer;
