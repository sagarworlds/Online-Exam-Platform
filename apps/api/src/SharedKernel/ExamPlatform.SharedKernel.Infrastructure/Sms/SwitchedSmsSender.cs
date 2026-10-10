using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.Sms;

/// <summary>
/// The master switch for SMS. While <c>Sms:Enabled</c> is not true it refuses every message, and otherwise it hands the message to the
/// provider. The switch is read from configuration at start-up, so a change takes effect when the host restarts (Render restarts a
/// service when one of its environment values changes).
/// </summary>
/// <param name="provider">The transport to the SMS provider.</param>
/// <param name="options">The switch.</param>
/// <param name="logger">Records each message that is not sent. It never records the number or the text.</param>
public sealed class SwitchedSmsSender(ISmsProvider provider, IOptions<SmsOptions> options, ILogger<SwitchedSmsSender> logger) : ISmsSender
{
    /// <inheritdoc />
    public Task<bool> SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        // Checked before the provider is called, so a host that is switched off never makes a call that could be billed.
        if (!options.Value.IsEnabled)
        {
            logger.LogWarning("SMS not sent: SMS is switched off (Sms:Enabled is not true).");
            return Task.FromResult(false);
        }

        return provider.SendAsync(message, cancellationToken);
    }
}
