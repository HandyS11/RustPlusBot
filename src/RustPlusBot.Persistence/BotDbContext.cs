using Microsoft.EntityFrameworkCore;
using Persistord.Core;
using RustPlusBot.Domain.Connections;
using RustPlusBot.Domain.Credentials;
using RustPlusBot.Domain.Entities;
using RustPlusBot.Domain.Events;
using RustPlusBot.Domain.Guilds;
using RustPlusBot.Domain.Servers;
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

    /// <summary>Per-server connection state.</summary>
    public DbSet<ConnectionState> ConnectionStates => Set<ConnectionState>();

    /// <summary>Per-guild settings.</summary>
    public DbSet<GuildSettings> GuildSettings => Set<GuildSettings>();

    /// <summary>Channel-to-feature bindings.</summary>
    public DbSet<ChannelBinding> ChannelBindings => Set<ChannelBinding>();

    /// <summary>Paired smart devices.</summary>
    public DbSet<PairedEntity> PairedEntities => Set<PairedEntity>();

    /// <summary>Per-guild event subscriptions.</summary>
    public DbSet<EventSubscription> EventSubscriptions => Set<EventSubscription>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder); // core skeleton + snowflake convention

        modelBuilder
            .ApplyConfiguration(new RustServerConfiguration())
            .ApplyConfiguration(new PlayerCredentialConfiguration())
            .ApplyConfiguration(new ChannelBindingConfiguration())
            .ApplyConfiguration(new ConnectionStateConfiguration())
            .ApplyConfiguration(new GuildSettingsConfiguration());
    }
}
