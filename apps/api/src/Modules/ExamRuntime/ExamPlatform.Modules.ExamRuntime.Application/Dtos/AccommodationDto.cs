namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>A candidate's accommodation at an exam, as staff see it (FR-49).</summary>
/// <param name="ExtraTimeMinutes">Minutes added to the candidate's deadline; 0 for none.</param>
/// <param name="ReaderScribe">Whether the candidate may use a reader or scribe.</param>
/// <param name="AlternateFormats">The alternate formats of the exam page they get: "large_text", "high_contrast", "screen_reader".</param>
/// <param name="Notes">The note staff wrote. Sensitive: only staff routes carry it.</param>
/// <param name="UpdatedAtUtc">When it was last set.</param>
public sealed record AccommodationDto(
    int ExtraTimeMinutes, bool ReaderScribe, IReadOnlyList<string> AlternateFormats, string? Notes, DateTime UpdatedAtUtc);

/// <summary>What a candidate is told about their own accommodation (FR-49): never the staff note.</summary>
/// <param name="ExtraTimeSeconds">Seconds added to their deadline; 0 for none.</param>
/// <param name="ReaderScribe">Whether they may use a reader or scribe.</param>
/// <param name="AlternateFormats">The alternate formats of the exam page they get.</param>
public sealed record CandidateAccommodationDto(int ExtraTimeSeconds, bool ReaderScribe, IReadOnlyList<string> AlternateFormats);
