using ExamPlatform.Modules.Identity.Domain.DataRequests;

namespace ExamPlatform.Modules.Identity.Application.Ports;

/// <summary>Stores and finds data requests (FR-48).</summary>
public interface IDataRequestRepository
{
    /// <summary>Finds a request by id, or null when there is none.</summary>
    /// <param name="id">The request id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<DataRequest?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Whether the account already has an open request of this kind.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="kind">The kind of request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasOpenAsync(Guid userId, DataRequestKind kind, CancellationToken cancellationToken);

    /// <summary>Every request the account has made, newest first.</summary>
    /// <param name="userId">The account.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DataRequest>> ListForUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Every request still waiting for an answer, oldest due first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<DataRequest>> ListOpenAsync(CancellationToken cancellationToken);

    /// <summary>Stores a new request. Nothing is saved until the unit of work is.</summary>
    /// <param name="request">The request to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AddAsync(DataRequest request, CancellationToken cancellationToken);
}
