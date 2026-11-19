using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// The ExamRuntime unit of work answers a lost race with a 409 and lets every other unique violation surface (the unit tests make a save
/// fail with a made-up Postgres error). This is the check against a real Postgres: the constraint names the code lists are the ones the
/// migrations created, every unique index of the schema has been classified, and a real violation is treated as classified.
/// </summary>
public sealed class ExamRuntimeRaceGuardsFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    // The unique indexes that are not race guards, each with why. A new unique index must be added here or to ExamRuntimeRaceGuards.
    private static readonly string[] NotRaceGuards =
    [
        // Built once, with the attempt, so a clash means a paper with one question twice. Two racing starts meet at the attempt number first.
        "IX_AttemptQuestions_AttemptId_QuestionId",
    ];

    private async Task<List<string>> UniqueIndexesOfTheSchemaAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ExamRuntimeDbContext>();

        // Primary keys are unique indexes too; they are never race guards, and are left out so this lists only what a migration named.
        return await db.Database.SqlQueryRaw<string>(
            """
            SELECT c.relname AS "Value"
            FROM pg_index i
            JOIN pg_class c ON c.oid = i.indexrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'examRuntime' AND i.indisunique AND NOT i.indisprimary
            ORDER BY c.relname
            """).ToListAsync();
    }

    [Fact]
    public async Task EveryRaceGuardNamed_IsAUniqueIndexOfTheMigratedSchema()
    {
        var inTheDatabase = await UniqueIndexesOfTheSchemaAsync();

        Assert.Empty(ExamRuntimeRaceGuards.IndexNames.Except(inTheDatabase, StringComparer.Ordinal));
    }

    [Fact]
    public async Task EveryUniqueIndexOfTheSchema_IsEitherARaceGuardOrSaidNotToBe()
    {
        var inTheDatabase = await UniqueIndexesOfTheSchemaAsync();

        var unclassified = inTheDatabase.Except(ExamRuntimeRaceGuards.IndexNames, StringComparer.Ordinal).Except(NotRaceGuards, StringComparer.Ordinal);

        Assert.True(
            !unclassified.Any(),
            $"These unique indexes are in neither ExamRuntimeRaceGuards nor NotRaceGuards: {string.Join(", ", unclassified)}. " +
            "Decide whether a clash on each is a lost race (a 409) or a fault.");
        Assert.Empty(NotRaceGuards.Except(inTheDatabase, StringComparer.Ordinal));
    }

    private ExamRuntimeUnitOfWork UnitOfWorkOn(IServiceScope scope, out ExamRuntimeDbContext db)
    {
        db = scope.ServiceProvider.GetRequiredService<ExamRuntimeDbContext>();
        return new ExamRuntimeUnitOfWork(db, new GradingWakeUp());
    }

    [Fact]
    public async Task TwoRecordsOfTheSameMessage_AreALostRace_ThatIsAConcurrencyConflict()
    {
        var subject = Guid.NewGuid();
        var recipient = Guid.NewGuid();
        using var first = factory.Services.CreateScope();
        var firstUnitOfWork = UnitOfWorkOn(first, out var firstDb);
        firstDb.NotificationDeliveries.Add(NotificationDelivery.Start(NotificationKind.ExamReminderOneHour, subject, recipient));
        await firstUnitOfWork.SaveChangesAsync(CancellationToken.None);

        using var second = factory.Services.CreateScope();
        var secondUnitOfWork = UnitOfWorkOn(second, out var secondDb);
        secondDb.NotificationDeliveries.Add(NotificationDelivery.Start(NotificationKind.ExamReminderOneHour, subject, recipient));

        var error = await Assert.ThrowsAsync<ConcurrencyConflictError>(() => secondUnitOfWork.SaveChangesAsync(CancellationToken.None));

        Assert.Equal(409, error.HttpStatusCode);
    }

    [Fact]
    public async Task TheSameIdUsedTwice_IsNotARace_AndSurfacesAsTheDatabaseError()
    {
        using var first = factory.Services.CreateScope();
        var firstUnitOfWork = UnitOfWorkOn(first, out var firstDb);
        var original = NotificationDelivery.Start(NotificationKind.ExamReminderOneHour, Guid.NewGuid(), Guid.NewGuid());
        firstDb.NotificationDeliveries.Add(original);
        await firstUnitOfWork.SaveChangesAsync(CancellationToken.None);

        // A different message (so no race guard fires) under the id already taken: the primary key is what clashes.
        using var second = factory.Services.CreateScope();
        var secondUnitOfWork = UnitOfWorkOn(second, out var secondDb);
        var reused = NotificationDelivery.Start(NotificationKind.ExamReminderOneHour, Guid.NewGuid(), Guid.NewGuid());
        secondDb.NotificationDeliveries.Add(reused);
        secondDb.Entry(reused).Property(nameof(NotificationDelivery.Id)).CurrentValue = original.Id;

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => secondUnitOfWork.SaveChangesAsync(CancellationToken.None));

        var violation = Assert.IsType<Npgsql.PostgresException>(thrown.InnerException);
        Assert.Equal("23505", violation.SqlState);
        Assert.StartsWith("PK_", violation.ConstraintName);
    }
}
