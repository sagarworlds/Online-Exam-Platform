using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class OtpChallengeIssuerTests
{
    private const string Destination = "candidate@example.com";

    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IOtpChallengeRepository _challengeRepository = Substitute.For<IOtpChallengeRepository>();
    private readonly IOtpCodeGenerator _codeGenerator = Substitute.For<IOtpCodeGenerator>();
    private readonly IOtpSender _sender = Substitute.For<IOtpSender>();
    private readonly OtpChallengeIssuer _issuer;

    public OtpChallengeIssuerTests()
    {
        _codeGenerator.GenerateCode().Returns("123456");
        _codeGenerator.Hash("123456").Returns("hashed-123456");
        _issuer = new OtpChallengeIssuer(_challengeRepository, _codeGenerator, _sender, new FakeClock(Now));
    }

    private static OtpChallenge EarlierChallenge(DateTime issuedAt) =>
        OtpChallenge.Issue(
            Guid.NewGuid(), OtpChannel.Email, Destination, "hashed-earlier", OtpPurpose.Login, issuedAt, TimeSpan.FromMinutes(10));

    [Fact]
    public async Task IssueAsync_SupersedesOutstandingChallengesForSameDestinationAndPurpose()
    {
        var earlier = new[] { EarlierChallenge(Now.AddMinutes(-3)), EarlierChallenge(Now.AddMinutes(-1)) };
        _challengeRepository
            .GetOutstandingAsync(Destination, OtpPurpose.Login, Now, Arg.Any<CancellationToken>())
            .Returns(earlier);

        var challengeId = await _issuer.IssueAsync(
            Guid.NewGuid(), OtpChannel.Email, Destination, OtpPurpose.Login, CancellationToken.None);

        // Every earlier live code is retired at the moment the new one is issued, so only
        // the new code can be verified and the attempt budget cannot be multiplied.
        Assert.All(earlier, c => Assert.Equal(Now, c.SupersededAtUtc));
        await _challengeRepository.Received(1).AddAsync(
            Arg.Is<OtpChallenge>(c => c.Id == challengeId && !c.IsSuperseded && c.CodeHash == "hashed-123456"),
            Arg.Any<CancellationToken>());
        await _sender.Received(1).SendAsync(OtpChannel.Email, Destination, "123456", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task IssueDecoyAsync_PersistsUnmatchableChallengeAndSendsNothing()
    {
        _codeGenerator.Hash(Arg.Any<string>()).Returns(call => "hashed-" + call.Arg<string>());
        var earlier = EarlierChallenge(Now.AddMinutes(-1));
        _challengeRepository
            .GetOutstandingAsync(Destination, OtpPurpose.Login, Now, Arg.Any<CancellationToken>())
            .Returns(new[] { earlier });
        OtpChallenge? stored = null;
        _challengeRepository
            .When(r => r.AddAsync(Arg.Any<OtpChallenge>(), Arg.Any<CancellationToken>()))
            .Do(call => stored = call.Arg<OtpChallenge>());

        var challengeId = await _issuer.IssueDecoyAsync(OtpChannel.Email, Destination, OtpPurpose.Login, CancellationToken.None);

        // Stored like a real challenge, so verifying it answers like one, but with no user
        // and the hash of 64 random hex characters, which no 6-digit code can match.
        Assert.NotNull(stored);
        Assert.Equal(challengeId, stored.Id);
        Assert.Null(stored.UserId);
        Assert.Equal(OtpPurpose.Login, stored.Purpose);
        Assert.Equal(Destination, stored.Destination);
        Assert.Matches("^hashed-[0-9A-F]{64}$", stored.CodeHash);
        Assert.Equal(Now, earlier.SupersededAtUtc);
        _codeGenerator.DidNotReceive().GenerateCode();
        await _sender.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default!, default);
    }
}
