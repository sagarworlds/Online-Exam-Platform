using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.LoadTest;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExamPlatform.Modules.Identity.Endpoints.Operations;

/// <summary>
/// <c>--create-load-test-candidates --count N --email-pattern "candidate-{n}@load.invalid"</c>: creates the candidate accounts the
/// load test signs in as (ADR 0006). It does nothing unless <c>LoadTest:Enabled</c> is set, which only a staging job does, and the
/// shared password comes from <c>LoadTest:Password</c>, never from the command line. The rules are in
/// <see cref="ProvisionLoadTestCandidatesHandler"/>; this class reads the command line and turns refusals into an exit code.
/// </summary>
public sealed class CreateLoadTestCandidatesCommand(
    ProvisionLoadTestCandidatesHandler handler,
    IOptions<LoadTestSeedingOptions> options,
    ILogger<CreateLoadTestCandidatesCommand> logger) : IOperatorCommand
{
    /// <summary>The command-line flag that selects this command.</summary>
    public const string CommandFlag = "--create-load-test-candidates";

    /// <summary>The exit code for a refusal: the command changed nothing and says why.</summary>
    public const int Refused = 2;

    /// <inheritdoc />
    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogError(
                "Creating load-test accounts is switched off here. Set {Section}__Enabled=true for this run, on staging only. Nothing was created.",
                LoadTestSeedingOptions.SectionName);
            return Refused;
        }

        LoadTestAccountPattern? pattern = null;
        string? problem = null;
        if (!int.TryParse(OperatorArguments.ValueOf(args, "--count"), out var count))
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
                "Usage: {Flag} --count <1 to {Max}> --email-pattern <pattern with {{n}}, ending in .invalid>. {Problem} Nothing was created.",
                CommandFlag, LoadTestAccountPattern.MaxCount, problem);
            return Refused;
        }

        try
        {
            var result = await handler.HandleAsync(new ProvisionLoadTestCandidatesCommand(pattern, options.Value.Password), cancellationToken);
            logger.LogInformation(
                "Created {Created} load-test candidates; {Existed} already existed and were left as they were.", result.Created, result.AlreadyExisted);
            return 0;
        }
        catch (InvalidLoadTestAccountsError ex)
        {
            logger.LogError("{Reason} Nothing was created.", ex.Message);
            return Refused;
        }
        catch (WeakPasswordError ex)
        {
            logger.LogError("The shared password is refused: {Reason} Nothing was created.", ex.Message);
            return Refused;
        }
        catch (RoleNotFoundError)
        {
            logger.LogError("The Candidate role does not exist yet. Run --migrate-and-seed first. Nothing was created.");
            return Refused;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogError("The database has no tables yet. Run --migrate-and-seed first. Nothing was created.");
            return Refused;
        }
    }
}
