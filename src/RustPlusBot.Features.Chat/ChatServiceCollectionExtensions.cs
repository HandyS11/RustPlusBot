using Microsoft.Extensions.DependencyInjection;
using RustPlusBot.Abstractions.Chat;
using RustPlusBot.Features.Chat.Hosting;
using RustPlusBot.Features.Chat.Inbound;
using RustPlusBot.Features.Chat.Relaying;
using RustPlusBot.Features.Chat.Webhooks;

namespace RustPlusBot.Features.Chat;

/// <summary>DI registration for the chat bridge (team and clan chat).</summary>
public static class ChatServiceCollectionExtensions
{
    /// <summary>Registers the dedup buffer, webhook poster, relay, inbound processor, and hosted service.</summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddChat(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<RelayDedupBuffer>();
        services.AddSingleton<IChatWebhookPoster, DiscordChatWebhookPoster>();
        services.AddSingleton<ChatRelay>();
        services.AddSingleton<ChatInboundProcessor>();
        services.AddHostedService<ChatHostedService>();

        return services;
    }
}
