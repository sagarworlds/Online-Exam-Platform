using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain.DataRequests;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Identity.Application.DataRequests;

/// <summary>Records staff's answer to a data request (FR-48).</summary>
public sealed class ResolveDataRequestHandler(
    IDataRequestRepository requests,
    IIdentityUnitOfWork unitOfWork,
    Clock clock)
{
    /// <summary>Answers the request, keeps who answered and when, and saves it.</summary>
    /// <param name="requestId">The request.</param>
    /// <param name="resolvedByUserId">The staff user answering it, from the caller's token.</param>
    /// <param name="outcome">Completed or Rejected.</param>
    /// <param name="note">What was done or why it was refused.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="DataRequestNotFoundError">No request has that id.</exception>
    /// <exception cref="DataRequestNotOpenError">It was already answered.</exception>
    /// <exception cref="InvalidDataRequestError">The outcome or the note is not acceptable.</exception>
    public async Task<DataRequestDto> HandleAsync(
        Guid requestId, Guid resolvedByUserId, DataRequestStatus outcome, string? note, CancellationToken cancellationToken)
    {
        var request = await requests.GetByIdAsync(requestId, cancellationToken) ?? throw new DataRequestNotFoundError();

        var nowUtc = clock.UtcNow;
        request.Resolve(resolvedByUserId, outcome, note, nowUtc);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DataRequestDto.From(request, nowUtc);
    }
}
