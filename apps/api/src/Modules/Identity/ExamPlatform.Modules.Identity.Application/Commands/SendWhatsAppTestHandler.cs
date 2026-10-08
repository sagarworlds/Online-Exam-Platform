using System.Globalization;
using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application.Dtos;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;

namespace ExamPlatform.Modules.Identity.Application.Commands;

/// <summary>An administrator's request to send a test message over WhatsApp.</summary>
/// <param name="PhoneNumber">The recipient, as typed.</param>
/// <param name="Mode">What to send: <c>Text</c> or <c>SignInTemplate</c>, in any case.</param>
/// <param name="Message">The text, for <c>Text</c>.</param>
/// <param name="ActorUserId">The administrator, for the audit trail.</param>
/// <param name="ActorRole">Their role name, for the audit trail.</param>
public sealed record SendWhatsAppTestCommand(string? PhoneNumber, string? Mode, string? Message, Guid ActorUserId, string? ActorRole);

/// <summary>
/// Sends a test message through the real WhatsApp connection so an administrator can tell whether it works, and why not if it does not.
/// It sends a real message to a real person, so every send is audited: who, to which number (masked), what kind and with what outcome.
/// What the administrator wrote is not recorded.
/// </summary>
public sealed class SendWhatsAppTestHandler(IWhatsAppDiagnostics diagnostics, IAuditLogger auditLogger)
{
    /// <summary>The longest message the test form takes. WhatsApp allows far more; a test needs a sentence.</summary>
    public const int MaxMessageLength = 1000;

    // Longer than any phone number, so a mistake is caught here and an absurd value never reaches Meta.
    private const int MaxPhoneNumberLength = 32;

    /// <summary>Validates the request, sends the message, audits it, and returns what happened.</summary>
    /// <param name="command">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidWhatsAppMessageError">There is no number, the mode is unknown, or a text message is empty or too long.</exception>
    public async Task<WhatsAppSendResultDto> HandleAsync(SendWhatsAppTestCommand command, CancellationToken cancellationToken)
    {
        var number = command.PhoneNumber?.Trim();
        if (string.IsNullOrEmpty(number))
        {
            throw new InvalidWhatsAppMessageError("Enter the phone number to send to.");
        }

        if (number.Length > MaxPhoneNumberLength)
        {
            throw new InvalidWhatsAppMessageError($"A phone number is at most {MaxPhoneNumberLength} characters.");
        }

        if (!Enum.TryParse<WhatsAppTestMode>(command.Mode?.Trim(), ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
        {
            throw new InvalidWhatsAppMessageError($"The mode must be one of: {string.Join(", ", Enum.GetNames<WhatsAppTestMode>())}.");
        }

        string? text = null;
        if (mode == WhatsAppTestMode.Text)
        {
            text = command.Message?.Trim();
            if (string.IsNullOrEmpty(text))
            {
                throw new InvalidWhatsAppMessageError("Write the message to send.");
            }

            if (text.Length > MaxMessageLength)
            {
                throw new InvalidWhatsAppMessageError($"The message must be at most {MaxMessageLength} characters.");
            }
        }

        var result = await diagnostics.SendAsync(mode, number, text, cancellationToken);

        var metadata = new Dictionary<string, string>
        {
            ["mode"] = mode.ToString(),
            ["outcome"] = result.Sent ? "Sent" : result.Failure?.Kind ?? "NotSent",
        };
        if (result.To is not null)
        {
            metadata["to"] = result.To;
        }

        if (result.MessageId is not null)
        {
            metadata["messageId"] = result.MessageId;
        }

        if (text is not null)
        {
            // How long, not what: the words are the administrator's and may name a person.
            metadata["length"] = text.Length.ToString(CultureInfo.InvariantCulture);
        }

        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: command.ActorUserId,
                ActorRole: command.ActorRole,
                Action: "Identity.WhatsAppTestSent",
                EntityType: "WhatsApp",
                EntityId: result.MessageId ?? "test",
                Metadata: metadata,
                CorrelationId: null),
            cancellationToken);

        return result;
    }
}
