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

/// <summary>The staff queue names who asked, so the answer can reach them, and the candidate's own list stays their own.</summary>
public class DataRequestQueriesTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ServicePeriod = TimeSpan.FromDays(30);
    private readonly IDataRequestRepository _requests = Substitute.For<IDataRequestRepository>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly FakeClock _clock = new(Now);

    private DataRequestQueries Queries() => new(_requests, _users, _clock);

    [Fact]
    public async Task ListOpen_NamesWhoAskedForEachRequest()
    {
        var asha = Guid.NewGuid();
        var ben = Guid.NewGuid();
        _requests.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<DataRequest>
        {
            DataRequest.Raise(asha, DataRequestKind.Access, "Send me a copy.", Now, ServicePeriod),
            DataRequest.Raise(ben, DataRequestKind.Erasure, null, Now, ServicePeriod),
        });
        _users.ListContactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>()).Returns(new Dictionary<Guid, UserContact>
        {
            [asha] = new("Asha Rao", "asha@example.com", "+919800000001"),
            [ben] = new("Ben Das", null, "+919800000002"),
        });

        var open = await Queries().ListOpenAsync(CancellationToken.None);

        Assert.Equal(2, open.Count);
        Assert.Equal(asha, open[0].UserId);
        Assert.Equal("Asha Rao", open[0].RequesterName);
        Assert.Equal("asha@example.com", open[0].RequesterEmail);
        Assert.Equal("+919800000001", open[0].RequesterPhoneNumber);
        Assert.Equal("Send me a copy.", open[0].Details);
        Assert.Equal("Ben Das", open[1].RequesterName);
        Assert.Null(open[1].RequesterEmail);
        Assert.Equal("+919800000002", open[1].RequesterPhoneNumber);
        Assert.Equal(DataRequestKind.Erasure, open[1].Kind);
    }

    [Fact]
    public async Task ListOpen_ReadsEachRequesterOnce_HoweverManyRequestsTheyHaveOpen()
    {
        var asha = Guid.NewGuid();
        _requests.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<DataRequest>
        {
            DataRequest.Raise(asha, DataRequestKind.Access, null, Now, ServicePeriod),
            DataRequest.Raise(asha, DataRequestKind.Erasure, null, Now, ServicePeriod),
        });
        _users.ListContactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserContact> { [asha] = new("Asha Rao", "asha@example.com", null) });

        var open = await Queries().ListOpenAsync(CancellationToken.None);

        Assert.Equal(2, open.Count);
        Assert.All(open, request => Assert.Equal("asha@example.com", request.RequesterEmail));
        await _users.Received(1).ListContactsAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(asha)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListOpen_StillListsARequest_WhoseAccountCannotBeFound()
    {
        var gone = Guid.NewGuid();
        _requests.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<DataRequest>
        {
            DataRequest.Raise(gone, DataRequestKind.Correction, "Wrong date.", Now, ServicePeriod),
        });
        _users.ListContactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserContact>());

        var open = await Queries().ListOpenAsync(CancellationToken.None);

        var request = Assert.Single(open);
        Assert.Equal(gone, request.UserId);
        Assert.Equal("Wrong date.", request.Details);
        Assert.Null(request.RequesterName);
        Assert.Null(request.RequesterEmail);
        Assert.Null(request.RequesterPhoneNumber);
    }

    [Fact]
    public async Task ListOpen_WithNothingWaiting_ReadsNoUsers()
    {
        _requests.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<DataRequest>());

        var open = await Queries().ListOpenAsync(CancellationToken.None);

        Assert.Empty(open);
        await _users.DidNotReceive().ListContactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListOpen_MarksARequestOverdue_OnceItsDueTimeHasPassed()
    {
        var asha = Guid.NewGuid();
        _requests.ListOpenAsync(Arg.Any<CancellationToken>()).Returns(new List<DataRequest>
        {
            DataRequest.Raise(asha, DataRequestKind.Access, null, Now.AddDays(-31), ServicePeriod),
            DataRequest.Raise(asha, DataRequestKind.Erasure, null, Now.AddDays(-1), ServicePeriod),
        });
        _users.ListContactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, UserContact>());

        var open = await Queries().ListOpenAsync(CancellationToken.None);

        Assert.True(open[0].IsOverdue);
        Assert.False(open[1].IsOverdue);
    }

    [Fact]
    public async Task ListMine_ReadsNoUsers_SinceTheCandidateKnowsWhoTheyAre()
    {
        var candidate = Guid.NewGuid();
        _requests.ListForUserAsync(candidate, Arg.Any<CancellationToken>()).Returns(new List<DataRequest>
        {
            DataRequest.Raise(candidate, DataRequestKind.Access, null, Now, ServicePeriod),
        });

        var mine = await Queries().ListMineAsync(candidate, CancellationToken.None);

        Assert.Single(mine);
        await _users.DidNotReceive().ListContactsAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }
}
