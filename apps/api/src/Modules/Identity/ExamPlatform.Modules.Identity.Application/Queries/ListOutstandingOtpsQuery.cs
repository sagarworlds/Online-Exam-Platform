namespace ExamPlatform.Modules.Identity.Application.Queries;

/// <summary>Asks for the candidate codes that are still usable.</summary>
/// <param name="DestinationContains">Only destinations containing this text, or null for all.</param>
/// <param name="ActorUserId">The admin asking, for the audit trail.</param>
/// <param name="ActorRole">The asking admin's role name, for the audit trail.</param>
public sealed record ListOutstandingOtpsQuery(string? DestinationContains, Guid ActorUserId, string ActorRole);
