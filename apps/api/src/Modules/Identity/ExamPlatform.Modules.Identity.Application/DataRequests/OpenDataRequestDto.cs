using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain.DataRequests;

namespace ExamPlatform.Modules.Identity.Application.DataRequests;

/// <summary>
/// One request still waiting for an answer, as staff see it: the request, and who made it, so the answer can reach them. A copy of the
/// data goes to their address, and a correction is checked with them. The candidate's own list does not carry these: they are theirs already.
/// </summary>
/// <param name="Id">The request.</param>
/// <param name="UserId">The account it is about.</param>
/// <param name="Kind">Access, correction or erasure.</param>
/// <param name="Status">Always received: only open requests are listed.</param>
/// <param name="Details">What the candidate said, or null.</param>
/// <param name="ReceivedAtUtc">When it was received.</param>
/// <param name="DueAtUtc">When staff must have answered it by.</param>
/// <param name="IsOverdue">Whether it is still open past its due time.</param>
/// <param name="ResolvedAtUtc">Always null: an open request has no answer yet.</param>
/// <param name="ResolutionNote">Always null: an open request has no answer yet.</param>
/// <param name="RequesterName">The name on the account, or null when the account cannot be found.</param>
/// <param name="RequesterEmail">The account's e-mail address, or null when it has none or cannot be found.</param>
/// <param name="RequesterPhoneNumber">The account's phone number, or null when it has none or cannot be found.</param>
public sealed record OpenDataRequestDto(
    Guid Id,
    Guid UserId,
    DataRequestKind Kind,
    DataRequestStatus Status,
    string? Details,
    DateTime ReceivedAtUtc,
    DateTime DueAtUtc,
    bool IsOverdue,
    DateTime? ResolvedAtUtc,
    string? ResolutionNote,
    string? RequesterName,
    string? RequesterEmail,
    string? RequesterPhoneNumber)
{
    /// <summary>Maps a request with the person who made it, judging lateness against the given instant.</summary>
    /// <param name="request">The request.</param>
    /// <param name="requester">Who made it, or null when the account cannot be found.</param>
    /// <param name="nowUtc">The current instant, for the overdue check.</param>
    public static OpenDataRequestDto From(DataRequest request, UserContact? requester, DateTime nowUtc)
    {
        var dto = DataRequestDto.From(request, nowUtc);
        return new OpenDataRequestDto(
            dto.Id,
            dto.UserId,
            dto.Kind,
            dto.Status,
            dto.Details,
            dto.ReceivedAtUtc,
            dto.DueAtUtc,
            dto.IsOverdue,
            dto.ResolvedAtUtc,
            dto.ResolutionNote,
            requester?.DisplayName,
            requester?.Email,
            requester?.PhoneNumber);
    }
}
