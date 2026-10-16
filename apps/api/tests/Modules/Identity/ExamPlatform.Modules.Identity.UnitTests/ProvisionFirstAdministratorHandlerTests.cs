using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Commands;
using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>
/// The first administrator of a deployed environment (ADR 0005): created from an address alone, with a password nobody
/// sees, only while no Super Admin exists, and set up through the ordinary password-reset link.
/// </summary>
public class ProvisionFirstAdministratorHandlerTests
{
    private const string Email = "owner@school.example";

    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly Role _superAdmin = Role.Create(RbacCatalog.RoleNames.SuperAdmin, requiresTwoFactor: true);
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRoleRepository _roles = Substitute.For<IRoleRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly IPasswordResetTokenRepository _tokens = Substitute.For<IPasswordResetTokenRepository>();
    private readonly IOtpSender _sender = Substitute.For<IOtpSender>();
    private readonly IAuditLogger _audit = Substitute.For<IAuditLogger>();
    private readonly IIdentityUnitOfWork _unitOfWork = Substitute.For<IIdentityUnitOfWork>();
    private readonly List<string> _hashedPasswords = [];
    private User? _added;

    public ProvisionFirstAdministratorHandlerTests()
    {
        _roles.GetByNameAsync(RbacCatalog.RoleNames.SuperAdmin, Arg.Any<CancellationToken>()).Returns(_superAdmin);
        _hasher.Hash(Arg.Any<string>()).Returns(call =>
        {
            _hashedPasswords.Add(call.Arg<string>());
            return "hash-of-" + call.Arg<string>();
        });
        _users.AddAsync(Arg.Do<User>(user => _added = user), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        // Once the account is saved the reset flow looks it up by address, as it does for any account.
        _users.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(_ => _added);
    }

    private ProvisionFirstAdministratorHandler NewHandler()
    {
        var clock = new FakeClock(Now);
        var reset = new RequestPasswordResetHandler(_users, _tokens, _sender, _unitOfWork, clock);
        return new ProvisionFirstAdministratorHandler(_users, _roles, _hasher, reset, _audit, _unitOfWork, clock);
    }

    private Task<Guid> ProvisionAsync(string? email = Email) =>
        NewHandler().HandleAsync(new ProvisionFirstAdministratorCommand(email), CancellationToken.None);

    [Fact]
    public async Task HandleAsync_CreatesAnActiveSuperAdminForTheAddress()
    {
        var id = await ProvisionAsync();

        Assert.NotNull(_added);
        Assert.Equal(id, _added.Id);
        Assert.Equal(Email, _added.Email);
        Assert.Equal(UserStatus.Active, _added.Status);
        Assert.Equal([RbacCatalog.RoleNames.SuperAdmin], _added.Roles.Select(r => r.Name));
        Assert.Equal(ProvisionFirstAdministratorHandler.PlaceholderDateOfBirth, _added.DateOfBirth);
        Assert.Equal(ProvisionFirstAdministratorHandler.DisplayName, _added.DisplayName);
    }

    [Fact]
    public async Task HandleAsync_TrimsTheAddress_AndStoresItAsGiven()
    {
        // Sign-in matches an address exactly, so the case the operator typed is the case that must be used to sign in.
        await ProvisionAsync("  Owner@School.example ");

        Assert.Equal("Owner@School.example", _added!.Email);
    }

    [Fact]
    public async Task HandleAsync_GivesTheAccountAPasswordNobodyKnows_ThatDiffersEveryTime()
    {
        await ProvisionAsync();
        var first = Assert.Single(_hashedPasswords);
        Assert.Equal("hash-of-" + first, _added!.PasswordHash);
        Assert.True(first.Length >= 48, "The unknown password is long enough that it cannot be guessed.");

        _added = null;
        await ProvisionAsync();

        Assert.NotEqual(first, _hashedPasswords[1]);
    }

    [Fact]
    public async Task HandleAsync_EmailsTheNormalPasswordSetupLink_ToTheAddress()
    {
        await ProvisionAsync();

        await _tokens.Received(1).AddAsync(
            Arg.Is<PasswordResetToken>(t => t.UserId == _added!.Id && t.IsUsable(Now)), Arg.Any<CancellationToken>());
        await _sender.Received(1).SendAsync(OtpChannel.Email, Email, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_NeverSendsOrLogsThePassword()
    {
        await ProvisionAsync();

        var password = Assert.Single(_hashedPasswords);
        // What the address receives is the reset code, never the password the account was created with.
        var sent = _sender.ReceivedCalls().SelectMany(c => c.GetArguments()).OfType<string>();
        Assert.DoesNotContain(sent, value => value.Contains(password, StringComparison.Ordinal));
        var audited = _audit.ReceivedCalls().SelectMany(c => c.GetArguments()).OfType<AuditEntry>();
        Assert.DoesNotContain(audited, entry => entry.ToString()!.Contains(password, StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleAsync_RecordsOneAuditEntry_NamingTheAccountByIdAndNeverByAddress()
    {
        var id = await ProvisionAsync();

        var entry = Assert.Single(_audit.ReceivedCalls().SelectMany(c => c.GetArguments()).OfType<AuditEntry>());
        Assert.Equal("Identity.FirstAdministratorProvisioned", entry.Action);
        Assert.Equal("User", entry.EntityType);
        Assert.Equal(id.ToString(), entry.EntityId);
        Assert.Null(entry.ActorUserId);
        Assert.DoesNotContain(Email, entry.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(entry.Metadata);
    }

    [Fact]
    public async Task HandleAsync_SavesTheAccountBeforeRecordingItAndBeforeSendingTheLink()
    {
        var steps = new List<string>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ => { steps.Add("save"); return Task.FromResult(1); });
        _audit.RecordAsync(Arg.Any<AuditEntry>(), Arg.Any<CancellationToken>()).Returns(_ => { steps.Add("audit"); return Task.CompletedTask; });
        _sender.SendAsync(Arg.Any<OtpChannel>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { steps.Add("mail"); return Task.CompletedTask; });

        await ProvisionAsync();

        // The audit entry and the e-mail must never describe an account that was not stored. (The reset flow saves its own
        // link after sending, which is why "save" appears again at the end.)
        Assert.Equal(["save", "audit", "mail", "save"], steps);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("Owner <owner@school.example>")]
    [InlineData("two@school.example, three@school.example")]
    public async Task HandleAsync_WithoutOnePlainAddress_IsRefused_AndChangesNothing(string? email)
    {
        await Assert.ThrowsAsync<InvalidAdministratorEmailError>(() => ProvisionAsync(email));

        AssertNothingChanged();
    }

    [Fact]
    public async Task HandleAsync_WithAnAddressLongerThanTheColumn_IsRefused()
    {
        var tooLong = new string('a', User.MaxEmailLength) + "@school.example";

        await Assert.ThrowsAsync<InvalidAdministratorEmailError>(() => ProvisionAsync(tooLong));

        AssertNothingChanged();
    }

    [Fact]
    public async Task HandleAsync_BeforeTheRolesAreSeeded_IsRefused_AndChangesNothing()
    {
        _roles.GetByNameAsync(RbacCatalog.RoleNames.SuperAdmin, Arg.Any<CancellationToken>()).Returns((Role?)null);

        await Assert.ThrowsAsync<RoleNotFoundError>(() => ProvisionAsync());

        AssertNothingChanged();
    }

    [Fact]
    public async Task HandleAsync_WhenASuperAdminAlreadyExists_IsRefused_AndChangesNothing()
    {
        // Even for a different address: the job gives a deployment its first administrator, never another.
        _users.AnyInRoleAsync(RbacCatalog.RoleNames.SuperAdmin, Arg.Any<CancellationToken>()).Returns(true);

        await Assert.ThrowsAsync<FirstAdministratorAlreadyExistsError>(() => ProvisionAsync());

        AssertNothingChanged();
    }

    [Fact]
    public async Task HandleAsync_WhenAnotherAccountUsesTheAddress_LeavesItAsItIs_AndIsRefused()
    {
        var candidate = User.Register(Email, null, new DateOnly(2000, 1, 1), "Candidate", Now.AddDays(-5));
        _users.GetByEmailAsync(Email, Arg.Any<CancellationToken>()).Returns(candidate);

        await Assert.ThrowsAsync<DuplicateAccountError>(() => ProvisionAsync());

        // Not promoted, not given a password, not e-mailed.
        Assert.Empty(candidate.Roles);
        Assert.Null(candidate.PasswordHash);
        AssertNothingChanged();
    }

    private void AssertNothingChanged()
    {
        Assert.Null(_added);
        Assert.Empty(_unitOfWork.ReceivedCalls());
        Assert.Empty(_audit.ReceivedCalls());
        Assert.Empty(_sender.ReceivedCalls());
        Assert.Empty(_tokens.ReceivedCalls());
        Assert.Empty(_hashedPasswords);
    }
}
