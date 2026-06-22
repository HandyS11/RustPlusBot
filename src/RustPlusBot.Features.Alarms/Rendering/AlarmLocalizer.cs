using System.Globalization;

namespace RustPlusBot.Features.Alarms.Rendering;

/// <summary>Dictionary-backed localizer for Smart Alarms with English fallback and region normalization.</summary>
/// <remarks>Mirrors the SwitchLocalizer pattern; a future refactor may hoist a shared implementation.</remarks>
/// <param name="catalog">The culture → (key → value) catalog.</param>
internal sealed class AlarmLocalizer(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> catalog)
    : IAlarmLocalizer
{
    private const string FallbackCulture = "en";

    /// <inheritdoc />
    public string Get(string key, string culture)
    {
        var normalized = Normalize(culture);
        if (catalog.TryGetValue(normalized, out var map) && map.TryGetValue(key, out var value))
        {
            return value;
        }

        if (catalog.TryGetValue(FallbackCulture, out var fallback) &&
            fallback.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    /// <inheritdoc />
    public string Get(string key, string culture, params object[] args)
    {
        var format = Get(key, culture);
        var provider = ResolveFormatProvider(Normalize(culture));
        return string.Format(provider, format, args);
    }

    private static string Normalize(string culture)
    {
        if (string.IsNullOrWhiteSpace(culture))
        {
            return FallbackCulture;
        }

        var dash = culture.IndexOf('-', StringComparison.Ordinal);
        var primary = dash >= 0 ? culture[..dash] : culture;
        return primary.ToLowerInvariant();
    }

    private static CultureInfo ResolveFormatProvider(string culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }
}
