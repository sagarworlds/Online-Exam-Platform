using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>
/// The minors' default (requirements section 7.2): a candidate under 18 is never held to a profile that uses the camera or records the
/// screen, while an adult keeps the profile the author chose.
/// </summary>
public class ProctoringMinorsDefaultTests
{
    [Fact]
    public void AMinorIsHeldToBrowserLockWhateverCameraOrScreenProfileTheExamUses()
    {
        Assert.Equal(ProctoringProfiles.BrowserLock, ProctoringProfiles.ForCandidate(ProctoringProfiles.Find("FULL")!, candidateIsMinor: true));
        Assert.Equal(ProctoringProfiles.BrowserLock, ProctoringProfiles.ForCandidate(ProctoringProfiles.Find("BROWSER_CAMERA")!, candidateIsMinor: true));
    }

    [Fact]
    public void AnAdultKeepsTheCameraAndScreenProfileTheyWereGiven()
    {
        Assert.Equal(ProctoringProfiles.Find("FULL"), ProctoringProfiles.ForCandidate(ProctoringProfiles.Find("FULL")!, candidateIsMinor: false));
    }

    [Fact]
    public void AMinorKeepsAProfileThatDoesNotCaptureMedia()
    {
        Assert.Equal(ProctoringProfiles.BrowserLock, ProctoringProfiles.ForCandidate(ProctoringProfiles.BrowserLock, candidateIsMinor: true));
        Assert.Equal(ProctoringProfiles.Off, ProctoringProfiles.ForCandidate(ProctoringProfiles.Off, candidateIsMinor: true));
    }

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
