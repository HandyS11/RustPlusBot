using System.Text.Json;

namespace RustPlusBot.Features.Pairing.Validation;

/// <summary>
/// A cheap structural guard against pasted garbage: confirms the blob is a JSON object. The authoritative
/// check is the live connect probe (a malformed-but-objecty blob is rejected by FCM at connect time).
/// </summary>
internal static class FcmCredentialValidator
{
    /// <summary>True when <paramref name="json"/> is non-empty and parses as a JSON object.</summary>
    /// <param name="json">The pasted credentials blob.</param>
    /// <returns>Whether it is structurally usable.</returns>
    public static bool IsWellFormed(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
