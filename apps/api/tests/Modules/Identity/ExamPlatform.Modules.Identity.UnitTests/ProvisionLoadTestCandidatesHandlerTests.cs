using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.SharedKernel.Application.LoadTest;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>
/// The load test's candidate pool (ADR 0006): accounts made the way a verified candidate is, sharing one password and its hash, on
/// addresses that cannot belong to anyone, created once and never changed on a second run.
/// </summary>
public class ProvisionLoadTestCandidatesHandlerTests
{
    private const string Password = "a long shared load test passphrase 2026";

    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly Role _candidateRole = Role.Create(RegisterCandidateHandler.CandidateRoleName, requiresTwoFactor: false);
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IPasswordPolicy _policy = Substitute.For<IPasswordPolicy>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly List<User> _added = [];
    private HashSet<string> _existing = [];

    public ProvisionLoadTestCandidatesHandlerTests()
    {
        _roles.GetByNameAsync(RegisterCandidateHandler.CandidateRoleName, Arg.Any<CancellationToken>()).Returns(_candidateRole);
        _hasher.Hash(Password).Returns("the-one-hash");
        _users.AddAsync(Arg.Do<User>(_added.Add), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _users.ListExistingEmailsAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlySet<string>)call.Arg<IReadOnlyCollection<string>>().Where(_existing.Contains).ToHashSet());
    }

    private static LoadTestAccountPattern Pool(int count, string pattern = "candidate-{n}@load.invalid")
    {
        Assert.True(LoadTestAccountPattern.TryCreate(pattern, count, out var problem, out var result), problem);
        return result;
    }

    private Task<ProvisionLoadTestCandidatesResult> RunAsync(int count, string? password = Password) =>
        new ProvisionLoadTestCandidatesHandler(_users, _roles, _hasher, _policy, _audit, _unitOfWork, new FakeClock(Now))
            .HandleAsync(new ProvisionLoadTestCandidatesCommand(Pool(count), password), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_CreatesAnActiveCandidateForEveryAddress_ThatSignsInWithThePassword()
    {
        var result = await RunAsync(3);

        Assert.Equal(new ProvisionLoadTestCandidatesResult(3, 0), result);
        Assert.Equal(["candidate-1@load.invalid", "candidate-2@load.invalid", "candidate-3@load.invalid"], _added.Select(u => u.Email));
        Assert.All(_added, user =>
        {
            Assert.Equal(UserStatus.Active, user.Status);
            Assert.Equal([RegisterCandidateHandler.CandidateRoleName], user.Roles.Select(r => r.Name));
            Assert.Equal("the-one-hash", user.PasswordHash);
            Assert.Equal(ProvisionLoadTestCandidatesHandler.AdultDateOfBirth, user.DateOfBirth);
        });
    }

    [Fact]
    public async Task HandleAsync_HashesThePasswordOnce_ForAllTheAccounts()
    {
        await RunAsync(50);

        _hasher.Received(1).Hash(Arg.Any<string>());
    }

    [Fact]
    public async Task HandleAsync_RaisesNoRegistrationEvents_ForSyntheticAccounts()
    {
        await RunAsync(3);

        Assert.All(_added, user => Assert.Empty(user.DomainEvents));
    }

    [Fact]
    public async Task HandleAsync_LeavesAnAccountThatExists_AsItIs_AndCreatesOnlyTheRest()
    {
        _existing = ["candidate-2@load.invalid"];

        var result = await RunAsync(3);

        Assert.Equal(new ProvisionLoadTestCandidatesResult(2, 1), result);
        Assert.Equal(["candidate-1@load.invalid", "candidate-3@load.invalid"], _added.Select(u => u.Email));
    }

    [Fact]
    public async Task HandleAsync_OnASecondRun_CreatesNothing_AndRecordsNothing()
    {
        _existing = Pool(3).Addresses().ToHashSet();

        var result = await RunAsync(3);

        Assert.Equal(new ProvisionLoadTestCandidatesResult(0, 3), result);
        Assert.Empty(_added);
        Assert.Empty(_audit.ReceivedCalls());
    }

    [Fact]
    public async Task HandleAsync_StoresInBatches_SoOneSaveIsNeverThousandsOfRows()
    {
        var size = ProvisionLoadTestCandidatesHandler.BatchSize;

        await RunAsync(size * 2 + 1);

        await _unitOfWork.Received(3).SaveChangesAsync(Arg.Any<CancellationToken>());
        Assert.Equal(size * 2 + 1, _added.Count);
    }

    [Fact]
    public async Task HandleAsync_RecordsTheRunOnce_WithCountsAndNoAddresses()
    {
        _existing = ["candidate-1@load.invalid"];

        await RunAsync(4);

        var entry = Assert.Single(_audit.ReceivedCalls().SelectMany(c => c.GetArguments()).OfType<AuditEntry>());
        Assert.Equal("Identity.LoadTestCandidatesCreated", entry.Action);
        Assert.Equal("3", entry.Metadata["created"]);
        Assert.Equal("1", entry.Metadata["alreadyExisted"]);
        Assert.DoesNotContain("load.invalid", string.Join(' ', entry.Metadata.Values));
        Assert.DoesNotContain(Password, entry.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithoutAPassword_IsRefused_AndCreatesNothing(string? password)
    {
        await Assert.ThrowsAsync<InvalidLoadTestAccountsError>(() => RunAsync(3, password));

        Assert.Empty(_added);
        Assert.Empty(_unitOfWork.ReceivedCalls());
    }

    [Fact]
    public async Task HandleAsync_WithAPasswordThePolicyRefuses_CreatesNothing()
    {
        _policy.When(p => p.EnsureAcceptable(Password, Arg.Any<string?>())).Throw(WeakPasswordError.TooShort(12));

        await Assert.ThrowsAsync<WeakPasswordError>(() => RunAsync(3));

        Assert.Empty(_added);
        Assert.Empty(_unitOfWork.ReceivedCalls());
    }

    [Fact]
    public async Task HandleAsync_BeforeTheRolesAreSeeded_IsRefused_AndCreatesNothing()
    {
        _roles.GetByNameAsync(RegisterCandidateHandler.CandidateRoleName, Arg.Any<CancellationToken>()).Returns((Role?)null);

        await Assert.ThrowsAsync<RoleNotFoundError>(() => RunAsync(3));

        Assert.Empty(_added);
    }
}
