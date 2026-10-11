using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>
/// A data request, or an answer to one, is not acceptable as given: too long, missing a required note, or not one of the two answers.
/// </summary>
public sealed class InvalidDataRequestError : DomainException
{
    private InvalidDataRequestError(string message) : base(message)
    {
    }

    /// <inheritdoc />
    public override string ErrorCode => "invalid_data_request";

    /// <summary>The details are longer than the limit.</summary>
    public static InvalidDataRequestError DetailsTooLong(int maximum) =>
        new($"The details can be at most {maximum} characters.");

    /// <summary>The answer is a refusal but gives no reason.</summary>
    public static InvalidDataRequestError NoteRequiredForRefusal() =>
        new("A refusal must say why, so give a note.");

    /// <summary>The note is longer than the limit.</summary>
    public static InvalidDataRequestError NoteTooLong(int maximum) =>
        new($"The note can be at most {maximum} characters.");

    /// <summary>The outcome is neither Completed nor Rejected.</summary>
    public static InvalidDataRequestError UnknownOutcome() =>
        new("A request can only be completed or rejected.");
}
