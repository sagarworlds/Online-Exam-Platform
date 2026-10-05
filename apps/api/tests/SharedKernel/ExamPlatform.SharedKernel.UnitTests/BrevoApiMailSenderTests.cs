using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using ExamPlatform.SharedKernel.Infrastructure.Email;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// <see cref="BrevoApiMailSender"/> against a stub <see cref="HttpMessageHandler"/>, so what is checked is the request it builds
/// and how it reads Brevo's response, without a real network call.
/// </summary>
public class BrevoApiMailSenderTests
{
    private static readonly OutgoingMail Mail = new(
        "candidate@example.com", "A subject", "A body with a secret code:\r\n123456\r\n\r\nIt works once.");

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    private static BrevoApiMailSender SenderFor(StubHandler handler, bool configured = true) =>
        new(
            new HttpClient(handler),
            Options.Create(new BrevoOptions
            {
                ApiKey = configured ? "test-api-key" : null,
                SenderEmail = "exams@examplatform.test",
            }),
            NullLogger<BrevoApiMailSender>.Instance);

    [Fact]
    public async Task SendAsync_PostsTheMessageToBrevosApi_WithTheApiKeyHeader()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));

        var sent = await SenderFor(handler).SendAsync(Mail, CancellationToken.None);

        Assert.True(sent);
        Assert.Equal("https://api.brevo.com/v3/smtp/email", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(HttpMethod.Post, handler.LastRequest.Method);
        Assert.Equal("test-api-key", Assert.Single(handler.LastRequest.Headers.GetValues("api-key")));

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastRequestBody!);
        Assert.Equal("exams@examplatform.test", body.GetProperty("sender").GetProperty("email").GetString());
        Assert.Equal("candidate@example.com", body.GetProperty("to")[0].GetProperty("email").GetString());
        Assert.Equal("A subject", body.GetProperty("subject").GetString());
        Assert.Contains("123456", body.GetProperty("textContent").GetString());
    }

    [Fact]
    public async Task SendAsync_WithNoApiKeyConfigured_SendsNothingAndSaysSo()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("Should not call out when not configured."));

        var sent = await SenderFor(handler, configured: false).SendAsync(Mail, CancellationToken.None);

        Assert.False(sent);
    }

    [Fact]
    public async Task SendAsync_WhenBrevoRefusesTheMessage_ReportsNotSent_InsteadOfThrowing()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"message\":\"Key not found\"}", Encoding.UTF8, "application/json"),
        });

        var sent = await SenderFor(handler).SendAsync(Mail, CancellationToken.None);

        Assert.False(sent);
    }

    [Fact]
    public async Task SendAsync_WhenTheConnectionFails_ReportsNotSent_InsteadOfThrowing()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("DNS failure"));

        var sent = await SenderFor(handler).SendAsync(Mail, CancellationToken.None);

        Assert.False(sent);
    }
}
