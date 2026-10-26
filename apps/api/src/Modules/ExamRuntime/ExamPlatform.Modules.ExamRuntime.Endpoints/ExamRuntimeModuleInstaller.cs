using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Endpoints.Grading;
using ExamPlatform.Modules.ExamRuntime.Endpoints.Notifications;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.Email;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ExamPlatform.Modules.ExamRuntime.Application.Retention;
using ExamPlatform.Modules.ExamRuntime.Endpoints.Retention;

namespace ExamPlatform.Modules.ExamRuntime.Endpoints;

/// <summary>Registers and maps the ExamRuntime module: what a candidate sees and does with the exams they are invited to.</summary>
public sealed class ExamRuntimeModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "ExamRuntime";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ExamRuntimeDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        // What administrators do to an attempt is audited from the events the attempt raises (FR-29, FR-40).
        services.AddDomainEventHandlers(typeof(AttemptAuditTrail).Assembly);

        services.AddScoped<IAttemptRepository, AttemptRepository>();
        services.AddScoped<IExtraAttemptGrantRepository, ExtraAttemptGrantRepository>();
        services.AddScoped<IAccommodationRepository, AccommodationRepository>();
        services.AddScoped<IAttemptRequestRepository, AttemptRequestRepository>();
        services.AddScoped<IDisputeRepository, DisputeRepository>();
        services.AddScoped<IIssueReportRepository, IssueReportRepository>();
        services.AddScoped<IWebcamPhotoRepository, WebcamPhotoRepository>();
        services.AddScoped<WebcamEligibility>();

        // Webcam photos and screen pictures are deleted after a set number of days (FR-47). Validated when the host starts, so a mistyped
        // period stops the application starting instead of silently keeping, or dropping, images.
        services.AddOptions<CaptureRetentionOptions>()
            .Bind(configuration.GetSection(CaptureRetentionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<PurgeOldCapturesHandler>();
        services.AddHostedService<CaptureRetentionBackgroundService>();

        // How long after a result is released a candidate may dispute its answer key; 0 switches disputes off (FR-31). Validated when the
        // host starts, so a mistyped window stops the application starting instead of failing the first candidate who disputes. It is
        // bound as options, not read from configuration here: a test's WebApplicationFactory layers its configuration on after AddModule
        // runs, so a value captured now could be stale.
        services.AddOptions<DisputeOptions>()
            .Bind(configuration.GetSection(DisputeOptions.SectionName))
            .Validate(
                options => options.WindowDays is null or (>= 0 and <= DisputePolicy.MaxWindowDays),
                $"{DisputeOptions.SectionName}:WindowDays must be between 0 and {DisputePolicy.MaxWindowDays}.")
            .ValidateOnStart();
        services.AddSingleton(sp => DisputePolicy.From(sp.GetRequiredService<IOptions<DisputeOptions>>().Value.WindowDays));

        // Whether time per question is kept for candidates under 18 (FR-36, section 7). Off unless the environment turns it on; bound as options
        // for the same reason as the dispute window above, so a test's configuration layered on after AddModule still takes effect.
        services.AddOptions<TimePerQuestionOptions>()
            .Bind(configuration.GetSection(TimePerQuestionOptions.SectionName));
        services.AddSingleton(sp => new QuestionTimePolicy(sp.GetRequiredService<IOptions<TimePerQuestionOptions>>().Value.MinorsEnabled));
        services.AddScoped<QuestionTimeWithholding>();

        // Answers to attempt requests go out through the platform's mail sender when a mail server is configured and are otherwise
        // not sent; the administrator is told so in the response.
        services.AddScoped<IAttemptRequestNotifier, SmtpAttemptRequestNotifier>();
        services.AddScoped<IExamRuntimeUnitOfWork, ExamRuntimeUnitOfWork>();
        services.AddScoped<IQuestionUsageSource, AnsweredQuestionUsageSource>();
        services.AddScoped<IQuestionStatisticsSource, AttemptAnswerStatisticsSource>();
        services.AddScoped<IAttemptRescorer, AttemptRescorer>();
        services.AddScoped<IAttemptSignalSource, AttemptSignalSource>();

        services.AddScoped<AttemptCloser>();
        // Stateless: the same rules for every candidate, so one instance serves the whole process.
        services.TryAddSingleton<IProctorPolicy, ProctorPolicy>();
        services.AddScoped<AttemptViewBuilder>();
        services.AddScoped<AttemptReviewBuilder>();
        services.AddScoped<AttemptAccess>();
        services.AddScoped<AttemptRequestDtoFactory>();
        services.AddScoped<DisputeDtoFactory>();
        services.AddScoped<IssueReportDtoFactory>();

        services.AddScoped<MyExamsHandler>();
        services.AddScoped<PaperDrawer>();
        services.TryAddSingleton<IQuestionPicker, RandomQuestionPicker>();
        services.AddScoped<StartAttemptHandler>();
        services.AddScoped<GetAttemptHandler>();
        services.AddScoped<GetQuestionPictureHandler>();
        services.AddScoped<GetAttemptReviewHandler>();
        services.AddScoped<GetAttemptResultHandler>();
        services.AddScoped<GetCertificateHandler>();
        services.AddScoped<SubjectMarks>();
        services.AddScoped<GetLeaderboardHandler>();
        services.AddScoped<SaveAnswerHandler>();
        services.AddScoped<ClearAnswerHandler>();
        services.AddScoped<MarkQuestionHandler>();
        services.AddScoped<RecordQuestionTimeHandler>();
        services.AddScoped<RecordFocusViolationHandler>();
        services.AddScoped<GetWebcamPolicyHandler>();
        services.AddScoped<RecordWebcamPhotoHandler>();
        services.AddScoped<ListWebcamPhotosHandler>();
        services.AddScoped<GetWebcamPhotoHandler>();
        services.AddScoped<StaffAttemptAccess>();
        services.AddScoped<WarnAttemptHandler>();
        services.AddScoped<RescoreAttemptHandler>();
        services.AddScoped<RequestAiCheckHandler>();
        services.AddScoped<GetAiGradingStatusHandler>();
        services.AddScoped<PauseAttemptHandler>();
        services.AddScoped<ResumeAttemptHandler>();
        services.AddScoped<TerminateAttemptHandler>();
        services.AddScoped<InvalidateAttemptHandler>();
        services.AddScoped<GetAttemptStatusHandler>();
        services.AddScoped<PreviewExamHandler>();
        services.AddScoped<ListAttemptClientsHandler>();
        services.AddScoped<MoveToSectionHandler>();
        services.AddScoped<SubmitAttemptHandler>();
        services.AddScoped<GrantExtraAttemptHandler>();
        services.AddScoped<SetAccommodationHandler>();
        services.AddScoped<RemoveAccommodationHandler>();
        services.AddScoped<ListExamAttemptsHandler>();
        services.AddScoped<GetAttemptPaperHandler>();
        services.AddScoped<RequestAttemptHandler>();
        services.AddScoped<ListAttemptRequestsHandler>();
        services.AddScoped<ApproveAttemptRequestHandler>();
        services.AddScoped<DeclineAttemptRequestHandler>();
        services.AddScoped<RaiseDisputeHandler>();
        services.AddScoped<ListDisputesHandler>();
        services.AddScoped<RejectDisputeHandler>();
        // The platform's scheduled e-mails (FR-39): reminders, released results and revised scores. A pass is started by a timer in the
        // host and by a key-protected route for an outside scheduler (a host that sleeps misses its own timer); both are safe together.
        services.AddOptions<NotificationOptions>()
            .Bind(configuration.GetSection(NotificationOptions.SectionName))
            .Validate(
                options => options.PollMinutes is >= 1 and <= NotificationOptions.MaxPollMinutes,
                $"{NotificationOptions.SectionName}:PollMinutes must be between 1 and {NotificationOptions.MaxPollMinutes}.")
            .Validate(
                options => string.IsNullOrEmpty(options.RunKey) || options.RunKey.Length >= NotificationOptions.MinRunKeyLength,
                $"{NotificationOptions.SectionName}:RunKey must be at least {NotificationOptions.MinRunKeyLength} characters, or empty to turn the route off.")
            .ValidateOnStart();
        services.AddScoped<INotificationDeliveryRepository, NotificationDeliveryRepository>();
        services.AddScoped<INotificationQueries, NotificationQueries>();
        services.AddScoped<IExamNotificationMailer, SmtpExamNotifier>();
        services.AddScoped<NotificationRun>();
        services.AddSingleton<NotificationRunner>();
        services.AddHostedService<NotificationBackgroundService>();

        // Typed answers that match no accepted answer under the question's matching rule are judged by Google's Gemini service (FR-5), but only when Gemini:ApiKey is
        // set: with no key the evaluator reports itself off, nothing waits, and typed answers are marked by the accepted answers alone. Bound as
        // options and validated when the host starts, so a mistyped model or address stops it starting instead of failing the first answer.
        services.AddOptions<GeminiOptions>()
            .Bind(configuration.GetSection(GeminiOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GeminiOptions>, GeminiOptionsValidator>();
        // The same for any service that speaks the OpenAI chat-completions protocol (Groq by default), judged the same way; with no key it is off.
        services.AddOptions<OpenAiCompatibleOptions>()
            .Bind(configuration.GetSection(OpenAiCompatibleOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<OpenAiCompatibleOptions>, OpenAiCompatibleOptionsValidator>();

        // Each provider's own time limit per request is the evaluator's; the client's default of 100 seconds would cut a longer one short. The
        // clients live as long as the host, so their connections are renewed now and then to follow a change of address.
        foreach (var client in new[] { nameof(GeminiTextAnswerEvaluator), nameof(OpenAiCompatibleTextAnswerEvaluator) })
        {
            services.AddHttpClient(client, http => http.Timeout = Timeout.InfiniteTimeSpan)
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        }

        // One chain for the whole host, so that the pacing and the pause of each provider are shared by every worker: the OpenAI-compatible models
        // first (each model has its own allowance, so each has its own gate), then Gemini. A provider with no key is skipped.
        services.AddSingleton(sp =>
        {
            var clients = sp.GetRequiredService<IHttpClientFactory>();
            var loggers = sp.GetRequiredService<ILoggerFactory>();
            var clock = sp.GetRequiredService<Clock>();
            var providers = new List<IGatedTextAnswerEvaluator>();

            var openAi = sp.GetRequiredService<IOptions<OpenAiCompatibleOptions>>();
            var openAiLogger = loggers.CreateLogger<OpenAiCompatibleTextAnswerEvaluator>();
            foreach (var model in openAi.Value.Models)
            {
                providers.Add(new OpenAiCompatibleTextAnswerEvaluator(
                    clients.CreateClient(nameof(OpenAiCompatibleTextAnswerEvaluator)), openAi, model, openAiLogger,
                    gate: new EvaluatorGate(openAi.Value.ProviderName, model, openAi.Value.RequestsPerMinute, clock, openAiLogger)));
            }

            var gemini = sp.GetRequiredService<IOptions<GeminiOptions>>();
            var geminiLogger = loggers.CreateLogger<GeminiTextAnswerEvaluator>();
            providers.Add(new GeminiTextAnswerEvaluator(
                clients.CreateClient(nameof(GeminiTextAnswerEvaluator)), gemini, geminiLogger,
                gate: new EvaluatorGate("Gemini", gemini.Value.ModelId, gemini.Value.RequestsPerMinute, clock, geminiLogger)));

            return new FallbackTextAnswerEvaluator(providers, loggers.CreateLogger<FallbackTextAnswerEvaluator>());
        });
        services.AddSingleton<ITextAnswerEvaluator>(sp => sp.GetRequiredService<FallbackTextAnswerEvaluator>());
        services.AddSingleton<IEvaluatorHealth>(sp => sp.GetRequiredService<FallbackTextAnswerEvaluator>());

        // An attempt with such an answer waits in grading, with no score, until a worker has the verdicts; the host's timer runs the worker.
        services.AddOptions<AiGradingOptions>()
            .Bind(configuration.GetSection(AiGradingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddScoped<TypedAnswerGrading>();
        services.AddSingleton<TypedAnswerGradingRunner>();
        services.AddHostedService<TypedAnswerGradingBackgroundService>();

        services.AddScoped<ReportIssueHandler>();
        services.AddScoped<ListIssueReportsHandler>();
        services.AddScoped<ResolveIssueReportHandler>();

        // Other modules read released results only through this contract (ADR 0001), e.g. candidate analytics (FR-36).
        services.AddScoped<ICandidateResultReader, CandidateResultReader>();

        // How an exam's candidates answered it, for item analysis (FR-37): released results only, marked the same way as the results.
        services.AddScoped<IExamResponseReader, ExamResponseReader>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapExamRuntimeEndpoints();
        endpoints.MapNotificationEndpoints();
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ExamRuntimeDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
