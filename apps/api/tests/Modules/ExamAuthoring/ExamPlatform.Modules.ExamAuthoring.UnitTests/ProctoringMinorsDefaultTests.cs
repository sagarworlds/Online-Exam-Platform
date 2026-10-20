using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>
/// Which profiles capture media, and what the candidate notice says. The minors' default itself (requirements section 7.2) is enforced
/// by the runtime's <c>WebcamEligibility</c> and tested in the ExamRuntime unit tests.
/// </summary>
public class ProctoringMinorsDefaultTests
{
    [Fact]
    public void OnlyTheCameraAndScreenProfilesCaptureMedia()
    {
        var capturing = ProctoringProfiles.All.Where(p => p.CaptureMedia).Select(p => p.Id).OrderBy(id => id).ToArray();

        Assert.Equal(["BROWSER_CAMERA", "FULL"], capturing);
    }

    [Fact]
    public void TheNoticeNamesTheWarningPointAndTheEnd()
    {
        var notice = ProctoringNotice.For(contentProtection: false, focusViolationLimit: 5);

        Assert.Contains(notice, l => l.Contains("You are warned from the third time you leave.", StringComparison.Ordinal)
            && l.Contains("If you leave 5 times, the exam is submitted", StringComparison.Ordinal));
        Assert.DoesNotContain(notice, l => l.Contains("warned each time", StringComparison.Ordinal));
    }

    [Fact]
    public void ASmallLimitWarnsEarlierInTheNotice()
    {
        var notice = ProctoringNotice.For(contentProtection: false, focusViolationLimit: 2);

        Assert.Contains(notice, l => l.Contains("You are warned each time you leave.", StringComparison.Ordinal));
    }
}
