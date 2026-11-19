using ExamPlatform.Modules.ExamRuntime.Application.Grading;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using ExamPlatform.SharedKernel.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>
/// Which database failures the ExamRuntime unit of work answers with a 409. Only a unique index that exists to settle a race may: a unique
/// violation anywhere else is a fault, and telling the client to try again would hide it. The save is made to fail with a Postgres error
/// naming a constraint, so no database is needed; <c>ExamRuntimeRaceGuardsFlowTests</c> checks the names against a real one.
/// </summary>
public class ExamRuntimeUnitOfWorkTests
{
    private const string UniqueViolation = "23505";

    /// <summary>Fails the save with the exception it is given, as the database would, before anything is sent.</summary>
    private sealed class FailingSave(Exception failure) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw failure;
    }

    private static DbUpdateException Failure(string sqlState, string? constraintName) =>
        new("The save failed.", new PostgresException("The database refused the change.", "ERROR", "ERROR", sqlState, constraintName: constraintName));

    private static Task<int> SaveFailingWithAsync(Exception failure)
    {
        var options = new DbContextOptionsBuilder<ExamRuntimeDbContext>().UseNpgsql("Host=unused").AddInterceptors(new FailingSave(failure)).Options;
        var context = new ExamRuntimeDbContext(options);
        // Something to save, so the save reaches the interceptor.
        context.NotificationDeliveries.Add(NotificationDelivery.Start(NotificationKind.ExamReminderOneHour, Guid.NewGuid(), Guid.NewGuid()));

        return new ExamRuntimeUnitOfWork(context, new GradingWakeUp()).SaveChangesAsync(CancellationToken.None);
    }

    public static TheoryData<string> RaceGuards() => [.. ExamRuntimeRaceGuards.IndexNames];

    [Theory]
    [MemberData(nameof(RaceGuards))]
    public async Task ALostRace_OnAnyOfTheRaceGuardIndexes_IsAConcurrencyConflict(string index)
    {
        var failure = Failure(UniqueViolation, index);

        var error = await Assert.ThrowsAsync<ConcurrencyConflictError>(() => SaveFailingWithAsync(failure));

        Assert.Equal(409, error.HttpStatusCode);
        Assert.Same(failure, error.InnerException);
    }

    [Theory]
    [InlineData("PK_Attempts")]                                      // a primary key: an id used twice is a bug, not a race
    [InlineData("PK_ExtraAttemptGrants")]
    [InlineData("IX_AttemptQuestions_AttemptId_QuestionId")]         // a paper holding the same question twice is a bug, not a race
    [InlineData("IX_SomeFutureUnique_A_B")]                          // a unique index nobody has classified yet is not assumed to be one
    [InlineData("ix_attempts_examid_candidateid_number")]            // names are matched exactly, as Postgres reports them
    [InlineData(null)]                                               // no constraint named: nothing to say it was a race
    public async Task AUniqueViolationOnAnyOtherConstraint_SurfacesUnchanged(string? constraint)
    {
        var failure = Failure(UniqueViolation, constraint);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => SaveFailingWithAsync(failure));

        Assert.Same(failure, thrown);
    }

    [Theory]
    [InlineData("23503", "IX_Attempts_ExamId_CandidateId_Number")]  // not a unique violation, whatever it names
    [InlineData("23502", null)]
    public async Task ADifferentKindOfDatabaseError_SurfacesUnchanged_EvenOnARaceGuardsName(string sqlState, string? constraint)
    {
        var failure = Failure(sqlState, constraint);

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => SaveFailingWithAsync(failure));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task AnUpdateThatFailedForAnotherReason_SurfacesUnchanged()
    {
        var failure = new DbUpdateException("The save failed.", new InvalidOperationException("not a database error"));

        var thrown = await Assert.ThrowsAsync<DbUpdateException>(() => SaveFailingWithAsync(failure));

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task ARowChangedByAnotherRequest_IsStillAConcurrencyConflict()
    {
        var failure = new DbUpdateConcurrencyException("The row changed.");

        await Assert.ThrowsAsync<ConcurrencyConflictError>(() => SaveFailingWithAsync(failure));
    }

    [Fact]
    public void TheRaceGuards_AreTenDistinctNames()
    {
        Assert.Equal(10, ExamRuntimeRaceGuards.IndexNames.Distinct(StringComparer.Ordinal).Count());
    }
}
