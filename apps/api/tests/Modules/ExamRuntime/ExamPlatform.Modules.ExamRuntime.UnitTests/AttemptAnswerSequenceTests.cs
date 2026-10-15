using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// NFR-4 and ADR 0004: a write to an answer (a save or a clear) is applied only when its sequence number is higher than the one held for the
/// question, so a request that arrives late cannot replace a newer answer or bring back one that was cleared.
/// </summary>
public class AttemptAnswerSequenceTests
{
    private static readonly DateTime Start = Fixtures.Now;

    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), 1, Start, Start.AddMinutes(30));

    private static DateTime At(int minutes) => Start.AddMinutes(minutes);

    private static long HeldFor(Attempt attempt, Guid question) =>
        attempt.AnswerSequences.SingleOrDefault(s => s.QuestionId == question)?.Value ?? 0;

    [Fact]
    public void ASaveWithAHigherSequence_ReplacesTheAnswer_AndRaisesTheNumber()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var second = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), At(1), sequence: 1);

        attempt.RecordAnswer(question, second, At(2), sequence: 2);

        Assert.Equal(second, Assert.Single(attempt.Answers).SelectedOptionId);
        Assert.Equal(2, HeldFor(attempt, question));
    }

    [Fact]
    public void ASaveWithAnOlderSequence_IsRefusedWithTheNumberHeld_AndLeavesTheAnswerAlone()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var newer = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), At(1), sequence: 1);
        attempt.RecordAnswer(question, newer, At(2), sequence: 2);

        // The first save's request, delivered late.
        var error = Assert.Throws<StaleAnswerError>(() => attempt.RecordAnswer(question, Guid.NewGuid(), At(3), sequence: 1));

        Assert.Equal(2, error.HeldSequence);
        Assert.Equal(newer, Assert.Single(attempt.Answers).SelectedOptionId);
        Assert.Equal(2, HeldFor(attempt, question));
    }

    [Fact]
    public void ARepeatOfTheSameSequence_IsRefused_SoAClientThatMeantItSendsItAboveTheNumberHeld()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), At(1), sequence: 4);

        var error = Assert.Throws<StaleAnswerError>(() => attempt.RecordAnswer(question, Guid.NewGuid(), At(2), sequence: 4));

        Assert.Equal(4, error.HeldSequence);
    }

    [Fact]
    public void ASaveWithNoSequence_TakesTheNextNumberUp_SoAnOlderOneFromANewClientIsStillRefused()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), At(1), sequence: 3);

        // A client from before sequence numbers: it is the newest by arrival, as every write was before them.
        var legacy = Guid.NewGuid();
        attempt.RecordAnswer(question, legacy, At(2));

        Assert.Equal(4, HeldFor(attempt, question));
        Assert.Throws<StaleAnswerError>(() => attempt.RecordAnswer(question, Guid.NewGuid(), At(3), sequence: 4));
        Assert.Equal(legacy, Assert.Single(attempt.Answers).SelectedOptionId);
    }

    [Fact]
    public void WritesWithNoSequenceAtAll_KeepTheOrderOfArrival()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var last = Guid.NewGuid();

        attempt.RecordAnswer(question, Guid.NewGuid(), At(1));
        attempt.RecordAnswer(question, Guid.NewGuid(), At(2));
        attempt.RecordAnswer(question, last, At(3));

        Assert.Equal(last, Assert.Single(attempt.Answers).SelectedOptionId);
        Assert.Equal(3, HeldFor(attempt, question));
    }

    [Fact]
    public void AClear_KeepsItsNumber_SoALateSaveFromBeforeItCannotBringTheAnswerBack()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), At(1), sequence: 1);
        attempt.ClearAnswer(question, At(2), sequence: 2);

        // The save of sequence 1 was still on its way when the candidate cleared the question.
        Assert.Throws<StaleAnswerError>(() => attempt.RecordAnswer(question, Guid.NewGuid(), At(3), sequence: 1));

        Assert.Empty(attempt.Answers);
        Assert.Equal(2, HeldFor(attempt, question));
    }

    [Fact]
    public void AClearWithAnOlderSequence_LeavesTheNewerAnswer()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var newer = Guid.NewGuid();
        attempt.RecordAnswer(question, newer, At(1), sequence: 5);

        // A clear from before the save, delivered late.
        Assert.Throws<StaleAnswerError>(() => attempt.ClearAnswer(question, At(2), sequence: 4));

        Assert.Equal(newer, Assert.Single(attempt.Answers).SelectedOptionId);
    }

    [Fact]
    public void AClearOfAnUnansweredQuestion_StillTakesItsNumber()
    {
        var attempt = Open();
        var question = Guid.NewGuid();

        attempt.ClearAnswer(question, At(1), sequence: 2);

        // The save of sequence 1 had not arrived when the clear did.
        Assert.Throws<StaleAnswerError>(() => attempt.RecordAnswer(question, Guid.NewGuid(), At(2), sequence: 1));
        Assert.Empty(attempt.Answers);
    }

    [Fact]
    public void AnAnswerSavedAfterAClear_NeedsAHigherNumberThanTheClear()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var again = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), At(1), sequence: 1);
        attempt.ClearAnswer(question, At(2), sequence: 2);

        attempt.RecordAnswer(question, again, At(3), sequence: 3);

        Assert.Equal(again, Assert.Single(attempt.Answers).SelectedOptionId);
    }

    [Fact]
    public void ATypedAnswer_FollowsTheSameRule()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordTextAnswer(question, "newer", At(1), sequence: 2);

        var error = Assert.Throws<StaleAnswerError>(() => attempt.RecordTextAnswer(question, "older", At(2), sequence: 1));

        Assert.Equal(2, error.HeldSequence);
        Assert.Equal("newer", Assert.Single(attempt.Answers).AnswerText);
    }

    [Fact]
    public void TheNumbersAreKeptPerQuestion()
    {
        var attempt = Open();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        attempt.RecordAnswer(first, Guid.NewGuid(), At(1), sequence: 9);

        // The second question has had no writes, so its first write is number 1, whatever the first question has reached.
        attempt.RecordAnswer(second, Guid.NewGuid(), At(2), sequence: 1);

        Assert.Equal(9, HeldFor(attempt, first));
        Assert.Equal(1, HeldFor(attempt, second));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(AttemptAnswerSequence.MaxValue + 1)]
    public void ASequenceOutsideTheRangeAClientMaySend_IsRefused_WithoutTouchingTheAnswer(long sequence)
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        var kept = Guid.NewGuid();
        attempt.RecordAnswer(question, kept, At(1), sequence: 1);

        Assert.Throws<InvalidAttemptError>(() => attempt.RecordAnswer(question, Guid.NewGuid(), At(2), sequence));
        Assert.Throws<InvalidAttemptError>(() => attempt.ClearAnswer(question, At(2), sequence));

        Assert.Equal(kept, Assert.Single(attempt.Answers).SelectedOptionId);
        Assert.Equal(1, HeldFor(attempt, question));
    }

    [Fact]
    public void TheLargestNumberAClientMaySend_IsAccepted()
    {
        var attempt = Open();
        var question = Guid.NewGuid();

        attempt.RecordAnswer(question, Guid.NewGuid(), At(1), sequence: AttemptAnswerSequence.MaxValue);

        Assert.Equal(AttemptAnswerSequence.MaxValue, HeldFor(attempt, question));
    }

    [Fact]
    public void AWriteRefusedForAnotherReason_DoesNotUseUpItsNumber()
    {
        var attempt = Open();
        var question = Guid.NewGuid();

        // Blank text is refused, and the number it carried is free for the write that follows.
        Assert.Throws<InvalidAttemptError>(() => attempt.RecordTextAnswer(question, "   ", At(1), sequence: 5));
        Assert.Throws<InvalidAttemptError>(() => attempt.RecordAnswer(question, [], At(1), sequence: 5));
        Assert.Equal(0, HeldFor(attempt, question));

        attempt.RecordTextAnswer(question, "typed", At(2), sequence: 5);

        Assert.Equal("typed", Assert.Single(attempt.Answers).AnswerText);
    }

    [Fact]
    public void AStaleWrite_ChangesNothingAtAll_NotEvenTheTimeTheAnswerWasSaved()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), At(1), sequence: 2);

        Assert.Throws<StaleAnswerError>(() => attempt.RecordAnswer(question, Guid.NewGuid(), At(9), sequence: 1));

        Assert.Equal(At(1), Assert.Single(attempt.Answers).AnsweredAtUtc);
    }

    [Fact]
    public void AWriteToAClosedAttempt_IsRefusedAsClosed_BeforeItsNumberIsLookedAt()
    {
        var attempt = Open();
        var question = Guid.NewGuid();
        attempt.RecordAnswer(question, Guid.NewGuid(), At(1), sequence: 3);

        // Time has run out: the attempt is closed, which is the answer whatever the number says.
        Assert.Throws<AttemptTimeExpiredError>(() => attempt.RecordAnswer(question, Guid.NewGuid(), At(31), sequence: 1));
    }

    [Fact]
    public void TheStaleError_CarriesTheNumberHeld_ForTheResponse()
    {
        var error = new StaleAnswerError(7);

        Assert.Equal("answer_superseded", error.ErrorCode);
        Assert.Equal(409, error.HttpStatusCode);
        Assert.Equal(7L, error.Extensions["currentSequence"]);
    }
}
