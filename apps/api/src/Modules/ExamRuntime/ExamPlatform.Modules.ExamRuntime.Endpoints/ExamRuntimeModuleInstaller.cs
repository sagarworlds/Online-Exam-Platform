using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.ExamRuntime.Endpoints.Notifications;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
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
using Microsoft.Extensions.Options;

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

        // Answers to attempt requests go out through the platform's mail sender when a mail server is configured and are otherwise
        // not sent; the administrator is told so in the response.
        services.AddScoped<IAttemptRequestNotifier, SmtpAttemptRequestNotifier>();
        services.AddScoped<IExamRuntimeUnitOfWork, ExamRuntimeUnitOfWork>();
        services.AddScoped<IQuestionUsageSource, AnsweredQuestionUsageSource>();
        services.AddScoped<IQuestionStatisticsSource, AttemptAnswerStatisticsSource>();
        services.AddScoped<IAttemptRescorer, AttemptRescorer>();
        services.AddScoped<IAttemptSignalSource, AttemptSignalSource>();

        services.AddScoped<AttemptCloser>();
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
        services.AddScoped<RecordFocusViolationHandler>();
        services.AddScoped<StaffAttemptAccess>();
        services.AddScoped<WarnAttemptHandler>();
        services.AddScoped<RescoreAttemptHandler>();
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
