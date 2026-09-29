namespace ExamPlatform.Modules.Identity.Application.Dtos;

/// <summary>Read-only projection of a user's profile.</summary>
/// <param name="UserId">The user's identifier.</param>
/// <param name="Email">Email address, if set.</param>
/// <param name="PhoneNumber">Phone number, if set.</param>
/// <param name="DisplayName">Name shown in the UI.</param>
/// <param name="Status">Current lifecycle status.</param>
/// <param name="Roles">Names of the roles assigned to this user.</param>
public sealed record UserProfileDto(
    Guid UserId,
    string? Email,
    string? PhoneNumber,
    string DisplayName,
    string Status,
    IReadOnlyCollection<string> Roles);
