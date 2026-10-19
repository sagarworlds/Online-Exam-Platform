namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>Whether the exam page takes webcam photos (FR-24) and screen pictures (FR-25) in an attempt, and how often.</summary>
/// <param name="Enabled">Whether photos are taken: the exam asks for them, the candidate is an adult and the attempt is open.</param>
/// <param name="IntervalSeconds">Seconds between photos; 0 when <paramref name="Enabled"/> is false.</param>
/// <param name="ScreenEnabled">Whether the page should ask the candidate to share their screen and send pictures of it (FR-25), on the same terms.</param>
/// <param name="ScreenIntervalSeconds">Seconds between screen pictures; 0 when <paramref name="ScreenEnabled"/> is false.</param>
public sealed record WebcamPolicyDto(bool Enabled, int IntervalSeconds, bool ScreenEnabled = false, int ScreenIntervalSeconds = 0);
