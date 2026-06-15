namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Plaintext inputs to upsert a per-server player credential from a pairing; the token is protected before persistence.</summary>
/// <param name="GuildId">Owning Discord guild snowflake.</param>
/// <param name="RustServerId">The server this credential connects to.</param>
/// <param name="OwnerUserId">The Discord user who owns the paired identity.</param>
/// <param name="SteamId">The player's Steam64 id (from the pairing notification).</param>
/// <param name="PlayerToken">The plaintext Rust+ player token (protected before storage).</param>
public sealed record StoreCredentialRequest(
    ulong GuildId,
    Guid RustServerId,
    ulong OwnerUserId,
    ulong SteamId,
    string PlayerToken);
