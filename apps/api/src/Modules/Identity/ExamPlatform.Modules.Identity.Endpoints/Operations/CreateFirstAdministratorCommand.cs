using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Privacy;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Endpoints.OtpDelivery;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace ExamPlatform.Modules.Identity.Endpoints.Operations;

/// <summary>
/// <c>--create-first-administrator &lt;e-mail&gt;</c>: gives a deployed environment its first Super Admin (ADR 0005). The
/// only input is the address. No password is typed, stored or printed: the account gets one nobody sees, and the address
/// is e-mailed the ordinary link to set its own. This class only reads the command line and turns the handler's refusals
/// into an exit code and a log line that never holds the address; the rules are in
/// <see cref="ProvisionFirstAdministratorHandler"/>.
/// </summary>
public sealed class CreateFirstAdministratorCommand(
    IServiceProvider services,
    IMailSender mailSender,
    IOptions<OtpDeliveryOptions> otpDelivery,
    ILogger<CreateFirstAdministratorCommand> logger) : IOperatorCommand
{
    /// <summary>The command-line flag that selects this command.</summary>
    public const string CommandFlag = "--create-first-administrator";

    /// <summary>The exit code for a refusal: the command changed nothing and says why.</summary>
    public const int Refused = 2;

    /// <inheritdoc />
    public async Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        var email = OperatorArguments.ValueOf(args, CommandFlag);
        if (email is null)
        {
            logger.LogError("Usage: {Flag} <e-mail address of the first administrator>. Nothing was created.", CommandFlag);
            return Refused;
        }

        // Checked before anything is created: the password-setup link is the only way in, so an account made while no
        // mail can be sent would exist with a password nobody knows. With the development log provider the link is
        // written to the log instead, which is how a developer reads it.
        OtpDeliveryOptions delivery;
        try
        {
            delivery = otpDelivery.Value;
        }
        catch (OptionsValidationException ex)
        {
            // A job started with only a connection string has no delivery settings, and outside Development they are required.
            logger.LogError(
                "Sign-in codes cannot be delivered with the current settings ({Reasons}), so the first administrator's password-setup " +
                "e-mail could not be sent. Nothing was created. Set the mail settings (docs/deploy-render.md, step 5) and run the command again.",
                string.Join("; ", ex.Failures));
            return Refused;
        }

        if (delivery.Provider == OtpDeliveryOptions.Smtp && !mailSender.IsConfigured)
        {
            logger.LogError(
                "No mail server is configured, so the first administrator's password-setup e-mail could not be sent. " +
                "Nothing was created. Set the mail settings (docs/deploy-render.md, step 5) and run the command again.");
            return Refused;
        }

        var masked = ContactMasker.Mask(OtpChannel.Email, email);
        try
        {
            // Resolved here, not injected: the handler reaches the code sender, which cannot be built until delivery is set up.
            var handler = services.GetRequiredService<ProvisionFirstAdministratorHandler>();
            await handler.HandleAsync(new ProvisionFirstAdministratorCommand(email), cancellationToken);
        }
        catch (InvalidAdministratorEmailError ex)
        {
            logger.LogError("{Reason} Nothing was created.", ex.Message);
            return Refused;
        }
        catch (RoleNotFoundError)
        {
            logger.LogError("The Super Admin role does not exist yet. Run --migrate-and-seed first. Nothing was created.");
            return Refused;
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // The most likely slip: the job was run against a database that no one has migrated yet.
            logger.LogError("The database has no tables yet. Run --migrate-and-seed first. Nothing was created.");
            return Refused;
        }
        catch (FirstAdministratorAlreadyExistsError ex)
        {
            logger.LogError("{Reason} Nothing was changed.", ex.Message);
            return Refused;
        }
        catch (DuplicateAccountError)
        {
            logger.LogError(
                "An account already exists for {Address}. It was not changed, and it was not made an administrator. Nothing was created.",
                masked);
            return Refused;
        }

        logger.LogInformation(
            "Created the first administrator {Address}. A password-setup e-mail was sent to that address; the link works once and " +
            "expires soon. If it does not arrive, check the mail settings and use 'Forgot password' on the sign-in page.",
            masked);
        return 0;
    }
}
