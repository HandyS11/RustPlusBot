using System.Text.Json;
using RustPlusBot.Features.ItemData.Data;

namespace RustPlusBot.ItemData.Generator.Sources;

/// <summary>Reads monument CCTV codes from the offline <c>cctv.json</c> file, keyed by monument name.</summary>
/// <param name="CctvFilePath">Path to the <c>cctv.json</c> file.</param>
internal sealed class OfflineCctvSource(string CctvFilePath) : ICctvSource
{
    /// <inheritdoc/>
    public IReadOnlyList<CctvMonument> LoadMonuments()
    {
        using var stream = File.OpenRead(CctvFilePath);
        using var doc = JsonDocument.Parse(stream);

        var monuments = new List<CctvMonument>();
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var codes = ReadCodes(prop.Value);
            if (codes.Count == 0)
            {
                continue; // monument with no codes — drop
            }

            var dynamic = prop.Value.TryGetProperty("dynamic", out var d) && d.ValueKind == JsonValueKind.True;
            monuments.Add(new CctvMonument(prop.Name, codes, dynamic));
        }

        return monuments;
    }

    private static List<string> ReadCodes(JsonElement entry)
    {
        var codes = new List<string>();
        if (!entry.TryGetProperty("codes", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return codes;
        }

        foreach (var element in arr.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } raw)
            {
                codes.Add(Unescape(raw));
            }
        }

        return codes;
    }

    /// <summary>Strips the markdown asterisk-escape used in cctv.json (<c>COMPOUND\*\*…</c> → <c>COMPOUND**…</c>).</summary>
    /// <param name="code">The raw camera code string from the JSON.</param>
    private static string Unescape(string code) => code.Replace("\\*", "*", StringComparison.Ordinal);
}
