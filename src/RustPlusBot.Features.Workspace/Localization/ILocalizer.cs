namespace RustPlusBot.Features.Workspace.Localization;

/// <summary>Resolves localized strings by key and BCP-47 culture, falling back to English.</summary>
internal interface ILocalizer
{
    /// <summary>Gets the localized string for a key, or the key itself if not found.</summary>
    /// <param name="key">The string key to resolve.</param>
    /// <param name="culture">The BCP-47 culture tag (e.g. "en", "fr").</param>
    string Get(string key, string culture);

    /// <summary>Gets the localized, <see cref="string.Format(IFormatProvider, string, object?[])"/>-applied string.</summary>
    /// <param name="key">The string key to resolve.</param>
    /// <param name="culture">The BCP-47 culture tag (e.g. "en", "fr").</param>
    /// <param name="args">Format arguments.</param>
    string Get(string key, string culture, params object[] args);
}
