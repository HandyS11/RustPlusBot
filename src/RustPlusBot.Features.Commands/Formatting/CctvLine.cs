using System.Globalization;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.Features.Commands.Formatting;

/// <summary>Formats a monument's CCTV reply: a header line then one code per line (source order).</summary>
internal static class CctvLine
{
    /// <summary>Lists a monument's camera codes under a header. The wildcard note is added by the
    /// caller (it is localized and surface-specific), not here.</summary>
    /// <param name="monument">The monument (with at least one code).</param>
    public static string Format(CctvMonument monument)
    {
        ArgumentNullException.ThrowIfNull(monument);
        return string.Create(CultureInfo.InvariantCulture,
            $"{monument.Name} CCTV:\n{string.Join("\n", monument.Codes)}");
    }
}
