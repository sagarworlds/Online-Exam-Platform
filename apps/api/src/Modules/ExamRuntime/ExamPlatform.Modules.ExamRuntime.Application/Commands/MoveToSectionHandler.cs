using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.ExamRuntime.Application.Commands;

/// <summary>Moves a candidate to a later section of an exam that locks sections, leaving the one they were in for good (FR-18, FR-12).</summary>
public sealed class MoveToSectionHandler(AttemptAccess access, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Checks the section belongs to the exam and records that the candidate is now in it.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="sectionId">The section to move to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is already submitted, or its time has just run out and it was closed.</exception>
    /// <exception cref="SectionNotInAttemptError">The section is not part of the exam.</exception>
    /// <exception cref="SectionLockedError">The section is before the one the candidate is in.</exception>
    /// <exception cref="ConcurrencyConflictError">The attempt was changed at the same moment.</exception>
    public async Task HandleAsync(Guid attemptId, Guid candidateId, Guid sectionId, CancellationToken cancellationToken)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        var section = exam.Sections.FirstOrDefault(s => s.Id == sectionId)
            ?? throw new SectionNotInAttemptError();

        // Exams without the lock let candidates roam, so there is nothing to record: the call is accepted and ignored.
        if (!exam.SectionLockEnabled)
            return;

        attempt.MoveToSection(section.Order, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
