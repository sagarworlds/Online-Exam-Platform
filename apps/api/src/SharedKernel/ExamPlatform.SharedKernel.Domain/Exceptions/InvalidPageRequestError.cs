namespace ExamPlatform.SharedKernel.Domain.Exceptions;

/// <summary>The page or page size a caller asked for is outside what a list will serve (400).</summary>
public sealed class InvalidPageRequestError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_page_request";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
