using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Events;

public sealed record InviteRevokedEvent(Guid InviteId, Guid ExamId, string Email) : DomainEvent;
