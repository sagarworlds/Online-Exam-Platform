using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>
/// Every change to an exam's config hands the config a copy of its marking scheme, never the instance it already had. Handing over the same
/// instance makes EF Core write NULL over the scheme's columns, which only a real database shows (see <c>ExamConfigChangeFlowTests</c> in the
/// integration tests); this is the quick check of the same rule, with no database.
/// </summary>
public class ExamConfigCopyTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 9, 0, 0, DateTimeKind.Utc);

    private static readonly MarkingScheme Authored = new(4m, -1m, -0.25m, PartialCredit: true);

    /// <summary>What a case needs before its change can run, and the change; <paramref name="NewScheme"/> is set when the change replaces the scheme.</summary>
    private sealed record Case(Action<Exam>? Setup, Action<Exam> Change, MarkingScheme? NewScheme = null);

    private static readonly Dictionary<string, Case> Cases = new()
    {
        ["marking-scheme"] = new(null, e => e.SetMarkingScheme(new MarkingScheme(2m, 0m, -0.5m), Now), new MarkingScheme(2m, 0m, -0.5m)),
        ["schedule"] = new(null, e => e.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, 1800, Now)),
        ["shuffle"] = new(null, e => e.SetShuffle(true, true, Now)),
        ["attempt-limit"] = new(null, e => e.SetMaxAttempts(3, Now)),
        ["content-protection"] = new(null, e => e.SetContentProtection(false, Now)),
        ["proctoring-profile"] = new(null, e => e.SetProctoringProfile("BROWSER_LOCK", Now)),
        ["focus-violation-limit"] = new(null, e => e.SetFocusViolationLimit(5, Now)),
        ["focus-warn-at"] = new(e => e.SetFocusViolationLimit(5, Now), e => e.SetFocusWarnAt(2, Now)),
        ["passing-percentage"] = new(null, e => e.SetPassingPercentage(60, Now)),
        ["passing-percentage-cleared"] = new(e => e.SetPassingPercentage(60, Now), e => e.ClearPassingPercentage(Now)),
        ["result-release"] = new(null, e => e.SetResultRelease(ResultReleaseMode.Scheduled, Now.AddDays(2), Now)),
        ["results-released-by-hand"] = new(
            e =>
            {
                e.SetResultRelease(ResultReleaseMode.Manual, null, Now);
                e.Publish(Now);
            },
            e => e.ReleaseResults(Now)),
    };

    /// <summary>The names of the cases, so each one is its own test.</summary>
    public static TheoryData<string> CaseNames() => [.. Cases.Keys];

    private static Exam ReadyExam()
    {
        var exam = new Exam(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid(), Now);
        exam.AddQuestion(exam.AddSection("S", null, Now).Id, Guid.NewGuid(), QuestionPlacement.Unfiled, Now);
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, null, Now);
        exam.SetMarkingScheme(Authored, Now);
        return exam;
    }

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void ChangingTheConfig_GivesItACopyOfTheMarkingScheme_NotTheOneItHad(string name)
    {
        var (setup, change, newScheme) = Cases[name];
        var exam = ReadyExam();
        setup?.Invoke(exam);
        var before = exam.Config.MarkingScheme;

        change(exam);

        Assert.NotSame(before, exam.Config.MarkingScheme);
        Assert.Equal(newScheme ?? before, exam.Config.MarkingScheme);
    }
}
