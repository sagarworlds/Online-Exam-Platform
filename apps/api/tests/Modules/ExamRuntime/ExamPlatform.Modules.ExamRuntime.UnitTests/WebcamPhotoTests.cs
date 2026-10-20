using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Webcam photos taken in an attempt (FR-24): who is photographed, how often, and what is kept.</summary>
public class WebcamPhotoTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    private readonly Guid _candidate = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IWebcamPhotoRepository _photos = Substitute.For<IWebcamPhotoRepository>();
    private readonly ICandidateAgeDirectory _ages = Substitute.For<ICandidateAgeDirectory>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly QuestionSnapshot _question = Fixtures.Question();
    private ExamSnapshot _exam;

    public WebcamPhotoTests()
    {
        _exam = Fixtures.Exam([_question]) with { WebcamSnapshots = true };
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_ => _exam);
        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<QuestionSnapshot>>([_question]));
        _ages.IsMinorOnAsync(_candidate, Fixtures.Now, Arg.Any<CancellationToken>()).Returns(false);
    }

    private AttemptAccess Access() =>
        new(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock, new ProctorPolicy());

    private RecordWebcamPhotoHandler Recorder() => new(Access(), new WebcamEligibility(_ages), _photos, _unitOfWork, _clock);

    private Attempt OpenAttempt()
    {
        var attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    // ---- the policy -------------------------------------------------------------------------------

    [Fact]
    public async Task TheCandidateIsPhotographed_WhenTheExamAsksAndTheyAreAnAdult()
    {
        var attempt = OpenAttempt();

        var policy = await new GetWebcamPolicyHandler(Access(), new WebcamEligibility(_ages))
            .HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.True(policy.Enabled);
        Assert.Equal(WebcamSnapshotPolicy.IntervalSeconds, policy.IntervalSeconds);
    }

    [Fact]
    public async Task AMinorIsNeverPhotographed_WhateverTheExamAsks()
    {
        _ages.IsMinorOnAsync(_candidate, Fixtures.Now, Arg.Any<CancellationToken>()).Returns(true);
        var attempt = OpenAttempt();

        var policy = await new GetWebcamPolicyHandler(Access(), new WebcamEligibility(_ages))
            .HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.False(policy.Enabled);
        Assert.Equal(0, policy.IntervalSeconds);
    }

    [Fact]
    public async Task NoOneIsPhotographed_WhenTheExamDoesNotAsk()
    {
        _exam = _exam with { WebcamSnapshots = false };
        var attempt = OpenAttempt();

        var policy = await new GetWebcamPolicyHandler(Access(), new WebcamEligibility(_ages))
            .HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.False(policy.Enabled);
        await _ages.DidNotReceiveWithAnyArgs().IsMinorOnAsync(default, default, default);
    }

    // ---- storing a photo --------------------------------------------------------------------------

    [Fact]
    public async Task APhoto_IsStoredWithTheServersTime()
    {
        var attempt = OpenAttempt();
        _clock.UtcNow = Fixtures.Now.AddMinutes(2);

        await Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Webcam, CancellationToken.None);

        _photos.Received(1).Add(Arg.Is<AttemptWebcamPhoto>(p =>
            p.AttemptId == attempt.Id && p.CapturedAtUtc == Fixtures.Now.AddMinutes(2) && p.Image.SequenceEqual(Jpeg)));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AMinorsPhoto_IsRefused_AndNothingIsStored()
    {
        _ages.IsMinorOnAsync(_candidate, Fixtures.Now, Arg.Any<CancellationToken>()).Returns(true);
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<WebcamNotRequiredError>(() => Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Webcam, CancellationToken.None));

        _photos.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task APhotoForAnExamThatDoesNotAsk_IsRefused()
    {
        _exam = _exam with { WebcamSnapshots = false };
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<WebcamNotRequiredError>(() => Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Webcam, CancellationToken.None));

        _photos.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task APhotoOfSomeoneElsesAttempt_IsNotFound()
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<AttemptNotFoundError>(() => Recorder().HandleAsync(attempt.Id, Guid.NewGuid(), Jpeg, CaptureKind.Webcam, CancellationToken.None));

        _photos.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task APhotoAfterTheAttemptIsSubmitted_IsRefused()
    {
        var attempt = OpenAttempt();
        attempt.Submit(Fixtures.Now.AddMinutes(1), 0, 1);

        await Assert.ThrowsAsync<AttemptNotInProgressError>(() => Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Webcam, CancellationToken.None));
    }

    [Fact]
    public async Task APhotoSoonAfterTheLast_IsRefused_AndOneAfterTheGapIsKept()
    {
        var attempt = OpenAttempt();
        _photos.LatestCapturedAtAsync(attempt.Id, CaptureKind.Webcam, Arg.Any<CancellationToken>()).Returns(Fixtures.Now);

        _clock.UtcNow = Fixtures.Now.AddSeconds(WebcamSnapshotPolicy.MinGapSeconds - 1);
        await Assert.ThrowsAsync<WebcamPhotoTooSoonError>(() => Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Webcam, CancellationToken.None));

        _clock.UtcNow = Fixtures.Now.AddSeconds(WebcamSnapshotPolicy.MinGapSeconds);
        await Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Webcam, CancellationToken.None);

        _photos.Received(1).Add(Arg.Any<AttemptWebcamPhoto>());
    }

    [Fact]
    public async Task AnAttemptAtTheLimit_TakesNoMorePhotos()
    {
        var attempt = OpenAttempt();
        _photos.CountAsync(attempt.Id, CaptureKind.Webcam, Arg.Any<CancellationToken>()).Returns(WebcamSnapshotPolicy.MaxPerAttempt);

        await Assert.ThrowsAsync<WebcamPhotoLimitError>(() => Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Webcam, CancellationToken.None));

        _photos.DidNotReceiveWithAnyArgs().Add(default!);
    }

    // ---- the photo itself -------------------------------------------------------------------------

    [Fact]
    public void APhoto_MustBeANonEmptyJpegWithinTheSizeLimit()
    {
        Assert.Throws<InvalidAttemptError>(() => AttemptWebcamPhoto.Create(Guid.NewGuid(), [], Fixtures.Now));
        Assert.Throws<InvalidAttemptError>(() => AttemptWebcamPhoto.Create(Guid.NewGuid(), [0x89, 0x50, 0x4E, 0x47], Fixtures.Now));
        Assert.Throws<InvalidAttemptError>(() => AttemptWebcamPhoto.Create(Guid.NewGuid(), Oversize(), Fixtures.Now));

        var photo = AttemptWebcamPhoto.Create(Guid.NewGuid(), Jpeg, Fixtures.Now);

        Assert.Equal(Jpeg, photo.Image);
    }

    private static byte[] Oversize()
    {
        var bytes = new byte[WebcamSnapshotPolicy.MaxImageBytes + 1];
        bytes[0] = 0xFF;
        bytes[1] = 0xD8;
        bytes[2] = 0xFF;
        return bytes;
    }

    // ---- screen pictures (FR-25) ------------------------------------------------------------------

    [Fact]
    public async Task ThePolicy_AsksForTheScreenOnlyWhenTheExamDoes_AndNeverOfAMinor()
    {
        _exam = _exam with { ScreenCapture = true };
        var attempt = OpenAttempt();
        var handler = new GetWebcamPolicyHandler(Access(), new WebcamEligibility(_ages));

        var adult = await handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.True(adult.ScreenEnabled);
        Assert.Equal(WebcamSnapshotPolicy.IntervalSeconds, adult.ScreenIntervalSeconds);

        _ages.IsMinorOnAsync(_candidate, Fixtures.Now, Arg.Any<CancellationToken>()).Returns(true);
        var minor = await handler.HandleAsync(attempt.Id, _candidate, CancellationToken.None);

        Assert.False(minor.ScreenEnabled);
        Assert.Equal(0, minor.ScreenIntervalSeconds);
    }

    [Fact]
    public async Task AScreenPicture_IsStoredAsAScreenPicture_AndHasItsOwnSpacingFromPhotos()
    {
        _exam = _exam with { ScreenCapture = true };
        var attempt = OpenAttempt();
        // A photo arrived a moment ago; that does not hold back the screen picture, which is counted on its own.
        _photos.LatestCapturedAtAsync(attempt.Id, CaptureKind.Webcam, Arg.Any<CancellationToken>()).Returns(Fixtures.Now);

        await Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Screen, CancellationToken.None);

        _photos.Received(1).Add(Arg.Is<AttemptWebcamPhoto>(p => p.Kind == CaptureKind.Screen && p.AttemptId == attempt.Id));
    }

    [Fact]
    public async Task AScreenPicture_IsRefused_WhenTheExamOnlyAsksForPhotos_OrTheCandidateIsAMinor()
    {
        var attempt = OpenAttempt();

        await Assert.ThrowsAsync<WebcamNotRequiredError>(() => Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Screen, CancellationToken.None));

        _exam = _exam with { ScreenCapture = true };
        _ages.IsMinorOnAsync(_candidate, Fixtures.Now, Arg.Any<CancellationToken>()).Returns(true);
        await Assert.ThrowsAsync<WebcamNotRequiredError>(() => Recorder().HandleAsync(attempt.Id, _candidate, Jpeg, CaptureKind.Screen, CancellationToken.None));

        _photos.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public void AScreenPicture_MayBeLargerThanAPhoto_ButNotLargerThanItsOwnLimit()
    {
        var big = new byte[WebcamSnapshotPolicy.MaxImageBytes + 1];
        big[0] = 0xFF;
        big[1] = 0xD8;
        big[2] = 0xFF;

        Assert.Throws<InvalidAttemptError>(() => AttemptWebcamPhoto.Create(Guid.NewGuid(), big, Fixtures.Now));
        Assert.Equal(CaptureKind.Screen, AttemptWebcamPhoto.Create(Guid.NewGuid(), big, Fixtures.Now, CaptureKind.Screen).Kind);

        var tooBig = new byte[ScreenCapturePolicy.MaxImageBytes + 1];
        tooBig[0] = 0xFF;
        tooBig[1] = 0xD8;
        tooBig[2] = 0xFF;
        Assert.Throws<InvalidAttemptError>(() => AttemptWebcamPhoto.Create(Guid.NewGuid(), tooBig, Fixtures.Now, CaptureKind.Screen));
    }

    // ---- staff ------------------------------------------------------------------------------------

    [Fact]
    public async Task Staff_ListAnAttemptsPhotos_ButOnlyThroughItsOwnExam()
    {
        var attempt = OpenAttempt();
        IReadOnlyList<WebcamPhotoSummary> summaries = [new(Guid.NewGuid(), CaptureKind.Webcam, Fixtures.Now, 6)];
        _photos.ListAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(summaries);
        var handler = new ListWebcamPhotosHandler(_attempts, _photos);

        Assert.Equal(summaries, await handler.HandleAsync(_exam.Id, attempt.Id, CancellationToken.None));
        await Assert.ThrowsAsync<AttemptNotFoundError>(() => handler.HandleAsync(Guid.NewGuid(), attempt.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Staff_ReadOnePhoto_AndAMissingOneIsNotFound()
    {
        var attempt = OpenAttempt();
        var photo = AttemptWebcamPhoto.Create(attempt.Id, Jpeg, Fixtures.Now);
        _photos.GetAsync(attempt.Id, photo.Id, Arg.Any<CancellationToken>()).Returns(photo);
        var handler = new GetWebcamPhotoHandler(_attempts, _photos);

        Assert.Same(photo, await handler.HandleAsync(_exam.Id, attempt.Id, photo.Id, CancellationToken.None));
        await Assert.ThrowsAsync<WebcamPhotoNotFoundError>(() => handler.HandleAsync(_exam.Id, attempt.Id, Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<AttemptNotFoundError>(() => handler.HandleAsync(Guid.NewGuid(), attempt.Id, photo.Id, CancellationToken.None));
    }
}
