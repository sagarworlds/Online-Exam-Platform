using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints.Grading;

/// <summary>
/// The host's timer for judging typed answers with the AI evaluator: a look at the queue every <see cref="AiGradingOptions.PollSeconds"/> seconds
/// while the host is awake. A pass that fails is logged and the timer carries on; whatever is still waiting is taken at the next one.
/// </summary>
public sealed class TypedAnswerGradingBackgroundService(
    TypedAnswerGradingRunner runner, IOptions<AiGradingOptions> options, ILogger<TypedAnswerGradingBackgroundService> logger) : BackgroundService
{
    // Long enough for start-up (migrations run before the host starts, but the database may still be waking) to be over.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("The AI grading timer is off ({Section}:Enabled).", AiGradingOptions.SectionName);
            return;
        }

        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            if (!await runner.IsEvaluatorOnAsync(stoppingToken))
                logger.LogInformation("Typed answers are marked by the accepted answers alone: no AI evaluator is set up (Gemini:ApiKey).");

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(settings.PollSeconds));
            do
            {
                try
                {
                    await runner.RunOnceAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogError(exception, "An AI grading pass failed; the next one will try again.");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }
}
