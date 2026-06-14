using Microsoft.AspNetCore.DataProtection;
using RustPlusBot.Abstractions.Credentials;

namespace RustPlusBot.Host.Credentials;

/// <summary>An <see cref="ICredentialProtector"/> backed by ASP.NET Core Data Protection.</summary>
internal sealed class DataProtectionCredentialProtector : ICredentialProtector
{
    private readonly IDataProtector _protector;

    /// <summary>Creates the protector from a Data Protection provider.</summary>
    /// <param name="provider">The Data Protection provider supplying the protector.</param>
    public DataProtectionCredentialProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector("RustPlusBot.Credentials.v1");
    }

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        return _protector.Protect(plaintext);
    }

    /// <inheritdoc />
    public string Unprotect(string protectedText)
    {
        ArgumentNullException.ThrowIfNull(protectedText);
        return _protector.Unprotect(protectedText);
    }
}
