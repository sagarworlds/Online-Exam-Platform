using ExamPlatform.Modules.Admin.Infrastructure;
using ExamPlatform.Modules.Batch.Infrastructure;
using ExamPlatform.Modules.Consent.Infrastructure;
using ExamPlatform.Modules.ExamAuthoring.Infrastructure;
using ExamPlatform.Modules.ExamRuntime.Infrastructure;
using ExamPlatform.Modules.Guardian.Infrastructure;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.Modules.Invite.Infrastructure;
using ExamPlatform.Modules.Proctoring.Infrastructure;
using ExamPlatform.Modules.QuestionBank.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Each module's migrations must describe its model exactly. Migrations here are written by hand, and a snapshot that has drifted from the
/// model makes the API refuse to migrate, and so to start, in production. This says so for the module that drifted, by name, instead of
/// every integration test failing on start-up with the same message.
/// </summary>
public sealed class MigrationsMatchModelTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    public static TheoryData<Type> Contexts =>
    [
        typeof(AdminDbContext),
        typeof(BatchDbContext),
        typeof(ConsentDbContext),
        typeof(ExamAuthoringDbContext),
        typeof(ExamRuntimeDbContext),
        typeof(GuardianDbContext),
        typeof(IdentityDbContext),
        typeof(InviteDbContext),
        typeof(ProctoringDbContext),
        typeof(QuestionBankDbContext),
    ];

    [Theory]
    [MemberData(nameof(Contexts))]
    public void TheMigrationsOfEachModule_DescribeItsModel(Type contextType)
    {
        using var scope = factory.Services.CreateScope();
        var context = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);

        Assert.False(
            context.Database.HasPendingModelChanges(),
            $"{contextType.Name} has model changes no migration describes: its model snapshot has drifted from the model.");
    }
}
