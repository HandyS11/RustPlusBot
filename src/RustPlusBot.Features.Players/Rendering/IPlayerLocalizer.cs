using RustPlusBot.Localization;

namespace RustPlusBot.Features.Players.Rendering;

/// <summary>Resolves localized player-event strings by key and culture, falling back to English.</summary>
internal interface IPlayerLocalizer : ILocalizer;
