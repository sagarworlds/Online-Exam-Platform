using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// The chain of AI providers (Groq's models first, then Gemini): which one is asked, what happens when one cannot answer, and what it says about
/// itself to the worker and to staff.
/// </summary>
public class FallbackTextAnswerEvaluatorTests
{
    private static readonly TextEvaluationRequest Request = new("Capital of France?", ["Paris"], "paris");
    private static readonly DateTime Start = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A provider whose answers the test scripts, with a real gate on a clock the test moves.</summary>
    private sealed class FakeProvider(string name, string model, FakeClock clock, bool enabled = true) : IGatedTextAnswerEvaluator
    {
        public EvaluatorGate Gate { get; } = new(name, model, 0, clock, NullLogger.Instance, jitter: () => 0);

        public bool IsEnabled { get; } = enabled;

        public int Asked { get; private set; }

        public Func<TextEvaluation>? Answer { get; set; }

        public Func<TextEvaluationException>? Fail { get; set; }

        public Task<TextEvaluation> EvaluateAsync(TextEvaluationRequest request, CancellationToken cancellationToken)
        {
            Asked++;
            if (Fail is not null)
                throw Fail();

            return Task.FromResult(Answer!());
        }
    }

    private static TextEvaluation Verdict(string model, TypedAnswerVerdict verdict = TypedAnswerVerdict.Correct) => new(verdict, "ok", model);

    private static FallbackTextAnswerEvaluator Chain(params IGatedTextAnswerEvaluator[] providers) => new(providers, NullLogger<FallbackTextAnswerEvaluator>.Instance);

    private static FakeProvider Provider(string name, string model, FakeClock? clock = null, bool enabled = true) => new(name, model, clock ?? new FakeClock(Start), enabled);

    [Fact]
    public async Task TheFirstProviderThatAnswers_IsTheAnswer_AndTheRestAreNotAsked()
    {
        var groq = Provider("Groq", "big");
        var gemini = Provider("Gemini", "flash");
        groq.Answer = () => Verdict("big");
        gemini.Answer = () => Verdict("flash");

        var evaluation = await Chain(groq, gemini).EvaluateAsync(Request, CancellationToken.None);

        Assert.Equal("big", evaluation.Model);
        Assert.Equal((1, 0), (groq.Asked, gemini.Asked));
    }

    [Fact]
    public async Task WhenTheFirstCannotAnswer_TheNextIsAsked_AndItsVerdictIsTheResult()
    {
        var groq = Provider("Groq", "big");
        var small = Provider("Groq", "small");
        groq.Fail = () => new TextEvaluationException("quota used up", false, retryAfter: TimeSpan.FromMinutes(30));
        small.Answer = () => Verdict("small", TypedAnswerVerdict.Wrong);

        var evaluation = await Chain(groq, small).EvaluateAsync(Request, CancellationToken.None);

        Assert.Equal(("small", TypedAnswerVerdict.Wrong), (evaluation.Model, evaluation.Verdict));
    }

    [Fact]
    public async Task AProviderThatIsPaused_IsPassedOverWithoutBeingAsked()
    {
        var groq = Provider("Groq", "big");
        var gemini = Provider("Gemini", "flash");
        groq.Gate.RecordQuotaExhausted("day used up");
        gemini.Answer = () => Verdict("flash");

        var evaluation = await Chain(groq, gemini).EvaluateAsync(Request, CancellationToken.None);

        Assert.Equal("flash", evaluation.Model);
        Assert.Equal(0, groq.Asked);
    }

    [Fact]
    public async Task AProviderWithNoKey_IsSkipped()
    {
        var off = Provider("Groq", "big", enabled: false);
        var gemini = Provider("Gemini", "flash");
        gemini.Answer = () => Verdict("flash");

        await Chain(off, gemini).EvaluateAsync(Request, CancellationToken.None);

        Assert.Equal(0, off.Asked);
    }

    [Fact]
    public async Task WhenNoProviderCanAnswer_TheFailureNamesTheFirstReason_AndIsNotPermanent()
    {
        var groq = Provider("Groq", "big");
        var gemini = Provider("Gemini", "flash");
        groq.Fail = () => new TextEvaluationException("The AI service answered 503.", false);
        gemini.Fail = () => new TextEvaluationException("The AI service answered 429.", false);

        var failure = await Assert.ThrowsAsync<TextEvaluationException>(() => Chain(groq, gemini).EvaluateAsync(Request, CancellationToken.None));

        Assert.StartsWith("Groq big: The AI service answered 503.", failure.Message, StringComparison.Ordinal);
        Assert.Contains("1 more provider", failure.Message);
        Assert.False(failure.IsPermanent);
        Assert.Equal((1, 1), (groq.Asked, gemini.Asked));
    }

    [Fact]
    public async Task WhenEveryProviderRefusesForGood_TheFailureIsPermanent()
    {
        var groq = Provider("Groq", "big");
        var gemini = Provider("Gemini", "flash");
        groq.Fail = () => new TextEvaluationException("Invalid key.", true);
        gemini.Fail = () => new TextEvaluationException("Model gone.", true);

        var failure = await Assert.ThrowsAsync<TextEvaluationException>(() => Chain(groq, gemini).EvaluateAsync(Request, CancellationToken.None));

        Assert.True(failure.IsPermanent);
    }

    [Fact]
    public async Task WhenEveryProviderIsPaused_TheFailureSaysWhenTheSoonestCanBeAskedAgain_AndNoOneIsAsked()
    {
        var groq = Provider("Groq", "big");
        var gemini = Provider("Gemini", "flash");
        groq.Gate.RecordRateLimited(TimeSpan.FromSeconds(40), "too fast");
        gemini.Gate.RecordQuotaExhausted("day used up");

        var failure = await Assert.ThrowsAsync<TextEvaluationException>(() => Chain(groq, gemini).EvaluateAsync(Request, CancellationToken.None));

        Assert.Equal(TimeSpan.FromSeconds(40), failure.RetryAfter);
        Assert.False(failure.IsPermanent);
        Assert.Equal((0, 0), (groq.Asked, gemini.Asked));
    }

    [Fact]
    public async Task WhenOneFailedForAnAnswerAndTheOthersArePaused_NoWaitIsInventedBeyondWhatIsKnown()
    {
        var groq = Provider("Groq", "big");
        var gemini = Provider("Gemini", "flash");
        groq.Fail = () => new TextEvaluationException("This answer was refused.", true);
        gemini.Gate.RecordRefused("key");

        var failure = await Assert.ThrowsAsync<TextEvaluationException>(() => Chain(groq, gemini).EvaluateAsync(Request, CancellationToken.None));

        // Gemini is paused but Groq is not, so some provider can be asked at once: the caller's own backoff decides.
        Assert.Null(failure.RetryAfter);
        Assert.True(failure.IsPermanent);
    }

    [Fact]
    public async Task ACallerAskingToStop_IsNotSwallowedAsAFailureToFallOverFrom()
    {
        var groq = Provider("Groq", "big");
        var gemini = Provider("Gemini", "flash");
        groq.Fail = () => throw new OperationCanceledException();
        gemini.Answer = () => Verdict("flash");

        await Assert.ThrowsAsync<OperationCanceledException>(() => Chain(groq, gemini).EvaluateAsync(Request, CancellationToken.None));

        Assert.Equal(0, gemini.Asked);
    }

    // ---- what the chain says about itself ----

    [Fact]
    public async Task WithNoProviderSetUp_TheChainIsOff_AndRefusesToBeAsked()
    {
        var chain = Chain(Provider("Groq", "big", enabled: false), Provider("Gemini", "flash", enabled: false));

        Assert.False(chain.IsEnabled);
        Assert.Null(chain.PausedFor);
        Assert.False(chain.Status.Enabled);
        Assert.Empty(chain.Status.Providers);
        await Assert.ThrowsAsync<InvalidOperationException>(() => chain.EvaluateAsync(Request, CancellationToken.None));
    }

    [Fact]
    public void WhileAnyProviderCanBeAsked_TheChainIsNotPaused()
    {
        var groq = Provider("Groq", "big");
        var gemini = Provider("Gemini", "flash");
        groq.Gate.RecordQuotaExhausted("day used up");

        Assert.Null(Chain(groq, gemini).PausedFor);
    }

    [Fact]
    public void WhenEveryProviderThatIsSetUpIsPaused_TheChainIsPausedUntilTheSoonest_ignoringOnesThatAreOff()
    {
        var groq = Provider("Groq", "big");
        var off = Provider("Other", "m", enabled: false);
        var gemini = Provider("Gemini", "flash");
        groq.Gate.RecordRateLimited(TimeSpan.FromSeconds(90), "too fast");
        gemini.Gate.RecordRateLimited(TimeSpan.FromSeconds(20), "too fast");

        Assert.Equal(TimeSpan.FromSeconds(20), Chain(groq, off, gemini).PausedFor);
    }

    [Fact]
    public void TheStatus_ListsEachProviderThatIsSetUp_InTheOrderTheyAreAsked_WithItsOwnReason()
    {
        var groq = Provider("Groq", "big");
        var off = Provider("Other", "m", enabled: false);
        var gemini = Provider("Gemini", "flash");
        groq.Gate.RecordQuotaExhausted("429 RESOURCE_EXHAUSTED: day used up");

        var status = Chain(groq, off, gemini).Status;

        Assert.True(status.Enabled);
        Assert.Equal(["Groq", "Gemini"], status.Providers.Select(p => p.Name));
        Assert.Equal(ProviderState.QuotaExhausted, status.Providers[0].State);
        Assert.Equal("429 RESOURCE_EXHAUSTED: day used up", status.Providers[0].LastError);
        Assert.Equal(ProviderState.Ready, status.Providers[1].State);
    }
}

/// <summary>What staff are told about judging typed answers: the providers' state and how many answers wait.</summary>
public class GetAiGradingStatusHandlerTests
{
    [Fact]
    public async Task ItReportsTheProvidersAsTheyAre_AndHowManyAnswersWait()
    {
        var health = Substitute.For<IEvaluatorHealth>();
        var provider = new ProviderStatus("Groq", "big", ProviderState.RateLimited, new DateTime(2026, 10, 25, 12, 1, 0, DateTimeKind.Utc), "429: slow down", null, null, 3, 2, 0, 0);
        health.Status.Returns(new EvaluatorStatus(true, [provider]));
        var attempts = Substitute.For<ExamPlatform.Modules.ExamRuntime.Application.Ports.IAttemptRepository>();
        attempts.CountAnswersAwaitingEvaluationAsync(Arg.Any<CancellationToken>()).Returns(17);

        var status = await new GetAiGradingStatusHandler(health, attempts).HandleAsync(CancellationToken.None);

        Assert.True(status.Enabled);
        Assert.Equal([provider], status.Providers);
        Assert.Equal(17, status.WaitingAnswers);
    }

    [Fact]
    public async Task WithNoProviderSetUp_ItSaysSo()
    {
        var health = Substitute.For<IEvaluatorHealth>();
        health.Status.Returns(new EvaluatorStatus(false, []));
        var attempts = Substitute.For<ExamPlatform.Modules.ExamRuntime.Application.Ports.IAttemptRepository>();

        var status = await new GetAiGradingStatusHandler(health, attempts).HandleAsync(CancellationToken.None);

        Assert.False(status.Enabled);
        Assert.Empty(status.Providers);
    }
}

/// <summary>The settings for any OpenAI-compatible provider: what an operator can get wrong, and what is said about each.</summary>
public class OpenAiCompatibleOptionsTests
{
    [Fact]
    public void TheDefaults_AreGroqsFreeTier_AreValid_AndAreOffUntilAKeyIsSet()
    {
        var options = new OpenAiCompatibleOptions();

        Assert.True(options.IsValid);
        Assert.False(options.IsEnabled);
        Assert.Equal("https://api.groq.com/openai/v1", options.BaseUrl);
        Assert.Equal("openai/gpt-oss-120b", options.Model);
        Assert.Equal(25, options.RequestsPerMinute);
        Assert.Equal("OpenAI-compatible (api.groq.com)", options.ProviderName);
    }

    [Theory]
    [InlineData("key", true, true)]
    [InlineData("key", false, false)]
    [InlineData(null, true, false)]
    [InlineData("  ", true, false)]
    public void AKeyAndTheSwitch_DecideWhetherItIsOn(string? key, bool enabled, bool expected) =>
        Assert.Equal(expected, new OpenAiCompatibleOptions { ApiKey = key, Enabled = enabled }.IsEnabled);

    [Fact]
    public void TheModelsToAsk_AreTheModelThenItsFallbacks_TrimmedAndEachOnce()
    {
        var options = new OpenAiCompatibleOptions { Model = "big", FallbackModels = " small , big,, tiny " };

        Assert.Equal(["big", "small", "tiny"], options.Models);
    }

    [Fact]
    public void WithNoFallbacks_OnlyTheModelIsAsked() =>
        Assert.Equal(["openai/gpt-oss-120b"], new OpenAiCompatibleOptions().Models);

    [Theory]
    [InlineData("llama-3.1-8b-instant")]
    [InlineData("openai/gpt-oss-120b")]
    [InlineData("meta-llama/llama-3.3-70b-instruct:free")]
    [InlineData("gpt-oss-120b")]
    [InlineData("qwen2.5:7b")]
    public void ARealModelName_IsAccepted(string model) => Assert.True(new OpenAiCompatibleOptions { Model = model }.IsValid);

    [Theory]
    [InlineData("")]
    [InlineData("bad model")]
    [InlineData("-leading-dash")]
    [InlineData("name?with=query")]
    public void AModelNameThatCouldBreakTheRequest_IsRefused(string model) => Assert.False(new OpenAiCompatibleOptions { Model = model }.IsValid);

    [Fact]
    public void ABadFallbackModel_IsRefused_NamingTheSetting()
    {
        var options = new OpenAiCompatibleOptions { FallbackModels = "good-one, bad one" };

        Assert.False(options.IsValid);
        Assert.Contains("OpenAiCompatible:FallbackModels", options.Problem);
    }

    [Fact]
    public void ABaseUrlThatIsNotAWebAddress_IsRefused() => Assert.False(new OpenAiCompatibleOptions { BaseUrl = "api.groq.com/openai/v1" }.IsValid);

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void ANumberOfTriesOutsideOneToTen_IsRefused(int tries) => Assert.False(new OpenAiCompatibleOptions { MaxTries = tries }.IsValid);

    [Theory]
    [InlineData(-1)]
    [InlineData(100_001)]
    public void ARequestsPerMinuteOutsideItsRange_IsRefused(int limit) => Assert.False(new OpenAiCompatibleOptions { RequestsPerMinute = limit }.IsValid);

    [Fact]
    public void NoLimitOfOurOwn_IsAllowed_ForAServerYouRun() => Assert.True(new OpenAiCompatibleOptions { RequestsPerMinute = 0 }.IsValid);

    [Fact]
    public void GeminisRequestsPerMinute_HasTheSameRange()
    {
        Assert.True(new GeminiOptions { RequestsPerMinute = 0 }.IsValid);
        Assert.True(new GeminiOptions { RequestsPerMinute = 10 }.IsValid);
        Assert.False(new GeminiOptions { RequestsPerMinute = -1 }.IsValid);
    }
}
