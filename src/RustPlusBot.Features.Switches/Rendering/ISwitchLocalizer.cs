using RustPlusBot.Localization;

namespace RustPlusBot.Features.Switches.Rendering;

/// <summary>Resolves localized smart-switch strings by key and culture, falling back to English.</summary>
internal interface ISwitchLocalizer : ILocalizer;
