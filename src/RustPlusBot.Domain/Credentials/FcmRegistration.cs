using Persistord.Core.Abstractions;

namespace RustPlusBot.Domain.Credentials;

/// <summary>
/// One Discord user's Rust+ FCM listener registration within a guild. One per (GuildId, OwnerUserId).
/// The credentials blob is stored protected at rest (see ICredentialProtector) and feeds the pairing listener.
/// </summary>
public sealed class FcmRegistration : IUpdatedAt
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The owning Discord guild snowflake.</summary>
    public ulong GuildId { get; set; }

    /// <summary>The Discord user who connected these credentials.</summary>
    public ulong OwnerUserId { get; set; }

    /// <summary>Protected FCM/Expo credentials JSON.</summary>
    public string ProtectedFcmCredentials { get; set; } = string.Empty;

    /// <summary>Listener lifecycle state.</summary>
    public FcmRegistrationStatus Status { get; set; } = FcmRegistrationStatus.Active;

    /// <summary>When the registration was last written (UTC). Stamped by Persistord's TimestampInterceptor.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
