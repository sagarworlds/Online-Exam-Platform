using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Application.Queries;
using ExamPlatform.Modules.Guardian.Domain;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;
using NSubstitute;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.UnitTests;

public class GuardianQueryHandlerTests
{
    [Fact]
    public async Task ListGuardianLinks_KeepsARevokedLink_AndLeavesOutAnUnlinkedOne()
    {
        var guardian = new GuardianAggregate("guardian@example.com", "Asha Rao");
        var revokedCandidate = Guid.NewGuid();
        var unlinkedCandidate = Guid.NewGuid();
        guardian.LinkCandidate(revokedCandidate, "revoked@example.com", "token-1");
        guardian.LinkCandidate(unlinkedCandidate, "unlinked@example.com", "token-2");
        guardian.RevokeCandidateLink(revokedCandidate);
        guardian.UnlinkCandidate(unlinkedCandidate);
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
