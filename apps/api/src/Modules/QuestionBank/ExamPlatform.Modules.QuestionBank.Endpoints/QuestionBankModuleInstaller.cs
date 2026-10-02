using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.Modules.QuestionBank.Infrastructure.Repositories;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.Modules.QuestionBank.Endpoints;

/// <summary>Registers and maps the QuestionBank module (FR-5).</summary>
public sealed class QuestionBankModuleInstaller : IModuleInstaller
{
    /// <inheritdoc />
    public string ModuleName => "QuestionBank";

    /// <inheritdoc />
    public void AddModule(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<QuestionBankDbContext>((sp, options) => options
            .UseNpgsql(configuration.GetConnectionString("Postgres"))
            .AddInterceptors(sp.GetRequiredService<DomainEventsSaveChangesInterceptor>()));

        services.AddScoped<IQuestionRepository, QuestionRepository>();
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IQuestionBankUnitOfWork, QuestionBankUnitOfWork>();
        services.AddScoped<IQuestionBank, QuestionBankReader>();
        services.AddScoped<IBookCatalog, BookCatalog>();
        // A new sanitizer per request: the library's instance carries mutable configuration.
        services.AddScoped<IRichTextSanitizer, RichTextSanitizer>();

        services.AddScoped<CreateQuestionHandler>();
        services.AddScoped<ListQuestionsHandler>();
        services.AddScoped<GetQuestionHandler>();

        services.AddScoped<CreateBookHandler>();
        services.AddScoped<ChangeBookHandler>();
        services.AddScoped<ListBooksHandler>();
        services.AddScoped<GetBookHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapQuestionBankEndpoints();
        endpoints.MapBookEndpoints();
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<QuestionBankDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
    }
}
