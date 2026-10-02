namespace ExamPlatform.Modules.Invite.Contracts;

/// <summary>
/// Who is enrolled in an exam, with the address each was invited at (ADR 0001): the exam delivery module shows staff the candidates of an
/// exam. Kept apart from <see cref="IEnrollments"/>, which a candidate's own requests use, so those never depend on a
/// listing only staff need. Consumed through this Contracts project only.
/// </summary>
public interface IExamRoster
{
    /// <summary>The candidates who have accepted an invitation to the exam, each once.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<EnrolledCandidate>> GetEnrolledCandidatesAsync(Guid examId, CancellationToken cancellationToken);
}

/// <summary>A candidate enrolled in an exam.</summary>
/// <param name="UserId">The candidate's account id.</param>
/// <param name="Email">The address they were invited at, which the account holds (only its holder can accept).</param>
public sealed record EnrolledCandidate(Guid UserId, string Email);
