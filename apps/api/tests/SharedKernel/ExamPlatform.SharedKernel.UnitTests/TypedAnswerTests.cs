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
}
