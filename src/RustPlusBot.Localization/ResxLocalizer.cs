using System.Globalization;
using System.Resources;

namespace RustPlusBot.Localization;

/// <summary>
/// <see cref="ILocalizer"/> backed by the embedded <c>Strings</c> resource set,
/// resolving per-call culture with English fallback and region normalization.
/// </summary>
public sealed class ResxLocalizer : ILocalizer
{
    private const string FallbackCulture = "en";

    private static readonly ResourceManager Resources =
        new("RustPlusBot.Localization.Strings", typeof(ResxLocalizer).Assembly);

    /// <inheritdoc />
    public string Get(string key, string culture)
    {
        var info = ResolveCulture(culture);

        // ResourceManager walks the requested culture down to the neutral (en) set,
        // so a single lookup already covers the English fallback.
        var value = Resources.GetString(key, info);
        return value ?? key;
    }

    /// <inheritdoc />
    public string Get(string key, string culture, params object[] args)
    {
        var format = Get(key, culture);
        return string.Format(ResolveCulture(culture), format, args);
    }

    private static CultureInfo ResolveCulture(string culture)
    {
        var normalized = Normalize(culture);
        try
        {
            return CultureInfo.GetCultureInfo(normalized);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
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
}
