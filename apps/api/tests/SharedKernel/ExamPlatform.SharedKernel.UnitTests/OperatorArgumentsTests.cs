using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>Reading the <c>--name value</c> pairs of an operator command's command line.</summary>
public class OperatorArgumentsTests
{
    [Fact]
    public void ValueOf_ReturnsTheArgumentAfterTheFlag()
    {
        Assert.Equal("5", OperatorArguments.ValueOf(["--cmd", "--count", "5", "--other", "x"], "--count"));
    }

    [Fact]
    public void ValueOf_WhenTheFlagIsAbsent_IsNull()
    {
        Assert.Null(OperatorArguments.ValueOf(["--cmd", "--other", "x"], "--count"));
    }

    [Fact]
    public void ValueOf_WhenTheFlagIsLast_IsNull()
    {
        Assert.Null(OperatorArguments.ValueOf(["--cmd", "--count"], "--count"));
    }

    [Fact]
    public void ValueOf_WhenAnotherFlagFollows_IsNull_SoAFlagIsNeverReadAsAValue()
    {
        Assert.Null(OperatorArguments.ValueOf(["--count", "--email-pattern", "p-{n}@x.invalid"], "--count"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValueOf_WhenTheValueIsBlank_IsNull(string blank)
    {
        Assert.Null(OperatorArguments.ValueOf(["--count", blank], "--count"));
    }

    [Fact]
    public void ValueOf_MatchesTheFlagExactly()
    {
        Assert.Null(OperatorArguments.ValueOf(["--COUNT", "5"], "--count"));
        Assert.Null(OperatorArguments.ValueOf(["--counter", "5"], "--count"));
    }
}
