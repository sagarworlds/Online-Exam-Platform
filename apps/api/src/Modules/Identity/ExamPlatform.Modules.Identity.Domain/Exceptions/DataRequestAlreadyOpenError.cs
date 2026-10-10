using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain.Exceptions;

/// <summary>
/// The account already has an open request of this kind. Two open requests of one kind would be answered twice, so the second is refused
/// until the first is answered.
/// </summary>
public sealed class DataRequestAlreadyOpenError() : DomainException(
    "You already have an open request of this kind. It will be answered before you can send another.")
{
    /// <inheritdoc />
    public override string ErrorCode => "data_request_already_open";

    /// <inheritdoc />
    public override int HttpStatusCode => 409;
}
