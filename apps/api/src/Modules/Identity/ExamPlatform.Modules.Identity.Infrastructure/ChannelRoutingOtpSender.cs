using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// Sends a code over the adapter for its channel: e-mail by e-mail, a phone number by whatever delivers to phones. One
/// deployment can then mail some people their codes and message others', which a single adapter cannot, since an adapter
/// serves the channels its provider supports.
/// </summary>
/// <param name="emailSender">Delivers to <see cref="OtpChannel.Email"/>.</param>
/// <param name="phoneSender">Delivers to <see cref="OtpChannel.Sms"/>, the channel that means a phone number.</param>
public sealed class ChannelRoutingOtpSender(IOtpSender emailSender, IOtpSender phoneSender) : IOtpSender
{
    /// <inheritdoc />
    public Task SendAsync(OtpChannel channel, string destination, string code, CancellationToken cancellationToken) =>
        (channel == OtpChannel.Email ? emailSender : phoneSender).SendAsync(channel, destination, code, cancellationToken);
}
