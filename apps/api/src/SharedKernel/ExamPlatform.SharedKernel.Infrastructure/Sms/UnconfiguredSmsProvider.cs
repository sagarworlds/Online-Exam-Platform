using Microsoft.Extensions.Logging;

namespace ExamPlatform.SharedKernel.Infrastructure.Sms;

/// <summary>
/// The provider a host gets until an SMS provider is built in and chosen. It sends nothing and says so, so an SMS that was asked for is
/// never dropped without a trace. A real <see cref="ISmsProvider"/> replaces it when one exists.
/// </summary>
/// <param name="logger">Records each attempt. It never records the number or the text.</param>
public sealed class UnconfiguredSmsProvider(ILogger<UnconfiguredSmsProvider> logger) : ISmsProvider
{
    /// <inheritdoc />
    public Task<bool> SendAsync(SmsMessage message, CancellationToken cancellationToken)
    {
        logger.LogWarning("SMS not sent: no SMS provider is built into this platform yet.");
        return Task.FromResult(false);
    }
}
