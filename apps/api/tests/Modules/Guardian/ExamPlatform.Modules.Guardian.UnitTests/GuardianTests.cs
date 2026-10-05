using ExamPlatform.Modules.Guardian.Application;
using ExamPlatform.Modules.Guardian.Application.Commands;
using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Domain;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;
using NSubstitute;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.UnitTests;

public class GuardianTests
{
    private static GuardianAggregate NewGuardian() => new("guardian@example.com", "Gia Guardian");

    [Fact]
    public void LinkCandidate_WhenAlreadyLinked_ThrowsGuardianAlreadyLinkedError()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", "token-1");

        Assert.Throws<GuardianAlreadyLinkedError>(() => guardian.LinkCandidate(candidateId, "c@example.com", "token-2"));
    }

    [Fact]
    public void RevokeCandidateLink_MarksTheLinkRevoked()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", "token-1");

        guardian.RevokeCandidateLink(candidateId);

        Assert.Equal(GuardianLinkStatus.Revoked, guardian.GetCandidateLink(candidateId)!.Status);
    }

    [Fact]
    public void RevokeCandidateLink_WithNoLink_ThrowsGuardianLinkNotFoundError()
    {
        Assert.Throws<GuardianLinkNotFoundError>(() => NewGuardian().RevokeCandidateLink(Guid.NewGuid()));
    }

    [Fact]
    public void RevokeCandidateLink_Twice_ThrowsGuardianLinkAlreadyRevokedError()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", "token-1");
        guardian.RevokeCandidateLink(candidateId);

        Assert.Throws<GuardianLinkAlreadyRevokedError>(() => guardian.RevokeCandidateLink(candidateId));
    }

    [Fact]
    public void UnlinkCandidate_WithNoLink_ThrowsGuardianLinkNotFoundError()
    {
        Assert.Throws<GuardianLinkNotFoundError>(() => NewGuardian().UnlinkCandidate(Guid.NewGuid()));
    }

    [Fact]
    public void UnlinkCandidate_RemovesTheLink_SoTheCandidateCanBeLinkedAgain()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", "token-1");

        guardian.UnlinkCandidate(candidateId);

        Assert.Null(guardian.GetCandidateLink(candidateId));
        guardian.LinkCandidate(candidateId, "c@example.com", "token-2");
    }

    [Fact]
    public void Verify_WhenNotPending_ThrowsGuardianLinkNotPendingError()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        var link = guardian.LinkCandidate(candidateId, "c@example.com", "token-1");
        link.Verify();

        Assert.Throws<GuardianLinkNotPendingError>(() => link.Verify());
    }
}

public class GuardianLinkHandlerTests
{
    private readonly IGuardianRepository repository = Substitute.For<IGuardianRepository>();
    private readonly IGuardianUnitOfWork unitOfWork = Substitute.For<IGuardianUnitOfWork>();

    [Fact]
    public async Task Link_UnknownGuardian_ThrowsGuardianNotFoundError()
    {
        await Assert.ThrowsAsync<GuardianNotFoundError>(() =>
            new LinkCandidateHandler(repository, unitOfWork)
                .HandleAsync(new LinkCandidateCommand(Guid.NewGuid(), Guid.NewGuid(), "c@example.com"), CancellationToken.None));
    }

    [Fact]
    public async Task Revoke_UnknownGuardian_ThrowsGuardianNotFoundError()
    {
        await Assert.ThrowsAsync<GuardianNotFoundError>(() =>
            new RevokeGuardianLinkHandler(repository, unitOfWork)
                .HandleAsync(new RevokeGuardianLinkCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Unlink_UnknownGuardian_ThrowsGuardianNotFoundError()
    {
        await Assert.ThrowsAsync<GuardianNotFoundError>(() =>
            new UnlinkCandidateHandler(repository, unitOfWork)
                .HandleAsync(new UnlinkCandidateCommand(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public async Task Revoke_WithNoLink_IsRefusedAndNothingIsSaved()
    {
        var guardian = new GuardianAggregate("guardian@example.com", "Gia Guardian");
        repository.GetByIdAsync(guardian.Id, Arg.Any<CancellationToken>()).Returns(guardian);

        await Assert.ThrowsAsync<GuardianLinkNotFoundError>(() =>
            new RevokeGuardianLinkHandler(repository, unitOfWork)
                .HandleAsync(new RevokeGuardianLinkCommand(guardian.Id, Guid.NewGuid()), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
