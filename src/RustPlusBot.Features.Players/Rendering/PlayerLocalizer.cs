using System.Globalization;

namespace RustPlusBot.Features.Players.Rendering;

/// <summary>Dictionary-backed <see cref="IPlayerLocalizer"/> with English fallback and region normalization.</summary>
/// <remarks>Duplicated from the command/workspace localizers; consolidate into a shared project in a future refactor.</remarks>
/// <param name="catalog">The string catalog.</param>
internal sealed class PlayerLocalizer(PlayerLocalizationCatalog catalog) : IPlayerLocalizer
{
    private const string FallbackCulture = "en";

    /// <inheritdoc />
    public string Get(string key, string culture)
    {
        var normalized = Normalize(culture);
        if (catalog.Strings.TryGetValue(normalized, out var map) && map.TryGetValue(key, out var value))
        {
            return value;
        }

        if (catalog.Strings.TryGetValue(FallbackCulture, out var fallback) &&
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
