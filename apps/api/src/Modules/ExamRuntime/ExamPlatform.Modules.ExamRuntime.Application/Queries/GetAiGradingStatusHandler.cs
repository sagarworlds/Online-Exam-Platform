using ExamPlatform.Modules.ExamRuntime.Application.Ports;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Where judging typed answers with an AI provider stands, for the staff who run exams.</summary>
/// <param name="Enabled">Whether at least one provider is set up. When none is, typed answers are marked by the accepted answers alone and nothing waits.</param>
/// <param name="Providers">Each set-up provider, in the order it is asked, with where it stands and the last thing that went wrong.</param>
/// <param name="WaitingAnswers">How many typed answers are queued for a verdict, whether being asked about now or waiting for a provider to answer.</param>
public sealed record AiGradingStatusDto(bool Enabled, IReadOnlyList<ProviderStatus> Providers, int WaitingAnswers);

/// <summary>
/// Answers "why have my candidates' scores not come?": whether the AI providers are working, paused or out of quota, with each one's own reason, and
/// how many answers wait. It holds no question, accepted answer or typed answer.
/// </summary>
public sealed class GetAiGradingStatusHandler(IEvaluatorHealth health, IAttemptRepository attempts)
{
    /// <summary>Reads the status.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<AiGradingStatusDto> HandleAsync(CancellationToken cancellationToken)
    {
        var status = health.Status;
        return new AiGradingStatusDto(status.Enabled, status.Providers, await attempts.CountAnswersAwaitingEvaluationAsync(cancellationToken));
    }
}
