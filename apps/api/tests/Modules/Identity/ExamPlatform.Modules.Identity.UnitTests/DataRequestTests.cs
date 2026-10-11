using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.DataRequests;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain.DataRequests;
using ExamPlatform.Modules.Identity.Domain.Exceptions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>Data-principal requests (FR-48): how a request is received, answered and judged late.</summary>
public class DataRequestTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ServicePeriod = TimeSpan.FromDays(30);
    private readonly Guid _candidate = Guid.NewGuid();
    private readonly Guid _staff = Guid.NewGuid();

    [Fact]
    public void Raise_TrimsTheDetails_AndDuesTheAnswerAfterTheServicePeriod()
    {
        var request = DataRequest.Raise(_candidate, DataRequestKind.Access, "  Send me a copy.  ", Now, ServicePeriod);

        Assert.Equal("Send me a copy.", request.Details);
        Assert.Equal(DataRequestStatus.Received, request.Status);
        Assert.Equal(Now.AddDays(30), request.DueAtUtc);
        Assert.True(request.IsOpen);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Raise_BlankDetails_BecomeNone(string? details)
    {
        var request = DataRequest.Raise(_candidate, DataRequestKind.Erasure, details, Now, ServicePeriod);

        Assert.Null(request.Details);
    }

    [Fact]
    public void Raise_DetailsOverTheLimit_AreRefused()
    {
        var tooLong = new string('x', DataRequest.MaxDetailsLength + 1);

        Assert.Throws<InvalidDataRequestError>(() => DataRequest.Raise(_candidate, DataRequestKind.Correction, tooLong, Now, ServicePeriod));
    }

    [Fact]
    public void Resolve_Completed_KeepsWhoAnsweredAndWhen()
    {
        var request = DataRequest.Raise(_candidate, DataRequestKind.Access, null, Now, ServicePeriod);

        request.Resolve(_staff, DataRequestStatus.Completed, "Copy sent by e-mail.", Now.AddDays(3));

        Assert.Equal(DataRequestStatus.Completed, request.Status);
        Assert.Equal(_staff, request.ResolvedByUserId);
        Assert.Equal(Now.AddDays(3), request.ResolvedAtUtc);
        Assert.Equal("Copy sent by e-mail.", request.ResolutionNote);
        Assert.False(request.IsOpen);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Resolve_Rejected_WithoutANote_IsRefused(string? note)
    {
        var request = DataRequest.Raise(_candidate, DataRequestKind.Erasure, null, Now, ServicePeriod);

        Assert.Throws<InvalidDataRequestError>(() => request.Resolve(_staff, DataRequestStatus.Rejected, note, Now));
        Assert.True(request.IsOpen);
    }

    [Fact]
    public void Resolve_AnAnswerTwice_IsRefused()
    {
        var request = DataRequest.Raise(_candidate, DataRequestKind.Access, null, Now, ServicePeriod);
        request.Resolve(_staff, DataRequestStatus.Completed, null, Now);

        Assert.Throws<DataRequestNotOpenError>(() => request.Resolve(_staff, DataRequestStatus.Completed, null, Now));
    }

    [Fact]
    public void Resolve_WithAnOutcomeThatIsNotAnAnswer_IsRefused()
    {
        var request = DataRequest.Raise(_candidate, DataRequestKind.Access, null, Now, ServicePeriod);

        Assert.Throws<InvalidDataRequestError>(() => request.Resolve(_staff, DataRequestStatus.Received, "note", Now));
    }

    [Fact]
    public void IsOverdue_OnlyWhileOpenAndPastTheDueTime()
    {
        var request = DataRequest.Raise(_candidate, DataRequestKind.Access, null, Now, ServicePeriod);

        Assert.False(request.IsOverdue(Now.AddDays(30)));
        Assert.True(request.IsOverdue(Now.AddDays(30).AddMinutes(1)));

        request.Resolve(_staff, DataRequestStatus.Completed, null, Now.AddDays(31));
        Assert.False(request.IsOverdue(Now.AddDays(60)));
    }
}

/// <summary>The two handlers around a data request: a second open request is refused, and an unknown request is not found.</summary>
public class DataRequestHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private readonly IDataRequestRepository _requests = Substitute.For<IDataRequestRepository>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly FakeClock _clock = new(Now);
    private readonly IOptions<DataRequestOptions> _options = Options.Create(new DataRequestOptions { ServiceDays = 30 });

    [Fact]
    public async Task Raise_RefusesASecondOpenRequestOfTheSameKind()
    {
        var candidate = Guid.NewGuid();
        _requests.HasOpenAsync(candidate, DataRequestKind.Access, Arg.Any<CancellationToken>()).Returns(true);
        var handler = new RaiseDataRequestHandler(_requests, _unitOfWork, _clock, _options);

        await Assert.ThrowsAsync<DataRequestAlreadyOpenError>(() =>
            handler.HandleAsync(candidate, DataRequestKind.Access, null, CancellationToken.None));

        await _requests.DidNotReceive().AddAsync(Arg.Any<DataRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Raise_StoresTheRequestAndSaves()
    {
        var candidate = Guid.NewGuid();
        var handler = new RaiseDataRequestHandler(_requests, _unitOfWork, _clock, _options);

        var dto = await handler.HandleAsync(candidate, DataRequestKind.Correction, "My name is misspelt.", CancellationToken.None);

        Assert.Equal(candidate, dto.UserId);
        Assert.Equal(DataRequestKind.Correction, dto.Kind);
        Assert.Equal(Now.AddDays(30), dto.DueAtUtc);
        await _requests.Received(1).AddAsync(Arg.Any<DataRequest>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Resolve_AnUnknownRequest_IsNotFound()
    {
        _requests.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((DataRequest?)null);
        var handler = new ResolveDataRequestHandler(_requests, _unitOfWork, _clock);

        await Assert.ThrowsAsync<DataRequestNotFoundError>(() =>
            handler.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), DataRequestStatus.Completed, null, CancellationToken.None));
    }
}
