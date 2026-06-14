namespace RustPlusBot.Abstractions.Credentials;

/// <summary>Protects secret credential material at rest (round-trippable on the same machine/key).</summary>
public interface ICredentialProtector
{
    /// <summary>Protects plaintext into an opaque, storable string.</summary>
    /// <param name="plaintext">The secret to protect.</param>
    /// <returns>An opaque, storable representation.</returns>
    string Protect(string plaintext);

    /// <summary>Reverses <see cref="Protect"/>.</summary>
    /// <param name="protectedText">A value previously produced by <see cref="Protect"/>.</param>
    /// <returns>The original plaintext.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The payload was tampered with, produced by a different key/purpose, or otherwise unreadable.
    /// </exception>
    string Unprotect(string protectedText);
}
