namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>Whether the exam page takes webcam photos in an attempt, and how often (FR-24).</summary>
/// <param name="Enabled">Whether photos are taken: the exam asks for them, the candidate is an adult and the attempt is open.</param>
/// <param name="IntervalSeconds">Seconds between photos; 0 when <paramref name="Enabled"/> is false.</param>
public sealed record WebcamPolicyDto(bool Enabled, int IntervalSeconds);
