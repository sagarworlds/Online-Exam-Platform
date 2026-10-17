namespace ExamPlatform.Modules.Identity.Application.Dtos;

/// <summary>A student as the staff list shows them. The date of birth is left out: the list is browsed more often than it is needed.</summary>
/// <param name="Id">The account's id.</param>
/// <param name="DisplayName">The name shown in the app.</param>
/// <param name="Email">The e-mail address the student signs in with, if they have one.</param>
/// <param name="PhoneNumber">The mobile number the student signs in with, if they have one.</param>
/// <param name="Status">Where the account is in its life: PendingVerification, Active, Suspended or Deactivated.</param>
public sealed record StudentSummaryDto(Guid Id, string DisplayName, string? Email, string? PhoneNumber, string Status);

/// <summary>One student's profile, with the date of birth staff may correct.</summary>
/// <param name="Id">The account's id.</param>
/// <param name="DisplayName">The name shown in the app.</param>
/// <param name="Email">The e-mail address the student signs in with, if they have one.</param>
/// <param name="PhoneNumber">The mobile number the student signs in with, if they have one.</param>
/// <param name="DateOfBirth">The date of birth, which decides the age band.</param>
/// <param name="AgeBand">Minor or Adult as of today, which decides whether guardian consent applies.</param>
/// <param name="Status">Where the account is in its life: PendingVerification, Active, Suspended or Deactivated.</param>
public sealed record StudentDto(
    Guid Id, string DisplayName, string? Email, string? PhoneNumber, DateOnly DateOfBirth, string AgeBand, string Status);

/// <summary>What changing a student's profile did.</summary>
/// <param name="Student">The profile as it is now.</param>
/// <param name="Changed">Which fields changed: any of name, email, phoneNumber and dateOfBirth. Empty when nothing was different.</param>
/// <param name="InvitationsMoved">How many of the student's invitations moved to the new e-mail address.</param>
/// <param name="SignedOut">
/// Whether the student was signed out: their sessions, and any sign-in code or reset link not yet used, were ended. It happens when their
/// e-mail address or mobile number changed.
/// </param>
public sealed record UpdateStudentResultDto(StudentDto Student, IReadOnlyList<string> Changed, int InvitationsMoved, bool SignedOut);
