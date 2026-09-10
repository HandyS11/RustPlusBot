using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Credentials;

/// <summary>
/// One player's Rust+ credentials within a server's pool. Many per (GuildId, RustServerId).
/// The token fields are stored protected at rest (see ICredentialProtector).
/// </summary>
public sealed class PlayerCredential : IGuildScoped
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The server this credential can connect to.</summary>
    public Guid RustServerId { get; set; }

    /// <summary>The Discord user who registered this credential.</summary>
    public ulong OwnerUserId { get; set; }

    /// <summary>The player's Steam64 id.</summary>
    public ulong SteamId { get; set; }

    /// <summary>Protected Rust+ player token.</summary>
    public string ProtectedPlayerToken { get; set; } = string.Empty;

    /// <summary>Pool lifecycle state.</summary>
    public CredentialStatus Status { get; set; } = CredentialStatus.Standby;

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }
}
