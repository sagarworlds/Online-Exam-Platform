using System.Text;
using System.Text.Json;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;

namespace ExamPlatform.SharedKernel.UnitTests;

public class WhatsAppWebhookTests
{
    private const string Secret = "app-secret-for-tests";

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    // ---- the signature ------------------------------------------------------------------------------

    [Fact]
    public void Signature_OfTheBodyUnderTheSecret_IsAccepted()
    {
        var body = Bytes("""{"object":"whatsapp_business_account"}""");

        Assert.True(WhatsAppWebhookSignature.IsValid(body, WhatsAppWebhookSignature.Compute(body, Secret), Secret));
    }

    [Fact]
    public void Signature_MatchesAKnownHmac()
    {
        // HMAC-SHA256("secret", "hello"), computed independently: a check that Compute is the HMAC Meta documents.
        Assert.Equal(
            "sha256=88aab3ede8d3adf94d26ab90d3bafd4a2083070c3bcce9c014ee04a443847c0b",
            WhatsAppWebhookSignature.Compute(Bytes("hello"), "secret"));
    }

    [Fact]
    public void Signature_IsAcceptedWithAnUpperCasePrefixOrHex()
    {
        var body = Bytes("{}");
        var signature = WhatsAppWebhookSignature.Compute(body, Secret);

        Assert.True(WhatsAppWebhookSignature.IsValid(body, "SHA256=" + signature["sha256=".Length..].ToUpperInvariant(), Secret));
    }

    [Fact]
    public void Signature_OfAnotherBody_IsRefused()
    {
        var signed = WhatsAppWebhookSignature.Compute(Bytes("""{"a":1}"""), Secret);

        Assert.False(WhatsAppWebhookSignature.IsValid(Bytes("""{"a":2}"""), signed, Secret));
    }

    [Fact]
    public void Signature_UnderAnotherSecret_IsRefused()
    {
        var body = Bytes("{}");

        Assert.False(WhatsAppWebhookSignature.IsValid(body, WhatsAppWebhookSignature.Compute(body, "someone-elses"), Secret));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sha256=")]
    [InlineData("sha256=not-hex")]
    [InlineData("sha256=abcd")]
    [InlineData("sha1=88aab3ede8d3adf94d26ab90d3bafd4a2083070c3bcce9c014ee04a443847c0b")]
    [InlineData("88aab3ede8d3adf94d26ab90d3bafd4a2083070c3bcce9c014ee04a443847c0b")]
    public void Signature_MissingOrMalformed_IsRefused(string? header) =>
        Assert.False(WhatsAppWebhookSignature.IsValid(Bytes("hello"), header, "secret"));

    // ---- the set-up handshake -----------------------------------------------------------------------

    [Fact]
    public void Verification_WithTheRightToken_EchoesTheChallenge() =>
        Assert.Equal("1158201444", WhatsAppWebhookVerification.Answer("subscribe", "verify-me", "1158201444", "verify-me"));

    [Theory]
    [InlineData("subscribe", "wrong", "1158201444")]
    [InlineData("subscribe", "", "1158201444")]
    [InlineData("subscribe", null, "1158201444")]
    [InlineData("unsubscribe", "verify-me", "1158201444")]
    [InlineData(null, "verify-me", "1158201444")]
    [InlineData("subscribe", "verify-me", "")]
    [InlineData("subscribe", "verify-me", null)]
    public void Verification_Otherwise_IsRefused(string? mode, string? token, string? challenge) =>
        Assert.Null(WhatsAppWebhookVerification.Answer(mode, token, challenge, "verify-me"));

    // ---- what a call carries ------------------------------------------------------------------------

    // The body of one change, wrapped the way Meta wraps it.
    private static string Call(string value) =>
        """{"object":"whatsapp_business_account","entry":[{"id":"WABA","changes":[{"field":"messages","value":{""" + value + """}}]}]}""";

    [Fact]
    public void Parse_ReadsDeliveryStatuses()
    {
        var events = WhatsAppWebhookPayload.Parse(Call(
            """
            "messaging_product":"whatsapp","metadata":{"phone_number_id":"1"},
            "statuses":[
              {"id":"wamid.A","status":"sent","timestamp":"1","recipient_id":"919876543210"},
              {"id":"wamid.A","status":"delivered","timestamp":"2","recipient_id":"919876543210"},
              {"id":"wamid.A","status":"read","timestamp":"3","recipient_id":"919876543210"}]
            """));

        Assert.Equal(
            ["sent", "delivered", "read"],
            events.OfType<WhatsAppDeliveryStatus>().Select(s => s.Status));
        Assert.All(events.OfType<WhatsAppDeliveryStatus>(), s =>
        {
            Assert.Equal("wamid.A", s.MessageId);
            Assert.Equal("919876543210", s.Recipient);
            Assert.Null(s.ErrorCode);
        });
    }

    [Fact]
    public void Parse_ReadsAFailureAndMetasReason()
    {
        var events = WhatsAppWebhookPayload.Parse(Call(
            """
            "statuses":[{"id":"wamid.B","status":"failed","recipient_id":"919876543210",
              "errors":[{"code":131026,"title":"Message undeliverable","message":"Message undeliverable","error_data":{"details":"x"}}]}]
            """));

        var failed = Assert.IsType<WhatsAppDeliveryStatus>(Assert.Single(events));
        Assert.Equal("failed", failed.Status);
        Assert.Equal(131026, failed.ErrorCode);
        Assert.Equal("Message undeliverable", failed.ErrorTitle);
    }

    [Fact]
    public void Parse_ReadsAnInboundMessageButNotItsContent()
    {
        var events = WhatsAppWebhookPayload.Parse(Call(
            """
            "messages":[{"from":"919876543210","id":"wamid.C","timestamp":"1","type":"text","text":{"body":"STOP, my number is private"}}]
            """));

        var inbound = Assert.IsType<WhatsAppInboundMessage>(Assert.Single(events));
        Assert.Equal("919876543210", inbound.From);
        Assert.Equal("wamid.C", inbound.MessageId);
        Assert.Equal("text", inbound.Type);
        Assert.DoesNotContain("private", inbound.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_ReadsEveryEntryAndChange()
    {
        const string json = """
            {"entry":[
              {"changes":[{"value":{"statuses":[{"id":"wamid.1","status":"sent"}]}},{"value":{"statuses":[{"id":"wamid.2","status":"sent"}]}}]},
              {"changes":[{"value":{"messages":[{"from":"1","id":"wamid.3","type":"image"}]}}]}]}
            """;

        Assert.Equal(["wamid.1", "wamid.2", "wamid.3"], WhatsAppWebhookPayload.Parse(json).Select(e => e switch
        {
            WhatsAppDeliveryStatus s => s.MessageId,
            WhatsAppInboundMessage m => m.MessageId,
            _ => string.Empty,
        }));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("""{"entry":"nope"}""")]
    [InlineData("""{"entry":[{"changes":[{"field":"message_template_status_update","value":{"event":"APPROVED"}}]}]}""")]
    [InlineData("""{"entry":[{"changes":[{"value":{"statuses":[{"status":"sent"},{"id":"wamid.X"}]}}]}]}""")]
    [InlineData("""{"entry":[{"changes":[{"value":{"messages":[{"id":"wamid.X"}]}}]}]}""")]
    public void Parse_SkipsWhatItDoesNotUnderstand(string json) =>
        Assert.Empty(WhatsAppWebhookPayload.Parse(json));

    [Fact]
    public void Parse_OfSomethingThatIsNotJson_Throws() =>
        Assert.ThrowsAny<JsonException>(() => WhatsAppWebhookPayload.Parse("not json"));
}
