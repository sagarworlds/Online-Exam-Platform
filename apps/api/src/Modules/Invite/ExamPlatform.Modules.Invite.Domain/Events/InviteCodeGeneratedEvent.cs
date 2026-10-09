using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Events;

/// <summary>
/// Staff asked an invite for another code to hand to the invited person themselves. It names the code by id and never carries it: the
/// code is a credential, and this travels to the audit trail.
/// </summary>
public sealed record InviteCodeGeneratedEvent(Guid InviteId, Guid ExamId, Guid CodeId) : DomainEvent;
