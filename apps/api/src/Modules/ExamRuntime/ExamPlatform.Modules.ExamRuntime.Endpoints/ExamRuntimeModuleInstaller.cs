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

        services.AddScoped<IAttemptRepository, AttemptRepository>();
        services.AddScoped<IExtraAttemptGrantRepository, ExtraAttemptGrantRepository>();
        services.AddScoped<IAttemptRequestRepository, AttemptRequestRepository>();

        // Answers to attempt requests go out through the platform's mail sender when a mail server is configured and are otherwise
        // not sent; the administrator is told so in the response.
        services.AddScoped<IAttemptRequestNotifier, SmtpAttemptRequestNotifier>();
        services.AddScoped<IExamRuntimeUnitOfWork, ExamRuntimeUnitOfWork>();
        services.AddScoped<IQuestionUsageSource, AnsweredQuestionUsageSource>();
        services.AddScoped<IAttemptRescorer, AttemptRescorer>();

        services.AddScoped<AttemptCloser>();
        services.AddScoped<AttemptViewBuilder>();
        services.AddScoped<AttemptReviewBuilder>();
        services.AddScoped<AttemptAccess>();
        services.AddScoped<AttemptRequestDtoFactory>();

        services.AddScoped<MyExamsHandler>();
        services.AddScoped<PaperDrawer>();
        services.TryAddSingleton<IQuestionPicker, RandomQuestionPicker>();
        services.AddScoped<StartAttemptHandler>();
        services.AddScoped<GetAttemptHandler>();
        services.AddScoped<GetAttemptReviewHandler>();
        services.AddScoped<SaveAnswerHandler>();
        services.AddScoped<ClearAnswerHandler>();
        services.AddScoped<MarkQuestionHandler>();
        services.AddScoped<MoveToSectionHandler>();
        services.AddScoped<SubmitAttemptHandler>();
        services.AddScoped<GrantExtraAttemptHandler>();
        services.AddScoped<ListExamAttemptsHandler>();
        services.AddScoped<GetAttemptPaperHandler>();
        services.AddScoped<RequestAttemptHandler>();
        services.AddScoped<ListAttemptRequestsHandler>();
        services.AddScoped<ApproveAttemptRequestHandler>();
        services.AddScoped<DeclineAttemptRequestHandler>();
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
