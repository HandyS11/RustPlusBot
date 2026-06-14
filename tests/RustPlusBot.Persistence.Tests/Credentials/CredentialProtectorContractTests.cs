using Microsoft.AspNetCore.DataProtection;
using RustPlusBot.Abstractions.Credentials;
using RustPlusBot.Host.Credentials;

namespace RustPlusBot.Persistence.Tests.Credentials;

public sealed class CredentialProtectorContractTests
{
    [Fact]
    public void ProtectThenUnprotect_ReturnsOriginal()
    {
        // DataProtectionProvider.Create() was removed in net10; EphemeralDataProtectionProvider
        // is the correct in-process, no-key-ring equivalent for tests.
        var provider = new EphemeralDataProtectionProvider();
        ICredentialProtector protector = new DataProtectionCredentialProtector(provider);

        const string secret = "player-token-12345";
        var protectedText = protector.Protect(secret);

        Assert.NotEqual(secret, protectedText);
        Assert.Equal(secret, protector.Unprotect(protectedText));
    }
}
