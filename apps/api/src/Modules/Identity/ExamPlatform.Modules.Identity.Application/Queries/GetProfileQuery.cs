namespace ExamPlatform.Modules.Identity.Application.Queries;

/// <summary>Reads a user's profile.</summary>
/// <param name="UserId">The user to look up.</param>
public sealed record GetProfileQuery(Guid UserId);
