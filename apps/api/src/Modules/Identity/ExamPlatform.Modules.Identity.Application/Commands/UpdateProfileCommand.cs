namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>Updates the editable parts of a user's profile.</summary>
/// <param name="UserId">The user to update.</param>
/// <param name="DisplayName">The new display name.</param>
public sealed record UpdateProfileCommand(Guid UserId, string DisplayName);
