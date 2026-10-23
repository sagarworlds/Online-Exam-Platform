using System.Net.Http.Json;
using System.Text.Json;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Endpoints.Grading;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static ExamPlatform.IntegrationTests.ExamScenarios;

namespace ExamPlatform.IntegrationTests;

/// <summary>An evaluator that answers as the test says, so what the platform does with a verdict, or with none, is what is tested and not Google's service.</summary>
public sealed class ScriptedTextAnswerEvaluator : ITextAnswerEvaluator
{
    private readonly object _gate = new();
    private readonly List<TextEvaluationRequest> _requests = [];

    /// <inheritdoc />
    public bool IsEnabled => true;

    /// <summary>What the evaluator does with a request: gives a verdict, or throws the failure a real one would.</summary>
    public Func<TextEvaluationRequest, TextEvaluation> Respond { get; set; } = _ => new TextEvaluation(TypedAnswerVerdict.Correct, "Says the same.", "scripted-model");

    /// <summary>What was asked, in order.</summary>
    public IReadOnlyList<TextEvaluationRequest> Requests
    {
        get
        {
            lock (_gate)
                return _requests.ToList();
        }
    }

    /// <summary>Forgets what was asked and goes back to saying every answer is right.</summary>
    public void Reset()
    {
        lock (_gate)
            _requests.Clear();
        Respond = _ => new TextEvaluation(TypedAnswerVerdict.Correct, "Says the same.", "scripted-model");
    }

    /// <inheritdoc />
    public Task<TextEvaluation> EvaluateAsync(TextEvaluationRequest request, CancellationToken cancellationToken)
    {
        lock (_gate)
            _requests.Add(request);

        return Task.FromResult(Respond(request));
    }
}

/// <summary>The Host with an AI evaluator that is set up, and answers as a test scripts it. The host's own timer is off: a test runs the worker itself, when it chooses.</summary>
public sealed class AiGradingApiFactory : ApiFactory
{
    /// <summary>The evaluator the host hands every typed answer to.</summary>
    public ScriptedTextAnswerEvaluator Evaluator { get; } = new();

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureTestServices(services => services.AddSingleton<ITextAnswerEvaluator>(Evaluator));
    }
}

/// <summary>
/// Typed answers judged by the AI evaluator over a real database: an answer worded differently from the accepted answers keeps the attempt in
/// grading, with no score, until the worker has the verdict; an exact answer never waits; a failure leaves the attempt waiting and loses nothing;
/// and a corrected answer key has the answers judged again against it.
/// </summary>
public sealed class AiGradingFlowTests(AiGradingApiFactory factory) : IClassFixture<AiGradingApiFactory>
{
    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) => await response.Content.ReadFromJsonAsync<JsonElement>();

    private sealed record Sitting(HttpClient Admin, HttpClient Candidate, Guid ExamId, Guid QuestionId, Guid AttemptId);

    private async Task<Guid> TextQuestionAsync(HttpClient admin, params string[] accepted)
    {
        var created = await admin.PostAsJsonAsync("/v1/questions", new { text = "<p>Capital of <b>France</b>?</p>", isTextAnswer = true, acceptedAnswers = accepted });
        await ServerErrorLog.EnsureSuccessAsync(created);
        return (await JsonAsync(created)).GetProperty("id").GetGuid();
    }

    // A candidate who has typed the answer to a one-question text exam and not yet submitted it.
    private async Task<Sitting> TypedAsync(string typed, params string[] accepted)
    {
        factory.Evaluator.Reset();
        var admin = await factory.AdminClientAsync();
        var questionId = await TextQuestionAsync(admin, accepted);
        var examId = await CreateExamAsync(admin, $"Geography {Guid.NewGuid():N}", [questionId], TimeSpan.FromMinutes(-5));
        var (candidate, _) = await factory.EnrollNewCandidateAsync(admin, examId);
        var attemptId = (await JsonAsync((await candidate.PostAsJsonAsync($"/v1/me/exams/{examId}/attempts", new { instructionsAcknowledged = true })).EnsureSuccessStatusCode())).GetProperty("id").GetGuid();
        (await candidate.PutAsJsonAsync($"/v1/me/attempts/{attemptId}/answers/{questionId}", new { text = typed })).EnsureSuccessStatusCode();
        return new Sitting(admin, candidate, examId, questionId, attemptId);
    }

    private static async Task<JsonElement> SubmitAsync(Sitting s) =>
        await JsonAsync((await s.Candidate.PostAsync($"/v1/me/attempts/{s.AttemptId}/submit", content: null)).EnsureSuccessStatusCode());

    private static Task<JsonElement> AttemptAsync(Sitting s) => s.Candidate.GetFromJsonAsync<JsonElement>($"/v1/me/attempts/{s.AttemptId}")!;

    private Task<int> RunWorkerAsync() => factory.Services.GetRequiredService<TypedAnswerGradingRunner>().RunOnceAsync(CancellationToken.None);

    private async Task ClearWaitAsync(Guid attemptId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExamRuntimeDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE \"examRuntime\".\"AttemptAnswers\" SET \"EvaluationNextAttemptAtUtc\" = NULL WHERE \"AttemptId\" = {0}", attemptId);
    }

    [Fact]
    public async Task AnAnswerWordedDifferently_KeepsTheAttemptInGradingWithNoScore_UntilTheWorkerHasTheVerdict()
    {
        var s = await TypedAsync("The French capital is Paris.", "Paris");
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var submitted = await SubmitAsync(s);

        Assert.Equal("Grading", submitted.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, submitted.GetProperty("score").ValueKind);
        var listed = await s.Candidate.GetFromJsonAsync<JsonElement>("/v1/me/exams");
        var mine = listed.EnumerateArray().Single(e => e.GetProperty("examId").GetGuid() == s.ExamId);
        Assert.Equal("Grading", Assert.Single(mine.GetProperty("attempts").EnumerateArray()).GetProperty("status").GetString());

        Assert.Equal(1, await RunWorkerAsync());

        var scored = await AttemptAsync(s);
        Assert.Equal("Submitted", scored.GetProperty("status").GetString());
        Assert.Equal(1m, scored.GetProperty("score").GetDecimal());
        var request = Assert.Single(factory.Evaluator.Requests);
        Assert.Equal("Capital of France?", request.QuestionText);
        Assert.Equal(["Paris"], request.AcceptedAnswers);
        Assert.Equal("The French capital is Paris.", request.CandidateAnswer);
    }

    [Fact]
    public async Task AnAnswerTheEvaluatorJudgesWrong_IsScoredWrong()
    {
        var s = await TypedAnswerThatIsWrongAsync();
        using var _a = s.Admin;
        using var _c = s.Candidate;

        await SubmitAsync(s);
        await RunWorkerAsync();

        var scored = await AttemptAsync(s);
        Assert.Equal("Submitted", scored.GetProperty("status").GetString());
        Assert.Equal(0m, scored.GetProperty("score").GetDecimal());
    }

    private async Task<Sitting> TypedAnswerThatIsWrongAsync()
    {
        var s = await TypedAsync("Lyon, I think", "Paris");
        factory.Evaluator.Respond = _ => new TextEvaluation(TypedAnswerVerdict.Wrong, "Names another city.", "scripted-model");
        return s;
    }

    [Fact]
    public async Task AnExactAnswer_IsScoredAtOnce_AndTheEvaluatorIsNeverAsked()
    {
        var s = await TypedAsync("  paris ", "Paris");
        using var _a = s.Admin;
        using var _c = s.Candidate;

        var submitted = await SubmitAsync(s);

        Assert.Equal("Submitted", submitted.GetProperty("status").GetString());
        Assert.Equal(1m, submitted.GetProperty("score").GetDecimal());
        Assert.Equal(0, await RunWorkerAsync());
        Assert.Empty(factory.Evaluator.Requests);
    }

    [Fact]
    public async Task WhenTheEvaluatorFails_TheAttemptWaitsInGrading_LosesNothing_AndIsScoredOnceItAnswers()
    {
        var s = await TypedAsync("The French capital is Paris.", "Paris");
        using var _a = s.Admin;
        using var _c = s.Candidate;
        await SubmitAsync(s);
        factory.Evaluator.Respond = _ => throw new TextEvaluationException("The AI service answered 503.", isPermanent: false);

        Assert.Equal(1, await RunWorkerAsync());

        // Not scored, not marked wrong: it waits, and the failure is what is recorded.
        var waiting = await AttemptAsync(s);
        Assert.Equal("Grading", waiting.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, waiting.GetProperty("score").ValueKind);

        // The wait after a failure has not passed, so nothing is asked again yet.
        factory.Evaluator.Reset();
        Assert.Equal(0, await RunWorkerAsync());
        Assert.Empty(factory.Evaluator.Requests);

        await ClearWaitAsync(s.AttemptId);
        Assert.Equal(1, await RunWorkerAsync());

        var scored = await AttemptAsync(s);
        Assert.Equal("Submitted", scored.GetProperty("status").GetString());
        Assert.Equal(1m, scored.GetProperty("score").GetDecimal());
    }

    [Fact]
    public async Task TwoWorkersAtOnce_NeverTakeTheSameAnswer()
    {
        var s = await TypedAsync("The French capital is Paris.", "Paris");
        using var _a = s.Admin;
        using var _c = s.Candidate;
        await SubmitAsync(s);

        async Task<int> TakeAsync()
        {
            using var scope = factory.Services.CreateScope();
            return (await scope.ServiceProvider.GetRequiredService<TypedAnswerGrading>().ClaimAsync(5, CancellationToken.None)).Count(c => c.AttemptId == s.AttemptId);
        }

        var taken = await Task.WhenAll(TakeAsync(), TakeAsync(), TakeAsync(), TakeAsync());

        Assert.Equal(1, taken.Sum());
        // Held by the one that took it, so a later look finds nothing due either.
        Assert.Equal(0, await TakeAsync());
    }

    [Fact]
    public async Task CorrectingTheKey_HasTheAnswerJudgedAgain_AndTheScoreIsRevisedWhenTheNewVerdictIsIn()
    {
        var s = await TypedAsync("The French capital is Paris.", "Paris");
        using var _a = s.Admin;
        using var _c = s.Candidate;
        factory.Evaluator.Respond = _ => new TextEvaluation(TypedAnswerVerdict.Wrong, "Not the same.", "scripted-model");
        await SubmitAsync(s);
        await RunWorkerAsync();
        Assert.Equal(0m, (await AttemptAsync(s)).GetProperty("score").GetDecimal());

        var correction = await JsonAsync((await s.Admin.PostAsJsonAsync(
            $"/v1/questions/{s.QuestionId}/correct-answer-key",
            new { acceptedAnswers = new[] { "Paris", "The capital of France" }, reason = "Both wordings are right." })).EnsureSuccessStatusCode());

        // Queued to be judged again against the new key, not rescored by the old verdict: the score the candidate saw stands until then.
        Assert.True(correction.GetProperty("keyChanged").GetBoolean());
        Assert.Equal(0, correction.GetProperty("attemptsRescored").GetInt32());
        Assert.Equal(0m, (await AttemptAsync(s)).GetProperty("score").GetDecimal());

        factory.Evaluator.Respond = _ => new TextEvaluation(TypedAnswerVerdict.Correct, "Same as the second answer.", "scripted-model");
        Assert.Equal(1, await RunWorkerAsync());

        var review = await JsonAsync((await s.Candidate.GetAsync($"/v1/me/attempts/{s.AttemptId}/review")).EnsureSuccessStatusCode());
        Assert.Equal(1m, review.GetProperty("score").GetDecimal());
        var revision = Assert.Single(review.GetProperty("revisions").EnumerateArray());
        Assert.Equal(0m, revision.GetProperty("previousScore").GetDecimal());
        Assert.Equal(1m, revision.GetProperty("newScore").GetDecimal());
        Assert.Equal("Both wordings are right.", revision.GetProperty("reason").GetString());
    }
}
