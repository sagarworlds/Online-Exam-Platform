using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain.DataRequests;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Identity.Application.DataRequests;

/// <summary>Receives a candidate's data request (FR-48) and starts its clock.</summary>
/// <remarks>
/// One open request per kind and account: a second would be answered twice, so it is refused until the first is answered.
/// </remarks>
public sealed class RaiseDataRequestHandler(
    IDataRequestRepository requests,
    IIdentityUnitOfWork unitOfWork,
    Clock clock,
    IOptions<DataRequestOptions> options)
{
    /// <summary>Records the request and saves it.</summary>
    /// <param name="userId">The account making the request, from the caller's token.</param>
    /// <param name="kind">What is asked for.</param>
    /// <param name="details">What the candidate said, or null.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="DataRequestAlreadyOpenError">The account has an open request of this kind.</exception>
    /// <exception cref="InvalidDataRequestError">The details are too long.</exception>
    public async Task<DataRequestDto> HandleAsync(Guid userId, DataRequestKind kind, string? details, CancellationToken cancellationToken)
    {
        if (await requests.HasOpenAsync(userId, kind, cancellationToken))
            throw new DataRequestAlreadyOpenError();

        var nowUtc = clock.UtcNow;
        var request = DataRequest.Raise(userId, kind, details, nowUtc, TimeSpan.FromDays(options.Value.ServiceDays));
        await requests.AddAsync(request, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return DataRequestDto.From(request, nowUtc);
    }
}
