using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>What the platform accepts as a caller's device signature and address before it stores one (FR-26).</summary>
public class ClientInfoTests
{
    [Theory]
    [InlineData("a1b2c3d4e5f60718293a4b5c6d7e8f90")]
    [InlineData("  a1b2c3d4  ")]
    [InlineData("ABCdef0123")]
    public void ASignatureOfLettersAndDigits_IsKept_Trimmed(string value) =>
        Assert.Equal(value.Trim(), ClientInfo.CleanFingerprint(value));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has spaces")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("semi;colon")]
    [InlineData("naïve0123")]
    public void ASignatureNotInTheExpectedForm_IsDropped(string? value) => Assert.Null(ClientInfo.CleanFingerprint(value));

    [Fact]
    public void ASignatureLongerThanTheLimit_IsDropped_AndOneAtTheLimitKept()
    {
        Assert.Null(ClientInfo.CleanFingerprint(new string('a', ClientInfo.MaxFingerprintLength + 1)));
        Assert.NotNull(ClientInfo.CleanFingerprint(new string('a', ClientInfo.MaxFingerprintLength)));
    }

    [Theory]
    [InlineData("203.0.113.10", "203.0.113.10")]
    [InlineData(" 2001:db8::1 ", "2001:db8::1")]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    public void AnAddress_IsTrimmed_AndBlankIsNothing(string? value, string? expected) => Assert.Equal(expected, ClientInfo.CleanIp(value));

    [Fact]
    public void AnAddressLongerThanAnIpv6Address_IsDropped() => Assert.Null(ClientInfo.CleanIp(new string('1', ClientInfo.MaxIpLength + 1)));
}
