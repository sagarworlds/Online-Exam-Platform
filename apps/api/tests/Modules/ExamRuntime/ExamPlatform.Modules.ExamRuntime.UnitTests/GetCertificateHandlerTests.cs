using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// Who may have a certificate (FR-34): only the candidate, only for a submitted result the exam's author has released, never for an
/// invalidated result, and only when the name and the exam's name can be printed.
/// </summary>
public class GetCertificateHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IDisplayNameDirectory _names = Substitute.For<IDisplayNameDirectory>();

    private readonly QuestionSnapshot _question = Fixtures.Question("Capital of France?");

    private ExamSnapshot ExamWith(ExamResultReleaseMode mode = ExamResultReleaseMode.Instant, DateTime? releaseTime = null)
    {
        var exam = Fixtures.Exam([_question], correct: 4m, incorrect: -1m, unattempted: 0m, resultRelease: mode, resultReleaseTime: releaseTime);
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        return exam;
    }

    private Attempt Submitted(ExamSnapshot exam, Guid? owner = null)
    {
        var attempt = Attempt.Start(exam.Id, owner ?? _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        attempt.RecordAnswer(_question.Id, _question.Correct(), Fixtures.Now);
        attempt.Submit(Fixtures.Now.AddMinutes(10), 4m, 4m);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    private void NamedAs(string? name) =>
        _names.GetDisplayNamesAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, string>>(name is null ? new Dictionary<Guid, string>() : new Dictionary<Guid, string> { [_candidate] = name }));

    private GetCertificateHandler Handler =>
        new(new AttemptAccess(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock), _names, _clock);

    public GetCertificateHandlerTests()
    {
        NamedAs("Asha Kumar");
    }

    [Fact]
    public async Task AReleasedResult_GivesTheCandidatesNameTheExamTheScoreAndTheAttemptReference()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);

        var details = await Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.Equal("Asha Kumar", details.CandidateName);
        Assert.Equal(exam.Name, details.ExamName);
        Assert.Equal(4m, details.Score);
        Assert.Equal(4m, details.MaxScore);
        Assert.Equal(attempt.Id, details.AttemptId);
    }

    [Fact]
    public async Task BeforeTheResultIsReleased_NoCertificate()
    {
        var exam = ExamWith(ExamResultReleaseMode.Scheduled, releaseTime: Fixtures.Now.AddDays(1));
        var attempt = Submitted(exam);

        await Assert.ThrowsAsync<ResultsNotReleasedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task AnInvalidatedResult_NoCertificate()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam);
        attempt.Invalidate(Guid.NewGuid(), "Shared answers", Fixtures.Now.AddDays(1));

        await Assert.ThrowsAsync<AttemptInvalidatedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task AnOpenAttempt_NoCertificate()
    {
        var exam = ExamWith();
        var attempt = Attempt.Start(exam.Id, _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);

        await Assert.ThrowsAsync<AttemptNotSubmittedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task SomeoneElsesAttempt_IsNotFound_ExactlyAsAMissingOneIs()
    {
        var exam = ExamWith();
        var attempt = Submitted(exam, owner: Guid.NewGuid());

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task AnAccountWithNoName_CannotHaveACertificate_UntilItHasOne()
    {
        NamedAs(null);
        var exam = ExamWith();
        var attempt = Submitted(exam);

        await Assert.ThrowsAsync<CertificateNameMissingError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }

    [Fact]
    public async Task AName_ThePdfFontCannotPrint_IsRefused_NotMisspelled()
    {
        NamedAs("अशा कुमार");
        var exam = ExamWith();
        var attempt = Submitted(exam);

        await Assert.ThrowsAsync<CertificateTextUnsupportedError>(() => Handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None));
    }
}
