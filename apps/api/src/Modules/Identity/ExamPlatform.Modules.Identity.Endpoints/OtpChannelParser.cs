using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Endpoints;

/// <summary>Turns the channel name a client sends into an <see cref="OtpChannel"/>.</summary>
internal static class OtpChannelParser
{
    /// <summary>Parses a channel name such as "Email" or "sms", ignoring case.</summary>
    /// <param name="value">The channel name from the request body; may be null when the client omitted it.</param>
    /// <returns>The named channel.</returns>
    /// <exception cref="InvalidOtpChannelError">The value is missing or names no channel.</exception>
    public static OtpChannel Parse(string? value)
    {
        // Matched against the declared names rather than through Enum.TryParse, which also
        // accepts numbers ("5", including undefined ones) and comma-joined names ("Email,Sms").
        foreach (var channel in Enum.GetValues<OtpChannel>())
        {
            if (string.Equals(channel.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return channel;
            }
        }

        throw new InvalidOtpChannelError();
    }
}
