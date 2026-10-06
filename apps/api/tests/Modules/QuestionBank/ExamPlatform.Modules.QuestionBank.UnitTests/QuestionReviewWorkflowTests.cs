using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>The review and approval workflow of a question (FR-8).</summary>
public class QuestionReviewWorkflowTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Staff = Guid.NewGuid();

    private static Question NewQuestion() =>
        Question.Create("<p>Capital of France?</p>", [new NewQuestionOption("Paris", true), new NewQuestionOption("Rome", false)], Staff, Now);

    private static QuestionReviewEntry Submit(Question q) => q.SubmitForReview(Staff, "author@example.com", null, Now);

    private static List<QuestionOptionEdit> Same(Question q) =>
        q.Options.OrderBy(o => o.Order).Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();

    [Fact]
    public void ANewQuestion_IsADraft() => Assert.Equal(QuestionStatus.Draft, NewQuestion().Status);

    [Fact]
    public void TheWholeWorkflow_GoesDraftToReviewToApprovedToRetired_AndBackToDraftWhenRestored()
    {
        var q = NewQuestion();

        Submit(q);
        Assert.Equal(QuestionStatus.InReview, q.Status);
        q.Approve(Staff, "reviewer@example.com", "Fine", Now);
        Assert.Equal(QuestionStatus.Approved, q.Status);
        q.Retire(Staff, null, "Out of date", Now);
        Assert.Equal(QuestionStatus.Retired, q.Status);
        q.Restore(Staff, null, null, Now);
        Assert.Equal(QuestionStatus.Draft, q.Status);
    }

    [Fact]
    public void EachStep_ReturnsAnEntry_NamingWhoDidItWhatTheyDidAndTheVersionTheyActedOn()
    {
        var q = NewQuestion();
        Submit(q);

        var entry = q.Approve(Staff, "reviewer@example.com", "  Looks right  ", Now.AddMinutes(5));

        Assert.Equal(q.Id, entry.QuestionId);
        Assert.Equal(QuestionReviewEntryKind.Approved, entry.Kind);
        Assert.Equal(Staff, entry.ByUserId);
        Assert.Equal("reviewer@example.com", entry.ByLabel);
        Assert.Equal("Looks right", entry.Comment);
        Assert.Equal(1, entry.VersionNumber);
        Assert.Equal(QuestionStatus.Approved, entry.StatusAfter);
        Assert.Equal(Now.AddMinutes(5), entry.CreatedAtUtc);
    }

    [Fact]
    public void AnEntryWithoutAName_IsShownAsStaff() =>
        Assert.Equal("staff", NewQuestion().Comment(Staff, "  ", "Hello", Now).ByLabel);

    [Theory]
    [InlineData("approve")]
    [InlineData("request-changes")]
    [InlineData("restore")]
    public void AStepTheStatusDoesNotAllow_IsRefused_AndChangesNothing(string step)
    {
        var q = NewQuestion(); // a draft

        Assert.Throws<InvalidQuestionStatusError>(() => _ = step switch
        {
            "approve" => q.Approve(Staff, null, null, Now),
            "request-changes" => q.RequestChanges(Staff, null, "No", Now),
            _ => q.Restore(Staff, null, null, Now),
        });
        Assert.Equal(QuestionStatus.Draft, q.Status);
    }

    [Fact]
    public void OnlyADraftCanBePutForward_AndARetiredQuestionCannotBeRetiredAgain()
    {
        var q = NewQuestion();
        Submit(q);
        Assert.Throws<InvalidQuestionStatusError>(() => Submit(q));

        q.Retire(Staff, null, null, Now);
        Assert.Throws<InvalidQuestionStatusError>(() => q.Retire(Staff, null, null, Now));
        Assert.Throws<InvalidQuestionStatusError>(() => Submit(q));
    }

    [Fact]
    public void SendingBack_NeedsAReason_AndReturnsTheQuestionToDraft()
    {
        var q = NewQuestion();
        Submit(q);

        Assert.Throws<InvalidQuestionError>(() => q.RequestChanges(Staff, null, "  ", Now));
        Assert.Equal(QuestionStatus.InReview, q.Status);

        var entry = q.RequestChanges(Staff, null, "Option B is also right", Now);
        Assert.Equal(QuestionStatus.Draft, q.Status);
        Assert.Equal(QuestionReviewEntryKind.ChangesRequested, entry.Kind);
    }

    [Fact]
    public void ACommentChangesNoStatus_NeedsText_AndHasALimit()
    {
        var q = NewQuestion();
        Submit(q);

        var entry = q.Comment(Staff, null, "A thought", Now);

        Assert.Equal(QuestionStatus.InReview, q.Status);
        Assert.Equal(QuestionStatus.InReview, entry.StatusAfter);
        Assert.Throws<InvalidQuestionError>(() => q.Comment(Staff, null, null, Now));
        Assert.Throws<InvalidQuestionError>(() => q.Comment(Staff, null, new string('x', QuestionReviewEntry.MaxCommentLength + 1), Now));
        Assert.Equal(QuestionReviewEntry.MaxCommentLength, q.Comment(Staff, null, new string('x', QuestionReviewEntry.MaxCommentLength), Now).Comment.Length);
    }

    [Theory]
    [InlineData(QuestionStatus.InReview)]
    [InlineData(QuestionStatus.Approved)]
    public void ChangingTheContent_OfAQuestionInReviewOrApproved_ReturnsItToDraft(QuestionStatus status)
    {
        var q = NewQuestion();
        Submit(q);
        if (status == QuestionStatus.Approved)
            q.Approve(Staff, null, null, Now);

        q.Revise("<p>Capital city of France?</p>", Same(q), answered: false, nowUtc: Now.AddMinutes(1));

        Assert.Equal(QuestionStatus.Draft, q.Status);
    }

    [Fact]
    public void SavingAnApprovedQuestionUnchanged_KeepsItApproved()
    {
        var q = NewQuestion();
        Submit(q);
        q.Approve(Staff, null, null, Now);

        q.Revise("<p>Capital of France?</p>", Same(q), answered: false, nowUtc: Now.AddMinutes(1));

        Assert.Equal(QuestionStatus.Approved, q.Status);
    }

    [Fact]
    public void CorrectingTheKey_KeepsAnApprovedQuestionApproved()
    {
        var q = NewQuestion();
        Submit(q);
        q.Approve(Staff, null, null, Now);
        var rome = q.Options.Single(o => o.Text == "Rome").Id;

        q.CorrectAnswerKey([rome], Now.AddMinutes(1));

        Assert.Equal(QuestionStatus.Approved, q.Status);
        Assert.Equal(2, q.CurrentVersionNumber);
    }

    [Fact]
    public void AnApprovalNamesTheVersionApproved()
    {
        var q = NewQuestion();
        q.Revise("<p>Reworded?</p>", Same(q), answered: false, nowUtc: Now.AddMinutes(1));
        Submit(q);

        Assert.Equal(2, q.Approve(Staff, null, null, Now).VersionNumber);
    }

    // ---- the handler ------------------------------------------------------------------------------

    [Fact]
    public async Task TheHandler_TakesTheStep_StoresItsEntry_AndSavesOnce()
    {
        var q = NewQuestion();
        var repository = Substitute.For<IQuestionRepository>();
        repository.GetByIdAsync(q.Id, Arg.Any<CancellationToken>()).Returns(q);
        var unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
        var clock = Substitute.For<Clock>();
        clock.UtcNow.Returns(Now);
        var handler = new QuestionReviewHandler(repository, unitOfWork, clock);

        var result = await handler.SubmitAsync(q.Id, new ReviewActor(Staff, "author@example.com"), "Please look", CancellationToken.None);

        Assert.Equal("in_review", result.Status);
        Assert.Equal("submitted", result.Entry.Kind);
        repository.Received(1).AddReviewEntry(Arg.Is<QuestionReviewEntry>(e => e.Comment == "Please look" && e.QuestionId == q.Id));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheHandler_StoresNothing_WhenTheStepIsRefused()
    {
        var q = NewQuestion();
        var repository = Substitute.For<IQuestionRepository>();
        repository.GetByIdAsync(q.Id, Arg.Any<CancellationToken>()).Returns(q);
        var unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
        var handler = new QuestionReviewHandler(repository, unitOfWork, Substitute.For<Clock>());

        await Assert.ThrowsAsync<InvalidQuestionStatusError>(() => handler.ApproveAsync(q.Id, new ReviewActor(Staff, null), null, CancellationToken.None));

        repository.DidNotReceiveWithAnyArgs().AddReviewEntry(default!);
        await unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }

    [Fact]
    public async Task TheHandler_RefusesAnUnknownQuestion()
    {
        var repository = Substitute.For<IQuestionRepository>();
        var handler = new QuestionReviewHandler(repository, Substitute.For<IQuestionBankUnitOfWork>(), Substitute.For<Clock>());

        await Assert.ThrowsAsync<QuestionNotFoundError>(() => handler.CommentAsync(Guid.NewGuid(), new ReviewActor(Staff, null), "Hi", CancellationToken.None));
    }

    // ---- what exams may use ---------------------------------------------------------------------

    [Theory]
    [InlineData(false, QuestionStatus.Draft, false)]
    [InlineData(false, QuestionStatus.InReview, false)]
    [InlineData(false, QuestionStatus.Approved, false)]
    [InlineData(false, QuestionStatus.Retired, true)]
    [InlineData(true, QuestionStatus.Draft, true)]
    [InlineData(true, QuestionStatus.InReview, true)]
    [InlineData(true, QuestionStatus.Approved, false)]
    [InlineData(true, QuestionStatus.Retired, true)]
    public void ARetiredQuestionIsNeverUsable_AndWhereApprovalIsRequiredOnlyAnApprovedOneIs(bool requireApproval, QuestionStatus status, bool unusable)
    {
        var reason = new QuestionApprovalPolicy(requireApproval).UnusableReason(status);

        Assert.Equal(unusable, reason is not null);
    }

    [Fact]
    public async Task TheBankTellsOtherModulesWhetherAQuestionIsUsable_AndWhichOnesADrawMayPickFrom()
    {
        var retired = NewQuestion();
        retired.Retire(Staff, null, null, Now);
        var repository = Substitute.For<IQuestionRepository>();
        repository.GetManyAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([retired]);
        repository.GetCurrentVersionNumbersAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, int>());
        repository.FindPlacementsAsync(Arg.Any<QuestionFilter>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);
        var books = Substitute.For<IBookRepository>();
        books.GetChapterRefsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, ChapterRef>());

        var open = new QuestionBankReader(repository, books);
        var strict = new QuestionBankReader(repository, books, new QuestionApprovalPolicy(RequireApproval: true));

        Assert.NotNull(Assert.Single(await open.GetAsync([retired.Id], CancellationToken.None)).UnusableReason);
        await open.FindAsync(new QuestionCriteria(), CancellationToken.None);
        await strict.FindAsync(new QuestionCriteria(), CancellationToken.None);

        await repository.Received(1).FindPlacementsAsync(
            Arg.Is<QuestionFilter>(f => f.Statuses!.SequenceEqual(new[] { QuestionStatus.Draft, QuestionStatus.InReview, QuestionStatus.Approved })), Arg.Any<int>(), Arg.Any<CancellationToken>());
        await repository.Received(1).FindPlacementsAsync(
            Arg.Is<QuestionFilter>(f => f.Statuses!.SequenceEqual(new[] { QuestionStatus.Approved })), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("draft", QuestionStatus.Draft)]
    [InlineData("IN_REVIEW", QuestionStatus.InReview)]
    [InlineData("in-review", QuestionStatus.InReview)]
    [InlineData("Approved", QuestionStatus.Approved)]
    [InlineData(" retired ", QuestionStatus.Retired)]
    public void StatusText_IsReadIgnoringCaseAndSeparators(string text, QuestionStatus expected) =>
        Assert.Equal(expected, QuestionStatusText.Parse(text));

    [Fact]
    public void StatusText_NoneMeansNoFilter_AndANonsenseNameIsRefused()
    {
        Assert.Null(QuestionStatusText.Parse(null));
        Assert.Null(QuestionStatusText.Parse("  "));
        Assert.Throws<InvalidQuestionError>(() => QuestionStatusText.Parse("nope"));
    }
}
