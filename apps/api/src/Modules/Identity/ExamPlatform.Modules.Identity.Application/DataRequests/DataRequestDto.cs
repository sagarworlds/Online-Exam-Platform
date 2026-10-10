using ExamPlatform.Modules.Identity.Domain.DataRequests;

namespace ExamPlatform.Modules.Identity.Application.DataRequests;

/// <summary>One data request as the candidate and staff see it.</summary>
/// <param name="Id">The request.</param>
/// <param name="UserId">The account it is about.</param>
/// <param name="Kind">Access, correction or erasure.</param>
/// <param name="Status">Received, completed or rejected.</param>
/// <param name="Details">What the candidate said, or null.</param>
/// <param name="ReceivedAtUtc">When it was received.</param>
/// <param name="DueAtUtc">When staff must have answered it by.</param>
/// <param name="IsOverdue">Whether it is still open past its due time.</param>
/// <param name="ResolvedAtUtc">When staff answered it, or null.</param>
/// <param name="ResolutionNote">What was done or why it was refused, or null.</param>
public sealed record DataRequestDto(
    Guid Id,
    Guid UserId,
    DataRequestKind Kind,
    DataRequestStatus Status,
    string? Details,
    DateTime ReceivedAtUtc,
    DateTime DueAtUtc,
    bool IsOverdue,
    DateTime? ResolvedAtUtc,
    string? ResolutionNote)
{
    /// <summary>Maps a request, judging lateness against the given instant.</summary>
    /// <param name="request">The request.</param>
    /// <param name="nowUtc">The current instant, for the overdue check.</param>
    public static DataRequestDto From(DataRequest request, DateTime nowUtc) => new(
        request.Id,
        request.UserId,
        request.Kind,
        request.Status,
        request.Details,
        request.ReceivedAtUtc,
        request.DueAtUtc,
        request.IsOverdue(nowUtc),
        request.ResolvedAtUtc,
        request.ResolutionNote);
}
