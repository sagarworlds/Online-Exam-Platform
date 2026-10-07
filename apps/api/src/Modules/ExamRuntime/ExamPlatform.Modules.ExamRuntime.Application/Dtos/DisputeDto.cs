using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>Whether a candidate can still dispute the answer key of a released result, and until when (FR-31).</summary>
/// <param name="Enabled">Whether disputes are being taken at all; false when the platform has switched them off.</param>
/// <param name="Open">Whether a dispute can be raised now.</param>
/// <param name="ClosesAtUtc">When the time to dispute ends (or ended); null when disputes are switched off.</param>
public sealed record DisputeWindowDto(bool Enabled, bool Open, DateTime? ClosesAtUtc);

/// <summary>A candidate's own dispute, as their result shows it. It leaves out which staff user settled it.</summary>
/// <param name="Id">The dispute's id.</param>
/// <param name="QuestionId">The question disputed.</param>
/// <param name="Reason">What they wrote when raising it.</param>
/// <param name="RaisedAtUtc">When they raised it.</param>
/// <param name="Status">Whether it is waiting, accepted (the key was corrected) or rejected (the key stands).</param>
/// <param name="ResolvedAtUtc">When it was settled, if it has been.</param>
/// <param name="ResolutionNote">Why the key stands (rejected), or the reason given for the correction (accepted).</param>
public sealed record MyDisputeDto(
    Guid Id,
    Guid QuestionId,
    string Reason,
    DateTime RaisedAtUtc,
    DisputeStatus Status,
    DateTime? ResolvedAtUtc,
    string? ResolutionNote);

/// <summary>A candidate's dispute as staff see it in the queue.</summary>
/// <param name="Id">The dispute's id.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name, or null if it can no longer be read.</param>
/// <param name="AttemptId">The attempt disputed.</param>
/// <param name="AttemptNumber">Which attempt that is for the candidate at the exam, from 1; null if the attempt can no longer be read.</param>
/// <param name="CandidateId">The candidate's account id.</param>
/// <param name="CandidateEmail">The address the candidate was invited at, or null if they are no longer enrolled.</param>
/// <param name="QuestionId">The question whose answer key is disputed; the id the key correction takes.</param>
/// <param name="QuestionText">The question as it is now, as sanitized HTML (render it with an HTML sanitizer in place); null if it can no longer be read.</param>
/// <param name="Reason">Why the candidate thinks the key is wrong.</param>
/// <param name="RaisedAtUtc">When they raised it.</param>
/// <param name="Status">Whether it is waiting, accepted or rejected.</param>
/// <param name="ResolvedAtUtc">When it was settled, if it has been.</param>
/// <param name="ResolutionNote">What the candidate was told when it was settled.</param>
public sealed record DisputeDto(
    Guid Id,
    Guid ExamId,
    string? ExamName,
    Guid AttemptId,
    int? AttemptNumber,
    Guid CandidateId,
    string? CandidateEmail,
    Guid QuestionId,
    string? QuestionText,
    string Reason,
    DateTime RaisedAtUtc,
    DisputeStatus Status,
    DateTime? ResolvedAtUtc,
    string? ResolutionNote);
