using ExamPlatform.Modules.Identity.Application.Exceptions;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Application.Queries;
using ExamPlatform.Modules.Identity.Domain;
using ExamPlatform.Modules.Identity.Domain.Rbac;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>Staff browsing and opening student accounts.</summary>
public class StudentQueryHandlersTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();

    private static User Student(string email, string name = "Asha Rao")
    {
        var user = User.Register(email, null, new DateOnly(2000, 1, 1), name, Now);
        user.AssignRole(Role.Create(RbacCatalog.RoleNames.Candidate, requiresTwoFactor: false));
        return user;
    }

    [Fact]
    public async Task List_ReturnsTheStudentsTheRepositoryFound_WithoutTheirDateOfBirth()
    {
        var asha = Student("asha@example.com");
        _users.ListStudentsAsync(null, ListStudentsHandler.MaxTake, Arg.Any<CancellationToken>()).Returns([asha]);

        var students = await new ListStudentsHandler(_users).HandleAsync(null, CancellationToken.None);

        var listed = Assert.Single(students);
        Assert.Equal(asha.Id, listed.Id);
        Assert.Equal("Asha Rao", listed.DisplayName);
        Assert.Equal("asha@example.com", listed.Email);
        Assert.Equal(asha.Status.ToString(), listed.Status);
    }

    [Fact]
    public async Task List_TrimsTheSearchBeforeAskingTheRepository()
    {
        _users.ListStudentsAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns([]);

        await new ListStudentsHandler(_users).HandleAsync("  asha ", CancellationToken.None);

        await _users.Received(1).ListStudentsAsync("asha", ListStudentsHandler.MaxTake, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Get_ReturnsTheProfileWithTheDateOfBirthAndAgeBand()
    {
        var asha = Student("asha@example.com");
        _users.GetByIdAsync(asha.Id, Arg.Any<CancellationToken>()).Returns(asha);

        var student = await new GetStudentHandler(_users, new FakeClock(Now)).HandleAsync(asha.Id, CancellationToken.None);

        Assert.Equal(new DateOnly(2000, 1, 1), student.DateOfBirth);
        Assert.Equal("Adult", student.AgeBand);
    }

    [Fact]
    public async Task Get_ForAnAccountThatDoesNotExist_ThrowsUserNotFoundError()
    {
        await Assert.ThrowsAsync<UserNotFoundError>(
            () => new GetStudentHandler(_users, new FakeClock(Now)).HandleAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Get_ForAStaffAccount_ThrowsTheSameNotFoundError()
    {
        var teacher = Student("teacher@example.com");
        teacher.AssignRole(Role.Create(RbacCatalog.RoleNames.InstituteTeacher, requiresTwoFactor: false));
        _users.GetByIdAsync(teacher.Id, Arg.Any<CancellationToken>()).Returns(teacher);

        await Assert.ThrowsAsync<UserNotFoundError>(
            () => new GetStudentHandler(_users, new FakeClock(Now)).HandleAsync(teacher.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Lookup_AsksTheRepositoryOnceForTheDistinctTrimmedAddresses_AndReturnsWhatItFound()
    {
        var asha = Student("asha@example.com");
        _users.ListStudentsByEmailAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns([asha]);

        var found = await new FindStudentsByEmailHandler(_users)
            .HandleAsync([" asha@example.com ", "ASHA@example.com", "", null, "   ", "ben@example.com"], CancellationToken.None);

        Assert.Equal(asha.Id, Assert.Single(found).Id);
        await _users.Received(1).ListStudentsByEmailAsync(
            Arg.Is<IReadOnlyCollection<string>>(emails => emails.SequenceEqual(new[] { "asha@example.com", "ben@example.com" })),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Lookup_WithNoAddress_FindsNothingWithoutAskingTheRepository(string? address)
    {
        var found = await new FindStudentsByEmailHandler(_users).HandleAsync(address is null ? null : [address], CancellationToken.None);

        Assert.Empty(found);
        await _users.DidNotReceiveWithAnyArgs().ListStudentsByEmailAsync(default!, default);
    }

    [Fact]
    public async Task Lookup_OfMoreAddressesThanItTakes_Throws400_AndAsksNothing()
    {
        var tooMany = Enumerable.Range(0, FindStudentsByEmailHandler.MaxEmails + 1).Select(n => $"student{n}@example.com").ToList();

        var error = await Assert.ThrowsAsync<TooManyAddressesError>(() => new FindStudentsByEmailHandler(_users).HandleAsync(tooMany, CancellationToken.None));

        Assert.Equal(400, error.HttpStatusCode);
        Assert.Equal("too_many_addresses", error.ErrorCode);
        await _users.DidNotReceiveWithAnyArgs().ListStudentsByEmailAsync(default!, default);
    }

    [Fact]
    public async Task Lookup_CountsAnAddressOnceHoweverItIsWritten()
    {
        // The same 200 addresses in two cases are 200 addresses, not 400.
        var addresses = Enumerable.Range(0, FindStudentsByEmailHandler.MaxEmails).Select(n => $"student{n}@example.com").ToList();
        var twice = addresses.Concat(addresses.Select(a => a.ToUpperInvariant())).ToList();
        _users.ListStudentsByEmailAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns([]);

        var found = await new FindStudentsByEmailHandler(_users).HandleAsync(twice, CancellationToken.None);

        Assert.Empty(found);
    }
}
