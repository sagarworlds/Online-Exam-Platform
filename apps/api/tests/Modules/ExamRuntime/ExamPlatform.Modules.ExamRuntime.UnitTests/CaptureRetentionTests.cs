using System.ComponentModel.DataAnnotations;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Application.Retention;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Webcam photos and screen pictures are deleted after the retention period (FR-47).</summary>
public class CaptureRetentionTests
{
    private readonly IWebcamPhotoRepository _photos = Substitute.For<IWebcamPhotoRepository>();
    private readonly FakeClock _clock = new(Fixtures.Now);

    private PurgeOldCapturesHandler Handler(int retentionDays) =>
        new(_photos, _clock, Options.Create(new CaptureRetentionOptions { RetentionDays = retentionDays }));

    [Fact]
    public async Task CapturesOlderThanThePeriodAreDeleted_AndTheCountIsReturned()
    {
        _photos.DeleteCapturedBeforeAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(7);

        var removed = await Handler(30).HandleAsync(CancellationToken.None);

        Assert.Equal(7, removed);
        await _photos.Received(1).DeleteCapturedBeforeAsync(Fixtures.Now.AddDays(-30), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ThePeriodComesFromTheOptions()
    {
        await Handler(90).HandleAsync(CancellationToken.None);

        await _photos.Received(1).DeleteCapturedBeforeAsync(Fixtures.Now.AddDays(-90), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void ThePeriodDefaultsToThirtyDays_AndTheSweepIsOn()
    {
        var options = new CaptureRetentionOptions();

        Assert.True(options.Enabled);
        Assert.Equal(30, options.RetentionDays);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(3651)]
    public void APeriodOutsideTheAllowedRangeIsRejected(int days)
    {
        var options = new CaptureRetentionOptions { RetentionDays = days };

        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true));
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(CaptureRetentionOptions.RetentionDays)));
    }
}
