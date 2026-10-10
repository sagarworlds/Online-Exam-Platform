using ExamPlatform.Modules.Batch.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>The three leaderboards an exam has (FR-35).</summary>
public enum LeaderboardBoard
{
    /// <summary>Every candidate, by their score.</summary>
    Overall,

    /// <summary>The candidates of one batch the signed-in candidate is in, by their score.</summary>
    Batch,

    /// <summary>Every candidate, by their marks in one subject of the exam.</summary>
    Subject,
}

/// <summary>
/// Builds a leaderboard of an exam from its released results (FR-35). Every board uses the same results and the same ranking, so a batch or a
/// subject board cannot disagree with the overall board about a candidate's attempt: each candidate counts once, by their best submitted attempt.
/// </summary>
public sealed class GetLeaderboardHandler(
    IExamCatalog catalog,
    IEnrollments enrollments,
    IAttemptRepository attempts,
    IExamBatchMembers batchMembers,
    IDisplayNameDirectory displayNames,
    SubjectMarks subjectMarks,
    Clock clock)
{
    /// <summary>Returns one of the exam's leaderboards, as the signed-in candidate may see it.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="candidateId">The signed-in candidate; they must be enrolled in the exam.</param>
    /// <param name="board">"overall", "batch" or "subject"; omitted means overall.</param>
    /// <param name="batchId">For the batch board, the batch to show; omitted means the first batch the candidate is in.</param>
    /// <param name="subject">For the subject board, the subject to show; omitted means the first subject of the exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The board.</returns>
    /// <exception cref="ExamNotFoundError">No exam has the id.</exception>
    /// <exception cref="CandidateNotEnrolledError">The candidate is not enrolled in the exam.</exception>
    /// <exception cref="ResultsNotReleasedError">The exam's author has not released the results, so no ranking is shown.</exception>
    /// <exception cref="InvalidLeaderboardBoardError">The board is not overall, batch or subject.</exception>
    /// <exception cref="LeaderboardBatchNotFoundError">The batch is not one the candidate is in on this exam.</exception>
    /// <exception cref="LeaderboardSubjectNotFoundError">No question of the exam is filed under the subject.</exception>
    public async Task<LeaderboardDto> HandleAsync(
        Guid examId, Guid candidateId, string? board, Guid? batchId, string? subject, CancellationToken cancellationToken)
    {
        var kind = ParseBoard(board);
        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamNotFoundError();
        if (!await enrollments.IsEnrolledAsync(candidateId, examId, cancellationToken))
            throw new CandidateNotEnrolledError();

        // Nobody's place is shown before the exam's author releases the results: the same rule the result page and the answer review use.
        var availability = ResultRelease.AvailabilityOf(exam, clock.UtcNow);
        if (!availability.Available)
            throw new ResultsNotReleasedError(availability.AvailableFromUtc);

        var memberships = await batchMembers.ListMembersOfExamAsync(examId, cancellationToken);
        var mine = memberships.Where(m => m.CandidateId == candidateId)
            .DistinctBy(m => m.BatchId)
            .OrderBy(m => m.BatchName, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new LeaderboardBatchDto(m.BatchId, m.BatchName))
            .ToList();

        // A candidate with a retake counts once, by the attempt that scored best, so a retake cannot take two places.
        var best = (await attempts.ListSubmittedScoresAsync(examId, cancellationToken))
            .GroupBy(s => s.CandidateId)
            .Select(g => g.OrderByDescending(s => s.Score).ThenBy(s => s.AttemptId).First())
            .ToList();

        var built = kind switch
        {
            LeaderboardBoard.Batch => BatchRows(batchId, mine, memberships, best),
            LeaderboardBoard.Subject => await SubjectRowsAsync(exam, subject, best, cancellationToken),
            _ => new BoardRows(best.Select(s => (s.CandidateId, s.Score)).ToList(), null, null, []),
        };

        var ranked = LeaderboardRanking.Rank(built.Rows);
        var top = ranked.Take(LeaderboardRanking.MaxEntries).ToList();
        var names = await displayNames.GetDisplayNamesAsync(top.Select(r => r.CandidateId).Append(candidateId).Distinct().ToList(), cancellationToken);

        // The candidate's own row is always reachable, even when they fall outside the listed places.
        var own = ranked.FirstOrDefault(r => r.CandidateId == candidateId);
        var yourEntry = own.CandidateId == candidateId && top.All(r => r.CandidateId != candidateId)
            ? EntryFor(own, names, candidateId)
            : null;

        return new LeaderboardDto(
            exam.Id,
            exam.Name,
            kind.ToString().ToLowerInvariant(),
            built.BatchId,
            built.Subject,
            top.Select(r => EntryFor(r, names, candidateId)).ToList(),
            yourEntry,
            ranked.Count,
            mine,
            built.Subjects,
            ranked.Count > top.Count);
    }

    /// <summary>The batch board: the candidates who are members of one batch, and the batch shown.</summary>
    private static BoardRows BatchRows(Guid? requested, IReadOnlyList<LeaderboardBatchDto> mine, IReadOnlyList<BatchMemberRef> memberships, IReadOnlyList<SubmittedScore> best)
    {
        // A batch the candidate is not in is refused as not found, so nobody can probe the rankings of batches they do not belong to.
        if (requested is { } wanted && mine.All(b => b.Id != wanted))
            throw new LeaderboardBatchNotFoundError();

        var batch = requested is { } chosen ? mine.First(b => b.Id == chosen) : mine.FirstOrDefault();
        if (batch is null)
            return new BoardRows([], null, null, []);

        var members = memberships.Where(m => m.BatchId == batch.Id).Select(m => m.CandidateId).ToHashSet();
        var rows = best.Where(s => members.Contains(s.CandidateId)).Select(s => (s.CandidateId, s.Score)).ToList();
        return new BoardRows(rows, batch.Id, null, []);
    }

    /// <summary>
    /// The subject board: each candidate's marks in the subject, from their best attempt. A candidate whose paper had no question in the subject is not
    /// ranked on it, since they were never marked in it.
    /// </summary>
    private async Task<BoardRows> SubjectRowsAsync(ExamSnapshot exam, string? requested, IReadOnlyList<SubmittedScore> best, CancellationToken cancellationToken)
    {
        var withAnswers = await attempts.ListWithAnswersAsync(best.Select(s => s.AttemptId).ToList(), cancellationToken);
        var table = await subjectMarks.BuildAsync(exam, withAnswers, cancellationToken);
        if (table.Subjects.Count == 0)
            return new BoardRows([], null, null, table.Subjects);

        var chosen = requested ?? table.Subjects[0];
        var name = table.Subjects.FirstOrDefault(s => string.Equals(s, chosen, StringComparison.OrdinalIgnoreCase))
            ?? throw new LeaderboardSubjectNotFoundError();

        var rows = best.Where(s => table.HasSubject(s.AttemptId, name)).Select(s => (s.CandidateId, table.ScoreOf(s.AttemptId, name))).ToList();
        return new BoardRows(rows, null, name, table.Subjects);
    }

    private static LeaderboardBoard ParseBoard(string? board) => (board ?? "overall").Trim().ToLowerInvariant() switch
    {
        "overall" => LeaderboardBoard.Overall,
        "batch" => LeaderboardBoard.Batch,
        "subject" => LeaderboardBoard.Subject,
        _ => throw new InvalidLeaderboardBoardError(),
    };

    private static LeaderboardEntryDto EntryFor(BoardRow row, IReadOnlyDictionary<Guid, string> names, Guid candidateId) =>
        new(row.Rank, DisplayNameMask.Mask(names.GetValueOrDefault(row.CandidateId)), row.Score, row.CandidateId == candidateId);

    /// <summary>The rows of one board, and what the board is about.</summary>
    /// <param name="Rows">Each candidate's score on the board, once each.</param>
    /// <param name="BatchId">The batch the board shows, if it is a batch board.</param>
    /// <param name="Subject">The subject the board shows, if it is a subject board.</param>
    /// <param name="Subjects">The subjects of the exam, for the subject selector.</param>
    private sealed record BoardRows(IReadOnlyList<(Guid CandidateId, decimal Score)> Rows, Guid? BatchId, string? Subject, IReadOnlyList<string> Subjects);
}
