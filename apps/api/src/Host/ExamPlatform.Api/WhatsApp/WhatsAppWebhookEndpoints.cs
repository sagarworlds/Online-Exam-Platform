using System.Text;
using System.Text.Json;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Api.WhatsApp;

/// <summary>
/// The callback URL Meta's WhatsApp webhook is pointed at. It is not a module's route: it is the platform's one door for what
/// WhatsApp reports, whichever module sent the message, so it lives in the Host beside the other cross-cutting wiring.
/// Anonymous by nature (Meta holds no account here), so every call is proven instead: the setup handshake by the verify token
/// and each report by the app secret's signature. Until those are configured the routes answer 404, as if not there.
/// </summary>
internal static class WhatsAppWebhookEndpoints
{
    /// <summary>The route to enter as the callback URL in the Meta app dashboard (on the API's own address).</summary>
    public const string Route = "/v1/webhooks/whatsapp";

    // Meta's reports are a few hundred bytes each; this only stops a caller making the host buffer an enormous body.
    private const int MaxBodyBytes = 1024 * 1024;

    /// <summary>Maps the verification handshake and the delivery-report receiver.</summary>
    public static IEndpointRouteBuilder MapWhatsAppWebhook(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(Route, Verify).AllowAnonymous().ExcludeFromDescription();
        endpoints.MapPost(Route, ReceiveAsync).AllowAnonymous().ExcludeFromDescription();
        return endpoints;
    }

    // Meta calls this once, when the operator saves the webhook in the app dashboard, and expects the challenge echoed back.
    private static IResult Verify(HttpRequest request, IOptions<WhatsAppOptions> options)
    {
        var whatsApp = options.Value;
        if (!whatsApp.CanReceiveWebhooks)
        {
            return Results.NotFound();
        }

        var challenge = WhatsAppWebhookVerification.Answer(
            request.Query["hub.mode"], request.Query["hub.verify_token"], request.Query["hub.challenge"], whatsApp.WebhookVerifyToken!);
        return challenge is null ? Results.StatusCode(StatusCodes.Status403Forbidden) : Results.Text(challenge, "text/plain");
    }

    private static async Task<IResult> ReceiveAsync(
        HttpRequest request, IOptions<WhatsAppOptions> options, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        var whatsApp = options.Value;
        if (!whatsApp.CanReceiveWebhooks)
        {
            return Results.NotFound();
        }

        var logger = loggerFactory.CreateLogger("ExamPlatform.Api.WhatsAppWebhook");

        // A declared length over the limit is refused without reading a byte; a body that lies about its length (or declares
        // none) is stopped by the bounded read below.
        if (request.ContentLength > MaxBodyBytes)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        var body = await ReadBoundedAsync(request.Body, cancellationToken);
        if (body is null)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        // The signature is over the bytes as received, so it is checked before anything is parsed.
        if (!WhatsAppWebhookSignature.IsValid(body, request.Headers[WhatsAppWebhookSignature.HeaderName], whatsApp.AppSecret!))
        {
            logger.LogWarning("A WhatsApp webhook call was refused: its signature is missing or does not match the app secret.");
            return Results.Unauthorized();
        }

        IReadOnlyList<WhatsAppWebhookEvent> events;
        try
        {
            events = WhatsAppWebhookPayload.Parse(Encoding.UTF8.GetString(body));
        }
        catch (JsonException)
        {
            return Results.BadRequest();
        }

        foreach (var webhookEvent in events)
        {
            Log(logger, webhookEvent);
        }

        // Answered 200 whatever the events were: Meta redelivers anything else, and there is nothing here to retry.
        return Results.Ok();
    }

    // What the platform does with a delivery report today is let an operator see it: whether a candidate's code reached their
    // phone, and if not, Meta's reason. Numbers are masked; nothing a person wrote is read.
    private static void Log(ILogger logger, WhatsAppWebhookEvent webhookEvent)
    {
        switch (webhookEvent)
        {
            case WhatsAppDeliveryStatus { Status: "failed" } failed:
                logger.LogWarning(
                    "WhatsApp message {MessageId} to {Recipient} failed: {ErrorCode} {ErrorTitle}",
                    failed.MessageId, WhatsAppPhoneNumber.Mask(failed.Recipient), failed.ErrorCode, failed.ErrorTitle);
                break;
            case WhatsAppDeliveryStatus status:
                logger.LogInformation(
                    "WhatsApp message {MessageId} to {Recipient} is {Status}",
                    status.MessageId, WhatsAppPhoneNumber.Mask(status.Recipient), status.Status);
                break;
            case WhatsAppInboundMessage inbound:
                logger.LogInformation(
                    "WhatsApp message {MessageId} ({Type}) received from {Sender}; its content is not read",
                    inbound.MessageId, inbound.Type, WhatsAppPhoneNumber.Mask(inbound.From));
                break;
        }
    }

    // Null when the body is longer than the limit.
    private static async Task<byte[]?> ReadBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > MaxBodyBytes)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}
