using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// ExamRuntime's side of a question-bank answer-key correction (FR-31, ADR 0001): QuestionBank asks for this through
/// <see cref="IAttemptRescorer"/> once it has saved the corrected key, and this is the one place that recomputes a
/// submitted attempt's score from scratch outside the normal submit flow. A correction also settles the candidates' open disputes
/// of that question: correcting the key is what accepting a dispute means.
/// </summary>
public sealed class AttemptRescorer(
    IAttemptRepository attempts,
    IExamCatalog examCatalog,
    IQuestionBank questionBank,
    IDisputeRepository disputes,
    IExamRuntimeUnitOfWork unitOfWork,
    IRequestContext requestContext,
    Clock clock)
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
        var repinned = false;

        // Grouped by exam, so an exam with many affected attempts reads its questions from the bank once, not once per attempt.
        foreach (var group in affected.GroupBy(a => a.ExamId))
        {
            var exam = await examCatalog.FindAsync(group.Key, cancellationToken);
            if (exam is null)
                continue; // The exam itself is gone; nothing left to rescore its attempts against.

            // The corrected question as it is now: the version that holds the new key.
            var corrected = (await questionBank.GetAsync([questionId], cancellationToken)).FirstOrDefault();

            foreach (var attempt in group)
            {
                // The exam as this attempt actually sat it (its drawn paper, if it drew one), under the now-corrected key.
                var examForAttempt = exam.For(attempt);
                // Every other question stays at the version the attempt sat; only the corrected one moves to its new version (FR-7).
                if (corrected is not null && attempt.QuestionVersions.Count > 0 && attempt.QuestionVersionOf(questionId) != corrected.VersionNumber)
                {
                    attempt.RepinQuestion(questionId, corrected.VersionNumber);
                    repinned = true;
                }
                var questionIds = examForAttempt.Sections.SelectMany(s => s.QuestionIds).ToList();
                var questions = await questionBank.ReadAsync(attempt, questionIds, cancellationToken);
                var result = AttemptScorer.Score(examForAttempt, questions, attempt.Answers.ToList());
                if (attempt.ReviseScore(result.Score, result.MaxScore, reason, nowUtc))
                    changed++;
            }
        }

        // Everyone who disputed this question's key now has their answer: it was corrected. The staff user is whoever is making the
        // correction, since this runs inside their request; outside a signed-in request there is none to name.
        var settled = false;
        foreach (var dispute in await disputes.ListOpenForQuestionAsync(questionId, cancellationToken))
        {
            dispute.Accept(requestContext.UserId, nowUtc, reason);
            settled = true;
        }

        // Saved when a score moved, when an attempt was only moved to the corrected version (which is part of what it shows), or when a
        // dispute was settled.
        if (changed > 0 || repinned || settled)
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return changed;
    }
}
