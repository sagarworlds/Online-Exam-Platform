namespace ExamPlatform.Modules.Invite.Contracts;

/// <summary>
/// Who may take which exam (FR-14, ADR 0001). A candidate is enrolled in an exam once they have accepted an
/// invitation to it. Exam delivery asks this before listing an exam or starting an attempt; it is consumed
/// through this Contracts project only.
/// </summary>
public interface IEnrollments
{
    /// <summary>The ids of the exams the user has accepted an invitation to.</summary>
    /// <param name="userId">The candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Guid>> GetEnrolledExamIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Whether the user has accepted an invitation to the exam.</summary>
    /// <param name="userId">The candidate.</param>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> IsEnrolledAsync(Guid userId, Guid examId, CancellationToken cancellationToken);
}
