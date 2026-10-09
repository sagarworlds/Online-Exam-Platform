namespace ExamPlatform.Modules.ExamAuthoring.Contracts;

/// <summary>
/// Something outside the exam builder that can object to a draft exam being deleted. The builder owns the exam but not who
/// has been told about it: an invitation or a batch holds the exam's id, and deleting the exam would leave it pointing at
/// nothing. Each module that keeps an exam's id contributes one of these (ADR 0001), so the builder can ask without knowing
/// who they are. A guard that fails fails the deletion, so an exam is never deleted because a module could not be asked.
/// </summary>
public interface IExamDeletionGuard
{
    /// <summary>Says why the exam must not be deleted, if this module holds something that refers to it.</summary>
    /// <param name="examId">The exam about to be deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Reasons in words for the author to read, such as "Invitations have been sent for it."; empty when there is no objection.</returns>
    Task<IReadOnlyList<string>> FindObjectionsAsync(Guid examId, CancellationToken cancellationToken);
}
