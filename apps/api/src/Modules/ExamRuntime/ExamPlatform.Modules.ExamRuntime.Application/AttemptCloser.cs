using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Scores and closes an attempt. Used both when a candidate submits and when somebody touches an attempt
/// whose deadline has passed: with no background worker in this first cut, an abandoned attempt is closed the
/// next time anyone looks at it, at its deadline, with whatever answers were saved.
/// </summary>
public sealed class AttemptCloser(IQuestionBank questionBank, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Marks the attempt, ends it and saves it.</summary>
    /// <param name="attempt">The open attempt to close.</param>
    /// <param name="exam">The exam it is an attempt at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted.</exception>
    /// <exception cref="ExamContentUnavailableError">The exam's questions cannot be read, so it cannot be marked.</exception>
    public async Task CloseAsync(Attempt attempt, ExamSnapshot exam, CancellationToken cancellationToken)
    {
        var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = (await questionBank.GetAsync(questionIds, cancellationToken)).ToDictionary(q => q.Id);

        var result = AttemptScorer.Score(exam, questions, attempt.Answers.ToList());
        attempt.Submit(clock.UtcNow, result.Score, result.MaxScore);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
