using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Endpoints.OtpDelivery;
using ExamPlatform.Modules.Identity.Infrastructure;
using ExamPlatform.SharedKernel.Infrastructure.WhatsApp;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.IntegrationTests;

/// <summary>
/// Proves the Identity module's own <see cref="IOtpSender"/> registration, with <c>Identity:OtpDelivery:PhoneProvider</c> set
/// to <c>WhatsApp</c>, sends a code for a phone number as a WhatsApp template message with the settings from the
/// <c>WhatsApp</c> section. <see cref="ApiFactory"/> swaps in <see cref="CapturingOtpSender"/> for every other test, so without
/// this the real registration would never run.
/// </summary>
public sealed class WhatsAppOtpDeliveryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed class RecordingWhatsAppSender : IWhatsAppSender
    {
        public List<WhatsAppTemplateMessage> Sent { get; } = [];

        public Task<WhatsAppSendResult> SendTemplateAsync(WhatsAppTemplateMessage message, CancellationToken cancellationToken)
        {
            Sent.Add(message);
            return Task.FromResult(new WhatsAppSendResult(true, "wamid.test"));
        }
    }

    private WebApplicationFactory<Program> HostWith(RecordingWhatsAppSender whatsApp, string? phoneProvider) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Identity:OtpDelivery:PhoneProvider"] = phoneProvider,
                ["WhatsApp:Enabled"] = "true",
                ["WhatsApp:AccessToken"] = "integration-test-token",
                ["WhatsApp:PhoneNumberId"] = "1234567890",
                ["WhatsApp:OtpTemplateName"] = "exam_login_code",
                ["WhatsApp:OtpTemplateLanguage"] = "hi",
            }));
            builder.ConfigureTestServices(services => services.AddSingleton<IWhatsAppSender>(whatsApp));
        });

    [Fact]
    public async Task PhoneProviderWhatsApp_SendsACodeForAPhoneAsAWhatsAppTemplate()
    {
        var whatsApp = new RecordingWhatsAppSender();
        await using var host = HostWith(whatsApp, OtpDeliveryOptions.WhatsApp);
        using var scope = host.Services.CreateScope();

        // The module's registration is the one ApiFactory later overrides, so look for it among all registrations.
        var sender = scope.ServiceProvider.GetServices<IOtpSender>().OfType<ChannelRoutingOtpSender>().Single();
        await sender.SendAsync(OtpChannel.Sms, "98765 43210", "123456", CancellationToken.None);

        var message = Assert.Single(whatsApp.Sent);
        Assert.Equal("919876543210", message.To);
        Assert.Equal("exam_login_code", message.TemplateName);
        Assert.Equal("hi", message.LanguageCode);
        Assert.Equal(["123456"], message.BodyParameters);
        Assert.Equal("123456", message.UrlButtonParameter);
    }

    [Fact]
    public async Task PhoneProviderWhatsApp_LeavesACodeForAnEmailAddressToTheEmailProvider()
    {
        var whatsApp = new RecordingWhatsAppSender();
        await using var host = HostWith(whatsApp, OtpDeliveryOptions.WhatsApp);
        using var scope = host.Services.CreateScope();

        var sender = scope.ServiceProvider.GetServices<IOtpSender>().OfType<ChannelRoutingOtpSender>().Single();
        await sender.SendAsync(OtpChannel.Email, "amy@example.com", "123456", CancellationToken.None);

        // Development's provider only logs the code, so what matters is that WhatsApp was not asked to send it.
        Assert.Empty(whatsApp.Sent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task WithoutAPhoneProvider_TheModulesAdapterTakesEveryChannelAsBefore(string? phoneProvider)
    {
        var whatsApp = new RecordingWhatsAppSender();
        await using var host = HostWith(whatsApp, phoneProvider);
        using var scope = host.Services.CreateScope();

        var senders = scope.ServiceProvider.GetServices<IOtpSender>().ToList();

        Assert.DoesNotContain(senders, s => s is ChannelRoutingOtpSender);
        Assert.Contains(senders, s => s is LoggingOtpSender);
    }
}
