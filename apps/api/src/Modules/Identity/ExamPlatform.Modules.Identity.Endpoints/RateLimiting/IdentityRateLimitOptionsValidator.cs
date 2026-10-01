using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Identity.Endpoints.RateLimiting;

/// <summary>
/// Refuses, at startup, an <see cref="IdentityRateLimitOptions"/> in which any policy has a
/// non-positive limit or window, and names the policy, so a bad value fails the boot with
/// a message that says which one to fix (NFR-5) instead of surfacing as a 500 when the
/// first request builds the limiter. Registered with <c>ValidateOnStart</c>.
/// </summary>
internal sealed class IdentityRateLimitOptionsValidator : IValidateOptions<IdentityRateLimitOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, IdentityRateLimitOptions options) =>
        options.FindInvalidPolicy() is { } policy
            ? ValidateOptionsResult.Fail(
                $"{IdentityRateLimitOptions.SectionName}:{policy} needs a positive PermitLimit and WindowSeconds.")
            : ValidateOptionsResult.Success;
}
