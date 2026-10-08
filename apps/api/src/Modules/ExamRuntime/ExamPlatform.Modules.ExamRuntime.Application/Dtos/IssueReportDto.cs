using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>A problem the candidate has just reported, as the exam page confirms it. It leaves out everything staff add.</summary>
/// <param name="Id">The report's id.</param>
/// <param name="Category">What kind of problem they said it was.</param>
/// <param name="QuestionId">The question on screen when they reported it, if it was about one.</param>
/// <param name="Message">What they wrote, as it was recorded (surrounding whitespace removed).</param>
/// <param name="ReportedAtUtc">When they reported it.</param>
public sealed record MyIssueReportDto(Guid Id, IssueCategory Category, Guid? QuestionId, string Message, DateTime ReportedAtUtc);

/// <summary>A reported problem as staff see it in the queue.</summary>
/// <param name="Id">The report's id.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name, or null if it can no longer be read.</param>
/// <param name="AttemptId">The attempt the candidate was sitting.</param>
/// <param name="AttemptNumber">Which attempt that is for the candidate at the exam, from 1; null if the attempt can no longer be read.</param>
/// <param name="CandidateId">The candidate's account id.</param>
/// <param name="CandidateEmail">The address the candidate was invited at, or null if they are no longer enrolled.</param>
/// <param name="QuestionId">The question on screen when they reported, or null.</param>
/// <param name="QuestionText">That question as it is now, as sanitized HTML (render it with an HTML sanitizer in place); null when none, or it can no longer be read.</param>
/// <param name="Category">What kind of problem they said it was.</param>
/// <param name="Message">What they wrote.</param>
/// <param name="ReportedAtUtc">When they reported it.</param>
/// <param name="Status">Whether it is waiting or resolved.</param>
/// <param name="ResolvedAtUtc">When it was resolved, if it has been.</param>
/// <param name="ResolutionNote">What staff said they did, if they said anything.</param>
public sealed record IssueReportDto(
    Guid Id,
    Guid ExamId,
    string? ExamName,
    Guid AttemptId,
    int? AttemptNumber,
    Guid CandidateId,
    string? CandidateEmail,
    Guid? QuestionId,
    string? QuestionText,
    IssueCategory Category,
    string Message,
    DateTime ReportedAtUtc,
    IssueReportStatus Status,
    DateTime? ResolvedAtUtc,
    string? ResolutionNote);
