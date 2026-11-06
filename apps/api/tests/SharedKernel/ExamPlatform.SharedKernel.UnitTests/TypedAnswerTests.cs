using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// How a typed answer is compared with the accepted answers (a text question). The question bank and the exam runtime both use this rule,
/// so these cases are what keeps a candidate's answer and the author's accepted answers judged the same way.
/// </summary>
public class TypedAnswerTests
{
    [Theory]
    [InlineData("  Paris ", "paris")]
    [InlineData("New   York", "new york")]
    [InlineData("PARIS", "paris")]
    [InlineData(null, "")]
    [InlineData("   ", "")]
    public void Normalize_TrimsCollapsesSpacesAndIgnoresCase(string? typed, string expected) =>
        Assert.Equal(expected, TypedAnswer.Normalize(typed));

    [Fact]
    public void AnAnswerIsRight_WhenItIsOneOfTheAcceptedAnswers_WhateverItsCaseOrSpacing() =>
        Assert.True(TypedAnswer.Matches("  new   york ", new[] { "New York", "NYC" }));

    [Fact]
    public void AnAnswerOutsideTheAcceptedAnswers_IsNotRight() =>
        Assert.False(TypedAnswer.Matches("Boston", new[] { "New York", "NYC" }));

    [Fact]
    public void APartOfAnAcceptedAnswer_IsNotRight() =>
        Assert.False(TypedAnswer.Matches("New", new[] { "New York" }));

    [Fact]
    public void ABlankAnswer_IsNeverRight_EvenAgainstABlankAcceptedAnswer() =>
        Assert.False(TypedAnswer.Matches("   ", new[] { "", " " }));

    [Fact]
    public void WithNoAcceptedAnswers_NothingIsRight() =>
        Assert.False(TypedAnswer.Matches("Paris", Array.Empty<string>()));

    [Fact]
    public void TheExactRule_IsWhatMatchesDidBeforeTheRuleExisted() =>
        Assert.Equal(TypedAnswer.Normalize("  New   York "), TypedAnswer.Normalize("  New   York ", TextMatchRule.Exact));

    [Fact]
    public void TheExactRule_DoesNotForgiveAFullStop() =>
        Assert.False(TypedAnswer.Matches("Paris.", new[] { "Paris" }, TextMatchRule.Exact));
}

/// <summary>
/// What the lenient rule forgives and, as important, what it does not: a rule that forgave "+" or a decimal point would mark a different
/// number or formula right.
/// </summary>
public class LenientTypedAnswerTests
{
    private static bool Same(string typed, string accepted) =>
        TypedAnswer.Matches(typed, new[] { accepted }, TextMatchRule.Lenient);

    [Theory]
    [InlineData("Paris.", "Paris")]
    [InlineData("Paris!", "Paris")]
    [InlineData("\"Paris\"", "Paris")]
    [InlineData("\u201cParis\u201d", "Paris")]
    [InlineData("(Paris)", "Paris")]
    [InlineData("Paris, France", "Paris France")]
    [InlineData("red,green", "red, green")]
    [InlineData("U.S.A.", "USA")]
    [InlineData("u.s.a", "USA")]
    [InlineData("Mr. Smith", "Mr Smith")]
    [InlineData("don't", "dont")]
    [InlineData("don\u2019t", "don't")]
    [InlineData("Newton\u2019s law", "newtons law")]
    [InlineData("well-known", "well known")]
    [InlineData("well\u2013known", "Well-Known")]
    [InlineData("snake_case", "snake case")]
    [InlineData("  NEW   york. ", "new york")]
    public void StrayPunctuationAndHyphensBetweenWords_AreForgiven(string typed, string accepted) =>
        Assert.True(Same(typed, accepted));

    [Theory]
    [InlineData("cafe", "caf\u00e9")]
    [InlineData("caf\u00e9", "cafe")]
    [InlineData("cafe\u0301", "caf\u00e9")]
    [InlineData("Zurich", "Z\u00fcrich")]
    [InlineData("jalapeno", "jalape\u00f1o")]
    [InlineData("Ångström", "angstrom")]
    public void AccentsOnLatinLetters_AreForgiven_HoweverTheyWereTyped(string typed, string accepted) =>
        Assert.True(Same(typed, accepted));

    [Theory]
    [InlineData("3.14", "314")]
    [InlineData("3.14", "3,14")]
    [InlineData("1,000", "1000")]
    [InlineData("-5", "5")]
    [InlineData("x+1", "x-1")]
    [InlineData("x+1", "x1")]
    [InlineData("10%", "10")]
    [InlineData("a/b", "ab")]
    [InlineData("a=b", "ab")]
    [InlineData("x<5", "x>5")]
    [InlineData("$5", "5")]
    [InlineData("x-1", "x 1")]
    public void SignsSymbolsAndDecimalPoints_ThatCanChangeAnAnswer_AreKept(string typed, string accepted) =>
        Assert.False(Same(typed, accepted));

    [Theory]
    [InlineData("Paris", "Lyon")]
    [InlineData("Par", "Paris")]
    [InlineData("Mitosis", "Meiosis")]
    public void ADifferentAnswer_IsStillWrong(string typed, string accepted) =>
        Assert.False(Same(typed, accepted));

    [Fact]
    public void ANumberKeepsItsDecimalPointAndItsMinusSign()
    {
        Assert.Equal("3.14", TypedAnswer.Normalize("3.14.", TextMatchRule.Lenient));
        Assert.Equal("-5", TypedAnswer.Normalize("-5", TextMatchRule.Lenient));
        Assert.Equal("1,000", TypedAnswer.Normalize("1,000", TextMatchRule.Lenient));
    }

    [Fact]
    public void AnAnswerThatIsOnlyPunctuation_IsBlank_AndMatchesNothing()
    {
        Assert.Equal(string.Empty, TypedAnswer.Normalize("?!...", TextMatchRule.Lenient));
        Assert.False(TypedAnswer.Matches("...", new[] { "?" }, TextMatchRule.Lenient));
    }

    [Fact]
    public void LettersOfOtherScripts_AreLeftAsTyped_SoAVowelSignIsNeverStripped()
    {
        // Hindi "India" with and without its vowel sign are different words; accents are only folded on basic Latin letters.
        Assert.Equal("\u092d\u093e\u0930\u0924", TypedAnswer.Normalize("\u092d\u093e\u0930\u0924", TextMatchRule.Lenient));
        Assert.False(Same("\u092d\u0930\u0924", "\u092d\u093e\u0930\u0924"));
    }

    [Fact]
    public void AHalfOfAnEmoji_DoesNotBreakMarking()
    {
        var lone = "Paris \ud83d";

        var result = TypedAnswer.Normalize(lone, TextMatchRule.Lenient);

        Assert.StartsWith("paris", result, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Paris.")]
    [InlineData("U.S.A.")]
    [InlineData("Caf\u00e9 - well-known, isn't it?")]
    [InlineData("x - 1 + 2")]
    [InlineData("a--b")]
    [InlineData("  ")]
    public void NormalizingTwice_ChangesNothing(string text)
    {
        var once = TypedAnswer.Normalize(text, TextMatchRule.Lenient);

        Assert.Equal(once, TypedAnswer.Normalize(once, TextMatchRule.Lenient));
    }

    [Theory]
    [InlineData("  New   York ", "new york")]
    [InlineData("PARIS", "Paris")]
    [InlineData("Caf\u00e9", "caf\u00c9")]
    [InlineData("x+1", "X+1")]
    public void WhatTheExactRuleAccepts_TheLenientRuleAcceptsToo(string typed, string accepted)
    {
        Assert.True(TypedAnswer.Matches(typed, new[] { accepted }, TextMatchRule.Exact));
        Assert.True(TypedAnswer.Matches(typed, new[] { accepted }, TextMatchRule.Lenient));
    }
}
