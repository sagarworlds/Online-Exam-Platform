using ExamPlatform.Modules.Invite.Application.Dtos;
using ExamPlatform.Modules.Invite.Application.Ports;

namespace ExamPlatform.Modules.Invite.Application.Queries;

/// <summary>Lists the newest invites for the staff screens.</summary>
public sealed class ListInvitesHandler(IInviteRepository repository)
{
    /// <summary>How many invites one listing returns at most; paging arrives with the full invite screens.</summary>
    public const int PageSize = 200;

    /// <summary>Returns the newest invites (without codes).</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<InviteDto>> HandleAsync(CancellationToken cancellationToken) =>
        (await repository.ListNewestAsync(PageSize, cancellationToken)).Select(i => i.ToDto(examName: null)).ToList();
}
