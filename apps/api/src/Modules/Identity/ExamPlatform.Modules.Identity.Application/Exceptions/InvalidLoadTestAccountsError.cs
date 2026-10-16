using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Application.Exceptions;

/// <summary>The settings given for creating the load test's candidate pool cannot be used (ADR 0006). The message says which.</summary>
/// <param name="message">What is wrong, safe to show to the operator.</param>
public sealed class InvalidLoadTestAccountsError(string message) : DomainException(message)
{
    /// <inheritdoc />
    public override string ErrorCode => "invalid_load_test_accounts";

    /// <inheritdoc />
    public override int HttpStatusCode => 400;
}
