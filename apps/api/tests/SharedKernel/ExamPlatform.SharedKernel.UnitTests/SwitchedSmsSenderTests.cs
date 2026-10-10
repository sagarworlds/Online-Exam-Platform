using ExamPlatform.SharedKernel.Infrastructure.Sms;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.SharedKernel.UnitTests;

public class SwitchedSmsSenderTests
{
    private static readonly SmsMessage Message = new("+15550100", "Your code is 123456");

    private static SwitchedSmsSender SenderFor(ISmsProvider provider, bool? enabled) =>
        new(provider, Options.Create(new SmsOptions { Enabled = enabled }), NullLogger<SwitchedSmsSender>.Instance);

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public async Task SendAsync_WhenTheSwitchIsBlankOrFalse_SendsNothingAndNeverCallsTheProvider(bool? enabled)
    {
        var provider = Substitute.For<ISmsProvider>();

        var sent = await SenderFor(provider, enabled).SendAsync(Message, CancellationToken.None);

        Assert.False(sent);
        await provider.DidNotReceive().SendAsync(Arg.Any<SmsMessage>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WhenTheSwitchIsOn_HandsTheMessageToTheProvider()
    {
        var provider = Substitute.For<ISmsProvider>();
        provider.SendAsync(Message, Arg.Any<CancellationToken>()).Returns(true);

        var sent = await SenderFor(provider, enabled: true).SendAsync(Message, CancellationToken.None);

        Assert.True(sent);
        await provider.Received(1).SendAsync(Message, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SendAsync_WhenTheProviderRefuses_ReportsNotSent()
    {
        var provider = Substitute.For<ISmsProvider>();
        provider.SendAsync(Arg.Any<SmsMessage>(), Arg.Any<CancellationToken>()).Returns(false);

        var sent = await SenderFor(provider, enabled: true).SendAsync(Message, CancellationToken.None);

        Assert.False(sent);
    }

    [Fact]
    public async Task UnconfiguredProvider_SendsNothingAndReportsNotSent()
    {
        var sent = await new UnconfiguredSmsProvider(NullLogger<UnconfiguredSmsProvider>.Instance)
            .SendAsync(Message, CancellationToken.None);

        Assert.False(sent);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void SmsOptions_IsEnabled_IsTrueOnlyForAnExplicitTrue(bool? enabled, bool expected)
    {
        Assert.Equal(expected, new SmsOptions { Enabled = enabled }.IsEnabled);
    }
}
