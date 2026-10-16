using ExamPlatform.SharedKernel.Application;
using Microsoft.Extensions.DependencyInjection;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>
/// How operator commands are found (ADR 0005, ADR 0006): by a flag registered apart from the command, so the Host builds only the
/// command it was asked to run. Building every command to read its flag once crashed a job that lacked a setting some other command
/// needs.
/// </summary>
public class OperatorCommandRegistrationTests
{
    private sealed class QuietCommand : IOperatorCommand
    {
        public Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    // Stands for a command whose dependencies this job does not have: building it fails.
    private sealed class UnbuildableCommand : IOperatorCommand
    {
        public UnbuildableCommand() => throw new InvalidOperationException("A command that was not asked for must not be built.");

        public Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken) => Task.FromResult(0);
    }

    [Fact]
    public void ACommandIsFoundByItsFlag_WithoutBuildingAnyOtherCommand()
    {
        var services = new ServiceCollection()
            .AddOperatorCommand<UnbuildableCommand>("--needs-mail")
            .AddOperatorCommand<QuietCommand>("--quiet");
        using var provider = services.BuildServiceProvider();

        var registration = provider.GetServices<OperatorCommandRegistration>().Single(r => r.Flag == "--quiet");
        using var scope = provider.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService(registration.CommandType);

        Assert.IsType<QuietCommand>(command);
    }

    [Fact]
    public void EveryRegistrationNamesItsFlagAndItsCommandType()
    {
        var services = new ServiceCollection()
            .AddOperatorCommand<UnbuildableCommand>("--needs-mail")
            .AddOperatorCommand<QuietCommand>("--quiet");
        using var provider = services.BuildServiceProvider();

        var registrations = provider.GetServices<OperatorCommandRegistration>().ToDictionary(r => r.Flag, r => r.CommandType);

        Assert.Equal(typeof(UnbuildableCommand), registrations["--needs-mail"]);
        Assert.Equal(typeof(QuietCommand), registrations["--quiet"]);
    }

    [Fact]
    public void ACommandIsBuiltPerScope_NotOnceForTheProcess()
    {
        var services = new ServiceCollection().AddOperatorCommand<QuietCommand>("--quiet");
        using var provider = services.BuildServiceProvider();

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.NotSame(first.ServiceProvider.GetRequiredService<QuietCommand>(), second.ServiceProvider.GetRequiredService<QuietCommand>());
    }

    [Fact]
    public void TwoCommandsCannotShareAFlag()
    {
        var services = new ServiceCollection().AddOperatorCommand<QuietCommand>("--same");

        var error = Assert.Throws<ArgumentException>(() => services.AddOperatorCommand<UnbuildableCommand>("--same"));

        Assert.Contains("--same", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("create")]
    [InlineData("-x")]
    public void AFlagMustBeginWithTwoDashes(string flag)
    {
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddOperatorCommand<QuietCommand>(flag));
    }
}
