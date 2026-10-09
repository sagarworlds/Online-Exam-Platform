namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>
/// The alternate formats an accommodation can give a candidate (FR-49), as the short codes stored and sent over the API. Each is a way the
/// exam page itself is delivered differently; a format that is not here cannot be set, so what staff pick is always something the page
/// does. To add one: add its code here and make the page act on it.
/// </summary>
public static class AccommodationFormat
{
    /// <summary>The exam page starts at its largest text size.</summary>
    public const string LargeText = "large_text";

    /// <summary>The exam page starts in high contrast.</summary>
    public const string HighContrast = "high_contrast";

    /// <summary>
    /// The candidate sits the exam with a screen reader. A screen reader and the page-leaving limit conflict (assistive technology moves
    /// focus), so this format lifts the limit for the candidate; see the accommodation policy.
    /// </summary>
    public const string ScreenReader = "screen_reader";

    /// <summary>Every format that can be set, in the order they are offered.</summary>
    public static IReadOnlyList<string> All { get; } = [LargeText, HighContrast, ScreenReader];
}
