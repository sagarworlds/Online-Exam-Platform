using ExamPlatform.Modules.Proctoring.Domain.Exceptions;

namespace ExamPlatform.Modules.Proctoring.Application.Queries;

/// <summary>Reads the review queue's filter from the text a caller sends, so the route does not guess at spelling or case.</summary>
public static class RiskFlagFilterText
{
    /// <summary>Reads a filter name, ignoring case; an absent one means the open queue.</summary>
    /// <param name="text">The name sent, such as "open", "reviewed", "dismissed" or "all".</param>
    /// <returns>The filter.</returns>
    /// <exception cref="InvalidRiskFilterError">The name is not one of the four filters.</exception>
    public static RiskFlagFilter Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return RiskFlagFilter.Open;

        return Enum.TryParse<RiskFlagFilter>(text.Trim(), ignoreCase: true, out var filter) && Enum.IsDefined(filter)
            ? filter
            : throw new InvalidRiskFilterError(text);
    }
}
