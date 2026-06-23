using RustPlusBot.Localization;

namespace RustPlusBot.Features.Commands.Localization;

/// <summary>Resolves localized in-game reply strings by key and culture, falling back to English.</summary>
internal interface ICommandLocalizer : ILocalizer;
