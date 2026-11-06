using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Events;

/// <summary>
/// A code was made whose link signs the invited person in. It names the code by id and never carries it: the code is a sign-in
/// credential, and this travels to the audit trail.
/// </summary>
public sealed record InviteDirectLinkIssuedEvent(Guid InviteId, Guid ExamId, Guid CodeId) : DomainEvent;
