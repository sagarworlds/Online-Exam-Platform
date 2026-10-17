using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.Modules.Invite.Contracts;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Staff correct a student's profile. Every field is sent: what is left the same simply comes back the same.</summary>
/// <param name="StudentId">The student's account.</param>
/// <param name="DisplayName">The name to show; it cannot be blank.</param>
/// <param name="Email">The e-mail address to sign in with, or blank for none.</param>
/// <param name="PhoneNumber">The mobile number to sign in with, or blank for none.</param>
/// <param name="DateOfBirth">The date of birth.</param>
/// <param name="ActorUserId">The staff member, for the audit trail.</param>
/// <param name="ActorRole">Their role name, for the audit trail.</param>
public sealed record UpdateStudentCommand(
    Guid StudentId,
    string? DisplayName,
    string? Email,
    string? PhoneNumber,
    DateOnly? DateOfBirth,
    Guid? ActorUserId,
    string? ActorRole);

/// <summary>
/// Handles <see cref="UpdateStudentCommand"/>. A change of e-mail address or mobile number takes effect at once and ends the student's
/// sessions and every code or link already sent to the old one (see <see cref="StudentSignInRevoker"/>), so they sign in again with a code
/// sent to the new one; a new e-mail address also moves their invitations, since only the account holding the invited address can accept
/// one. Nothing is sent to the old address and no confirmation is asked of the student.
/// </summary>
public sealed class UpdateStudentHandler(
    IUserRepository userRepository,
    IInviteAddressChanger inviteAddresses,
    StudentSignInRevoker signIns,
    IAuditLogger auditLogger,
    IIdentityUnitOfWork unitOfWork,
    Clock clock,
    ILogger<UpdateStudentHandler> logger)
{
    // Digits with an optional leading +, and spaces, dashes and brackets between them. The sign-in compares the number exactly as stored,
    // so this only keeps out text that cannot be a number; it does not reformat what staff type. A plain Regex and not [GeneratedRegex],
    // whose generated type sits outside the module's namespace, which the architecture rules refuse.
    private static readonly Regex PhonePattern = new(
        @"^\+?[0-9][0-9 ()\-]{5,18}[0-9]$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Checks the changes, applies them, and records what changed in the audit trail.</summary>
    /// <param name="command">The student and their corrected profile.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The profile as it is now and what the change did.</returns>
    /// <exception cref="UserNotFoundError">There is no such account, or it is not a student's: a staff account cannot be changed here.</exception>
    /// <exception cref="InvalidDisplayNameError">The name is blank or too long.</exception>
    /// <exception cref="InvalidEmailAddressError">The e-mail address is not a valid address.</exception>
    /// <exception cref="InvalidPhoneNumberError">The mobile number does not look like a phone number.</exception>
    /// <exception cref="ContactRequiredError">Both the e-mail address and the mobile number are blank.</exception>
    /// <exception cref="InvalidContactError">The e-mail address or mobile number is longer than the platform stores.</exception>
    /// <exception cref="InvalidDateOfBirthError">The date of birth is missing, in the future, or implausibly long ago.</exception>
    /// <exception cref="ContactInUseError">Another account already holds the new e-mail address or mobile number.</exception>
    public async Task<UpdateStudentResultDto> HandleAsync(UpdateStudentCommand command, CancellationToken cancellationToken)
    {
        // The same answer for a staff account as for no account, so this route says nothing about who is staff.
        var student = await userRepository.GetByIdAsync(command.StudentId, cancellationToken);
        if (student is null || !student.IsStudent)
            throw new UserNotFoundError();

        var nowUtc = clock.UtcNow;
        var before = new Snapshot(student.DisplayName, student.Email, student.PhoneNumber, student.DateOfBirth, student.GetAgeBand(nowUtc));

        var email = Trimmed(command.Email);
        if (email is not null && !IsDeliverable(email))
            throw new InvalidEmailAddressError();

        var phone = Trimmed(command.PhoneNumber);
        if (phone is not null && !PhonePattern.IsMatch(phone))
            throw new InvalidPhoneNumberError();

        // Applied to the loaded account in memory first. Nothing is saved, and nothing else is touched, until every rule has passed.
        student.UpdateDisplayName(command.DisplayName ?? string.Empty);
        student.ChangeContact(email, phone);
        student.ChangeDateOfBirth(command.DateOfBirth ?? throw InvalidDateOfBirthError.Missing(), nowUtc);

        var changed = ChangedFields(before, student);
        if (changed.Count == 0)
            return new UpdateStudentResultDto(StudentMapping.ToDto(student, nowUtc), changed, InvitationsMoved: 0, SignedOut: false);

        var emailChanged = changed.Contains("email");
        var phoneChanged = changed.Contains("phoneNumber");
        await EnsureContactIsFreeAsync(student, emailChanged, phoneChanged, cancellationToken);

        // The invitations go first. Moving them is safe to repeat, and checking the new address is free has already passed, so the one
        // thing that can still fail is the save below. If it does, the invitations are at the new address and the account is not, and
        // sending the same change again finishes the job; the other order would leave a change nobody could repeat.
        var invitationsMoved = 0;
        if (emailChanged && before.Email is not null && student.Email is not null)
            invitationsMoved = await inviteAddresses.ChangeAddressAsync(before.Email, student.Email, cancellationToken);

        // Nothing started under the old address or number may outlive it: whoever held it might not be the student any more.
        var signedOut = emailChanged || phoneChanged;
        if (signedOut)
            await signIns.RevokeAsync(student, nowUtc, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await RecordAsync(command, student, before, changed, invitationsMoved, signedOut, nowUtc, cancellationToken);

        // Which fields and how many, never their values: a name, address or number in a log is personal data (NFR-6).
        logger.LogInformation(
            "Staff changed {Fields} on student {StudentId}: {InvitationsMoved} invitations moved, signed out {SignedOut}.",
            string.Join(',', changed), student.Id, invitationsMoved, signedOut);

        return new UpdateStudentResultDto(StudentMapping.ToDto(student, nowUtc), changed, invitationsMoved, signedOut);
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // EmailAddressAttribute accepts spaces inside an address ("a b@x.in"), which no mail server delivers to; a sign-in code sent there is lost.
    private static bool IsDeliverable(string email) => !email.Any(char.IsWhiteSpace) && new EmailAddressAttribute().IsValid(email);

    private static List<string> ChangedFields(Snapshot before, User student)
    {
        var changed = new List<string>();
        if (!string.Equals(before.DisplayName, student.DisplayName, StringComparison.Ordinal))
            changed.Add("name");
        if (!string.Equals(before.Email, student.Email, StringComparison.Ordinal))
            changed.Add("email");
        if (!string.Equals(before.PhoneNumber, student.PhoneNumber, StringComparison.Ordinal))
            changed.Add("phoneNumber");
        if (before.DateOfBirth != student.DateOfBirth)
            changed.Add("dateOfBirth");
        return changed;
    }

    // Sign-in finds an account by its address or number exactly as stored, and two accounts must never share one. The address is compared
    // without regard to case here, which is stricter than the unique index, so staff cannot create a look-alike of an existing account.
    private async Task EnsureContactIsFreeAsync(User student, bool emailChanged, bool phoneChanged, CancellationToken cancellationToken)
    {
        if (emailChanged && student.Email is not null && await userRepository.AnyOtherWithEmailAsync(student.Email, student.Id, cancellationToken))
            throw ContactInUseError.Email();

        if (phoneChanged && student.PhoneNumber is not null && await userRepository.AnyOtherWithPhoneAsync(student.PhoneNumber, student.Id, cancellationToken))
            throw ContactInUseError.Phone();
    }

    // The entry names what changed and, when the date of birth moved the student across the adult line, which side they were on and are
    // on now: that is the effect that matters to guardian consent, and it is not personal data. No value is recorded (NFR-6).
    private Task RecordAsync(
        UpdateStudentCommand command,
        User student,
        Snapshot before,
        IReadOnlyList<string> changed,
        int invitationsMoved,
        bool signedOut,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>
        {
            ["fields"] = string.Join(',', changed),
            ["invitationsMoved"] = invitationsMoved.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["signedOut"] = signedOut ? "true" : "false",
        };

        if (changed.Contains("dateOfBirth"))
        {
            metadata["ageBandBefore"] = before.AgeBand.ToString();
            metadata["ageBandAfter"] = student.GetAgeBand(nowUtc).ToString();
        }

        return auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: command.ActorUserId,
                ActorRole: command.ActorRole,
                Action: "Identity.StudentProfileChanged",
                EntityType: "User",
                EntityId: student.Id.ToString(),
                Metadata: metadata,
                CorrelationId: null),
            cancellationToken);
    }

    private readonly record struct Snapshot(string DisplayName, string? Email, string? PhoneNumber, DateOnly DateOfBirth, AgeBand AgeBand);
}
