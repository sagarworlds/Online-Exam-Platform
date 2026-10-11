using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamAuthoring.UnitTests;

public class InstructionTemplateTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_TrimsTheTitleAndBody()
    {
        var template = InstructionTemplate.Create("  Board exam rules  ", "\n Answer all questions. \n", Now);

        Assert.Equal("Board exam rules", template.Title);
        Assert.Equal("Answer all questions.", template.Body);
        Assert.Equal(Now, template.CreatedAtUtc);
        Assert.Equal(Now, template.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(null, "Body")]
    [InlineData("   ", "Body")]
    [InlineData("Title", null)]
    [InlineData("Title", "   ")]
    public void Create_WithABlankTitleOrBody_Throws(string? title, string? body)
    {
        Assert.Throws<InvalidInstructionTemplateError>(() => InstructionTemplate.Create(title, body, Now));
    }

    [Fact]
    public void Create_WithATitleOverTheLimit_Throws()
    {
        var title = new string('t', InstructionTemplate.MaxTitleLength + 1);

        Assert.Throws<InvalidInstructionTemplateError>(() => InstructionTemplate.Create(title, "Body", Now));
    }

    [Fact]
    public void Create_WithABodyOverTheLimit_Throws()
    {
        // The same limit as an exam's instructions, because the body is copied into one.
        var body = new string('b', Exam.MaxInstructionsLength + 1);

        Assert.Throws<InvalidInstructionTemplateError>(() => InstructionTemplate.Create("Title", body, Now));
    }

    [Fact]
    public void Change_UpdatesTheTemplate_AndStampsTheChange()
    {
        var template = InstructionTemplate.Create("Old", "Old body", Now);
        var later = Now.AddDays(1);

        template.Change("New", "New body", later);

        Assert.Equal("New", template.Title);
        Assert.Equal("New body", template.Body);
        Assert.Equal(Now, template.CreatedAtUtc);
        Assert.Equal(later, template.UpdatedAtUtc);
    }

    [Fact]
    public void Change_WhenRejected_KeepsTheOldText()
    {
        var template = InstructionTemplate.Create("Old", "Old body", Now);

        Assert.Throws<InvalidInstructionTemplateError>(() => template.Change("New", "  ", Now.AddDays(1)));

        Assert.Equal("Old", template.Title);
        Assert.Equal("Old body", template.Body);
        Assert.Equal(Now, template.UpdatedAtUtc);
    }
}
