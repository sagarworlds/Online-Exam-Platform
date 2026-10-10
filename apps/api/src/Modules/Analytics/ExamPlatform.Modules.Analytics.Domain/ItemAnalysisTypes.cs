namespace ExamPlatform.Modules.Analytics.Domain;

/// <summary>A candidate's best counted score at an exam, which places them in the cohort and in the upper or lower group.</summary>
/// <param name="CandidateId">The candidate.</param>
/// <param name="Score">The candidate's best counted score at the exam.</param>
public readonly record struct CandidateScore(Guid CandidateId, decimal Score);

/// <summary>One candidate's answer to one question that was on their paper.</summary>
/// <param name="CandidateId">The candidate.</param>
/// <param name="Correct">Whether the answer was fully correct. An unanswered question is not correct.</param>
public readonly record struct ItemAnswer(Guid CandidateId, bool Correct);

/// <summary>The upper and lower groups of an exam's cohort, which the discrimination index compares (FR-37).</summary>
/// <param name="Upper">The candidates with the highest scores.</param>
/// <param name="Lower">The candidates with the lowest scores; never overlaps <paramref name="Upper"/>.</param>
public sealed record CohortGroups(IReadOnlySet<Guid> Upper, IReadOnlySet<Guid> Lower);

/// <summary>The indices of one question (FR-37).</summary>
/// <param name="Attempts">How many counted candidates had the question on their paper. Shown whether or not the indices are.</param>
/// <param name="CorrectCount">How many of those answered it fully correctly.</param>
/// <param name="Difficulty">The share of those candidates who answered correctly, from 0 to 1; null while the cohort is below the threshold.</param>
/// <param name="Discrimination">
/// How much better the upper group answered the question than the lower group, from -1 to 1; null while the cohort is below the threshold, or when
/// a group has no candidate who had the question.
/// </param>
public sealed record ItemIndices(int Attempts, int CorrectCount, decimal? Difficulty, decimal? Discrimination);
