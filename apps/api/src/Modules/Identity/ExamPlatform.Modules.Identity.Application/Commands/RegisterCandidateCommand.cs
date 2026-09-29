using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Registers a new candidate account (FR-1) and sends a confirmation OTP.</summary>
/// <param name="Email">Email address, if provided.</param>
/// <param name="PhoneNumber">Phone number, if provided.</param>
/// <param name="DateOfBirth">Date of birth (required for age-band/guardian-consent gating elsewhere).</param>
/// <param name="DisplayName">Name to show in the UI.</param>
/// <param name="OtpChannel">Which of email or phone to send the confirmation code to.</param>
public sealed record RegisterCandidateCommand(
    string? Email,
    string? PhoneNumber,
    DateOnly DateOfBirth,
    string DisplayName,
    OtpChannel OtpChannel);
