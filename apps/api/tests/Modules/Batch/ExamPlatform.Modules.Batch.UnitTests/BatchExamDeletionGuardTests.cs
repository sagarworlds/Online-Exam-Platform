using ExamPlatform.Modules.Batch.Application;
using ExamPlatform.Modules.Batch.Application.Ports;
using NSubstitute;
using BatchAggregate = ExamPlatform.Modules.Batch.Domain.Batch;

namespace ExamPlatform.Modules.Batch.UnitTests;

public class BatchExamDeletionGuardTests
{
    private readonly IBatchRepository repository = Substitute.For<IBatchRepository>();
    private readonly Guid examId = Guid.NewGuid();

    [Fact]
    public async Task WhenABatchIsAssignedToTheExam_ItObjects()
    {
        repository.ListByExamAsync(examId, Arg.Any<CancellationToken>())
            .Returns([new BatchAggregate(examId, "Batch A", null, 30, Guid.NewGuid())]);

        var objections = await new BatchExamDeletionGuard(repository).FindObjectionsAsync(examId, CancellationToken.None);

        Assert.Contains("batch", Assert.Single(objections), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhenNoBatchIsAssigned_ItHasNoObjection()
    {
        repository.ListByExamAsync(examId, Arg.Any<CancellationToken>()).Returns([]);

        Assert.Empty(await new BatchExamDeletionGuard(repository).FindObjectionsAsync(examId, CancellationToken.None));
    }
}
