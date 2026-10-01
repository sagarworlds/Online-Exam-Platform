using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>The request named a one-time-code delivery channel that does not exist.</summary>
public sealed class InvalidOtpChannelError()
    : DomainException($"The one-time code channel must be one of: {string.Join(", ", Enum.GetNames<OtpChannel>())}.")
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_otp_channel";
}
