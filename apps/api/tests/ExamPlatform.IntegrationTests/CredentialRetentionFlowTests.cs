using ExamPlatform.Modules.Identity.Application.Retention;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>The credential sweep (FR-47) against a real database: an expired code goes, a code that is still valid stays.</summary>
public class CredentialRetentionFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task ASweep_RemovesACodeThatExpiredLongAgo_AndKeepsOneThatIsStillValid()
    {
        var now = DateTime.UtcNow;
        var expired = OtpChallenge.Issue(
            null, OtpChannel.Email, "long-gone@example.com", "hash-expired", OtpPurpose.Login, now.AddDays(-10), TimeSpan.FromMinutes(10));
        var valid = OtpChallenge.Issue(
            null, OtpChannel.Email, "still-valid@example.com", "hash-valid", OtpPurpose.Login, now, TimeSpan.FromMinutes(10));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.OtpChallenges.AddRange(expired, valid);
            await db.SaveChangesAsync();
        }

        CredentialPurgeResult result;
        using (var scope = factory.Services.CreateScope())
        {
            result = await scope.ServiceProvider.GetRequiredService<PurgeExpiredCredentialsHandler>()
                .HandleAsync(CancellationToken.None);
        }

        Assert.True(result.OneTimeCodes >= 1);
        using var check = factory.Services.CreateScope();
        var rows = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.False(await rows.OtpChallenges.AnyAsync(challenge => challenge.Id == expired.Id));
        Assert.True(await rows.OtpChallenges.AnyAsync(challenge => challenge.Id == valid.Id));
    }
}
