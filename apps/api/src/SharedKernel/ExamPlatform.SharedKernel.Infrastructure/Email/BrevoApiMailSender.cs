using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.Email;

/// <summary>
/// Sends mail through Brevo's HTTPS transactional-email API instead of SMTP, for a host (e.g. Render's free plan)
/// that blocks outbound SMTP ports: the request is a normal HTTPS call, which those hosts allow. With no API key
/// configured it sends nothing and says so, the same contract <see cref="SmtpMailSender"/> follows. It never writes
/// a message's body or subject to a log: a body can carry a credential, such as an invitation link or an OTP code.
/// </summary>
public sealed class BrevoApiMailSender(HttpClient httpClient, IOptions<BrevoOptions> options, ILogger<BrevoApiMailSender> logger)
    : IMailSender
{
    private const string SendEndpoint = "https://api.brevo.com/v3/smtp/email";

    /// <inheritdoc />
    public async Task<bool> SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        var brevo = options.Value;
        if (!brevo.IsConfigured)
        {
            logger.LogWarning("E-mail not sent: Brevo is not configured (Brevo:ApiKey / Brevo:SenderEmail).");
            return false;
        }

        // Brevo's send-email request shape: https://developers.brevo.com/reference/sendtransacemail
        var payload = JsonSerializer.Serialize(new
        {
            sender = new { email = brevo.SenderEmail },
            to = new[] { new { email = mail.To } },
            subject = mail.Subject,
            textContent = mail.Body,
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, SendEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        // Brevo authenticates the transactional API with this header, not a Bearer token.
        request.Headers.TryAddWithoutValidation("api-key", brevo.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // The body can hold Brevo's reason (e.g. an unverified sender); it never names the recipient's address.
            var reason = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError("Brevo refused a message (status {StatusCode}): {Reason}", (int)response.StatusCode, reason);
            return false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "The connection to Brevo's API failed while sending a message.");
            return false;
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own request timeout this way, distinct from the caller's cancellation.
            logger.LogError(ex, "Brevo's API did not respond in time.");
            return false;
        }
    }
}
