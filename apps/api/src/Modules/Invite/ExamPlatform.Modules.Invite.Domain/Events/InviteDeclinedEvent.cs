using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Events;

public sealed record InviteDeclinedEvent(Guid InviteId, Guid ExamId, string Email) : DomainEvent;
