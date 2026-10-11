using ExamPlatform.Modules.Analytics.Domain;

namespace ExamPlatform.Modules.Analytics.UnitTests;

/// <summary>The difficulty and discrimination indices of a question (FR-37), and the groups they compare.</summary>
public class ItemStatisticsTests
{
    /// <summary>A candidate id that sorts in the same order as its number, so the tie-break is easy to reason about.</summary>
    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    /// <summary>Candidates 1 to <paramref name="n"/>, where candidate i scored i, so the top of the cohort is the highest numbers.</summary>
    private static List<CandidateScore> Cohort(int n) => Enumerable.Range(1, n).Select(i => new CandidateScore(Id(i), i)).ToList();

    private static ItemAnswer Answer(int n, bool correct) => new(Id(n), correct);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(4, 1)]
    [InlineData(10, 3)]
    [InlineData(100, 27)]
    public void GroupSize_IsTheShareOfTheCohort_AtLeastOneOnceTwoCandidatesCanBeCompared(int cohortSize, int expected)
    {
        Assert.Equal(expected, ItemStatistics.GroupSize(cohortSize));
    }

    [Fact]
    public void GroupsOf_TakesTheTopAndBottomShareOfTheCohortByScore()
    {
        var groups = ItemStatistics.GroupsOf(Cohort(10));

        Assert.True(groups.Upper.SetEquals(new[] { Id(10), Id(9), Id(8) }));
        Assert.True(groups.Lower.SetEquals(new[] { Id(1), Id(2), Id(3) }));
    }

    [Fact]
    public void GroupsOf_DoesNotDependOnTheOrderTheCandidatesCameIn()
    {
        // Two candidates on the same score at the edge of a group: the split must be the same whichever order they arrive in.
        var tied = new List<CandidateScore>
        {
            new(Id(1), 5), new(Id(2), 5), new(Id(3), 5), new(Id(4), 1), new(Id(5), 9), new(Id(6), 0), new(Id(7), 9), new(Id(8), 2), new(Id(9), 3), new(Id(10), 8),
        };

        var first = ItemStatistics.GroupsOf(tied);
        var second = ItemStatistics.GroupsOf(tied.AsEnumerable().Reverse().ToList());

        Assert.True(first.Upper.SetEquals(second.Upper));
        Assert.True(first.Lower.SetEquals(second.Lower));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(50)]
    public void GroupsOf_NeverShareACandidate_SoTheTwoGroupsCanBeCompared(int cohortSize)
    {
        var groups = ItemStatistics.GroupsOf(Cohort(cohortSize));

        Assert.Empty(groups.Upper.Intersect(groups.Lower));
    }

    [Fact]
    public void GroupsOf_IsEmpty_WhenThereAreFewerThanTwoCandidates()
    {
        var groups = ItemStatistics.GroupsOf(Cohort(1));

        Assert.Empty(groups.Upper);
        Assert.Empty(groups.Lower);
    }

    [Fact]
    public void Difficulty_IsTheShareCorrect_AnUnansweredQuestionCountingAsWrong()
    {
        // Ten candidates had the question; six answered it correctly and four did not answer it at all. Four unanswered are four wrong.
        var answers = Enumerable.Range(1, 10).Select(i => Answer(i, correct: i <= 6)).ToList();

        var indices = ItemStatistics.IndicesOf(ItemStatistics.GroupsOf(Cohort(10)), answers, minimumCohortSize: 10);

        Assert.Equal(10, indices.Attempts);
        Assert.Equal(6, indices.CorrectCount);
        Assert.Equal(0.6m, indices.Difficulty);
    }

    [Fact]
    public void Discrimination_IsTheUpperGroupsShareCorrectLessTheLowerGroupsShare()
    {
        // The top three all answer correctly, the bottom three all wrongly, and the middle four are wrong: difficulty 3 in 10, discrimination 1.
        var answers = Enumerable.Range(1, 10).Select(i => Answer(i, correct: i >= 8)).ToList();

        var indices = ItemStatistics.IndicesOf(ItemStatistics.GroupsOf(Cohort(10)), answers, minimumCohortSize: 10);

        Assert.Equal(0.3m, indices.Difficulty);
        Assert.Equal(1m, indices.Discrimination);
    }

    [Fact]
    public void Discrimination_IsRoundedToFourDecimals()
    {
        // Upper: two of three correct (2/3). Lower: one of three correct (1/3). The difference is 1/3, shown to four places.
        var answers = new List<ItemAnswer>
        {
            Answer(10, true), Answer(9, true), Answer(8, false),
            Answer(3, true), Answer(2, false), Answer(1, false),
            Answer(5, false), Answer(6, false), Answer(7, false), Answer(4, false),
        };

        var indices = ItemStatistics.IndicesOf(ItemStatistics.GroupsOf(Cohort(10)), answers, minimumCohortSize: 10);

        Assert.Equal(0.3333m, indices.Discrimination);
    }

    [Fact]
    public void Discrimination_CanBeNegative_WhenTheWeakCandidatesAnsweredBetter()
    {
        var answers = Enumerable.Range(1, 10).Select(i => Answer(i, correct: i <= 3)).ToList();

        var indices = ItemStatistics.IndicesOf(ItemStatistics.GroupsOf(Cohort(10)), answers, minimumCohortSize: 10);

        Assert.Equal(-1m, indices.Discrimination);
    }

    [Fact]
    public void BelowTheThreshold_BothIndicesAreWithheld_ButTheCountsStillShow()
    {
        // Nine candidates against a threshold of ten: the figures would rest on too few answers to show, but the count is still true.
        var answers = Enumerable.Range(1, 9).Select(i => Answer(i, correct: true)).ToList();

        var indices = ItemStatistics.IndicesOf(ItemStatistics.GroupsOf(Cohort(9)), answers, minimumCohortSize: 10);

        Assert.Equal(9, indices.Attempts);
        Assert.Equal(9, indices.CorrectCount);
        Assert.Null(indices.Difficulty);
        Assert.Null(indices.Discrimination);
    }

    [Fact]
    public void Discrimination_IsWithheld_WhenAGroupHasNoOneWhoHadTheQuestion()
    {
        // Only the middle candidates had this question, so neither group can be compared and the difficulty still stands.
        var answers = Enumerable.Range(4, 4).Select(i => Answer(i, correct: true)).ToList();

        var indices = ItemStatistics.IndicesOf(ItemStatistics.GroupsOf(Cohort(10)), answers, minimumCohortSize: 4);

        Assert.Equal(1m, indices.Difficulty);
        Assert.Null(indices.Discrimination);
    }

    [Fact]
    public void AnEmptyCohort_HasNoAttempts_AndNoIndices()
    {
        var indices = ItemStatistics.IndicesOf(ItemStatistics.GroupsOf([]), [], minimumCohortSize: 10);

        Assert.Equal(0, indices.Attempts);
        Assert.Null(indices.Difficulty);
        Assert.Null(indices.Discrimination);
    }
}
