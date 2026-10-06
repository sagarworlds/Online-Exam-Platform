namespace ExamPlatform.Modules.ExamRuntime.Application.Dtos;

/// <summary>What the exam page is told after it reports that the candidate left it (FR-22).</summary>
/// <param name="Violations">How many times the candidate has left the page so far in this attempt.</param>
/// <param name="Limit">How many times they may; 0 when the exam does not watch for it, in which case nothing was recorded.</param>
/// <param name="AttemptEnded">Whether this report reached the limit, so the server has ended the attempt and scored what was saved.</param>
public sealed record FocusViolationResultDto(int Violations, int Limit, bool AttemptEnded);
