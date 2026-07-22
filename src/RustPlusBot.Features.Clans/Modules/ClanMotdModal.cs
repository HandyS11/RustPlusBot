using Discord;
using Discord.Interactions;

namespace RustPlusBot.Features.Clans.Modules;

/// <summary>The modal that collects a new clan MOTD. Handled by <see cref="ClanMotdModule"/>.</summary>
public sealed class ClanMotdModal : IModal
{
    /// <summary>The new message of the day.</summary>
    [InputLabel("Message of the day")]
    [ModalTextInput(ClanComponentIds.MotdInputId, TextInputStyle.Paragraph, maxLength: 1024)]
    public string Motd { get; set; } = string.Empty;

    /// <inheritdoc />
    public string Title => "Set clan MOTD";
}
