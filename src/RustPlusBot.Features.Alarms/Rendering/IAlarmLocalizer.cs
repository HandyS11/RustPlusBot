using RustPlusBot.Localization;

namespace RustPlusBot.Features.Alarms.Rendering;

/// <summary>Resolves localized smart-alarm strings by key and culture, falling back to English.</summary>
internal interface IAlarmLocalizer : ILocalizer;
