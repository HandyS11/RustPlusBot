using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Features.Chat.Webhooks;

namespace RustPlusBot.Features.Chat.Tests;

public sealed class DiscordChatWebhookPosterTests
{
    [Fact]
    public void Uses_the_clan_webhook_name_for_clan_lines()
    {
        // The poster re-discovers its webhook by name on restart, so these strings are load-bearing:
        // changing one orphans every webhook already provisioned in live guilds.
        Assert.Equal("RustPlusBot ClanChat", DiscordChatWebhookPoster.WebhookNameFor(ChatChannelKind.Clan));
        Assert.Equal("RustPlusBot TeamChat", DiscordChatWebhookPoster.WebhookNameFor(ChatChannelKind.Team));
    }
}
