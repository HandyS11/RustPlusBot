using Microsoft.EntityFrameworkCore;
using Persistord.Core;
using RustPlusBot.Domain.Commands;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Entities;
using RustPlusBot.Domain.Events;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Map;
using RustPlusBot.Domain.Servers;
using RustPlusBot.Domain.Workspace;
using RustPlusBot.Persistence.Configurations;

namespace RustPlusBot.Persistence;

/// <summary>
/// The bot's EF Core context. Inherits Persistord's DiscordDbContext for the Discord skeleton and
/// the global ulong&lt;-&gt;long snowflake conversion, and adds the Rust-domain sets.
/// </summary>
/// <param name="options">The EF Core options, typically configured with a specific provider (e.g. SQLite, PostgreSQL).</param>
public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DiscordDbContext(options)
{
    /// <summary>Configured Rust+ servers.</summary>
    public DbSet<RustServer> RustServers => Set<RustServer>();

    /// <summary>Stored player credentials.</summary>
    public DbSet<PlayerCredential> PlayerCredentials => Set<PlayerCredential>();

    /// <summary>Per-user FCM listener registrations.</summary>
    public DbSet<FcmRegistration> FcmRegistrations => Set<FcmRegistration>();

    /// <summary>Per-server connection state.</summary>
    public DbSet<ConnectionState> ConnectionStates => Set<ConnectionState>();

    /// <summary>Per-server command settings (trigger prefix and mute state).</summary>
    public DbSet<ServerCommandSettings> ServerCommandSettings => Set<ServerCommandSettings>();

    /// <summary>Per-(guild, server) rendered-map layer settings.</summary>
    public DbSet<ServerMapSettings> ServerMapSettings => Set<ServerMapSettings>();

    /// <summary>Per-guild settings.</summary>
    public DbSet<GuildSettings> GuildSettings => Set<GuildSettings>();

    /// <summary>Paired smart devices.</summary>
    public DbSet<PairedEntity> PairedEntities => Set<PairedEntity>();

    /// <summary>Per-guild event subscriptions.</summary>
    public DbSet<EventSubscription> EventSubscriptions => Set<EventSubscription>();

    /// <summary>Provisioned Discord categories (global + per-server).</summary>
    public DbSet<ProvisionedCategory> ProvisionedCategories => Set<ProvisionedCategory>();

    /// <summary>Provisioned Discord channels, keyed by spec key.</summary>
    public DbSet<ProvisionedChannel> ProvisionedChannels => Set<ProvisionedChannel>();

    /// <summary>Anchored bot messages, edited in place.</summary>
    public DbSet<ProvisionedMessage> ProvisionedMessages => Set<ProvisionedMessage>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder); // core skeleton + snowflake convention

        modelBuilder
            .ApplyConfiguration(new RustServerConfiguration())
            .ApplyConfiguration(new PlayerCredentialConfiguration())
            .ApplyConfiguration(new FcmRegistrationConfiguration())
            .ApplyConfiguration(new ConnectionStateConfiguration())
            .ApplyConfiguration(new ServerCommandSettingsConfiguration())
            .ApplyConfiguration(new ServerMapSettingsConfiguration())
            .ApplyConfiguration(new GuildSettingsConfiguration())
            .ApplyConfiguration(new PairedEntityConfiguration())
            .ApplyConfiguration(new EventSubscriptionConfiguration())
            .ApplyConfiguration(new ProvisionedCategoryConfiguration())
            .ApplyConfiguration(new ProvisionedChannelConfiguration())
            .ApplyConfiguration(new ProvisionedMessageConfiguration());
    }
}
