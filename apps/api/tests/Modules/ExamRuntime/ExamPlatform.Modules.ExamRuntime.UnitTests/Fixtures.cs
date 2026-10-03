using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Builders for the snapshots other modules hand to ExamRuntime, so each test states only what it cares about.</summary>
internal static class Fixtures
{
    public static readonly DateTime Now = new(2026, 10, 2, 9, 0, 0, DateTimeKind.Utc);

    /// <summary>A question whose first option is the correct one.</summary>
    public static QuestionSnapshot Question(string text = "2 + 2?") =>
        new(Guid.NewGuid(), text,
        [
            new QuestionOptionSnapshot(Guid.NewGuid(), "4", IsCorrect: true),
            new QuestionOptionSnapshot(Guid.NewGuid(), "5", IsCorrect: false),
            new QuestionOptionSnapshot(Guid.NewGuid(), "22", IsCorrect: false),
        ]);

    /// <summary>A multiple-answer question: the first two options are both correct, the last two are not.</summary>
    public static QuestionSnapshot MultiQuestion(string text = "Which are prime?") =>
        new(Guid.NewGuid(), text,
        [
            new QuestionOptionSnapshot(Guid.NewGuid(), "2", IsCorrect: true),
            new QuestionOptionSnapshot(Guid.NewGuid(), "3", IsCorrect: true),
            new QuestionOptionSnapshot(Guid.NewGuid(), "4", IsCorrect: false),
            new QuestionOptionSnapshot(Guid.NewGuid(), "6", IsCorrect: false),
        ], AllowsMultiple: true);

    /// <summary>Every correct option of the question, for a multiple-answer one.</summary>
    public static Guid[] CorrectSet(this QuestionSnapshot question) => question.Options.Where(o => o.IsCorrect).Select(o => o.Id).ToArray();

    public static Guid Correct(this QuestionSnapshot question) => question.Options.Single(o => o.IsCorrect).Id;

    public static Guid Wrong(this QuestionSnapshot question) => question.Options.First(o => !o.IsCorrect).Id;

    /// <summary>A published exam open from an hour ago to two hours from now, one section holding the given questions.</summary>
    public static ExamSnapshot Exam(
        IEnumerable<QuestionSnapshot> questions,
        bool published = true,
        DateTime? start = null,
        DateTime? end = null,
        DateTime? lateEntry = null,
        int? durationSeconds = 1800,
        decimal correct = 1m,
        decimal incorrect = 0m,
        decimal unattempted = 0m,
        ExamResultReleaseMode resultRelease = ExamResultReleaseMode.Instant,
        DateTime? resultReleaseTime = null) =>
        new(
            Guid.NewGuid(),
            "Physics",
            "Mechanics",
            published,
            start ?? Now.AddHours(-1),
            end ?? Now.AddHours(2),
            lateEntry,
            durationSeconds,
            correct,
            incorrect,
            unattempted,
            [new ExamSectionSnapshot(Guid.NewGuid(), "Section A", 1, questions.Select(q => q.Id).ToList())],
            resultRelease,
            resultReleaseTime);
}
