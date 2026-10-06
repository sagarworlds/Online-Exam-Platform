using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Application.Queries;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

/// <summary>Proctoring profiles (FR-46): presets of the exam's proctoring settings, and the candidate notice written from them.</summary>
public class ProctoringProfileTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);

    private static Exam DraftExam() => new(null, "Maths", null, Exam.NotScheduledAt, Exam.NotScheduledAt, Guid.NewGuid());

    private static Exam PublishedExam()
    {
        var exam = DraftExam();
        exam.AddQuestion(exam.AddSection("S", null).Id, Guid.NewGuid(), QuestionPlacement.Unfiled);
        exam.Schedule(Now.AddHours(1), Now.AddHours(4), null, null, null, Now);
        exam.Publish(Now);
        return exam;
    }

    // ---- the catalogue ---------------------------------------------------------------------------------

    [Fact]
    public void TheCatalogue_OffersOffAndBrowserLock_AndListsTheCameraProfilesWithoutOfferingThem()
    {
        Assert.Equal(["OFF", "BROWSER_LOCK", "BROWSER_CAMERA", "FULL"], ProctoringProfiles.All.Select(p => p.Id));
        Assert.Equal(["OFF", "BROWSER_LOCK"], ProctoringProfiles.All.Where(p => p.Available).Select(p => p.Id));
        Assert.All(ProctoringProfiles.All.Where(p => !p.Available), p => Assert.False(string.IsNullOrWhiteSpace(p.UnavailableReason)));
    }

    [Theory]
    [InlineData("OFF")]
    [InlineData("off")]
    [InlineData("  Browser_Lock ")]
    public void AProfileIsFoundByIdIgnoringCaseAndSpaces(string id) => Assert.NotNull(ProctoringProfiles.Find(id));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("NOPE")]
    public void AnUnknownProfileIsNotFound(string? id) => Assert.Null(ProctoringProfiles.Find(id));

    [Theory]
    [InlineData(false, 0, "OFF")]
    [InlineData(true, ProctoringProfiles.BrowserLockViolationLimit, "BROWSER_LOCK")]
    [InlineData(true, 0, ProctoringProfiles.Custom)]
    [InlineData(false, 3, ProctoringProfiles.Custom)]
    [InlineData(true, 3, ProctoringProfiles.Custom)]
    public void TheProfileOfAnExam_IsWhicheverOneItsSettingsMatch_ElseCustom(bool protection, int limit, string expected) =>
        Assert.Equal(expected, ProctoringProfiles.IdFor(protection, limit));

    // ---- the notice -----------------------------------------------------------------------------------------

    [Fact]
    public void TheNotice_ForNothingTurnedOn_StillSaysWhatIsRecordedAndWhatIsNot()
    {
        var notice = ProctoringNotice.For(contentProtection: false, focusViolationLimit: 0);

        Assert.Equal(2, notice.Count);
        Assert.Contains("IP address", notice[0]);
        Assert.Equal("No camera, microphone or screen recording is used.", notice[^1]);
    }

    [Fact]
    public void TheNotice_SaysCopyingIsTurnedOff_OnlyWhenItIs()
    {
        Assert.Contains(ProctoringNotice.For(true, 0), l => l.StartsWith("Copying, pasting, right-click and printing are turned off", StringComparison.Ordinal));
        Assert.DoesNotContain(ProctoringNotice.For(false, 0), l => l.Contains("Copying", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, "The exam is submitted for you the first time you leave.")]
    [InlineData(3, "If you leave 3 times, the exam is submitted for you with the answers saved so far.")]
    public void TheNotice_NamesTheViolationLimit_BecauseItCanEndTheAttempt(int limit, string expected) =>
        Assert.Contains(ProctoringNotice.For(false, limit), l => l.Contains(expected, StringComparison.Ordinal));

    [Fact]
    public void TheNotice_DoesNotMentionTheViolationLimitWhenNothingIsWatched() =>
        Assert.DoesNotContain(ProctoringNotice.For(true, 0), l => l.Contains("Stay on the exam page", StringComparison.Ordinal));

    [Fact]
    public void EveryProfile_HasANoticeWrittenFromItsOwnSettings()
    {
        foreach (var profile in ProctoringProfiles.All)
        {
            var notice = ProctoringNotice.For(profile.ContentProtection, profile.FocusViolationLimit);
            Assert.Equal(profile.ContentProtection, notice.Any(l => l.Contains("Copying", StringComparison.Ordinal)));
            Assert.Equal(profile.FocusViolationLimit > 0, notice.Any(l => l.Contains("Stay on the exam page", StringComparison.Ordinal)));
        }
    }

    // ---- applying a profile -----------------------------------------------------------------------------

    [Fact]
    public void ApplyingBrowserLock_SetsBothSettings_AndTheExamReportsThatProfile()
    {
        var exam = DraftExam();

        exam.SetProctoringProfile("BROWSER_LOCK", Now.AddMinutes(5));

        Assert.True(exam.Config.ContentProtection);
        Assert.Equal(ProctoringProfiles.BrowserLockViolationLimit, exam.Config.FocusViolationLimit);
        Assert.Equal(Now.AddMinutes(5), exam.UpdatedAt);
        Assert.Equal("BROWSER_LOCK", ProctoringProfiles.IdFor(exam.Config.ContentProtection, exam.Config.FocusViolationLimit));
    }

    [Fact]
    public void ApplyingOff_TurnsBothSettingsOff()
    {
        var exam = DraftExam();
        exam.SetProctoringProfile("BROWSER_LOCK", Now);

        exam.SetProctoringProfile("off", Now);

        Assert.False(exam.Config.ContentProtection);
        Assert.Equal(0, exam.Config.FocusViolationLimit);
    }

    [Fact]
    public void ApplyingAProfile_ChangesNothingElseInTheConfig()
    {
        var exam = DraftExam();
        exam.SetMaxAttempts(3, Now);
        var before = exam.Config;

        exam.SetProctoringProfile("BROWSER_LOCK", Now);

        Assert.Equal(before with { ContentProtection = true, FocusViolationLimit = ProctoringProfiles.BrowserLockViolationLimit }, exam.Config);
    }

    [Fact]
    public void TuningASettingAfterwards_MakesTheExamCustom()
    {
        var exam = DraftExam();
        exam.SetProctoringProfile("BROWSER_LOCK", Now);

        exam.SetFocusViolationLimit(2, Now);

        Assert.Equal(ProctoringProfiles.Custom, ProctoringProfiles.IdFor(exam.Config.ContentProtection, exam.Config.FocusViolationLimit));
    }

    [Theory]
    [InlineData("BROWSER_CAMERA")]
    [InlineData("FULL")]
    public void AProfileThatPromisesSomethingNotBuilt_CannotBeChosen_AndSaysWhy(string id)
    {
        var exam = DraftExam();
        exam.SetProctoringProfile("OFF", Now);

        var error = Assert.Throws<InvalidExamConfigError>(() => exam.SetProctoringProfile(id, Now));

        Assert.Contains("not built yet", error.Message, StringComparison.Ordinal);
        Assert.False(exam.Config.ContentProtection);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("NOPE")]
    public void AnUnknownProfile_IsRefused_AndNothingChanges(string? id)
    {
        var exam = DraftExam();
        var before = exam.Config;

        Assert.Throws<InvalidExamConfigError>(() => exam.SetProctoringProfile(id, Now));

        Assert.Equal(before, exam.Config);
    }

    [Fact]
    public void AProfileCanBeApplied_AfterPublishing_BecauseItChangesNothingAskedOrScored()
    {
        var exam = PublishedExam();

        exam.SetProctoringProfile("BROWSER_LOCK", Now);

        Assert.Equal(ProctoringProfiles.BrowserLockViolationLimit, exam.Config.FocusViolationLimit);
    }

    [Fact]
    public void AnArchivedExam_RefusesAProfile()
    {
        var exam = PublishedExam();
        exam.Status = ExamStatus.Archived;

        Assert.Throws<ExamArchivedError>(() => exam.SetProctoringProfile("OFF", Now));
    }

    // ---- reading ----------------------------------------------------------------------------------------

    private static ExamDtoFactory Dtos() => new(Substitute.For<IBookCatalog>());

    [Fact]
    public async Task AnExamDto_CarriesItsProfileAndTheNoticeWrittenFromItsSettings()
    {
        var exam = DraftExam();
        exam.SetProctoringProfile("BROWSER_LOCK", Now);

        var proctoring = (await Dtos().ToDtoAsync(exam, CancellationToken.None)).Proctoring!;

        Assert.Equal("BROWSER_LOCK", proctoring.Profile);
        Assert.Equal("Browser lock", proctoring.ProfileName);
        Assert.Equal(ProctoringNotice.For(true, ProctoringProfiles.BrowserLockViolationLimit), proctoring.Notice);
    }

    [Fact]
    public async Task ACustomExam_IsCalledCustom()
    {
        var exam = DraftExam();

        var proctoring = (await Dtos().ToDtoAsync(exam, CancellationToken.None)).Proctoring!;

        Assert.Equal(ProctoringProfiles.Custom, proctoring.Profile);
        Assert.Equal("Custom", proctoring.ProfileName);
    }

    [Fact]
    public void TheCatalogueQuery_ListsEveryProfileWithItsNotice()
    {
        var profiles = new ListProctoringProfilesHandler().Handle();

        Assert.Equal(4, profiles.Count);
        var browserLock = profiles.Single(p => p.Id == "BROWSER_LOCK");
        Assert.True(browserLock.Available);
        Assert.Equal(ProctoringNotice.For(true, ProctoringProfiles.BrowserLockViolationLimit), browserLock.Notice);
        Assert.False(profiles.Single(p => p.Id == "FULL").Available);
    }

    [Fact]
    public async Task TheSnapshotTheRuntimeReads_CarriesTheNotice()
    {
        var exam = DraftExam();
        exam.SetProctoringProfile("BROWSER_LOCK", Now);
        var repository = Substitute.For<IExamRepository>();
        repository.ListByIdsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);

        var snapshot = await new ExamCatalog(repository).FindAsync(exam.Id, CancellationToken.None);

        Assert.Equal(ProctoringNotice.For(true, ProctoringProfiles.BrowserLockViolationLimit), snapshot!.ProctoringNotice);
    }

    // ---- the handler -------------------------------------------------------------------------------------

    private readonly IExamRepository _repository = Substitute.For<IExamRepository>();
    private readonly IExamAuthoringUnitOfWork _unitOfWork = Substitute.For<IExamAuthoringUnitOfWork>();
    private readonly Clock _clock = Substitute.For<Clock>();

    private SetProctoringProfileHandler Handler() => new(_repository, _unitOfWork, new ExamDtoFactory(Substitute.For<IBookCatalog>()), _clock);

    [Fact]
    public async Task TheHandler_AppliesTheProfile_SavesIt_AndReportsItInTheExam()
    {
        var exam = DraftExam();
        _repository.GetByIdOrThrowAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _clock.UtcNow.Returns(Now);

        var dto = await Handler().HandleAsync(new SetProctoringProfileCommand(exam.Id, "BROWSER_LOCK"), CancellationToken.None);

        Assert.Equal("BROWSER_LOCK", dto.Proctoring!.Profile);
        Assert.Equal(ProctoringProfiles.BrowserLockViolationLimit, dto.Config.FocusViolationLimit);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task AMissingProfile_IsRefusedWithAReason_AndNothingIsLoadedOrSaved(string? profile)
    {
        var error = await Assert.ThrowsAsync<InvalidExamConfigError>(
            () => Handler().HandleAsync(new SetProctoringProfileCommand(Guid.NewGuid(), profile), CancellationToken.None));

        Assert.Equal("invalid_exam_config", error.ErrorCode);
        await _repository.DidNotReceive().GetByIdOrThrowAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
