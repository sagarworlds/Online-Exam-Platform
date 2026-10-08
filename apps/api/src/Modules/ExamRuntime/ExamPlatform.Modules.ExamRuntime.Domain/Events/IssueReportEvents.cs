using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Events;

/// <summary>A candidate reported a problem from inside an exam (FR-42). What they wrote is not carried: it stays on the report and out of the audit trail.</summary>
/// <param name="IssueReportId">The report.</param>
/// <param name="AttemptId">The attempt they were sitting.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate who reported.</param>
/// <param name="Category">What kind of problem they said it was.</param>
public sealed record IssueReportedEvent(Guid IssueReportId, Guid AttemptId, Guid ExamId, Guid CandidateId, IssueCategory Category) : DomainEvent;

/// <summary>Staff marked a reported problem as dealt with (FR-42).</summary>
/// <param name="IssueReportId">The report.</param>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate who reported.</param>
public sealed record IssueResolvedEvent(Guid IssueReportId, Guid AttemptId, Guid ExamId, Guid CandidateId) : DomainEvent;
