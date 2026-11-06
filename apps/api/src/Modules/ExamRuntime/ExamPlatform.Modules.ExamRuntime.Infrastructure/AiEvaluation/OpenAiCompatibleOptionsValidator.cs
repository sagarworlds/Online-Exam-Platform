using Microsoft.Extensions.Options;

namespace ExamPlatform.Modules.ExamRuntime.Infrastructure.AiEvaluation;

/// <summary>
/// Stops the host starting with <c>OpenAiCompatible</c> settings that cannot work, naming the one that is wrong, instead of failing the first
/// candidate whose answer is judged. A key is not required: without one the provider is simply off.
/// </summary>
public sealed class OpenAiCompatibleOptionsValidator : IValidateOptions<OpenAiCompatibleOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpenAiCompatibleOptions options) =>
        options.Problem is { } problem ? ValidateOptionsResult.Fail(problem) : ValidateOptionsResult.Success;
}
