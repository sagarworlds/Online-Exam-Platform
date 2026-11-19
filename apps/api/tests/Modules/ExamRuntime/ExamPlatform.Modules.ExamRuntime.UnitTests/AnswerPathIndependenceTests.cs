using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// Saving or clearing an answer loads only the attempt, its paper and that question's answer and sequence number, so the other collections
/// (marks, times, warnings, departures, sightings) are empty when it runs (see IAnswerAttemptLoader). These tests pin that those writes give the
/// same result whatever else the attempt holds, so a rule added to them later that reads one of the others fails here, and not as a wrong
/// answer in an exam.
/// </summary>
public class AnswerPathIndependenceTests
{
    private static readonly Guid Candidate = Guid.NewGuid();

    private static Attempt Open() => Attempt.Start(Guid.NewGuid(), Candidate, 1, Fixtures.Now.AddMinutes(-5), Fixtures.Now.AddMinutes(55));

    /// <summary>An open attempt that holds something in every collection the answer path does not read.</summary>
    private static Attempt Busy(Guid marked, Guid timed)
    {
        var attempt = Open();
        attempt.SetMarked(marked, true, Fixtures.Now);
        attempt.RecordQuestionTime(timed, 90, Fixtures.Now);
        attempt.RecordFocusViolation(FocusViolationKind.TabHidden, Fixtures.Now);
        attempt.Warn("Please stay on the exam page.", Guid.NewGuid(), Fixtures.Now);
        attempt.NoteClient("203.0.113.9", "device-a", Fixtures.Now);
        return attempt;
    }

    private static (Guid Question, Guid[] Chosen, string? Text, long? Sequence)[] Writes(Guid first, Guid second, Guid third) =>
    [
        (first, [Guid.NewGuid()], null, 1),
        (second, [], "Paris", 4),
        (first, [Guid.NewGuid(), Guid.NewGuid()], null, 2),
        (third, [Guid.NewGuid()], null, null),
    ];

    private static void Apply(Attempt attempt, (Guid Question, Guid[] Chosen, string? Text, long? Sequence)[] writes)
    {
        foreach (var (question, chosen, text, sequence) in writes)
        {
            if (text is not null)
                attempt.RecordTextAnswer(question, text, Fixtures.Now, sequence);
            else
                attempt.RecordAnswer(question, chosen, Fixtures.Now, sequence);
        }
    }

    // Written out as text, so a difference shows as two readable lines.
    private static string Held(Attempt attempt) =>
        "answers: " + string.Join("; ", attempt.Answers.OrderBy(a => a.QuestionId).Select(a => $"{a.QuestionId} [{string.Join(',', a.SelectedOptionIds.Order())}] '{a.AnswerText}'"))
        + "\nsequences: " + string.Join("; ", attempt.AnswerSequences.OrderBy(s => s.QuestionId).Select(s => $"{s.QuestionId}={s.Value}"));

    [Fact]
    public void SavingAndReplacingAnswers_GivesTheSameResult_WhateverElseTheAttemptHolds()
    {
        var (first, second, third) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var writes = Writes(first, second, third);
        var bare = Open();
        var busy = Busy(marked: first, timed: second);

        Apply(bare, writes);
        Apply(busy, writes);

        Assert.Equal(Held(bare), Held(busy));
        Assert.Equal(3, bare.Answers.Count);
    }

    [Fact]
    public void AClear_GivesTheSameResult_WhateverElseTheAttemptHolds()
    {
        var (first, second, third) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var writes = Writes(first, second, third);
        var unknown = Guid.NewGuid();
        var bare = Open();
        var busy = Busy(marked: first, timed: second);

        foreach (var attempt in new[] { bare, busy })
        {
            Apply(attempt, writes);
            attempt.ClearAnswer(second, Fixtures.Now, sequence: 5);
            attempt.ClearAnswer(unknown, Fixtures.Now);
        }

        Assert.Equal(Held(bare), Held(busy));
        Assert.DoesNotContain(bare.Answers, a => a.QuestionId == second);
    }

    [Fact]
    public void AStaleSequenceNumber_IsRefusedTheSameWay_WhateverElseTheAttemptHolds()
    {
        var question = Guid.NewGuid();
        foreach (var attempt in new[] { Open(), Busy(marked: question, timed: question) })
        {
            attempt.RecordAnswer(question, [Guid.NewGuid()], Fixtures.Now, sequence: 7);

            var stale = Assert.Throws<StaleAnswerError>(() => attempt.RecordAnswer(question, [Guid.NewGuid()], Fixtures.Now, sequence: 7));

            Assert.Equal(7, stale.HeldSequence);
        }
    }

    [Fact]
    public void AWriteAfterTheDeadline_IsRefusedTheSameWay_WhateverElseTheAttemptHolds()
    {
        var question = Guid.NewGuid();
        foreach (var attempt in new[] { Open(), Busy(marked: question, timed: question) })
        {
            Assert.Throws<AttemptTimeExpiredError>(() => attempt.RecordAnswer(question, [Guid.NewGuid()], Fixtures.Now.AddHours(2)));
            Assert.Empty(attempt.Answers);
        }
    }
}
