using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.LoadTest;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.Application.Commands;

/// <summary>Enrols the load test's candidate pool in an exam.</summary>
/// <param name="ExamId">The load-test exam.</param>
/// <param name="Pattern">The pool's addresses and how many, as the accounts were created (<c>--create-load-test-candidates</c>).</param>
public sealed record EnrolLoadTestPoolCommand(Guid ExamId, LoadTestAccountPattern Pattern);

/// <summary>What <see cref="EnrolLoadTestCandidatesHandler"/> did.</summary>
/// <param name="Enrolled">The accounts that were enrolled now.</param>
/// <param name="AlreadyEnrolled">The accounts that were already enrolled in the exam and were left as they were.</param>
/// <param name="WithoutAccount">The addresses of the pool that have no active account, so nobody could be enrolled.</param>
public sealed record EnrolLoadTestPoolResult(int Enrolled, int AlreadyEnrolled, int WithoutAccount);

/// <summary>
/// Handles <see cref="EnrolLoadTestPoolCommand"/> (ADR 0006). Starting an attempt checks that the candidate has accepted an invite
/// to the exam (<see cref="Contracts.IEnrollments"/>), so each pool account gets an invite that is created and accepted through the
/// invite's own methods. The real code path checks the real rules (the accepting account must hold the invited address, a code is
/// used once), and what a candidate's enrolment looks like to the rest of the platform is exactly what it would be after a real accept.
/// </summary>
/// <remarks>
/// Run <c>--create-load-test-candidates</c> first: an address without an account is counted and skipped. Running it again enrols only
/// whoever is not enrolled yet. The exam has to be published for attempts to start; this command does not publish it.
/// </remarks>
public sealed class EnrolLoadTestCandidatesHandler(
    IInviteRepository repository,
    IInviteUnitOfWork unitOfWork,
    IExamCatalog examCatalog,
    IContactDirectory contacts,
    IAuditLogger auditLogger,
    Clock clock)
{
    /// <summary>How many invites are stored together. It keeps one save small.</summary>
    public const int BatchSize = 500;

    /// <summary>The code of a pool invite lives this long. It is used in the same instant, so the value only has to be valid.</summary>
    private const int CodeExpiryHours = Domain.Invite.MinCodeExpiryHours;

    /// <summary>Enrols every pool account that is not enrolled yet, in batches, and records the run in the audit trail.</summary>
    /// <param name="command">The exam and the pool.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>How many were enrolled, were already, and had no account.</returns>
    /// <exception cref="InviteExamNotFoundError">The exam does not exist.</exception>
    public async Task<EnrolLoadTestPoolResult> HandleAsync(EnrolLoadTestPoolCommand command, CancellationToken cancellationToken)
    {
        _ = await examCatalog.FindAsync(command.ExamId, cancellationToken)
            ?? throw new InviteExamNotFoundError(command.ExamId);

        var nowUtc = clock.UtcNow;
        var enrolled = 0;
        var already = 0;
        var withoutAccount = 0;
        foreach (var batch in command.Pattern.Addresses().Chunk(BatchSize))
        {
            foreach (var email in batch)
            {
                var userId = await contacts.FindActiveAccountIdByEmailAsync(email, cancellationToken);
                if (userId is null)
                {
                    withoutAccount++;
                    continue;
                }

                if (await repository.HasAcceptedAsync(userId.Value, command.ExamId, cancellationToken))
                {
                    already++;
                    continue;
                }

                // No staff member invited these accounts: the all-zero id says so, as it does for the audit entries nobody made.
                var invite = new InviteAggregate(command.ExamId, batchMemberId: null, email, createdByUserId: Guid.Empty, nowUtc);
                var code = invite.GenerateCode(CodeExpiryHours, nowUtc);
                invite.Accept(code.Code, userId.Value, email, nowUtc);

                // Creating and accepting an invite each raise an event that the audit trail writes down, and every audit entry is a
                // write of its own. For a pool of thousands that is tens of thousands of writes, so the run is recorded once, below.
                invite.ClearDomainEvents();

                repository.Add(invite);
                enrolled++;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        if (enrolled > 0)
        {
            await auditLogger.RecordAsync(
                new AuditEntry(
                    ActorUserId: null,
                    ActorRole: null,
                    Action: "Invite.LoadTestCandidatesEnrolled",
                    EntityType: "Exam",
                    EntityId: command.ExamId.ToString(),
                    Metadata: new Dictionary<string, string>
                    {
                        ["enrolled"] = enrolled.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["alreadyEnrolled"] = already.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    },
                    CorrelationId: null),
                cancellationToken);
        }

        return new EnrolLoadTestPoolResult(enrolled, already, withoutAccount);
    }
}
