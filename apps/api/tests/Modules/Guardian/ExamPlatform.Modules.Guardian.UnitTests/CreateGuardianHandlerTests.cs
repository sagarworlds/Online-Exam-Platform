using ExamPlatform.Modules.Guardian.Application;
using ExamPlatform.Modules.Guardian.Application.Commands;
using ExamPlatform.Modules.Guardian.Application.Ports;
using ExamPlatform.Modules.Guardian.Domain;
using NSubstitute;
using GuardianAggregate = ExamPlatform.Modules.Guardian.Domain.Guardian;

namespace ExamPlatform.Modules.Guardian.UnitTests;

public class CreateGuardianHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithValidCommand_CreatesGuardianAndReturnsDto()
    {
        var repository = Substitute.For<IGuardianRepository>();
        var unitOfWork = Substitute.For<IGuardianUnitOfWork>();
        var handler = new CreateGuardianHandler(repository, unitOfWork);

        var email = "guardian@example.com";
        var fullName = "John Guardian";
        var phone = "+91-9876543210";
        var command = new CreateGuardianCommand(email, fullName, phone);

        var result = await handler.Handle(command, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.Id);
        Assert.Equal(email, result.Email);
        Assert.Equal(fullName, result.FullName);
        Assert.Equal(phone, result.Phone);

        repository.Received(1).Add(Arg.Is<GuardianAggregate>(g =>
            g.Email == email &&
            g.FullName == fullName &&
            g.Phone == phone));
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithInvalidEmail_ThrowsException()
    {
        var handler = new CreateGuardianHandler(
            Substitute.For<IGuardianRepository>(),
            Substitute.For<IGuardianUnitOfWork>());

        var command = new CreateGuardianCommand("invalid-email", "John Guardian", null);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(command, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_WithEmptyFullName_ThrowsException(string fullName)
    {
        var handler = new CreateGuardianHandler(
            Substitute.For<IGuardianRepository>(),
            Substitute.For<IGuardianUnitOfWork>());

        var command = new CreateGuardianCommand("guardian@example.com", fullName, null);

        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(command, CancellationToken.None));
    }
}
