using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using ExamPlatform.Modules.Proctoring.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.Proctoring.UnitTests;

/// <summary>
/// Only the conflict two overlapping scans can produce becomes a structured 409. Any other unique violation stays an error (FR-27).
/// </summary>
public class OverlappingScanDetectorTests
{
    [Fact]
    public void AUniqueViolationOnTheAttemptIndex_IsAnOverlappingScan() =>
        Assert.True(OverlappingScanDetector.IsAttemptIndexViolation("23505", "IX_RiskAssessments_AttemptId"));

    [Theory]
    [InlineData("23503", "IX_RiskAssessments_AttemptId")]
    [InlineData("23505", "PK_RiskAssessments")]
    [InlineData("23505", "IX_RiskAssessments_ExamId_Status")]
    [InlineData(null, "IX_RiskAssessments_AttemptId")]
    [InlineData("23505", null)]
    public void AnyOtherConflict_IsNotAnOverlappingScan(string? sqlState, string? constraint) =>
        Assert.False(OverlappingScanDetector.IsAttemptIndexViolation(sqlState, constraint));

    [Fact]
    public void AnErrorThatIsNotADatabaseUpdateFailure_IsNotAnOverlappingScan() =>
        Assert.False(OverlappingScanDetector.IsOverlappingScan(new InvalidOperationException("not a save conflict")));

    [Fact]
    public void ADatabaseUpdateFailureWithoutAPostgresCause_IsNotAnOverlappingScan() =>
        Assert.False(OverlappingScanDetector.IsOverlappingScan(new DbUpdateException("save failed", new Exception("no postgres error"))));

    [Fact]
    public void TheConflictIsReportedAsA409WithAStableCode()
    {
        var error = new ScanAlreadyRunningError(new Exception("conflict"));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal("scan_already_running", error.ErrorCode);
    }
}
