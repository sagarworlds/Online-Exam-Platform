using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.UnitTests;

/// <summary>Editing a question (FR-5, FR-7): what an author may change, and what stays fixed once candidates have answered.</summary>
public class QuestionReviseTests
{
    private static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    private static Question Capitals() =>
        Question.Create("Capital of France?", [new("Paris", true), new("Rome", false), new("Oslo", false)], Guid.NewGuid(), Now, Guid.NewGuid());

    /// <summary>The question's own options as edits that change nothing, for a test to tweak.</summary>
    private static List<QuestionOptionEdit> Unchanged(Question question) =>
        question.Options.Select(o => new QuestionOptionEdit(o.Id, o.Text, o.IsCorrect)).ToList();

    // ---- before anyone has answered ---------------------------------------------------------------

    [Fact]
    public void Revise_ReplacesTheTextAndTrimsIt()
    {
        var question = Capitals();

        question.Revise("  Capital of Spain?  ", Unchanged(question), answered: false);

        Assert.Equal("Capital of Spain?", question.Text);
    }

    [Fact]
    public void AnOptionThatKeepsItsId_KeepsItsIdentity_WhateverItsTextOrPositionBecomes()
    {
        var question = Capitals();
        var paris = question.Options[0].Id;
        var edits = Unchanged(question);
        edits[0] = edits[0] with { Text = " Paris, France " };
        edits.Reverse();

        question.Revise("Capital of France?", edits, answered: false);

        Assert.Equal(["Oslo", "Rome", "Paris, France"], question.Options.Select(o => o.Text));
        Assert.Equal([1, 2, 3], question.Options.Select(o => o.Order));
        Assert.Equal(paris, question.Options.Single(o => o.Text == "Paris, France").Id);
    }

    [Fact]
    public void ANewOption_GetsItsOwnId_AndAnOptionLeftOutIsRemoved()
    {
        var question = Capitals();
        var rome = question.Options[1].Id;
        var edits = Unchanged(question);
        edits.RemoveAt(1);
        edits.Add(new QuestionOptionEdit(null, "Madrid", false));

        question.Revise("Capital of France?", edits, answered: false);

        Assert.Equal(["Paris", "Oslo", "Madrid"], question.Options.Select(o => o.Text));
        Assert.DoesNotContain(question.Options, o => o.Id == rome);
        Assert.All(question.Options, o => Assert.Equal(question.Id, o.QuestionId));
        Assert.NotEqual(Guid.Empty, question.Options[2].Id);
    }

    [Fact]
    public void Before_AnyAnswer_TheCorrectOptionMayChange()
    {
        var question = Capitals();
        var edits = Unchanged(question);
        edits[0] = edits[0] with { IsCorrect = false };
        edits[1] = edits[1] with { IsCorrect = true };

        question.Revise("Capital of France?", edits, answered: false);

        Assert.Equal("Rome", question.Options.Single(o => o.IsCorrect).Text);
    }

    [Fact]
    public void ReviseLeavesWhereTheQuestionIsFiledAlone()
    {
        var question = Capitals();
        var chapter = question.ChapterId;

        question.Revise("Capital of France?", Unchanged(question), answered: false);

        Assert.Equal(chapter, question.ChapterId);
    }

    // ---- the same rules as creating ---------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankText_IsRefused(string? text)
    {
        var question = Capitals();

        var error = Assert.Throws<InvalidQuestionError>(() => question.Revise(text, Unchanged(question), answered: false));

        Assert.Equal("The question text is required.", error.Message);
    }

    [Fact]
    public void TooFewOrTooManyOptions_AreRefused()
    {
        var question = Capitals();

        Assert.Throws<InvalidQuestionError>(() => question.Revise("Q?", Unchanged(question).Take(1).ToList(), answered: false));
        Assert.Throws<InvalidQuestionError>(() => question.Revise("Q?", null, answered: false));
        var seven = Enumerable.Range(1, 7).Select(i => new QuestionOptionEdit(null, $"Option {i}", i == 1)).ToList();
        Assert.Throws<InvalidQuestionError>(() => question.Revise("Q?", seven, answered: false));
    }

    [Fact]
    public void ZeroOrTwoCorrectOptions_AreRefused()
    {
        var question = Capitals();
        var none = Unchanged(question).Select(o => o with { IsCorrect = false }).ToList();
        var two = Unchanged(question).Select(o => o with { IsCorrect = o.Text != "Oslo" }).ToList();

        Assert.Equal("Exactly one option must be marked correct.", Assert.Throws<InvalidQuestionError>(() => question.Revise("Q?", none, answered: false)).Message);
        Assert.Equal("Exactly one option must be marked correct.", Assert.Throws<InvalidQuestionError>(() => question.Revise("Q?", two, answered: false)).Message);
    }

    [Fact]
    public void ABlankOrTooLongOption_IsRefused()
    {
        var question = Capitals();
        var blank = Unchanged(question);
        blank[2] = blank[2] with { Text = "  " };
        var tooLong = Unchanged(question);
        tooLong[2] = tooLong[2] with { Text = new string('x', Question.MaxOptionTextLength + 1) };

        Assert.Equal("Every option needs text.", Assert.Throws<InvalidQuestionError>(() => question.Revise("Q?", blank, answered: false)).Message);
        Assert.Throws<InvalidQuestionError>(() => question.Revise("Q?", tooLong, answered: false));
    }

    [Fact]
    public void AnOptionThatBelongsToAnotherQuestion_IsRefused_SoAnIdCannotBeBorrowed()
    {
        var question = Capitals();
        var other = Capitals();
        var edits = Unchanged(question);
        edits[1] = edits[1] with { Id = other.Options[1].Id };

        var error = Assert.Throws<InvalidQuestionError>(() => question.Revise("Q?", edits, answered: false));

        Assert.Equal("An option does not belong to this question.", error.Message);
    }

    [Fact]
    public void TheSameOptionTwice_IsRefused()
    {
        var question = Capitals();
        var edits = Unchanged(question);
        edits[1] = edits[1] with { Id = edits[0].Id };

        Assert.Equal("An option appears more than once.", Assert.Throws<InvalidQuestionError>(() => question.Revise("Q?", edits, answered: false)).Message);
    }

    [Fact]
    public void ARefusedEdit_ChangesNothing()
    {
        var question = Capitals();
        var before = question.Options.Select(o => (o.Id, o.Text, o.IsCorrect, o.Order)).ToList();
        var edits = Unchanged(question);
        edits[2] = edits[2] with { Text = "" };

        Assert.Throws<InvalidQuestionError>(() => question.Revise("Changed text", edits, answered: false));

        Assert.Equal("Capital of France?", question.Text);
        Assert.Equal(before, question.Options.Select(o => (o.Id, o.Text, o.IsCorrect, o.Order)));
    }

    // ---- once candidates have answered ------------------------------------------------------------

    [Fact]
    public void Once_Answered_TheWordingOfTheTextAndOfEveryOptionCanStillBeCorrected()
    {
        var question = Capitals();
        var ids = question.Options.Select(o => o.Id).ToList();
        var edits = Unchanged(question).Select(o => o with { Text = o.Text + "." }).ToList();

        question.Revise("Capital of France ?", edits, answered: true);

        Assert.Equal("Capital of France ?", question.Text);
        Assert.Equal(["Paris.", "Rome.", "Oslo."], question.Options.Select(o => o.Text));
        Assert.Equal(ids, question.Options.Select(o => o.Id));
        Assert.Equal("Paris.", question.Options.Single(o => o.IsCorrect).Text);
    }

    [Fact]
    public void Once_Answered_ChangingTheCorrectOptionIsRefused()
    {
        var question = Capitals();
        var edits = Unchanged(question);
        edits[0] = edits[0] with { IsCorrect = false };
        edits[1] = edits[1] with { IsCorrect = true };

        Assert.Throws<QuestionLockedError>(() => question.Revise("Capital of France?", edits, answered: true));
    }

    [Fact]
    public void Once_Answered_AddingRemovingOrReorderingOptionsIsRefused()
    {
        var question = Capitals();

        var added = Unchanged(question);
        added.Add(new QuestionOptionEdit(null, "Madrid", false));
        var removed = Unchanged(question).Take(2).ToList();
        var reordered = Unchanged(question);
        reordered.Reverse();
        var replaced = Unchanged(question);
        replaced[1] = new QuestionOptionEdit(null, "Madrid", false); // a different option in the old one's place

        Assert.Throws<QuestionLockedError>(() => question.Revise("Capital of France?", added, answered: true));
        Assert.Throws<QuestionLockedError>(() => question.Revise("Capital of France?", removed, answered: true));
        Assert.Throws<QuestionLockedError>(() => question.Revise("Capital of France?", reordered, answered: true));
        Assert.Throws<QuestionLockedError>(() => question.Revise("Capital of France?", replaced, answered: true));
    }

    [Fact]
    public void ARefusedEditOfAnAnsweredQuestion_ChangesNothing_NotEvenTheWording()
    {
        var question = Capitals();
        var edits = Unchanged(question);
        edits[0] = edits[0] with { Text = "Paris!" };
        edits[1] = edits[1] with { IsCorrect = true };
        edits[0] = edits[0] with { IsCorrect = false };

        Assert.Throws<QuestionLockedError>(() => question.Revise("New wording", edits, answered: true));

        Assert.Equal("Capital of France?", question.Text);
        Assert.Equal("Paris", question.Options[0].Text);
        Assert.True(question.Options[0].IsCorrect);
    }

    [Fact]
    public void TheLockedError_ExplainsWhatToDoInstead_AndIsAConflict()
    {
        var error = new QuestionLockedError();

        Assert.Equal("question_locked", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        Assert.Contains("only its wording can change", error.Message);
    }
}
