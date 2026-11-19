using ExamPlatform.Modules.ExamAuthoring.Application;
using ExamPlatform.Modules.ExamAuthoring.Application.Commands;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Application.Queries;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Infrastructure;
using ExamPlatform.Modules.ExamAuthoring.Infrastructure.Repositories;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamAuthoring.Endpoints;

/// <summary>Registers and maps the ExamAuthoring module (M3: exam creation and management).</summary>
public sealed class ExamAuthoringModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "ExamAuthoring";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ExamAuthoringDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        // An exam's snapshot is kept for a few seconds so every call a candidate makes during an exam does not rebuild it from four queries.
        // The catalog is wrapped to read through the cache and the unit of work to clear it after every save; the lifetime only bounds how
        // long another host can miss a change. Bound as options so a host that adds its settings after registration is honoured.
        services.AddOptions<ExamSnapshotCacheOptions>()
            .Bind(configuration.GetSection(ExamSnapshotCacheOptions.SectionName))
            .Validate(options => options.IsValid, "ExamAuthoring:SnapshotCache needs LifetimeSeconds from 0 to 3600 and MaxEntries of 1 or more.")
            .ValidateOnStart();
        services.AddSingleton<IExamSnapshotCache>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<ExamSnapshotCacheOptions>>().Value;
            return new ExamSnapshotCache(sp.GetRequiredService<Clock>(), TimeSpan.FromSeconds(options.LifetimeSeconds), options.MaxEntries);
        });
        services.AddScoped<IExamRepository, EFExamRepository>();
        services.AddScoped<ExamAuthoringUnitOfWork>();
        services.AddScoped<IExamAuthoringUnitOfWork>(sp => new SnapshotInvalidatingUnitOfWork(
            sp.GetRequiredService<ExamAuthoringUnitOfWork>(), sp.GetRequiredService<IExamSnapshotCache>()));
        services.AddScoped<ExamCatalog>();
        services.AddScoped<IExamCatalog>(sp => new CachedExamCatalog(sp.GetRequiredService<ExamCatalog>(), sp.GetRequiredService<IExamSnapshotCache>()));
        services.AddScoped<IQuestionUsageSource, ExamQuestionUsageSource>();
        services.AddScoped<IQuestionPlacementGuard, ExamScopePlacementGuard>();

        services.AddScoped<ExamScopeResolver>();
        services.AddScoped<ExamDtoFactory>();

        services.AddScoped<CreateExamHandler>();
        services.AddDomainEventHandlers(typeof(ExamAuditTrail).Assembly);
        services.AddScoped<ListExamsHandler>();
        services.AddScoped<GetExamHandler>();
        services.AddScoped<ScheduleExamHandler>();
        services.AddScoped<AddSectionHandler>();
        services.AddScoped<AddExamQuestionHandler>();
        services.AddScoped<AddExamQuestionsHandler>();
        services.AddScoped<RemoveExamQuestionHandler>();
        services.AddScoped<DrawExamQuestionsHandler>();
        services.AddScoped<AddDrawRuleHandler>();
        services.AddScoped<RemoveDrawRuleHandler>();
        services.AddScoped<DrawPoolChecker>();
        services.TryAddSingleton<IQuestionPicker, RandomQuestionPicker>();
        services.AddScoped<RemoveSectionHandler>();
        services.AddScoped<ReorderSectionsHandler>();
        services.AddScoped<ReorderQuestionsHandler>();
        services.AddScoped<UpdateExamDetailsHandler>();
        services.AddScoped<DeleteExamHandler>();
        services.AddScoped<EditSectionHandler>();
        services.AddScoped<PublishExamHandler>();
        services.AddScoped<SetExamScopeHandler>();
        services.AddScoped<SetResultReleaseHandler>();
        services.AddScoped<SetMarkingSchemeHandler>();
        services.AddScoped<SetMaxAttemptsHandler>();
        services.AddScoped<SetContentProtectionHandler>();
        services.AddScoped<SetFocusViolationLimitHandler>();
        services.AddScoped<SetFocusWarnAtHandler>();
        services.AddScoped<SetPassingPercentageHandler>();
        services.AddScoped<ClearPassingPercentageHandler>();
        services.AddScoped<SetProctoringProfileHandler>();
        services.AddSingleton<ListProctoringProfilesHandler>();
        services.AddScoped<SetShuffleHandler>();
        services.AddScoped<ReleaseResultsHandler>();

        services.AddScoped<IInstructionTemplateRepository, EFInstructionTemplateRepository>();
        services.AddScoped<InstructionAudit>();
        services.AddScoped<ListInstructionTemplatesHandler>();
        services.AddScoped<CreateInstructionTemplateHandler>();
        services.AddScoped<UpdateInstructionTemplateHandler>();
        services.AddScoped<DeleteInstructionTemplateHandler>();
        services.AddScoped<SetExamInstructionsHandler>();
        services.AddScoped<UseInstructionTemplateHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapExamAuthoringEndpoints();
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<ExamAuthoringDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
