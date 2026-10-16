namespace ExamPlatform.SharedKernel.Application;

/// <summary>
/// A one-off command an operator runs through the Host's command line, as a deployment job, in the same way as
/// <c>--migrate-and-seed</c> (ADR 0005). The Host runs the command registered for a flag that is among its arguments, against the
/// database, and exits before the web host starts. A module registers its commands in its own installer with
/// <see cref="OperatorCommandServiceCollectionExtensions.AddOperatorCommand{TCommand}"/>, so adding one never changes the Host
/// (Open/Closed Principle, ADR 0001).
/// </summary>
/// <remarks>
/// Registered as scoped, and only the command that was asked for is built: the Host finds it by its flag and resolves nothing else,
/// so a job is never refused for lacking a setting that a different command needs. A command may take any scoped service in its
/// constructor. It must be safe to run twice, and must never write a secret or a personal value to the log or to its output.
/// </remarks>
public interface IOperatorCommand
{
    /// <summary>Runs the command.</summary>
    /// <param name="args">The Host's command-line arguments, including the command's flag and anything after it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The process exit code: <c>0</c> when the command did what was asked, <c>2</c> when it refused (bad arguments, or a
    /// precondition that does not hold) and changed nothing. An unexpected failure is thrown, so the process fails loudly.
    /// </returns>
    Task<int> RunAsync(IReadOnlyList<string> args, CancellationToken cancellationToken);
}
