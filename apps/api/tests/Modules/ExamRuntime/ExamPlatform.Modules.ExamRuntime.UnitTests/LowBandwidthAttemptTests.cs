using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The attempt sent without its pictures, and the picture served when the page asks for it (FR-53).</summary>
public class LowBandwidthAttemptTests
{
    private static readonly byte[] TextPicture = [10, 20, 30, 40, 50, 60];

    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly QuestionOptionSnapshot _paris = new(Guid.NewGuid(), "Paris", IsCorrect: true);
    private readonly QuestionOptionSnapshot _rome = new(Guid.NewGuid(), "Rome", IsCorrect: false);
    private readonly QuestionSnapshot _question;

    public LowBandwidthAttemptTests()
    {
        _question = new QuestionSnapshot(Guid.NewGuid(), $"<p>Which city?</p>{Image(TextPicture, "A map")}", [_paris, _rome]);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([_question]);
    }

    private static string Image(byte[] bytes, string alt) => $"<img src=\"data:image/png;base64,{Convert.ToBase64String(bytes)}\" alt=\"{alt}\">";

    private AttemptAccess Access => new(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock);

    private (ExamSnapshot Exam, Attempt Attempt) OpenAttempt(Guid? owner = null)
    {
        var exam = Fixtures.Exam([_question]);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        var attempt = Attempt.Start(exam.Id, owner ?? _candidate, 1, Fixtures.Now.AddMinutes(-5), Fixtures.Now.AddMinutes(25));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return (exam, attempt);
    }

    // ---- the attempt without its pictures --------------------------------------------------------------

    [Fact]
    public async Task ByDefault_TheQuestionsCarryTheirPicturesInline()
    {
        var (exam, attempt) = OpenAttempt();

        var dto = await new AttemptViewBuilder(_bank, _clock).BuildAsync(attempt, exam, CancellationToken.None);

        var question = dto.Sections.Single().Questions.Single();
        Assert.Contains("data:image/png;base64", question.Text);
        Assert.All(question.Options, o => Assert.DoesNotContain("lazy-media", o.Text));
    }

    [Fact]
    public async Task InLowBandwidthMode_NoPictureDataIsSent_EachLeftAsAMarkerNamingIt()
    {
        var (exam, attempt) = OpenAttempt();

        var dto = await new AttemptViewBuilder(_bank, _clock).BuildAsync(attempt, exam, CancellationToken.None, lowBandwidth: true);

        var question = dto.Sections.Single().Questions.Single();
        Assert.DoesNotContain("base64", question.Text);
        Assert.Contains("<p>Which city?</p>", question.Text);
        Assert.Contains("lazy-media--q-0 lazy-bytes--6", question.Text);
        Assert.Contains("alt=\"A map\"", question.Text);
        // An option is plain text and is sent as it is.
        Assert.Equal(["Paris", "Rome"], question.Options.Select(o => o.Text).Order());
    }

    [Fact]
    public async Task InLowBandwidthMode_EverythingElseAboutTheAttemptIsAsItWas()
    {
        var (exam, attempt) = OpenAttempt();
        var builder = new AttemptViewBuilder(_bank, _clock);

        var full = await builder.BuildAsync(attempt, exam, CancellationToken.None);
        var lite = await builder.BuildAsync(attempt, exam, CancellationToken.None, lowBandwidth: true);

        Assert.Equal(full.DeadlineUtc, lite.DeadlineUtc);
        Assert.Equal(full.Status, lite.Status);
        Assert.Equal(full.Sections.Single().Questions.Single().Id, lite.Sections.Single().Questions.Single().Id);
        Assert.Equal(
            full.Sections.Single().Questions.Single().Options.Select(o => o.Id),
            lite.Sections.Single().Questions.Single().Options.Select(o => o.Id));
    }

    // ---- one picture on request ------------------------------------------------------------------------

    private GetQuestionPictureHandler Handler => new(Access, _bank);

    [Fact]
    public async Task APictureInTheQuestionsText_IsServedByItsKey()
    {
        var (_, attempt) = OpenAttempt();

        var picture = await Handler.HandleAsync(attempt.Id, _candidate, _question.Id, "q-0", CancellationToken.None);

        Assert.Equal("image/png", picture.ContentType);
        Assert.Equal(TextPicture, picture.Bytes);
    }

    [Theory]
    [InlineData("q-1")]
    [InlineData("q-x")]
    [InlineData("nonsense")]
    public async Task APictureThatIsNotThere_IsNotFound(string key)
    {
        var (_, attempt) = OpenAttempt();

        var error = await Assert.ThrowsAsync<QuestionMediaNotFoundError>(
            () => Handler.HandleAsync(attempt.Id, _candidate, _question.Id, key, CancellationToken.None));

        Assert.Equal("question_media_not_found", error.ErrorCode);
        Assert.Equal(404, error.HttpStatusCode);
    }

    [Fact]
    public async Task SomeoneElsesAttempt_AnswersLikeAMissingOne()
    {
        var (_, attempt) = OpenAttempt(owner: Guid.NewGuid());

        await Assert.ThrowsAsync<AttemptNotFoundError>(
            () => Handler.HandleAsync(attempt.Id, _candidate, _question.Id, "q-0", CancellationToken.None));
    }

    [Fact]
    public async Task AQuestionThatIsNotInTheAttempt_IsNotServed()
    {
        var (_, attempt) = OpenAttempt();

        await Assert.ThrowsAsync<QuestionNotInAttemptError>(
            () => Handler.HandleAsync(attempt.Id, _candidate, Guid.NewGuid(), "q-0", CancellationToken.None));
    }

    [Fact]
    public async Task AnAttemptThatIsOver_ServesNothing()
    {
        var (_, attempt) = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(-1), 0, 1);

        await Assert.ThrowsAsync<AttemptNotInProgressError>(
            () => Handler.HandleAsync(attempt.Id, _candidate, _question.Id, "q-0", CancellationToken.None));
    }
}
