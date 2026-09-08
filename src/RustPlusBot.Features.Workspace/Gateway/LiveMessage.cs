namespace RustPlusBot.Features.Workspace.Gateway;

/// <summary>A provisioned message as it currently exists in Discord.</summary>
/// <param name="Id">The message snowflake.</param>
/// <param name="AttachmentFileName">The file name of the upload it carries, or null when it has none.</param>
internal sealed record LiveMessage(ulong Id, string? AttachmentFileName)
{
    private const string AttachmentPathMarker = "/attachments/";

    /// <summary>
    /// Builds the live view from what Discord reports, resolving where the upload's name actually lives.
    /// </summary>
    /// <remarks>
    /// An attachment referenced by an embed through <c>attachment://</c> is folded INTO that embed: Discord
    /// rewrites the embed's image URL to the CDN one and returns an EMPTY attachments array. Reading only
    /// the array therefore reports "no attachment" for exactly the messages that have one, which made the
    /// reconciler treat every pass as an image change and repost (a fresh upload every reconcile).
    /// </remarks>
    /// <param name="id">The message snowflake.</param>
    /// <param name="attachmentFileName">The first entry of the message's attachments array, if any.</param>
    /// <param name="embedImageUrl">The first embed's image URL, if any.</param>
    /// <returns>The live message with the upload's file name resolved from either place.</returns>
    public static LiveMessage From(ulong id, string? attachmentFileName, string? embedImageUrl) =>
        new(id, attachmentFileName ?? FileNameFromCdnUrl(embedImageUrl));

    /// <summary>Extracts the file name from a Discord attachment CDN URL; null for any other URL.</summary>
    /// <param name="url">The embed image URL.</param>
    /// <returns>The uploaded file's name, or null when the URL is not a Discord attachment.</returns>
    private static string? FileNameFromCdnUrl(string? url)
    {
        if (url?.Contains(AttachmentPathMarker, StringComparison.Ordinal) != true)
        {
            return null;
        }

        var path = url.Split('?', 2)[0];
        var name = path[(path.LastIndexOf('/') + 1)..];
        return name.Length == 0 ? null : name;
    }
}
