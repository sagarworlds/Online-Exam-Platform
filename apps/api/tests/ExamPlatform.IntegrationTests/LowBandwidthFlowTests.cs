using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Low-bandwidth mode (FR-53) over real HTTP and a real database: an attempt sent without the pictures in its questions, each picture
/// fetched on request by its key and only by the candidate sitting it, and responses compressed for a client that accepts it.
/// </summary>
public sealed class LowBandwidthFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // A real 1x1 PNG, so the question's sanitizer takes it as a picture.
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");
    private static readonly string PngTag = $"<img src=\"data:image/png;base64,{Convert.ToBase64String(Png)}\" alt=\"A map\">";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record Sitting(HttpClient Admin, HttpClient Candidate, Guid ExamId, Guid QuestionId, Guid AttemptId);

    /// <summary>A question whose text holds a picture, in a published exam with a candidate enrolled and their attempt open.</summary>
    private async Task<Sitting> SittingAsync()
    {
        var admin = await factory.AdminClientAsync();
        var questionId = await CreateQuestionAsync(admin, $"<p>Which city is marked?</p>{PngTag}", "Paris", "Rome");
        var examId = await CreateExamAsync(admin, $"Geography {Guid.NewGuid():N}", [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        var started = await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true });
        var attemptId = (await JsonAsync(started.EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        return new Sitting(admin, candidate, examId, questionId, attemptId);
    }

    private static string QuestionText(JsonElement attempt) =>
        attempt.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("text").GetString()!;

    [Fact]
    public async Task ByDefault_TheQuestionCarriesItsPictureInline()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var attempt = await s.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{s.AttemptId}");

        Assert.Contains("data:image/png;base64", QuestionText(attempt));
    }

    [Fact]
    public async Task WithLiteTrue_NoPictureDataIsSent_AndTheMarkerNamesThePicture()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var attempt = await s.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{s.AttemptId}?lite=true");

        var text = QuestionText(attempt);
        Assert.DoesNotContain("base64", text);
        Assert.Contains("Which city is marked?", text);
        Assert.Contains($"lazy-media--q-0 lazy-bytes--{Png.Length}", text);
        Assert.Contains("alt=\"A map\"", text);
    }

    [Fact]
    public async Task APictureIsServedToItsCandidate_AsTheBytesTheQuestionHolds_AndMayBeKept()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var response = await s.Candidate.GetAsync($"/v1/me/attempts/{s.AttemptId}/questions/{s.QuestionId}/pictures/q-0");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await response.Content.ReadAsByteArrayAsync());
        var cache = response.Headers.CacheControl;
        Assert.True(cache?.Private);
        Assert.True(cache?.MaxAge > TimeSpan.Zero);
    }

    [Theory]
    [InlineData("q-1")]
    [InlineData("nonsense")]
    [InlineData("o00000000000000000000000000000000-0")]
    public async Task APictureTheQuestionDoesNotHave_IsNotFound(string key)
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var response = await s.Candidate.GetAsync($"/v1/me/attempts/{s.AttemptId}/questions/{s.QuestionId}/pictures/{key}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("question_media_not_found", (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task AQuestionOfAnotherExam_IsNotServedThroughThisAttempt()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var other = await CreateQuestionAsync(s.Admin, $"<p>Elsewhere</p>{PngTag}", "Yes", "No");

        var response = await s.Candidate.GetAsync($"/v1/me/attempts/{s.AttemptId}/questions/{other}/pictures/q-0");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("question_not_in_attempt", (await JsonAsync(response)).GetProperty("title").GetString());
    }

    [Fact]
    public async Task AnotherCandidate_AndAnAnonymousCaller_CannotReadTheAttemptsPictures()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        var (other, _) = await factory.EnrollNewCandidateAsync(s.Admin, s.ExamId);
        using var _o = other;
        var route = $"/v1/me/attempts/{s.AttemptId}/questions/{s.QuestionId}/pictures/q-0";

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync(route)).StatusCode);
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task OnceTheAttemptIsOver_ThereIsNoPageToShowAPictureOn()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        (await s.Candidate.PostAsync($"/v1/me/attempts/{s.AttemptId}/submit", content: null)).EnsureSuccessStatusCode();

        var response = await s.Candidate.GetAsync($"/v1/me/attempts/{s.AttemptId}/questions/{s.QuestionId}/pictures/q-0");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- compression -----------------------------------------------------------------------------------

    [Fact]
    public async Task AClientThatAcceptsGzip_GetsItsAttemptCompressed()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/me/attempts/{s.AttemptId}");
        request.Headers.AcceptEncoding.ParseAdd("gzip");

        var response = await s.Candidate.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        Assert.Contains("Accept-Encoding", response.Headers.Vary);
    }

    [Fact]
    public async Task AClientThatAsksForNothing_GetsItPlain()
    {
        var s = await SittingAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var response = await s.Candidate.GetAsync($"/v1/me/attempts/{s.AttemptId}");

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task TheSignInRoutes_AreNeverCompressed_BecauseTheirAnswersCarryTokens()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");

        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email = "nobody@tests.local", password = "wrong" });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(response.Content.Headers.ContentEncoding);
    }
}
