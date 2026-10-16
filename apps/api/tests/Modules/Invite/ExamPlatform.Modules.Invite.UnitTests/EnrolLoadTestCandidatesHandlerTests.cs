using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Application;
using ExamPlatform.Modules.Invite.Application.Commands;
using ExamPlatform.Modules.Invite.Application.Ports;
using ExamPlatform.Modules.Invite.Domain;
using ExamPlatform.Modules.Invite.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application.LoadTest;
using NSubstitute;
using InviteAggregate = ExamPlatform.Modules.Invite.Domain.Invite;

namespace ExamPlatform.Modules.Invite.UnitTests;

/// <summary>
/// Enrolling the load test's candidate pool in an exam (ADR 0006): each account gets an invite created and accepted through the
/// invite's own methods, so enrolment is exactly what a real accept leaves behind.
/// </summary>
public class EnrolLoadTestCandidatesHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);
    private static readonly Guid ExamId = Guid.NewGuid();

    private readonly IInviteRepository _repository = Substitute.For<IInviteRepository>();
    private readonly IInviteUnitOfWork _unitOfWork = Substitute.For<IInviteUnitOfWork>();
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IContactDirectory _contacts = Substitute.For<IContactDirectory>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly Dictionary<string, Guid> _accounts = [];
    private readonly HashSet<Guid> _alreadyEnrolled = [];
    private readonly List<InviteAggregate> _added = [];

    public EnrolLoadTestCandidatesHandlerTests()
    {
        _catalog.FindAsync(ExamId, Arg.Any<CancellationToken>()).Returns(new ExamSnapshot(
            ExamId, "Load test exam", null, false, Now, Now.AddHours(3), null, null, 1, 0, 0, []));
        _contacts.FindActiveAccountIdByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => _accounts.TryGetValue(call.Arg<string>(), out var id) ? id : (Guid?)null);
        _repository.HasAcceptedAsync(Arg.Any<Guid>(), ExamId, Arg.Any<CancellationToken>())
            .Returns(call => _alreadyEnrolled.Contains(call.ArgAt<Guid>(0)));
        _repository.When(r => r.Add(Arg.Any<InviteAggregate>())).Do(call => _added.Add(call.Arg<InviteAggregate>()));
    }

    private static LoadTestAccountPattern Pool(int count)
    {
        Assert.True(LoadTestAccountPattern.TryCreate("candidate-{n}@load.invalid", count, out var problem, out var result), problem);
        return result;
    }

    private void GivenAccounts(int count)
    {
        foreach (var email in Pool(count).Addresses())
        {
            _accounts[email] = Guid.NewGuid();
        }
    }

    private Task<EnrolLoadTestPoolResult> RunAsync(int count, Guid? examId = null) =>
        new EnrolLoadTestCandidatesHandler(_repository, _unitOfWork, _catalog, _contacts, _audit, new FakeClock(Now))
            .HandleAsync(new EnrolLoadTestPoolCommand(examId ?? ExamId, Pool(count)), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_GivesEveryAccountAnAcceptedInviteToTheExam_HeldByThatAccount()
    {
        GivenAccounts(3);

        var result = await RunAsync(3);

        Assert.Equal(new EnrolLoadTestPoolResult(3, 0, 0), result);
        Assert.Equal(Pool(3).Addresses(), _added.Select(i => i.Email));
        Assert.All(_added, invite =>
        {
            Assert.Equal(ExamId, invite.ExamId);
            Assert.Equal(InviteStatus.Accepted, invite.Status);
            Assert.Equal(_accounts[invite.Email], invite.AcceptedByUserId);
            Assert.NotNull(invite.AcceptedAt);
        });
    }

    [Fact]
    public async Task HandleAsync_RaisesNoInviteEvents_SoNoAuditEntryIsWrittenPerAccount()
    {
        GivenAccounts(3);

        await RunAsync(3);

        Assert.All(_added, invite => Assert.Empty(invite.DomainEvents));
    }

    [Fact]
    public async Task HandleAsync_SkipsAnAccountAlreadyEnrolled()
    {
        GivenAccounts(3);
        _alreadyEnrolled.Add(_accounts["candidate-2@load.invalid"]);

        var result = await RunAsync(3);

        Assert.Equal(new EnrolLoadTestPoolResult(2, 1, 0), result);
        Assert.Equal(["candidate-1@load.invalid", "candidate-3@load.invalid"], _added.Select(i => i.Email));
    }

    [Fact]
    public async Task HandleAsync_CountsAnAddressWithNoAccount_AndEnrolsNobodyForIt()
    {
        GivenAccounts(3);
        _accounts.Remove("candidate-3@load.invalid");

        var result = await RunAsync(3);

        Assert.Equal(new EnrolLoadTestPoolResult(2, 0, 1), result);
        Assert.DoesNotContain(_added, invite => invite.Email == "candidate-3@load.invalid");
    }

    [Fact]
    public async Task HandleAsync_OnASecondRun_EnrolsNobody_AndRecordsNothing()
    {
        GivenAccounts(3);
        foreach (var id in _accounts.Values) _alreadyEnrolled.Add(id);

        var result = await RunAsync(3);

        Assert.Equal(new EnrolLoadTestPoolResult(0, 3, 0), result);
        Assert.Empty(_added);
        Assert.Empty(_audit.ReceivedCalls());
    }

    [Fact]
    public async Task HandleAsync_StoresInBatches()
    {
        var size = EnrolLoadTestCandidatesHandler.BatchSize;
        GivenAccounts(size + 1);

        await RunAsync(size + 1);

        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(size + 1, _added.Count);
    }

    [Fact]
    public async Task HandleAsync_RecordsTheRunOnce_NamingTheExamAndNoAddresses()
    {
        GivenAccounts(2);

        await RunAsync(2);

        var entry = Assert.Single(_audit.ReceivedCalls().SelectMany(c => c.GetArguments()).OfType<AuditEntry>());
        Assert.Equal("Invite.LoadTestCandidatesEnrolled", entry.Action);
        Assert.Equal(ExamId.ToString(), entry.EntityId);
        Assert.Equal("2", entry.Metadata["enrolled"]);
        Assert.DoesNotContain("load.invalid", entry.ToString());
    }

    [Fact]
    public async Task HandleAsync_ForAnExamThatDoesNotExist_IsRefused_AndEnrolsNobody()
    {
        GivenAccounts(2);

        await Assert.ThrowsAsync<InviteExamNotFoundError>(() => RunAsync(2, Guid.NewGuid()));

        Assert.Empty(_added);
        Assert.Empty(_unitOfWork.ReceivedCalls());
    }
}
