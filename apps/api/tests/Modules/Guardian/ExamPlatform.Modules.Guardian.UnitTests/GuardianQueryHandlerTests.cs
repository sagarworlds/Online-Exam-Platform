using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Application.Queries;
using ExamPlatform.Modules.Guardian.Domain;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;
using NSubstitute;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.UnitTests;

public class GuardianQueryHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListGuardianLinks_KeepsARevokedLink_AndLeavesOutAnUnlinkedOne()
    {
        var guardian = new GuardianAggregate("guardian@example.com", "Asha Rao", null, Now);
        var revokedCandidate = Guid.NewGuid();
        var unlinkedCandidate = Guid.NewGuid();
        var expiry = Now.Add(GuardianLinkToken.Lifetime);
        guardian.LinkCandidate(revokedCandidate, "revoked@example.com", GuardianLinkToken.Hash("code-1"), expiry, Now);
        guardian.LinkCandidate(unlinkedCandidate, "unlinked@example.com", GuardianLinkToken.Hash("code-2"), expiry, Now);
        guardian.RevokeCandidateLink(revokedCandidate, Now);
        guardian.UnlinkCandidate(unlinkedCandidate, Now);
        var repository = Substitute.For<IGuardianRepository>();
        repository.GetByIdAsync(guardian.Id, Arg.Any<CancellationToken>()).Returns(guardian);

        var links = await new ListGuardianLinksHandler(repository).HandleAsync(guardian.Id, CancellationToken.None);

        var link = Assert.Single(links);
        Assert.Equal(revokedCandidate, link.CandidateId);
        Assert.Equal(GuardianLinkStatus.Revoked, link.Status);
    }

    [Fact]
    public async Task ListGuardianLinks_ForAnUnknownGuardian_ThrowsNotFound()
    {
        var repository = Substitute.For<IGuardianRepository>();
        repository.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((GuardianAggregate?)null);

        await Assert.ThrowsAsync<GuardianNotFoundError>(() =>
            new ListGuardianLinksHandler(repository).HandleAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
