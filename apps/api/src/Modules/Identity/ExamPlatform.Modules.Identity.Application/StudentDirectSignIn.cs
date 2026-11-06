using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Domain.Exceptions;

namespace ExamPlatform.Modules.Identity.Application;

/// <summary>
/// Signs a student in because they opened a link that was e-mailed to them (<see cref="IDirectSignIn"/>). The link is the proof that they
/// can read the mailbox, as a one-time code would be, so no code is asked for. It is for students only: a staff account needs a password
/// and a second factor, and a link in a mailbox must never stand in for either.
/// </summary>
public sealed class StudentDirectSignIn(
    IUserRepository userRepository,
    IRoleRepository roleRepository,
    LoginEligibilityPolicy eligibilityPolicy,
    LoginSessionIssuer sessionIssuer,
    IAuditLogger auditLogger,
    IIdentityUnitOfWork unitOfWork,
    Clock clock) : IDirectSignIn
{
    /// <inheritdoc />
    public async Task<DirectSignInAccount> FindOrCreateStudentAsync(string email, CancellationToken cancellationToken)
    {
        var address = email.Trim();

        // Sign-in matches an address exactly, but an invitation was typed by staff, so it is matched without regard to case. When two
        // accounts differ only by case it is not clear whose the link is, and nobody is signed in.
        var holders = await userRepository.ListByEmailIgnoreCaseAsync(address, cancellationToken);
        if (holders.Count > 1)
        {
            throw new DirectSignInNotAllowedError();
        }

        if (holders.Count == 1)
        {
            EnsureMaySignIn(holders[0]);
            return new DirectSignInAccount(holders[0].Id, Created: false);
        }

        return new DirectSignInAccount(await CreateStudentAsync(address, cancellationToken), Created: true);
    }

    /// <inheritdoc />
    public async Task<DirectSignInSession> StartSessionAsync(
        Guid userId, string? deviceFingerprint, string? ipAddress, CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken) ?? throw new UserNotFoundError();

        // Checked again: some time passed since the account was found, and an account suspended in between must not get a session.
        EnsureMaySignIn(user);

        // The link proved the address, which is what verifying a pending account means.
        if (user.Status == UserStatus.PendingVerification)
        {
            user.Activate();
        }

        var result = sessionIssuer.Issue(user, deviceFingerprint, ipAddress);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new DirectSignInSession(
            result.AccessToken ?? throw new InvalidOperationException("A completed sign-in has an access token."),
            result.SessionId ?? throw new InvalidOperationException("A completed sign-in has a session."));
    }

    private async Task<Guid> CreateStudentAsync(string address, CancellationToken cancellationToken)
    {
        var candidateRole = await roleRepository.GetByNameAsync(RegisterCandidateHandler.CandidateRoleName, cancellationToken)
            ?? throw new RoleNotFoundError();

        // Active at once: nothing else would verify it, and the link already did. The date of birth is a placeholder adult, because the
        // person was never asked for one; staff correct it from the student's page where it matters.
        var student = User.Register(address, phoneNumber: null, User.PlaceholderAdultDateOfBirth, DisplayNameFor(address), clock.UtcNow);
        student.AssignRole(candidateRole);
        student.Activate();
        await userRepository.AddAsync(student, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // Named by id only, never by address (NFR-6). The new student is the one who opened the link, so they are the actor.
        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: student.Id,
                ActorRole: RbacCatalog.RoleNames.Candidate,
                Action: "Identity.StudentAccountCreatedFromInviteLink",
                EntityType: "User",
                EntityId: student.Id.ToString(),
                Metadata: new Dictionary<string, string>(),
                CorrelationId: null),
            cancellationToken);

        return student.Id;
    }

    private void EnsureMaySignIn(User user)
    {
        if (eligibilityPolicy.GetAccountViolation(user) is { } locked)
        {
            throw locked;
        }

        // Not merely "does not need a second factor": a teacher does not, and holds staff powers all the same.
        if (!user.IsStudent)
        {
            throw new DirectSignInNotAllowedError();
        }
    }

    // The part of the address before the "@", so the student has a name to be greeted by until they set their own.
    private static string DisplayNameFor(string address)
    {
        var at = address.IndexOf('@', StringComparison.Ordinal);
        var name = (at > 0 ? address[..at] : address).Trim();
        if (name.Length == 0)
        {
            return "Student";
        }

        return name.Length > User.MaxDisplayNameLength ? name[..User.MaxDisplayNameLength] : name;
    }
}
