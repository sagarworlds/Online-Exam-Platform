using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IAnswerAttemptLoader"/>: two queries where <see cref="AttemptRepository.GetByIdAsync"/> takes ten.</summary>
public sealed class AnswerAttemptLoader(ExamRuntimeDbContext context) : IAnswerAttemptLoader
{
    /// <inheritdoc />
    public async Task<Attempt?> LoadForAnswerAsync(Guid attemptId, Guid questionId, CancellationToken cancellationToken)
    {
        // The one answer and the one sequence number the write can touch, in a single query: each is at most one row, so joining them
        // multiplies nothing. Tracked, so the answer saved or removed is written by change tracking as it is for the full attempt.
        var attempt = await context.Attempts
            .AsSingleQuery()
            .Include(a => a.Answers.Where(answer => answer.QuestionId == questionId))
            .Include(a => a.AnswerSequences.Where(sequence => sequence.QuestionId == questionId))
            .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);

        // The paper (the questions this candidate was set) is read apart, because joined to the rows above it would repeat them once per question.
        if (attempt is not null)
            await context.Entry(attempt).Collection(a => a.Paper).LoadAsync(cancellationToken);

        return attempt;
    }
}
