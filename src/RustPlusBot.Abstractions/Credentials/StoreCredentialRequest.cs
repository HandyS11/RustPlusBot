namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Plaintext inputs to register a credential; tokens are protected by the store before persistence.</summary>
/// <param name="GuildId">Owning Discord guild snowflake.</param>
/// <param name="RustServerId">The server this credential connects to.</param>
/// <param name="OwnerUserId">The Discord user registering the credential.</param>
/// <param name="SteamId">The player's Steam64 id.</param>
/// <param name="PlayerToken">The plaintext Rust+ player token (protected before storage).</param>
/// <param name="FcmCredentialsJson">The plaintext FCM/Expo credential JSON (protected before storage).</param>
public sealed record StoreCredentialRequest(
    ulong GuildId,
    Guid RustServerId,
    ulong OwnerUserId,
    ulong SteamId,
    string PlayerToken,
    string FcmCredentialsJson);
