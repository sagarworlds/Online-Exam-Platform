using ExamPlatform.Modules.Batch.Application.Ports;
using ExamPlatform.Modules.Batch.Contracts;
using ExamPlatform.Modules.Batch.Domain;

namespace ExamPlatform.Modules.Batch.Application;

/// <summary>The <see cref="IExamBatchMembers"/> other modules ask who belongs to an exam's batches (FR-35).</summary>
public sealed class ExamBatchMembers(IBatchRepository batches) : IExamBatchMembers
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<BatchMemberRef>> ListMembersOfExamAsync(Guid examId, CancellationToken cancellationToken)
    {
        var found = await batches.ListByExamAsync(examId, cancellationToken);

        // A deleted batch, a removed member, or one who withdrew has no place on the exam's boards: they are no longer taking part.
        return found
            .Where(batch => !batch.IsDeleted)
            .SelectMany(batch => batch.Members
                .Where(member => !member.IsDeleted && member.CandidateId is not null && member.Status != MemberRegistrationStatus.Withdrawn)
                .Select(member => new BatchMemberRef(member.CandidateId!.Value, batch.Id, batch.Name)))
            .ToList();
    }
}
