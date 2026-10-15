using ExamPlatform.Modules.QuestionBank.Application;
using ExamPlatform.Modules.QuestionBank.Application.Commands;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Application.Queries;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using ExamPlatform.Modules.QuestionBank.Infrastructure.Encryption;
using ExamPlatform.Modules.QuestionBank.Infrastructure.Repositories;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure;
using ExamPlatform.SharedKernel.Infrastructure.KeyRing;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // Question content is encrypted at rest (NFR-5, #57). The key ring is kept in Postgres, so every replica decrypts with the same keys;
        // the application name keeps the ring the same across deployments that share the database. Each entry of the ring is itself encrypted
        // with a key held outside the database (NFR-5, #86), so a copy of the database alone does not open the content. The wrapping is applied
        // after the repository is chosen, and the key is asked for only when the ring is first used (see ProtectedXmlRepository).
        services.AddDataProtection()
            .SetApplicationName("ExamPlatform")
            .PersistKeysToDbContext<QuestionBankDbContext>()
            .ProtectKeyRing();
        services.AddSingleton<QuestionContentCipher>();
        services.AddScoped<QuestionContentBackfill>();
        // The protector is passed as a factory, so a run on an empty key ring never asks for the key (see KeyRingReprotection).
        services.AddScoped(provider => new KeyRingReprotection(
            provider.GetRequiredService<QuestionBankDbContext>(),
            () => provider.GetRequiredService<IKeyRingProtector>(),
            provider.GetRequiredService<ILogger<KeyRingReprotection>>()));
        services.AddScoped<IQuestionContentEncryptionStatus, QuestionContentEncryptionStatus>();
        services.AddScoped<ReadContentEncryptionStatusHandler>();

        services.AddScoped<IQuestionRepository, QuestionRepository>();
        services.AddScoped<IBookRepository, BookRepository>();
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IQuestionBankUnitOfWork, QuestionBankUnitOfWork>();
        // Whether an exam may only hold approved questions (FR-8). Off unless the deployment asks for it.
        // Read when first needed, not now, so a host that adds its settings after registration (as the tests do) is honoured.
        services.AddSingleton(sp => new QuestionApprovalPolicy(sp.GetRequiredService<IConfiguration>().GetValue<bool>("QuestionBank:RequireApproval")));
        services.AddScoped<IQuestionBank, QuestionBankReader>();
        services.AddScoped<IQuestionSubjects, QuestionSubjects>();
        services.AddScoped<IQuestionTopics, QuestionTopics>();
        services.AddScoped<IBookCatalog, BookCatalog>();
        // Combines the IQuestionUsageSource each module that uses questions registers for itself.
        services.AddScoped<QuestionUsageReader>();
        services.AddScoped<QuestionDtoFactory>();
        services.AddScoped<QuestionDuplicateFinder>();
        services.AddSingleton(sp => new QuestionDuplicatePolicy(sp.GetRequiredService<IConfiguration>().GetValue("QuestionBank:RefuseDuplicates", true)));
        services.AddScoped<OpenChapterResolver>();
        services.AddScoped<OpenClassResolver>();
        // A new sanitizer per request: the library's instance carries mutable configuration.
        services.AddScoped<IRichTextSanitizer, RichTextSanitizer>();

        services.AddScoped<CreateQuestionHandler>();
        services.AddScoped<EditQuestionHandler>();
        services.AddScoped<CorrectAnswerKeyHandler>();
        services.AddScoped<DeleteQuestionHandler>();
        services.AddScoped<FileQuestionsHandler>();
        services.AddScoped<ListQuestionsHandler>();
        services.AddScoped<GetQuestionHandler>();
        services.AddScoped<GetQuestionHistoryHandler>();
        services.AddScoped<GetQuestionReviewLogHandler>();
        services.AddScoped<QuestionReviewHandler>();
        services.AddScoped<ListTopicsHandler>();
        services.AddScoped<FindDuplicatesHandler>();
        services.AddScoped<AddTranslationHandler>();
        services.AddScoped<ListTranslationsHandler>();
        services.AddScoped<GetQuestionStatisticsHandler>();
        services.AddScoped<ImportQuestionsHandler>();
        services.AddScoped<ExportQuestionsHandler>();

        services.AddScoped<CreateBookHandler>();
        services.AddScoped<ChangeBookHandler>();
        services.AddScoped<ListBooksHandler>();
        services.AddScoped<GetBookHandler>();

        services.AddScoped<CreateClassHandler>();
        services.AddScoped<ChangeClassHandler>();
        services.AddScoped<ListClassesHandler>();
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapQuestionBankEndpoints();
        endpoints.MapBookEndpoints();
        endpoints.MapClassEndpoints();
    }

    /// <inheritdoc />
    public async Task MigrateAndSeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<QuestionBankDbContext>();
        await db.Database.MigrateAsync(cancellationToken);

        // First, so that the backfill below, which is the first thing to use the keys, finds a protected ring, and so that a ring stored in the
        // clear before #86 does not stay that way. It is idempotent, and a database with no key ring entries asks for no key (ADR 0003).
        await services.GetRequiredService<KeyRingReprotection>().RunAsync(cancellationToken);

        // Before any replica serves questions: the encryption backfill turns plaintext content that predates #57 into ciphertext, so a
        // question is never read as plaintext. It is idempotent, so a run on an already encrypted bank changes nothing.
        await services.GetRequiredService<QuestionContentBackfill>().RunAsync(cancellationToken);
    }
}
