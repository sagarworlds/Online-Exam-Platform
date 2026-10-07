using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Events;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>The accommodation itself (FR-49): what it may hold, and what it says when it is set.</summary>
public class AccommodationEntityTests
{
    private static readonly Guid Exam = Guid.NewGuid();
    private static readonly Guid Candidate = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    private static Accommodation Make(int seconds = 1800, bool scribe = false, IEnumerable<string?>? formats = null, string? notes = null) =>
        Accommodation.Create(Exam, Candidate, seconds, scribe, formats, notes, Admin, Fixtures.Now);

    [Fact]
    public void ItRecordsWhatIsGivenAndWhoGaveIt()
    {
        var accommodation = Make(2700, scribe: true, formats: ["large_text"], notes: "Certificate seen");

        Assert.Equal((Exam, Candidate, 2700, true), (accommodation.ExamId, accommodation.CandidateId, accommodation.ExtraTimeSeconds, accommodation.ReaderScribe));
        Assert.Equal(["large_text"], accommodation.AlternateFormats);
        Assert.Equal(("Certificate seen", Admin, Fixtures.Now), (accommodation.Notes, accommodation.UpdatedByUserId, accommodation.UpdatedAtUtc));
    }

    [Fact]
    public void FormatsAreCleaned_SoSpacingCaseAndRepeatsDoNotMatter() =>
        Assert.Equal(["large_text", "high_contrast"], Make(formats: [" Large_Text ", "large_text", "HIGH_CONTRAST"]).AlternateFormats);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNote_IsStoredAsNone(string? notes) => Assert.Null(Make(notes: notes).Notes);

    [Theory]
    [InlineData(0, false, null)]
    [InlineData(0, false, new string[0])]
    public void AnAccommodationThatGivesNothing_IsRefused_BecauseRemovingOneIsItsOwnAction(int seconds, bool scribe, string[]? formats)
    {
        var refused = Assert.Throws<InvalidAccommodationError>(() => Make(seconds, scribe, formats));

        Assert.Contains("must give something", refused.Message);
        Assert.Equal("invalid_accommodation", refused.ErrorCode);
    }

    [Theory]
    [InlineData(600, false, null)]
    [InlineData(0, true, null)]
    [InlineData(0, false, new[] { "screen_reader" })]
    public void AnyOneOfTheThreeThings_IsEnough(int seconds, bool scribe, string[]? formats) => Assert.NotNull(Make(seconds, scribe, formats));

    [Theory]
    [InlineData(-1)]
    [InlineData(Accommodation.MaxExtraTimeSeconds + 1)]
    public void ExtraTimeOutsideTheRange_IsRefused(int seconds) => Assert.Throws<InvalidAccommodationError>(() => Make(seconds));

    [Fact]
    public void TheMostExtraTime_IsAllowed() => Assert.Equal(Accommodation.MaxExtraTimeSeconds, Make(Accommodation.MaxExtraTimeSeconds).ExtraTimeSeconds);

    [Fact]
    public void AFormatThatIsNotOffered_IsRefused_AndTheMessageSaysWhichAre()
    {
        var refused = Assert.Throws<InvalidAccommodationError>(() => Make(formats: ["braille"]));

        Assert.Contains("'braille'", refused.Message);
        Assert.Contains("large_text", refused.Message);
    }

    [Fact]
    public void ANoteThatIsTooLong_IsRefused()
    {
        Assert.Throws<InvalidAccommodationError>(() => Make(notes: new string('x', Accommodation.MaxNotesLength + 1)));
        Assert.NotNull(Make(notes: new string('x', Accommodation.MaxNotesLength)).Notes);
    }

    [Fact]
    public void ReviseReplacesTheWholeAccommodation_NotJustWhatWasSent()
    {
        var accommodation = Make(1800, scribe: true, formats: ["large_text"], notes: "first");

        accommodation.Revise(600, readerScribe: false, ["high_contrast"], null, Guid.NewGuid(), Fixtures.Now.AddDays(1));

        Assert.Equal((600, false, null), (accommodation.ExtraTimeSeconds, accommodation.ReaderScribe, accommodation.Notes));
        Assert.Equal(["high_contrast"], accommodation.AlternateFormats);
        Assert.Equal(Fixtures.Now.AddDays(1), accommodation.UpdatedAtUtc);
    }

    [Fact]
    public void ARefusedRevision_LeavesTheAccommodationAsItWas()
    {
        var accommodation = Make(1800, scribe: true, formats: ["large_text"], notes: "first");

        Assert.Throws<InvalidAccommodationError>(() => accommodation.Revise(0, false, [], null, Admin, Fixtures.Now.AddDays(1)));
        Assert.Throws<InvalidAccommodationError>(() => accommodation.Revise(900, false, ["braille"], "second", Admin, Fixtures.Now.AddDays(1)));

        Assert.Equal((1800, true, "first"), (accommodation.ExtraTimeSeconds, accommodation.ReaderScribe, accommodation.Notes));
        Assert.Equal(["large_text"], accommodation.AlternateFormats);
        Assert.Equal(Fixtures.Now, accommodation.UpdatedAtUtc);
    }

    [Fact]
    public void SettingOne_RaisesAnEventWithWhatWasGiven_ButNeverTheNote()
    {
        var accommodation = Make(1800, scribe: true, formats: ["screen_reader"], notes: "private detail");

        var raised = Assert.IsType<AccommodationSetEvent>(Assert.Single(accommodation.DomainEvents));

        Assert.Equal((accommodation.Id, 1800, true), (raised.AccommodationId, raised.ExtraTimeSeconds, raised.ReaderScribe));
        Assert.Equal(["screen_reader"], raised.AlternateFormats);
        // The note describes a person's need and stays out of the audit trail: the event has nowhere to carry it.
        Assert.DoesNotContain(typeof(AccommodationSetEvent).GetProperties(), p => p.Name == "Notes");
    }

    [Fact]
    public void EveryFormatThatCanBeSet_IsOneTheCatalogueNames() =>
        Assert.Equal(["large_text", "high_contrast", "screen_reader"], AccommodationFormat.All);
}

/// <summary>An attempt taking on an accommodation (FR-49): the time is added to the deadline the server holds, never taken away mid-exam.</summary>
public class AttemptAccommodationTests
{
    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Fixtures.Now, Fixtures.Now.AddMinutes(30));

    [Fact]
    public void AnAttemptCarriesNoAccommodation_UntilOneIsApplied()
    {
        var attempt = Open();

        Assert.False(attempt.IsAccommodated);
        Assert.Equal((0, false), (attempt.AccommodationExtraSeconds, attempt.AccommodationReaderScribe));
        Assert.Empty(attempt.AccommodationFormats);
    }

    [Fact]
    public void ExtraTime_IsAddedToTheDeadline_AndTheAttemptRecordsWhatApplied()
    {
        var attempt = Open();

        attempt.ApplyAccommodation(1200, readerScribe: true, ["large_text"]);

        Assert.Equal(Fixtures.Now.AddMinutes(50), attempt.DeadlineUtc);
        Assert.Equal((1200, true, true), (attempt.AccommodationExtraSeconds, attempt.AccommodationReaderScribe, attempt.IsAccommodated));
        Assert.Equal(["large_text"], attempt.AccommodationFormats);
        var raised = Assert.IsType<AttemptAccommodatedEvent>(Assert.Single(attempt.DomainEvents));
        Assert.Equal((attempt.Id, 1200, 1200), (raised.AttemptId, raised.ExtraTimeSeconds, raised.AddedSeconds));
    }

    [Fact]
    public void ApplyingTheSameAccommodationTwice_ChangesNothing_AndRecordsNothingNew()
    {
        var attempt = Open();
        attempt.ApplyAccommodation(1200, false, ["large_text"]);

        attempt.ApplyAccommodation(1200, false, ["large_text"]);

        Assert.Equal(Fixtures.Now.AddMinutes(50), attempt.DeadlineUtc);
        Assert.Single(attempt.DomainEvents);
    }

    [Fact]
    public void RaisingTheExtraTime_AddsOnlyTheDifference()
    {
        var attempt = Open();
        attempt.ApplyAccommodation(1200, false, []);

        attempt.ApplyAccommodation(3600, false, []);

        Assert.Equal(Fixtures.Now.AddMinutes(90), attempt.DeadlineUtc);
        Assert.Equal(3600, attempt.AccommodationExtraSeconds);
        Assert.Equal(2400, Assert.IsType<AttemptAccommodatedEvent>(attempt.DomainEvents.Last()).AddedSeconds);
    }

    [Fact]
    public void LoweringTheExtraTime_NeverTakesTimeAwayFromACandidateWhoIsSitting()
    {
        var attempt = Open();
        attempt.ApplyAccommodation(3600, false, []);

        attempt.ApplyAccommodation(600, false, []);

        Assert.Equal(Fixtures.Now.AddMinutes(90), attempt.DeadlineUtc);
        Assert.Equal(3600, attempt.AccommodationExtraSeconds);
    }

    [Fact]
    public void FormatsAndTheReaderFlag_CanChangeEitherWay_BecauseTheyTakeNothingAway()
    {
        var attempt = Open();
        attempt.ApplyAccommodation(0, false, ["large_text", "high_contrast"]);

        attempt.ApplyAccommodation(0, true, ["screen_reader"]);

        Assert.Equal(["screen_reader"], attempt.AccommodationFormats);
        Assert.True(attempt.AccommodationReaderScribe);
        Assert.Equal(Fixtures.Now.AddMinutes(30), attempt.DeadlineUtc);
        Assert.Equal(0, Assert.IsType<AttemptAccommodatedEvent>(attempt.DomainEvents.Last()).AddedSeconds);
    }

    [Fact]
    public void AFinishedAttempt_CannotTakeOnAnAccommodation()
    {
        var attempt = Open();
        attempt.Submit(Fixtures.Now.AddMinutes(5), 1m, 2m);

        Assert.Throws<AttemptNotInProgressError>(() => attempt.ApplyAccommodation(600, false, []));
    }

    [Fact]
    public void APausedAttempt_StillTakesTheTime_AndResumingKeepsIt()
    {
        var attempt = Open();
        attempt.Pause(Fixtures.Now.AddMinutes(10));

        attempt.ApplyAccommodation(600, false, []);
        attempt.Resume(Fixtures.Now.AddMinutes(15));

        Assert.Equal(Fixtures.Now.AddMinutes(30 + 5 + 10), attempt.DeadlineUtc);
    }
}

/// <summary>Staff setting and removing accommodations, and what they do to the exam a candidate sits (FR-49).</summary>
public class AccommodationHandlerTests
{
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _admin = Guid.NewGuid();
    private readonly FakeClock _clock = new(Fixtures.Now);
    private readonly FakeClientInfo _client = new();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IEnrollments _enrollments = Substitute.For<IEnrollments>();
    private readonly IExamRoster _roster = Substitute.For<IExamRoster>();
    private readonly IQuestionBank _bank = Substitute.For<IQuestionBank>();
    private readonly IAttemptRepository _attempts = Substitute.For<IAttemptRepository>();
    private readonly IExtraAttemptGrantRepository _grants = Substitute.For<IExtraAttemptGrantRepository>();
    private readonly IAccommodationRepository _accommodations = Substitute.For<IAccommodationRepository>();
    private readonly IExamRuntimeUnitOfWork _unitOfWork = Substitute.For<IExamRuntimeUnitOfWork>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IRequestContext _context = Substitute.For<IRequestContext>();

    private readonly QuestionSnapshot _q1 = Fixtures.Question("First");
    private readonly QuestionSnapshot _q2 = Fixtures.Question("Second");
    private ExamSnapshot _exam;
    private readonly List<Attempt> _theirs = [];
    private Accommodation? _stored;

    public AccommodationHandlerTests()
    {
        _exam = Fixtures.Exam([_q1, _q2]);
        UseExam(_exam);
        _enrollments.IsEnrolledAsync(_candidate, _exam.Id, Arg.Any<CancellationToken>()).Returns(true);
        _roster.GetEnrolledCandidatesAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns([new EnrolledCandidate(_candidate, "student@example.com")]);
        _attempts.ListForCandidateAtExamAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyList<Attempt>>(_theirs.OrderBy(a => a.Number).ToList()));
        _attempts.When(a => a.Add(Arg.Any<Attempt>())).Do(call => _theirs.Add(call.Arg<Attempt>()));
        _attempts.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(_theirs.FirstOrDefault(a => a.Id == call.Arg<Guid>())));
        _grants.CountAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>()).Returns(0);

        _accommodations.FindAsync(_exam.Id, _candidate, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(_stored));
        _accommodations.When(a => a.Add(Arg.Any<Accommodation>())).Do(call => _stored = call.Arg<Accommodation>());
        _accommodations.When(a => a.Remove(Arg.Any<Accommodation>())).Do(_ => _stored = null);
        _accommodations.ListForCandidateAsync(_candidate, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyDictionary<Guid, Accommodation>>(_stored is null ? new Dictionary<Guid, Accommodation>() : new Dictionary<Guid, Accommodation> { [_exam.Id] = _stored }));
        _accommodations.ListForExamAsync(_exam.Id, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<IReadOnlyDictionary<Guid, Accommodation>>(_stored is null ? new Dictionary<Guid, Accommodation>() : new Dictionary<Guid, Accommodation> { [_candidate] = _stored }));

        _bank.GetAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(new[] { _q1, _q2 }.Where(q => call.Arg<IReadOnlyCollection<Guid>>().Contains(q.Id)).ToList()));
        _bank.GetVersionsAsync(Arg.Any<IReadOnlyCollection<QuestionVersionRef>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<QuestionSnapshot>>(
                new[] { _q1, _q2 }.Where(q => call.Arg<IReadOnlyCollection<QuestionVersionRef>>().Any(r => r.QuestionId == q.Id)).ToList()));
        _context.UserId.Returns(_admin);
        _context.Role.Returns("Admin");
    }

    private void UseExam(ExamSnapshot exam)
    {
        _exam = exam;
        _catalog.FindAsync(exam.Id, Arg.Any<CancellationToken>()).Returns(exam);
        _catalog.FindPublishedAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([exam]);
    }

    private SetAccommodationHandler Set => new(_catalog, _roster, _attempts, _grants, _accommodations, _unitOfWork, _clock);
    private RemoveAccommodationHandler Remove => new(_catalog, _roster, _attempts, _grants, _accommodations, _unitOfWork, _audit, _context, _clock);
    private AttemptViewBuilder Views => new(_bank, _clock);
    private AttemptAccess Access => new(_attempts, _catalog, new AttemptCloser(_bank, _unitOfWork, _clock), _clock);
    private StartAttemptHandler Start => new(_catalog, _enrollments, _attempts, _grants, _accommodations, _unitOfWork, Access, Views, new PaperDrawer(_bank, new RandomQuestionPicker()), _bank, _clock, _client);

    private SetAccommodationCommand Command(int minutes = 30, bool scribe = false, string?[]? formats = null, string? notes = null) =>
        new(_exam.Id, _candidate, _admin, minutes * 60, scribe, formats, notes);

    private void Given(int minutes = 30, bool scribe = false, string?[]? formats = null, string? notes = null) =>
        _stored = Accommodation.Create(_exam.Id, _candidate, minutes * 60, scribe, formats, notes, _admin, Fixtures.Now);

    private Attempt OpenAttempt(DateTime? deadline = null)
    {
        var attempt = Attempt.Start(_exam.Id, _candidate, 1, Fixtures.Now.AddMinutes(-10), deadline ?? Fixtures.Now.AddMinutes(20));
        _theirs.Add(attempt);
        _attempts.GetByIdAsync(attempt.Id, Arg.Any<CancellationToken>()).Returns(attempt);
        return attempt;
    }

    // ---- setting ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Set_StoresTheAccommodation_AndStaffSeeItOnTheCandidatesRow_NoteIncluded()
    {
        var row = await Set.HandleAsync(Command(30, scribe: true, ["large_text"], "Certificate seen"), CancellationToken.None);

        _accommodations.Received(1).Add(Arg.Is<Accommodation>(a => a.ExtraTimeSeconds == 1800 && a.CandidateId == _candidate && a.UpdatedByUserId == _admin));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        var shown = Assert.IsType<AccommodationDto>(row.Accommodation);
        Assert.Equal((30, true, "Certificate seen"), (shown.ExtraTimeMinutes, shown.ReaderScribe, shown.Notes));
        Assert.Equal(["large_text"], shown.AlternateFormats);
    }

    [Fact]
    public async Task Set_WhenOneExists_ReplacesIt_AndAddsNoSecond()
    {
        Given(30, formats: ["large_text"]);

        var row = await Set.HandleAsync(Command(60, formats: ["high_contrast"]), CancellationToken.None);

        _accommodations.DidNotReceive().Add(Arg.Any<Accommodation>());
        var shown = Assert.IsType<AccommodationDto>(row.Accommodation);
        Assert.Equal(60, shown.ExtraTimeMinutes);
        Assert.Equal(["high_contrast"], shown.AlternateFormats);
    }

    [Fact]
    public async Task Set_ForAnExamThatIsNotThere_IsNotFound()
    {
        var unknown = new SetAccommodationCommand(Guid.NewGuid(), _candidate, _admin, 600, false, null, null);

        await Assert.ThrowsAsync<ExamNotFoundError>(() => Set.HandleAsync(unknown, CancellationToken.None));
    }

    [Fact]
    public async Task Set_ForSomeoneWhoNeverAcceptedAnInvitation_IsNotFound_AndStoresNothing()
    {
        var stranger = new SetAccommodationCommand(_exam.Id, Guid.NewGuid(), _admin, 600, false, null, null);

        await Assert.ThrowsAsync<CandidateNotEnrolledError>(() => Set.HandleAsync(stranger, CancellationToken.None));

        _accommodations.DidNotReceive().Add(Arg.Any<Accommodation>());
    }

    [Fact]
    public async Task Set_WithNothingGiven_IsRefused_AndNothingIsSaved()
    {
        await Assert.ThrowsAsync<InvalidAccommodationError>(() => Set.HandleAsync(Command(0), CancellationToken.None));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_WhileTheCandidateIsSitting_MovesTheirDeadlineAtOnce_AndAppliesTheFormats()
    {
        var open = OpenAttempt();

        await Set.HandleAsync(Command(30, scribe: true, ["large_text"]), CancellationToken.None);

        Assert.Equal(Fixtures.Now.AddMinutes(50), open.DeadlineUtc);
        Assert.Equal((1800, true), (open.AccommodationExtraSeconds, open.AccommodationReaderScribe));
        Assert.Equal(["large_text"], open.AccommodationFormats);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Set_ForAnAttemptWhoseTimeHasAlreadyRunOut_LeavesItAlone_SoItCannotBeReopened()
    {
        var expired = OpenAttempt(deadline: Fixtures.Now.AddMinutes(-1));

        await Set.HandleAsync(Command(30), CancellationToken.None);

        Assert.Equal(Fixtures.Now.AddMinutes(-1), expired.DeadlineUtc);
        Assert.False(expired.IsAccommodated);
    }

    [Fact]
    public async Task Set_ForAFinishedAttempt_LeavesItAlone()
    {
        var done = OpenAttempt();
        done.Submit(Fixtures.Now.AddMinutes(-2), 1m, 2m);

        await Set.HandleAsync(Command(30), CancellationToken.None);

        Assert.False(done.IsAccommodated);
    }

    // ---- removing -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Remove_TakesTheAccommodationAway_AndReturnsTheCandidateWithoutOne()
    {
        Given(30);

        var row = await Remove.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        _accommodations.Received(1).Remove(Arg.Is<Accommodation>(a => a.CandidateId == _candidate));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Null(row.Accommodation);
    }

    [Fact]
    public async Task Remove_IsAudited_WithWhoAndWhichCandidate_ButNeverTheNote()
    {
        Given(30, notes: "Certificate seen");

        await Remove.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        var entry = Assert.Single(_audit.ReceivedCalls().Select(c => (AuditEntry)c.GetArguments()[0]!));
        Assert.Equal(("ExamRuntime.AccommodationRemoved", "Accommodation", _admin), (entry.Action, entry.EntityType, entry.ActorUserId));
        Assert.Equal(_candidate.ToString(), entry.Metadata["candidateId"]);
        Assert.DoesNotContain(entry.Metadata.Values, v => v.Contains("Certificate"));
    }

    [Fact]
    public async Task Remove_WhenThereIsNone_IsNotFound() =>
        await Assert.ThrowsAsync<AccommodationNotFoundError>(() => Remove.HandleAsync(_exam.Id, _candidate, CancellationToken.None));

    [Fact]
    public async Task Remove_LeavesAnAttemptInProgressWithWhatItWasGiven()
    {
        Given(30, formats: ["large_text"]);
        var open = OpenAttempt();
        open.ApplyAccommodation(1800, false, ["large_text"]);

        await Remove.HandleAsync(_exam.Id, _candidate, CancellationToken.None);

        Assert.Equal(Fixtures.Now.AddMinutes(50), open.DeadlineUtc);
        Assert.Equal(["large_text"], open.AccommodationFormats);
    }

    // ---- starting an attempt ------------------------------------------------------------------------------------

    [Fact]
    public async Task Start_WithAnAccommodation_GivesADeadlineThatAlreadyIncludesTheExtraTime_AndTellsTheCandidateWhatApplies()
    {
        Given(30, scribe: true, formats: ["large_text"], notes: "private");

        var dto = await Start.HandleAsync(_exam.Id, _candidate, true, CancellationToken.None);

        Assert.Equal(Fixtures.Now.AddMinutes(30 + 30), dto.DeadlineUtc);
        var told = Assert.IsType<CandidateAccommodationDto>(dto.Accommodation);
        Assert.Equal((1800, true), (told.ExtraTimeSeconds, told.ReaderScribe));
        Assert.Equal(["large_text"], told.AlternateFormats);
        _attempts.Received(1).Add(Arg.Is<Attempt>(a => a.AccommodationExtraSeconds == 1800 && a.DeadlineUtc == Fixtures.Now.AddMinutes(60)));
    }

    [Fact]
    public async Task Start_WithoutAnAccommodation_IsTheExamsOwnDeadline_AndNothingIsSaidAboutOne()
    {
        var dto = await Start.HandleAsync(_exam.Id, _candidate, true, CancellationToken.None);

        Assert.Equal(Fixtures.Now.AddMinutes(30), dto.DeadlineUtc);
        Assert.Null(dto.Accommodation);
    }

    [Fact]
    public async Task Start_ExtraTimeIsOnTopOfTheWindowsEnd_SoACandidateWhoStartsLateStillGetsIt()
    {
        // Others who start now are cut off at the window's end, ten minutes away; the extra time comes after that.
        UseExam(_exam with { EndUtc = Fixtures.Now.AddMinutes(10) });
        Given(20);

        var dto = await Start.HandleAsync(_exam.Id, _candidate, true, CancellationToken.None);

        Assert.Equal(Fixtures.Now.AddMinutes(10 + 20), dto.DeadlineUtc);
    }

    [Fact]
    public async Task Start_ResumingAnAttempt_KeepsItsExtraTime_EvenIfTheAccommodationWasRemovedSince()
    {
        Given(30);
        var first = await Start.HandleAsync(_exam.Id, _candidate, true, CancellationToken.None);
        _stored = null;

        var resumed = await Start.HandleAsync(_exam.Id, _candidate, true, CancellationToken.None);

        Assert.Equal(first.Id, resumed.Id);
        Assert.Equal(first.DeadlineUtc, resumed.DeadlineUtc);
        Assert.Equal(1800, resumed.Accommodation!.ExtraTimeSeconds);
    }

    // ---- the one override ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ACandidateWithAScreenReader_IsNotHeldToThePageLeavingLimit_ButOthersAre()
    {
        UseExam(_exam with { FocusViolationLimit = 3 });
        Given(0, formats: ["screen_reader"]);
        var withReader = await Start.HandleAsync(_exam.Id, _candidate, true, CancellationToken.None);

        _theirs.Clear();
        Given(0, formats: ["large_text"]);
        var withoutReader = await Start.HandleAsync(_exam.Id, _candidate, true, CancellationToken.None);

        Assert.Equal(0, withReader.FocusViolationLimit);
        Assert.Equal(3, withoutReader.FocusViolationLimit);
    }

    [Fact]
    public async Task TheServerDoesNotCountLeavingThePage_ForAScreenReaderCandidate_SoItNeverEndsTheirAttempt()
    {
        UseExam(_exam with { FocusViolationLimit = 2 });
        Given(0, formats: ["screen_reader"]);
        var started = await Start.HandleAsync(_exam.Id, _candidate, true, CancellationToken.None);
        var closer = new AttemptCloser(_bank, _unitOfWork, _clock);
        var handler = new RecordFocusViolationHandler(Access, closer, _unitOfWork, _clock);

        for (var i = 0; i < 5; i++)
        {
            var result = await handler.HandleAsync(started.Id, _candidate, FocusViolationKind.TabHidden, CancellationToken.None);
            Assert.False(result.AttemptEnded);
        }

        Assert.Equal(AttemptStatus.InProgress, _theirs.Single().Status);
        Assert.Empty(_theirs.Single().FocusViolations);
    }

    [Fact]
    public void TheOverrideLiftsOnlyThePageLeavingLimit()
    {
        var exam = _exam with { FocusViolationLimit = 3, ContentProtection = true };

        var theirs = AccommodationPolicy.Apply(exam, ["screen_reader"]);

        Assert.Equal((0, true), (theirs.FocusViolationLimit, theirs.ContentProtection));
        Assert.Equal(exam with { FocusViolationLimit = 0 }, theirs);
        Assert.Same(exam, AccommodationPolicy.Apply(exam, ["large_text", "high_contrast"]));
        Assert.Same(exam, AccommodationPolicy.Apply(exam, []));
    }

    // ---- what each side is shown --------------------------------------------------------------------------------

    private async Task<MyExamDto> MyExamAsync()
    {
        _enrollments.GetEnrolledExamIdsAsync(_candidate, Arg.Any<CancellationToken>()).Returns([_exam.Id]);
        _attempts.ListForCandidateAsync(_candidate, Arg.Any<CancellationToken>()).Returns(_theirs.ToList());
        _grants.CountsForCandidateAsync(_candidate, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());

        return Assert.Single(await new MyExamsHandler(
            _enrollments, _catalog, _attempts, _grants, Substitute.For<IAttemptRequestRepository>(), _accommodations, _clock).HandleAsync(_candidate, CancellationToken.None));
    }

    [Fact]
    public async Task TheCandidateIsToldTheirAccommodation_OnMyExams_ButNeverTheStaffNote()
    {
        Given(45, scribe: true, formats: ["high_contrast"], notes: "Certificate seen");

        var exam = await MyExamAsync();

        var told = Assert.IsType<CandidateAccommodationDto>(exam.Accommodation);
        Assert.Equal((2700, true), (told.ExtraTimeSeconds, told.ReaderScribe));
        Assert.Equal(["high_contrast"], told.AlternateFormats);
        Assert.DoesNotContain(typeof(CandidateAccommodationDto).GetProperties(), p => p.Name == "Notes");
    }

    [Fact]
    public async Task MyExams_ShowsNoAccommodation_WhenThereIsNone() => Assert.Null((await MyExamAsync()).Accommodation);

    [Fact]
    public async Task MyExams_StatesTheRulesAsThisCandidateSitsThem_SoAScreenReaderUserIsNotToldTheyWillBeCountedOut()
    {
        UseExam(_exam with { FocusViolationLimit = 3 });
        Given(0, formats: ["screen_reader"]);

        var exam = await MyExamAsync();

        Assert.Equal(0, exam.Rules!.FocusViolationLimit);
    }

    [Fact]
    public async Task TheStaffList_ShowsEachCandidatesAccommodation_NoteIncluded()
    {
        Given(30, notes: "Certificate seen");
        _attempts.ListForExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_theirs);
        _grants.CountsForExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());

        var list = await new ListExamAttemptsHandler(_catalog, _roster, _attempts, _grants, _accommodations, _clock).HandleAsync(_exam.Id, CancellationToken.None);

        var row = Assert.Single(list.Candidates);
        Assert.Equal((30, "Certificate seen"), (row.Accommodation!.ExtraTimeMinutes, row.Accommodation.Notes));
    }

    [Fact]
    public async Task TheStaffList_ShowsNoAccommodation_ForACandidateWhoHasNone()
    {
        _attempts.ListForExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_theirs);
        _grants.CountsForExamAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());

        var list = await new ListExamAttemptsHandler(_catalog, _roster, _attempts, _grants, _accommodations, _clock).HandleAsync(_exam.Id, CancellationToken.None);

        Assert.Null(Assert.Single(list.Candidates).Accommodation);
    }

    // ---- the audit trail ----------------------------------------------------------------------------------------

    [Fact]
    public async Task SettingOne_IsAudited_WithWhatWasGiven_ButNeverTheNote()
    {
        var trail = new AccommodationAuditTrail(_audit, _context);
        var accommodation = Accommodation.Create(_exam.Id, _candidate, 1800, true, ["screen_reader"], "Certificate seen", _admin, Fixtures.Now);

        await trail.HandleAsync(Assert.IsType<AccommodationSetEvent>(Assert.Single(accommodation.DomainEvents)), CancellationToken.None);

        var entry = Assert.Single(_audit.ReceivedCalls().Select(c => (AuditEntry)c.GetArguments()[0]!));
        Assert.Equal(("ExamRuntime.AccommodationSet", "Accommodation", accommodation.Id.ToString()), (entry.Action, entry.EntityType, entry.EntityId));
        Assert.Equal(("1800", "true", "screen_reader"), (entry.Metadata["extraTimeSeconds"], entry.Metadata["readerScribe"], entry.Metadata["alternateFormats"]));
        Assert.DoesNotContain(entry.Metadata.Values, v => v.Contains("Certificate"));
    }

    [Fact]
    public async Task AnAttemptTakingOnTime_IsAudited_AsAnEventOnTheAttempt()
    {
        var trail = new AccommodationAuditTrail(_audit, _context);
        var attempt = OpenAttempt();
        attempt.ApplyAccommodation(1200, false, []);

        await trail.HandleAsync(Assert.IsType<AttemptAccommodatedEvent>(Assert.Single(attempt.DomainEvents)), CancellationToken.None);

        var entry = Assert.Single(_audit.ReceivedCalls().Select(c => (AuditEntry)c.GetArguments()[0]!));
        Assert.Equal(("ExamRuntime.AttemptAccommodated", "Attempt", attempt.Id.ToString()), (entry.Action, entry.EntityType, entry.EntityId));
        Assert.Equal(("1200", "1200"), (entry.Metadata["extraTimeSeconds"], entry.Metadata["addedSeconds"]));
    }
}
