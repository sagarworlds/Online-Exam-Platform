namespace ExamPlatform.Modules.ExamRuntime.Infrastructure;

/// <summary>
/// The unique indexes of the ExamRuntime schema that exist to settle a race: two requests do the same thing at the same moment, both pass
/// the check in code, and the store lets only one insert survive. When one of these fires, the loser is told to reload and carry on from
/// what is stored (a 409), which is an expected outcome and not a server fault.
/// </summary>
/// <remarks>
/// <para>
/// Matched by the name Postgres reports for the violated constraint, because a unique violation on any <i>other</i> constraint means
/// something is wrong with the code or the data, and answering "try again" to that would make a client retry a failure that can never
/// succeed, and hide the bug. Those surface unchanged: every primary key, and <c>IX_AttemptQuestions_AttemptId_QuestionId</c>, which only
/// fires if one paper is built with the same question twice (two racing starts meet at the attempt-number index first).
/// </para>
/// <para>
/// The names are the ones the migrations create. An integration test reads every unique index from a migrated database and fails when one
/// is neither listed here nor in the test's own list of indexes that are not race guards, so a new unique index has to be classified, and
/// a renamed one cannot quietly stop being a 409.
/// </para>
/// </remarks>
public static class ExamRuntimeRaceGuards
{
    /// <summary>The unique indexes whose violation is a lost race, in the order of the schema's tables.</summary>
    public static IReadOnlyList<string> IndexNames { get; } =
    [
        // A candidate's attempt numbers: two parallel "start" calls both try for the next number.
        "IX_Attempts_ExamId_CandidateId_Number",
        // One answer, mark, answer sequence and question-time total per question per attempt: two first writes race, and the page sends again.
        "IX_AttemptAnswers_AttemptId_QuestionId",
        "IX_AttemptMarks_AttemptId_QuestionId",
        "IX_AttemptAnswerSequences_AttemptId_QuestionId",
        "IX_AttemptQuestionTimes_AttemptId_QuestionId",
        // One waiting request for another attempt per candidate per exam: two taps of "ask".
        "IX_AttemptRequests_ExamId_CandidateId",
        // One dispute per question per attempt: two taps of "dispute".
        "IX_Disputes_AttemptId_QuestionId",
        // One record per scheduled message: two overlapping passes of the notification run.
        "IX_NotificationDeliveries_Kind_SubjectId_RecipientId",
        // One accommodation per candidate per exam: two administrators setting it at once.
        "IX_Accommodations_ExamId_CandidateId",
        // Extra attempts are numbered one after the last: two administrators granting at the same moment.
        "IX_ExtraAttemptGrants_ExamId_CandidateId_Number",
    ];

    private static readonly HashSet<string> Names = new(IndexNames, StringComparer.Ordinal);

    /// <summary>Whether a unique violation on the named constraint is a lost race.</summary>
    /// <param name="constraintName">The constraint Postgres reported as violated, or null when it did not say.</param>
    /// <returns>True for one of <see cref="IndexNames"/>; false for any other constraint, and for no name at all.</returns>
    public static bool IsRaceGuard(string? constraintName) => constraintName is not null && Names.Contains(constraintName);
}
