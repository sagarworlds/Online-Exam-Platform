using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Application.Queries;
using NSubstitute;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.UnitTests;

/// <summary>Whether a candidate has a guardian who confirmed the link to them. Only a confirmed link that is still in force counts (FR-43).</summary>
public class GuardianLinkDirectoryTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly IGuardianRepository repository = Substitute.For<IGuardianRepository>();
    private readonly Guid candidateId = Guid.NewGuid();

    private GuardianLinkDirectory Directory => new(repository);

    private void GuardiansOfTheCandidate(params GuardianAggregate[] guardians) =>
        repository.ListByCandidateAsync(candidateId, Arg.Any<CancellationToken>()).Returns(guardians.ToList());

    /// <summary>A guardian with a link to the candidate; the link is confirmed when asked, and otherwise still pending.</summary>
    private GuardianAggregate GuardianWithLink(bool confirmed, Guid? linkedTo = null)
    {
        var guardian = new GuardianAggregate("guardian@example.com", "Gia Guardian");
        guardian.LinkCandidate(linkedTo ?? candidateId, "candidate@example.com", "hash", Now.AddDays(1));
        if (confirmed)
            guardian.VerifyCandidateLink("hash", Now);
        return guardian;
    }

    [Fact]
    public async Task AConfirmedLink_CountsAsAConfirmedGuardian()
    {
        GuardiansOfTheCandidate(GuardianWithLink(confirmed: true));

        Assert.True(await Directory.HasConfirmedGuardianAsync(candidateId, CancellationToken.None));
    }

    [Fact]
    public async Task APendingLink_IsNotEnough()
    {
        GuardiansOfTheCandidate(GuardianWithLink(confirmed: false));

        Assert.False(await Directory.HasConfirmedGuardianAsync(candidateId, CancellationToken.None));
    }

    [Fact]
    public async Task ARevokedLink_IsNotEnough()
    {
        var guardian = GuardianWithLink(confirmed: true);
        guardian.RevokeCandidateLink(candidateId);
        GuardiansOfTheCandidate(guardian);

        Assert.False(await Directory.HasConfirmedGuardianAsync(candidateId, CancellationToken.None));
    }

    [Fact]
    public async Task AnUnlinkedLink_IsNotEnough()
    {
        var guardian = GuardianWithLink(confirmed: true);
        guardian.UnlinkCandidate(candidateId);
        GuardiansOfTheCandidate(guardian);

        Assert.False(await Directory.HasConfirmedGuardianAsync(candidateId, CancellationToken.None));
    }

    [Fact]
    public async Task AConfirmedLinkToAnotherCandidate_DoesNotCount()
    {
        // The repository narrows by candidate, but the check must not rely on that alone.
        GuardiansOfTheCandidate(GuardianWithLink(confirmed: true, linkedTo: Guid.NewGuid()));

        Assert.False(await Directory.HasConfirmedGuardianAsync(candidateId, CancellationToken.None));
    }

    [Fact]
    public async Task ACandidateWithNoGuardian_HasNoConfirmedGuardian()
    {
        GuardiansOfTheCandidate();

        Assert.False(await Directory.HasConfirmedGuardianAsync(candidateId, CancellationToken.None));
    }
}
