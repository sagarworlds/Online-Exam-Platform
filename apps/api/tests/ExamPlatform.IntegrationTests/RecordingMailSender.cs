using ExamPlatform.SharedKernel.Infrastructure.Email;

namespace ExamPlatform.IntegrationTests;

/// <summary>A mail sender that sends nothing and remembers what it was asked to send, so a notifier's message can be checked on its own.</summary>
internal sealed class RecordingMailSender : IMailSender
{
    /// <summary>What <see cref="SendAsync"/> answers, standing in for "handed to a mail server" (true) or "not sent" (false).</summary>
    public bool Delivers { get; set; } = true;

    /// <summary>Every message handed over, oldest first.</summary>
    public List<OutgoingMail> Sent { get; } = [];

    /// <inheritdoc />
    public Task<bool> SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        Sent.Add(mail);
        return Task.FromResult(Delivers);
    }
}
