using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Invite.Application.Dtos;
using ExamPlatform.Modules.Invite.Application.Ports;

namespace ExamPlatform.Modules.Invite.Application.Queries;

/// <summary>Lists the newest invites for the staff screens.</summary>
public sealed class ListInvitesHandler(IInviteRepository repository, IExamCatalog examCatalog)
{
    /// <summary>How many invites one listing returns at most; paging arrives with the full invite screens.</summary>
    public const int PageSize = 200;

    /// <summary>Returns the newest invites (without codes), each with the name of its exam.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<InviteDto>> HandleAsync(CancellationToken cancellationToken)
    {
        var invites = await repository.ListNewestAsync(PageSize, cancellationToken);

        // The screen groups invites by student and shows which exam each is for, so the name is looked up here. A listing has a handful of
        // exams but up to PageSize invites, so each exam is asked for once; one at a time because the lookups share a database connection.
        var names = new Dictionary<Guid, string?>();
        foreach (var examId in invites.Select(i => i.ExamId).Distinct())
        {
            names[examId] = (await examCatalog.FindAsync(examId, cancellationToken))?.Name;
        }

        // An exam that cannot be found keeps its invites in the list with no name rather than hiding them from staff.
        return invites.Select(i => i.ToDto(names[i.ExamId])).ToList();
    }
}
