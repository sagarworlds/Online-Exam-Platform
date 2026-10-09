using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>An administrator's WhatsApp test request was not usable: no number, no message, a message that is too long, or an unknown mode.</summary>
/// <param name="message">What is wrong, safe to show to the administrator.</param>
public sealed class InvalidWhatsAppMessageError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_whatsapp_message";
}
