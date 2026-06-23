using RustPlusBot.Localization;

namespace RustPlusBot.Features.Events.Rendering;

/// <summary>Resolves localized live-event strings by key and culture, falling back to English.</summary>
internal interface IEventLocalizer : ILocalizer;
