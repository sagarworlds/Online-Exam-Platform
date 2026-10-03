using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>A candidate's request for another attempt as staff see it in the queue.</summary>
/// <param name="Id">The request's id.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name, or null if it can no longer be read.</param>
/// <param name="CandidateId">The candidate's account id.</param>
/// <param name="CandidateEmail">The address the candidate was invited at, or null if they are no longer enrolled.</param>
/// <param name="Message">Why they asked, if they said.</param>
/// <param name="RequestedAtUtc">When they asked.</param>
/// <param name="Status">Whether it is waiting, approved or declined.</param>
/// <param name="DecidedAtUtc">When it was decided, if it has been.</param>
/// <param name="DecisionNote">What the administrator said when declining, if anything.</param>
/// <param name="CandidateNotified">
/// Whether the candidate was e-mailed the answer: set only on the response to approving or declining, and null in a listing.
/// False means nothing was sent (no mail server, the server refused, or the candidate is no longer enrolled), so the administrator
/// should let them know by other means.
/// </param>
public sealed record AttemptRequestDto(
    Guid Id,
    Guid ExamId,
    string? ExamName,
    Guid CandidateId,
    string? CandidateEmail,
    string? Message,
    DateTime RequestedAtUtc,
    AttemptRequestStatus Status,
    DateTime? DecidedAtUtc,
    string? DecisionNote,
    bool? CandidateNotified = null);

/// <summary>A candidate's own request for another attempt, as their exams page shows it. It leaves out who decided it.</summary>
/// <param name="Id">The request's id.</param>
/// <param name="Status">Whether it is waiting, approved or declined.</param>
/// <param name="Message">What they wrote when asking.</param>
/// <param name="RequestedAtUtc">When they asked.</param>
/// <param name="DecidedAtUtc">When it was decided, if it has been.</param>
/// <param name="DecisionNote">What the administrator said when declining, if anything.</param>
public sealed record MyAttemptRequestDto(
    Guid Id,
    AttemptRequestStatus Status,
    string? Message,
    DateTime RequestedAtUtc,
    DateTime? DecidedAtUtc,
    string? DecisionNote);
