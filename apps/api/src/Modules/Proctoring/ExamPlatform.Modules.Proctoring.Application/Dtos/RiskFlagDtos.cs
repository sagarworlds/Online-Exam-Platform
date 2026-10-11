namespace ExamPlatform.Modules.Proctoring.Application.Dtos;

/// <summary>One page of the review queue for an exam.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="ExamName">The exam's name, for the heading.</param>
/// <param name="Filter">Which decisions the page shows.</param>
/// <param name="Page">The page, from 1.</param>
/// <param name="PageSize">How many rows a page holds.</param>
/// <param name="Total">How many flags match the filter across every page.</param>
/// <param name="Items">The flags on this page, highest score first.</param>
/// <param name="MinorsScanEnabled">Whether attempts by candidates under 18 may be scored in this environment (section 7.2).</param>
/// <param name="ExcludedUnder18Attempts">How many finished attempts were left out of scoring because the candidate was under 18. Always zero when minors may be scanned.</param>
public sealed record RiskFlagQueueDto(
    Guid ExamId,
    string ExamName,
    string Filter,
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<RiskFlagDto> Items,
    bool MinorsScanEnabled,
    int ExcludedUnder18Attempts);

/// <summary>One flagged attempt in the review queue, with every signal that produced its score.</summary>
/// <param name="Id">The assessment's id, used to decide on it.</param>
/// <param name="AttemptId">The attempt.</param>
/// <param name="CandidateId">The candidate who sat it.</param>
/// <param name="CandidateEmail">The address the candidate was invited at; null when the roster no longer lists them.</param>
/// <param name="AttemptNumber">Which attempt it was for the candidate.</param>
/// <param name="Score">The points scored.</param>
/// <param name="MaxScore">The most points available.</param>
/// <param name="Status">Open, Reviewed or Dismissed.</param>
/// <param name="ComputedAtUtc">When the score was last worked out.</param>
/// <param name="DecidedAtUtc">When a reviewer decided; null while open.</param>
/// <param name="DecisionNote">The reviewer's note, if any.</param>
/// <param name="Signals">Every signal, raised or not, with its value, threshold, weight and points.</param>
public sealed record RiskFlagDto(
    Guid Id,
    Guid AttemptId,
    Guid CandidateId,
    string? CandidateEmail,
    int AttemptNumber,
    int Score,
    int MaxScore,
    string Status,
    DateTime ComputedAtUtc,
    DateTime? DecidedAtUtc,
    string? DecisionNote,
    IReadOnlyList<RiskSignalDto> Signals);

/// <summary>One signal of a flag, as it was judged.</summary>
/// <param name="Kind">The signal's name.</param>
/// <param name="Value">The value read; null when it could not be judged.</param>
/// <param name="Threshold">The threshold it was judged against.</param>
/// <param name="RaisedWhen">Whether a value at or above, or at or below, the threshold raises it.</param>
/// <param name="Weight">The points it adds when raised.</param>
/// <param name="Raised">Whether it was raised.</param>
/// <param name="Points">The points it added.</param>
public sealed record RiskSignalDto(
    string Kind,
    decimal? Value,
    decimal Threshold,
    string RaisedWhen,
    int Weight,
    bool Raised,
    int Points);

/// <summary>What a scan of an exam did.</summary>
/// <param name="ExamId">The exam scanned.</param>
/// <param name="Scored">How many finished attempts were scored, new or rescored.</param>
/// <param name="Flagged">How many of those scored attempts reached the flag threshold.</param>
/// <param name="KeptDecided">How many attempts were left as they were, because a reviewer had already decided on them.</param>
/// <param name="ExcludedUnder18">How many finished attempts were not scored because the candidate was under 18. Zero when minors may be scanned.</param>
public sealed record RiskScanResultDto(Guid ExamId, int Scored, int Flagged, int KeptDecided, int ExcludedUnder18);
