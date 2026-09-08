using Discord;

namespace RustPlusBot.Features.Workspace.Gateway;

/// <summary>A renderable message: optional text, embed, components, and an image attachment.</summary>
/// <param name="Text">Plain content, or null.</param>
/// <param name="Embed">An embed, or null.</param>
/// <param name="Components">Message components (buttons/selects), or null.</param>
/// <param name="Attachment">
/// A file to upload with the message, or null. Attachments are posted once and then left alone: the
/// reconciler re-renders every pass, so re-uploading the file each time would push hundreds of kilobytes
/// per server per pass. A payload whose attachment file name differs from the live message's is treated as
/// a new image, and the message is reposted.
/// </param>
public sealed record MessagePayload(
    string? Text,
    Embed? Embed,
    MessageComponent? Components,
    MessageAttachment? Attachment = null);

/// <summary>A file uploaded alongside a message; an embed can show it via <c>attachment://{FileName}</c>.</summary>
/// <param name="Bytes">The encoded file bytes.</param>
/// <param name="FileName">
/// The upload file name. It doubles as the attachment's identity: name it after the content (a hash, say)
/// so the reconciler can tell "already posted" from "the image changed" without re-uploading to compare.
/// </param>
public sealed record MessageAttachment(byte[] Bytes, string FileName);
