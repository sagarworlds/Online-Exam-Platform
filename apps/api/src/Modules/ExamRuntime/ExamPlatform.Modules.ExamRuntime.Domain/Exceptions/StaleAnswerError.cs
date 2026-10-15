using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;

/// <summary>
/// A save or a clear of an answer carried a sequence number that is not higher than the one the server already holds for the question, so
/// a newer write has been recorded and this one is not applied (NFR-4, ADR 0004). A late copy of an old request is harmless for that
/// reason. A client that did mean the write reads <see cref="HeldSequence"/> from the response and sends it again above that number.
/// </summary>
/// <param name="heldSequence">The highest sequence number the server holds for the question.</param>
public sealed class StaleAnswerError(long heldSequence) : DomainException("A newer save of this answer has already been recorded.")
{
    /// <summary>The highest sequence number the server holds for the question.</summary>
    public long HeldSequence { get; } = heldSequence;

    /// <inheritdoc />
    public override string ErrorCode => "answer_superseded";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object?> Extensions { get; } =
        new Dictionary<string, object?> { ["currentSequence"] = heldSequence };
}
