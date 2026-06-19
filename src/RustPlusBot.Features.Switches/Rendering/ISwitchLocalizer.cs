namespace RustPlusBot.Features.Switches.Rendering;

/// <summary>Resolves localized smart-switch strings by key and culture, falling back to English.</summary>
internal interface ISwitchLocalizer
{
    /// <summary>Gets the localized string for a key, or the key itself if not found.</summary>
    /// <param name="key">The string key.</param>
    /// <param name="culture">The BCP-47 culture tag (e.g. "en", "fr").</param>
    string Get(string key, string culture);

    /// <summary>Gets the localized, format-applied string.</summary>
    /// <param name="key">The string key.</param>
    /// <param name="culture">The BCP-47 culture tag.</param>
    /// <param name="args">Format arguments.</param>
    string Get(string key, string culture, params object[] args);
}
