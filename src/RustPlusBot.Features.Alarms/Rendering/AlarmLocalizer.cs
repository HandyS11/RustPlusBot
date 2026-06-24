using RustPlusBot.Localization;

namespace RustPlusBot.Features.Alarms.Rendering;

/// <summary>Smart-alarm localizer backed by the shared <see cref="DictionaryLocalizer"/>.</summary>
/// <param name="catalog">The culture → (key → value) catalog.</param>
internal sealed class AlarmLocalizer(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> catalog)
    : DictionaryLocalizer(catalog), IAlarmLocalizer;
