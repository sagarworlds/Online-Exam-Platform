using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Events;

/// <summary>Raised when a new <see cref="User"/> is created.</summary>
/// <param name="UserId">The new user's identifier.</param>
/// <param name="OccurredAtUtc">When the registration happened.</param>
public sealed record UserRegisteredEvent(Guid UserId, DateTime OccurredAtUtc) : IDomainEvent;
