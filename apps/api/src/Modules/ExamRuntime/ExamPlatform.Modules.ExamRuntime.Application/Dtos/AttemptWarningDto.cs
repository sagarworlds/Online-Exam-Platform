namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>A warning an administrator sent the candidate during the attempt (FR-29).</summary>
/// <param name="Id">The warning's id, so the page shows each one once.</param>
/// <param name="Message">What the administrator said.</param>
/// <param name="IssuedAtUtc">When it was sent.</param>
public sealed record AttemptWarningDto(Guid Id, string Message, DateTime IssuedAtUtc);

/// <summary>
/// What the exam page asks for every few seconds while an attempt is open (FR-29): whether it is still open or paused, the deadline
/// as it stands now, and every warning so far. Small on purpose, so asking often costs little.
/// </summary>
/// <param name="Status">Whether the attempt is still open; anything else tells the page to load the result.</param>
/// <param name="PausedAtUtc">When an administrator paused it, or null while it runs.</param>
/// <param name="DeadlineUtc">When the server will close the attempt; later than before after a pause is resumed.</param>
/// <param name="ServerTimeUtc">The server's clock, so the countdown ignores the candidate's own.</param>
/// <param name="Warnings">Every warning sent during the attempt, oldest first.</param>
public sealed record AttemptStatusDto(
    ExamPlatform.Modules.ExamRuntime.Domain.AttemptStatus Status,
    DateTime? PausedAtUtc,
    DateTime DeadlineUtc,
    DateTime ServerTimeUtc,
    IReadOnlyList<AttemptWarningDto> Warnings);
