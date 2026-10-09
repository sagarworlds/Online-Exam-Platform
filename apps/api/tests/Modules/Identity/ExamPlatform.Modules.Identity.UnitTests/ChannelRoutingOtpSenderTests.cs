using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Infrastructure;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

public class ChannelRoutingOtpSenderTests
{
    private readonly IOtpSender _email = Substitute.For<IOtpSender>();
    private readonly IOtpSender _phone = Substitute.For<IOtpSender>();

    [Fact]
    public async Task SendAsync_ByEmail_UsesTheEmailAdapterOnly()
    {
        await new ChannelRoutingOtpSender(_email, _phone).SendAsync(OtpChannel.Email, "amy@example.com", "123456", CancellationToken.None);

        await _email.Received(1).SendAsync(OtpChannel.Email, "amy@example.com", "123456", Arg.Any<CancellationToken>());
        await _phone.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task SendAsync_ToAPhone_UsesThePhoneAdapterOnly()
    {
        await new ChannelRoutingOtpSender(_email, _phone).SendAsync(OtpChannel.Sms, "9876543210", "123456", CancellationToken.None);

        await _phone.Received(1).SendAsync(OtpChannel.Sms, "9876543210", "123456", Arg.Any<CancellationToken>());
        await _email.DidNotReceiveWithAnyArgs().SendAsync(default, default!, default!, default);
    }
}
