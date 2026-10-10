using ExamPlatform.Modules.Guardian.Application;
using ExamPlatform.Modules.Guardian.Application.Commands;
using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Domain;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;
using NSubstitute;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.UnitTests;

public class VerifyGuardianLinkHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly IGuardianRepository repository = Substitute.For<IGuardianRepository>();
    private readonly IGuardianUnitOfWork unitOfWork = Substitute.For<IGuardianUnitOfWork>();
    private readonly FakeClock clock = new(Now);

    private VerifyGuardianLinkHandler Handler => new(repository, unitOfWork, clock);

    /// <summary>A guardian with a pending link whose code is the one returned, so the test can present it.</summary>
    private (GuardianAggregate Guardian, string Code, Guid CandidateId) PendingLink(DateTime expiresAt)
    {
        var issued = GuardianLinkToken.Issue(Now);
        var guardian = new GuardianAggregate("guardian@example.com", "Gia Guardian");
        var candidateId = Guid.NewGuid();
        guardian.LinkCandidate(candidateId, "c@example.com", GuardianLinkToken.Hash(issued.Raw), expiresAt);
        return (guardian, issued.Raw, candidateId);
    }

    [Fact]
    public async Task Verify_MissingCode_IsRefusedWithoutLookingAnythingUp()
    {
        await Assert.ThrowsAsync<InvalidLinkTokenError>(() =>
            Handler.HandleAsync(new VerifyGuardianLinkCommand(null), CancellationToken.None));

        await repository.DidNotReceive().GetByVerificationTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Verify_UnknownCode_IsRefused()
    {
        repository.GetByVerificationTokenHashAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((GuardianAggregate?)null);

        await Assert.ThrowsAsync<InvalidLinkTokenError>(() =>
            Handler.HandleAsync(new VerifyGuardianLinkCommand("not-a-code"), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Verify_CodeWithinItsLifetime_ConfirmsTheLinkAndSavesIt()
    {
        var (guardian, code, candidateId) = PendingLink(Now.AddDays(14));
        repository.GetByVerificationTokenHashAsync(GuardianLinkToken.Hash(code), Arg.Any<CancellationToken>()).Returns(guardian);

        var confirmed = await Handler.HandleAsync(new VerifyGuardianLinkCommand(code), CancellationToken.None);

        Assert.Equal(candidateId, confirmed.CandidateId);
        Assert.Equal(GuardianLinkStatus.Verified, confirmed.Status);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Verify_ExpiredCode_IsRefusedAndNothingIsSaved()
    {
        var (guardian, code, _) = PendingLink(Now.AddMinutes(-1));
        repository.GetByVerificationTokenHashAsync(GuardianLinkToken.Hash(code), Arg.Any<CancellationToken>()).Returns(guardian);

        await Assert.ThrowsAsync<InvalidLinkTokenError>(() =>
            Handler.HandleAsync(new VerifyGuardianLinkCommand(code), CancellationToken.None));

        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
