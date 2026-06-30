using RustPlusBot.Localization;

namespace RustPlusBot.Features.Switches.Tests.Fakes;

/// <summary>Test double: returns the key unchanged, enabling key-based assertions without depending on real resource strings.</summary>
internal sealed class KeyEchoLocalizer : ILocalizer
{
    /// <inheritdoc />
    public string Get(string key, string culture) => key;

    /// <inheritdoc />
    public string Get(string key, string culture, params object[] args) => key;
}
