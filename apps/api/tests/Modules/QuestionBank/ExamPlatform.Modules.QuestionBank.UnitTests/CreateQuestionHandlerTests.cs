using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.SharedKernel.Application;
using NSubstitute;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

public class CreateQuestionHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Author = Guid.NewGuid();

    private readonly IQuestionRepository repository = Substitute.For<IQuestionRepository>();
    private readonly IQuestionBankUnitOfWork unitOfWork = Substitute.For<IQuestionBankUnitOfWork>();
    private readonly CreateQuestionHandler handler;

    public CreateQuestionHandlerTests()
    {
        var clock = Substitute.For<Clock>();
        clock.UtcNow.Returns(Now);
        // The real sanitizer, not a stub: these rules are about what survives the cleaning.
        handler = new CreateQuestionHandler(repository, unitOfWork, new RichTextSanitizer(), clock);
    }

    private static List<NewQuestionOption> TwoOptions() => [new("Paris", true), new("Rome", false)];

    private Task<ExamPlatform.Modules.QuestionBank.Application.Dtos.QuestionDto> Create(string? text) =>
        handler.HandleAsync(new CreateQuestionCommand(text, TwoOptions(), Author), CancellationToken.None);

    [Fact]
    public async Task Create_StoresTheSanitizedHtmlNotWhatWasSent()
    {
        var created = await Create("<p>Capital of <strong>France</strong>?<script>alert(1)</script></p>");

        Assert.Equal("<p>Capital of <strong>France</strong>?</p>", created.Text);
        repository.Received(1).Add(Arg.Is<Question>(q => q.Text == created.Text));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<p></p>")]
    [InlineData("<p><br></p>")]
    [InlineData("<script>alert(1)</script>")]
    public async Task Create_WithNothingVisible_IsRefusedAndNothingIsStored(string? text)
    {
        var error = await Assert.ThrowsAsync<InvalidQuestionError>(() => Create(text));

        Assert.Equal("The question text is required.", error.Message);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }

    [Fact]
    public async Task Create_CountsReadableTextNotMarkup()
    {
        // Heavy formatting must not eat the allowance: the readable text is exactly at the limit.
        var text = "<p>" + string.Concat(Enumerable.Repeat("<strong>x</strong>", Question.MaxVisibleTextLength)) + "</p>";

        var created = await Create(text);

        Assert.True(created.Text.Length > Question.MaxVisibleTextLength);
    }

    [Fact]
    public async Task Create_WithMoreReadableTextThanAllowed_IsRefused()
    {
        var text = "<p>" + new string('x', Question.MaxVisibleTextLength + 1) + "</p>";

        var error = await Assert.ThrowsAsync<InvalidQuestionError>(() => Create(text));

        Assert.Contains($"{Question.MaxVisibleTextLength} characters", error.Message);
        repository.DidNotReceive().Add(Arg.Any<Question>());
    }
}
