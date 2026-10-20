using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Staff turn a deactivated student's account back on.</summary>
/// <param name="StudentId">The student's account.</param>
/// <param name="ActorUserId">The staff member, for the audit trail.</param>
/// <param name="ActorRole">Their role name, for the audit trail.</param>
public sealed record ReactivateStudentCommand(Guid StudentId, Guid? ActorUserId, string? ActorRole);

/// <summary>
/// Handles <see cref="ReactivateStudentCommand"/>: the reverse of <see cref="DeactivateStudentHandler"/>. The account becomes active again and
/// the student can sign in with a code sent to their e-mail address or mobile number. Nothing is sent to them and no old session comes back;
/// the sessions ended by the deactivation stay ended. Only a deactivated account changes. Any other account is returned as it is, with no
/// audit entry, so repeating the request is harmless.
/// </summary>
public sealed class ReactivateStudentHandler(
    IUserRepository userRepository,
    IAuditLogger auditLogger,
    IIdentityUnitOfWork unitOfWork,
    Clock clock,
    ILogger<ReactivateStudentHandler> logger)
{
    /// <summary>Reactivates the account and records who did it.</summary>
    /// <param name="command">The student and the staff member.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile as it is now.</returns>
    /// <exception cref="UserNotFoundError">There is no such account, or it is not a student's: a staff account cannot be changed here.</exception>
    public async Task<StudentDto> HandleAsync(ReactivateStudentCommand command, CancellationToken cancellationToken)
    {
        // The same answer for a staff account as for no account, so this route says nothing about who is staff.
        var student = await userRepository.GetByIdAsync(command.StudentId, cancellationToken);
        if (student is null || !student.IsStudent)
            throw new UserNotFoundError();

        var nowUtc = clock.UtcNow;
        if (!student.Reactivate())
            return StudentMapping.ToDto(student, nowUtc);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: command.ActorUserId,
                ActorRole: command.ActorRole,
                Action: "Identity.StudentReactivated",
                EntityType: "User",
                EntityId: student.Id.ToString(),
                Metadata: new Dictionary<string, string>(),
                CorrelationId: null),
            cancellationToken);

        // Only the id: a name, address or number in a log is personal data (NFR-6).
        logger.LogInformation("Staff reactivated student {StudentId}.", student.Id);

        return StudentMapping.ToDto(student, nowUtc);
    }
}
