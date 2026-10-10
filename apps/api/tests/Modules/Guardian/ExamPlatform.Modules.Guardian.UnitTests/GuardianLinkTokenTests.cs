using ExamPlatform.Modules.Guardian.Domain;

namespace ExamPlatform.Modules.Guardian.UnitTests;

public class GuardianLinkTokenTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Hash_IsTheSha256OfTheCode_asLowercaseHex()
    {
        // The SHA-256 test vector for "abc" (FIPS 180-2, appendix B.1).
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", GuardianLinkToken.Hash("abc"));
    }

    [Fact]
    public void Issue_GivesAUrlSafeCode_whoseHashIsWhatIsStored()
    {
        var issued = GuardianLinkToken.Issue(Now);

        Assert.DoesNotContain('+', issued.Raw);
        Assert.DoesNotContain('/', issued.Raw);
        Assert.DoesNotContain('=', issued.Raw);
        Assert.Equal(GuardianLinkToken.Hash(issued.Raw), issued.Hash);
        Assert.Matches("^[0-9a-f]{64}$", issued.Hash);
    }

    [Fact]
    public void Issue_ExpiresAfterTheLifetime()
    {
        var issued = GuardianLinkToken.Issue(Now);

        Assert.Equal(Now + GuardianLinkToken.Lifetime, issued.ExpiresAtUtc);
    }

    [Fact]
    public void Issue_GivesADifferentCodeEachTime()
    {
        Assert.NotEqual(GuardianLinkToken.Issue(Now).Raw, GuardianLinkToken.Issue(Now).Raw);
    }
}
