using ExamPlatform.Modules.ExamRuntime.Application;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// The rules every leaderboard shares (FR-35): the places on a board, and how a name is shortened for other candidates. Each rule is pinned
/// here, because a board is only as fair as its ties and its privacy.
/// </summary>
public class LeaderboardRulesTests
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");

    [Fact]
    public void TheBoard_IsOrderedBestFirst_WithPlacesCountedByPosition()
    {
        var rows = LeaderboardRanking.Rank([(C, 2m), (A, 9m), (B, 5m)]);

        Assert.Equal([A, B, C], rows.Select(r => r.CandidateId));
        Assert.Equal([1, 2, 3], rows.Select(r => r.Rank));
    }

    [Fact]
    public void CandidatesWithTheSameScore_ShareAPlace_AndTheNextPlaceIsSkipped()
    {
        var rows = LeaderboardRanking.Rank([(A, 9m), (B, 5m), (C, 5m)]);

        Assert.Equal([1, 2, 2], rows.Select(r => r.Rank));
    }

    [Fact]
    public void TiedCandidates_AreListedInCandidateIdOrder_SoTheSameBoardAlwaysReadsTheSameWay()
    {
        var rows = LeaderboardRanking.Rank([(C, 5m), (B, 5m)]);

        Assert.Equal([B, C], rows.Select(r => r.CandidateId));
    }

    [Fact]
    public void AnEmptyBoard_HasNoRows()
    {
        Assert.Empty(LeaderboardRanking.Rank([]));
    }

    [Theory]
    [InlineData("Asha Kumar", "Asha K.")]
    [InlineData("  Asha   Kumar Rao ", "Asha R.")]
    [InlineData("Asha", "Asha")]
    [InlineData("asha kumar", "asha K.")]
    public void AName_IsShortenedToTheFirstNameAndTheInitialOfTheLast(string name, string shown)
    {
        Assert.Equal(shown, DisplayNameMask.Mask(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoName_ShowsNothing_SoTheBoardCanSayAnonymous(string? name)
    {
        Assert.Null(DisplayNameMask.Mask(name));
    }
}
