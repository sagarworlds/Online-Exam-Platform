using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Question content is stored encrypted (#57): what the database holds is ciphertext, the author reads and searches the text as written,
/// a value stored as plaintext before the switch is encrypted by the backfill, and the admin status reports that everything is encrypted.
/// </summary>
public sealed class QuestionContentEncryptionFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<string> StoredTextAsync(Guid questionId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QuestionBankDbContext>();
        return await db.Database
            .SqlQueryRaw<string>("SELECT \"Text\" AS \"Value\" FROM \"questionBank\".\"Questions\" WHERE \"Id\" = {0}", questionId)
            .SingleAsync();
    }

    [Fact]
    public async Task TheStoredStem_IsCiphertext_AndTheAuthorReadsItAsWritten()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Capital of France, encrypted?", "Paris", "Rome");

        var stored = await StoredTextAsync(questionId);
        Assert.StartsWith(QuestionContentCipher.Prefix, stored, StringComparison.Ordinal);
        Assert.DoesNotContain("Capital of France", stored, StringComparison.Ordinal);

        var question = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");
        Assert.Contains("Capital of France, encrypted?", question.GetProperty("text").GetString());
    }

    [Fact]
    public async Task TheSearch_StillFindsAQuestionByWhatItSays()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Boiling point of water in kelvin, searchable", "373", "100");

        var found = await admin.GetFromJsonAsync<JsonElement>("/v1/questions?q=kelvin%2C%20searchable");

        Assert.Contains(found.EnumerateArray(), q => q.GetProperty("id").GetGuid() == questionId);
    }

    [Fact]
    public async Task APlaintextStem_LeftFromBeforeTheSwitch_IsEncryptedByTheBackfill_AndStillReadsAsWritten()
    {
        using var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, "Stem to be left as plaintext", "A", "B");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<QuestionBankDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"questionBank\".\"Questions\" SET \"Text\" = 'Plaintext stem from before the switch' WHERE \"Id\" = {0}", questionId);
            Assert.Equal("Plaintext stem from before the switch", await StoredTextAsync(questionId));

            var scan = await scope.ServiceProvider.GetRequiredService<QuestionContentBackfill>().RunAsync(CancellationToken.None);
            Assert.True(scan >= 1);
        }

        var stored = await StoredTextAsync(questionId);
        Assert.StartsWith(QuestionContentCipher.Prefix, stored, StringComparison.Ordinal);
        var question = await admin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");
        Assert.Equal("Plaintext stem from before the switch", question.GetProperty("text").GetString());
    }

    [Fact]
    public async Task TheAdminStatus_ReportsEverythingEncrypted_AfterTheBackfill()
    {
        using var admin = await factory.AdminClientAsync();
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<QuestionContentBackfill>().RunAsync(CancellationToken.None);
        }

        var status = await admin.GetFromJsonAsync<JsonElement>("/v1/questions/encryption-status");

        Assert.True(status.GetProperty("contentValues").GetInt32() >= 1);
        Assert.Equal(0, status.GetProperty("plaintextValues").GetInt32());
        Assert.True(status.GetProperty("complete").GetBoolean());
    }

    [Fact]
    public async Task TheStatus_IsRefusedToACandidate()
    {
        var (candidate, _) = await factory.CandidateClientAsync();
        using var _c = candidate;

        var response = await candidate.GetAsync("/v1/questions/encryption-status");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
