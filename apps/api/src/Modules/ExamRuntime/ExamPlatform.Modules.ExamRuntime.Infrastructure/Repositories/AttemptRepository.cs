using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;

/// <summary>EF Core-backed <see cref="IAttemptRepository"/>.</summary>
public sealed class AttemptRepository(ExamRuntimeDbContext context) : IAttemptRepository
{
    /// <inheritdoc />
    public void Add(Attempt attempt) => context.Attempts.Add(attempt);

    // Tracked, with answers, marks, the drawn paper and any score revisions loaded, on purpose: the aggregate's rules read
    // them, the review shows the revisions, and handlers rely on change tracking to INSERT, UPDATE or DELETE them; an
    // explicit DbSet.Update would flag every answer Modified.
    /// <inheritdoc />
    public Task<Attempt?> GetByIdAsync(Guid attemptId, CancellationToken cancellationToken) =>
        // Seven sibling collections (Answers, Marks, Paper, Revisions, FocusViolations, Warnings, ClientSightings): split so EF Core issues one
        // query per collection instead of joining all seven and returning their cartesian product.
        context.Attempts.AsSplitQuery()
            .Include(a => a.Answers).Include(a => a.Marks).Include(a => a.Paper).Include(a => a.Revisions).Include(a => a.FocusViolations).Include(a => a.Warnings).Include(a => a.ClientSightings)
            .FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListForCandidateAtExamAsync(Guid examId, Guid candidateId, CancellationToken cancellationToken) =>
        // Starting an exam again resumes an open attempt through this list, and what it shows is built from the attempt's
        // answers and marks, so both are loaded: without the marks a resumed exam would forget what the candidate marked.
        await context.Attempts.AsSplitQuery()
            .Include(a => a.Answers).Include(a => a.Marks).Include(a => a.Paper).Include(a => a.FocusViolations).Include(a => a.Warnings).Include(a => a.ClientSightings)
            .Where(a => a.ExamId == examId && a.CandidateId == candidateId)
            .OrderBy(a => a.Number)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListForExamAsync(Guid examId, CancellationToken cancellationToken) =>
        // Staff see how many departures and warnings each attempt has, so those two are loaded; the answers and paper are not.
        await context.Attempts.AsNoTracking().AsSplitQuery()
            .Include(a => a.FocusViolations).Include(a => a.Warnings).Include(a => a.ClientSightings)
            .Where(a => a.ExamId == examId).OrderBy(a => a.Number).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<Guid>> FindAnsweredQuestionIdsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken) =>
        // A question drawn onto a paper counts like an answered one: the candidate has seen it, so its key must not change either.
        await context.Set<AttemptAnswer>().AsNoTracking()
            .Where(a => questionIds.Contains(a.QuestionId))
            .Select(a => a.QuestionId)
            .Union(context.Set<AttemptQuestion>().AsNoTracking().Where(q => questionIds.Contains(q.QuestionId)).Select(q => q.QuestionId))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubmittedAnswer>> ListSubmittedAnswersAsync(Guid questionId, CancellationToken cancellationToken)
    {
        var rows = await (
            from answer in context.Set<AttemptAnswer>().AsNoTracking()
            join attempt in context.Attempts.AsNoTracking() on answer.AttemptId equals attempt.Id
            where answer.QuestionId == questionId && attempt.Status == AttemptStatus.Submitted && attempt.InvalidatedAtUtc == null
            select new { answer.SelectedOptionIds, answer.AnswerText, attempt.QuestionVersions }).ToListAsync(cancellationToken);

        return rows
            .Select(r => new SubmittedAnswer(
                r.SelectedOptionIds, r.QuestionVersions.TryGetValue(questionId, out var version) ? version : null, r.AnswerText))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListForCandidateAsync(Guid candidateId, CancellationToken cancellationToken) =>
        await context.Attempts.AsNoTracking().Where(a => a.CandidateId == candidateId).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListCountedForExamAsync(Guid examId, CancellationToken cancellationToken) =>
        // Read-only and untracked, for the item analysis: each attempt is marked from its answers and paper, which are loaded, and never saved.
        await context.Attempts.AsNoTracking().AsSplitQuery()
            .Include(a => a.Answers).Include(a => a.Paper)
            .Where(a => a.ExamId == examId && a.Status == AttemptStatus.Submitted && a.InvalidatedAtUtc == null)
            .OrderBy(a => a.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListCountedForCandidateAsync(Guid candidateId, CancellationToken cancellationToken) =>
        // Read-only and untracked: the results are marked from these rows and never saved. The answers, paper and revisions are what
        // the marks and the score are built from, so all three are loaded; the split query keeps the three collections from joining.
        await context.Attempts.AsNoTracking().AsSplitQuery()
            .Include(a => a.Answers).Include(a => a.Paper).Include(a => a.Revisions)
            .Where(a => a.CandidateId == candidateId && a.Status == AttemptStatus.Submitted && a.InvalidatedAtUtc == null)
            .OrderBy(a => a.SubmittedAtUtc)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<decimal>> ListBestScoresOfOtherCandidatesAsync(Guid examId, Guid excludedCandidateId, CancellationToken cancellationToken)
    {
        // Grouped in the database so only one number per candidate leaves it, not every attempt of a large exam.
        var best = await context.Attempts.AsNoTracking()
            .Where(a => a.ExamId == examId && a.CandidateId != excludedCandidateId && a.Status == AttemptStatus.Submitted && a.InvalidatedAtUtc == null)
            .GroupBy(a => a.CandidateId)
            .Select(g => g.Max(a => a.Score))
            .ToListAsync(cancellationToken);

        // A submitted attempt always has a score; the null check only satisfies the nullable column's type.
        return best.Where(score => score is not null).Select(score => score!.Value).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SubmittedScore>> ListSubmittedScoresAsync(Guid examId, CancellationToken cancellationToken) =>
        // A projection, so a large exam moves one row per attempt and no answers.
        await context.Attempts.AsNoTracking()
            .Where(a => a.ExamId == examId && a.Status == AttemptStatus.Submitted && a.InvalidatedAtUtc == null && a.Score != null)
            .Select(a => new SubmittedScore(a.Id, a.CandidateId, a.Score!.Value))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListWithAnswersAsync(IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken) =>
        // The answers and the drawn paper are what marking a subject needs; the split keeps the two collections from multiplying each other.
        await context.Attempts.AsNoTracking().AsSplitQuery()
            .Include(a => a.Answers).Include(a => a.Paper)
            .Where(a => attemptIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListSubmittedByQuestionIdAsync(Guid questionId, CancellationToken cancellationToken)
    {
        // Attempts that answered the question, union attempts that only drew it onto their paper (unanswered but
        // still "included"): both indexed on QuestionId already, for the same reason the question bank's own
        // "has anyone answered this" check is cheap.
        var attemptIds = await context.Set<AttemptAnswer>().AsNoTracking()
            .Where(a => a.QuestionId == questionId)
            .Select(a => a.AttemptId)
            .Union(context.Set<AttemptQuestion>().AsNoTracking().Where(q => q.QuestionId == questionId).Select(q => q.AttemptId))
            .ToListAsync(cancellationToken);

        if (attemptIds.Count == 0)
            return [];

        return await context.Attempts.AsSplitQuery()
            .Include(a => a.Answers).Include(a => a.Marks).Include(a => a.Paper).Include(a => a.Revisions)
            .Where(a => attemptIds.Contains(a.Id) && a.Status == AttemptStatus.Submitted)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListFinishedForExamAsync(Guid examId, CancellationToken cancellationToken) =>
        // Only the attempt's own columns: the scope needs who sat it and when, and nothing else.
        await context.Attempts.AsNoTracking()
            .Where(a => a.ExamId == examId && a.Status == AttemptStatus.Submitted)
            .OrderBy(a => a.Number)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Attempt>> ListFinishedWithAnswersAsync(Guid examId, IReadOnlyCollection<Guid> attemptIds, CancellationToken cancellationToken)
    {
        // Nothing to read is not a query: an empty list of ids would otherwise still cost a round trip for no rows.
        if (attemptIds.Count == 0)
        {
            return [];
        }

        // The risk review reads the answers, the focus departures and the client sightings. The paper, marks, revisions and warnings are
        // not needed for it, so they are not loaded: a large exam is read in one split query per collection, not one wide join.
        return await context.Attempts.AsNoTracking().AsSplitQuery()
            .Include(a => a.Answers).Include(a => a.FocusViolations).Include(a => a.ClientSightings)
            .Where(a => a.ExamId == examId && a.Status == AttemptStatus.Submitted && attemptIds.Contains(a.Id))
            .OrderBy(a => a.Number)
            .ToListAsync(cancellationToken);
    }
}
