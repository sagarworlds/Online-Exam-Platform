using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>
/// The registration asked for its confirmation code on a channel whose contact detail was
/// not given (e.g. by email, with only a phone number).
/// </summary>
/// <param name="channel">The channel the confirmation code was asked for on.</param>
public sealed class ContactChannelMismatchError(OtpChannel channel) : DomainException(channel == OtpChannel.Email
    ? "A confirmation code can only be sent by email when an email address is given."
    : "A confirmation code can only be sent by SMS when a phone number is given.")
{
    /// <inheritdoc />
    public override string ErrorCode => "contact_channel_mismatch";
}
