using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.DataRequests;

/// <summary>Reads data requests for the candidate who made them and for the staff who answer them (FR-48).</summary>
public sealed class DataRequestQueries(IDataRequestRepository requests, Clock clock)
{
    /// <summary>Every request the account has made.</summary>
    /// <param name="userId">The account, from the caller's token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<DataRequestDto>> ListMineAsync(Guid userId, CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var mine = await requests.ListForUserAsync(userId, cancellationToken);
        return mine.Select(request => DataRequestDto.From(request, nowUtc)).ToList();
    }

    /// <summary>Every request still waiting for an answer, oldest due first, so the overdue ones come first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IReadOnlyList<DataRequestDto>> ListOpenAsync(CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;
        var open = await requests.ListOpenAsync(cancellationToken);
        return open.Select(request => DataRequestDto.From(request, nowUtc)).ToList();
    }
}
