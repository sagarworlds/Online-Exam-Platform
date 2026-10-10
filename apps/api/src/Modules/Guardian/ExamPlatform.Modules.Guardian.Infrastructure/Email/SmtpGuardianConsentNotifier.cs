using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.SharedKernel.Infrastructure.Email;

namespace ExamPlatform.Modules.Guardian.Infrastructure.Email;

/// <summary>
/// Writes the guardian's confirmation request and hands it to the platform's mail sender. With no mail server configured nothing is
/// sent, and the staff member is handed the link instead (FR-39). The link carries the code, so it appears only in the message itself.
/// </summary>
public sealed class SmtpGuardianConsentNotifier(IMailSender mailSender) : IGuardianConsentNotifier
{
    /// <inheritdoc />
    public Task<bool> SendAsync(GuardianConsentRequest request, CancellationToken cancellationToken) =>
        mailSender.SendAsync(
            new OutgoingMail(
                request.GuardianEmail,
                "Please confirm that you are the guardian of an exam candidate",
                $"Hello {request.GuardianName},\r\n\r\n" +
                $"{request.CandidateEmail} has been linked to you as their guardian for the platform's exams.\r\n\r\n" +
                $"If this is correct, confirm it at this link:\r\n{request.ConfirmLink}\r\n\r\n" +
                $"The link works once and expires at {request.ExpiresAtUtc:u}. If you do not know this candidate, do nothing; " +
                "the link will not be confirmed."),
            cancellationToken);
}
