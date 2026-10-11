using ExamPlatform.Modules.Identity.Application;
using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.Identity.UnitTests;

/// <summary>The minors question other modules ask Identity: an attempt is judged by the candidate's age on the day it started (section 7).</summary>
public class CandidateAgeDirectoryTests
{
    private static readonly DateTime Start = new(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly CandidateAgeDirectory _directory;

    public CandidateAgeDirectoryTests()
    {
        _directory = new CandidateAgeDirectory(_users);
    }

    private void DatesOfBirth(params (Guid Id, DateOnly Dob)[] users) =>
        _users.ListDatesOfBirthAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyDictionary<Guid, DateOnly>>(users.ToDictionary(u => u.Id, u => u.Dob)));

    [Fact]
    public async Task AnAttemptSatByAMinor_IsReported()
    {
        var candidate = Guid.NewGuid();
        var attempt = new AttemptStart(Guid.NewGuid(), candidate, Start);
        DatesOfBirth((candidate, new DateOnly(2010, 1, 1)));

        var minors = await _directory.FindAttemptsSatAsMinorAsync([attempt], CancellationToken.None);

        Assert.Equal([attempt.AttemptId], minors);
    }

    [Fact]
    public async Task AnAttemptSatOnTheEighteenthBirthday_IsNotReported()
    {
        // Adult from the start of the birthday: the attempt started on the day the candidate turned eighteen.
        var candidate = Guid.NewGuid();
        var attempt = new AttemptStart(Guid.NewGuid(), candidate, Start);
        DatesOfBirth((candidate, new DateOnly(2008, 10, 10)));

        var minors = await _directory.FindAttemptsSatAsMinorAsync([attempt], CancellationToken.None);

        Assert.Empty(minors);
    }

    [Fact]
    public async Task AnAttemptTheDayBeforeTheEighteenthBirthday_IsReported()
    {
        var candidate = Guid.NewGuid();
        var attempt = new AttemptStart(Guid.NewGuid(), candidate, Start);
        DatesOfBirth((candidate, new DateOnly(2008, 10, 11)));

        var minors = await _directory.FindAttemptsSatAsMinorAsync([attempt], CancellationToken.None);

        Assert.Equal([attempt.AttemptId], minors);
    }

    [Fact]
    public async Task ACandidateWithNoDateOfBirthOnRecord_IsTreatedAsAMinor()
    {
        // The minors rule fails safe: a candidate the platform cannot show to be an adult is held to the minors rule.
        var attempt = new AttemptStart(Guid.NewGuid(), Guid.NewGuid(), Start);
        DatesOfBirth();

        var minors = await _directory.FindAttemptsSatAsMinorAsync([attempt], CancellationToken.None);

        Assert.Equal([attempt.AttemptId], minors);
    }

    [Fact]
    public async Task EachAttempt_IsJudgedByItsOwnStartDate()
    {
        // The same candidate turned eighteen between two attempts: the earlier is a minor's, the later is an adult's.
        var candidate = Guid.NewGuid();
        var earlier = new AttemptStart(Guid.NewGuid(), candidate, new DateTime(2026, 10, 9, 9, 0, 0, DateTimeKind.Utc));
        var later = new AttemptStart(Guid.NewGuid(), candidate, new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
        DatesOfBirth((candidate, new DateOnly(2008, 10, 10)));

        var minors = await _directory.FindAttemptsSatAsMinorAsync([earlier, later], CancellationToken.None);

        Assert.Equal([earlier.AttemptId], minors);
    }

    [Fact]
    public async Task NoAttempts_ReadsNothing()
    {
        var minors = await _directory.FindAttemptsSatAsMinorAsync([], CancellationToken.None);

        Assert.Empty(minors);
        await _users.DidNotReceive().ListDatesOfBirthAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManyAttemptsOfOneCandidate_ReadTheirDateOfBirthOnce()
    {
        var candidate = Guid.NewGuid();
        var attempts = Enumerable.Range(0, 3).Select(_ => new AttemptStart(Guid.NewGuid(), candidate, Start)).ToList();
        DatesOfBirth((candidate, new DateOnly(2000, 1, 1)));

        await _directory.FindAttemptsSatAsMinorAsync(attempts, CancellationToken.None);

        await _users.Received(1).ListDatesOfBirthAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(candidate)),
            Arg.Any<CancellationToken>());
    }
}
