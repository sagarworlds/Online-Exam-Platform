using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>Reading the languages a caller asked for from <c>Accept-Language</c> (FR-51).</summary>
public class RequestLanguageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("*")]
    public void NoHeaderOrAWildcard_IsNoPreference(string? header) => Assert.Empty(RequestLanguage.Parse(header));

    [Fact]
    public void ALanguageWithARegion_IsReadAsTheLanguage_BecauseContentIsTranslatedPerLanguage() =>
        Assert.Equal(["hi"], RequestLanguage.Parse("hi-IN"));

    [Fact]
    public void LanguagesAreOrderedByQuality_AndEquallyRankedOnesKeepTheOrderTheyWereWritten() =>
        Assert.Equal(["mr", "hi", "en"], RequestLanguage.Parse("en;q=0.5, mr, hi;q=0.9"));

    [Fact]
    public void ARegionVariantOfALanguageAlreadyListed_IsNotRepeated() =>
        Assert.Equal(["en", "hi"], RequestLanguage.Parse("en-US,en;q=0.9,hi;q=0.8,en-GB;q=0.7"));

    [Fact]
    public void ALanguageRankedAtZero_IsOneTheClientDoesNotWant() =>
        Assert.Equal(["en"], RequestLanguage.Parse("hi;q=0, en;q=0.4"));

    [Theory]
    [InlineData("h1")]
    [InlineData("hindi-language-name-that-is-too-long")]
    [InlineData("hi;q=banana")]
    [InlineData("<script>")]
    public void SomethingThatIsNotALanguageTag_IsLeftOut(string header) => Assert.Empty(RequestLanguage.Parse(header));

    [Fact]
    public void OnlyTheFirstFewLanguagesAreKept() =>
        Assert.Equal(RequestLanguage.MaxLanguages, RequestLanguage.Parse("aa,bb,cc,dd,ee,ff,gg,hh,ii,jj").Count);

    [Fact]
    public void AHeaderLongerThanAnyRealOne_IsIgnored() =>
        Assert.Empty(RequestLanguage.Parse(new string('a', RequestLanguage.MaxHeaderLength + 1)));
}
