using Discord;
using Discord.Interactions;

namespace RustPlusBot.Features.Pairing.Modules;

/// <summary>The ephemeral modal that collects a user's FCM credentials JSON.</summary>
public sealed class ConnectModal : IModal
{
    /// <summary>Custom id of the modal, handled by <see cref="CredentialModule"/>.</summary>
    public const string ModalId = "pairing:connect:modal";

    /// <summary>Custom id of the credentials text input.</summary>
    public const string CredentialsInputId = "pairing:connect:credentials";

    /// <inheritdoc />
    public string Title => "Connect your Rust+ account";

    /// <summary>The pasted FCM credentials JSON.</summary>
    [InputLabel("FCM credentials JSON")]
    [ModalTextInput(CredentialsInputId, TextInputStyle.Paragraph, placeholder: "Paste the JSON from the credentials helper")]
    public string Credentials { get; set; } = string.Empty;
}
