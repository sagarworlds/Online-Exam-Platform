using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>The exam's author has not yet allowed candidates to see which answers were right.</summary>
/// <param name="availableFromUtc">When the answers will be shown, if that is already decided; null when an administrator releases them by hand.</param>
public sealed class ResultsNotReleasedError(DateTime? availableFromUtc) : DomainException(MessageFor(availableFromUtc))
{
    /// <inheritdoc />
    public override string ErrorCode => "results_not_released";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;

    private static string MessageFor(DateTime? availableFromUtc) =>
        availableFromUtc is { } at
            ? $"The correct answers have not been released yet. They will be shown from {at:yyyy-MM-dd HH:mm} UTC."
            : "The correct answers have not been released yet. They will be shown when the exam's organiser releases them.";
}
