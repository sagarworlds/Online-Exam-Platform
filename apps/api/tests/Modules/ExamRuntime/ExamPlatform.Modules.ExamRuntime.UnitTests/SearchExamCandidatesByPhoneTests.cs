using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Queries;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.Identity.Contracts;
using ExamPlatform.Modules.Invite.Contracts;
using NSubstitute;

namespace ExamPlatform.Modules.ExamRuntime.UnitTests;

/// <summary>Finding an exam's candidates by phone number from the staff page: only ids come back, and only for candidates of this exam.</summary>
public class SearchExamCandidatesByPhoneTests
{
    private readonly IExamCatalog _catalog = Substitute.For<IExamCatalog>();
    private readonly IExamRoster _roster = Substitute.For<IExamRoster>();
    private readonly IAccountPhoneSearch _phones = Substitute.For<IAccountPhoneSearch>();
    private readonly ExamSnapshot _exam = Fixtures.Exam([Fixtures.Question("First")]);
    private readonly Guid _ravi = Guid.NewGuid();
    private readonly Guid _meena = Guid.NewGuid();
    private readonly SearchExamCandidatesByPhoneHandler _handler;

    public SearchExamCandidatesByPhoneTests()
    {
        _catalog.FindAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns(_exam);
        _roster.GetEnrolledCandidatesAsync(_exam.Id, Arg.Any<CancellationToken>())
            .Returns([new EnrolledCandidate(_meena, "meena@example.com"), new EnrolledCandidate(_ravi, "ravi@example.com")]);
        _handler = new SearchExamCandidatesByPhoneHandler(_catalog, _roster, _phones);
    }

    private void Matching(params Guid[] ids) =>
        _phones.FindAccountsByPhoneAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<Guid>>(ids.ToHashSet()));

    [Fact]
    public async Task TheMatchingCandidates_AreReturnedByIdInTheOrderOfTheCandidateList()
    {
        Matching(_ravi, _meena);

        var result = await _handler.HandleAsync(_exam.Id, "98765", CancellationToken.None);

        Assert.Equal([_meena, _ravi], result.CandidateIds);
    }

    [Fact]
    public async Task OnlyThisExamsCandidates_AreLookedThrough_AndOnlyTheyCanBeReturned()
    {
        var stranger = Guid.NewGuid();
        Matching(_ravi, stranger); // an account the exam does not have, if Identity ever answered with one

        var result = await _handler.HandleAsync(_exam.Id, "98765", CancellationToken.None);

        Assert.Equal([_ravi], result.CandidateIds);
        await _phones.Received(1).FindAccountsByPhoneAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(_ravi) && ids.Contains(_meena)),
            "98765",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoMatch_IsAnEmptyList()
    {
        Matching();

        var result = await _handler.HandleAsync(_exam.Id, "98765", CancellationToken.None);

        Assert.Empty(result.CandidateIds);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ABlankSearch_AsksNothing(string? phone)
    {
        var result = await _handler.HandleAsync(_exam.Id, phone, CancellationToken.None);

        Assert.Empty(result.CandidateIds);
        await _phones.DidNotReceive().FindAccountsByPhoneAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExamWithNoEnrolledCandidates_AsksNothing()
    {
        _roster.GetEnrolledCandidatesAsync(_exam.Id, Arg.Any<CancellationToken>()).Returns([]);

        var result = await _handler.HandleAsync(_exam.Id, "98765", CancellationToken.None);

        Assert.Empty(result.CandidateIds);
        await _phones.DidNotReceive().FindAccountsByPhoneAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnExamThatDoesNotExist_IsNotFound()
    {
        await Assert.ThrowsAsync<ExamNotFoundError>(() => _handler.HandleAsync(Guid.NewGuid(), "98765", CancellationToken.None));
    }
}
