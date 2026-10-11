using ExamPlatform.Modules.Admin.Domain;
using ExamPlatform.Modules.Admin.Domain.Exceptions;

namespace ExamPlatform.Modules.Admin.UnitTests;

public class InstituteBrandingTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static byte[] Jpeg() => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    private static byte[] Webp() => [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8];

    [Fact]
    public void Describe_TrimsTheNameAndWritesTheColourInUpperCase()
    {
        var branding = InstituteBranding.Create(Now);

        branding.Describe("  Riverside Academy  ", " #1a56db ", Now);

        Assert.Equal("Riverside Academy", branding.InstituteName);
        Assert.Equal("#1A56DB", branding.PrimaryColour);
    }

    [Fact]
    public void Describe_WithBlankValues_ClearsBothBackToTheDefaultLook()
    {
        var branding = InstituteBranding.Create(Now);
        branding.Describe("Riverside Academy", "#1A56DB", Now);

        branding.Describe("   ", null, Now.AddMinutes(1));

        Assert.Null(branding.InstituteName);
        Assert.Null(branding.PrimaryColour);
    }

    [Fact]
    public void Describe_WithANameOverTheLimit_Throws()
    {
        var branding = InstituteBranding.Create(Now);

        Assert.Throws<InvalidBrandingError>(() =>
            branding.Describe(new string('x', InstituteBranding.MaxNameLength + 1), null, Now));
    }

    [Theory]
    [InlineData("#1AD")]
    [InlineData("1A56DB")]
    [InlineData("#1A56DBFF")]
    [InlineData("#GGGGGG")]
    [InlineData("red")]
    public void Describe_WithAColourThatIsNotSixDigitHex_Throws(string colour)
    {
        // Only the six-digit form renders the same in every browser, so anything else is refused rather than guessed at.
        var branding = InstituteBranding.Create(Now);

        Assert.Throws<InvalidBrandingError>(() => branding.Describe("Riverside", colour, Now));
    }

    [Fact]
    public void Describe_WhenRejected_KeepsTheValuesItHad()
    {
        var branding = InstituteBranding.Create(Now);
        branding.Describe("Riverside Academy", "#1A56DB", Now);

        Assert.Throws<InvalidBrandingError>(() => branding.Describe("New Name", "not-a-colour", Now.AddMinutes(1)));

        Assert.Equal("Riverside Academy", branding.InstituteName);
        Assert.Equal("#1A56DB", branding.PrimaryColour);
        Assert.Equal(Now, branding.UpdatedAtUtc);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public void SetLogo_AcceptsPngJpegAndWebp_AndRecordsTheMediaType(string expectedType)
    {
        var content = expectedType switch
        {
            "image/png" => Png(),
            "image/jpeg" => Jpeg(),
            _ => Webp(),
        };
        var branding = InstituteBranding.Create(Now);

        branding.SetLogo(content, Now);

        Assert.True(branding.HasLogo);
        Assert.Equal(expectedType, branding.LogoContentType);
        Assert.Equal(content, branding.Logo);
    }

    [Theory]
    [InlineData("GIF89a")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")]
    [InlineData("RIFF$\u0000\u0000\u0000WAVEfmt ")]
    [InlineData("%PDF-1.7")]
    public void SetLogo_RejectsAnythingThatIsNotAnAcceptedFormat(string text)
    {
        // The format comes from the bytes, so a file renamed to .png, or sent as image/png, is still refused unless it really is one.
        var branding = InstituteBranding.Create(Now);

        Assert.Throws<UnsupportedLogoTypeError>(() => branding.SetLogo(System.Text.Encoding.ASCII.GetBytes(text), Now));
        Assert.False(branding.HasLogo);
    }

    [Fact]
    public void SetLogo_RejectsAnEmptyFile()
    {
        var branding = InstituteBranding.Create(Now);

        Assert.Throws<UnsupportedLogoTypeError>(() => branding.SetLogo([], Now));
    }

    [Fact]
    public void SetLogo_AcceptsAFileExactlyAtTheLimit()
    {
        var content = new byte[LogoImage.MaxBytes];
        Png().CopyTo(content, 0);
        var branding = InstituteBranding.Create(Now);

        branding.SetLogo(content, Now);

        Assert.True(branding.HasLogo);
    }

    [Fact]
    public void SetLogo_RejectsAFileOverTheLimit_EvenWhenItIsAPng()
    {
        var content = new byte[LogoImage.MaxBytes + 1];
        Png().CopyTo(content, 0);
        var branding = InstituteBranding.Create(Now);

        Assert.Throws<LogoTooLargeError>(() => branding.SetLogo(content, Now));
    }

    [Fact]
    public void RemoveLogo_ClearsTheLogo()
    {
        var branding = InstituteBranding.Create(Now);
        branding.SetLogo(Png(), Now);

        branding.RemoveLogo(Now.AddMinutes(1));

        Assert.False(branding.HasLogo);
        Assert.Null(branding.LogoContentType);
        Assert.Equal(Now.AddMinutes(1), branding.UpdatedAtUtc);
    }

    [Fact]
    public void RemoveLogo_WithNoLogo_ChangesNothing()
    {
        var branding = InstituteBranding.Create(Now);

        branding.RemoveLogo(Now.AddMinutes(1));

        Assert.Equal(Now, branding.UpdatedAtUtc);
    }

    [Fact]
    public void ContentTypeOf_NamesEachAcceptedFormat()
    {
        Assert.Equal("image/png", LogoImage.ContentTypeOf(LogoFormat.Png));
        Assert.Equal("image/jpeg", LogoImage.ContentTypeOf(LogoFormat.Jpeg));
        Assert.Equal("image/webp", LogoImage.ContentTypeOf(LogoFormat.Webp));
    }
}
