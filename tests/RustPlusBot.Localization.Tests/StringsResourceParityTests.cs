using System.Collections;
using System.Globalization;
using System.Resources;

namespace RustPlusBot.Localization.Tests;

public sealed class StringsResourceParityTests
{
    private static readonly ResourceManager Resources =
        new("RustPlusBot.Localization.Strings", typeof(ResxLocalizer).Assembly);

    private static HashSet<string> Keys(CultureInfo culture)
    {
        // Do not dispose: ResourceManager caches and reuses the returned set instance,
        // so disposing it here would break the next culture's lookup.
        var set = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!;
        return set.Cast<DictionaryEntry>()
            .Select(e => (string)e.Key)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> EnglishKeys() =>
        // English lives in the neutral set (NeutralResourcesLanguage("en")), keyed by
        // the invariant culture — there is no "en" satellite to read.
        Keys(CultureInfo.InvariantCulture);

    private static HashSet<string> FrenchKeys() => Keys(CultureInfo.GetCultureInfo("fr"));

    [Fact]
    public void French_covers_every_english_key()
    {
        Assert.Empty(EnglishKeys().Except(FrenchKeys()));
    }

    [Fact]
    public void English_covers_every_french_key()
    {
        Assert.Empty(FrenchKeys().Except(EnglishKeys()));
    }

    [Fact]
    public void Catalog_has_expected_key_count()
    {
        Assert.Equal(430, EnglishKeys().Count);
    }
}
