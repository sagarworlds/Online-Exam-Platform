using ExamPlatform.Modules.Identity.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// <c>--create-load-test-candidates</c> and <c>--enrol-load-test-candidates</c> run as deployment jobs (ADR 0006): the real Host binary
/// in Production as a separate process, so these cover the flags, the guard that keeps them from running anywhere by accident, the exit
/// codes, and what the jobs print. The shared password must never appear in the output.
/// </summary>
public sealed class LoadTestPoolJobTests(PostgresServerFixture postgres) : IClassFixture<PostgresServerFixture>
{
    private const string Password = "a long shared load test passphrase 2026";
    private const string CreateFlag = "--create-load-test-candidates";
    private const string EnrolFlag = "--enrol-load-test-candidates";

    private static readonly IReadOnlyDictionary<string, string> Enabled = new Dictionary<string, string>
    {
        ["LoadTest__Enabled"] = "true",
        ["LoadTest__Password"] = Password,
    };

    private static string[] Create(int count = 3, string pattern = "pool-{n}@load.invalid") =>
        [CreateFlag, "--count", count.ToString(), "--email-pattern", pattern];

    [Fact]
    public async Task CreatesThePool_PrintsCountsOnly_AndASecondRunCreatesNothing()
    {
        var connectionString = await MigratedDatabaseAsync();

        var first = await HostJob.RunAsync(Create(), connectionString, Enabled);
        Assert.True(first.ExitCode == 0, first.Output);
        Assert.Contains("Created 3 load-test candidates; 0 already existed", first.Output);
        Assert.DoesNotContain(Password, first.Output);

        await using (var identity = IdentityContext(connectionString))
        {
            Assert.Equal(3, await identity.Users.CountAsync());
            Assert.All(await identity.Users.ToListAsync(), user =>
            {
                Assert.EndsWith("@load.invalid", user.Email);
                Assert.NotNull(user.PasswordHash);
                Assert.DoesNotContain(Password, user.PasswordHash);
            });
        }

        var second = await HostJob.RunAsync(Create(), connectionString, Enabled);
        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Contains("Created 0 load-test candidates; 3 already existed", second.Output);
        await using var after = IdentityContext(connectionString);
        Assert.Equal(3, await after.Users.CountAsync());
    }

    [Theory]
    [InlineData(CreateFlag)]
    [InlineData(EnrolFlag)]
    public async Task WithoutTheEnabledSetting_RefusesAndChangesNothing(string flag)
    {
        var connectionString = await MigratedDatabaseAsync();
        string[] args = flag == CreateFlag ? Create() : [EnrolFlag, "--exam", Guid.NewGuid().ToString(), "--count", "3", "--email-pattern", "pool-{n}@load.invalid"];

        // The password alone is not enough: the guard is its own setting, so a leftover password cannot switch the jobs on.
        var run = await HostJob.RunAsync(args, connectionString, new Dictionary<string, string> { ["LoadTest__Password"] = Password });

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("switched off", run.Output);
        await using var identity = IdentityContext(connectionString);
        Assert.Equal(0, await identity.Users.CountAsync());
    }

    [Fact]
    public async Task WithAnAddressThatCouldBeARealPerson_Refuses()
    {
        var connectionString = await MigratedDatabaseAsync();

        var run = await HostJob.RunAsync(Create(pattern: "pool-{n}@example.com"), connectionString, Enabled);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains(".invalid", run.Output);
        await using var identity = IdentityContext(connectionString);
        Assert.Equal(0, await identity.Users.CountAsync());
    }

    [Fact]
    public async Task WithoutASharedPassword_Refuses()
    {
        var connectionString = await MigratedDatabaseAsync();

        var run = await HostJob.RunAsync(Create(), connectionString, new Dictionary<string, string> { ["LoadTest__Enabled"] = "true" });

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("shared password is missing", run.Output);
        await using var identity = IdentityContext(connectionString);
        Assert.Equal(0, await identity.Users.CountAsync());
    }

    [Fact]
    public async Task WithAPasswordThePolicyRefuses_CreatesNothing_AndDoesNotPrintIt()
    {
        var connectionString = await MigratedDatabaseAsync();

        var run = await HostJob.RunAsync(
            Create(), connectionString, new Dictionary<string, string> { ["LoadTest__Enabled"] = "true", ["LoadTest__Password"] = "short" });

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("password is refused", run.Output);
        Assert.DoesNotContain("short\"", run.Output);
        await using var identity = IdentityContext(connectionString);
        Assert.Equal(0, await identity.Users.CountAsync());
    }

    [Theory]
    [InlineData("--count", "x")]
    [InlineData("--count", "0")]
    public async Task WithABadCount_PrintsUsage(string flag, string value)
    {
        var connectionString = await MigratedDatabaseAsync();

        var run = await HostJob.RunAsync([CreateFlag, flag, value, "--email-pattern", "pool-{n}@load.invalid"], connectionString, Enabled);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Usage", run.Output);
    }

    [Fact]
    public async Task Enrolling_InAnExamThatDoesNotExist_Refuses()
    {
        var connectionString = await MigratedDatabaseAsync();
        Assert.Equal(0, (await HostJob.RunAsync(Create(), connectionString, Enabled)).ExitCode);

        var run = await HostJob.RunAsync(
            [EnrolFlag, "--exam", Guid.NewGuid().ToString(), "--count", "3", "--email-pattern", "pool-{n}@load.invalid"], connectionString, Enabled);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("No exam has that id", run.Output);
    }

    [Fact]
    public async Task Enrolling_WithoutAnExamId_PrintsUsage()
    {
        var connectionString = await MigratedDatabaseAsync();

        var run = await HostJob.RunAsync([EnrolFlag, "--count", "3", "--email-pattern", "pool-{n}@load.invalid"], connectionString, Enabled);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Usage", run.Output);
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
}
