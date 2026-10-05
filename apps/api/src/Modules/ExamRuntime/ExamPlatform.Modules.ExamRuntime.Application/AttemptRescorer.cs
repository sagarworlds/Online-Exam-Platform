using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// ExamRuntime's side of a question-bank answer-key correction (FR-31, ADR 0001): QuestionBank asks for this through
/// <see cref="IAttemptRescorer"/> once it has saved the corrected key, and this is the one place that recomputes a
/// submitted attempt's score from scratch outside the normal submit flow.
/// </summary>
public sealed class AttemptRescorer(
    IAttemptRepository attempts, IExamCatalog examCatalog, IQuestionBank questionBank, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
    : IAttemptRescorer
{
    /// <inheritdoc />
    public async Task<int> RescoreForQuestionAsync(Guid questionId, string reason, CancellationToken cancellationToken)
    {
        var affected = await attempts.ListSubmittedByQuestionIdAsync(questionId, cancellationToken);
        if (affected.Count == 0)
            return 0;

        var nowUtc = clock.UtcNow;
        var changed = 0;

        // Grouped by exam, so an exam with many affected attempts reads its questions from the bank once, not once per attempt.
        foreach (var group in affected.GroupBy(a => a.ExamId))
        {
            var exam = await examCatalog.FindAsync(group.Key, cancellationToken);
            if (exam is null)
                continue; // The exam itself is gone; nothing left to rescore its attempts against.

            var questionIds = exam.Sections.SelectMany(s => s.QuestionIds).ToList();
            var questions = (await questionBank.GetAsync(questionIds, cancellationToken)).ToDictionary(q => q.Id);

            foreach (var attempt in group)
            {
                // The exam as this attempt actually sat it (its drawn paper, if it drew one), under the now-corrected key.
                var examForAttempt = exam.For(attempt);
                var result = AttemptScorer.Score(examForAttempt, questions, attempt.Answers.ToList());
                if (attempt.ReviseScore(result.Score, result.MaxScore, reason, nowUtc))
                    changed++;
            }
        }

        if (changed > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return changed;
    }
}
