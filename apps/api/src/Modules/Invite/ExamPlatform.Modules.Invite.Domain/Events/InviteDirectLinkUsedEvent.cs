using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Invite.Domain.Events;

/// <summary>
/// A direct sign-in link was opened: the invitation was accepted and the person signed in. It stands in for the ordinary accepted event,
/// and carries who signed in, because the caller is anonymous and any token in the request is not to be trusted as the actor. It names the
/// code by id and never carries it.
/// </summary>
/// <param name="InviteId">The invitation.</param>
/// <param name="ExamId">The exam it was for.</param>
/// <param name="CodeId">The code that was used.</param>
/// <param name="UserId">The account that was signed in.</param>
/// <param name="AccountCreated">Whether the account was made by this link, because none held the invited address.</param>
public sealed record InviteDirectLinkUsedEvent(Guid InviteId, Guid ExamId, Guid CodeId, Guid UserId, bool AccountCreated) : DomainEvent;
