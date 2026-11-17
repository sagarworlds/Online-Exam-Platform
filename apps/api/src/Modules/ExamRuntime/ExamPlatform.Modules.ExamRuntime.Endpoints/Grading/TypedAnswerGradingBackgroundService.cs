using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints.Grading;

/// <summary>
/// The host's worker for judging typed answers with the AI evaluator. It looks at the queue when it is woken (typed answers were queued: a
/// submission, an answer-key correction, a check staff asked for), once after start-up for anything left from before a restart, and when something
/// it is waiting for falls due (a retry, the end of a provider's pause, the give-up deadline). With nothing queued it sleeps and does not touch
/// the database, which lets a free Neon database suspend. A pass that fails is logged and tried again a minute later.
/// </summary>
public sealed class TypedAnswerGradingBackgroundService(
    TypedAnswerGradingRunner runner,
    GradingWakeUp wakeUp,
    IOptions<AiGradingOptions> options,
    ILogger<TypedAnswerGradingBackgroundService> logger) : BackgroundService
{
    // Long enough for start-up (migrations run before the host starts, but the database may still be waking) to be over.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);

    // Between a pass that failed (the database or the evaluator unreachable) and the next try; long enough not to hammer a database that is down.
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(1);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("The AI grading worker is off ({Section}:Enabled).", AiGradingOptions.SectionName);
            return;
        }

        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            if (!await runner.IsEvaluatorOnAsync(stoppingToken))
                logger.LogInformation("Typed answers are marked by the accepted answers alone: no AI evaluator is set up (Gemini:ApiKey).");

            // The first pass is the start-up look; each later one follows a nudge or the time something fell due.
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan? sleep;
                try
                {
                    await runner.RunOnceAsync(stoppingToken);
                    sleep = await runner.NextWakeAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "An AI grading pass failed; the next one will try again in a minute.");
                    sleep = RetryAfterFailure;
                }

                await wakeUp.WaitAsync(sleep, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }
}
