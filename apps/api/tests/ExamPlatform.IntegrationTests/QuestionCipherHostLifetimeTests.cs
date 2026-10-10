using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Two test hosts that run one after the other in the same process. EF Core keeps the QuestionBank model for the whole process, and
/// that model's value converters hold the content cipher of the host that built it first. When that host is disposed, a later host must
/// still write and read questions through those same converters. Otherwise the later host's first write fails with a disposed
/// <c>IServiceProvider</c>, which is what the intermittent CI failure on <c>POST /v1/questions</c> looks like.
/// </summary>
public sealed class QuestionCipherHostLifetimeTests
{
    [Fact]
    public async Task AQuestionWrittenByALaterHost_StillSavesAndReads_AfterAnEarlierHostIsDisposed()
    {
        var earlier = new ApiFactory();
        try
        {
            await earlier.InitializeAsync();
            using var earlierAdmin = await earlier.AdminClientAsync();
            await CreateQuestionAsync(earlierAdmin, "Written while the first host was alive", "Right", "Wrong");
        }
        finally
        {
            await DisposeAsync(earlier);
        }

        var later = new ApiFactory();
        try
        {
            await later.InitializeAsync();
            using var laterAdmin = await later.AdminClientAsync();
            var questionId = await CreateQuestionAsync(laterAdmin, "Written after the first host was disposed", "Right", "Wrong");

            var question = await laterAdmin.GetFromJsonAsync<JsonElement>($"/v1/questions/{questionId}");
            Assert.Contains("Written after the first host was disposed", question.GetProperty("text").GetString());
        }
        finally
        {
            await DisposeAsync(later);
        }
    }

    /// <summary>Stops the test host and then its Postgres container; the factory's own lifetime does both, but the host alone is disposed first.</summary>
    private static async Task DisposeAsync(ApiFactory factory)
    {
        await factory.DisposeAsync();
        await ((IAsyncLifetime)factory).DisposeAsync();
    }
}
