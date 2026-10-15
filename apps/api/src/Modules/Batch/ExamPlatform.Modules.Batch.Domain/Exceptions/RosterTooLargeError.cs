using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Batch.Domain.Exceptions;

/// <summary>A roster file has more rows than one import takes (400). It is refused whole, never run partway.</summary>
/// <param name="maxRows">The most rows one import takes.</param>
public sealed class RosterTooLargeError(int maxRows)
    : DomainException($"A roster import takes at most {maxRows} rows. Split the file and import it in parts.")
{
    /// <inheritdoc />
    public override string ErrorCode => "roster_too_large";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
