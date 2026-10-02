using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.Application.Ports;

/// <summary>Persistence port for the <see cref="InviteAggregate"/> aggregate.</summary>
public interface IInviteRepository
{
    /// <summary>Starts tracking a new invite; it is stored when the unit of work saves.</summary>
    /// <param name="invite">The invite to add.</param>
    void Add(InviteAggregate invite);

    /// <summary>Loads an invite with its codes, tracked so changes to it are saved.</summary>
    /// <param name="inviteId">The invite's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<InviteAggregate?> GetByIdAsync(Guid inviteId, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="GetByIdAsync"/>, but a missing invite is an error.</summary>
    /// <param name="inviteId">The invite's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Domain.Exceptions.InviteNotFoundError">No invite has that id.</exception>
    Task<InviteAggregate> GetByIdOrThrowAsync(Guid inviteId, CancellationToken cancellationToken = default);

    /// <summary>Loads the invite that owns a code, with all its codes, tracked.</summary>
    /// <param name="code">The code, already upper-cased.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The invite, or null when no invite has that code.</returns>
    Task<InviteAggregate?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>Lists the most recently created invites, newest first.</summary>
    /// <param name="take">How many to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<InviteAggregate>> ListNewestAsync(int take, CancellationToken cancellationToken = default);

    /// <summary>The distinct exam ids of the invites a user has accepted.</summary>
    /// <param name="userId">The accepting user.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Guid>> ListAcceptedExamIdsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The invites to an exam that have been accepted, without tracking, for read-only display.</summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<InviteAggregate>> ListAcceptedForExamAsync(Guid examId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether any invite to the exam can still be used or has been: one waiting to be accepted or already accepted. Revoked,
    /// declined and expired invites are history and do not count.
    /// </summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> AnyLiveForExamAsync(Guid examId, CancellationToken cancellationToken = default);

    /// <summary>Whether the user has accepted an invite to the exam.</summary>
    /// <param name="userId">The accepting user.</param>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasAcceptedAsync(Guid userId, Guid examId, CancellationToken cancellationToken = default);
}
