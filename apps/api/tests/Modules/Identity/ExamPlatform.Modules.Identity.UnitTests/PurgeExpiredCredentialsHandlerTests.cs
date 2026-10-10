using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Retention;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>The credential sweep (FR-47): what it asks the store to delete, and what it reports back.</summary>
public class PurgeExpiredCredentialsHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly IExpiredCredentialStore _store = Substitute.For<IExpiredCredentialStore>();

    private PurgeExpiredCredentialsHandler HandlerWithGraceDays(int graceDays) =>
        new(_store, new FakeClock(Now), Options.Create(new CredentialRetentionOptions { GraceDays = graceDays }));

    [Fact]
    public async Task HandleAsync_AsksForTheRowsThatExpiredBeforeTheGracePeriodBegan()
    {
        await HandlerWithGraceDays(3).HandleAsync(CancellationToken.None);

        // Three days of grace: a credential that expired less than three days ago is still kept, so a late attempt gets "expired".
        await _store.Received(1).DeleteExpiredAsync(Now.AddDays(-3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithZeroGrace_AsksForTheRowsThatExpiredBeforeNow()
    {
        await HandlerWithGraceDays(0).HandleAsync(CancellationToken.None);

        await _store.Received(1).DeleteExpiredAsync(Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_ReturnsTheCountsTheStoreRemoved()
    {
        var removed = new CredentialPurgeResult(OneTimeCodes: 4, PasswordResetTokens: 1, Sessions: 7);
        _store.DeleteExpiredAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(removed);

        var result = await HandlerWithGraceDays(1).HandleAsync(CancellationToken.None);

        Assert.Equal(removed, result);
        Assert.False(result.IsEmpty);
    }

    [Fact]
    public void CredentialPurgeResult_IsEmpty_OnlyWhenNothingWasRemoved()
    {
        Assert.True(new CredentialPurgeResult(0, 0, 0).IsEmpty);
        Assert.False(new CredentialPurgeResult(0, 0, 1).IsEmpty);
        Assert.False(new CredentialPurgeResult(1, 0, 0).IsEmpty);
        Assert.False(new CredentialPurgeResult(0, 1, 0).IsEmpty);
    }
}
