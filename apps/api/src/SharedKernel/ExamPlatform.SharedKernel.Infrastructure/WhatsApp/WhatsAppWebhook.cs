using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

/// <summary>Checks that a webhook call really came from Meta, by the HMAC it signs the body with.</summary>
public static class WhatsAppWebhookSignature
{
    /// <summary>The header Meta puts the signature in.</summary>
    public const string HeaderName = "X-Hub-Signature-256";

    private const string Prefix = "sha256=";

    /// <summary>Signs a body the way Meta does: <c>sha256=</c> and the lower-case hex HMAC-SHA256 of it under the app secret.</summary>
    /// <param name="body">The request body exactly as received, byte for byte.</param>
    /// <param name="appSecret">The Meta app's secret.</param>
    public static string Compute(ReadOnlySpan<byte> body, string appSecret) =>
        Prefix + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));

    /// <summary>Whether <paramref name="header"/> is the signature of <paramref name="body"/> under the app secret.</summary>
    /// <param name="body">The request body exactly as received; a re-serialised copy would not match.</param>
    /// <param name="header">The <see cref="HeaderName"/> header's value, or null when the call had none.</param>
    /// <param name="appSecret">The Meta app's secret.</param>
    public static bool IsValid(ReadOnlySpan<byte> body, string? header, string appSecret)
    {
        if (string.IsNullOrEmpty(header) || !header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] claimed;
        try
        {
            claimed = Convert.FromHexString(header.AsSpan(Prefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        // A comparison that takes the same time wherever the two differ, so the signature cannot be found out byte by byte.
        return CryptographicOperations.FixedTimeEquals(claimed, HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body));
    }
}

/// <summary>Meta's one-off check, when the webhook is set up, that the callback URL belongs to someone who knows the verify token.</summary>
public static class WhatsAppWebhookVerification
{
    /// <summary>
    /// Answers the <c>GET</c> Meta sends (<c>hub.mode</c>, <c>hub.verify_token</c>, <c>hub.challenge</c>).
    /// </summary>
    /// <param name="mode">The <c>hub.mode</c> query value; it must be <c>subscribe</c>.</param>
    /// <param name="token">The <c>hub.verify_token</c> query value.</param>
    /// <param name="challenge">The <c>hub.challenge</c> query value, to be echoed back.</param>
    /// <param name="expectedToken">The verify token configured here.</param>
    /// <returns>The challenge to echo, or null when the call is not a valid verification.</returns>
    public static string? Answer(string? mode, string? token, string? challenge, string expectedToken)
    {
        if (mode != "subscribe" || string.IsNullOrEmpty(challenge) || token is null)
        {
            return null;
        }

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(token), Encoding.UTF8.GetBytes(expectedToken))
            ? challenge
            : null;
    }
}

/// <summary>Something a webhook call reports.</summary>
public abstract record WhatsAppWebhookEvent;

/// <summary>What became of a message the platform sent: <c>sent</c>, <c>delivered</c>, <c>read</c> or <c>failed</c>.</summary>
/// <param name="MessageId">The id <see cref="WhatsAppSendResult.MessageId"/> gave when the message was handed over.</param>
/// <param name="Status">The status as Meta names it.</param>
/// <param name="Recipient">The recipient's WhatsApp id (their number), if given.</param>
/// <param name="ErrorCode">Meta's error code when the message failed.</param>
/// <param name="ErrorTitle">Meta's short reason when the message failed.</param>
public sealed record WhatsAppDeliveryStatus(string MessageId, string Status, string? Recipient, int? ErrorCode, string? ErrorTitle)
    : WhatsAppWebhookEvent;

/// <summary>
/// A message someone sent to the platform's number. Only the fact is kept: what they wrote is personal data the platform has
/// no use for yet.
/// </summary>
/// <param name="MessageId">WhatsApp's id for the message.</param>
/// <param name="From">The sender's WhatsApp id (their number).</param>
/// <param name="Type">The message type as Meta names it (<c>text</c>, <c>button</c>, <c>image</c>, ...).</param>
public sealed record WhatsAppInboundMessage(string MessageId, string From, string Type) : WhatsAppWebhookEvent;

/// <summary>Reads what Meta's webhook calls carry.</summary>
public static class WhatsAppWebhookPayload
{
    /// <summary>
    /// Reads the delivery reports and inbound messages out of a webhook body, skipping anything of another kind (templates
    /// changing status, account alerts) and any entry missing what it needs.
    /// </summary>
    /// <param name="json">The request body.</param>
    /// <exception cref="JsonException">The body is not JSON.</exception>
    public static IReadOnlyList<WhatsAppWebhookEvent> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var events = new List<WhatsAppWebhookEvent>();

        foreach (var entry in Items(document.RootElement, "entry"))
        {
            foreach (var change in Items(entry, "changes"))
            {
                if (!change.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var status in Items(value, "statuses"))
                {
                    if (Text(status, "id") is { } id && Text(status, "status") is { } state)
                    {
                        var error = Items(status, "errors").FirstOrDefault();
                        events.Add(new WhatsAppDeliveryStatus(
                            id,
                            state,
                            Text(status, "recipient_id"),
                            error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var code) && code.TryGetInt32(out var number)
                                ? number
                                : null,
                            error.ValueKind == JsonValueKind.Object ? Text(error, "title") : null));
                    }
                }

                foreach (var inbound in Items(value, "messages"))
                {
                    if (Text(inbound, "id") is { } id && Text(inbound, "from") is { } sender)
                    {
                        events.Add(new WhatsAppInboundMessage(id, sender, Text(inbound, "type") ?? "unknown"));
                    }
                }
            }
        }

        return events;
    }

    private static IEnumerable<JsonElement> Items(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(name, out var array)
        && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static string? Text(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
