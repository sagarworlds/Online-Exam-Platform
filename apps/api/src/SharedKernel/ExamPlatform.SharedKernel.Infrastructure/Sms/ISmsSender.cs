namespace ExamPlatform.SharedKernel.Infrastructure.Sms;

/// <summary>A text message to be sent to a phone.</summary>
/// <param name="To">The recipient's phone number in international form (a plus sign and the country code). Never logged.</param>
/// <param name="Body">The text. It may carry a code or a link, so a sender never logs it.</param>
public sealed record SmsMessage(string To, string Body);

/// <summary>
/// Sends SMS for every module that needs one. The one place that knows whether SMS may be sent at all: the master switch
/// (<c>Sms:Enabled</c>) is checked here, before anything reaches a provider, so no module can send past it.
/// </summary>
public interface ISmsSender
{
    /// <summary>Tries to deliver the message.</summary>
    /// <param name="message">What to send and where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when the message was handed to a provider; <see langword="false"/> when nothing was sent, either because
    /// SMS is switched off or because no provider sent it. It does not throw for either: the caller has already saved whatever the
    /// message is about, and tells the person to pass it on by other means.
    /// </returns>
    Task<bool> SendAsync(SmsMessage message, CancellationToken cancellationToken);
}
