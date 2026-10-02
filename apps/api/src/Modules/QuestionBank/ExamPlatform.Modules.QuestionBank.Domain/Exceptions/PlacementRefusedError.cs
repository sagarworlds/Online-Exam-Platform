using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

/// <summary>Something that depends on a question's place in the bank objects to it being filed under a different chapter.</summary>
/// <param name="message">Which question and why, safe to show to its author.</param>
public sealed class PlacementRefusedError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "placement_refused";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
