using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.LoadTest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExamPlatform.Modules.Invite.Endpoints.Operations;

/// <summary>
/// <c>--enrol-load-test-candidates --exam &lt;id&gt; --count N --email-pattern "candidate-{n}@load.invalid"</c>: enrols the load test's
/// candidate pool in the load-test exam (ADR 0006). Run it after <c>--create-load-test-candidates</c>, with the same count and pattern.
/// Like that command it does nothing unless <c>LoadTest:Enabled</c> is set, which only a staging job does. The rules are in
/// <see cref="EnrolLoadTestCandidatesHandler"/>; this class reads the command line and turns refusals into an exit code.
/// </summary>
public sealed class EnrolLoadTestCandidatesCommand(
    EnrolLoadTestCandidatesHandler handler,
    IOptions<LoadTestSeedingOptions> options,
    ILogger<EnrolLoadTestCandidatesCommand> logger) : IOperatorCommand
{
    /// <summary>The command-line flag that selects this command.</summary>
    public const string CommandFlag = "--enrol-load-test-candidates";

    /// <summary>The exit code for a refusal: the command changed nothing and says why.</summary>
    public const int Refused = 2;

    /// <inheritdoc />
    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogError(
                "Enrolling load-test accounts is switched off here. Set {Section}__Enabled=true for this run, on staging only. Nothing was enrolled.",
                LoadTestSeedingOptions.SectionName);
            return Refused;
        }

        LoadTestAccountPattern? pattern = null;
        string? problem = null;
        if (!Guid.TryParse(OperatorArguments.ValueOf(args, "--exam"), out var examId))
        {
            problem = "The exam id is missing or is not a GUID.";
        }
        else if (!int.TryParse(OperatorArguments.ValueOf(args, "--count"), out var count))
        {
            problem = "The count is missing or not a number.";
        }
        else if (LoadTestAccountPattern.TryCreate(OperatorArguments.ValueOf(args, "--email-pattern"), count, out var why, out var checkedPattern))
        {
            pattern = checkedPattern;
        }
        else
        {
            problem = why;
        }

        if (pattern is null)
        {
            logger.LogError(
                "Usage: {Flag} --exam <exam id> --count <1 to {Max}> --email-pattern <pattern with {{n}}, ending in .invalid>. {Problem} Nothing was enrolled.",
                CommandFlag, LoadTestAccountPattern.MaxCount, problem);
            return Refused;
        }

        try
        {
            var result = await handler.HandleAsync(new EnrolLoadTestPoolCommand(examId, pattern), cancellationToken);
            logger.LogInformation(
                "Enrolled {Enrolled} load-test candidates in the exam; {Already} were already enrolled. {Missing} addresses have no account yet (run --create-load-test-candidates first).",
                result.Enrolled, result.AlreadyEnrolled, result.WithoutAccount);
            return result.WithoutAccount > 0 && result.Enrolled == 0 && result.AlreadyEnrolled == 0 ? Refused : 0;
        }
        catch (InviteExamNotFoundError)
        {
            logger.LogError("No exam has that id. Nothing was enrolled.");
            return Refused;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogError("The database has no tables yet. Run --migrate-and-seed first. Nothing was enrolled.");
            return Refused;
        }
    }
}
