using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Events;

/// <summary>A candidate disputed the answer key of a question in one of their attempts (FR-31). Their reason is not carried: it stays on the dispute and out of the audit trail.</summary>
/// <param name="DisputeId">The dispute.</param>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate who disputed.</param>
/// <param name="QuestionId">The question disputed.</param>
public sealed record DisputeRaisedEvent(Guid DisputeId, Guid AttemptId, Guid ExamId, Guid CandidateId, Guid QuestionId) : DomainEvent;

/// <summary>Staff settled a dispute, by correcting the answer key or by leaving it as it is (FR-31).</summary>
/// <param name="DisputeId">The dispute.</param>
/// <param name="AttemptId">The attempt.</param>
/// <param name="ExamId">The exam.</param>
/// <param name="CandidateId">The candidate who disputed.</param>
/// <param name="QuestionId">The question disputed.</param>
/// <param name="Accepted">Whether the answer key was corrected (otherwise it was left as it is).</param>
public sealed record DisputeResolvedEvent(Guid DisputeId, Guid AttemptId, Guid ExamId, Guid CandidateId, Guid QuestionId, bool Accepted) : DomainEvent;
