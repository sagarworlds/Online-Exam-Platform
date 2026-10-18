namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>How to name and reach the holder of one account, as the staff who answer a data request need it (FR-48).</summary>
/// <param name="DisplayName">The name the account shows.</param>
/// <param name="Email">The e-mail address, or null when the account has none.</param>
/// <param name="PhoneNumber">The phone number, or null when the account has none.</param>
public sealed record UserContact(string DisplayName, string? Email, string? PhoneNumber);
