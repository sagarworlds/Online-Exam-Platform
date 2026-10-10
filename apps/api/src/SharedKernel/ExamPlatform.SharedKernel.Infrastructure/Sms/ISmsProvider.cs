namespace ExamPlatform.SharedKernel.Infrastructure.Sms;

/// <summary>
/// The transport to an SMS provider, the one part that changes when a provider is chosen. Modules never call it: the master switch
/// sits in front of it in <see cref="SwitchedSmsSender"/>.
/// </summary>
public interface ISmsProvider
{
    /// <summary>Hands the message to the provider.</summary>
    /// <param name="message">What to send and where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the provider accepted it; <see langword="false"/> when it did not.</returns>
    Task<bool> SendAsync(SmsMessage message, CancellationToken cancellationToken);
}
