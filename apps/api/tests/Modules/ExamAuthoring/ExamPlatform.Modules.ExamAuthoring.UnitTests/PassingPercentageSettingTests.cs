using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>The author's pass mark for an exam (FR-34): none by default, a percentage from 1 to 100 when set, and removable.</summary>
public class PassingPercentageSettingTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 9, 0, 0, DateTimeKind.Utc);

    private static Exam DraftExam() => new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());

    [Fact]
    public void ANewExam_HasNoPassMark_SoNothingIsCalledAPass()
    {
        Assert.Null(DraftExam().Config.PassingPercentage);
    }

    [Theory]
    [InlineData(ExamConfig.LowestPassingPercentage)]
    [InlineData(40)]
    [InlineData(ExamConfig.HighestPassingPercentage)]
    public void TheAuthor_CanSetAPercentageInRange(int percent)
    {
        var exam = DraftExam();

        exam.SetPassingPercentage(percent, Now);

        Assert.Equal(percent, exam.Config.PassingPercentage);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(ExamConfig.HighestPassingPercentage + 1)]
    public void APercentageOutsideTheRange_IsRefused_AndTheMarkIsUnchanged(int percent)
    {
        var exam = DraftExam();
        exam.SetPassingPercentage(40, Now);

        Assert.Throws<InvalidExamConfigError>(() => exam.SetPassingPercentage(percent, Now.AddMinutes(1)));

        Assert.Equal(40, exam.Config.PassingPercentage);
    }

    [Fact]
    public void TheAuthor_CanRemoveThePassMark_SoTheExamHasNoPass()
    {
        var exam = DraftExam();
        exam.SetPassingPercentage(40, Now);

        exam.ClearPassingPercentage(Now.AddMinutes(1));

        Assert.Null(exam.Config.PassingPercentage);
    }

    [Fact]
    public void SettingThePassMark_LeavesTheOtherSettingsAsTheyWere()
    {
        var exam = DraftExam();
        var before = exam.Config with { PassingPercentage = null };

        exam.SetPassingPercentage(40, Now);

        Assert.Equal(before with { PassingPercentage = 40 }, exam.Config);
    }
}
