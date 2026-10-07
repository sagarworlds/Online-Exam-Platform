using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>
/// Sends template messages through Meta's WhatsApp Cloud API (<c>POST /{version}/{phone-number-id}/messages</c>). With the
/// access token or phone number id missing it sends nothing and says so, the contract <c>IMailSender</c>'s adapters follow.
/// It never writes a message's parameters or the access token to a log: a parameter can be a one-time code.
/// </summary>
public sealed class WhatsAppCloudApiSender(HttpClient httpClient, IOptions<WhatsAppOptions> options, ILogger<WhatsAppCloudApiSender> logger)
    : IWhatsAppSender
{
    /// <inheritdoc />
    public async Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken)
    {
        var whatsApp = options.Value;
        if (!whatsApp.CanSend)
        {
            logger.LogWarning("WhatsApp message not sent: it is not configured (WhatsApp:AccessToken / WhatsApp:PhoneNumberId).");
            return new WhatsAppSendResult(false);
        }

        var endpoint = $"{whatsApp.BaseUrl.TrimEnd('/')}/{whatsApp.ApiVersion.Trim('/')}/{Uri.EscapeDataString(whatsApp.PhoneNumberId!)}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildPayload(message), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", whatsApp.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return new WhatsAppSendResult(true, ReadMessageId(body));
            }

            // Meta says why (an expired token, a template that is not approved, a number that is not on WhatsApp). The reason
            // never names the recipient, and nothing from the request is logged.
            var (code, reason) = ReadError(body);
            logger.LogError(
                "WhatsApp refused a message (status {StatusCode}, error {ErrorCode}): {Reason}", (int)response.StatusCode, code, reason);
            return new WhatsAppSendResult(false);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "The connection to WhatsApp's Cloud API failed while sending a message.");
            return new WhatsAppSendResult(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own request timeout this way, distinct from the caller's cancellation.
            logger.LogError(ex, "WhatsApp's Cloud API did not respond in time.");
            return new WhatsAppSendResult(false);
        }
    }

    // The request shape: https://developers.facebook.com/docs/whatsapp/cloud-api/reference/messages
    private static string BuildPayload(WhatsAppTemplateMessage message)
    {
        var components = new List<object>();
        if (message.BodyParameters.Count > 0)
        {
            components.Add(new
            {
                type = "body",
                parameters = message.BodyParameters.Select(text => new { type = "text", text }).ToArray(),
            });
        }

        if (message.UrlButtonParameter is not null)
        {
            // The first (and, for an Authentication template, only) button; its index is a string in Meta's schema.
            components.Add(new
            {
                type = "button",
                sub_type = "url",
                index = "0",
                parameters = new[] { new { type = "text", text = message.UrlButtonParameter } },
            });
        }

        return JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = message.To,
            type = "template",
            template = new
            {
                name = message.TemplateName,
                language = new { code = message.LanguageCode },
                components,
            },
        });
    }

    private static string? ReadMessageId(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("messages", out var messages)
                && messages.ValueKind == JsonValueKind.Array
                && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("id", out var id)
                    ? id.GetString()
                    : null;
        }
        catch (JsonException)
        {
            // Accepted, but the answer was not what the API documents; the message was still sent.
            return null;
        }
    }

    private static (int? Code, string Reason) ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                int? code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var number) ? number : null;
                var reason = error.TryGetProperty("message", out var m) ? m.GetString() : null;
                return (code, reason ?? "no reason given");
            }
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's error page, say): there is nothing safe to repeat.
        }

        return (null, "the response was not an error report");
    }
}
