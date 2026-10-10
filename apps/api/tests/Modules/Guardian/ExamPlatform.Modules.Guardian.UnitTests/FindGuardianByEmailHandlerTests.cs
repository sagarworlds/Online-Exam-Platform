using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Application.Queries;
using ExamPlatform.Modules.Guardian.Domain.Exceptions;
using NSubstitute;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.UnitTests;

public class FindGuardianByEmailHandlerTests
{
    private readonly IGuardianRepository repository = Substitute.For<IGuardianRepository>();

    private FindGuardianByEmailHandler Handler => new(repository);

    [Fact]
    public async Task Find_OneMatch_ReturnsThatGuardian()
    {
        var guardian = new GuardianAggregate("guardian@example.com", "Gia Guardian");
        IReadOnlyList<GuardianAggregate> matches = [guardian];
        repository.ListByEmailAsync("guardian@example.com", Arg.Any<CancellationToken>()).Returns(matches);

        var found = await Handler.HandleAsync("guardian@example.com", CancellationToken.None);

        Assert.Equal(guardian.Id, found.Id);
        Assert.Equal("Gia Guardian", found.FullName);
    }

    [Fact]
    public async Task Find_NoMatch_ThrowsGuardianNotFoundByEmailError()
    {
        IReadOnlyList<GuardianAggregate> none = [];
        repository.ListByEmailAsync("nobody@example.com", Arg.Any<CancellationToken>()).Returns(none);

        await Assert.ThrowsAsync<GuardianNotFoundByEmailError>(() =>
            Handler.HandleAsync("nobody@example.com", CancellationToken.None));
    }

    [Fact]
    public async Task Find_SeveralMatches_ThrowsGuardianEmailAmbiguousError()
    {
        IReadOnlyList<GuardianAggregate> duplicates =
        [
            new GuardianAggregate("dup@example.com", "First Record"),
            new GuardianAggregate("dup@example.com", "Second Record"),
        ];
        repository.ListByEmailAsync("dup@example.com", Arg.Any<CancellationToken>()).Returns(duplicates);

        await Assert.ThrowsAsync<GuardianEmailAmbiguousError>(() =>
            Handler.HandleAsync("dup@example.com", CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Find_BlankAddress_IsRefusedBeforeAnyLookup(string? email)
    {
        await Assert.ThrowsAsync<InvalidGuardianDetailsError>(() => Handler.HandleAsync(email, CancellationToken.None));

        await repository.DidNotReceive().ListByEmailAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
