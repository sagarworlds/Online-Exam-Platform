using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>
/// What ends when a student's e-mail address or mobile number changes: not only their sessions, but the codes already sent to the old
/// address or number and any reset link, so whoever holds the old one cannot sign in with a code requested a moment before.
/// </summary>
public class StudentSignInRevokerTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly IOtpChallengeRepository _challenges = Substitute.For<IOtpChallengeRepository>();
    private readonly IPasswordResetTokenRepository _resetTokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly User _student = User.Register("asha@example.com", "+919800000000", new DateOnly(2000, 1, 1), "Asha Rao", Now);

    private static OtpChallenge Challenge(Guid userId, OtpPurpose purpose, string destination) =>
        OtpChallenge.Issue(userId, OtpChannel.Email, destination, "hash", purpose, Now, TimeSpan.FromMinutes(10));

    private Task RunAsync() => new StudentSignInRevoker(_challenges, _resetTokens).RevokeAsync(_student, Now.AddMinutes(1), CancellationToken.None);

    [Fact]
    public async Task RevokeAsync_EndsTheStudentsSessions_AsContactChanged()
    {
        var session = _student.StartNewSession("session-hash", Now, Now.AddHours(1), null, null);
        _challenges.GetOutstandingForUserAsync(_student.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);

        await RunAsync();

        Assert.False(session.IsActive(Now.AddMinutes(1)));
        Assert.Equal(SessionRevocationReason.ContactChanged, session.RevokedReason);
    }

    [Fact]
    public async Task RevokeAsync_EndsEveryCodeStillUsable_WhateverItsPurposeAndWhereverItWasSent()
    {
        var login = Challenge(_student.Id, OtpPurpose.Login, "asha@example.com");
        var phone = Challenge(_student.Id, OtpPurpose.Login, "+919800000000");
        var reset = Challenge(_student.Id, OtpPurpose.PasswordReset, "asha@example.com");
        _challenges.GetOutstandingForUserAsync(_student.Id, Now.AddMinutes(1), Arg.Any<CancellationToken>()).Returns([login, phone, reset]);

        await RunAsync();

        Assert.All([login, phone, reset], challenge => Assert.True(challenge.IsSuperseded));
    }

    [Fact]
    public async Task RevokeAsync_RevokesTheStudentsResetLinks()
    {
        _challenges.GetOutstandingForUserAsync(_student.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);

        await RunAsync();

        await _resetTokens.Received(1).RevokeOutstandingForUserAsync(_student.Id, Now.AddMinutes(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RevokeAsync_WithNothingOutstanding_StillEndsTheSessions()
    {
        var session = _student.StartNewSession("session-hash", Now, Now.AddHours(1), null, null);
        _challenges.GetOutstandingForUserAsync(_student.Id, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns([]);

        await RunAsync();

        Assert.False(session.IsActive(Now.AddMinutes(1)));
    }
}
