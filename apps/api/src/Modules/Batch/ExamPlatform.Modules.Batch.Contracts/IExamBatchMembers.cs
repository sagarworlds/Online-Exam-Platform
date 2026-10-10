namespace ExamPlatform.Modules.Batch.Contracts;

/// <summary>
/// Which candidates belong to which batch of an exam (ADR 0001), so the exam's leaderboards can show a batch's own ranking without reaching
/// into the batch module's tables. Consumed through this Contracts project only.
/// </summary>
public interface IExamBatchMembers
{
    /// <summary>
    /// Lists every candidate who is an active member of a batch of the exam. A candidate in two batches appears once per batch.
    /// </summary>
    /// <param name="examId">The exam.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The memberships, in no particular order; empty when the exam has no batch with a registered member.</returns>
    Task<IReadOnlyList<BatchMemberRef>> ListMembersOfExamAsync(Guid examId, CancellationToken cancellationToken);
}

/// <summary>A candidate who is a member of a batch.</summary>
/// <param name="CandidateId">The candidate's account id.</param>
/// <param name="BatchId">The batch.</param>
/// <param name="BatchName">The batch's name, as staff gave it.</param>
public sealed record BatchMemberRef(Guid CandidateId, Guid BatchId, string BatchName);
