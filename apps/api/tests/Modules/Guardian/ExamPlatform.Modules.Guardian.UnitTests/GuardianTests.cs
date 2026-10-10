using ExamPlatform.Modules.Guardian.Application;
using ExamPlatform.Modules.Guardian.Application.Commands;
using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Domain;
using ExamPlatform.Modules.Guardian.Domain.Events;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;
using NSubstitute;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.UnitTests;

public class GuardianTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Expiry = Now + GuardianLinkToken.Lifetime;

    private static GuardianAggregate NewGuardian() => new("guardian@example.com", "Gia Guardian");

    [Fact]
    public void LinkCandidate_WhenAlreadyLinked_ThrowsGuardianAlreadyLinkedError()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", "hash-1", Expiry);

        Assert.Throws<GuardianAlreadyLinkedError>(() => guardian.LinkCandidate(candidateId, "c@example.com", "hash-2", Expiry));
    }

    [Fact]
    public void RevokeCandidateLink_MarksTheLinkRevoked()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", "hash-1", Expiry);

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
        guardian.LinkCandidate(candidateId, "c@example.com", "hash-1", Expiry);
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
        guardian.LinkCandidate(candidateId, "c@example.com", "hash-1", Expiry);

        guardian.UnlinkCandidate(candidateId);

        Assert.Null(guardian.GetCandidateLink(candidateId));
        guardian.LinkCandidate(candidateId, "c@example.com", "hash-2", Expiry);
    }

    [Fact]
    public void VerifyCandidateLink_WithTheCodesHash_ConfirmsTheLinkAndRaisesTheEvent()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", "hash-1", Expiry);

        var link = guardian.VerifyCandidateLink("hash-1", Now);

        Assert.Equal(GuardianLinkStatus.Verified, link.Status);
        Assert.Equal(Now, link.VerifiedAt);
        var confirmed = Assert.Single(guardian.DomainEvents.OfType<GuardianLinkVerifiedEvent>());
        Assert.Equal(candidateId, confirmed.CandidateId);
    }

    [Fact]
    public void VerifyCandidateLink_WithAnotherCodesHash_ThrowsInvalidLinkTokenError()
    {
        var guardian = NewGuardian();
        guardian.LinkCandidate(Guid.NewGuid(), "c@example.com", "hash-1", Expiry);

        Assert.Throws<InvalidLinkTokenError>(() => guardian.VerifyCandidateLink("hash-2", Now));
    }

    [Fact]
    public void VerifyCandidateLink_AfterTheCodeExpires_IsRefusedAndTheLinkStaysPending()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", "hash-1", Expiry);

        // Exactly at the expiry moment is already too late: the code is valid strictly before it.
        Assert.Throws<InvalidLinkTokenError>(() => guardian.VerifyCandidateLink("hash-1", Expiry));

        Assert.Equal(GuardianLinkStatus.Pending, guardian.GetCandidateLink(candidateId)!.Status);
    }

    [Fact]
    public void VerifyCandidateLink_Twice_ThrowsGuardianLinkNotPendingError()
    {
        var guardian = NewGuardian();
        guardian.LinkCandidate(Guid.NewGuid(), "c@example.com", "hash-1", Expiry);
        guardian.VerifyCandidateLink("hash-1", Now);

        Assert.Throws<GuardianLinkNotPendingError>(() => guardian.VerifyCandidateLink("hash-1", Now));
    }

    [Fact]
    public void VerifyCandidateLink_OfARevokedLink_ThrowsGuardianLinkNotPendingError()
    {
        var guardian = NewGuardian();
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", "hash-1", Expiry);
        guardian.RevokeCandidateLink(candidateId);

        Assert.Throws<GuardianLinkNotPendingError>(() => guardian.VerifyCandidateLink("hash-1", Now));
    }
}

public class GuardianLinkHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly IGuardianRepository repository = Substitute.For<IGuardianRepository>();
    private readonly IGuardianUnitOfWork unitOfWork = Substitute.For<IGuardianUnitOfWork>();
    private readonly IGuardianConsentNotifier notifier = Substitute.For<IGuardianConsentNotifier>();
    private readonly IGuardianConsentLinkBuilder linkBuilder = Substitute.For<IGuardianConsentLinkBuilder>();
    private readonly FakeClock clock = new(Now);

    private LinkCandidateHandler Handler => new(repository, unitOfWork, notifier, linkBuilder, clock);

    [Fact]
    public async Task Link_UnknownGuardian_ThrowsGuardianNotFoundError()
    {
        await Assert.ThrowsAsync<GuardianNotFoundError>(() =>
            Handler.HandleAsync(new LinkCandidateCommand(Guid.NewGuid(), Guid.NewGuid(), "c@example.com"), CancellationToken.None));
    }

    [Fact]
    public async Task Link_EmailsTheGuardianAConfirmationLink_AndStoresOnlyTheHashOfTheCodeSent()
    {
        var guardian = new GuardianAggregate("guardian@example.com", "Gia Guardian");
        repository.GetByIdAsync(guardian.Id, Arg.Any<CancellationToken>()).Returns(guardian);
        string? sentCode = null;
        linkBuilder.Build(Arg.Do<string>(code => sentCode = code)).Returns("https://app.example/guardian/confirm-link?token=sent");
        notifier.SendAsync(Arg.Any<GuardianConsentRequest>(), Arg.Any<CancellationToken>()).Returns(true);
        var candidateId = Guid.NewGuid();

        var result = await Handler.HandleAsync(new LinkCandidateCommand(guardian.Id, candidateId, "c@example.com"), CancellationToken.None);

        Assert.True(result.ConsentRequestSent);
        Assert.Null(result.ConsentLink);
        await notifier.Received(1).SendAsync(
            Arg.Is<GuardianConsentRequest>(r =>
                r.GuardianEmail == "guardian@example.com" &&
                r.CandidateEmail == "c@example.com" &&
                r.ConfirmLink == "https://app.example/guardian/confirm-link?token=sent" &&
                r.ExpiresAtUtc == Now + GuardianLinkToken.Lifetime),
            Arg.Any<CancellationToken>());
        // The platform keeps only the hash, so the stored hash must be the hash of the code the guardian was sent.
        Assert.Equal(GuardianLinkToken.Hash(sentCode!), guardian.GetCandidateLink(candidateId)!.VerificationTokenHash);
    }

    [Fact]
    public async Task Link_WhenNoEmailIsSent_HandsTheConfirmationLinkBackAndKeepsTheLink()
    {
        var guardian = new GuardianAggregate("guardian@example.com", "Gia Guardian");
        repository.GetByIdAsync(guardian.Id, Arg.Any<CancellationToken>()).Returns(guardian);
        linkBuilder.Build(Arg.Any<string>()).Returns("https://app.example/guardian/confirm-link?token=handed");
        notifier.SendAsync(Arg.Any<GuardianConsentRequest>(), Arg.Any<CancellationToken>()).Returns(false);

        var result = await Handler.HandleAsync(new LinkCandidateCommand(guardian.Id, Guid.NewGuid(), "c@example.com"), CancellationToken.None);

        Assert.False(result.ConsentRequestSent);
        Assert.Equal("https://app.example/guardian/confirm-link?token=handed", result.ConsentLink);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
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
