using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class EditQuestionHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private readonly IQuestionRepository repository = Substitute.For<IQuestionRepository>();
    private readonly IBookRepository books = Substitute.For<IBookRepository>();
    private readonly IQuestionBankUnitOfWork unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly IQuestionUsageSource usageSource = Substitute.For<IQuestionUsageSource>();
    private readonly EditQuestionHandler handler;

    public EditQuestionHandlerTests()
    {
        // The real sanitizer, not a stub: the rules are about what survives the cleaning.
        var usage = new QuestionUsageReader([usageSource]);
        handler = new EditQuestionHandler(repository, unitOfWork, new RichTextSanitizer(), usage, new QuestionDtoFactory(books));
        usageSource.FindAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns([]);
    }

    private Question Stored()
    {
        var question = Question.Create("<p>Capital of France?</p>", [new("Paris", true), new("Rome", false)], Guid.NewGuid(), Now);
        repository.GetByIdAsync(question.Id, Arg.Any<CancellationToken>()).Returns(question);
        return question;
    }

    private static List<QuestionOptionEdit> Unchanged(Question q) => q.Options.Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();

    private void SaysAnswered(Question question) =>
        usageSource.FindAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new QuestionUse(question.Id, QuestionUseKind.Answered, "Answered by candidates")]);

    [Fact]
    public async Task Edit_StoresTheSanitizedHtml_NotWhatWasSent_AndSavesOnce()
    {
        var question = Stored();

        var dto = await handler.HandleAsync(
            new EditQuestionCommand(question.Id, "<p>Capital of <em>Spain</em>?<script>alert(1)</script></p>", Unchanged(question)), CancellationToken.None);

        Assert.Equal("<p>Capital of <em>Spain</em>?</p>", dto.Text);
        Assert.Equal("<p>Capital of <em>Spain</em>?</p>", question.Text);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Edit_ReportsTheQuestionWithItsUsage()
    {
        var question = Stored();
        SaysAnswered(question);

        var dto = await handler.HandleAsync(new EditQuestionCommand(question.Id, "<p>Capital of France ?</p>", Unchanged(question)), CancellationToken.None);

        Assert.True(dto.Usage!.Answered);
    }

    [Fact]
    public async Task AnUnknownQuestion_IsNotFound_AndNothingIsSaved()
    {
        var error = await Record.ExceptionAsync(() => handler.HandleAsync(new EditQuestionCommand(Guid.NewGuid(), "<p>Q</p>", []), CancellationToken.None));

        Assert.IsType<QuestionNotFoundError>(error);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("<p></p>")]
    [InlineData("<script>alert(1)</script>")]
    public async Task NothingVisible_IsRefused_AndNothingIsSaved(string? text)
    {
        var question = Stored();

        await Assert.ThrowsAsync<InvalidQuestionError>(() => handler.HandleAsync(new EditQuestionCommand(question.Id, text, Unchanged(question)), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal("<p>Capital of France?</p>", question.Text);
    }

    [Fact]
    public async Task AnAnsweredQuestion_AllowsWordingChanges()
    {
        var question = Stored();
        SaysAnswered(question);
        var edits = Unchanged(question).Select(o => o with { Text = o.Text + "." }).ToList();

        var dto = await handler.HandleAsync(new EditQuestionCommand(question.Id, "<p>Capital of France ?</p>", edits), CancellationToken.None);

        Assert.Equal(["Paris.", "Rome."], dto.Options.Select(o => o.Text));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAnsweredQuestion_RefusesAKeyChange_AndSavesNothing()
    {
        var question = Stored();
        SaysAnswered(question);
        var edits = Unchanged(question);
        edits[0] = edits[0] with { IsCorrect = false };
        edits[1] = edits[1] with { IsCorrect = true };

        await Assert.ThrowsAsync<QuestionLockedError>(() => handler.HandleAsync(new EditQuestionCommand(question.Id, "<p>Capital of France?</p>", edits), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.True(question.Options[0].IsCorrect);
    }

    [Fact]
    public async Task AQuestionNobodyHasAnswered_AllowsAKeyChange_EvenInsideAnExam()
    {
        var question = Stored();
        usageSource.FindAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns([new QuestionUse(question.Id, QuestionUseKind.InExam, "Maths mock")]);
        var edits = Unchanged(question);
        edits[0] = edits[0] with { IsCorrect = false };
        edits[1] = edits[1] with { IsCorrect = true };

        var dto = await handler.HandleAsync(new EditQuestionCommand(question.Id, "<p>Capital of France?</p>", edits), CancellationToken.None);

        Assert.Equal("Rome", dto.Options.Single(o => o.IsCorrect).Text);
        Assert.Equal(1, dto.Usage!.ExamCount);
    }
}
