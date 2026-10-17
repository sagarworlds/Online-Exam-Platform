using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Events;

/// <summary>An invite's address was changed. <paramref name="Email"/> is the new address.</summary>
public sealed record InviteEmailChangedEvent(Guid InviteId, Guid ExamId, string Email) : DomainEvent;
