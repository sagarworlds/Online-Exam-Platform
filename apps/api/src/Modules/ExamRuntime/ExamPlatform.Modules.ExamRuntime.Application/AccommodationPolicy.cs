using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// How an accommodation changes the exam for one candidate (FR-49), in one place so the page they are told about, the attempt they sit and
/// the rules the server enforces all agree.
/// </summary>
/// <remarks>
/// The one override: a candidate who sits with a screen reader is not held to the page-leaving limit. Assistive technology moves focus
/// and a scribe or reader may need another window, so counting that as leaving the exam would punish the candidate for the very
/// accommodation they were given. Nothing else about the exam's proctoring changes for them, and the override is stated to them on the
/// instructions page rather than applied silently.
/// </remarks>
public static class AccommodationPolicy
{
    /// <summary>Whether the formats lift the page-leaving limit.</summary>
    /// <param name="formats">The alternate formats the candidate has.</param>
    public static bool LiftsFocusLimit(IEnumerable<string> formats) => formats.Contains(AccommodationFormat.ScreenReader);

    /// <summary>The exam as a candidate with these formats sits it.</summary>
    /// <param name="exam">The exam as every candidate sees it.</param>
    /// <param name="formats">The alternate formats the candidate has.</param>
    public static ExamSnapshot Apply(ExamSnapshot exam, IEnumerable<string> formats) =>
        exam.FocusViolationLimit > 0 && LiftsFocusLimit(formats) ? exam with { FocusViolationLimit = 0 } : exam;

    /// <summary>What a candidate is told about an accommodation: everything but the staff note.</summary>
    /// <param name="accommodation">The accommodation.</param>
    public static CandidateAccommodationDto ForCandidate(Accommodation accommodation) =>
        new(accommodation.ExtraTimeSeconds, accommodation.ReaderScribe, accommodation.AlternateFormats);

    /// <summary>What a candidate is told about the accommodation an attempt carries; null when it carries none.</summary>
    /// <param name="attempt">The attempt.</param>
    public static CandidateAccommodationDto? ForCandidate(Attempt attempt) =>
        attempt.IsAccommodated ? new CandidateAccommodationDto(attempt.AccommodationExtraSeconds, attempt.AccommodationReaderScribe, attempt.AccommodationFormats) : null;

    /// <summary>What staff are shown about an accommodation, the note included.</summary>
    /// <param name="accommodation">The accommodation.</param>
    public static AccommodationDto ForStaff(Accommodation accommodation) =>
        new(accommodation.ExtraTimeSeconds / 60, accommodation.ReaderScribe, accommodation.AlternateFormats, accommodation.Notes, accommodation.UpdatedAtUtc);
}
