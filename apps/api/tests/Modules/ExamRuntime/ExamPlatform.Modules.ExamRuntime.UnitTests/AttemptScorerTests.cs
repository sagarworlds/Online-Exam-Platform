using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

public class AttemptScorerTests
{
    private static Dictionary<Guid, QuestionSnapshot> Index(params QuestionSnapshot[] questions) =>
        questions.ToDictionary(q => q.Id);

    private static Attempt AttemptWith(params (QuestionSnapshot Question, Guid Option)[] answers)
    {
        var attempt = Attempt.Start(Guid.NewGuid(), Guid.NewGuid(), Fixtures.Now, Fixtures.Now.AddHours(1));
        foreach (var (question, option) in answers)
            attempt.RecordAnswer(question.Id, option, Fixtures.Now);
        return attempt;
    }

    [Fact]
    public void Score_AwardsTheCorrectMarksForRightAnswersAndNothingElseByDefault()
    {
        var q1 = Fixtures.Question();
        var q2 = Fixtures.Question();
        var q3 = Fixtures.Question();
        var exam = Fixtures.Exam([q1, q2, q3]);
        var attempt = AttemptWith((q1, q1.Correct()), (q2, q2.Wrong()));

        var result = AttemptScorer.Score(exam, Index(q1, q2, q3), attempt.Answers.ToList());

        Assert.Equal(1m, result.Score);
        Assert.Equal(3m, result.MaxScore);
    }

    [Fact]
    public void Score_AppliesTheExamsOwnMarkingScheme_IncludingNegativeMarking()
    {
        var right = Fixtures.Question();
        var wrong = Fixtures.Question();
        var skipped = Fixtures.Question();
        var exam = Fixtures.Exam([right, wrong, skipped], correct: 4m, incorrect: -1m, unattempted: -0.5m);
        var attempt = AttemptWith((right, right.Correct()), (wrong, wrong.Wrong()));

        var result = AttemptScorer.Score(exam, Index(right, wrong, skipped), attempt.Answers.ToList());

        Assert.Equal(4m - 1m - 0.5m, result.Score);
        Assert.Equal(12m, result.MaxScore);
    }

    [Fact]
    public void Score_WithNoAnswers_IsTheUnattemptedMarksOfEveryQuestion()
    {
        var q1 = Fixtures.Question();
        var q2 = Fixtures.Question();
        var exam = Fixtures.Exam([q1, q2], unattempted: -0.25m);

        var result = AttemptScorer.Score(exam, Index(q1, q2), []);

        Assert.Equal(-0.5m, result.Score);
        Assert.Equal(2m, result.MaxScore);
    }

    [Fact]
    public void Score_IgnoresAnAnswerToAQuestionThatIsNotInTheExam()
    {
        var inExam = Fixtures.Question();
        var stray = Fixtures.Question();
        var exam = Fixtures.Exam([inExam]);
        var attempt = AttemptWith((stray, stray.Correct()));

        var result = AttemptScorer.Score(exam, Index(inExam, stray), attempt.Answers.ToList());

        Assert.Equal(0m, result.Score);
        Assert.Equal(1m, result.MaxScore);
    }

    [Fact]
    public void Score_WhenTheBankNoLongerReturnsAQuestion_FailsLoudlyInsteadOfGuessingMarks()
    {
        var q1 = Fixtures.Question();
        var exam = Fixtures.Exam([q1]);

        var error = Assert.Throws<ExamContentUnavailableError>(() => AttemptScorer.Score(exam, Index(), []));

        Assert.Equal(500, error.HttpStatusCode);
    }

    [Fact]
    public void Score_WhenAnAnswerNamesAnOptionTheQuestionDoesNotHave_FailsLoudly()
    {
        var q1 = Fixtures.Question();
        var exam = Fixtures.Exam([q1]);
        var attempt = AttemptWith((q1, Guid.NewGuid()));

        Assert.Throws<ExamContentUnavailableError>(() => AttemptScorer.Score(exam, Index(q1), attempt.Answers.ToList()));
    }
}
