using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Queries;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class ListOutstandingOtpsHandlerTests
{
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IOtpChallengeRepository _challenges = Substitute.For<IOtpChallengeRepository>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly ListOutstandingOtpsHandler _handler;

    public ListOutstandingOtpsHandlerTests()
    {
        _handler = new ListOutstandingOtpsHandler(_challenges, _audit, new FakeClock(Now));
    }

    private static OtpChallenge Challenge(string destination, string code) =>
        OtpChallenge.Issue(
            Guid.NewGuid(), OtpChannel.Email, destination, "hash", OtpPurpose.Login, Now, TimeSpan.FromMinutes(10),
            revealableCode: code);

    [Fact]
    public async Task HandleAsync_ReturnsTheCodesWithTheirDestinationsUnmasked()
    {
        var challenge = Challenge("candidate@example.com", "123456");
        _challenges
            .ListRevealableAsync(null, Now, ListOutstandingOtpsHandler.MaxResults, Arg.Any<CancellationToken>())
            .Returns([challenge]);

        var result = await _handler.HandleAsync(
            new ListOutstandingOtpsQuery(null, Guid.NewGuid(), "SuperAdmin"), CancellationToken.None);

        var code = Assert.Single(result);
        Assert.Equal(challenge.Id, code.ChallengeId);
        Assert.Equal("candidate@example.com", code.Destination);
        Assert.Equal("123456", code.Code);
        Assert.Equal("Login", code.Purpose);
        Assert.Equal("Email", code.Channel);
        Assert.Equal(Now.AddMinutes(10), code.ExpiresAtUtc);
    }

    [Fact]
    public async Task HandleAsync_PassesTheSearchTextToTheRepository()
    {
        _challenges
            .ListRevealableAsync("jane", Now, ListOutstandingOtpsHandler.MaxResults, Arg.Any<CancellationToken>())
            .Returns([]);

        var result = await _handler.HandleAsync(
            new ListOutstandingOtpsQuery("jane", Guid.NewGuid(), "SuperAdmin"), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task HandleAsync_RecordsWhoLooked_WithoutPuttingTheCodesInTheAuditTrail()
    {
        var actor = Guid.NewGuid();
        _challenges
            .ListRevealableAsync(Arg.Any<string?>(), Now, Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns([Challenge("a@example.com", "111111"), Challenge("b@example.com", "222222")]);

        await _handler.HandleAsync(new ListOutstandingOtpsQuery("example", actor, "SuperAdmin"), CancellationToken.None);

        await _audit.Received(1).RecordAsync(
            Arg.Is<AuditEntry>(e =>
                e.ActorUserId == actor
                && e.ActorRole == "SuperAdmin"
                && e.Action == "Identity.OtpCodesViewed"
                && e.Metadata["count"] == "2"
                && e.Metadata["filtered"] == "True"
                && !e.Metadata.Values.Any(v => v.Contains("111111") || v.Contains("222222"))),
            Arg.Any<CancellationToken>());
    }
}
