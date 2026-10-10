namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>One row of a leaderboard.</summary>
/// <param name="Rank">The place on the board: 1 for the best. Candidates with the same score share a place.</param>
/// <param name="Name">
/// The candidate's name as other candidates may see it: the first name and the initial of the last, never the whole name (FR-35). Null when
/// the account has no name, which the page shows as an anonymous candidate.
/// </param>
/// <param name="Score">The score on this board: the marks overall, or the marks in the subject.</param>
/// <param name="IsYou">Whether this row is the signed-in candidate's own.</param>
public sealed record LeaderboardEntryDto(int Rank, string? Name, decimal Score, bool IsYou);

/// <summary>A batch the candidate belongs to on the exam, for the batch board's selector.</summary>
/// <param name="Id">The batch's id.</param>
/// <param name="Name">The batch's name.</param>
public sealed record LeaderboardBatchDto(Guid Id, string Name);

/// <summary>
/// One leaderboard of an exam (FR-35), built from the exam's released results only. Each candidate counts once, by their best submitted attempt.
/// </summary>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name.</param>
/// <param name="Board">Which board this is: "overall", "batch" or "subject".</param>
/// <param name="BatchId">The batch the batch board shows, or null when the board is not a batch board or the candidate is in no batch.</param>
/// <param name="Subject">The subject the subject board shows, or null when the board is not a subject board or the exam has no subject.</param>
/// <param name="Entries">The places from first, up to <see cref="Application.LeaderboardRanking.MaxEntries"/>.</param>
/// <param name="You">The signed-in candidate's own row when it falls outside <paramref name="Entries"/>; null otherwise, or when they have no result on this board.</param>
/// <param name="CandidateCount">How many candidates are on this board in all.</param>
/// <param name="Batches">The batches the candidate belongs to on this exam, for the selector.</param>
/// <param name="Subjects">The subjects the exam's questions are filed under, for the subject board's selector; empty for other boards.</param>
/// <param name="Truncated">Whether the board has more candidates than <paramref name="Entries"/> lists.</param>
public sealed record LeaderboardDto(
    Guid ExamId,
    string ExamName,
    string Board,
    Guid? BatchId,
    string? Subject,
    IReadOnlyList<LeaderboardEntryDto> Entries,
    LeaderboardEntryDto? You,
    int CandidateCount,
    IReadOnlyList<LeaderboardBatchDto> Batches,
    IReadOnlyList<string> Subjects,
    bool Truncated);
