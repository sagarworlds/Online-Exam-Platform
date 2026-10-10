using ExamPlatform.Modules.Proctoring.Domain.Exceptions;
using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.Proctoring.Endpoints;

/// <summary>
/// Checks the risk score's configuration when the host starts, so a mistyped weight stops the application starting instead of flagging
/// nobody (or everybody) in production. The rules themselves live in <see cref="ExamPlatform.Modules.Proctoring.Domain.RiskPolicy"/>.
/// </summary>
public sealed class RiskScoringOptionsValidator : IValidateOptions<RiskScoringOptions>
{
    /// <summary>Checks the options by building the policy from them.</summary>
    /// <param name="name">The options instance name; unused, as there is only one.</param>
    /// <param name="options">The bound options.</param>
    /// <returns>Success, or a failure naming the value that is wrong.</returns>
    public ValidateOptionsResult Validate(string? name, RiskScoringOptions options)
    {
        try
        {
            options.ToPolicy();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidRiskPolicyError error)
        {
            return ValidateOptionsResult.Fail($"{RiskScoringOptions.SectionName}: {error.Message}");
        }
    }
}
