using ExamPlatform.Modules.ExamRuntime.Application;
using ExamPlatform.Modules.ExamRuntime.Application.Commands;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.Email;
using ExamPlatform.Modules.ExamRuntime.Infrastructure.Repositories;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        // How long after a result is released a candidate may dispute its answer key; 0 switches disputes off (FR-31). Read now, not on
        // first use, so a mistyped window stops the application starting instead of failing the first candidate who disputes.
        services.AddSingleton(DisputePolicy.From(configuration.GetValue<int?>("ExamRuntime:Disputes:WindowDays")));

        // Answers to attempt requests go out through the platform's mail sender when a mail server is configured and are otherwise
        // not sent; the administrator is told so in the response.
        services.AddScoped<IAttemptRequestNotifier, SmtpAttemptRequestNotifier>();
        services.AddScoped<IExamRuntimeUnitOfWork, ExamRuntimeUnitOfWork>();
        services.AddScoped<IQuestionUsageSource, AnsweredQuestionUsageSource>();
        services.AddScoped<IQuestionStatisticsSource, AttemptAnswerStatisticsSource>();
        services.AddScoped<IAttemptRescorer, AttemptRescorer>();

        services.AddScoped<AttemptCloser>();
        services.AddScoped<AttemptViewBuilder>();
        services.AddScoped<AttemptReviewBuilder>();
        services.AddScoped<AttemptAccess>();
        services.AddScoped<AttemptRequestDtoFactory>();
        services.AddScoped<DisputeDtoFactory>();

        services.AddScoped<MyExamsHandler>();
        services.AddScoped<PaperDrawer>();
        services.TryAddSingleton<IQuestionPicker, RandomQuestionPicker>();
        services.AddScoped<StartAttemptHandler>();
        services.AddScoped<GetAttemptHandler>();
        services.AddScoped<GetAttemptReviewHandler>();
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
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints) => endpoints.MapExamRuntimeEndpoints();

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ExamRuntimeDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
