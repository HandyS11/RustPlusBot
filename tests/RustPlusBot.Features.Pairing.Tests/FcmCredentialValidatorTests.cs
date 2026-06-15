using RustPlusBot.Features.Pairing.Validation;

namespace RustPlusBot.Features.Pairing.Tests;

public sealed class FcmCredentialValidatorTests
{
    [Theory]
    [InlineData("{\"fcm\":{\"token\":\"abc\"}}")]
    [InlineData("   {\"a\":1}   ")]
    public void IsWellFormed_True_ForJsonObject(string json) =>
        Assert.True(FcmCredentialValidator.IsWellFormed(json));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("42")]
    public void IsWellFormed_False_ForNonObjectOrGarbage(string? json) =>
        Assert.False(FcmCredentialValidator.IsWellFormed(json));
}
