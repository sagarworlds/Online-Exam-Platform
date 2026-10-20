using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Staff deactivate a student's account.</summary>
/// <param name="StudentId">The student's account.</param>
/// <param name="ActorUserId">The staff member, for the audit trail.</param>
/// <param name="ActorRole">Their role name, for the audit trail.</param>
public sealed record DeactivateStudentCommand(Guid StudentId, Guid? ActorUserId, string? ActorRole);

/// <summary>
/// Handles <see cref="DeactivateStudentCommand"/>. Deactivating is the platform's way of deleting a student: the account can no longer sign
/// in and everything it had open ends (sessions, unused sign-in codes, password-reset links), but nothing is removed. Their attempts, scores,
/// certificates, consent records and the audit trail still refer to the account, and erasing the personal data is a separate, deliberate
/// step. Repeating it is harmless: an account that is already deactivated is returned as it is, with no second audit entry.
/// </summary>
public sealed class DeactivateStudentHandler(
    IUserRepository userRepository,
    StudentSignInRevoker signIns,
    IAuditLogger auditLogger,
    IIdentityUnitOfWork unitOfWork,
    Clock clock,
    ILogger<DeactivateStudentHandler> logger)
{
    /// <summary>Deactivates the account, ends what it had open, and records who did it.</summary>
    /// <param name="command">The student and the staff member.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile as it is now.</returns>
    /// <exception cref="UserNotFoundError">There is no such account, or it is not a student's: a staff account cannot be deactivated here.</exception>
    public async Task<StudentDto> HandleAsync(DeactivateStudentCommand command, CancellationToken cancellationToken)
    {
        // The same answer for a staff account as for no account, so this route says nothing about who is staff.
        var student = await userRepository.GetByIdAsync(command.StudentId, cancellationToken);
        if (student is null || !student.IsStudent)
            throw new UserNotFoundError();

        var nowUtc = clock.UtcNow;
        if (!student.Deactivate(nowUtc))
            return StudentMapping.ToDto(student, nowUtc);

        // The sessions already ended with the account. What is left is any code or link that could still sign the student in.
        await signIns.RevokeAsync(student, nowUtc, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: command.ActorUserId,
                ActorRole: command.ActorRole,
                Action: "Identity.StudentDeactivated",
                EntityType: "User",
                EntityId: student.Id.ToString(),
                Metadata: new Dictionary<string, string>(),
                CorrelationId: null),
            cancellationToken);

        // Only the id: a name, address or number in a log is personal data (NFR-6).
        logger.LogInformation("Staff deactivated student {StudentId}.", student.Id);

        return StudentMapping.ToDto(student, nowUtc);
    }
}
