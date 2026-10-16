using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Exceptions;

/// <summary>A roster file has no rows to import, or cannot be read as a roster at all (400). The message says why.</summary>
/// <param name="message">What is wrong with the file, safe to show to whoever sent it.</param>
public sealed class RosterUnreadableError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "roster_unreadable";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
