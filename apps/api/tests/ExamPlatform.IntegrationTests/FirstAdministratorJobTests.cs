using ExamPlatform.Modules.Admin.Infrastructure;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using ExamPlatform.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// <c>--create-first-administrator</c> run as a deployment job (ADR 0005): the real Host binary in Production as a separate
/// process, against its own empty database, so these cover the flag, the exit codes, the early return before the web host starts,
/// and what the job prints. The address is personal data, so the output must never hold it.
/// </summary>
public sealed class FirstAdministratorJobTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    private const string AdminEmail = "must.not.be.printed@tests.local";
    private const string CreateFlag = "--create-first-administrator";

    // A mail server that is set but unreachable: the job needs one to be configured, and a refused connection is how a real
    // outage looks. Nothing in these tests needs the e-mail to arrive.
    private static readonly IReadOnlyDictionary<string, string> MailConfigured = new Dictionary<string, string>
    {
        ["Identity__OtpDelivery__Provider"] = "Smtp",
        ["Mail__Provider"] = "Smtp",
        ["Smtp__Host"] = "127.0.0.1",
        ["Smtp__Port"] = "1",
    };

    private static readonly IReadOnlyDictionary<string, string> MailNotConfigured = new Dictionary<string, string>
    {
        ["Identity__OtpDelivery__Provider"] = "Smtp",
        ["Mail__Provider"] = "Smtp",
    };

    [Fact]
    public async Task AfterMigrateAndSeed_CreatesTheSuperAdmin_RecordsIt_AndPrintsNoAddress()
    {
        var connectionString = await MigratedDatabaseAsync();

        var run = await HostJob.RunAsync([CreateFlag, AdminEmail], connectionString, MailConfigured);

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.Contains("Created the first administrator", run.Output);
        Assert.DoesNotContain(AdminEmail, run.Output);
        Assert.DoesNotContain(AdminEmail.Split('@')[0], run.Output);
        // The operator can still tell which address it was: the masked form, first letter and domain. (The host's log
        // redaction would hide a full address anyway, so this is what shows the job masks it itself.)
        Assert.Contains("Created the first administrator m***@tests.local", run.Output);

        await using var identity = IdentityContext(connectionString);
        var admin = await identity.Users.Include(u => u.Roles).SingleAsync();
        Assert.Equal(AdminEmail, admin.Email);
        Assert.Equal(UserStatus.Active, admin.Status);
        Assert.Equal([RbacCatalog.RoleNames.SuperAdmin], admin.Roles.Select(r => r.Name));
        // A hash exists (so the reset link works) and no password was supplied to the job to put in it.
        Assert.NotNull(admin.PasswordHash);

        await using var adminDb = AdminContext(connectionString);
        var audit = await adminDb.AuditLogs.SingleAsync(a => a.Action == "Identity.FirstAdministratorProvisioned");
        Assert.Equal(admin.Id.ToString(), audit.EntityId);
        Assert.DoesNotContain(AdminEmail, string.Join(' ', audit.Metadata.Values));
    }

    [Fact]
    public async Task RunAgain_RefusesWithExitCode2_AndChangesNothing()
    {
        var connectionString = await MigratedDatabaseAsync();
        Assert.Equal(0, (await HostJob.RunAsync([CreateFlag, AdminEmail], connectionString, MailConfigured)).ExitCode);

        var second = await HostJob.RunAsync([CreateFlag, "another.person@tests.local"], connectionString, MailConfigured);

        Assert.Equal(2, second.ExitCode);
        Assert.Contains("already exists", second.Output);
        Assert.DoesNotContain("another.person", second.Output);
        await using var identity = IdentityContext(connectionString);
        Assert.Equal(1, await identity.Users.CountAsync());
    }

    [Fact]
    public async Task WithoutAMailServer_RefusesBeforeCreatingAnything()
    {
        var connectionString = await MigratedDatabaseAsync();

        var run = await HostJob.RunAsync([CreateFlag, AdminEmail], connectionString, MailNotConfigured);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("No mail server is configured", run.Output);
        await using var identity = IdentityContext(connectionString);
        Assert.Equal(0, await identity.Users.CountAsync());
    }

    [Fact]
    public async Task OnADatabaseNobodyMigratedYet_SaysToRunMigrateAndSeedFirst()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var run = await HostJob.RunAsync([CreateFlag, AdminEmail], connectionString, MailConfigured);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--migrate-and-seed", run.Output);
        Assert.DoesNotContain(AdminEmail, run.Output);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--urls")]
    public async Task WithoutAnAddress_PrintsUsage_AndCreatesNothing(string next)
    {
        var connectionString = await MigratedDatabaseAsync();
        IReadOnlyList<string> arguments = next.Length == 0 ? [CreateFlag] : [CreateFlag, next];

        var run = await HostJob.RunAsync(arguments, connectionString, MailConfigured);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Usage", run.Output);
        await using var identity = IdentityContext(connectionString);
        Assert.Equal(0, await identity.Users.CountAsync());
    }

    [Fact]
    public async Task WithSomethingThatIsNotOneAddress_RefusesWithoutRepeatingIt()
    {
        var connectionString = await MigratedDatabaseAsync();

        var run = await HostJob.RunAsync([CreateFlag, "Not An Address"], connectionString, MailConfigured);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("one plain e-mail address", run.Output);
        Assert.DoesNotContain("Not An Address", run.Output);
        await using var identity = IdentityContext(connectionString);
        Assert.Equal(0, await identity.Users.CountAsync());
    }

    private async Task<string> MigratedDatabaseAsync()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var seeded = await HostJob.RunAsync(["--migrate-and-seed"], connectionString);
        Assert.True(seeded.ExitCode == 0, seeded.Output);
        return connectionString;
    }

    private static IdentityDbContext IdentityContext(string connectionString) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(connectionString).Options);

    private static AdminDbContext AdminContext(string connectionString) =>
        new(new DbContextOptionsBuilder<AdminDbContext>().UseNpgsql(connectionString).Options);
}
