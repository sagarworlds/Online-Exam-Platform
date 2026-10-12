using ExamPlatform.Modules.Batch.Domain.Exceptions;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.UnitTests;

/// <summary>
/// The error code and HTTP status each batch error reports. Clients branch on the code and the API maps the status,
/// so changing either one changes what callers see.
/// </summary>
public class BatchErrorContractTests
{
    public static TheoryData<DomainException, string, int> Errors => new()
    {
        { new BatchNotFoundError(Guid.NewGuid()), "batch_not_found", 404 },
        { new BatchMemberNotFoundError(Guid.NewGuid()), "batch_member_not_found", 404 },
        { new DuplicateMemberError(), "duplicate_member", 409 },
        { new InvalidBatchConfigError("A batch needs a name."), "invalid_batch_config", 400 },
        { new InvalidBatchMemberError("A member needs a valid e-mail address."), "invalid_batch_member", 400 },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public void EachError_ReportsItsCodeAndStatus(DomainException error, string code, int status)
    {
        Assert.Equal(code, error.ErrorCode);
        Assert.Equal(status, error.HttpStatusCode);
    }
}
