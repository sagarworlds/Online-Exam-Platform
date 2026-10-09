using ExamPlatform.SharedKernel.Application;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Staff previewing an exam as a candidate would see it (FR-15): the same view, in any state, and nothing stored.</summary>
public class PreviewExamTests
{
    private readonly Guid _staff = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly QuestionSnapshot _q1 = Fixtures.Question("First");
    private readonly QuestionSnapshot _q2 = Fixtures.Question("Second");

    public PreviewExamTests()
    {
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _q1, _q2 }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
    }

    private PreviewExamHandler Handler() =>
        new(_catalog, new AttemptViewBuilder(_bank, _clock), new PaperDrawer(_bank, new RandomQuestionPicker()), _clock);

    private ExamSnapshot Exam(bool published, int? durationSeconds = 1800)
    {
        var exam = Fixtures.Exam([_q1, _q2], published: published, durationSeconds: durationSeconds);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        return exam;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnExamInAnyState_ShowsItsQuestionsAsACandidateWouldSeeThem_WithoutTheAnswerKey(bool published)
    {
        var exam = Exam(published);

        var dto = await Handler().HandleAsync(exam.Id, _staff, CancellationToken.None);

        var section = Assert.Single(dto.Sections);
        Assert.Equal(["First", "Second"], section.Questions.Select(q => q.Text));
        Assert.All(section.Questions, q => Assert.Equal(3, q.Options.Count));
        Assert.Equal(AttemptStatus.InProgress, dto.Status);
        Assert.Equal(exam.Name, dto.ExamName);
    }

    [Fact]
    public async Task ThePreviewCountdown_IsTheExamsDuration_OrADayWhenUntimed()
    {
        var timed = Exam(published: true, durationSeconds: 1800);
        Assert.Equal(Fixtures.Now.AddMinutes(30), (await Handler().HandleAsync(timed.Id, _staff, CancellationToken.None)).DeadlineUtc);

        var untimed = Exam(published: true, durationSeconds: null);
        Assert.Equal(Fixtures.Now.AddHours(24), (await Handler().HandleAsync(untimed.Id, _staff, CancellationToken.None)).DeadlineUtc);
    }

    [Fact]
    public async Task ThePreview_CarriesTheExamsRules_SoTheStaffMemberSeesWhatACandidateWouldBeBoundBy()
    {
        var exam = Exam(published: true) with { ContentProtection = false, FocusViolationLimit = 3 };
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);

        var dto = await Handler().HandleAsync(exam.Id, _staff, CancellationToken.None);

        Assert.False(dto.ContentProtection);
        Assert.Equal(3, dto.FocusViolationLimit);
    }

    [Fact]
    public async Task AnUnknownExam_IsNotFound()
    {
        await Assert.ThrowsAsync<ExamNotFoundError>(() => Handler().HandleAsync(Guid.NewGuid(), _staff, CancellationToken.None));
    }

    [Fact]
    public async Task APaperThatCannotBeDrawn_IsReportedNow_NotOnExamDay()
    {
        var rule = new DrawRuleSnapshot(5, null, null, null, null, null);
        var exam = Fixtures.Exam([_q1]) with
        {
            Sections = [new ExamSectionSnapshot(Guid.NewGuid(), "A", 1, [_q1.Id], [rule])],
        };
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _bank.FindAsync(Arg.Any<QuestionCriteria>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<IReadOnlyList<FoundQuestion>>([]));

        await Assert.ThrowsAsync<PaperCannotBeDrawnError>(() => Handler().HandleAsync(exam.Id, _staff, CancellationToken.None));
    }
}
