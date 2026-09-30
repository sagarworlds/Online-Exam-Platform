using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Events;

public sealed record InviteCreatedEvent(Guid InviteId, Guid ExamId, string Email, Guid CreatedByUserId) : DomainEvent;
