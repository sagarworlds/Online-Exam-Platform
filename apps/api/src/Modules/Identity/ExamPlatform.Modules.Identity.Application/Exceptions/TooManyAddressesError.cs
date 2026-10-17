using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>A lookup of accounts by e-mail address named more addresses than one lookup takes.</summary>
/// <param name="limit">The most addresses one lookup takes.</param>
public sealed class TooManyAddressesError(int limit) : DomainException($"A lookup takes at most {limit} e-mail addresses.")
{
    /// <inheritdoc />
    public override string ErrorCode => "too_many_addresses";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
