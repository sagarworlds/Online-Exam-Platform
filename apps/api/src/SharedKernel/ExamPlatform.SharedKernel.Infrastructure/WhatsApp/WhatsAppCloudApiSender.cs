using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>
/// Sends messages through Meta's WhatsApp Cloud API. Every refusal comes back as a <see cref="WhatsAppFailure"/> saying what went wrong
/// and what to do about it, so an operator testing the setup is told the real reason; callers that answer a candidate ignore it, since
/// a candidate can do nothing about it.
/// </summary>
public sealed class WhatsAppCloudApiSender(HttpClient httpClient, IOptions<WhatsAppOptions> options, ILogger<WhatsAppCloudApiSender> logger)
    : IWhatsAppSender
{
    /// <inheritdoc />
    public Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken) =>
        PostAsync(BuildTemplatePayload(message), message.TemplateName, message.LanguageCode, cancellationToken);

    /// <inheritdoc />
    public Task<WhatsAppSendResult> SendTextAsync(WhatsAppTextMessage message, CancellationToken cancellationToken) =>
        PostAsync(BuildTextPayload(message), templateName: null, languageCode: null, cancellationToken);

    private async Task<WhatsAppSendResult> PostAsync(string payload, string? templateName, string? languageCode, CancellationToken cancellationToken)
    {
        var whatsApp = options.Value;
        if (!whatsApp.IsEnabled)
        {
            // Said every time, not once: a sign-in or invitation that got nothing should say why in the log, and a switch left off by
            // mistake is found that way.
            logger.LogWarning("WhatsApp message not sent: WhatsApp is switched off (WhatsApp:Enabled is not true).");
            return new WhatsAppSendResult(false, Failure: WhatsAppFailure.SwitchedOff());
        }

        if (!whatsApp.CanSend)
        {
            logger.LogWarning("WhatsApp message not sent: it is not configured (WhatsApp:AccessToken / WhatsApp:PhoneNumberId).");
            return new WhatsAppSendResult(false, Failure: WhatsAppFailure.NotConfigured(whatsApp.MissingForSending()));
        }

        var baseUrl = whatsApp.BaseUrl.TrimEnd('/');
        var endpoint = $"{baseUrl}/{whatsApp.ApiVersion.Trim('/')}/{Uri.EscapeDataString(whatsApp.PhoneNumberId!)}/messages";
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var address)
            || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
        {
            // Without this, HttpClient throws for an address like "graph.facebook.com" typed without its https://, and a sender that
            // says it never throws would take the request down with it.
            logger.LogError("WhatsApp message not sent: WhatsApp:BaseUrl is not a web address.");
            return new WhatsAppSendResult(false, Failure: WhatsAppFailure.BadBaseUrl(whatsApp.BaseUrl));
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
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
            var error = ReadError(body);
            logger.LogError(
                "WhatsApp refused a message (status {StatusCode}, error {ErrorCode}): {Reason}",
                (int)response.StatusCode, error.Code, error.Message ?? "no reason given");
            return new WhatsAppSendResult(
                false,
                Failure: WhatsAppErrorGuide.Explain(
                    (int)response.StatusCode,
                    error.Code,
                    WhatsAppErrorGuide.Describe(error.Message, error.Details, error.TraceId),
                    templateName,
                    languageCode,
                    error.Subcode));
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "The connection to WhatsApp's Cloud API failed while sending a message.");
            return new WhatsAppSendResult(false, Failure: WhatsAppFailure.Unreachable(Host(baseUrl), (ex.InnerException ?? ex).Message));
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own request timeout this way, distinct from the caller's cancellation.
            logger.LogError(ex, "WhatsApp's Cloud API did not respond in time.");
            return new WhatsAppSendResult(false, Failure: WhatsAppFailure.TimedOut());
        }
    }

    // The request shape: https://developers.facebook.com/docs/whatsapp/cloud-api/reference/messages
    private static string BuildTemplatePayload(WhatsAppTemplateMessage message)
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
            to = WireNumber(message.To),
            type = "template",
            template = new
            {
                name = message.TemplateName,
                language = new { code = message.LanguageCode },
                components,
            },
        });
    }

    private static string BuildTextPayload(WhatsAppTextMessage message) =>
        JsonSerializer.Serialize(new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = WireNumber(message.To),
            type = "text",
            // No link previews: an administrator's test should send exactly the words typed and nothing fetched from them.
            text = new { preview_url = false, body = message.Body },
        });

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

    // Meta recommends the leading +: without it, a number can be read as local to the business's own country.
    private static string WireNumber(string number) => number.StartsWith('+') ? number : "+" + number;

    private static (int? Code, int? Subcode, string? Message, string? Details, string? TraceId) ReadError(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                int? code = error.TryGetProperty("code", out var c) && c.TryGetInt32(out var number) ? number : null;
                int? subcode = error.TryGetProperty("error_subcode", out var s) && s.TryGetInt32(out var sub) ? sub : null;
                var message = error.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : null;
                var details = error.TryGetProperty("error_data", out var data)
                    && data.ValueKind == JsonValueKind.Object
                    && data.TryGetProperty("details", out var d)
                    && d.ValueKind == JsonValueKind.String
                        ? d.GetString()
                        : null;
                var trace = error.TryGetProperty("fbtrace_id", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                return (code, subcode, message, details, trace);
            }
        }
        catch (JsonException)
        {
            // Not JSON (a proxy's error page, say): there is nothing safe to repeat.
        }

        return (null, null, null, null, null);
    }

    private static string Host(string baseUrl) => Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ? uri.Host : baseUrl;
}
